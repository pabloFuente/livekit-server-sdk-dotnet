using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Livekit.Server.Sdk.Dotnet
{
    /// <summary>
    /// Error returned by the LiveKit server for a service call. It extends the generated
    /// <see cref="global::Twirp.Exception"/> (so existing <c>catch</c> blocks keep working) with the
    /// HTTP status and the <c>meta</c> map of the Twirp error (for example <c>sip_status_code</c>
    /// for SIP calls).
    /// </summary>
    public class TwirpException : global::Twirp.Exception
    {
        private static readonly IReadOnlyDictionary<string, string> NoMeta =
            new Dictionary<string, string>();

        /// <summary>
        /// HTTP status of the response, 0 when unknown.
        /// </summary>
        public int Status { get; }

        /// <summary>
        /// The <c>meta</c> map of the Twirp error, empty when the server sent none.
        /// </summary>
        public IReadOnlyDictionary<string, string> Meta { get; }

        /// <summary>
        /// Creates a new error.
        /// </summary>
        /// <param name="code">The Twirp error code.</param>
        /// <param name="message">The error message.</param>
        /// <param name="status">The HTTP status of the response, 0 when unknown.</param>
        /// <param name="meta">The <c>meta</c> map of the Twirp error.</param>
        public TwirpException(
            global::Twirp.ErrorCode code,
            string message,
            int status = 0,
            IReadOnlyDictionary<string, string> meta = null
        )
            : base(code, message)
        {
            Status = status;
            Meta = meta ?? NoMeta;
        }

        /// <summary>
        /// Builds the error from a non-2xx response body, which is a Twirp JSON error
        /// (<c>{"code":"...","msg":"...","meta":{...}}</c>) or, from proxies, anything else.
        /// Called by the generated <c>Twirp.DoRequest</c> (see <c>generate_proto.sh</c>).
        /// </summary>
        /// <param name="body">The response body.</param>
        /// <param name="status">The HTTP status of the response.</param>
        /// <returns>The exception describing the error.</returns>
        public static TwirpException FromResponse(string body, int status)
        {
            var code = global::Twirp.ErrorCode.Unknown;
            var message = body ?? "";
            Dictionary<string, string> meta = null;
            try
            {
                var json = JObject.Parse(body);
                var codeValue = (string)json["code"];
                if (codeValue != null)
                {
                    code = ParseCode(codeValue);
                }
                var msg = (string)json["msg"];
                if (msg != null)
                {
                    message = msg;
                }
                if (json["meta"] is JObject metaObject)
                {
                    meta = new Dictionary<string, string>();
                    foreach (var property in metaObject.Properties())
                    {
                        meta[property.Name] =
                            property.Value.Type == JTokenType.Null
                                ? null
                                : property.Value.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // Not a Twirp error (HTML error page from a proxy, empty body...): keep the raw body
            }
            return new TwirpException(code, message, status, meta);
        }

        // Twirp codes are lower-case ("permission_denied"); the generated enum keeps the
        // underscores except for "dataloss" (Data_Loss), so compare without them.
        private static global::Twirp.ErrorCode ParseCode(string value)
        {
            var normalized = value.Replace("_", "");
            foreach (
                global::Twirp.ErrorCode candidate in Enum.GetValues(typeof(global::Twirp.ErrorCode))
            )
            {
                var name = candidate.ToString().Replace("_", "");
                if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            return global::Twirp.ErrorCode.Unknown;
        }
    }
}
