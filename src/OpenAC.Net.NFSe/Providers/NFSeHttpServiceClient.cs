// ***********************************************************************
// Assembly         : OpenAC.Net.NFSe
// Author           : Rafael Dias
// Created          : 09-03-2022
//
// Last Modified By : Rafael Dias
// Last Modified On : 09-03-2022
// ***********************************************************************
// <copyright file="NFSeHttpServiceClient.cs" company="OpenAC .Net">
//		        		   The MIT License (MIT)
//	     		    Copyright (c) 2014 - 2021 Projeto OpenAC .Net
//
//	 Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//	 The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//	 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
// ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
// </copyright>
// <summary></summary>
// ***********************************************************************

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using OpenAC.Net.Core;
using OpenAC.Net.Core.Extensions;
using OpenAC.Net.DFe.Core;
using OpenAC.Net.DFe.Core.Common;

namespace OpenAC.Net.NFSe.Providers
{
    public abstract class NFSeHttpServiceClient : IDisposable
    {
        #region Inner Types

        public enum SoapVersion
        {
            Soap11,
            Soap12,
        }

        #endregion Inner Types

        #region Constructors

        /// <summary>
        ///
        /// </summary>
        /// <param name="provider"></param>
        /// <param name="tipoUrl"></param>
        protected NFSeHttpServiceClient(ProviderBase provider, TipoUrl tipoUrl) : this(provider, tipoUrl, provider.Certificado)
        {
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="provider"></param>
        /// <param name="tipoUrl"></param>
        /// <param name="certificado"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected NFSeHttpServiceClient(ProviderBase provider, TipoUrl tipoUrl, X509Certificate2 certificado)
        {
            Certificado = certificado;
            Url = provider.GetUrl(tipoUrl).Replace("?wsdl", "");
            Provider = provider;

            switch (tipoUrl)
            {
                case TipoUrl.Enviar:
                    PrefixoEnvio = "lot";
                    PrefixoResposta = "lot";
                    break;

                case TipoUrl.EnviarSincrono:
                    PrefixoEnvio = "lot-sinc";
                    PrefixoResposta = "lot-sinc";
                    break;

                case TipoUrl.ConsultarSituacao:
                    PrefixoEnvio = "env-sit-lot";
                    PrefixoResposta = "rec-sit-lot";
                    break;

                case TipoUrl.ConsultarLoteRps:
                    PrefixoEnvio = "con-lot";
                    PrefixoResposta = "con-lot";
                    break;

                case TipoUrl.ConsultarSequencialRps:
                    PrefixoEnvio = "seq-rps";
                    PrefixoResposta = "seq-rps";
                    break;

                case TipoUrl.ConsultarNFSeRps:
                    PrefixoEnvio = "con-rps-nfse";
                    PrefixoResposta = "con-rps-nfse";
                    break;

                case TipoUrl.ConsultarNFSe:
                    PrefixoEnvio = "con-nfse";
                    PrefixoResposta = "con-nfse";
                    break;

                case TipoUrl.CancelarNFSe:
                    PrefixoEnvio = "canc-nfse";
                    PrefixoResposta = "canc-nfse";
                    break;

                case TipoUrl.SubstituirNFSe:
                    PrefixoEnvio = "sub-nfse";
                    PrefixoResposta = "sub-nfse";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(tipoUrl), tipoUrl, null);
            }
        }

        ~NFSeHttpServiceClient() => Dispose(false);

        #endregion Constructors

        #region Properties

        public string PrefixoEnvio { get; protected set; }

        public string PrefixoResposta { get; protected set; }

        public string EnvelopeEnvio { get; protected set; }

        public string EnvelopeRetorno { get; protected set; }

        public ProviderBase Provider { get; set; }

        public bool EhHomologa��o => Provider.Configuracoes.WebServices.Ambiente == DFeTipoAmbiente.Homologacao;

        protected string Url { get; set; }

        protected X509Certificate2 Certificado { get; set; }

        protected SoapVersion MessageVersion { get; set; } = SoapVersion.Soap11;

        protected bool IsDisposed { get; private set; }

        #endregion Properties

        #region Methods

        protected virtual string Execute(string soapAction, string message, string responseTag, params string[] soapNamespaces)
        {
            return Execute(soapAction, message, string.Empty, new[] { responseTag }, soapNamespaces);
        }

        protected virtual string Execute(string soapAction, string message, string[] responseTag, params string[] soapNamespaces)
        {
            return Execute(soapAction, message, string.Empty, responseTag, soapNamespaces);
        }

        protected virtual string Execute(string soapAction, string message, string soapHeader, string responseTag, params string[] soapNamespaces)
        {
            return Execute(soapAction, message, soapHeader, new[] { responseTag }, soapNamespaces);
        }

