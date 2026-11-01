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
        /// A client for interacting with the Connector service, which connects WhatsApp and Twilio
        /// calls to LiveKit rooms. Only available in LiveKit Cloud.
        /// </summary>
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
