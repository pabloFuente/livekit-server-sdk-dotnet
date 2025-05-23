using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Google.Protobuf;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// Access tokens are required to connect to the server.
    /// Once information is filled out, create the token string with <see cref="ToJwt"/>.
    /// <see href="https://docs.livekit.io/guides/access-tokens"/>
    /// </summary>
    public class AccessToken
    {
        private readonly string apiKey;
        private readonly string apiSecret;
        public ClaimsModel Claims { get; private set; }
        private TimeSpan ttl = Constants.DefaultTtl;
        private DateTime expiration;
        private DateTime notBefore;

        /// <summary>
        /// Constructs a new AccessToken
        /// </summary>
        /// <param name="apiKey">LiveKit API key</param>
        /// <param name="apiSecret">LiveKit API secret</param>
        public AccessToken(string apiKey = null, string apiSecret = null)
        {
            this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            this.apiSecret = apiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                throw new ArgumentException("apiKey and apiSecret must be set");
            }
            if (Encoding.Default.GetBytes(apiSecret).Length < 32)
            {
                throw new ArgumentException(
                    "apiSecret must be at least 256 bits long. Currently it is "
                        + Encoding.Default.GetBytes(apiSecret).Length * 8
                        + " bits long"
                );
            }

            Claims = new ClaimsModel();
        }

        /// <summary>
        /// Amount of time the created token is valid for.
        /// Defaults to 6 hours.
        /// </summary>
        public AccessToken WithTtl(TimeSpan ttl)
        {
            this.ttl = ttl;
            return this;
        }

        /// <summary>
        /// Used to specify an expiration time.
        /// If set, takes preference over the value set with <see cref="WithTtl"/>.
        /// </summary>
        public AccessToken WithExpiration(DateTime expiration)
        {
            this.expiration = expiration;
            return this;
        }

        /// <summary>
        /// Date specifying the time before which this token is invalid.
        /// If not set, the token is valid immediately after creation.
        /// <see href="https://tools.ietf.org/html/draft-ietf-oauth-json-web-token-25#section-4.1.5"/>
        /// </summary>
        public AccessToken WithNotBefore(DateTime notBefore)
        {
            this.notBefore = notBefore;
            return this;
        }

        /// <summary>
        /// Adds <see cref="VideoGrants"/> to this token.
        /// </summary>
        public AccessToken WithGrants(VideoGrants grants)
        {
            Claims.Video = grants;
            return this;
        }

        /// <summary>
        /// Adds <see cref="SIPGrants"/> to this token.
        /// </summary>
        public AccessToken WithSipGrants(SIPGrants grants)
        {
            Claims.Sip = grants;
            return this;
        }

        /// <summary>
        /// Unique identity of the user, required for room join tokens.
        /// </summary>
        public AccessToken WithIdentity(string identity)
        {
            Claims.Identity = identity;
            return this;
        }

        /// <summary>
        /// Kind of participant (standard, egress, ingress, agent, sip)
        /// Allows further identifying participants of custom services.
        /// </summary>
        public AccessToken WithKind(ParticipantInfo.Types.Kind kind)
        {
            Claims.Kind = kind.ToString().ToLower();
            return this;
        }

        /// <summary>
        /// Display name for the participant, available as <code>Participant.name</code>.
        /// </summary>
        public AccessToken WithName(string name)
        {
            Claims.Name = name;
            return this;
        }

        /// <summary>
        /// Custom metadata to be passed to participants.
        /// </summary>
        public AccessToken WithMetadata(string metadata)
        {
            Claims.Metadata = metadata;
            return this;
        }

        /// <summary>
        /// Key/value attributes to attach to the participant.
        /// </summary>
        public AccessToken WithAttributes(Dictionary<string, string> attributes)
        {
            Claims.Attributes = attributes;
            return this;
        }

        /// <summary>
        /// For verifying the integrity of the message body.
        /// </summary>
        public AccessToken WithSha256(string sha256)
        {
            Claims.Sha256 = sha256;
            return this;
        }

        public AccessToken WithRoomPreset(string roomPreset)
        {
            Claims.RoomPreset = roomPreset;
            return this;
        }

        public AccessToken WithRoomConfig(RoomConfiguration roomConfig)
        {
            Claims.RoomConfig = roomConfig;
            return this;
        }

        /// <summary>
        /// Generates the JWT token as a string.
        /// </summary>
        public string ToJwt()
        {
            var video = Claims.Video;
            if (
                video.RoomJoin
                && (string.IsNullOrEmpty(Claims.Identity) || string.IsNullOrEmpty(video.Room))
            )
            {
                throw new ArgumentException("identity and room must be set when joining a room");
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var initUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var exp =
                expiration == DateTime.MinValue
                    ? now + ttl.TotalSeconds
                    : expiration.ToUniversalTime().Subtract(initUtc).TotalSeconds;

            var nbf =
                notBefore == DateTime.MinValue
                    ? now
                    : notBefore.ToUniversalTime().Subtract(initUtc).TotalSeconds;

            var jwtClaims = new Dictionary<string, object>
            {
                { "sub", Claims.Identity },
                { "jti", Claims.Identity },
                { "iss", apiKey },
                { "nbf", nbf },
                { "iat", now },
                { "exp", exp },
            };

            jwtClaims["video"] = ConvertClaimsKeysToCamelCase(Claims.Video);
            jwtClaims["sip"] = ConvertClaimsKeysToCamelCase(Claims.Sip);
            jwtClaims["name"] = Claims.Name;
            jwtClaims["metadata"] = Claims.Metadata;
            jwtClaims["sha256"] = Claims.Sha256;
            jwtClaims["kind"] = Claims.Kind;

            if (Claims.Attributes != null && Claims.Attributes.Count > 0)
            {
                jwtClaims["attributes"] = Claims.Attributes;
            }
            if (!string.IsNullOrEmpty(Claims.RoomPreset))
            {
                jwtClaims["roomPreset"] = Claims.RoomPreset;
            }
            if (Claims.RoomConfig != null)
            {
                jwtClaims["roomConfig"] = Claims.RoomConfig;
            }

            List<Claim> claims = new List<Claim>();
            foreach (var kv in jwtClaims)
            {
                switch (kv.Value)
                {
                    case string val:
                        claims.Add(new Claim(kv.Key, val));
                        break;
                    case int val:
                        claims.Add(new Claim(kv.Key, val.ToString(), ClaimValueTypes.Integer64));
                        break;
                    case long val:
                        claims.Add(new Claim(kv.Key, val.ToString(), ClaimValueTypes.Integer64));
                        break;
                    case double val:
                        claims.Add(new Claim(kv.Key, val.ToString(), ClaimValueTypes.Double));
                        break;
                    case bool val:
                        claims.Add(new Claim(kv.Key, val.ToString(), ClaimValueTypes.Boolean));
                        break;
                    case Dictionary<string, object> val:
                        var jsonString = JsonConvert.SerializeObject(kv.Value);
                        claims.Add(new Claim(kv.Key, jsonString, JsonClaimValueTypes.Json));
                        break;
                    case Dictionary<string, string> val:
                        var jsonString2 = JsonConvert.SerializeObject(kv.Value);
                        claims.Add(new Claim(kv.Key, jsonString2, JsonClaimValueTypes.Json));
                        break;
                    default:
                        try
                        {
                            // Try protobuf formatter
                            var jsonString3 = JsonFormatter.Default.Format(kv.Value as IMessage);
                            claims.Add(new Claim(kv.Key, jsonString3, JsonClaimValueTypes.Json));
                            break;
                        }
                        catch (Exception)
                        {
                            throw new ArgumentException(
                                $"unsupported claim type {kv.Value.GetType()}"
                            );
                        }
                }
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(claims: claims, signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static Dictionary<string, object> ConvertClaimsKeysToCamelCase(object obj)
        {
            return obj.GetType()
                .GetProperties()
                .ToDictionary(prop => PascalToCamelCase(prop.Name), prop => prop.GetValue(obj));
        }

        private static string PascalToCamelCase(string str)
        {
            return char.ToLower(str[0]) + str.Substring(1);
        }
    }

    /// <summary>
    /// Helper to verify the integrity of a token.
    /// </summary>
    public class TokenVerifier
    {
        private readonly string apiKey;
        private readonly string apiSecret;
        private readonly TimeSpan leeway;

        public TokenVerifier(string apiKey = null, string apiSecret = null, TimeSpan? leeway = null)
        {
            this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            this.apiSecret = apiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
            this.leeway = leeway ?? Constants.DefaultLeeway;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                throw new ArgumentException("apiKey and apiSecret must be set");
            }
            if (Encoding.Default.GetBytes(apiSecret).Length < 32)
            {
                throw new ArgumentException(
                    "apiSecret must be at least 256 bits long. Currently it is "
                        + Encoding.Default.GetBytes(apiSecret).Length * 8
                        + " bits long"
                );
            }
        }

        public ClaimsModel Verify(string token)
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = apiKey,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret)),
                ValidateLifetime = true,
                ValidateAudience = false,
                ClockSkew = leeway,
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(
                token,
                validationParameters,
                out var securityToken
            );
            var claims = principal.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
            var decodedSecurityToken = (JwtSecurityToken)securityToken;

            var video = new VideoGrants();
            var exists = claims.TryGetValue("video", out var videoClaim);
            if (exists && videoClaim != null)
            {
                video = JsonConvert.DeserializeObject<VideoGrants>(videoClaim);
            }

            var sip = claims.TryGetValue("sip", out var sipClaim)
                ? JsonConvert.DeserializeObject<SIPGrants>(sipClaim)
                : new SIPGrants();

            var claimsModel = new ClaimsModel
            {
                Identity = decodedSecurityToken.Subject != null ? decodedSecurityToken.Subject : "",
                Name = claims.TryGetValue("name", out var name) ? name : "",
                Video = video,
                Sip = sip,
                Metadata = claims.TryGetValue("metadata", out var metadata) ? metadata : "",
                Sha256 = claims.TryGetValue("sha256", out var sha256) ? sha256 : "",
                Kind = claims.TryGetValue("kind", out var kind) ? kind : "",
                Attributes = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                    claims.TryGetValue("attributes", out var attributes) ? attributes : "{}"
                ),
            };

            if (claims.TryGetValue("roomPreset", out var roomPreset))
            {
                claimsModel.RoomPreset = roomPreset;
            }
            if (claims.TryGetValue("roomConfig", out var roomConfig))
            {
                claimsModel.RoomConfig = JsonConvert.DeserializeObject<RoomConfiguration>(
                    roomConfig
                );
            }

            return claimsModel;
        }
    }
}
