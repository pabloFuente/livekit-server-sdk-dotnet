// author: https://github.com/pabloFuente

using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Threading;

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

        // Process-wide counters read by the reliability tests (internal, not part of the public
        // API): a handle that is never released, or released twice, is a native leak or a
        // double free that no exception reveals
        private static long s_wrapped;
        private static long s_released;
        private static long s_failedReleases;

        private readonly ulong _handleId;
        private bool _disposed;

        /// <summary>
        /// Valid handles wrapped and not released yet, in the whole process.
        /// </summary>
        internal static long LiveCount =>
            Interlocked.Read(ref s_wrapped) - Interlocked.Read(ref s_released);

        /// <summary>
        /// Releases the native library rejected because it did not know the handle: a handle
        /// released twice, or wrapped by two owners.
        /// </summary>
        internal static long FailedReleaseCount => Interlocked.Read(ref s_failedReleases);

        /// <summary>
        /// Creates a new FfiHandle from a native handle ID.
        /// </summary>
        /// <param name="handleId">The native handle ID.</param>
        internal FfiHandle(ulong handleId)
            : base(IntPtr.Zero, true)
        {
            _handleId = handleId;
            SetHandle(new IntPtr((long)handleId));
            if (handleId != InvalidHandle)
                Interlocked.Increment(ref s_wrapped);
        }

        /// <summary>
        /// Creates an FfiHandle from an IntPtr.
        /// </summary>
        /// <param name="ptr">The pointer value.</param>
        internal FfiHandle(IntPtr ptr)
            : base(ptr, true)
        {
            _handleId = (ulong)ptr.ToInt64();
            if (!IsInvalid)
                Interlocked.Increment(ref s_wrapped);
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
            var dropped = NativeMethods.DropHandle(_handleId);
            Interlocked.Increment(ref s_released);
            if (!dropped)
                Interlocked.Increment(ref s_failedReleases);
            return dropped;
        }

        /// <summary>
        /// Records that a request handed the handle over to the native library, which now owns it
        /// (the incremental read of a data stream reader takes its reader): it counts as released
        /// and is never dropped. Only the sole owner of the handle may call it.
        /// </summary>
        internal void MarkConsumed()
        {
            if (_disposed || IsInvalid || IsClosed)
                return;
            _disposed = true;
            Interlocked.Increment(ref s_released);
            SetHandleAsInvalid();
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
