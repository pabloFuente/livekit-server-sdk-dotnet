using System;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// Receives the webhook events LiveKit posts: verifies the token of the <c>Authorization</c> header and the
    /// SHA-256 of the body, then parses the <see cref="WebhookEvent"/>.
    /// </summary>
    public class WebhookReceiver : TokenVerifier
    {
        /// <summary>
        /// WebhookReceiver Constructor.
        /// </summary>
        /// <param name="apiKey">The LiveKit API Key, can be set in env LIVEKIT_API_KEY.</param>
        /// <param name="apiSecret">The LiveKit API Secret Key, can be set in env LIVEKIT_API_SECRET.</param>
        /// <exception cref="Exception">Thrown when apiKey or apiSecret are not provided.</exception>
        public WebhookReceiver(string apiKey = null, string apiSecret = null)
            : base(apiKey, apiSecret) { }

        /// <summary>
        /// WebhookReceiver Constructor with a custom clock tolerance for the token validation.
        /// </summary>
        /// <param name="apiKey">The LiveKit API Key, can be set in env LIVEKIT_API_KEY.</param>
        /// <param name="apiSecret">The LiveKit API Secret Key, can be set in env LIVEKIT_API_SECRET.</param>
        /// <param name="leeway">Clock tolerance applied to the token lifetime (defaults to 1 minute).</param>
        public WebhookReceiver(string apiKey, string apiSecret, TimeSpan? leeway)
            : base(apiKey, apiSecret, leeway) { }

        /// <summary>
        /// Process a webhook request from its raw body bytes, which is what LiveKit hashes.
        /// </summary>
        /// <param name="body">The raw bytes of the posted body.</param>
        /// <param name="authHeader">The Authorization header of the request.</param>
        /// <param name="skipAuth">True to skip auth validation, false otherwise.</param>
        /// <param name="ignoreUnknownFields">True to ignore unknown fields, false otherwise.</param>
        /// <returns>WebhookEvent</returns>
        /// <exception cref="Exception">Thrown when authorization fails or checksum does not match.</exception>
        public WebhookEvent Receive(
            byte[] body,
            string authHeader = null,
            bool skipAuth = false,
            bool ignoreUnknownFields = true
        )
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            return Receive(
                body,
                Encoding.UTF8.GetString(body),
                authHeader,
                skipAuth,
                ignoreUnknownFields
            );
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
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            return Receive(
                Encoding.UTF8.GetBytes(body),
                body,
                authHeader,
                skipAuth,
                ignoreUnknownFields
            );
        }

        private WebhookEvent Receive(
            byte[] bodyBytes,
            string body,
            string authHeader,
            bool skipAuth,
            bool ignoreUnknownFields
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
                    var hash = sha256.ComputeHash(bodyBytes);
                    var hashBase64 = Convert.ToBase64String(hash);
                    if (claims.Sha256 != hashBase64)
                    {
                        throw new Exception("Sha256 checksum of the body does not match");
                    }
                }
            }

            // Parse the body. The JSON parser must be told to ignore unknown fields, otherwise any
            // field added by a newer server makes every webhook fail after passing authentication.
            var parser = new JsonParser(
                JsonParser.Settings.Default.WithIgnoreUnknownFields(ignoreUnknownFields)
            );
            return parser.Parse<WebhookEvent>(body);
        }
    }
}
