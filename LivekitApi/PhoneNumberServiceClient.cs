using System.Net.Http;
using System.Threading.Tasks;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// A client for the Phone Number service, which searches, purchases and manages the phone
    /// numbers of a LiveKit project. Only available in LiveKit Cloud. Every call is signed with
    /// the SIP <see cref="SIPGrants.Admin"/> grant.
    /// </summary>
    public class PhoneNumberServiceClient : BaseService
    {
        /// <summary>
        /// Creates the client.
        /// </summary>
        /// <param name="host">URL of the LiveKit server, <c>http(s)://</c> or <c>ws(s)://</c>; a subpath such as <c>https://host/livekit</c> is kept.</param>
        /// <param name="apiKey">API key; the <c>LIVEKIT_API_KEY</c> environment variable when omitted.</param>
        /// <param name="apiSecret">API secret; the <c>LIVEKIT_API_SECRET</c> environment variable when omitted.</param>
        /// <param name="client">Optional <see cref="HttpClient"/> to send the requests with. The SDK never modifies it, so it can be shared with other clients and code.</param>
        public PhoneNumberServiceClient(
            string host,
            string apiKey = null,
            string apiSecret = null,
            HttpClient client = null
        )
            : base(host, apiKey, apiSecret, client) { }

        /// <summary>
        /// Searches the inventory for phone numbers available to purchase.
        /// </summary>
        /// <param name="request">Country code (required), and optionally an area code, the maximum number of results (50 by default) and a page token.</param>
        /// <returns>The available phone numbers and the token of the next page.</returns>
        public async Task<SearchPhoneNumbersResponse> SearchPhoneNumbers(
            SearchPhoneNumbersRequest request
        )
        {
            return await SendAsync(
                    Twirp.Routes.PhoneNumberService.SearchPhoneNumbers,
                    request,
                    SearchPhoneNumbersResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Purchases phone numbers from the inventory for the project.
        /// </summary>
        /// <param name="request">Phone numbers to purchase, in E.164 format, and optionally the SIP dispatch rule to apply to all of them.</param>
        /// <returns>The purchased phone numbers, with their ID, E.164 format and status.</returns>
        public async Task<PurchasePhoneNumberResponse> PurchasePhoneNumber(
            PurchasePhoneNumberRequest request
        )
        {
            return await SendAsync(
                    Twirp.Routes.PhoneNumberService.PurchasePhoneNumber,
                    request,
                    PurchasePhoneNumberResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Lists the phone numbers of the project.
        /// </summary>
        /// <param name="request">Optional filters (statuses, SIP dispatch rule), the maximum number of results (50 by default) and a page token.</param>
        /// <returns>The phone numbers, the token of the next page, and the total and offline counts.</returns>
        public async Task<ListPhoneNumbersResponse> ListPhoneNumbers(
            ListPhoneNumbersRequest request
        )
        {
            return await SendAsync(
                    Twirp.Routes.PhoneNumberService.ListPhoneNumbers,
                    request,
                    ListPhoneNumbersResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a phone number of the project.
        /// </summary>
        /// <param name="request">The phone number to get, by its ID or in E.164 format.</param>
        /// <returns>The phone number.</returns>
        public async Task<GetPhoneNumberResponse> GetPhoneNumber(GetPhoneNumberRequest request)
        {
            return await SendAsync(
                    Twirp.Routes.PhoneNumberService.GetPhoneNumber,
                    request,
                    GetPhoneNumberResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates a phone number of the project: its name or the SIP dispatch rule assigned to it.
        /// </summary>
        /// <param name="request">The phone number to update, by its ID or in E.164 format, and the new values.</param>
        /// <returns>The updated phone number.</returns>
        public async Task<UpdatePhoneNumberResponse> UpdatePhoneNumber(
            UpdatePhoneNumberRequest request
        )
        {
            return await SendAsync(
                    Twirp.Routes.PhoneNumberService.UpdatePhoneNumber,
                    request,
                    UpdatePhoneNumberResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Releases phone numbers of the project.
        /// </summary>
        /// <param name="request">The phone numbers to release, by their IDs or in E.164 format.</param>
        /// <returns>Empty response.</returns>
        public async Task<ReleasePhoneNumbersResponse> ReleasePhoneNumbers(
            ReleasePhoneNumbersRequest request
        )
        {
            return await SendAsync(
                    Twirp.Routes.PhoneNumberService.ReleasePhoneNumbers,
                    request,
                    ReleasePhoneNumbersResponse.Parser,
                    AuthHeader(new VideoGrants { }, new SIPGrants { Admin = true })
                )
                .ConfigureAwait(false);
        }
    }
}