        protected virtual string Execute(string soapAction, string message, string soapHeader, string[] responseTag, params string[] soapNamespaces)
        {
            EnvelopeEnvio = WriteSoapEnvelope(message, soapAction, soapHeader, soapNamespaces);

            RemoteCertificateValidationCallback validation = null;
            var naoValidarCertificado = !ValidarCertificadoServidor();

            if (naoValidarCertificado)
            {
                validation = ServicePointManager.ServerCertificateValidationCallback;
                ServicePointManager.ServerCertificateValidationCallback += (_, _, _, _) => true;
            }

            var protocolos = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol = Provider.Configuracoes.WebServices.Protocolos;

            try
            {
                GravarSoap(EnvelopeEnvio, $"{DateTime.Now:yyyyMMddssfff}_{PrefixoEnvio}_soap_env.xml");

                var request = WebRequest.CreateHttp(Url);
                request.Method = "POST";

                if (Provider.TimeOut.HasValue)
                    request.Timeout = Provider.TimeOut.Value.Milliseconds;

                if (Certificado != null)
                    request.ClientCertificates.Add(Certificado);

                switch (MessageVersion)
                {
                    case SoapVersion.Soap11:
                        request.ContentType = "text/xml; charset=utf-8";
                        request.Headers.Add("SOAPAction", soapAction);
                        break;

                    case SoapVersion.Soap12:
                        request.ContentType = $"application/soap+xml; charset=utf-8;action={soapAction}";
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                {
                    streamWriter.Write(EnvelopeEnvio);
                    streamWriter.Flush();
                }

                var response = request.GetResponse().GetResponseStream();
                Guard.Against<OpenDFeCommunicationException>(response == null, "Erro ao obter resposta do servidor.");

                using (var reader = new StreamReader(response))
                    EnvelopeRetorno = reader.ReadToEnd();

                GravarSoap(EnvelopeRetorno, $"{DateTime.Now:yyyyMMddssfff}_{PrefixoResposta}_soap_ret.xml");
            }
            finally
            {
                if (naoValidarCertificado)
                    ServicePointManager.ServerCertificateValidationCallback = validation;

                ServicePointManager.SecurityProtocol = protocolos;
            }

            var xmlDocument = XDocument.Parse(EnvelopeRetorno);
            var body = xmlDocument.ElementAnyNs("Envelope")?.ElementAnyNs("Body");
            var retorno = TratarRetorno(body?.Descendants().First(), responseTag);
            if (retorno.IsValidXml()) return retorno;

            throw new ApplicationException(retorno);
        }

        protected virtual bool ValidarCertificadoServidor() => true;

        protected virtual string WriteSoapEnvelope(string message, string soapAction, string soapHeader, string[] soapNamespaces)
        {
            var envelope = new StringBuilder();
            switch (MessageVersion)
            {
                case SoapVersion.Soap11:
                    envelope.Append("<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\"");
                    break;

                case SoapVersion.Soap12:
                    envelope.Append("<soapenv:Envelope xmlns:soapenv=\"http://www.w3.org/2003/05/soap-envelope\"");
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            foreach (var ns in soapNamespaces)
            {
                envelope.Append($" {ns}");
            }

            envelope.Append(">");
            envelope.Append(soapHeader.IsEmpty() ? "<soapenv:Header/>" : $"<soapenv:Header>{soapHeader}</soapenv:Header>");
            envelope.Append("<soapenv:Body>");
            envelope.Append(message);
            envelope.Append("</soapenv:Body>");
            envelope.Append("</soapenv:Envelope>");

            return envelope.ToString();
        }

        protected abstract string TratarRetorno(XElement xmlDocument, string[] responseTag);

        /// <summary>
        /// Salvar o arquivo xml no disco de acordo com as propriedades.
        /// </summary>
        /// <param name="conteudoArquivo"></param>
        /// <param name="nomeArquivo"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected virtual void GravarSoap(string conteudoArquivo, string nomeArquivo)
        {
            if (Provider.Configuracoes.WebServices.Salvar == false) return;

            var path = Provider.Configuracoes.Arquivos.GetPathSoap(DateTime.Now, Provider.Configuracoes.PrestadorPadrao.CpfCnpj);
            nomeArquivo = Path.Combine(path, nomeArquivo);
            File.WriteAllText(nomeArquivo, conteudoArquivo, Encoding.UTF8);
        }

        /// <inheritdoc />
        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            // Dispose all managed and unmanaged resources.
            Dispose(true);

            // Take this object off the finalization queue and prevent finalization code for this
            // object from executing a second time.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the managed resources implementing <see cref="IDisposable"/>.
        /// </summary>
        protected virtual void DisposeManaged()
        {
        }

        /// <summary>
        /// Disposes the unmanaged resources implementing <see cref="IDisposable"/>.
        /// </summary>
        protected virtual void DisposeUnmanaged()
        {
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources;
        /// <c>false</c> to release only unmanaged resources, called from the finalizer only.</param>
        private void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (IsDisposed)
                return;

            // If disposing managed and unmanaged resources.
            if (disposing)
            {
                DisposeManaged();
            }

            DisposeUnmanaged();

            IsDisposed = true;
        }

        #endregion Methods
    }
}