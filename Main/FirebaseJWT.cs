using Jose;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SpawnerWorker
{
    public static class FirebaseJWT
    {
        static readonly DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        const string ProjectId = "roguefleetonline";
        const string Issuer = "https://securetoken.google.com/" + ProjectId;

        const string URL = "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";

        static Dictionary<string, RSACryptoServiceProvider> cryptoServiceProviders;
        static TimeSpan timeUntilRefresh;

        public static bool Verify(string token, out string tokenOutput)
        {
            Dictionary<string, string> headers = JWT.Headers<Dictionary<string, string>>(token);
            string kid = headers["kid"];

            if (cryptoServiceProviders.TryGetValue(kid, out var key))
            {
                string unparsedPayload;

                try
                {
                    unparsedPayload = JWT.Decode(token, key, JwsAlgorithm.RS256);
                }
                catch (InvalidAlgorithmException)
                {
                    tokenOutput = "INVALID ALGORITHM";
                    return false;
                }
                catch (IntegrityException)
                {
                    tokenOutput = "INVALID TOKEN";
                    return false;
                }

                var payload = JsonConvert.DeserializeObject<TokenPayload>(unparsedPayload);

                var now = (int)ToUnixTime(DateTime.Now);
                
                //Must be in the future
                if (payload.exp < now)
                {
                    tokenOutput = string.Format("TOKEN_EXPIRED {0} < {1}", payload.exp, now);
                    return false;
                }

                //Must be in the past
                if (payload.auth_time > now)
                {
                    tokenOutput = string.Format("INVALID_AUTHENTICATION_TIME {0} > {1}", payload.auth_time, now);
                    return false;
                }

                //Must be in the past
                if (payload.iat > now)
                {
                    tokenOutput = string.Format("INVALID_ISSUE-AT-TIME {0} > {1}", payload.iat, now);
                    return false;
                }

                //Must correspond to projectId
                if (payload.aud != ProjectId)
                {
                    tokenOutput = string.Format("INVALID_AUDIENCE {0} != {1}", payload.aud, ProjectId);
                    return false;
                }

                if (payload.iss != Issuer)
                {
                    tokenOutput = string.Format("INVALID_ISSUER {0} != {1}", payload.iss, Issuer);
                    return false;
                }

                tokenOutput = payload.sub;
                return true;
            }

            tokenOutput = "INVALID TOKEN KID";

            return false;
        }

        public async static void PeriodicKeyUpdate()
        {
            async void BackgroundUpdate()
            {
                while (true)
                {
                    string keys = await GetPublicKeysAsync();

                    cryptoServiceProviders = UpdateCryptoServiceProviders(keys);

                    Thread.Sleep(timeUntilRefresh);
                }
            }

            var task = new Task(() => BackgroundUpdate());
            task.Start();
            await task;
        }

        static async Task<string> GetPublicKeysAsync()
        {
            var uri = new Uri(URL);

            var webRequest = WebRequest.Create(uri);
            
            using (WebResponse webResponse = await webRequest.GetResponseAsync())
            {
                var headers = webResponse.Headers;
                var cacheControl = headers.Get("Cache-Control");
                var resultString = Regex.Match(cacheControl, @"\d+").Value;
                var maxAge = int.Parse(resultString);

                timeUntilRefresh = TimeSpan.FromSeconds(maxAge);

                using (var stream = webResponse.GetResponseStream())
                {
                    using (var reader = new StreamReader(stream))
                    {
                        return await reader.ReadToEndAsync();
                    }   
                }    
            }
        }

        static Dictionary<string, RSACryptoServiceProvider> UpdateCryptoServiceProviders(string json)
        {
            var publicKeys = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

            var newCryptoServiceProviders = new Dictionary<string, RSACryptoServiceProvider>();
            foreach (var keyIdPEMPair in publicKeys)
            {
                var keyId = keyIdPEMPair.Key;
                var pem = keyIdPEMPair.Value;

                var certBuffer = GetBytesFromPEM(pem);
                var publicKey = new X509Certificate2(certBuffer).PublicKey;
                var cryptoServiceProvider = publicKey.Key as RSACryptoServiceProvider;

                newCryptoServiceProviders.Add(keyId, cryptoServiceProvider);
            }

            return newCryptoServiceProviders;
        }

        static byte[] GetBytesFromPEM(string pem, string type = "CERTIFICATE")
        {
            var header = string.Format("-----BEGIN {0}-----", type);
            var footer = string.Format("-----END {0}-----", type);

            int start = pem.IndexOf(header) + header.Length;
            int end = pem.IndexOf(footer, start);

            string base64 = pem.Substring(start, (end - start));

            return Convert.FromBase64String(base64);
        }

        static double ToUnixTime(DateTime date)
        {
            return (date.ToUniversalTime() - epoch).TotalSeconds;
        }
    }

    #pragma warning disable IDE1006 // Naming Styles
    class TokenPayload
    {
        public int exp { get; set; }
        public int iat { get; set; }
        public string aud { get; set; }
        public string iss { get; set; }
        public string sub { get; set; }
        public int auth_time { get; set; }
    }
    #pragma warning restore IDE1006 // Naming Styles
}
