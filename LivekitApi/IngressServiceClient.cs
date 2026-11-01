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
        /// A client for interacting with the Ingress service.
        /// See: <see href="https://docs.livekit.io/realtime/ingress/overview/">Ingress</see>
        /// </summary>
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
