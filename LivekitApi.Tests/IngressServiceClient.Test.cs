using System.ComponentModel.DataAnnotations;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    [Collection("Integration tests")]
    public class IngressServiceClientTest : IAsyncLifetime
    {
        private ServiceClientFixture fixture;

        public IngressServiceClientTest(ServiceClientFixture fixture)
        {
            this.fixture = fixture;
        }

        private readonly IngressServiceClient ingressClient = new IngressServiceClient(
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
        public async void List_Ingress()
        {
            var response = await ingressClient.ListIngress(new ListIngressRequest());
            Assert.NotNull(response.Items);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Create_Ingress_Url()
        {
            var url =
                "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            IngressInfo ingress = await ingressClient.CreateIngress(
                new CreateIngressRequest
                {
                    RoomName = TestConstants.ROOM_NAME,
                    ParticipantIdentity = "ingress-participant",
                    ParticipantMetadata = "ingress-metadata",
                    ParticipantName = "ingress-name",
                    InputType = IngressInput.UrlInput,
                    Url = url,
                }
            );
            Assert.NotNull(ingress.IngressId);
            Assert.Equal(TestConstants.ROOM_NAME, ingress.RoomName);
            Assert.Equal("ingress-participant", ingress.ParticipantIdentity);
            Assert.Equal("ingress-metadata", ingress.ParticipantMetadata);
            Assert.Equal("ingress-name", ingress.ParticipantName);
            Assert.Equal(IngressInput.UrlInput, ingress.InputType);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Create_Ingress_Rtmp()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            IngressInfo ingress = await ingressClient.CreateIngress(
                new CreateIngressRequest
                {
                    RoomName = TestConstants.ROOM_NAME,
                    ParticipantIdentity = "ingress-participant",
                    ParticipantMetadata = "ingress-metadata",
                    ParticipantName = "ingress-name",
                    InputType = IngressInput.RtmpInput,
                    Video = new IngressVideoOptions
                    {
                        Preset = IngressVideoEncodingPreset.H2641080P30Fps3LayersHighMotion,
                    },
                }
            );
            Assert.NotNull(ingress.IngressId);
            Assert.Equal(TestConstants.ROOM_NAME, ingress.RoomName);
            Assert.Equal(IngressInput.RtmpInput, ingress.InputType);
            Assert.True(ingress.StreamKey.Length > 0);
            Assert.True(ingress.EnableTranscoding);
            Assert.Equal(
                IngressVideoEncodingPreset.H2641080P30Fps3LayersHighMotion,
                ingress.Video.Preset
            );
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Create_Ingress_Whip()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            IngressInfo ingress = await ingressClient.CreateIngress(
                new CreateIngressRequest
                {
                    RoomName = TestConstants.ROOM_NAME,
                    ParticipantIdentity = "ingress-participant",
                    ParticipantMetadata = "ingress-metadata",
                    ParticipantName = "ingress-name",
                    InputType = IngressInput.WhipInput,
                }
            );
            Assert.NotNull(ingress.IngressId);
            Assert.Equal(TestConstants.ROOM_NAME, ingress.RoomName);
            Assert.Equal(IngressInput.WhipInput, ingress.InputType);
            Assert.True(ingress.StreamKey.Length > 0);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Update_Ingress()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            IngressInfo ingress = await ingressClient.CreateIngress(
                new CreateIngressRequest
                {
                    RoomName = TestConstants.ROOM_NAME,
                    ParticipantIdentity = "ingress-participant",
                    ParticipantMetadata = "ingress-metadata",
                    ParticipantName = "ingress-name",
                    InputType = IngressInput.RtmpInput,
                }
            );
            IngressInfo updatedIngress = await ingressClient.UpdateIngress(
                new UpdateIngressRequest
                {
                    IngressId = ingress.IngressId,
                    ParticipantIdentity = "updated-ingress-participant",
                    ParticipantMetadata = "updated-ingress-metadata",
                    ParticipantName = "updated-ingress-name",
                }
            );
            Assert.Equal(ingress.IngressId, updatedIngress.IngressId);
            Assert.Equal(TestConstants.ROOM_NAME, updatedIngress.RoomName);
            Assert.Equal("updated-ingress-participant", updatedIngress.ParticipantIdentity);
            Assert.Equal("updated-ingress-metadata", updatedIngress.ParticipantMetadata);
            Assert.Equal("updated-ingress-name", updatedIngress.ParticipantName);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async void Delete_Ingress()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            IngressInfo ingress = await ingressClient.CreateIngress(
                new CreateIngressRequest
                {
                    RoomName = TestConstants.ROOM_NAME,
                    ParticipantIdentity = "ingress-participant",
                }
            );
            await ingressClient.DeleteIngress(
                new DeleteIngressRequest { IngressId = ingress.IngressId }
            );
            var response = await ingressClient.ListIngress(new ListIngressRequest());
            Assert.DoesNotContain(response.Items, i => i.IngressId == ingress.IngressId);
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        // After each test stop all ingresses
        public async Task DisposeAsync()
        {
            await ingressClient
                .ListIngress(new ListIngressRequest())
                .ContinueWith(async response =>
                {
                    foreach (var ingress in response.Result.Items)
                    {
                        await ingressClient.DeleteIngress(
                            new DeleteIngressRequest { IngressId = ingress.IngressId }
                        );
                    }
                });
        }
    }
}
