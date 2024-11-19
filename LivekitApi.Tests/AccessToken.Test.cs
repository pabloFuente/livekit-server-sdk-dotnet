using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    public class AccessTokenTest
    {
        const string TEST_KEY = "API87mWmmh7KM3V";
        const string TEST_SECRET = "helOnxxeT71NeOEBcYm3kW0s1pofQAbitubCw7AIsY0A";
        private TokenValidationParameters validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.Default.GetBytes(TEST_SECRET)),
            ValidateIssuer = false,
            ValidateAudience = false,
        };

        [Fact]
        [Trait("Category", "Unit")]
        public void Generates_Valid_JWT_With_Defaults()
        {
            var token = new AccessToken(TEST_KEY, TEST_SECRET);
            token.WithGrants(new VideoGrants());
            var jwt = token.ToJwt();
            new JwtSecurityTokenHandler().ValidateToken(
                jwt,
                validationParameters,
                out var validatedToken
            );
            Assert.Equal(TEST_KEY, validatedToken.Issuer);
            Assert.True(validatedToken.ValidFrom < DateTime.UtcNow);
            Assert.True(validatedToken.ValidTo - validatedToken.ValidFrom == Constants.DefaultTtl);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Generates_Valid_Payload_From_Grants()
        {
            var token = new AccessToken(TEST_KEY, TEST_SECRET);
            token
                .WithGrants(
                    new VideoGrants
                    {
                        RoomCreate = true,
                        RoomRecord = true,
                        CanUpdateOwnMetadata = false,
                    }
                )
                .WithSipGrants(new SIPGrants { Call = true, Admin = false });

            string jwt = token.ToJwt();
            new JwtSecurityTokenHandler().ValidateToken(
                jwt,
                validationParameters,
                out var validatedToken
            );
            var decoded = (JwtSecurityToken)validatedToken;

            var videoGrant = JObject.Parse(decoded.Payload["video"].ToString());
            Assert.True(videoGrant.SelectToken("roomCreate").Value<bool>());
            Assert.True(videoGrant.SelectToken("roomRecord").Value<bool>());
            Assert.False(videoGrant.SelectToken("canUpdateOwnMetadata").Value<bool>());
            var sipGrant = JObject.Parse(decoded.Payload["sip"].ToString());
            Assert.True(sipGrant.SelectToken("call").Value<bool>());
            Assert.False(sipGrant.SelectToken("admin").Value<bool>());
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Create_Token()
        {
            var expirationTime = new DateTime(3023, 10, 15);
            var token = new AccessToken(TEST_KEY, TEST_SECRET)
                .WithTtl(expirationTime - DateTime.UtcNow)
                .WithName("name")
                .WithIdentity("identity")
                .WithMetadata("metadata")
                .WithSha256("gfedcba");
            token.WithAttributes(new Dictionary<string, string> { { "key", "value" } });
            token.WithGrants(
                new VideoGrants
                {
                    Room = "room_name",
                    CanPublishSources = new List<string> { "camera", "microphone" },
                }
            );
            token.WithSipGrants(new SIPGrants { Admin = true });

            var jwt = token.ToJwt();

            var principal = new JwtSecurityTokenHandler().ValidateToken(
                jwt,
                validationParameters,
                out var validatedToken
            );
            var jwtToken = (JwtSecurityToken)validatedToken;
            var claims = jwtToken.Claims;

            var tokenVerifier = new TokenVerifier(TEST_KEY, TEST_SECRET);
            var claimsModel = tokenVerifier.Verify(jwt);

            Assert.Equal(
                TEST_KEY,
                claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Iss)?.Value
            );
            Assert.Equal(claimsModel.Name, claims.FirstOrDefault(c => c.Type == "name")?.Value);
            Assert.Equal(
                claimsModel.Identity,
                claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value
            );
            Assert.Equal(
                claimsModel.Identity,
                claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
            );
            Assert.Equal(
                claimsModel.Metadata,
                claims.FirstOrDefault(c => c.Type == "metadata")?.Value
            );
            Assert.Equal(claimsModel.Sha256, claims.FirstOrDefault(c => c.Type == "sha256")?.Value);
            Assert.Equal(expirationTime, jwtToken.ValidTo.AddSeconds(1));

            var attributes = claims.FirstOrDefault(c => c.Type == "attributes")?.Value;
            Assert.NotNull(attributes);
            Assert.Contains("value", attributes);

            var videoGrants = claims.FirstOrDefault(c => c.Type == "video")?.Value;
            Assert.NotNull(videoGrants);
            Assert.Contains("room_name", videoGrants);
            Assert.Contains("camera", videoGrants);
            Assert.Contains("microphone", videoGrants);

            var sipGrants = claims.FirstOrDefault(c => c.Type == "sip")?.Value;
            Assert.NotNull(sipGrants);
            Assert.Contains("admin", sipGrants);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Encodes_Join_Tokens()
        {
            var token = new AccessToken(TEST_KEY, TEST_SECRET)
                .WithIdentity("test_identity")
                .WithTtl(TimeSpan.FromMinutes(1))
                .WithName("myname")
                .WithKind(AccessToken.ParticipantKind.Standard)
                .WithGrants(
                    new VideoGrants
                    {
                        RoomJoin = true,
                        Room = "myroom",
                        CanPublish = false,
                    }
                );
            string jwt = token.ToJwt();
            new JwtSecurityTokenHandler().ValidateToken(
                jwt,
                validationParameters,
                out var validatedToken
            );
            var decoded = (JwtSecurityToken)validatedToken;

            Assert.Equal(TEST_KEY, decoded.Issuer);
            Assert.Equal("test_identity", decoded.Subject);
            Assert.Equal("test_identity", decoded.Id);
            Assert.Equal("myname", decoded.Payload["name"]);
            Assert.Equal("standard", decoded.Payload["kind"]);
            var videoGrant = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                decoded.Payload["video"].ToString()
            );
            Assert.True((bool)videoGrant["roomJoin"]);
            Assert.Equal("myroom", videoGrant["room"]);
            Assert.False((bool)videoGrant["canPublish"]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HandlesAgentDispatch()
        {
            var token = new AccessToken(TEST_KEY, TEST_SECRET)
                .WithIdentity("test_identity")
                .WithName("myname")
                .WithGrants(
                    new VideoGrants
                    {
                        RoomJoin = true,
                        Room = "myroom",
                        CanPublish = false,
                    }
                );

            var roomConfig = new RoomConfiguration { MaxParticipants = 10 };
            roomConfig.Agents.Add(
                new RoomAgentDispatch { AgentName = "test-agent", Metadata = "test-metadata" }
            );
            token.WithRoomConfig(roomConfig);

            var jwt = token.ToJwt();
            var verifier = new TokenVerifier(TEST_KEY, TEST_SECRET);
            var grant = verifier.Verify(jwt);

            Assert.Equal("myroom", grant.Video.Room);
            Assert.Equal("test-agent", grant.RoomConfig.Agents[0].AgentName);
            Assert.Equal("test-metadata", grant.RoomConfig.Agents[0].Metadata);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Verify_ValidToken_ReturnsClaimsModel()
        {
            var tokenVerifier = new TokenVerifier(TEST_KEY, TEST_SECRET);
            var token = GenerateTestToken(TEST_KEY);
            var claimsModel = tokenVerifier.Verify(token);
            Assert.NotNull(claimsModel);
            Assert.Equal("testIdentity", claimsModel.Identity);
            Assert.Equal("testName", claimsModel.Name);
            Assert.Equal("testMetadata", claimsModel.Metadata);
            Assert.Equal("testSha256", claimsModel.Sha256);
            Assert.Equal("testValue", claimsModel.Attributes["testKey"]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Verify_InvalidToken_ThrowsException()
        {
            var tokenVerifier = new TokenVerifier(TEST_KEY, TEST_SECRET);
            // Wrong token
            var invalidToken = "invalidToken";
            Assert.Throws<SecurityTokenMalformedException>(
                () => tokenVerifier.Verify(invalidToken)
            );
            // Wrong api key
            invalidToken = GenerateTestToken("invalid_key");
            Assert.Throws<SecurityTokenInvalidIssuerException>(
                () => tokenVerifier.Verify(invalidToken)
            );
            // Expired
            invalidToken = GenerateTestToken(TEST_KEY, DateTime.UtcNow.AddMinutes(-1));
            Assert.Throws<SecurityTokenExpiredException>(() => tokenVerifier.Verify(invalidToken));
        }

        private string GenerateTestToken(string apiKey, DateTime? expires = null)
        {
            var claims = new List<Claim>
            {
                new Claim("sub", "testIdentity"),
                new Claim("iss", apiKey),
                new Claim("name", "testName"),
                new Claim("metadata", "testMetadata"),
                new Claim("sha256", "testSha256"),
                new Claim(
                    "attributes",
                    JsonConvert.SerializeObject(
                        new Dictionary<string, string> { { "testKey", "testValue" } }
                    )
                ),
            };
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TEST_SECRET));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: apiKey,
                claims: claims,
                expires: expires == null ? DateTime.UtcNow.AddHours(1) : expires,
                signingCredentials: credentials
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
