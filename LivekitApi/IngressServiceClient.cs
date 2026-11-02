using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// A client for interacting with the Ingress service.
    /// See: <see href="https://docs.livekit.io/realtime/ingress/overview/">Ingress</see>
    /// </summary>
    public class IngressServiceClient : BaseService
    {
        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="host">URL of the LiveKit server, <c>http(s)://</c> or <c>ws(s)://</c>; a subpath such as <c>https://host/livekit</c> is kept.</param>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="client">Optional <see cref="HttpClient"/> to send the requests with. The SDK never modifies it, so it can be shared with other clients and code.</param>
        public IngressServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        /// <summary>
        /// Creates a new ingress. Default audio and video options will be used if none is provided.
        /// </summary>
        /// <param name="request">Input type (RTMP, WHIP or URL), name, the room and participant to publish as, and the encoding options.</param>
        /// <returns>The created ingress, including the URL and stream key to push media to.</returns>
        public async Task<IngressInfo> CreateIngress(CreateIngressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Ingress/CreateIngress",
                    request,
                    IngressInfo.Parser,
                    AuthHeader(new VideoGrants { IngressAdmin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the existing ingress with the given ingressID. Only inactive ingress can be updated.
        /// </summary>
        /// <param name="request">Ingress ID and the fields to change.</param>
        /// <returns>The updated ingress.</returns>
        public async Task<IngressInfo> UpdateIngress(UpdateIngressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Ingress/UpdateIngress",
                    request,
                    IngressInfo.Parser,
                    AuthHeader(new VideoGrants { IngressAdmin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Lists the ingresses, optionally filtered by room or ingress ID.
        /// </summary>
        /// <param name="request">Optional filters: room name, ingress ID.</param>
        /// <returns>The matching ingresses.</returns>
        public async Task<ListIngressResponse> ListIngress(ListIngressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Ingress/ListIngress",
                    request,
                    ListIngressResponse.Parser,
                    AuthHeader(new VideoGrants { IngressAdmin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes an ingress.
        /// </summary>
        /// <param name="request">Ingress ID.</param>
        /// <returns>The deleted ingress.</returns>
        public async Task<IngressInfo> DeleteIngress(DeleteIngressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Ingress/DeleteIngress",
                    request,
                    IngressInfo.Parser,
                    AuthHeader(new VideoGrants { IngressAdmin = true })
                )
                .ConfigureAwait(false);
        }
    }
}
