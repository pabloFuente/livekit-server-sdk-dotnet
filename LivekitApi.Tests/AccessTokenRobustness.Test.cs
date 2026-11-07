using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test
{
    /// <summary>
    /// Regression tests for the token builder under non-invariant cultures and for the
    /// LIVEKIT_API_KEY / LIVEKIT_API_SECRET fallback of the constructors.
    /// </summary>
    public class AccessTokenRobustnessTest
    {
        const string TEST_KEY = "API87mWmmh7KM3V";
        const string TEST_SECRET = "helOnxxeT71NeOEBcYm3kW0s1pofQAbitubCw7AIsY0A";

        private static JObject DecodePayload(string jwt)
        {
            var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
            return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part)));
        }

        private static T WithCulture<T>(string name, Func<T> action)
        {
            var original = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(name);
            try
            {
                return action();
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ToJwt_TimeClaims_AreWholeSeconds_UnderCommaDecimalCulture()
        {
            // es-ES formats 1.5 as "1,5": a fractional exp used to become an invalid claim
            var expiration = DateTime.UtcNow.AddMinutes(30).AddMilliseconds(123);
            var notBefore = DateTime.UtcNow.AddSeconds(-10).AddMilliseconds(456);
            var jwt = WithCulture(
                "es-ES",
                () =>
                    new AccessToken(TEST_KEY, TEST_SECRET)
                        .WithIdentity("identity")
                        .WithGrants(new VideoGrants { Room = "room", RoomJoin = true })
                        .WithExpiration(expiration)
                        .WithNotBefore(notBefore)
                        .ToJwt()
            );

            var payload = DecodePayload(jwt);
            foreach (var claim in new[] { "exp", "nbf", "iat" })
            {
                Assert.Equal(JTokenType.Integer, payload[claim]!.Type);
            }
            Assert.Equal(
                (long)Math.Floor((expiration - DateTime.UnixEpoch).TotalSeconds),
                payload["exp"]!.Value<long>()
            );
            Assert.Equal(
                (long)Math.Floor((notBefore - DateTime.UnixEpoch).TotalSeconds),
                payload["nbf"]!.Value<long>()
            );

            // And the token verifies, in the same culture
            var claims = WithCulture(
                "es-ES",
                () => new TokenVerifier(TEST_KEY, TEST_SECRET).Verify(jwt)
            );
            Assert.Equal("identity", claims.Identity);
            Assert.True(claims.Video.RoomJoin);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ToJwt_FractionalTtl_IsTruncatedToWholeSeconds()
        {
            var jwt = WithCulture(
                "de-DE",
                () =>
                    new AccessToken(TEST_KEY, TEST_SECRET)
                        .WithTtl(TimeSpan.FromMilliseconds(90500))
                        .WithGrants(new VideoGrants())
                        .ToJwt()
            );
            var payload = DecodePayload(jwt);
            Assert.Equal(JTokenType.Integer, payload["exp"]!.Type);
            Assert.Equal(90, payload["exp"]!.Value<long>() - payload["iat"]!.Value<long>());
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ToJwt_KindAndGrantKeys_AreLowerCasedInvariantly_UnderTurkishCulture()
        {
            // tr-TR lower-cases "I" to dotless "ı": "Ingress" and "IngressAdmin" used to be mangled
            var jwt = WithCulture(
                "tr-TR",
                () =>
                    new AccessToken(TEST_KEY, TEST_SECRET)
                        .WithIdentity("identity")
                        .WithKind(ParticipantInfo.Types.Kind.Ingress)
                        .WithGrants(new VideoGrants { IngressAdmin = true })
                        .ToJwt()
            );

            var payload = DecodePayload(jwt);
            Assert.Equal("ingress", payload["kind"]!.Value<string>());
            Assert.True(payload["video"]!["ingressAdmin"]!.Value<bool>());
            Assert.Null(payload["video"]!["ıngressAdmin"]);

            var claims = new TokenVerifier(TEST_KEY, TEST_SECRET).Verify(jwt);
            Assert.True(claims.Video.IngressAdmin);
            Assert.Equal("ingress", claims.Kind);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ToJwt_NullStringClaims_DoNotThrow_AndAreOmitted()
        {
            // Used to throw NullReferenceException from the claim type switch
            var jwt = new AccessToken(TEST_KEY, TEST_SECRET)
                .WithIdentity("identity")
                .WithName(null)
                .WithMetadata(null)
                .WithSha256(null)
                .WithGrants(new VideoGrants { Room = "room", RoomJoin = true })
                .ToJwt();

            var payload = DecodePayload(jwt);
            Assert.Equal("identity", payload["sub"]!.Value<string>());
            foreach (var claim in new[] { "name", "metadata", "sha256", "kind", "roomConfig" })
            {
                Assert.False(payload.ContainsKey(claim), $"{claim} should be omitted");
            }
            Assert.Equal("", new TokenVerifier(TEST_KEY, TEST_SECRET).Verify(jwt).Name);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ToJwt_OptionalClaims_ArePresentOnlyWhenSet()
        {
            var bare = DecodePayload(
                new AccessToken(TEST_KEY, TEST_SECRET).WithGrants(new VideoGrants()).ToJwt()
            );
            foreach (
                var claim in new[]
                {
                    "name",
                    "metadata",
                    "sha256",
                    "kind",
                    "roomPreset",
                    "roomConfig",
                }
            )
            {
                Assert.False(bare.ContainsKey(claim), $"{claim} should be omitted when unset");
            }
            Assert.True(bare.ContainsKey("video"));
            Assert.True(bare.ContainsKey("sip"));

            var fullJwt = new AccessToken(TEST_KEY, TEST_SECRET)
                .WithName("name")
                .WithMetadata("metadata")
                .WithSha256("sha")
                .WithKind(ParticipantInfo.Types.Kind.Agent)
                .WithRoomPreset("preset")
                .WithRoomConfig(new RoomConfiguration { MaxParticipants = 3 })
                .WithGrants(new VideoGrants())
                .ToJwt();
            var full = DecodePayload(fullJwt);
            Assert.Equal("name", full["name"]!.Value<string>());
            Assert.Equal("metadata", full["metadata"]!.Value<string>());
            Assert.Equal("sha", full["sha256"]!.Value<string>());
            Assert.Equal("agent", full["kind"]!.Value<string>());
            Assert.Equal(3, full["roomConfig"]!["maxParticipants"]!.Value<int>());
            Assert.Equal("preset", full["roomPreset"]!.Value<string>());
            Assert.Equal(
                "preset",
                new TokenVerifier(TEST_KEY, TEST_SECRET).Verify(fullJwt).RoomPreset
            );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Verify_ReadsBack_RoomConfigWithEnums_AndNewGrants()
        {
            var roomConfig = new RoomConfiguration
            {
                MaxParticipants = 5,
                Egress = new RoomEgress
                {
                    Room = new RoomCompositeEgressRequest
                    {
                        Preset = EncodingOptionsPreset.H2641080P30,
                    },
                },
            };
            var jwt = new AccessToken(TEST_KEY, TEST_SECRET)
                .WithIdentity("identity")
                .WithGrants(new VideoGrants { CanManageAgentSession = true })
                .WithInferenceGrants(new InferenceGrants { Perform = true })
                .WithObservabilityGrants(new ObservabilityGrants { Write = true })
                .WithRoomConfig(roomConfig)
                .ToJwt();

            var payload = DecodePayload(jwt);
            Assert.True(payload["video"]!["canManageAgentSession"]!.Value<bool>());
            Assert.True(payload["inference"]!["perform"]!.Value<bool>());
            Assert.True(payload["observability"]!["write"]!.Value<bool>());
            // Empty grant values are omitted, like the other SDKs
            Assert.Null(payload["video"]!["room"]);
            Assert.Null(payload["video"]!["canPublishSources"]);

            // Used to throw: roomConfig is written as protobuf JSON but was read with Newtonsoft
            var claims = new TokenVerifier(TEST_KEY, TEST_SECRET).Verify(jwt);
            Assert.Equal(roomConfig, claims.RoomConfig);
            Assert.True(claims.Video.CanManageAgentSession);
            Assert.True(claims.Inference!.Perform);
            Assert.True(claims.Observability!.Write);
            Assert.Null(
                new TokenVerifier(TEST_KEY, TEST_SECRET)
                    .Verify(
                        new AccessToken(TEST_KEY, TEST_SECRET).WithGrants(new VideoGrants()).ToJwt()
                    )
                    .Inference
            );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Verify_Tolerates_ArrayClaims()
        {
            // The JWT handler yields one Claim per array element; this used to throw on duplicates
            var token = new JwtSecurityToken(
                issuer: TEST_KEY,
                claims: new[] { new Claim("roles", "a"), new Claim("roles", "b") },
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(TEST_SECRET)
                    ),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
                )
            );
            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            Assert.NotNull(new TokenVerifier(TEST_KEY, TEST_SECRET).Verify(jwt));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WithExpiration_DateTimeOffset_IsNotReadAsLocalTime()
        {
            var expiration = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));
            var jwt = new AccessToken(TEST_KEY, TEST_SECRET)
                .WithGrants(new VideoGrants())
                .WithExpiration(expiration)
                .WithNotBefore(expiration.AddHours(-1))
                .ToJwt();
            var payload = DecodePayload(jwt);
            Assert.Equal(expiration.ToUnixTimeSeconds(), payload["exp"]!.Value<long>());
            Assert.Equal(
                expiration.AddHours(-1).ToUnixTimeSeconds(),
                payload["nbf"]!.Value<long>()
            );
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructors_FallBackToEnvironmentVariables_WhenArgumentsAreNull()
        {
            var previousKey = Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            var previousSecret = Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
            Environment.SetEnvironmentVariable("LIVEKIT_API_KEY", TEST_KEY);
            Environment.SetEnvironmentVariable("LIVEKIT_API_SECRET", TEST_SECRET);
            try
            {
                var jwt = new AccessToken()
                    .WithIdentity("identity")
                    .WithGrants(new VideoGrants { Room = "room", RoomJoin = true })
                    .ToJwt();
                Assert.Equal(TEST_KEY, new JwtSecurityTokenHandler().ReadJwtToken(jwt).Issuer);

                var claims = new TokenVerifier().Verify(jwt);
                Assert.Equal("identity", claims.Identity);

                // WebhookReceiver derives from TokenVerifier and used to throw here too
                Assert.NotNull(new WebhookReceiver());
            }
            finally
            {
                Environment.SetEnvironmentVariable("LIVEKIT_API_KEY", previousKey);
                Environment.SetEnvironmentVariable("LIVEKIT_API_SECRET", previousSecret);
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructors_StillReject_MissingCredentials()
        {
            var previousKey = Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            var previousSecret = Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
            Environment.SetEnvironmentVariable("LIVEKIT_API_KEY", null);
            Environment.SetEnvironmentVariable("LIVEKIT_API_SECRET", null);
            try
            {
                Assert.Throws<ArgumentException>(() => new AccessToken());
                Assert.Throws<ArgumentException>(() => new AccessToken(TEST_KEY, null));
                Assert.Throws<ArgumentException>(() => new TokenVerifier());
                Assert.Throws<ArgumentException>(() => new AccessToken(TEST_KEY, "too-short"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("LIVEKIT_API_KEY", previousKey);
                Environment.SetEnvironmentVariable("LIVEKIT_API_SECRET", previousSecret);
            }
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData(null, "my-room")]
        [InlineData("", "my-room")]
        [InlineData("alice", null)]
        [InlineData("alice", "")]
        public void ToJwt_RoomJoinWithoutIdentityOrRoom_Throws(string? identity, string? room)
        {
            var token = new AccessToken(TEST_KEY, TEST_SECRET).WithGrants(
                new VideoGrants { RoomJoin = true, Room = room }
            );
            if (identity != null)
            {
                token.WithIdentity(identity);
            }

            var ex = Assert.Throws<ArgumentException>(() => token.ToJwt());
            Assert.Equal("identity and room must be set when joining a room", ex.Message);
        }
    }
}
