// author: https://github.com/pabloFuente

using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

namespace LiveKit.Rtc.Internal
{
    /// <summary>
    /// SafeHandle wrapper for FFI handles returned by the native library.
    /// Ensures proper cleanup of native resources.
    /// </summary>
    public class FfiHandle : SafeHandle
    {
        /// <summary>
        /// Invalid handle constant.
        /// </summary>
        public const ulong InvalidHandle = 0;

        private readonly ulong _handleId;
        private bool _disposed;

        /// <summary>
        /// Creates a new FfiHandle from a native handle ID.
        /// </summary>
        /// <param name="handleId">The native handle ID.</param>
        internal FfiHandle(ulong handleId)
            : base(IntPtr.Zero, true)
        {
            _handleId = handleId;
            SetHandle(new IntPtr((long)handleId));
        }

        /// <summary>
        /// Creates an FfiHandle from an IntPtr.
        /// </summary>
        /// <param name="ptr">The pointer value.</param>
        internal FfiHandle(IntPtr ptr)
            : base(ptr, true)
        {
            _handleId = (ulong)ptr.ToInt64();
        }

        /// <summary>
        /// Gets whether this handle is invalid.
        /// </summary>
        public override bool IsInvalid => handle == IntPtr.Zero || _handleId == InvalidHandle;

        /// <summary>
        /// Gets the underlying handle ID.
        /// </summary>
        public ulong HandleId => _handleId;

        /// <summary>
        /// Releases the native handle.
        /// </summary>
        /// <returns>True if the handle was released successfully.</returns>
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            if (_disposed || IsInvalid)
                return true;

            _disposed = true;
            return NativeMethods.DropHandle(_handleId);
        }

        /// <summary>
        /// Creates an FfiHandle from an owned handle proto message.
        /// </summary>
        /// <param name="id">The handle ID.</param>
        /// <returns>A new FfiHandle instance.</returns>
        public static FfiHandle FromId(ulong id)
        {
            return new FfiHandle(id);
        }
    }

    /// <summary>
    /// Represents an owned handle that can be converted to an FfiHandle.
    /// </summary>
    public readonly struct OwnedHandle
    {
        /// <summary>
        /// The handle ID.
        /// </summary>
        public ulong Id { get; }

        /// <summary>
        /// Creates a new OwnedHandle.
        /// </summary>
        /// <param name="id">The handle ID.</param>
        public OwnedHandle(ulong id)
        {
            Id = id;
        }

        /// <summary>
        /// Converts this OwnedHandle to an FfiHandle.
        /// </summary>
        /// <returns>A new FfiHandle instance.</returns>
        public FfiHandle ToFfiHandle()
        {
            return new FfiHandle(Id);
        }
    }
}
