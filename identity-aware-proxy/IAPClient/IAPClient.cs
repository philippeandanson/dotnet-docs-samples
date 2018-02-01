/*
Copyright 2018 Google Inc

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

// [START generate_iap_request]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Json;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace GoogleCloudSamples
{
    class IAPClient
    {
        /// <summary>
        /// Authenticates using the client id and credentials, then fetches
        /// the uri.
        /// </summary>
        /// <param name="iapClientId">The client id observed on 
        /// https://console.cloud.google.com/apis/credentials.</param>
        /// <param name="credentialsFilePath">Path to the credentials .json file
        /// download from https://console.cloud.google.com/apis/credentials.
        /// </param>
        /// <param name="uri">HTTP uri to fetch.</param>
        /// <returns>The http response body as a string.</returns>
        public static string InvokeRequest(string iapClientId,
            string credentialsFilePath, string uri)
        {
            // Read credentials from the credentials .json file.
            Credentials credentials;
            using (var fs = new FileStream(credentialsFilePath,
                FileMode.Open, FileAccess.Read))
            {
                credentials = NewtonsoftJsonSerializer.Instance
                    .Deserialize<Credentials>(fs);
            }

            // Generate a JWT signed with the service account's private key 
            // containing a special "target_audience" claim.
            var jwtBasedAccessToken =
                CreateAccessToken(credentials.PrivateKey, iapClientId,
                    credentials.ClientEmail);

            var body = new Dictionary<string, string>
            {
                { "assertion", jwtBasedAccessToken },
                { "grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"}
            };

            // Request an OIDC token for the Cloud IAP-secured client ID.
            var httpClient = new HttpClient();
            var httpContent = new FormUrlEncodedContent(body);
            var result = httpClient.PostAsync(GoogleAuthConsts.OidcTokenUrl,
                httpContent).Result;
            var responseContent = result.Content.ReadAsStringAsync().Result;
            if (!result.IsSuccessStatusCode)
            {
                throw new HttpRequestException(string.Format(
                    CultureInfo.CurrentCulture, "{0} {1}\n{2}",
                    (int)result.StatusCode, result.ReasonPhrase,
                    responseContent));
            }
            string token = JsonConvert.DeserializeObject<IapResponse>(
                responseContent).IdToken;

            // Include the OIDC token in an Authorization: Bearer header to 
            // IAP-secured resource
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            string response = httpClient.GetStringAsync(uri).Result;
            return response;
        }

        /// <summary>
        /// Generate a JWT signed with the service account's private key 
        /// containing a special "target_audience" claim.
        /// </summary>
        /// <param name="privateKey">The private key string pulled from
        /// a credentials .json file.</param>
        /// <param name="iapClientId">The client id observed on 
        /// https://console.cloud.google.com/apis/credentials.</param>
        /// <param name="email">The e-mail address associated with the
        /// privateKey.</param>
        /// <returns>An access token.</returns>
        static string CreateAccessToken(string privateKey,
            string iapClientId, string email)
        {
            var now = DateTime.UtcNow;
            var currentTime = ToUnixEpochDate(now);
            var expTime = ToUnixEpochDate(now.AddHours(1));

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Aud,
                    GoogleAuthConsts.OidcTokenUrl),
                new Claim(JwtRegisteredClaimNames.Sub, email),
                new Claim(JwtRegisteredClaimNames.Iat, currentTime.ToString()),
                new Claim(JwtRegisteredClaimNames.Exp, expTime.ToString()),
                new Claim(JwtRegisteredClaimNames.Iss, email),

                // We need to generate a JWT signed with the service account's 
                // private key containing a special "target_audience" claim. 
                // That claim should contain the clientId of IAP we eventually
                // want to access.
                new Claim("target_audience", iapClientId)
            };

            // Encryption algorithm must be RSA SHA-256, according to
            // https://developers.google.com/identity/protocols/OAuth2ServiceAccount
            SecurityKey key = new RsaSecurityKey(
                Pkcs8.DecodeRsaParameters(privateKey));
            var signingCredentials = new SigningCredentials(key,
                SecurityAlgorithms.RsaSha256);
            var token = new JwtSecurityToken(
                claims: claims,
                signingCredentials: signingCredentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        static long ToUnixEpochDate(DateTime date)
              => (long)Math.Round((date.ToUniversalTime() -
                                   new DateTimeOffset(1970, 1, 1, 0, 0, 0,
                                        TimeSpan.Zero)).TotalSeconds);
    }

    class Credentials : JsonCredentialParameters
    {
        [JsonProperty("project_id")]
        public string ProjectId { get; set; }
    }

    class IapResponse
    {
        [JsonProperty("id_token")]
        public string IdToken { get; set; }
    }
}
// [END generate_iap_request]