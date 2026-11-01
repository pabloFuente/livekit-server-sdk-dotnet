using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    // The Connector service is only available in LiveKit Cloud, so these tests use a mocked
    // HttpClient instead of the integration test containers.
    public class ConnectorServiceClientTest
    {
        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("DialWhatsAppCall")]
        [InlineData("DisconnectWhatsAppCall")]
        [InlineData("ConnectWhatsAppCall")]
        [InlineData("AcceptWhatsAppCall")]
        [InlineData("ConnectTwilioCall")]
        public async Task Method_CallsConnectorRoute_WithRoomCreateGrant(string method)
        {
            var handler = new TestHttpMessageHandler();
            var service = new ConnectorServiceClient(
                "http://localhost",
                "key",
                "secretsecretsecretsecretsecretsecret",
                new HttpClient(handler)
            );

            Task call = method switch
            {
                "DialWhatsAppCall" => service.DialWhatsAppCall(new DialWhatsAppCallRequest()),
                "DisconnectWhatsAppCall" => service.DisconnectWhatsAppCall(
                    new DisconnectWhatsAppCallRequest()
                ),
                "ConnectWhatsAppCall" => service.ConnectWhatsAppCall(
                    new ConnectWhatsAppCallRequest()
                ),
                "AcceptWhatsAppCall" => service.AcceptWhatsAppCall(new AcceptWhatsAppCallRequest()),
                "ConnectTwilioCall" => service.ConnectTwilioCall(new ConnectTwilioCallRequest()),
                _ => throw new ArgumentException("Unknown method " + method),
            };
            await call;

            Assert.NotNull(handler.LastRequest?.RequestUri);
            Assert.Equal(
                "/twirp/livekit.Connector/" + method,
                handler.LastRequest.RequestUri.AbsolutePath
            );
            Assert.True(handler.LastVideoGrant().SelectToken("roomCreate")?.Value<bool>());
        }
    }
}
