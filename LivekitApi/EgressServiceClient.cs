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
        /// A client for interacting with the Egress service.
        /// See: <see href="https://docs.livekit.io/realtime/egress/overview/">Egress</see>
        /// </summary>
        public EgressServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

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
