using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    public class AgentDispatchServiceClient : BaseService
    {
        public AgentDispatchServiceClient(
            string host,
            string apiKey,
            string apiSecret,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

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

        /// <summary>
        /// Get an agent dispatch by its ID. Returns null if the dispatch does not exist.
        /// </summary>
        public async Task<AgentDispatch> GetDispatch(string dispatchId, string roomName)
        {
            var response = await ListDispatch(
                new ListAgentDispatchRequest { DispatchId = dispatchId, Room = roomName }
            );
            return response.AgentDispatches.Count > 0 ? response.AgentDispatches[0] : null;
        }
    }
}
