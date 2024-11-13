using System.Net.Http.Headers;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet;

public class SipService : BaseService
{
    public SipService(string host, string apiKey, string secret)
        : base(host, apiKey, secret) { }

    public async Task<SIPTrunkInfo> CreateSIPTrunk(CreateSIPTrunkRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.CreateSIPTrunk(httpClient, request);
    }

    public async Task<ListSIPTrunkResponse> ListSIPTrunk(ListSIPTrunkRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.ListSIPTrunk(httpClient, request);
    }

    public async Task<SIPInboundTrunkInfo> CreateSIPInboundTrunk(
        CreateSIPInboundTrunkRequest request
    )
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.CreateSIPInboundTrunk(httpClient, request);
    }

    public async Task<SIPOutboundTrunkInfo> CreateSIPOutboundTrunk(
        CreateSIPOutboundTrunkRequest request
    )
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.CreateSIPOutboundTrunk(httpClient, request);
    }

    public async Task<ListSIPInboundTrunkResponse> ListSIPInboundTrunk(
        ListSIPInboundTrunkRequest request
    )
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.ListSIPInboundTrunk(httpClient, request);
    }

    public async Task<ListSIPOutboundTrunkResponse> ListSIPOutboundTrunk(
        ListSIPOutboundTrunkRequest request
    )
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.ListSIPOutboundTrunk(httpClient, request);
    }

    public async Task<SIPTrunkInfo> DeleteSIPTrunk(DeleteSIPTrunkRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.DeleteSIPTrunk(httpClient, request);
    }

    public async Task<SIPDispatchRuleInfo> CreateSIPDispatchRule(
        CreateSIPDispatchRuleRequest request
    )
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.CreateSIPDispatchRule(httpClient, request);
    }

    public async Task<ListSIPDispatchRuleResponse> ListSIPDispatchRule(
        ListSIPDispatchRuleRequest request
    )
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.ListSIPDispatchRule(httpClient, request);
    }

    public async Task<SIPDispatchRuleInfo> DeleteSIPDispatchRule(
        DeleteSIPDispatchRuleRequest request
    )
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
        );
        return await Twirp.DeleteSIPDispatchRule(httpClient, request);
    }

    public async Task<SIPParticipantInfo> CreateSIPParticipant(CreateSIPParticipantRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { }, new SIPGrants { Call = true })
        );
        return await Twirp.CreateSIPParticipant(httpClient, request);
    }
}
