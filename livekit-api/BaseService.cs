namespace Livekit.Server.Sdk.Dotnet;

public class BaseService
{
    private readonly string apiKey;
    private readonly string secret;
    protected readonly HttpClient httpClient;

    public BaseService(string host, string apiKey, string secret, HttpClient? client = null)
    {
        this.apiKey = apiKey;
        this.secret = secret;
        this.httpClient = client ?? new HttpClient();
        this.httpClient.BaseAddress = new Uri(host);
    }

    protected string AuthHeader(VideoGrants videoGrants)
    {
        var accessToken = new AccessToken(apiKey, secret);
        accessToken.WithGrants(videoGrants);
        accessToken.WithTtl(Constants.DefaultTtl);

        return accessToken.ToJwt();
    }

    protected string AuthHeader(VideoGrants videoGrants, SIPGrants sipGrants)
    {
        var accessToken = new AccessToken(apiKey, secret);
        accessToken.WithGrants(videoGrants);
        accessToken.WithSipGrants(sipGrants);
        accessToken.WithTtl(Constants.DefaultTtl);

        return accessToken.ToJwt();
    }
}