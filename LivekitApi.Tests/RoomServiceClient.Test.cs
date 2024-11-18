using Google.Protobuf;
using LiveKit.Proto;
using Xunit.Abstractions;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    public class RoomServiceClientTest(ServiceClientFixture fixture, ITestOutputHelper output)
        : IClassFixture<ServiceClientFixture>,
            IDisposable
    {
        RoomServiceClient client = new RoomServiceClient(
            "http://localhost:7880",
            ServiceClientFixture.TEST_API_KEY,
            ServiceClientFixture.TEST_API_SECRET
        );
        const string ROOM_NAME = "test-room";
        const string ROOM_METADATA = "room-metadata";
        const string PARTICIPANT_IDENTITY = "test-participant";

        // Clean all rooms after each test
        public void Dispose()
        {
            client
                .ListRooms(new ListRoomsRequest())
                .ContinueWith(async response =>
                {
                    foreach (var room in response.Result.Rooms)
                    {
                        await client.DeleteRoom(new DeleteRoomRequest { Room = room.Name });
                    }
                })
                .Wait();
        }

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
        public async void Remove_Participant()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            await ServiceClientFixture.JoinParticipant(ROOM_NAME, PARTICIPANT_IDENTITY);
            var participants = await client.ListParticipants(
                new ListParticipantsRequest { Room = ROOM_NAME }
            );
            Assert.Single(participants.Participants);
            var request = new RoomParticipantIdentity
            {
                Room = ROOM_NAME,
                Identity = PARTICIPANT_IDENTITY,
            };
            await client.RemoveParticipant(request);
            participants = await client.ListParticipants(
                new ListParticipantsRequest { Room = ROOM_NAME }
            );
            Assert.Empty(participants.Participants);
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

        [Fact]
        [Trait("Category", "Integration")]
        public async void Mute_Published_Track()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            await ServiceClientFixture.PublishVideoTrackInRoom(ROOM_NAME, PARTICIPANT_IDENTITY);
            ParticipantInfo participant = null;
            // Wait for participant to have tracks
            var timeout = DateTime.Now.AddSeconds(10);
            while ((participant == null || participant.Tracks.Count == 0) && DateTime.Now < timeout)
            {
                participant = await client.GetParticipant(
                    new RoomParticipantIdentity
                    {
                        Room = ROOM_NAME,
                        Identity = PARTICIPANT_IDENTITY,
                    }
                );
                if (participant.Tracks.Count == 0)
                {
                    await Task.Delay(500);
                }
            }
            if (participant.Tracks.Count == 0)
            {
                Assert.Fail("Participant has no tracks");
            }
            Assert.NotNull(participant);
            Assert.Single(participant.Tracks);
            Assert.False(participant.Tracks[0].Muted);
            var mutePublishedTrackRequest = new MuteRoomTrackRequest
            {
                Room = ROOM_NAME,
                Identity = PARTICIPANT_IDENTITY,
                TrackSid = participant.Tracks[0].Sid,
                Muted = true,
            };
            MuteRoomTrackResponse mutedPublishedTrack = await client.MutePublishedTrack(
                mutePublishedTrackRequest
            );
            Assert.NotNull(mutedPublishedTrack);
            Assert.True(mutedPublishedTrack.Track.Muted);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Update_Subscriptions()
        {
            string publisherIdentity = PARTICIPANT_IDENTITY + "2";
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            await Task.WhenAll(
                ServiceClientFixture.JoinParticipant(ROOM_NAME, PARTICIPANT_IDENTITY),
                ServiceClientFixture.PublishVideoTrackInRoom(ROOM_NAME, publisherIdentity)
            );
            var participants = await Task.WhenAll(
                client.GetParticipant(
                    new RoomParticipantIdentity
                    {
                        Room = ROOM_NAME,
                        Identity = PARTICIPANT_IDENTITY,
                    }
                ),
                client.GetParticipant(
                    new RoomParticipantIdentity { Room = ROOM_NAME, Identity = publisherIdentity }
                )
            );
            var subscriber = participants[0];
            var publisher = participants[1];

            Assert.Single(publisher.Tracks);
            // Subscribe to track
            var updateSubscriptionsRequest = new UpdateSubscriptionsRequest
            {
                Room = ROOM_NAME,
                Identity = subscriber.Identity,
                Subscribe = true,
            };
            updateSubscriptionsRequest.TrackSids.Add(publisher.Tracks[0].Sid);
            await client.UpdateSubscriptions(updateSubscriptionsRequest);
            // Unsubscribe from track
            updateSubscriptionsRequest.Subscribe = false;
            updateSubscriptionsRequest.TrackSids.Add(publisher.Tracks[0].Sid);
            await client.UpdateSubscriptions(updateSubscriptionsRequest);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Send_Data()
        {
            await client.CreateRoom(new CreateRoomRequest { Name = ROOM_NAME });
            var sendDataRequest = new SendDataRequest
            {
                Room = ROOM_NAME,
                Data = ByteString.CopyFromUtf8("test-data"),
                Kind = DataPacket.Types.Kind.Reliable,
            };
            var response = await client.SendData(sendDataRequest);
            Assert.NotNull(response);
        }
    }
}
