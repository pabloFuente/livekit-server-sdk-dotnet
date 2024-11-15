using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet.Test;

public class AccessTokenTest
{
    const string TEST_KEY = "API87mWmmh7KM3V";
    const string TEST_SECRET = "helOnxxeT71NeOEBcYm3kW0s1pofQAbitubCw7AIsY0A";

    [Fact]
    public void GeneratesValidJWTWithDefaults()
    {
        var token = new AccessToken(TEST_KEY, TEST_SECRET);
        token.WithGrants(new VideoGrants());
        var jwt = token.ToJwt();

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.Default.GetBytes(TEST_SECRET)),
            ValidateIssuer = false,
            ValidateAudience = false,
        };

        new JwtSecurityTokenHandler().ValidateToken(
            jwt,
            validationParameters,
            out var validatedToken
        );
        Assert.Equal(TEST_KEY, validatedToken.Issuer);
        Assert.True(validatedToken.ValidFrom < DateTime.UtcNow);
        Assert.True(validatedToken.ValidTo - validatedToken.ValidFrom == Constants.DefaultTtl);
    }
}