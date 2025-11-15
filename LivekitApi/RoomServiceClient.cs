using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Protobuf;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// A client for interacting and managing LiveKit rooms and participants.
    /// </summary>
    /// <remarks>
    /// See: <see href="https://docs.livekit.io/realtime/server/managing-rooms/">Managing Rooms</see>
    /// </remarks>
    public class RoomServiceClient : BaseService
    {
        /// <summary>
        /// A client for interacting managing LiveKit rooms and participants.
        /// <see href="https://docs.livekit.io/realtime/server/managing-rooms/" />
        /// </summary>
        public RoomServiceClient(
            string host,
            string apiKey,
            string apiSecret,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        /// <summary>
        /// Creates a new room. Explicit room creation is not required, since rooms will
        /// be automatically created when the first participant joins. This method can be
        /// used to customize room settings.
        /// </summary>
        public async Task<Room> CreateRoom(CreateRoomRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomCreate = true })
            );
            return await Twirp.CreateRoom(httpClient, request);
        }

        /// <summary>
        /// Lists active rooms.
        /// </summary>
        public async Task<ListRoomsResponse> ListRooms(ListRoomsRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomList = true })
            );
            return await Twirp.ListRooms(httpClient, request);
        }

        /// <summary>
        /// Deletes a room.
        /// </summary>
        public async Task<DeleteRoomResponse> DeleteRoom(DeleteRoomRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomCreate = true })
            );
            return await Twirp.DeleteRoom(httpClient, request);
        }

        /// <summary>
        /// Lists participants in a room.
        /// </summary>
        public async Task<ListParticipantsResponse> ListParticipants(
            ListParticipantsRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.ListParticipants(httpClient, request);
        }

        /// <summary>
        /// Gets information on a specific participant, including the tracks that the participant has published.
        /// </summary>
        public async Task<ParticipantInfo> GetParticipant(RoomParticipantIdentity request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.GetParticipant(httpClient, request);
        }

        /// <summary>
        /// Removes a participant from a room. This will disconnect the participant
        /// and emit a Disconnected event for them. They can re-join the room later.
        /// </summary>
        public async Task<RemoveParticipantResponse> RemoveParticipant(
            RoomParticipantIdentity request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.RemoveParticipant(httpClient, request);
        }

        /// <summary>
        /// Mutes a track that a participant has published.
        /// </summary>
        public async Task<MuteRoomTrackResponse> MutePublishedTrack(MuteRoomTrackRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.MutePublishedTrack(httpClient, request);
        }

        /// <summary>
        /// Updates a participant's metadata or permissions.
        /// </summary>
        public async Task<ParticipantInfo> UpdateParticipant(UpdateParticipantRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.UpdateParticipant(httpClient, request);
        }

        /// <summary>
        /// Updates a participant's subscription to tracks.
        /// </summary>
        public async Task<UpdateSubscriptionsResponse> UpdateSubscriptions(
            UpdateSubscriptionsRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.UpdateSubscriptions(httpClient, request);
        }

        /// <summary>
        /// Sends a data message to participants in the room.
        /// </summary>
        public async Task<SendDataResponse> SendData(SendDataRequest request)
        {
            // Add random nonce to request
            request.Nonce = ByteString.CopyFrom(System.Guid.NewGuid().ToByteArray());
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.SendData(httpClient, request);
        }

        /// <summary>
        /// Updates metadata of a room.
        /// </summary>
        public async Task<Room> UpdateRoomMetadata(UpdateRoomMetadataRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
            );
            return await Twirp.UpdateRoomMetadata(httpClient, request);
        }

        /// <summary>
        /// Forwards a participant's track(s) to another room. Requires <c>RoomAdmin</c> and <c>DestinationRoom</c>.
        /// This will create a participant to join the destination room that has same information with the source participant
        /// except the kind to be <c>Forwarded</c>. All changes to the source participant will be reflected to the forwarded
        /// participant. When the source participant disconnects or the <c>RemoveParticipant</c> method is called in the
        /// destination room, the forwarding will be stopped. A participant can be forwarded to multiple rooms. The destination
        /// room will be created if it does not exist.
        /// </summary>
        public async Task<ForwardParticipantResponse> ForwardParticipant(
            ForwardParticipantRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(
                    new VideoGrants
                    {
                        RoomAdmin = true,
                        Room = request.Room,
                        DestinationRoom = request.DestinationRoom,
                    }
                )
            );
            return await Twirp.ForwardParticipant(httpClient, request);
        }

        /// <summary>
        /// Move a connected participant to a different room. Requires <c>RoomAdmin</c> and <c>DestinationRoom</c>.
        /// The participant will be removed from the current room and added to the destination room.
        /// From other observers' perspective, the participant would've disconnected from the previous room and joined the new one.
        /// </summary>
        public async Task<MoveParticipantResponse> MoveParticipant(MoveParticipantRequest request)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(
                    new VideoGrants
                    {
                        RoomAdmin = true,
                        Room = request.Room,
                        DestinationRoom = request.DestinationRoom,
                    }
                )
            );
            return await Twirp.MoveParticipant(httpClient, request);
        }
    }
}
