// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Data passed to method handler for incoming RPC invocations.
    /// </summary>
    public class RpcInvocationData
    {
        /// <summary>
        /// Gets the unique request ID. Will match at both sides of the call, useful for debugging or logging.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// Gets the unique participant identity of the caller.
        /// </summary>
        public string CallerIdentity { get; }

        /// <summary>
        /// Gets the payload of the request. User-definable format, typically JSON.
        /// </summary>
        public string Payload { get; }

        /// <summary>
        /// Gets the maximum time the caller will wait for a response in seconds.
        /// </summary>
        public double ResponseTimeout { get; }

        /// <summary>
        /// Initializes a new RpcInvocationData.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        /// <param name="callerIdentity">The caller identity.</param>
        /// <param name="payload">The payload.</param>
        /// <param name="responseTimeout">The response timeout in seconds.</param>
        public RpcInvocationData(
            string requestId,
            string callerIdentity,
            string payload,
            double responseTimeout
        )
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            CallerIdentity =
                callerIdentity ?? throw new ArgumentNullException(nameof(callerIdentity));
            Payload = payload ?? string.Empty;
            ResponseTimeout = responseTimeout;
        }
    }

    /// <summary>
    /// RPC error codes.
    /// </summary>
    public enum RpcErrorCode
    {
        /// <summary>
        /// Application error in method handler.
        /// </summary>
        ApplicationError = 1500,

        /// <summary>
        /// Connection timeout.
        /// </summary>
        ConnectionTimeout = 1501,

        /// <summary>
        /// Response timeout.
        /// </summary>
        ResponseTimeout = 1502,

        /// <summary>
        /// Recipient disconnected.
        /// </summary>
        RecipientDisconnected = 1503,

        /// <summary>
        /// Response payload too large.
        /// </summary>
        ResponsePayloadTooLarge = 1504,

        /// <summary>
        /// Failed to send.
        /// </summary>
        SendFailed = 1505,

        /// <summary>
        /// Method not supported at destination.
        /// </summary>
        UnsupportedMethod = 1400,

        /// <summary>
        /// Recipient not found.
        /// </summary>
        RecipientNotFound = 1401,

        /// <summary>
        /// Request payload too large.
        /// </summary>
        RequestPayloadTooLarge = 1402,

        /// <summary>
        /// RPC not supported by server.
        /// </summary>
        UnsupportedServer = 1403,

        /// <summary>
        /// Unsupported RPC version.
        /// </summary>
        UnsupportedVersion = 1404,
    }

    /// <summary>
    /// Specialized error handling for RPC methods.
    /// Instances of this type, when thrown in a method handler, will have their message
    /// serialized and sent across the wire.
    /// </summary>
    public class RpcError : Exception
    {
        private static readonly Dictionary<RpcErrorCode, string> ErrorMessages = new Dictionary<
            RpcErrorCode,
            string
        >
        {
            { RpcErrorCode.ApplicationError, "Application error in method handler" },
            { RpcErrorCode.ConnectionTimeout, "Connection timeout" },
            { RpcErrorCode.ResponseTimeout, "Response timeout" },
            { RpcErrorCode.RecipientDisconnected, "Recipient disconnected" },
            { RpcErrorCode.ResponsePayloadTooLarge, "Response payload too large" },
            { RpcErrorCode.SendFailed, "Failed to send" },
            { RpcErrorCode.UnsupportedMethod, "Method not supported at destination" },
            { RpcErrorCode.RecipientNotFound, "Recipient not found" },
            { RpcErrorCode.RequestPayloadTooLarge, "Request payload too large" },
            { RpcErrorCode.UnsupportedServer, "RPC not supported by server" },
            { RpcErrorCode.UnsupportedVersion, "Unsupported RPC version" },
        };

        /// <summary>
        /// Gets the error code.
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// Gets optional additional data associated with the error (JSON recommended).
        /// </summary>
        public string? RpcData { get; }

        /// <summary>
        /// Creates an error object with the given code and message, plus an optional data payload.
        /// </summary>
        /// <param name="code">Your error code (Error codes 1001-1999 are reserved for built-in errors).</param>
        /// <param name="message">A readable error message.</param>
        /// <param name="data">Optional additional data associated with the error (JSON recommended).</param>
        public RpcError(int code, string message, string? data = null)
            : base(message)
        {
            Code = code;
            RpcData = data;
        }

        /// <summary>
        /// Creates an error from an error code enum.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <param name="message">Optional custom message.</param>
        /// <param name="data">Optional additional data.</param>
        public RpcError(RpcErrorCode code, string? message = null, string? data = null)
            : base(message ?? GetDefaultMessage(code))
        {
            Code = (int)code;
            RpcData = data;
        }

        /// <summary>
        /// Creates a built-in error from an error code.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <param name="data">Optional additional data.</param>
        /// <returns>A new RpcError instance.</returns>
        public static RpcError BuiltIn(RpcErrorCode code, string? data = null)
        {
            return new RpcError(code, null, data);
        }

        /// <summary>
        /// Creates an RpcError from a proto message.
        /// </summary>
        /// <param name="proto">The proto error.</param>
        /// <returns>A new RpcError instance.</returns>
        internal static RpcError FromProto(LiveKit.Proto.RpcError proto)
        {
            return new RpcError((int)proto.Code, proto.Message, proto.Data);
        }

        /// <summary>
        /// Converts to a proto message.
        /// </summary>
        /// <returns>The proto error.</returns>
        internal LiveKit.Proto.RpcError ToProto()
        {
            var error = new LiveKit.Proto.RpcError
            {
                Code = (uint)Code,
                Message = Message ?? string.Empty,
            };

            if (RpcData != null)
            {
                error.Data = RpcData;
            }

            return error;
        }

        private static string GetDefaultMessage(RpcErrorCode code)
        {
            if (ErrorMessages.TryGetValue(code, out var message))
            {
                return message;
            }
            return "Unknown error";
        }
    }

    /// <summary>
    /// Delegate for RPC method handlers.
    /// </summary>
    /// <param name="data">The invocation data.</param>
    /// <returns>The response payload as a string.</returns>
    public delegate Task<string> RpcMethodHandler(RpcInvocationData data);
}
