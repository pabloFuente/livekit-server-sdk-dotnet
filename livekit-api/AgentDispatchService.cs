using System.Net.Http.Headers;
using LiveKit.Proto;

namespace Livekit.Server.Sdk.Dotnet;

public class AgentDispatchService : BaseService
{
    public AgentDispatchService(string host, string apiKey, string secret)
        : base(host, apiKey, secret) { }

    public async Task<AgentDispatch> CreateDispatch(CreateAgentDispatchRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
        );
        return await Twirp.CreateDispatch(httpClient, request);
    }

    public async Task<AgentDispatch> DeleteDispatch(DeleteAgentDispatchRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
        );
        return await Twirp.DeleteDispatch(httpClient, request);
    }

    public async Task<ListAgentDispatchResponse> ListDispatch(ListAgentDispatchRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
        );
        return await Twirp.ListDispatch(httpClient, request);
    }
}
