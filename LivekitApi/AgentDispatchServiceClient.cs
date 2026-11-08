using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// Client of the LiveKit Agent Dispatch service: dispatches agents to rooms explicitly, lists and deletes
    /// those dispatches.
    /// </summary>
    public class AgentDispatchServiceClient : BaseService
    {
        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="host">URL of the LiveKit server, <c>http(s)://</c> or <c>ws(s)://</c>; a subpath such as <c>https://host/livekit</c> is kept.</param>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="client">Optional <see cref="HttpClient"/> to send the requests with. The SDK never modifies it, so it can be shared with other clients and code.</param>
        public AgentDispatchServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        /// <summary>
        /// Dispatches an agent to a room.
        /// </summary>
        /// <param name="request">Room, agent name and optional metadata for the agent.</param>
        /// <returns>The created dispatch.</returns>
        public async Task<AgentDispatch> CreateDispatch(CreateAgentDispatchRequest request)
        {
            return await SendAsync(
                    Twirp.Routes.AgentDispatchService.CreateDispatch,
                    request,
                    AgentDispatch.Parser,
                    AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes an agent dispatch.
        /// </summary>
        /// <param name="request">Dispatch ID and room.</param>
        /// <returns>The deleted dispatch.</returns>
        public async Task<AgentDispatch> DeleteDispatch(DeleteAgentDispatchRequest request)
        {
            return await SendAsync(
                    Twirp.Routes.AgentDispatchService.DeleteDispatch,
                    request,
                    AgentDispatch.Parser,
                    AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Lists the agent dispatches of a room.
        /// </summary>
        /// <param name="request">Room name, and optionally a dispatch ID to match.</param>
        /// <returns>The dispatches of the room.</returns>
        public async Task<ListAgentDispatchResponse> ListDispatch(ListAgentDispatchRequest request)
        {
            return await SendAsync(
                    Twirp.Routes.AgentDispatchService.ListDispatch,
                    request,
                    ListAgentDispatchResponse.Parser,
                    AuthHeader(new VideoGrants { RoomAdmin = true, Room = request.Room })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets an agent dispatch by its ID, or null when it does not exist.
        /// </summary>
        /// <param name="dispatchId">ID of the dispatch.</param>
        /// <param name="roomName">Room the dispatch belongs to.</param>
        /// <returns>The dispatch, or null when the room has no dispatch with that ID.</returns>
        public async Task<AgentDispatch> GetDispatch(string dispatchId, string roomName)
        {
            var response = await ListDispatch(
                new ListAgentDispatchRequest { DispatchId = dispatchId, Room = roomName }
            );
            return response.AgentDispatches.Count > 0 ? response.AgentDispatches[0] : null;
        }
    }
}
