// author: https://github.com/pabloFuente

using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace LiveKit.Rtc.Internal
{
    /// <summary>
    /// Delegate for receiving FFI events from the native library.
    /// </summary>
    /// <param name="dataPtr">Pointer to the event data buffer.</param>
    /// <param name="dataLen">Length of the event data.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FfiCallbackDelegate(IntPtr dataPtr, UIntPtr dataLen);

    /// <summary>
    /// P/Invoke declarations for the LiveKit FFI native library.
    /// </summary>
    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        private const string LibName = "livekit_ffi";

        /// <summary>
        /// Initialize the LiveKit FFI library (internal - use the wrapper method).
        /// </summary>
        [DllImport(
            LibName,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "livekit_ffi_initialize"
        )]
        private static extern unsafe IntPtr InitializeNative(
            FfiCallbackDelegate callback,
            [MarshalAs(UnmanagedType.I1)] bool captureLogs,
            byte* sdk,
            byte* sdkVersion
        );

        /// <summary>
        /// Initialize the LiveKit FFI library.
        /// </summary>
        /// <param name="callback">Callback function for receiving events.</param>
        /// <param name="captureLogs">Whether to capture and forward logs.</param>
        /// <param name="sdk">SDK identifier string.</param>
        /// <param name="sdkVersion">SDK version string.</param>
        /// <returns>Handle ID for the initialization.</returns>
        internal static unsafe IntPtr Initialize(
            FfiCallbackDelegate callback,
            bool captureLogs,
            string sdk,
            string sdkVersion
        )
        {
            // Convert strings to null-terminated UTF-8 byte arrays
            var sdkBytes = StringToUtf8Null(sdk);
            var sdkVersionBytes = StringToUtf8Null(sdkVersion);

            fixed (byte* sdkPtr = sdkBytes)
            fixed (byte* sdkVersionPtr = sdkVersionBytes)
            {
                return InitializeNative(callback, captureLogs, sdkPtr, sdkVersionPtr);
            }
        }

        /// <summary>
        /// Converts a string to a null-terminated UTF-8 byte array.
        /// </summary>
        private static byte[] StringToUtf8Null(string str)
        {
            if (str == null)
            {
                return new byte[] { 0 };
            }

            var utf8 = Encoding.UTF8.GetBytes(str);
            var result = new byte[utf8.Length + 1];
            Buffer.BlockCopy(utf8, 0, result, 0, utf8.Length);
            result[utf8.Length] = 0; // Null terminator
            return result;
        }

        /// <summary>
        /// Send an FFI request to the native library.
        /// </summary>
        /// <param name="data">Pointer to the request data buffer.</param>
        /// <param name="len">Length of the request data.</param>
        /// <param name="dataPtr">Output pointer to the response data buffer.</param>
        /// <param name="dataLen">Output length of the response data.</param>
        /// <returns>Handle ID for the request (used for cleanup).</returns>
        [DllImport(
            LibName,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "livekit_ffi_request"
        )]
        internal static extern unsafe ulong Request(
            byte* data,
            int len,
            out byte* dataPtr,
            out UIntPtr dataLen
        );

        /// <summary>
        /// Drop (release) a handle returned by the FFI library.
        /// </summary>
        /// <param name="handleId">The handle ID to release.</param>
        /// <returns>True if the handle was successfully dropped.</returns>
        [DllImport(
            LibName,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "livekit_ffi_drop_handle"
        )]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool DropHandle(ulong handleId);

        /// <summary>
        /// Dispose and cleanup the FFI library.
        /// </summary>
        [DllImport(
            LibName,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "livekit_ffi_dispose"
        )]
        internal static extern void Dispose();

        /// <summary>
        /// Copy a buffer from native memory to managed memory.
        /// </summary>
        /// <param name="ptr">Pointer to the native buffer.</param>
        /// <param name="len">Length of the buffer.</param>
        /// <param name="dst">Destination managed buffer.</param>
        [DllImport(
            LibName,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "livekit_copy_buffer"
        )]
        internal static extern unsafe void CopyBuffer(ulong ptr, UIntPtr len, byte* dst);

        /// <summary>
        /// Retrieve a pointer from data.
        /// </summary>
        /// <param name="data">Pointer to the data.</param>
        /// <param name="len">Length of the data.</param>
        /// <returns>The pointer value.</returns>
        [DllImport(
            LibName,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "livekit_retrieve_ptr"
        )]
        internal static extern unsafe ulong RetrievePtr(byte* data, UIntPtr len);
    }
}
