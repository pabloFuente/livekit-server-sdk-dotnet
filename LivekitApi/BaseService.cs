using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// Base class of the service clients: holds the credentials and the <see cref="HttpClient"/>, signs every
    /// call with a short-lived token and sends it as a Twirp request.
    /// </summary>
    public class BaseService
    {
        private static readonly HttpClientHandler SharedHandler = new HttpClientHandler();

        private static readonly MediaTypeHeaderValue ProtobufContentType = new MediaTypeHeaderValue(
            "application/protobuf"
        );

        // Product token sent as User-Agent: livekit-server-sdk-dotnet/<version>
        private static readonly ProductInfoHeaderValue UserAgent = new ProductInfoHeaderValue(
            "livekit-server-sdk-dotnet",
            SdkVersion()
        );

        private readonly string apiKey;
        private readonly string apiSecret;
        private readonly Uri baseUri;

        /// <summary>
        /// The <see cref="HttpClient"/> the requests are sent with: the one supplied to the constructor, or one
        /// owned by the SDK (which shares a connection pool with the other SDK-owned clients).
        /// </summary>
        protected readonly HttpClient httpClient;

        /// <summary>
        /// Stores the credentials and the <see cref="HttpClient"/> the requests are sent with.
        /// </summary>
        /// <param name="host">URL of the LiveKit server, <c>http(s)://</c> or <c>ws(s)://</c>; a subpath such as <c>https://host/livekit</c> is kept.</param>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="client">Optional <see cref="HttpClient"/> to send the requests with. The SDK never modifies it, so it can be shared with other clients and code.</param>
        public BaseService(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
        {
            this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            this.apiSecret = apiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");

            if (string.IsNullOrEmpty(this.apiKey) || string.IsNullOrEmpty(this.apiSecret))
            {
                throw new ArgumentException("apiKey and apiSecret must be set");
            }
            if (Encoding.UTF8.GetBytes(this.apiSecret).Length < 32)
            {
                throw new ArgumentException(
                    "apiSecret must be at least 256 bits long. Currently it is "
                        + Encoding.UTF8.GetBytes(this.apiSecret).Length * 8
                        + " bits long"
                );
            }

            baseUri = new Uri(WithTrailingSlash(ToHttpScheme(host)));
            if (client == null)
            {
                // SDK-owned clients share one handler (and its connection pool): service clients
                // are often created per request and never disposed
                httpClient = new HttpClient(SharedHandler, false) { BaseAddress = baseUri };
            }
            else
            {
                // A caller-supplied client may be shared with other code (or other service
                // clients) and may already have sent requests, so it is never modified: the base
                // address, the bearer token and the user agent travel with each request instead.
                httpClient = client;
            }
        }

        /// <summary>
        /// Sends one Twirp call: a protobuf-encoded POST to <paramref name="route"/> (relative
        /// to the host, e.g. <c>twirp/livekit.RoomService/CreateRoom</c>) signed with
        /// <paramref name="bearerToken"/>, parsed with <paramref name="parser"/>. Every call builds
        /// its own request, so concurrent calls never share headers and the shared
        /// <see cref="HttpClient"/> is left untouched. A non-2xx answer is raised as
        /// <see cref="TwirpException"/>.
        /// </summary>
        internal async Task<TResponse> SendAsync<TRequest, TResponse>(
            string route,
            TRequest request,
            MessageParser<TResponse> parser,
            string bearerToken,
            CancellationToken cancellationToken = default
        )
            where TRequest : IMessage
            where TResponse : IMessage<TResponse>
        {
            using (var content = new ByteArrayContent(request.ToByteArray()))
            {
                content.Headers.ContentType = ProtobufContentType;
                using (
                    var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, route))
                )
                {
                    message.Content = content;
                    message.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        bearerToken
                    );
                    // A request header replaces the client's default one, so keep the caller's own
                    // User-Agent products and add the SDK's after them
                    foreach (var product in httpClient.DefaultRequestHeaders.UserAgent)
                    {
                        message.Headers.UserAgent.Add(product);
                    }
                    message.Headers.UserAgent.Add(UserAgent);

                    using (
                        var response = await httpClient
                            .SendAsync(message, cancellationToken)
                            .ConfigureAwait(false)
                    )
                    {
                        var body = await response
                            .Content.ReadAsByteArrayAsync()
                            .ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw TwirpException.FromResponse(
                                Encoding.UTF8.GetString(body),
                                (int)response.StatusCode
                            );
                        }
                        return parser.ParseFrom(body);
                    }
                }
            }
        }

        private static string SdkVersion()
        {
            var assembly = typeof(BaseService).Assembly;
            var informational = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault();
            var version = informational != null ? informational.InformationalVersion : null;
            if (string.IsNullOrEmpty(version))
            {
                version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            }
            var plus = version.IndexOf('+');
            return plus >= 0 ? version.Substring(0, plus) : version;
        }

        // LIVEKIT_URL is usually the WebSocket URL of the server; the service APIs speak HTTP on
        // the same host (the Node and Python SDKs do the same conversion)
        private static string ToHttpScheme(string host)
        {
            if (host == null)
            {
                return null;
            }
            if (host.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + host.Substring("wss://".Length);
            }
            if (host.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                return "http://" + host.Substring("ws://".Length);
            }
            return host;
        }

        // Twirp routes are relative ("twirp/livekit.X/Y") and System.Uri drops the last path
        // segment of a base address without a trailing "/" when resolving a relative reference,
        // so "https://my.server.com/livekit" would send requests to "https://my.server.com/twirp/...".
        // Append the slash so hosts with a subpath work whether or not the caller added it.
        private static string WithTrailingSlash(string host)
        {
            if (host == null || host.EndsWith("/", StringComparison.Ordinal))
            {
                return host;
            }
            return host + "/";
        }

        /// <summary>
        /// Builds the bearer token of a call that needs <paramref name="videoGrants"/>, valid for
        /// <see cref="Constants.DefaultTtl"/>.
        /// </summary>
        /// <param name="videoGrants">Room permissions the call needs.</param>
        /// <returns>The signed JWT, sent as bearer token.</returns>
        protected string AuthHeader(VideoGrants videoGrants)
        {
            var accessToken = new AccessToken(apiKey, apiSecret);
            accessToken.WithGrants(videoGrants);
            accessToken.WithTtl(Constants.DefaultTtl);

            return accessToken.ToJwt();
        }

        /// <summary>
        /// Builds the bearer token of a SIP call that needs <paramref name="videoGrants"/> and
        /// <paramref name="sipGrants"/>, valid for <see cref="Constants.DefaultTtl"/>.
        /// </summary>
        /// <param name="videoGrants">Room permissions the call needs.</param>
        /// <param name="sipGrants">SIP permissions the call needs.</param>
        /// <returns>The signed JWT, sent as bearer token.</returns>
        protected string AuthHeader(VideoGrants videoGrants, SIPGrants sipGrants)
        {
            var accessToken = new AccessToken(apiKey, apiSecret);
            accessToken.WithGrants(videoGrants);
            accessToken.WithSipGrants(sipGrants);
            accessToken.WithTtl(Constants.DefaultTtl);

            return accessToken.ToJwt();
        }
    }
}
