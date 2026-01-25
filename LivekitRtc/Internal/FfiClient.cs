// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveKit.Proto;

namespace LiveKit.Rtc.Internal
{
    public sealed class FfiClient : IDisposable
    {
        private static readonly Lazy<FfiClient> _instance = new Lazy<FfiClient>(
            () => new FfiClient(),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

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

        public const string SdkVersion = "0.1.0";
        public const string SdkName = "dotnet";

        private FfiClient()
        {
            _callbackDelegate = OnFfiCallback;
        }

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
        public unsafe FfiResponse SendRequest(FfiRequest request)
        {
            EnsureInitialized();

            byte[] requestData = request.ToByteArray();
            fixed (byte* dataPtr = requestData)
            {
                ulong handleId = NativeMethods.Request(
                    dataPtr,
                    requestData.Length,
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

                    // If no one is waiting for this specifically, queue it
                    if (toTrigger.Count == 0)
                    {
                        _eventQueue.Add(ffiEvent);
                        if (_eventQueue.Count > 1000)
                            _eventQueue.RemoveAt(0); // Circular buffer: remove oldest
                    }
                }

                // Trigger TaskCompletionSources and Events OUTSIDE the lock to prevent deadlocks
                foreach (var tcs in toTrigger)
                    tcs.TrySetResult(ffiEvent);
                EventReceived?.Invoke(this, ffiEvent);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error in FFI Callback: {ex}");
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

        public unsafe byte[] CopyBuffer(ulong ptr, int len)
        {
            byte[] buffer = new byte[len];
            fixed (byte* dst = buffer)
                NativeMethods.CopyBuffer(ptr, (UIntPtr)len, dst);
            return buffer;
        }

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
