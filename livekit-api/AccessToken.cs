using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace Livekit.Server.Sdk.Dotnet;

public class AccessToken
{
    public enum ParticipantKind
    {
        Standard,
        Egress,
        Ingress,
        Sip,
        Agent,
    }

    private readonly string apiKey;
    private readonly string apiSecret;
    public ClaimsModel Claims { get; private set; }
    private TimeSpan ttl = Constants.DefaultTtl;

    /// <summary>
    /// Constructs a new AccessToken
    /// </summary>
    /// <param name="apiKey">LiveKit API key</param>
    /// <param name="apiSecret">LiveKit API secret</param>
    public AccessToken(string? apiKey = null, string? apiSecret = null)
    {
        this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
        this.apiSecret = apiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            throw new ArgumentException("apiKey and apiSecret must be set");
        }

        Claims = new ClaimsModel();
    }

    public AccessToken WithTtl(TimeSpan ttl)
    {
        this.ttl = ttl;
        return this;
    }

    public AccessToken WithGrants(VideoGrants grants)
    {
        Claims.Video = grants;
        return this;
    }

    public AccessToken WithSipGrants(SIPGrants grants)
    {
        Claims.Sip = grants;
        return this;
    }

    public AccessToken WithIdentity(string identity)
    {
        Claims.Identity = identity;
        return this;
    }

    public AccessToken WithKind(ParticipantKind kind)
    {
        Claims.Kind = kind.ToString().ToLower();
        return this;
    }

    public AccessToken WithName(string name)
    {
        Claims.Name = name;
        return this;
    }

    public AccessToken WithMetadata(string metadata)
    {
        Claims.Metadata = metadata;
        return this;
    }

    public AccessToken WithAttributes(Dictionary<string, string> attributes)
    {
        Claims.Attributes = attributes;
        return this;
    }

    public AccessToken WithSha256(string sha256)
    {
        Claims.Sha256 = sha256;
        return this;
    }

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
        var exp = now + (long)ttl.TotalSeconds;

        var jwtClaims = new Dictionary<string, object>
        {
            { "sub", Claims.Identity },
            { "iss", apiKey },
            { "nbf", now },
            { "exp", exp },
        };

        jwtClaims["video"] = ConvertClaimsKeysToCamelCase(Claims.Video);
        jwtClaims["sip"] = ConvertClaimsKeysToCamelCase(Claims.Sip);

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
                default:
                    throw new ArgumentException($"unsupported claim type {kv.Value.GetType()}");
            }
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(claims: claims, signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Dictionary<string, object?> ConvertClaimsKeysToCamelCase(object obj)
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

        if (string.IsNullOrEmpty(this.apiKey) || string.IsNullOrEmpty(this.apiSecret))
        {
            throw new ArgumentException("api_key and api_secret must be set");
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
        var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
        var claims = principal.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);

        var video = new VideoGrants();
        var exists = claims.TryGetValue("video", out var videoClaim);
        if (exists && videoClaim != null)
        {
            video = JsonConvert.DeserializeObject<VideoGrants>(videoClaim);
        }

        var sip = claims.TryGetValue("sip", out var sipClaim)
            ? JsonConvert.DeserializeObject<SIPGrants>(sipClaim)
            : new SIPGrants();

        return new ClaimsModel
        {
            Identity = claims.GetValueOrDefault("sub"),
            Name = claims.GetValueOrDefault("name"),
            Video = video,
            Sip = sip,
            Metadata = claims.GetValueOrDefault("metadata"),
            Sha256 = claims.GetValueOrDefault("sha256"),
            Attributes = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                claims.GetValueOrDefault("attributes", "{}")
            ),
        };
    }
}
