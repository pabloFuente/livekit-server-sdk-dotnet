using System.Net.Http.Headers;
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
        public IngressServiceClient(string host, string apiKey, string apiSecret)
            : base(host, apiKey, apiSecret) { }

        /// <summary>
        /// Creates a new ingress. Default audio and video options will be used if none is provided.
        /// </summary>
        public async Task<IngressInfo> CreateIngress(CreateIngressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { IngressAdmin = true })
            );
            return await Twirp.CreateIngress(httpClient, request);
        }

        /// <summary>
        /// Updates the existing ingress with the given ingressID. Only inactive ingress can be updated.
        /// </summary>
        public async Task<IngressInfo> UpdateIngress(UpdateIngressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { IngressAdmin = true })
            );
            return await Twirp.UpdateIngress(httpClient, request);
        }

        public async Task<ListIngressResponse> ListIngress(ListIngressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { IngressAdmin = true })
            );
            return await Twirp.ListIngress(httpClient, request);
        }

        public async Task<IngressInfo> DeleteIngress(DeleteIngressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { IngressAdmin = true })
            );
            return await Twirp.DeleteIngress(httpClient, request);
        }
    }
}
