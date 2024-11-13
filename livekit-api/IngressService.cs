using System.Net.Http.Headers;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet;

public class IngressService : BaseService
{
    public IngressService(string host, string apiKey, string secret)
        : base(host, apiKey, secret) { }

    public async Task<IngressInfo> CreateIngress(CreateIngressRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { IngressAdmin = true })
        );
        return await Twirp.CreateIngress(httpClient, request);
    }

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
