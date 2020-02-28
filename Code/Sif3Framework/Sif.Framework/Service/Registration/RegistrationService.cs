﻿/*
 * Copyright 2020 Systemic Pty Ltd
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Sif.Framework.Extensions;
using Sif.Framework.Model.Authentication;
using Sif.Framework.Model.Exceptions;
using Sif.Framework.Model.Infrastructure;
using Sif.Framework.Model.Requests;
using Sif.Framework.Model.Settings;
using Sif.Framework.Service.Authentication;
using Sif.Framework.Service.Mapper;
using Sif.Framework.Service.Serialisation;
using Sif.Framework.Service.Sessions;
using Sif.Framework.Utils;
using Sif.Specification.Infrastructure;
using System;
using System.Xml;
using Environment = Sif.Framework.Model.Infrastructure.Environment;

namespace Sif.Framework.Service.Registration
{
    /// <summary>
    /// <see cref="IRegistrationService">IRegistrationService</see>
    /// </summary>
    internal class RegistrationService : IRegistrationService
    {
        private static readonly slf4net.ILogger log = slf4net.LoggerFactory.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IAuthorisationTokenService authorisationTokenService;
        private readonly ISessionService sessionService;
        private readonly IFrameworkSettings settings;

        private string environmentUrl;
        private string sessionToken;

        protected ContentType ContentType
        {
            get
            {
                return ContentType.JSON;
            }
        }

        /// <summary>
        /// <see cref="IRegistrationService.AuthorisationToken">AuthorisationToken</see>
        /// </summary>
        public AuthorisationToken AuthorisationToken { get; private set; }

        /// <summary>
        /// <see cref="IRegistrationService.Registered">Registered</see>
        /// </summary>
        public bool Registered { get; private set; }

        /// <summary>
        /// The current environment that this RegistrationService has registered with.
        /// </summary>
        public Environment CurrentEnvironment { get; private set; }

        /// <summary>
        /// Parse the URL of the Environment infrastructure service from the XML.
        /// </summary>
        /// <param name="environmentXml">Serialised Environment object as XML.</param>
        /// <returns>URL of the Environment infrastructure service.</returns>
        private string TryParseEnvironmentUrl(string environmentXml)
        {
            string environmentUrl = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(environmentXml))
                {
                    XmlNamespaceManager xmlNamespaceManager = new XmlNamespaceManager(new NameTable());
                    xmlNamespaceManager.AddNamespace("ns", "http://www.sifassociation.org/infrastructure/3.0.1");

                    XmlDocument environmentDoc = new XmlDocument();
                    environmentDoc.LoadXml(environmentXml);

                    environmentUrl = environmentDoc.SelectSingleNode("//ns:infrastructureService[@name='environment']", xmlNamespaceManager).InnerText;
                    if (log.IsDebugEnabled) log.Debug("Parsed environment URL is " + environmentUrl + ".");
                }
            }
            catch (Exception)
            {
                // Return null if unable to parse for an environment URL.
            }

            return environmentUrl;
        }

        /// <summary>
        /// Create an instance using the appropriate settings and service.
        /// </summary>
        /// <param name="settings">Framework settings.</param>
        /// <param name="sessionService">Service used for managing sessions.</param>
        public RegistrationService(IFrameworkSettings settings, ISessionService sessionService)
        {
            this.settings = settings;
            this.sessionService = sessionService;

            if (AuthenticationMethod.Basic.ToString().Equals(settings.AuthenticationMethod, StringComparison.OrdinalIgnoreCase))
            {
                authorisationTokenService = new BasicAuthorisationTokenService();
            }
            else if (AuthenticationMethod.SIF_HMACSHA256.ToString().Equals(settings.AuthenticationMethod, StringComparison.OrdinalIgnoreCase))
            {
                authorisationTokenService = new HmacShaAuthorisationTokenService();
            }
            else
            {
                authorisationTokenService = new BasicAuthorisationTokenService();
            }

            Registered = false;
        }

        /// <summary>
        /// <see cref="IRegistrationService.Register()">Register</see>
        /// </summary>
        public Environment Register()
        {
            Environment environment = EnvironmentUtils.LoadFromSettings(SettingsManager.ProviderSettings);
            return Register(ref environment);
        }

        /// <summary>
        /// <see cref="IRegistrationService.Register(ref Environment)">Register</see>
        /// </summary>
        public Environment Register(ref Environment environment)
        {
            if (Registered)
            {
                return CurrentEnvironment;
            }

            if (sessionService.HasSession(environment.ApplicationInfo.ApplicationKey, environment.SolutionId, environment.UserToken, environment.InstanceId))
            {
                if (log.IsDebugEnabled) log.Debug("Session token already exists for this object service (Consumer/Provider).");

                string storedSessionToken = sessionService.RetrieveSessionToken(environment.ApplicationInfo.ApplicationKey, environment.SolutionId, environment.UserToken, environment.InstanceId);
                AuthorisationToken = authorisationTokenService.Generate(storedSessionToken, settings.SharedSecret);
                string storedEnvironmentUrl = sessionService.RetrieveEnvironmentUrl(environment.ApplicationInfo.ApplicationKey, environment.SolutionId, environment.UserToken, environment.InstanceId);
                string environmentBody = HttpUtils.GetRequest(
                    storedEnvironmentUrl,
                    AuthorisationToken,
                    contentTypeOverride: ContentType.ToDescription(),
                    acceptOverride: ContentType.ToDescription());

                if (log.IsDebugEnabled) log.Debug("Environment response from GET request ...");
                if (log.IsDebugEnabled) log.Debug(environmentBody);

                environmentType environmentTypeToDeserialise = SerialiserFactory.GetSerialiser<environmentType>(ContentType).Deserialise(environmentBody);
                Environment environmentResponse = MapperFactory.CreateInstance<environmentType, Environment>(environmentTypeToDeserialise);

                sessionToken = environmentResponse.SessionToken;
                environmentUrl = environmentResponse.InfrastructureServices[InfrastructureServiceNames.environment].Value;

                if (log.IsDebugEnabled) log.Debug("Environment URL is " + environmentUrl + ".");

                if (!storedSessionToken.Equals(sessionToken) || !storedEnvironmentUrl.Equals(environmentUrl))
                {
                    AuthorisationToken = authorisationTokenService.Generate(sessionToken, settings.SharedSecret);
                    sessionService.RemoveSession(storedSessionToken);
                    sessionService.StoreSession(environmentResponse.ApplicationInfo.ApplicationKey, sessionToken, environmentUrl, environmentResponse.SolutionId, environmentResponse.UserToken, environmentResponse.InstanceId);
                }

                environment = environmentResponse;
            }
            else
            {
                if (log.IsDebugEnabled) log.Debug("Session token does not exist for this object service (Consumer/Provider).");

                string environmentBody = null;

                try
                {
                    AuthorisationToken initialToken = authorisationTokenService.Generate(environment.ApplicationInfo.ApplicationKey, settings.SharedSecret);
                    environmentType environmentTypeToSerialise = MapperFactory.CreateInstance<Environment, environmentType>(environment);
                    string body = SerialiserFactory.GetSerialiser<environmentType>(ContentType).Serialise(environmentTypeToSerialise);
                    environmentBody = HttpUtils.PostRequest(
                        settings.EnvironmentUrl,
                        initialToken,
                        body,
                        contentTypeOverride: ContentType.ToDescription(),
                        acceptOverride: ContentType.ToDescription());

                    if (log.IsDebugEnabled) log.Debug("Environment response from POST request ...");
                    if (log.IsDebugEnabled) log.Debug(environmentBody);

                    environmentType environmentTypeToDeserialise = SerialiserFactory.GetSerialiser<environmentType>(ContentType).Deserialise(environmentBody);
                    Environment environmentResponse = MapperFactory.CreateInstance<environmentType, Environment>(environmentTypeToDeserialise);

                    sessionToken = environmentResponse.SessionToken;
                    environmentUrl = environmentResponse.InfrastructureServices[InfrastructureServiceNames.environment].Value;

                    if (log.IsDebugEnabled) log.Debug("Environment URL is " + environmentUrl + ".");

                    AuthorisationToken = authorisationTokenService.Generate(sessionToken, settings.SharedSecret);
                    sessionService.StoreSession(environment.ApplicationInfo.ApplicationKey, sessionToken, environmentUrl, environmentResponse.SolutionId, environmentResponse.UserToken, environmentResponse.InstanceId);
                    environment = environmentResponse;
                }
                catch (Exception e)
                {
                    if (environmentUrl != null)
                    {
                        HttpUtils.DeleteRequest(environmentUrl, AuthorisationToken);
                    }
                    else if (!string.IsNullOrWhiteSpace(TryParseEnvironmentUrl(environmentBody)))
                    {
                        HttpUtils.DeleteRequest(TryParseEnvironmentUrl(environmentBody), AuthorisationToken);
                    }

                    throw new RegistrationException("Registration failed.", e);
                }
            }

            CurrentEnvironment = environment;
            Registered = true;
            return CurrentEnvironment;
        }

        /// <summary>
        /// <see cref="IRegistrationService.Unregister(bool?)">Unregister</see>
        /// </summary>
        public void Unregister(bool? deleteOnUnregister = null)
        {
            if (Registered)
            {
                if (deleteOnUnregister ?? settings.DeleteOnUnregister)
                {
                    HttpUtils.DeleteRequest(
                        environmentUrl,
                        AuthorisationToken,
                        contentTypeOverride: ContentType.ToDescription(),
                        acceptOverride: ContentType.ToDescription());
                    sessionService.RemoveSession(sessionToken);
                }

                Registered = false;
            }
        }
    }
}