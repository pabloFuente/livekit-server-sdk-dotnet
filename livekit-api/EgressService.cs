using System.Net.Http.Headers;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet;

public class EgressService : BaseService
{
    public EgressService(string host, string apiKey, string secret)
        : base(host, apiKey, secret) { }

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
