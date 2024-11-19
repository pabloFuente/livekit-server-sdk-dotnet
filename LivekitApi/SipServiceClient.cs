using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

namespace Livekit.Server.Sdk.Dotnet
{
    public class SipServiceClient : BaseService
    {
        public SipServiceClient(string host, string apiKey, string apiSecret)
            : base(host, apiKey, apiSecret) { }

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

        public async Task<GetSIPInboundTrunkResponse> GetSIPInboundTrunk(
            GetSIPInboundTrunkRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.GetSIPInboundTrunk(httpClient, request);
        }

        public async Task<GetSIPOutboundTrunkResponse> GetSIPOutboundTrunk(
            GetSIPOutboundTrunkRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.GetSIPOutboundTrunk(httpClient, request);
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

        public async Task<SIPParticipantInfo> CreateSIPParticipant(
            CreateSIPParticipantRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.CreateSIPParticipant(httpClient, request);
        }

        public async Task<Empty> TransferSIPParticipant(TransferSIPParticipantRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.TransferSIPParticipant(httpClient, request);
        }
    }
}
