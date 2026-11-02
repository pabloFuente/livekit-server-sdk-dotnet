using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// A client for interacting with the Connector service, which connects WhatsApp and Twilio
    /// calls to LiveKit rooms. Only available in LiveKit Cloud.
    /// </summary>
    public class ConnectorServiceClient : BaseService
    {
        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="host">URL of the LiveKit server, <c>http(s)://</c> or <c>ws(s)://</c>; a subpath such as <c>https://host/livekit</c> is kept.</param>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="client">Optional <see cref="HttpClient"/> to send the requests with. The SDK never modifies it, so it can be shared with other clients and code.</param>
        public ConnectorServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        /// <summary>
        /// Initiate an outbound WhatsApp call.
        /// </summary>
        /// <param name="request">WhatsApp phone number ID, API key, the number to call and the room to connect the call to.</param>
        /// <returns>The WhatsApp call ID and the room.</returns>
        public async Task<DialWhatsAppCallResponse> DialWhatsAppCall(
            DialWhatsAppCallRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.Connector/DialWhatsAppCall",
                    request,
                    DialWhatsAppCallResponse.Parser,
                    AuthHeader(new VideoGrants { RoomCreate = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Disconnect an active WhatsApp call.
        /// </summary>
        /// <param name="request">WhatsApp call ID and the reason.</param>
        /// <returns>Empty response.</returns>
        public async Task<DisconnectWhatsAppCallResponse> DisconnectWhatsAppCall(
            DisconnectWhatsAppCallRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.Connector/DisconnectWhatsAppCall",
                    request,
                    DisconnectWhatsAppCallResponse.Parser,
                    AuthHeader(new VideoGrants { RoomCreate = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Connect an established WhatsApp call (used for business-initiated calls).
        /// </summary>
        /// <param name="request">WhatsApp call ID and the SDP of the call; optionally wait until it is answered.</param>
        /// <returns>The connection details of the call.</returns>
        public async Task<ConnectWhatsAppCallResponse> ConnectWhatsAppCall(
            ConnectWhatsAppCallRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.Connector/ConnectWhatsAppCall",
                    request,
                    ConnectWhatsAppCallResponse.Parser,
                    AuthHeader(new VideoGrants { RoomCreate = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Accept an inbound WhatsApp call.
        /// </summary>
        /// <param name="request">WhatsApp phone number ID, API key and the ID of the incoming call to accept.</param>
        /// <returns>The accepted call.</returns>
        public async Task<AcceptWhatsAppCallResponse> AcceptWhatsAppCall(
            AcceptWhatsAppCallRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.Connector/AcceptWhatsAppCall",
                    request,
                    AcceptWhatsAppCallResponse.Parser,
                    AuthHeader(new VideoGrants { RoomCreate = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Connect a Twilio call to a LiveKit room.
        /// </summary>
        /// <param name="request">Twilio call direction and the room to connect the call to.</param>
        /// <returns>The URL Twilio must connect the call to.</returns>
        public async Task<ConnectTwilioCallResponse> ConnectTwilioCall(
            ConnectTwilioCallRequest request
        )
        {
            return await SendAsync(
                    "twirp/livekit.Connector/ConnectTwilioCall",
                    request,
                    ConnectTwilioCallResponse.Parser,
                    AuthHeader(new VideoGrants { RoomCreate = true })
                )
                .ConfigureAwait(false);
        }
    }
}
