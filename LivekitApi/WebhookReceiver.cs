using System;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet
{
    public class WebhookReceiver : TokenVerifier
    {
        /// <summary>
        /// WebhookReceiver Constructor.
        /// </summary>
        /// <param name="apiKey">The LiveKit API Key, can be set in env LIVEKIT_API_KEY.</param>
        /// <param name="apiSecret">The LiveKit API Secret Key, can be set in env LIVEKIT_API_SECRET.</param>
        /// <exception cref="Exception">Thrown when apiKey or apiSecret are not provided.</exception>
        public WebhookReceiver(string apiKey = null, string apiSecret = null)
        {
            apiKey = apiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            apiSecret = apiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                throw new Exception("ApiKey and apiSecret are required.");
            }
        }

        /// <summary>
        /// Process a webhook request.
        /// </summary>
        /// <param name="body">The string of the posted body.</param>
        /// <param name="authHeader">The Authorization header of the request.</param>
        /// <param name="skipAuth">True to skip auth validation, false otherwise.</param>
        /// <param name="ignoreUnknownFields">True to ignore unknown fields, false otherwise.</param>
        /// <returns>WebhookEvent</returns>
        /// <exception cref="Exception">Thrown when authorization fails or checksum does not match.</exception>
        public WebhookEvent Receive(
            string body,
            string authHeader = null,
            bool skipAuth = false,
            bool ignoreUnknownFields = true
        )
        {
            // Verify token.
            if (!skipAuth)
            {
                if (string.IsNullOrEmpty(authHeader))
                {
                    throw new Exception("Authorization header is empty");
                }

                var claims = Verify(authHeader);

                // Validate Sha256.
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(body));
                    var hashBase64 = Convert.ToBase64String(hash);
                    if (claims.Sha256 != hashBase64)
                    {
                        throw new Exception("Sha256 checksum of the body does not match");
                    }
                }
            }

            // Parse the body
            var parser = new MessageParser<WebhookEvent>(() => new WebhookEvent());
            parser.WithDiscardUnknownFields(ignoreUnknownFields);
            return parser.ParseFrom(Encoding.Default.GetBytes(body));
        }
    }
}
