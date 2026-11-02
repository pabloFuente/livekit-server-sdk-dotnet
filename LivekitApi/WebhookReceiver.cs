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
    /// <example>
    /// <code language="csharp">
    /// var receiver = new WebhookReceiver("api-key", "api-secret");
    ///
    /// // ASP.NET Core minimal API. Pass the raw body: LiveKit signs its SHA-256.
    /// app.MapPost("/livekit/webhook", async (HttpRequest request) =>
    /// {
    ///     using var body = new MemoryStream();
    ///     await request.Body.CopyToAsync(body);
    ///
    ///     WebhookEvent webhookEvent = receiver.Receive(
    ///         body.ToArray(),
    ///         request.Headers.Authorization.ToString()
    ///     );
    ///     Console.WriteLine($"{webhookEvent.Event} in room {webhookEvent.Room?.Name}");
    ///     return Results.Ok();
    /// });
    /// </code>
    /// </example>
    public class WebhookReceiver : TokenVerifier
    {
        /// <summary>
        /// Creates a receiver for webhooks signed with the given API key and secret.
        /// </summary>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret, at least 32 bytes long; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <exception cref="ArgumentException">The key or the secret is missing, or the secret is shorter than 32 bytes.</exception>
        public WebhookReceiver(string apiKey = null, string apiSecret = null)
            : base(apiKey, apiSecret) { }

        /// <summary>
        /// Creates a receiver with a custom clock tolerance for the token lifetime.
        /// </summary>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when null.</param>
        /// <param name="apiSecret">API secret, at least 32 bytes long; the <c>LIVEKIT_API_SECRET</c> environment variable when null.</param>
        /// <param name="leeway">Clock tolerance applied to the token lifetime; <see cref="Constants.DefaultLeeway"/> when null.</param>
        /// <exception cref="ArgumentException">The key or the secret is missing, or the secret is shorter than 32 bytes.</exception>
        public WebhookReceiver(string apiKey, string apiSecret, TimeSpan? leeway)
            : base(apiKey, apiSecret, leeway) { }

        /// <summary>
        /// Verifies a webhook request and parses its event, from the raw bytes of the body, which is what
        /// LiveKit signs. Prefer this overload.
        /// </summary>
        /// <param name="body">The raw bytes of the posted body.</param>
        /// <param name="authHeader">Value of the <c>Authorization</c> header of the request.</param>
        /// <param name="skipAuth">True to skip the token and SHA-256 checks.</param>
        /// <param name="ignoreUnknownFields">True to ignore the fields this version of the SDK does not know, so events from newer servers still parse.</param>
        /// <returns>The decoded event.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
        /// <exception cref="Exception">The header is empty, or the SHA-256 of the body does not match the token.</exception>
        /// <exception cref="Microsoft.IdentityModel.Tokens.SecurityTokenException">The token has a wrong signature or issuer, or is expired.</exception>
        /// <exception cref="ArgumentException">The header is not a well-formed JWT (<c>SecurityTokenMalformedException</c>).</exception>
        /// <exception cref="Google.Protobuf.InvalidJsonException">The body is not valid JSON.</exception>
        /// <exception cref="Google.Protobuf.InvalidProtocolBufferException">The body is not a webhook event, or has unknown fields while <paramref name="ignoreUnknownFields"/> is false.</exception>
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
        /// Verifies a webhook request and parses its event, from the body as a string. The text is encoded
        /// as UTF-8 again to check its SHA-256, so it must be exactly what LiveKit posted; the
        /// <see cref="Receive(byte[], string, bool, bool)"/> overload avoids that conversion.
        /// </summary>
        /// <param name="body">The posted body, as text.</param>
        /// <param name="authHeader">Value of the <c>Authorization</c> header of the request.</param>
        /// <param name="skipAuth">True to skip the token and SHA-256 checks.</param>
        /// <param name="ignoreUnknownFields">True to ignore the fields this version of the SDK does not know, so events from newer servers still parse.</param>
        /// <returns>The decoded event.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
        /// <exception cref="Exception">The header is empty, or the SHA-256 of the body does not match the token.</exception>
        /// <exception cref="Microsoft.IdentityModel.Tokens.SecurityTokenException">The token has a wrong signature or issuer, or is expired.</exception>
        /// <exception cref="ArgumentException">The header is not a well-formed JWT (<c>SecurityTokenMalformedException</c>).</exception>
        /// <exception cref="Google.Protobuf.InvalidJsonException">The body is not valid JSON.</exception>
        /// <exception cref="Google.Protobuf.InvalidProtocolBufferException">The body is not a webhook event, or has unknown fields while <paramref name="ignoreUnknownFields"/> is false.</exception>
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
