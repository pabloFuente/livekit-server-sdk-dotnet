using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    public class AgentDispatchServiceClient : BaseService
    {
        public AgentDispatchServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        public async Task<AgentDispatch> CreateDispatch(CreateAgentDispatchRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.AgentDispatchService/CreateDispatch",
                    request,
                    AgentDispatch.Parser,
                    AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
                )
                .ConfigureAwait(false);
        }

        public async Task<AgentDispatch> DeleteDispatch(DeleteAgentDispatchRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.AgentDispatchService/DeleteDispatch",
                    request,
                    AgentDispatch.Parser,
                    AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
                )
                .ConfigureAwait(false);
        }

        public async Task<ListAgentDispatchResponse> ListDispatch(ListAgentDispatchRequest request)
        {
            return await SendAsync(
                    "twirp/livekit.AgentDispatchService/ListDispatch",
                    request,
                    ListAgentDispatchResponse.Parser,
                    AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
                )
                .ConfigureAwait(false);
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
