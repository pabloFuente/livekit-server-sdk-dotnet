// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveKit.Proto;

namespace LiveKit.Rtc.Internal
{
    /// <summary>
    /// Process-wide client of the native <c>livekit_ffi</c> library: sends the requests and delivers the native
    /// events. Public for the tests; applications use <see cref="Room"/> and the track, source and stream classes.
    /// </summary>
    public sealed class FfiClient : IDisposable
    {
        private static readonly Lazy<FfiClient> _instance = new Lazy<FfiClient>(
            () => new FfiClient(),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static FfiClient Instance => _instance.Value;

        private readonly FfiCallbackDelegate _callbackDelegate;

        // Synchronization
        private readonly object _lock = new object();
        private readonly object _initLock = new object();
        private volatile bool _initialized;
        private volatile bool _disposed;

        // Event Storage
        private readonly List<FfiEvent> _eventQueue = new();
        private readonly List<(
            Func<FfiEvent, bool> Predicate,
            TaskCompletionSource<FfiEvent> Tcs
        )> _waiters = new List<(Func<FfiEvent, bool>, TaskCompletionSource<FfiEvent>)>();

        /// <summary>
        /// Global event for all FFI messages. Invoked outside the internal lock.
        /// </summary>
        public event EventHandler<FfiEvent>? EventReceived;

        /// <summary>
        /// The SDK name reported to the native library, and by it to the LiveKit server.
        /// </summary>
        public const string SdkName = "dotnet";

        /// <summary>
        /// SDK version reported to the FFI and sent to the LiveKit server as the client SDK
        /// version. Read from the assembly, which the build stamps with the
        /// <c>&lt;Version&gt;</c> property of LivekitRtc.csproj, so it never has to be updated
        /// by hand on release.
        /// </summary>
        public static readonly string SdkVersion = ReadSdkVersion();

        private FfiClient()
        {
            _callbackDelegate = OnFfiCallback;
        }

        private static string ReadSdkVersion()
        {
            var assembly = typeof(FfiClient).Assembly;

            // The informational version is the csproj <Version> as written (e.g. "0.1.4" or
            // "0.2.0-beta.1"), plus "+<commit sha>" build metadata appended by SourceLink.
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (informational != null && informational.Length > 0)
            {
                var metadataStart = informational.IndexOf('+');
                return metadataStart >= 0
                    ? informational.Substring(0, metadataStart)
                    : informational;
            }

            // Fallback: the four-part assembly version, trimmed to major.minor.patch
            var version = assembly.GetName().Version;
            return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        /// <summary>
        /// Loads the native library and registers the event callback, once per process.
        /// <paramref name="captureLogs"/> forwards the native logs to the console.
        /// </summary>
        /// <param name="captureLogs">Forwards the native logs to the console when true.</param>
        public void Initialize(bool captureLogs = false)
        {
            if (_initialized)
                return;

            lock (_initLock)
            {
                if (_initialized || _disposed)
                    return;
                NativeMethods.Initialize(_callbackDelegate, captureLogs, SdkName, SdkVersion);
                _initialized = true;
            }
        }

        /// <summary>
        /// Sends a synchronous request to the Rust SDK.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <returns>The native response.</returns>
        public unsafe FfiResponse SendRequest(FfiRequest request)
        {
            EnsureInitialized();

            byte[] requestData = request.ToByteArray();
            fixed (byte* dataPtr = requestData)
            {
                ulong handleId = NativeMethods.Request(
                    dataPtr,
                    (UIntPtr)requestData.Length,
                    out byte* responsePtr,
                    out UIntPtr responseLen
                );

                if (handleId == FfiHandle.InvalidHandle)
                    throw new InvalidOperationException("FFI request failed: invalid handle");

                try
                {
                    // Direct parse from unmanaged memory to reduce allocations
                    using var stream = new UnmanagedMemoryStream(responsePtr, (long)responseLen);
                    return FfiResponse.Parser.ParseFrom(stream);
                }
                finally
                {
                    NativeMethods.DropHandle(handleId);
                }
            }
        }

        /// <summary>
        /// Async waits for a specific event. Uses the Waiter Pattern to ensure no events
        /// are missed between a Request and a Wait call.
        /// </summary>
        /// <param name="predicate">Selects the awaited event.</param>
        /// <param name="timeout">Maximum time to wait; the default when null.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The first event that satisfies the predicate.</returns>
        public async Task<FfiEvent> WaitForEventAsync(
            Func<FfiEvent, bool> predicate,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            TaskCompletionSource<FfiEvent> tcs = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            lock (_lock)
            {
                // 1. Check if the event is already in the queue
                for (int i = 0; i < _eventQueue.Count; i++)
                {
                    if (predicate(_eventQueue[i]))
                    {
                        var evt = _eventQueue[i];
                        _eventQueue.RemoveAt(i); // Preserves order of remaining events
                        return evt;
                    }
                }

                // 2. Not found in queue; register a waiter while holding the lock
                _waiters.Add((predicate, tcs));
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout.HasValue)
                cts.CancelAfter(timeout.Value);

            using var registration = cts.Token.Register(() =>
            {
                lock (_lock)
                    _waiters.RemoveAll(w => w.Tcs == tcs);
                tcs.TrySetCanceled();
            });

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"FFI event wait timed out after {timeout?.TotalSeconds}s"
                );
            }
        }

