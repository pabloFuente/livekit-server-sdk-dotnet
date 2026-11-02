using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// Client of the LiveKit SIP service: inbound and outbound trunks, dispatch rules and SIP participants
    /// (outbound calls, transfers).
    /// </summary>
    public class SipServiceClient : BaseService
    {
        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="host">URL of the LiveKit server, <c>http(s)://</c> or <c>ws(s)://</c>; a subpath such as <c>https://host/livekit</c> is kept.</param>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="client">Optional <see cref="HttpClient"/> to send the requests with. The SDK never modifies it, so it can be shared with other clients and code.</param>
        public SipServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        /// <summary>
        /// Lists the legacy SIP trunks. Deprecated by LiveKit: use <see cref="ListSIPInboundTrunk"/> or
        /// <see cref="ListSIPOutboundTrunk"/>.
        /// </summary>
        /// <param name="request">Empty request.</param>
        /// <returns>The legacy trunks.</returns>
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
        /// Creates a SIP inbound trunk.
        /// </summary>
        /// <param name="request">Trunk to create: name, numbers, allowed addresses and numbers, authentication, media options.</param>
        /// <returns>The created trunk.</returns>
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
        /// Creates a SIP outbound trunk.
        /// </summary>
        /// <param name="request">Trunk to create: name, SIP address, numbers, authentication, media options.</param>
        /// <returns>The created trunk.</returns>
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
        /// Gets a SIP inbound trunk by ID.
        /// </summary>
        /// <param name="request">Trunk ID.</param>
        /// <returns>The trunk.</returns>
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
        /// Gets a SIP outbound trunk by ID.
        /// </summary>
        /// <param name="request">Trunk ID.</param>
        /// <returns>The trunk.</returns>
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
        /// Lists the SIP inbound trunks.
        /// </summary>
        /// <param name="request">Optional filters: trunk IDs, numbers, page.</param>
        /// <returns>The matching trunks.</returns>
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
        /// Lists the SIP outbound trunks.
        /// </summary>
        /// <param name="request">Optional filters: trunk IDs, numbers, page.</param>
        /// <returns>The matching trunks.</returns>
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
        /// Deletes a SIP trunk.
        /// </summary>
        /// <param name="request">Trunk ID.</param>
        /// <returns>The deleted trunk.</returns>
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
        /// Creates a SIP dispatch rule, which decides the room an inbound call joins.
        /// </summary>
        /// <param name="request">Rule to create: the trunks it applies to, how calls are dispatched (a shared room or one room per caller), participant settings.</param>
        /// <returns>The created rule.</returns>
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
        /// Lists the SIP dispatch rules, optionally filtered.
        /// </summary>
        /// <param name="request">Optional filters: rule IDs, trunk IDs, page.</param>
        /// <returns>The matching rules.</returns>
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
        /// Deletes a SIP dispatch rule.
        /// </summary>
        /// <param name="request">Rule ID.</param>
        /// <returns>The deleted rule.</returns>
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
        /// Places an outbound call through a SIP trunk and adds the callee to a room as a SIP participant.
        /// </summary>
        /// <param name="request">Outbound call to place: trunk ID, number to call, room and participant identity, DTMF to dial, options.</param>
        /// <returns>The SIP participant placing the call.</returns>
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
        /// Transfers the call of a SIP participant to another phone number or SIP URI, with a SIP REFER.
        /// </summary>
        /// <param name="request">Room name, participant identity and the number or SIP URI to transfer the call to.</param>
        /// <returns>The outcome of the transfer.</returns>
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
        /// <param name="request">Trunk ID and the fields to change, or a full replacement.</param>
        /// <returns>The updated trunk.</returns>
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
        /// <param name="request">Trunk ID and the fields to change, or a full replacement.</param>
        /// <returns>The updated trunk.</returns>
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
        /// <param name="request">Rule ID and the fields to change, or a full replacement.</param>
        /// <returns>The updated rule.</returns>
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
