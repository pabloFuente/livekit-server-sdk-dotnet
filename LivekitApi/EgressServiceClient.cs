using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// A client for interacting with the Egress service.
    /// See: <see href="https://docs.livekit.io/realtime/egress/overview/">Egress</see>
    /// </summary>
    public class EgressServiceClient : BaseService
    {
        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="host">URL of the LiveKit server, <c>http(s)://</c> or <c>ws(s)://</c>; a subpath such as <c>https://host/livekit</c> is kept.</param>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="client">Optional <see cref="HttpClient"/> to send the requests with. The SDK never modifies it, so it can be shared with other clients and code.</param>
        public EgressServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        /// <summary>
        /// Starts a room composite egress: records or streams the whole room, rendered by a web template.
        /// </summary>
        /// <param name="request">Room name, layout, template and the file, stream, segment or image outputs.</param>
        /// <returns>The started egress, with its ID and status.</returns>
        public async Task<EgressInfo> StartRoomCompositeEgress(RoomCompositeEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/StartRoomCompositeEgress",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a web egress: records or streams an arbitrary web page.
        /// </summary>
        /// <param name="request">URL to render and the outputs.</param>
        /// <returns>The started egress, with its ID and status.</returns>
        public async Task<EgressInfo> StartWebEgress(WebEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/StartWebEgress",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a participant egress: records or streams the tracks of one participant.
        /// </summary>
        /// <param name="request">Room name, participant identity and the outputs.</param>
        /// <returns>The started egress, with its ID and status.</returns>
        public async Task<EgressInfo> StartParticipantEgress(ParticipantEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/StartParticipantEgress",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a track composite egress: records or streams one audio and one video track mixed together.
        /// </summary>
        /// <param name="request">Room name, the audio and video track SIDs and the outputs.</param>
        /// <returns>The started egress, with its ID and status.</returns>
        public async Task<EgressInfo> StartTrackCompositeEgress(TrackCompositeEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/StartTrackCompositeEgress",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a track egress: exports a single track to a file or to a WebSocket.
        /// </summary>
        /// <param name="request">Room name, track SID and the file or WebSocket output.</param>
        /// <returns>The started egress, with its ID and status.</returns>
        public async Task<EgressInfo> StartTrackEgress(TrackEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/StartTrackEgress",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Start any type of egress. The request's source and outputs determine the egress type.
        /// </summary>
        /// <param name="request">Which kind of egress to start, with its own options.</param>
        /// <returns>The started egress, with its ID and status.</returns>
        public async Task<EgressInfo> StartEgress(StartEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/StartEgress",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the layout of a running room composite egress.
        /// </summary>
        /// <param name="request">Egress ID and the new layout.</param>
        /// <returns>The updated egress.</returns>
        public async Task<EgressInfo> UpdateLayout(UpdateLayoutRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/UpdateLayout",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds or removes stream destinations (RTMP, SRT) of a running stream egress.
        /// </summary>
        /// <param name="request">Egress ID and the stream URLs to add or remove.</param>
        /// <returns>The updated egress.</returns>
        public async Task<EgressInfo> UpdateStream(UpdateStreamRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/UpdateStream",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Lists the egresses, optionally filtered by room, egress ID or active state.
        /// </summary>
        /// <param name="request">Optional filters: room name, egress ID, active egresses only.</param>
        /// <returns>The matching egresses.</returns>
        public async Task<ListEgressResponse> ListEgress(ListEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/ListEgress",
                    request,
                    ListEgressResponse.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Stops a running egress.
        /// </summary>
        /// <param name="request">Egress ID.</param>
        /// <returns>The stopped egress.</returns>
        public async Task<EgressInfo> StopEgress(StopEgressRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.Egress/StopEgress",
                    request,
                    EgressInfo.Parser,
                    AuthHeader(new VideoGrants { RoomRecord = true })
                )
                .ConfigureAwait(false);
        }
    }
}
