namespace Livekit.Server.Sdk.Dotnet.Test
{
    [Collection("Integration tests")]
    public class AgentDispatchServiceClientTest
    {
        private readonly AgentDispatchServiceClient agentDispatchClient =
            new AgentDispatchServiceClient(
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
        [Trait("Category", "AgentDispatchService")]
        public async Task Get_Dispatch()
        {
            await roomClient.CreateRoom(new CreateRoomRequest { Name = TestConstants.ROOM_NAME });
            try
            {
                var created = await agentDispatchClient.CreateDispatch(
                    new CreateAgentDispatchRequest
                    {
                        AgentName = "test-agent",
                        Room = TestConstants.ROOM_NAME,
                        Metadata = "dispatch-metadata",
                    }
                );

                var dispatch = await agentDispatchClient.GetDispatch(
                    created.Id,
                    TestConstants.ROOM_NAME
                );
                Assert.NotNull(dispatch);
                Assert.Equal(created.Id, dispatch.Id);
                Assert.Equal("test-agent", dispatch.AgentName);
                Assert.Equal("dispatch-metadata", dispatch.Metadata);

                Assert.Null(
                    await agentDispatchClient.GetDispatch("AD_unknown", TestConstants.ROOM_NAME)
                );
            }
            finally
            {
                await roomClient.DeleteRoom(
                    new DeleteRoomRequest { Room = TestConstants.ROOM_NAME }
                );
            }
        }
    }
}
