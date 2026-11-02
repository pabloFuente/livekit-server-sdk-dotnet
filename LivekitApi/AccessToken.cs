using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// See <see href="https://docs.livekit.io/guides/access-tokens">Access tokens</see>.
    /// </summary>
    public class AccessToken
    {
        private readonly string apiKey;
        private readonly string apiSecret;

        /// <summary>
        /// The claims of the token (identity, name, metadata, grants...), filled by the <c>With*</c> methods.
        /// </summary>
        public ClaimsModel Claims { get; private set; }
        private TimeSpan ttl = Constants.DefaultTtl;
        private DateTime expiration;
        private DateTime notBefore;

        /// <summary>
        /// Constructs a new access token.
        /// </summary>
        /// <param name="apiKey">LiveKit API key.</param>
        /// <param name="apiSecret">LiveKit API secret.</param>
        public AccessToken(string apiKey = null, string apiSecret = null)
        {
            this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            this.apiSecret = apiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");

            if (string.IsNullOrEmpty(this.apiKey) || string.IsNullOrEmpty(this.apiSecret))
            {
                throw new ArgumentException("apiKey and apiSecret must be set");
            }
            if (Encoding.UTF8.GetBytes(this.apiSecret).Length < 32)
            {
                throw new ArgumentException(
                    "apiSecret must be at least 256 bits long. Currently it is "
                        + Encoding.UTF8.GetBytes(this.apiSecret).Length * 8
                        + " bits long"
                );
            }

            Claims = new ClaimsModel();
        }

        /// <summary>
        /// Amount of time the created token is valid for.
        /// Defaults to 6 hours.
        /// </summary>
        /// <param name="ttl">How long the token stays valid after it is generated.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithTtl(TimeSpan ttl)
        {
            this.ttl = ttl;
            return this;
        }

        /// <summary>
        /// Used to specify an expiration time.
        /// If set, takes preference over the value set with <see cref="WithTtl"/>.
        /// </summary>
        /// <param name="expiration">Absolute expiration time. A value of kind <c>Unspecified</c> is read as local time.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithExpiration(DateTime expiration)
        {
            this.expiration = expiration;
            return this;
        }

        /// <summary>
        /// Used to specify an expiration time, with an explicit offset (a <see cref="DateTime"/>
        /// of kind <c>Unspecified</c> would be read as local time).
        /// If set, takes preference over the value set with <see cref="WithTtl"/>.
        /// </summary>
        /// <param name="expiration">Absolute expiration time.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithExpiration(DateTimeOffset expiration)
        {
            this.expiration = expiration.UtcDateTime;
            return this;
        }

        /// <summary>
        /// Date specifying the time before which this token is invalid.
        /// If not set, the token is valid immediately after creation.
        /// See <see href="https://tools.ietf.org/html/draft-ietf-oauth-json-web-token-25#section-4.1.5">the JWT specification</see>.
        /// </summary>
        /// <param name="notBefore">Time before which the token is not valid. A value of kind <c>Unspecified</c> is read as local time.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithNotBefore(DateTime notBefore)
        {
            this.notBefore = notBefore;
            return this;
        }

        /// <summary>
        /// Date specifying the time before which this token is invalid, with an explicit offset
        /// (a <see cref="DateTime"/> of kind <c>Unspecified</c> would be read as local time).
        /// </summary>
        /// <param name="notBefore">Time before which the token is not valid.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithNotBefore(DateTimeOffset notBefore)
        {
            this.notBefore = notBefore.UtcDateTime;
            return this;
        }

        /// <summary>
        /// Adds <see cref="VideoGrants"/> to this token.
        /// </summary>
        /// <param name="grants">Room permissions of the token holder: joining, publishing, subscribing, room administration...</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithGrants(VideoGrants grants)
        {
            Claims.Video = grants;
            return this;
        }

        /// <summary>
        /// Adds <see cref="SIPGrants"/> to this token.
        /// </summary>
        /// <param name="grants">SIP permissions: administering trunks and dispatch rules (<c>Admin</c>) or making calls (<c>Call</c>).</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithSipGrants(SIPGrants grants)
        {
            Claims.Sip = grants;
            return this;
        }

        /// <summary>
        /// Adds <see cref="InferenceGrants"/> to this token.
        /// </summary>
        /// <param name="grants">Inference permissions.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithInferenceGrants(InferenceGrants grants)
        {
            Claims.Inference = grants;
            return this;
        }

        /// <summary>
        /// Adds <see cref="ObservabilityGrants"/> to this token.
        /// </summary>
        /// <param name="grants">Observability permissions.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithObservabilityGrants(ObservabilityGrants grants)
        {
            Claims.Observability = grants;
            return this;
        }

        /// <summary>
        /// Unique identity of the user, required for room join tokens.
        /// </summary>
        /// <param name="identity">Identity of the participant, unique within the room.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithIdentity(string identity)
        {
            Claims.Identity = identity;
            return this;
        }

        /// <summary>
        /// Kind of participant (standard, egress, ingress, agent, sip)
        /// Allows further identifying participants of custom services.
        /// </summary>
        /// <param name="kind">Kind of participant: standard, egress, ingress, agent or SIP.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithKind(ParticipantInfo.Types.Kind kind)
        {
            Claims.Kind = kind.ToString().ToLowerInvariant();
            return this;
        }

        /// <summary>
        /// Display name for the participant, available as <c>Participant.name</c>.
        /// </summary>
        /// <param name="name">Display name shown to the other participants.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithName(string name)
        {
            Claims.Name = name;
            return this;
        }

        /// <summary>
        /// Custom metadata to be passed to participants.
        /// </summary>
        /// <param name="metadata">Free-form metadata attached to the participant and visible to the others.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithMetadata(string metadata)
        {
            Claims.Metadata = metadata;
            return this;
        }

        /// <summary>
        /// Key/value attributes to attach to the participant.
        /// </summary>
        /// <param name="attributes">Key/value attributes attached to the participant.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithAttributes(Dictionary<string, string> attributes)
        {
            Claims.Attributes = attributes;
            return this;
        }

        /// <summary>
        /// For verifying the integrity of the message body.
        /// </summary>
        /// <param name="sha256">Base64 SHA-256 of the message body the token authenticates (used by webhooks).</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithSha256(string sha256)
        {
            Claims.Sha256 = sha256;
            return this;
        }

        /// <summary>
        /// Name of the room configuration preset to apply when this token creates the room.
        /// </summary>
        /// <param name="roomPreset">Name of the room configuration preset.</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithRoomPreset(string roomPreset)
        {
            Claims.RoomPreset = roomPreset;
            return this;
        }

        /// <summary>
        /// Room configuration (agents to dispatch, egress, limits...) applied when this token creates the room.
        /// </summary>
        /// <param name="roomConfig">Room configuration: agents to dispatch, egress, participant limits...</param>
        /// <returns>This token, so calls can be chained.</returns>
        public AccessToken WithRoomConfig(RoomConfiguration roomConfig)
        {
            Claims.RoomConfig = roomConfig;
            return this;
        }

        /// <summary>
        /// Generates the JWT token as a string.
        /// </summary>
        /// <returns>The signed JWT.</returns>
        public string ToJwt()
        {
            var video = Claims.Video;
            if (
                video != null
                && video.RoomJoin
                && (string.IsNullOrEmpty(Claims.Identity) || string.IsNullOrEmpty(video.Room))
            )
            {
                throw new ArgumentException("identity and room must be set when joining a room");
            }

            // Whole seconds, like the other LiveKit SDKs: the JWT spec defines these claims as
            // numeric dates, and a fractional value would be formatted with the current culture.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var exp =
                expiration == DateTime.MinValue
                    ? now + (long)ttl.TotalSeconds
                    : ToUnixSeconds(expiration);

            var nbf = notBefore == DateTime.MinValue ? now : ToUnixSeconds(notBefore);

            var jwtClaims = new Dictionary<string, object>
            {
                { "sub", Claims.Identity ?? "" },
                { "jti", Claims.Identity ?? "" },
                { "iss", apiKey },
                { "nbf", nbf },
                { "iat", now },
                { "exp", exp },
            };

            jwtClaims["video"] = ConvertClaimsKeysToCamelCase(Claims.Video ?? new VideoGrants());
            jwtClaims["sip"] = ConvertClaimsKeysToCamelCase(Claims.Sip ?? new SIPGrants());
            if (Claims.Inference != null)
            {
                jwtClaims["inference"] = ConvertClaimsKeysToCamelCase(Claims.Inference);
            }
            if (Claims.Observability != null)
            {
                jwtClaims["observability"] = ConvertClaimsKeysToCamelCase(Claims.Observability);
            }

            // Optional claims are only written when set, like the other LiveKit SDKs do (the
            // server marks them omitempty). A null value used to throw a NullReferenceException.
            if (!string.IsNullOrEmpty(Claims.Name))
            {
                jwtClaims["name"] = Claims.Name;
            }
            if (!string.IsNullOrEmpty(Claims.Metadata))
            {
                jwtClaims["metadata"] = Claims.Metadata;
            }
            if (!string.IsNullOrEmpty(Claims.Sha256))
            {
                jwtClaims["sha256"] = Claims.Sha256;
            }
            if (!string.IsNullOrEmpty(Claims.Kind))
            {
                jwtClaims["kind"] = Claims.Kind;
            }
            if (Claims.Attributes != null && Claims.Attributes.Count > 0)
            {
                jwtClaims["attributes"] = Claims.Attributes;
            }
            if (!string.IsNullOrEmpty(Claims.RoomPreset))
            {
                jwtClaims["roomPreset"] = Claims.RoomPreset;
            }
            if (Claims.RoomConfig != null && Claims.RoomConfig.CalculateSize() > 0)
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
                        claims.Add(
                            new Claim(
                                kv.Key,
                                val.ToString(CultureInfo.InvariantCulture),
                                ClaimValueTypes.Integer64
                            )
                        );
                        break;
                    case long val:
                        claims.Add(
                            new Claim(
                                kv.Key,
                                val.ToString(CultureInfo.InvariantCulture),
                                ClaimValueTypes.Integer64
                            )
                        );
                        break;
                    case double val:
                        claims.Add(
                            new Claim(
                                kv.Key,
                                val.ToString(CultureInfo.InvariantCulture),
                                ClaimValueTypes.Double
                            )
                        );
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

        // Empty strings and lists are omitted, like the other LiveKit SDKs do (the server treats
        // them as unset anyway)
        private static Dictionary<string, object> ConvertClaimsKeysToCamelCase(object obj)
        {
            var result = new Dictionary<string, object>();
            foreach (var prop in obj.GetType().GetProperties())
            {
                var value = prop.GetValue(obj);
                if (value == null)
                {
                    continue;
                }
                if (value is string s && s.Length == 0)
                {
                    continue;
                }
                if (value is System.Collections.ICollection c && c.Count == 0)
                {
                    continue;
                }
                result[PascalToCamelCase(prop.Name)] = value;
            }
            return result;
        }

        private static string PascalToCamelCase(string str)
        {
            return char.ToLowerInvariant(str[0]) + str.Substring(1);
        }

        private static long ToUnixSeconds(DateTime dateTime)
        {
            return (long)
                Math.Floor(
                    dateTime
                        .ToUniversalTime()
                        .Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                        .TotalSeconds
                );
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

        /// <summary>
        /// Creates a verifier for tokens signed with the given API key and secret.
        /// </summary>
        /// <param name="apiKey">API key the tokens must be issued by; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret the tokens must be signed with; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="leeway">Clock tolerance applied to the token lifetime; <see cref="Constants.DefaultLeeway"/> when omitted.</param>
        public TokenVerifier(string apiKey = null, string apiSecret = null, TimeSpan? leeway = null)
        {
            this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
            this.apiSecret = apiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
            this.leeway = leeway ?? Constants.DefaultLeeway;

            if (string.IsNullOrEmpty(this.apiKey) || string.IsNullOrEmpty(this.apiSecret))
            {
                throw new ArgumentException("apiKey and apiSecret must be set");
            }
            if (Encoding.UTF8.GetBytes(this.apiSecret).Length < 32)
            {
                throw new ArgumentException(
                    "apiSecret must be at least 256 bits long. Currently it is "
                        + Encoding.UTF8.GetBytes(this.apiSecret).Length * 8
                        + " bits long"
                );
            }
        }

        /// <summary>
        /// Validates the signature, the issuer and the lifetime of a token and returns its claims.
        /// </summary>
        /// <param name="token">The JWT to verify.</param>
        /// <returns>The claims of the token.</returns>
        /// <exception cref="Microsoft.IdentityModel.Tokens.SecurityTokenException">The token is not valid: wrong signature or issuer, expired or not yet valid.</exception>
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
                // LiveKit tokens are always HS256 (the Python SDK pins the algorithm too)
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(
                token,
                validationParameters,
                out var securityToken
            );
            // A JSON array claim yields one Claim per element: keep the first instead of throwing
            var claims = principal
                .Claims.GroupBy(claim => claim.Type)
                .ToDictionary(group => group.Key, group => group.First().Value);
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

            var inference = claims.TryGetValue("inference", out var inferenceClaim)
                ? JsonConvert.DeserializeObject<InferenceGrants>(inferenceClaim)
                : null;
            var observability = claims.TryGetValue("observability", out var observabilityClaim)
                ? JsonConvert.DeserializeObject<ObservabilityGrants>(observabilityClaim)
                : null;

            var claimsModel = new ClaimsModel
            {
                Identity = decodedSecurityToken.Subject != null ? decodedSecurityToken.Subject : "",
                Name = claims.TryGetValue("name", out var name) ? name : "",
                Video = video,
                Sip = sip,
                Inference = inference,
                Observability = observability,
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
                // Written with the protobuf JSON formatter (enum names, oneofs...): read it back
                // with the protobuf JSON parser, not with Newtonsoft
                claimsModel.RoomConfig = new JsonParser(
                    JsonParser.Settings.Default.WithIgnoreUnknownFields(true)
                ).Parse<RoomConfiguration>(roomConfig);
            }

            return claimsModel;
        }
    }
}
