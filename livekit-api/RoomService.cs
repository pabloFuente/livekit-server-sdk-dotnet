using System.Net.Http.Headers;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet;

/// <summary>
/// A client for interacting managing LiveKit rooms and participants.
/// <see href="https://docs.livekit.io/realtime/server/managing-rooms/" />
/// </summary>
public class RoomService : BaseService
{
    /// <summary>
    /// A client for interacting managing LiveKit rooms and participants.
    /// <see href="https://docs.livekit.io/realtime/server/managing-rooms/" />
    /// </summary>
    public RoomService(string host, string apiKey, string secret) : base(host, apiKey, secret)
    {
    }

    /// <summary>
    /// Creates a new room. Explicit room creation is not required, since rooms will be automatically
    /// created when the first participant joins. This method can be used to customize room settings.
    /// </summary>
    public async Task<Room> CreateRoom(CreateRoomRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomCreate = true }));
        return await Twirp.CreateRoom(httpClient, request);
    }

    /// <summary>
    /// List active rooms
    /// </summary>
    public async Task<ListRoomsResponse> ListRooms(ListRoomsRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomList = true }));
        return await Twirp.ListRooms(httpClient, request);
    }

    public async Task<DeleteRoomResponse> DeleteRoom(DeleteRoomRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomCreate = true }));
        return await Twirp.DeleteRoom(httpClient, request);
    }

    public async Task<ListParticipantsResponse> ListParticipants(ListParticipantsRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.ListParticipants(httpClient, request);
    }

    public async Task<ParticipantInfo> GetParticipant(RoomParticipantIdentity request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.GetParticipant(httpClient, request);
    }

    public async Task<RemoveParticipantResponse> RemoveParticipant(RoomParticipantIdentity request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.RemoveParticipant(httpClient, request);
    }

    public async Task<MuteRoomTrackResponse> MutePublishedTrack(MuteRoomTrackRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.MutePublishedTrack(httpClient, request);
    }

    public async Task<ParticipantInfo> UpdateParticipant(UpdateParticipantRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.UpdateParticipant(httpClient, request);
    }

    public async Task<UpdateSubscriptionsResponse> UpdateSubscriptions(UpdateSubscriptionsRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.UpdateSubscriptions(httpClient, request);
    }

    public async Task<SendDataResponse> SendData(SendDataRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.SendData(httpClient, request);
    }

    public async Task<Room> UpdateRoomMetadata(UpdateRoomMetadataRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room }));
        return await Twirp.UpdateRoomMetadata(httpClient, request);
    }
}