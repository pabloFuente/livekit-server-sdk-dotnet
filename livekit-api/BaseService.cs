namespace Livekit.Server.Sdk.Dotnet;

public class BaseService
{
    private readonly string apiKey;
    private readonly string apiSecret;
    protected readonly HttpClient httpClient;

    public BaseService(string host, string apiKey, string apiSecret, HttpClient? client = null)
    {
        this.apiKey = apiKey;
        this.apiSecret = apiSecret;
        httpClient = client ?? new HttpClient();
        httpClient.BaseAddress = new Uri(host);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LiveKit .NET SDK");
    }

    protected string AuthHeader(VideoGrants videoGrants)
    {
        var accessToken = new AccessToken(apiKey, apiSecret);
        accessToken.WithGrants(videoGrants);
        accessToken.WithTtl(Constants.DefaultTtl);

        return accessToken.ToJwt();
    }

    protected string AuthHeader(VideoGrants videoGrants, SIPGrants sipGrants)
    {
        var accessToken = new AccessToken(apiKey, apiSecret);
        accessToken.WithGrants(videoGrants);
        accessToken.WithSipGrants(sipGrants);
        accessToken.WithTtl(Constants.DefaultTtl);

        return accessToken.ToJwt();
    }
}
