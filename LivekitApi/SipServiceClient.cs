using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    public class SipServiceClient : BaseService
    {
        public SipServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        [System.Obsolete(
            "This method is obsolete. Call ListSIPInboundTrunk or ListSIPOutboundTrunk instead.",
            false
        )]
        public async Task<ListSIPTrunkResponse> ListSIPTrunk(ListSIPTrunkRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.SIP/ListSIPTrunk",
                    request,
                    ListSIPTrunkResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Create a new SIP inbound trunk.
        /// </summary>
        public async Task<SIPInboundTrunkInfo> CreateSIPInboundTrunk(
            CreateSIPInboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/CreateSIPInboundTrunk",
                    request,
                    SIPInboundTrunkInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Create a new SIP outbound trunk.
        /// </summary>
        public async Task<SIPOutboundTrunkInfo> CreateSIPOutboundTrunk(
            CreateSIPOutboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/CreateSIPOutboundTrunk",
                    request,
                    SIPOutboundTrunkInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get a SIP inbound trunk.
        /// </summary>
        public async Task<GetSIPInboundTrunkResponse> GetSIPInboundTrunk(
            GetSIPInboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/GetSIPInboundTrunk",
                    request,
                    GetSIPInboundTrunkResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get a SIP outbound trunk.
        /// </summary>
        public async Task<GetSIPOutboundTrunkResponse> GetSIPOutboundTrunk(
            GetSIPOutboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/GetSIPOutboundTrunk",
                    request,
                    GetSIPOutboundTrunkResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// List SIP inbound trunks.
        /// </summary>
        public async Task<ListSIPInboundTrunkResponse> ListSIPInboundTrunk(
            ListSIPInboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/ListSIPInboundTrunk",
                    request,
                    ListSIPInboundTrunkResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// List SIP outbound trunks.
        /// </summary>
        public async Task<ListSIPOutboundTrunkResponse> ListSIPOutboundTrunk(
            ListSIPOutboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/ListSIPOutboundTrunk",
                    request,
                    ListSIPOutboundTrunkResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a SIP trunk.
        /// </summary>
        public async Task<SIPTrunkInfo> DeleteSIPTrunk(DeleteSIPTrunkRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.SIP/DeleteSIPTrunk",
                    request,
                    SIPTrunkInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Create a new SIP dispatch rule.
        /// </summary>
        public async Task<SIPDispatchRuleInfo> CreateSIPDispatchRule(
            CreateSIPDispatchRuleRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/CreateSIPDispatchRule",
                    request,
                    SIPDispatchRuleInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// List SIP dispatch rules with optional filtering.
        /// </summary>
        public async Task<ListSIPDispatchRuleResponse> ListSIPDispatchRule(
            ListSIPDispatchRuleRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/ListSIPDispatchRule",
                    request,
                    ListSIPDispatchRuleResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a SIP dispatch rule.
        /// </summary>
        public async Task<SIPDispatchRuleInfo> DeleteSIPDispatchRule(
            DeleteSIPDispatchRuleRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/DeleteSIPDispatchRule",
                    request,
                    SIPDispatchRuleInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Create a new SIP participant.
        /// </summary>
        public async Task<SIPParticipantInfo> CreateSIPParticipant(
            CreateSIPParticipantRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/CreateSIPParticipant",
                    request,
                    SIPParticipantInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Call = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Transfer a SIP participant to a different room.
        /// </summary>
        public async Task<TransferSIPParticipantResponse> TransferSIPParticipant(
            TransferSIPParticipantRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/TransferSIPParticipant",
                    request,
                    TransferSIPParticipantResponse.Parser,
                    AuthHeader(
                        new VideoGrants { RoomAdmin = true, Room = request.RoomName },
                        new SIPGrants { Call = true }
                    )
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates an existing SIP inbound trunk.
        /// </summary>
        public async Task<SIPInboundTrunkInfo> UpdateSIPInboundTrunk(
            UpdateSIPInboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/UpdateSIPInboundTrunk",
                    request,
                    SIPInboundTrunkInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates an existing SIP outbound trunk.
        /// </summary>
        public async Task<SIPOutboundTrunkInfo> UpdateSIPOutboundTrunk(
            UpdateSIPOutboundTrunkRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/UpdateSIPOutboundTrunk",
                    request,
                    SIPOutboundTrunkInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates an existing SIP dispatch rule.
        /// </summary>
        public async Task<SIPDispatchRuleInfo> UpdateSIPDispatchRule(
            UpdateSIPDispatchRuleRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.SIP/UpdateSIPDispatchRule",
                    request,
                    SIPDispatchRuleInfo.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }
    }
}
