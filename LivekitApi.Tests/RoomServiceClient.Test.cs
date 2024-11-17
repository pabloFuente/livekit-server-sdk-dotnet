using LiveKit.Proto;
using Xunit.Abstractions;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    public class RoomServiceClientTest(ServiceClientFixture fixture, ITestOutputHelper output)
        : IClassFixture<ServiceClientFixture>
    {
        RoomServiceClient client = new RoomServiceClient(
            "http://localhost:7880",
            ServiceClientFixture.TEST_API_KEY,
            ServiceClientFixture.TEST_API_SECRET
        );
        const string ROOM_NAME = "test-room";
        const string ROOM_METADATA = "room-metadata";
        const string PARTICIPANT_IDENTITY = "test-participant";

        [Fact]
        [Trait("Category", "Integration")]
        public async void Create_Room()
        {
            var request = new CreateRoomRequest { Name = ROOM_NAME, Metadata = ROOM_METADATA };
            var room = await client.CreateRoom(request);
            Assert.NotNull(room);
            Assert.NotEmpty(room.Sid);
            Assert.Equal(ROOM_NAME, room.Name);
            Assert.Equal(ROOM_METADATA, room.Metadata);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void List_Rooms()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            var request = new ListRoomsRequest();
            var response = await client.ListRooms(request);
            Assert.NotNull(response);
            Assert.NotEmpty(response.Rooms);
            Assert.Contains(response.Rooms, r => r.Name == ROOM_NAME);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Delete_Room()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            await client.DeleteRoom(new DeleteRoomRequest { Room = ROOM_NAME });
            var rooms = await client.ListRooms(new ListRoomsRequest());
            Assert.NotNull(rooms.Rooms);
            Assert.DoesNotContain(rooms.Rooms, r => r.Name == ROOM_NAME);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Update_RoomMetadata()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            var request = new UpdateRoomMetadataRequest
            {
                Room = ROOM_NAME,
                Metadata = "new-metadata",
            };
            Room room = await client.UpdateRoomMetadata(request);
            Assert.NotNull(room);
            Assert.Equal("new-metadata", room.Metadata);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void List_Participants()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            var request = new ListParticipantsRequest { Room = ROOM_NAME };
            var response = await client.ListParticipants(request);
            Assert.NotNull(response);
            Assert.Empty(response.Participants);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Get_Participant()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            await ServiceClientFixture.JoinParticipant(ROOM_NAME, PARTICIPANT_IDENTITY);
            ParticipantInfo participant = await client.GetParticipant(
                new RoomParticipantIdentity { Room = ROOM_NAME, Identity = PARTICIPANT_IDENTITY }
            );
            Assert.NotNull(participant);
            Assert.Equal(PARTICIPANT_IDENTITY, participant.Identity);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Update_Participant()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            await ServiceClientFixture.JoinParticipant(ROOM_NAME, PARTICIPANT_IDENTITY);
            ParticipantInfo participant = await client.GetParticipant(
                new RoomParticipantIdentity { Room = ROOM_NAME, Identity = PARTICIPANT_IDENTITY }
            );
            Assert.True(participant.Permission.CanPublish);
            Assert.True(participant.Permission.CanSubscribe);
            Assert.False(participant.Permission.CanUpdateMetadata);
            var updateParticipantRequest = new UpdateParticipantRequest
            {
                Room = ROOM_NAME,
                Identity = PARTICIPANT_IDENTITY,
                Name = "new-name",
                Metadata = "new-metadata",
                Permission = new ParticipantPermission
                {
                    CanPublish = false,
                    CanSubscribe = false,
                    CanUpdateMetadata = true,
                },
            };
            participant = await client.UpdateParticipant(updateParticipantRequest);
            Assert.NotNull(participant);
            Assert.Equal(PARTICIPANT_IDENTITY, participant.Identity);
            Assert.Equal("new-name", participant.Name);
            Assert.Equal("new-metadata", participant.Metadata);
            Assert.False(participant.Permission.CanPublish);
            Assert.False(participant.Permission.CanSubscribe);
            Assert.True(participant.Permission.CanUpdateMetadata);
        }
    }
}
