using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Microsoft.IdentityModel.Tokens;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    public class WebhookReceiverTest
    {
        private const string TEST_API_KEY = "myapikey";
        private const string TEST_API_SECRET = "secretsecretsecretsecretsecretsecret";
        private const string TEST_EVENT =
            @"
    {
      ""event"": ""room_started"",
      ""room"": {
        ""sid"": ""RM_hycBMAjmt6Ub"",
        ""name"": ""Demo Room"",
        ""emptyTimeout"": 300,
        ""creationTime"": ""1692627281"",
        ""turnPassword"": ""2Pvdj+/WV1xV4EkB8klJ9xkXDWY="",
        ""enabledCodecs"": [
          {""mime"": ""audio/opus""},
          {""mime"": ""video/H264""},
          {""mime"": ""video/VP8""},
          {""mime"": ""video/AV1""},
          {""mime"": ""video/H264""},
          {""mime"": ""audio/red""},
          {""mime"": ""video/VP9""}
        ]
      },
      ""id"": ""EV_eugWmGhovZmm"",
      ""createdAt"": ""1692985556""
    }";

        [Fact]
        [Trait("Category", "Unit")]
        public void Test_WebhookReceiver()
        {
            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);
            var hash64 = ComputeBase64Sha256(TEST_EVENT);
            var token = new AccessToken(TEST_API_KEY, TEST_API_SECRET).WithSha256(hash64);
            var jwt = token.ToJwt();
            receiver.Receive(TEST_EVENT, jwt); // Should not throw exceptions if valid
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Bad_Hash()
        {
            var tokenVerifier = new TokenVerifier(TEST_API_KEY, TEST_API_SECRET);
            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);
            var hash64 = ComputeBase64Sha256("wrong_hash");
            var token = new AccessToken(TEST_API_KEY, TEST_API_SECRET).WithSha256(hash64);
            var jwt = token.ToJwt();
            var ex = Assert.Throws<Exception>(() => receiver.Receive(TEST_EVENT, jwt));
            Assert.Equal("Sha256 checksum of the body does not match", ex.Message);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Invalid_Body()
        {
            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);
            // Not a valid JSON object
            var body = "invalid body";
            var hash64 = ComputeBase64Sha256(body);
            var token = new AccessToken(TEST_API_KEY, TEST_API_SECRET).WithSha256(hash64);
            var jwt = token.ToJwt();
            Assert.Throws<InvalidJsonException>(() => receiver.Receive(body, jwt));
            // Unknown fields are ignored by default (a newer server may add fields)...
            body = "{\"event\": \"room_started\", \"id\": \"EV_1\", \"brandNewField\": \"x\"}";
            hash64 = ComputeBase64Sha256(body);
            token = new AccessToken(TEST_API_KEY, TEST_API_SECRET).WithSha256(hash64);
            jwt = token.ToJwt();
            Assert.Equal("room_started", receiver.Receive(body, jwt).Event);
            // ...unless asked otherwise
            Assert.Throws<InvalidProtocolBufferException>(() =>
                receiver.Receive(body, jwt, ignoreUnknownFields: false)
            );
            // The raw bytes can be passed too (that is what the server hashes)
            Assert.Equal(
                "EV_1",
                receiver.Receive(System.Text.Encoding.UTF8.GetBytes(body), jwt).Id
            );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Receive()
        {
            var hash64 = ComputeBase64Sha256(TEST_EVENT);
            var token = new AccessToken(TEST_API_KEY, TEST_API_SECRET).WithSha256(hash64);
            var jwt = token.ToJwt();

            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);

            var eventReceived = receiver.Receive(TEST_EVENT, jwt);

            Assert.Equal("Demo Room", eventReceived.Room.Name);
            Assert.Equal("room_started", eventReceived.Event);
            Assert.Equal("RM_hycBMAjmt6Ub", eventReceived.Room.Sid);
            Assert.Equal("EV_eugWmGhovZmm", eventReceived.Id);
            Assert.Equal(1692985556, eventReceived.CreatedAt);
            Assert.Equal("2Pvdj+/WV1xV4EkB8klJ9xkXDWY=", eventReceived.Room.TurnPassword);
            Assert.Equal(1692627281, eventReceived.Room.CreationTime);
            Assert.Equal(7, eventReceived.Room.EnabledCodecs.Count);
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData(null)]
        [InlineData("")]
        public void MissingAuthorizationHeader_IsRejected(string? authHeader)
        {
            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);

            // An unsigned request never reaches the parser, whichever overload receives it
            var ex = Assert.Throws<Exception>(() => receiver.Receive(TEST_EVENT, authHeader));
            Assert.Equal("Authorization header is empty", ex.Message);
            ex = Assert.Throws<Exception>(() =>
                receiver.Receive(Encoding.UTF8.GetBytes(TEST_EVENT), authHeader)
            );
            Assert.Equal("Authorization header is empty", ex.Message);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SkipAuth_ParsesAnUnsignedBody()
        {
            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);

            Assert.Equal("EV_eugWmGhovZmm", receiver.Receive(TEST_EVENT, skipAuth: true).Id);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void NullBody_IsRejected()
        {
            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);

            Assert.Throws<ArgumentNullException>(() => receiver.Receive((string)null!, "jwt"));
            Assert.Throws<ArgumentNullException>(() => receiver.Receive((byte[])null!, "jwt"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExpiredToken_IsRejected_UnlessWithinTheLeeway()
        {
            var jwt = new AccessToken(TEST_API_KEY, TEST_API_SECRET)
                .WithSha256(ComputeBase64Sha256(TEST_EVENT))
                .WithNotBefore(DateTimeOffset.UtcNow.AddMinutes(-10))
                .WithExpiration(DateTimeOffset.UtcNow.AddMinutes(-2))
                .ToJwt();

            // Expired two minutes ago: beyond the default leeway of one minute...
            var receiver = new WebhookReceiver(TEST_API_KEY, TEST_API_SECRET);
            Assert.Throws<SecurityTokenExpiredException>(() => receiver.Receive(TEST_EVENT, jwt));
            // ...but within a leeway of five
            var tolerant = new WebhookReceiver(
                TEST_API_KEY,
                TEST_API_SECRET,
                TimeSpan.FromMinutes(5)
            );
            Assert.Equal("room_started", tolerant.Receive(TEST_EVENT, jwt).Event);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SecretShorterThan256Bits_IsRejected()
        {
            var shortSecret = new string('s', 31);

            Assert.Throws<ArgumentException>(() => new WebhookReceiver(TEST_API_KEY, shortSecret));
            Assert.Throws<ArgumentException>(() => new TokenVerifier(TEST_API_KEY, shortSecret));
        }

        private string ComputeBase64Sha256(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.Default.GetBytes(input));
                return Convert.ToBase64String(hash);
            }
        }
    }
}
