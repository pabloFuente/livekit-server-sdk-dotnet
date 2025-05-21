namespace Livekit.Server.Sdk.Dotnet.Test
{
    [Collection("Integration tests")]
    public class EgressServiceClientTest : IAsyncLifetime
    {
        private ServiceClientFixture fixture;

        public EgressServiceClientTest(ServiceClientFixture fixture)
        {
            this.fixture = fixture;
        }

        private readonly EgressServiceClient egressClient = new EgressServiceClient(
            ServiceClientFixture.TEST_HTTP_URL,
            ServiceClientFixture.TEST_API_KEY,
            ServiceClientFixture.TEST_API_SECRET
        );
        private readonly RoomServiceClient roomClient = new RoomServiceClient(
            ServiceClientFixture.TEST_HTTP_URL,
            ServiceClientFixture.TEST_API_KEY,
            ServiceClientFixture.TEST_API_SECRET
        );

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task List_Egress()
        {
            var response = await egressClient.ListEgress(new ListEgressRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task Start_RoomComposite_Egress()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            await fixture.PublishVideoTrackInRoom(
                roomClient,
                TestConstants.ROOM_NAME,
                TestConstants.PARTICIPANT_IDENTITY
            );
            var request = new RoomCompositeEgressRequest { RoomName = TestConstants.ROOM_NAME };
            request.FileOutputs.Add(
                new EncodedFileOutput
                {
                    FileType = EncodedFileType.Mp4,
                    Filepath = "/home/egress/" + new Random().Next(0, int.MaxValue) + "-test.mp4",
                }
            );
            var egress = await egressClient.StartRoomCompositeEgress(request);
            await WaitUntilEgressIsActive(egress);
            Assert.Equal(TestConstants.ROOM_NAME, egress.RoomName);
            Assert.Single(egress.FileResults);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task Start_TrackComposite_Egress()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            await fixture.PublishVideoTrackInRoom(
                roomClient,
                TestConstants.ROOM_NAME,
                TestConstants.PARTICIPANT_IDENTITY
            );
            var participant = await roomClient.GetParticipant(
                new RoomParticipantIdentity
                {
                    Room = TestConstants.ROOM_NAME,
                    Identity = TestConstants.PARTICIPANT_IDENTITY,
                }
            );
            var videoTrack = participant
                .Tracks.Where(t => t.Type == TrackType.Video)
                .FirstOrDefault();
            Assert.NotNull(videoTrack);
            var request = new TrackCompositeEgressRequest
            {
                RoomName = TestConstants.ROOM_NAME,
                VideoTrackId = videoTrack.Sid,
                FileOutputs =
                {
                    new EncodedFileOutput
                    {
                        FileType = EncodedFileType.Mp4,
                        Filepath =
                            "/home/egress/" + new Random().Next(0, int.MaxValue) + "-test.mp4",
                    },
                },
            };
            var egress = await egressClient.StartTrackCompositeEgress(request);
            await WaitUntilEgressIsActive(egress);
            Assert.Equal(TestConstants.ROOM_NAME, egress.RoomName);
            Assert.Single(egress.FileResults);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task Start_Participant_Egress()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            await fixture.PublishVideoTrackInRoom(
                roomClient,
                TestConstants.ROOM_NAME,
                TestConstants.PARTICIPANT_IDENTITY
            );
            var request = new ParticipantEgressRequest
            {
                RoomName = TestConstants.ROOM_NAME,
                Identity = TestConstants.PARTICIPANT_IDENTITY,
                FileOutputs =
                {
                    new EncodedFileOutput
                    {
                        FileType = EncodedFileType.Mp4,
                        Filepath =
                            "/home/egress/" + new Random().Next(0, int.MaxValue) + "-test.mp4",
                    },
                },
            };
            var egress = await egressClient.StartParticipantEgress(request);
            await WaitUntilEgressIsActive(egress);
            Assert.Equal(TestConstants.ROOM_NAME, egress.RoomName);
            Assert.Single(egress.FileResults);
            Assert.Equal(TestConstants.PARTICIPANT_IDENTITY, egress.Participant.Identity);
            Assert.Single(egress.Participant.FileOutputs);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task Start_Track_Egress()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            await fixture.PublishVideoTrackInRoom(
                roomClient,
                TestConstants.ROOM_NAME,
                TestConstants.PARTICIPANT_IDENTITY
            );
            var participant = await roomClient.GetParticipant(
                new RoomParticipantIdentity
                {
                    Room = TestConstants.ROOM_NAME,
                    Identity = TestConstants.PARTICIPANT_IDENTITY,
                }
            );
            var videoTrack = participant
                .Tracks.Where(t => t.Type == TrackType.Video)
                .FirstOrDefault();
            Assert.NotNull(videoTrack);
            var request = new TrackEgressRequest
            {
                RoomName = TestConstants.ROOM_NAME,
                TrackId = videoTrack.Sid,
                File = new DirectFileOutput
                {
                    Filepath =
                        "/home/egress/"
                        + new Random().Next(0, int.MaxValue)
                        + "-{room_name}/{track_id}",
                },
            };
            var egress = await egressClient.StartTrackEgress(request);
            await WaitUntilEgressIsActive(egress);
            Assert.Equal(egress.Track.TrackId, videoTrack.Sid);
            Assert.Equal(TestConstants.ROOM_NAME, egress.Track.RoomName);
            Assert.Equal(TestConstants.ROOM_NAME, egress.RoomName);
            Assert.Single(egress.FileResults);
            Assert.Contains("{room_name}/{track_id}", egress.Track.File.Filepath);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task Start_Web_Egress()
        {
            var request = new WebEgressRequest
            {
                Url = "https://google.com",
                VideoOnly = true,
                FileOutputs =
                {
                    new EncodedFileOutput
                    {
                        FileType = EncodedFileType.Mp4,
                        Filepath =
                            "/home/egress/" + new Random().Next(0, int.MaxValue) + "-test.mp4",
                    },
                },
            };
            var egress = await egressClient.StartWebEgress(request);
            await WaitUntilEgressIsActive(egress);
            Assert.Single(egress.FileResults);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task Update_Layout()
        {
            await fixture.PublishVideoTrackInRoom(
                roomClient,
                TestConstants.ROOM_NAME,
                TestConstants.PARTICIPANT_IDENTITY
            );
            var request = new RoomCompositeEgressRequest { RoomName = TestConstants.ROOM_NAME };
            request.FileOutputs.Add(
                new EncodedFileOutput
                {
                    FileType = EncodedFileType.Mp4,
                    Filepath = "/home/egress/" + new Random().Next(0, int.MaxValue) + "-test.mp4",
                }
            );
            var egress = await egressClient.StartRoomCompositeEgress(request);
            Assert.Equal("", egress.RoomComposite.Layout);
            await WaitUntilEgressIsActive(egress);
            var newLayout = "single-speaker-light";
            var updateRequest = new UpdateLayoutRequest
            {
                EgressId = egress.EgressId,
                Layout = newLayout,
            };
            var updatedEgress = await egressClient.UpdateLayout(updateRequest);
            Assert.NotNull(updatedEgress);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Category", "EgressService")]
        public async Task Stop_Egress()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            var request = new RoomCompositeEgressRequest { RoomName = TestConstants.ROOM_NAME };
            request.FileOutputs.Add(
                new EncodedFileOutput
                {
                    FileType = EncodedFileType.Mp4,
                    Filepath = "/home/egress/" + new Random().Next(0, int.MaxValue) + "-test.mp4",
                }
            );
            var egress = await egressClient.StartRoomCompositeEgress(request);
            var stopRequest = new StopEgressRequest { EgressId = egress.EgressId };
            var response = await egressClient.StopEgress(stopRequest);
            Assert.NotNull(response);
        }

        private async Task WaitUntilEgressIsActive(EgressInfo egress)
        {
            var timeout = DateTime.Now.AddSeconds(100);
            while (
                egress != null
                && egress.Status != EgressStatus.EgressActive
                && DateTime.Now < timeout
            )
            {
                var egresses = await egressClient.ListEgress(
                    new ListEgressRequest
                    {
                        RoomName = TestConstants.ROOM_NAME,
                        EgressId = egress.EgressId,
                    }
                );
                egress = egresses.Items.FirstOrDefault()!;
                await Task.Delay(700);
            }
            Assert.NotNull(egress);
            Assert.Equal(EgressStatus.EgressActive, egress.Status);
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        // After each test delete all rooms and stop all egresses
        public async Task DisposeAsync()
        {
            var timeout = DateTime.Now.AddSeconds(60);
            var activeRooms = (await roomClient.ListRooms(new ListRoomsRequest())).Rooms;
            while (activeRooms.Count > 0 && DateTime.Now < timeout)
            {
                foreach (var room in activeRooms)
                {
                    await roomClient.DeleteRoom(new DeleteRoomRequest { Room = room.Name });
                }
                await Task.Delay(700);
                activeRooms = (await roomClient.ListRooms(new ListRoomsRequest())).Rooms;
            }
            if (DateTime.Now >= timeout)
            {
                Assert.Fail("Timeout waiting for rooms to be deleted");
            }
            timeout = DateTime.Now.AddSeconds(60);
            var activeEgresses = (await egressClient.ListEgress(new ListEgressRequest())).Items;
            while (
                activeEgresses.Any(eg =>
                    eg.Status == EgressStatus.EgressStarting
                    || eg.Status == EgressStatus.EgressActive
                )
                && DateTime.Now < timeout
            )
            {
                foreach (var egress in activeEgresses)
                {
                    if (
                        egress.Status == EgressStatus.EgressStarting
                        || egress.Status == EgressStatus.EgressActive
                    )
                    {
                        try
                        {
                            await egressClient.StopEgress(
                                new StopEgressRequest { EgressId = egress.EgressId }
                            );
                        }
                        catch (Exception) { }
                    }
                }
                await Task.Delay(700);
                activeEgresses = (await egressClient.ListEgress(new ListEgressRequest())).Items;
            }
            if (DateTime.Now >= timeout)
            {
                Assert.Fail("Timeout waiting for egresses to stop");
            }
        }
    }
}
