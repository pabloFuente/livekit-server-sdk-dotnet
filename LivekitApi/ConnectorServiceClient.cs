using System.Net.Http;
using System.Net.Http.Headers;
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
            string apiKey,
            string apiSecret,
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
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomCreate = true })
            );
            return await Twirp.DialWhatsAppCall(httpClient, request);
        }

        /// <summary>
        /// Disconnect an active WhatsApp call.
        /// </summary>
        public async Task<DisconnectWhatsAppCallResponse> DisconnectWhatsAppCall(
            DisconnectWhatsAppCallRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomCreate = true })
            );
            return await Twirp.DisconnectWhatsAppCall(httpClient, request);
        }

        /// <summary>
        /// Connect an established WhatsApp call (used for business-initiated calls).
        /// </summary>
        public async Task<ConnectWhatsAppCallResponse> ConnectWhatsAppCall(
            ConnectWhatsAppCallRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomCreate = true })
            );
            return await Twirp.ConnectWhatsAppCall(httpClient, request);
        }

        /// <summary>
        /// Accept an inbound WhatsApp call.
        /// </summary>
        public async Task<AcceptWhatsAppCallResponse> AcceptWhatsAppCall(
            AcceptWhatsAppCallRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomCreate = true })
            );
            return await Twirp.AcceptWhatsAppCall(httpClient, request);
        }

        /// <summary>
        /// Connect a Twilio call to a LiveKit room.
        /// </summary>
        public async Task<ConnectTwilioCallResponse> ConnectTwilioCall(
            ConnectTwilioCallRequest request
        )
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                AuthHeader(new VideoGrants { RoomCreate = true })
            );
            return await Twirp.ConnectTwilioCall(httpClient, request);
        }
    }
}
