using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

namespace Livekit.Server.Sdk.Dotnet
{
    public class SipServiceClient : BaseService
    {
        public SipServiceClient(
            string host,
            string apiKey,
            string apiSecret,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        [System.Obsolete(
            "This method is obsolete. Call ListSipInboundTrunk or ListSipOutboundTrunk instead.",
            false
        )]
        public async Task<ListSIPTrunkResponse> ListSIPTrunk(ListSIPTrunkRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.ListSIPTrunk(httpClient, request);
        }

        /// <summary>
        /// Create a new SIP inbound trunk.
        /// </summary>
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

        /// <summary>
        /// Create a new SIP outbound trunk.
        /// </summary>
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

        /// <summary>
        /// Get a SIP inbound trunk.
        /// </summary>
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

        /// <summary>
        /// Get a SIP outbound trunk.
        /// </summary>
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

        /// <summary>
        /// List SIP inbound trunks.
        /// </summary>
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

        /// <summary>
        /// List SIP outbound trunks.
        /// </summary>
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

        /// <summary>
        /// Delete a SIP trunk.
        /// </summary>
        public async Task<SIPTrunkInfo> DeleteSIPTrunk(DeleteSIPTrunkRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.DeleteSIPTrunk(httpClient, request);
        }

        /// <summary>
        /// Create a new SIP dispatch rule.
        /// </summary>
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

        /// <summary>
        /// List SIP dispatch rules with optional filtering.
        /// </summary>
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

        /// <summary>
        /// Delete a SIP dispatch rule.
        /// </summary>
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

        /// <summary>
        /// Create a new SIP participant.
        /// </summary>
        public async Task<SIPParticipantInfo> CreateSIPParticipant(
            CreateSIPParticipantRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Call = true })
            );
            return await Twirp.CreateSIPParticipant(httpClient, request);
        }

        /// <summary>
        /// Transfer a SIP participant to a different room.
        /// </summary>
        public async Task<Empty> TransferSIPParticipant(TransferSIPParticipantRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(
                    new VideoGrants { RoomAdmin = true, Room = request.RoomName },
                    new SIPGrants { Call = true }
                )
            );
            return await Twirp.TransferSIPParticipant(httpClient, request);
        }

        /// <summary>
        /// Updates an existing SIP inbound trunk.
        /// </summary>
        public async Task<SIPInboundTrunkInfo> UpdateSIPInboundTrunk(
            UpdateSIPInboundTrunkRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.UpdateSIPInboundTrunk(httpClient, request);
        }

        /// <summary>
        /// Updates an existing SIP outbound trunk.
        /// </summary>
        public async Task<SIPOutboundTrunkInfo> UpdateSIPOutboundTrunk(
            UpdateSIPOutboundTrunkRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.UpdateSIPOutboundTrunk(httpClient, request);
        }

        /// <summary>
        /// Updates an existing SIP dispatch rule.
        /// </summary>
        public async Task<SIPDispatchRuleInfo> UpdateSIPDispatchRule(
            UpdateSIPDispatchRuleRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
            );
            return await Twirp.UpdateSIPDispatchRule(httpClient, request);
        }
    }
}