        private void OnFfiCallback(IntPtr dataPtr, UIntPtr dataLen)
        {
            if (_disposed)
                return;

            try
            {
                FfiEvent ffiEvent;
                unsafe
                {
                    using var stream = new UnmanagedMemoryStream((byte*)dataPtr, (long)dataLen);
                    ffiEvent = FfiEvent.Parser.ParseFrom(stream);
                }

                if (ffiEvent.MessageCase == FfiEvent.MessageOneofCase.Logs)
                {
                    foreach (var record in ffiEvent.Logs.Records)
                        LogRecord(record);
                    return;
                }

                if (ffiEvent.MessageCase == FfiEvent.MessageOneofCase.Panic)
                {
                    Console.Error.WriteLine($"FFI Panic: {ffiEvent.Panic.Message}");
                    Environment.Exit(1);
                    return;
                }

                List<TaskCompletionSource<FfiEvent>> toTrigger = new();

                lock (_lock)
                {
                    if (_disposed)
                        return;

                    // Match against active waiters
                    for (int i = _waiters.Count - 1; i >= 0; i--)
                    {
                        if (_waiters[i].Predicate(ffiEvent))
                        {
                            toTrigger.Add(_waiters[i].Tcs);
                            _waiters.RemoveAt(i);
                        }
                    }

                    // If no one is waiting for this specifically, queue it, unless it is a kind
                    // that is only ever consumed through EventReceived (room, track and stream
                    // fan-out events): those would fill the queue, evict pending callbacks and
                    // make every wait scan a thousand stale frames.
                    if (toTrigger.Count == 0 && IsClaimable(ffiEvent))
                    {
                        Enqueue(ffiEvent);
                    }
                }

                // Trigger TaskCompletionSources and Events OUTSIDE the lock to prevent deadlocks
                foreach (var tcs in toTrigger)
                {
                    // The waiter may have been cancelled or timed out meanwhile: keep the event
                    // for a later wait on the same predicate instead of dropping it.
                    if (!tcs.TrySetResult(ffiEvent))
                    {
                        lock (_lock)
                        {
                            Enqueue(ffiEvent);
                        }
                    }
                }
                RaiseEventReceived(ffiEvent);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error in FFI Callback: {ex}");
            }
        }

        // Caller holds _lock
        private void Enqueue(FfiEvent ffiEvent)
        {
            _eventQueue.Add(ffiEvent);
            if (_eventQueue.Count > 1000)
                _eventQueue.RemoveAt(0); // Circular buffer: remove oldest
        }

        private static bool IsClaimable(FfiEvent ffiEvent)
        {
            switch (ffiEvent.MessageCase)
            {
                case FfiEvent.MessageOneofCase.RoomEvent:
                case FfiEvent.MessageOneofCase.TrackEvent:
                case FfiEvent.MessageOneofCase.AudioStreamEvent:
                case FfiEvent.MessageOneofCase.VideoStreamEvent:
                case FfiEvent.MessageOneofCase.DataTrackStreamEvent:
                case FfiEvent.MessageOneofCase.RpcMethodInvocation:
                    return false;
                default:
                    return true;
            }
        }

        // One throwing subscriber must not hide the event from the others (a second Room, the
        // audio and video streams...)
        private void RaiseEventReceived(FfiEvent ffiEvent)
        {
            var handlers = EventReceived;
            if (handlers == null)
                return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<FfiEvent>)handler)(this, ffiEvent);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] FFI event subscriber threw: {ex}");
                }
            }
        }

        private void LogRecord(LogRecord record)
        {
            var level = record.Level switch
            {
                LogLevel.LogError => "ERROR",
                LogLevel.LogWarn => "WARN",
                LogLevel.LogInfo => "INFO",
                _ => "DEBUG",
            };
            Console.WriteLine($"[{level}] {record.Target} - {record.Message}");
        }

        /// <summary>
        /// Copies <paramref name="len"/> bytes from native memory. Implemented in managed code:
        /// the native library has no copy entry point (calling the old binding threw
        /// <see cref="EntryPointNotFoundException"/>).
        /// </summary>
        /// <param name="ptr">Address of the native buffer.</param>
        /// <param name="len">Number of bytes to copy.</param>
        /// <returns>A managed copy of the bytes.</returns>
        public byte[] CopyBuffer(ulong ptr, int len)
        {
            byte[] buffer = new byte[len];
            if (len > 0)
            {
                Marshal.Copy((IntPtr)ptr, buffer, 0, len);
            }
            return buffer;
        }

        /// <summary>
        /// Never worked: the native library has no such entry point, and a pointer to a managed
        /// array is only valid while the array is pinned. Pin the buffer around the request
        /// instead (see <c>AudioFrame.WithPinnedBuffer</c>).
        /// </summary>
        /// <param name="data">The managed array.</param>
        /// <returns>A pointer that is only valid while the array stays pinned.</returns>
        [Obsolete(
            "The native entry point does not exist and the returned pointer would not be pinned. Pin the buffer with 'fixed' around the FFI request instead."
        )]
        public unsafe ulong RetrievePtr(byte[] data)
        {
            fixed (byte* ptr = data)
                return NativeMethods.RetrievePtr(ptr, (UIntPtr)data.Length);
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
                Initialize();
        }

        /// <summary>
        /// Releases the native library. The SDK cannot be used in this process afterwards.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            lock (_initLock)
            {
                if (_disposed)
                    return;
                _disposed = true;

                lock (_lock)
                {
                    // Cancel all pending async waiters
                    foreach (var waiter in _waiters)
                        waiter.Tcs.TrySetCanceled();
                    _waiters.Clear();
                    _eventQueue.Clear();
                }

                if (_initialized)
                    NativeMethods.Dispose();
            }
        }
    }
}
