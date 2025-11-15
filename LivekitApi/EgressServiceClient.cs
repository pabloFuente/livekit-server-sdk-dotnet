using System.Net.Http;
using System.Net.Http.Headers;
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
            string apiKey,
            string apiSecret,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        public async Task<EgressInfo> StartRoomCompositeEgress(RoomCompositeEgressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.StartRoomCompositeEgress(httpClient, request);
        }

        public async Task<EgressInfo> StartWebEgress(WebEgressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.StartWebEgress(httpClient, request);
        }

        public async Task<EgressInfo> StartParticipantEgress(ParticipantEgressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.StartParticipantEgress(httpClient, request);
        }

        public async Task<EgressInfo> StartTrackCompositeEgress(TrackCompositeEgressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.StartTrackCompositeEgress(httpClient, request);
        }

        public async Task<EgressInfo> StartTrackEgress(TrackEgressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.StartTrackEgress(httpClient, request);
        }

        public async Task<EgressInfo> UpdateLayout(UpdateLayoutRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.UpdateLayout(httpClient, request);
        }

        public async Task<EgressInfo> UpdateStream(UpdateStreamRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.UpdateStream(httpClient, request);
        }

        public async Task<ListEgressResponse> ListEgress(ListEgressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.ListEgress(httpClient, request);
        }

        public async Task<EgressInfo> StopEgress(StopEgressRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomRecord = true })
            );
            return await Twirp.StopEgress(httpClient, request);
        }
    }
}
