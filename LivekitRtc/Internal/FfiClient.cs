// author: https://github.com/pabloFuente

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveKit.Proto;

namespace LiveKit.Rtc.Internal
{
    /// <summary>
    /// Main FFI client for communicating with the LiveKit Rust SDK.
    /// Thread-safe singleton that handles all FFI requests and events.
    /// </summary>
    public sealed class FfiClient : IDisposable
    {
        private static readonly Lazy<FfiClient> _instance = new Lazy<FfiClient>(
            () => new FfiClient(),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        /// <summary>
        /// Gets the singleton instance of the FfiClient.
        /// </summary>
        public static FfiClient Instance => _instance.Value;

        private readonly FfiCallbackDelegate _callbackDelegate;
        private readonly ConcurrentDictionary<
            ulong,
            TaskCompletionSource<FfiEvent>
        > _pendingRequests;
        private readonly ConcurrentQueue<FfiEvent> _eventQueue;
        private readonly object _queueLock = new object(); // Lock for queue operations to prevent race conditions
        private bool _initialized;
        private bool _disposed;
        private readonly object _initLock = new object();

        /// <summary>
        /// Event raised when an FFI event is received from the native library.
        /// </summary>
        public event EventHandler<FfiEvent>? EventReceived;

        /// <summary>
        /// SDK version string.
        /// </summary>
        public const string SdkVersion = "0.1.0";

        /// <summary>
        /// SDK identifier string.
        /// </summary>
        public const string SdkName = "dotnet";

        private FfiClient()
        {
            _pendingRequests = new ConcurrentDictionary<ulong, TaskCompletionSource<FfiEvent>>();
            _eventQueue = new ConcurrentQueue<FfiEvent>();

            // Keep a reference to prevent GC
            _callbackDelegate = OnFfiCallback;
        }

        /// <summary>
        /// Initializes the FFI client. Safe to call multiple times.
        /// </summary>
        /// <param name="captureLogs">Whether to capture logs from the native library.</param>
        public void Initialize(bool captureLogs = false)
        {
            if (_initialized)
                return;

            lock (_initLock)
            {
                if (_initialized)
                    return;

                NativeMethods.Initialize(_callbackDelegate, captureLogs, SdkName, SdkVersion);
                _initialized = true;
            }
        }

        /// <summary>
        /// Sends a synchronous FFI request and returns the response.
        /// </summary>
        /// <param name="request">The FFI request to send.</param>
        /// <returns>The FFI response.</returns>
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
                {
                    throw new InvalidOperationException(
                        "FFI request failed: invalid handle returned"
                    );
                }

                try
                {
                    byte[] responseData = new byte[(int)responseLen];
                    Marshal.Copy((IntPtr)responsePtr, responseData, 0, (int)responseLen);

                    return FfiResponse.Parser.ParseFrom(responseData);
                }
                finally
                {
                    // Drop the response handle
                    NativeMethods.DropHandle(handleId);
                }
            }
        }

        /// <summary>
        /// Waits for an FFI event matching the specified predicate.
        /// </summary>
        /// <param name="predicate">Predicate to match events.</param>
        /// <param name="timeout">Timeout for waiting.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The matching FFI event.</returns>
        public async Task<FfiEvent> WaitForEventAsync(
            Func<FfiEvent, bool> predicate,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var tcs = new TaskCompletionSource<FfiEvent>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            void Handler(object? sender, FfiEvent e)
            {
                if (predicate(e))
                {
                    EventReceived -= Handler;
                    tcs.TrySetResult(e);
                }
            }

            // CRITICAL FIX: Check the event queue for already-received events BEFORE subscribing
            // This prevents a race condition where the event arrives between SendRequest and WaitForEventAsync
            // We need to lock to prevent events from being enqueued while we're checking
            // AND we must subscribe to EventReceived while still holding the lock to prevent
            // missing events that arrive between queue check and subscription
            lock (_queueLock)
            {
                int queueSize = _eventQueue.Count;
                for (int i = 0; i < queueSize; i++)
                {
                    if (_eventQueue.TryDequeue(out var queuedEvent))
                    {
                        if (predicate(queuedEvent))
                        {
                            return queuedEvent;
                        }
                        // Re-enqueue non-matching event
                        _eventQueue.Enqueue(queuedEvent);
                    }
                }

                // Subscribe to future events while still holding the lock
                // This ensures no events can arrive between our queue check and subscription
                EventReceived += Handler;
            }

            try
            {
                using var cts = timeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                if (timeout.HasValue)
                {
                    cts.CancelAfter(timeout.Value);
                }

                // Register cancellation to complete the TCS
                using var registration = cts.Token.Register(() =>
                {
                    EventReceived -= Handler;
                    tcs.TrySetCanceled(cts.Token);
                });

                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred (not external cancellation)
                throw new TimeoutException(
                    $"Timeout waiting for FFI event after {timeout?.TotalSeconds ?? 0} seconds"
                );
            }
            finally
            {
                EventReceived -= Handler;
            }
        }

        /// <summary>
        /// Copies a buffer from native memory.
        /// </summary>
        /// <param name="ptr">Pointer to the native buffer.</param>
        /// <param name="len">Length of the buffer.</param>
        /// <returns>The copied buffer.</returns>
        public unsafe byte[] CopyBuffer(ulong ptr, int len)
        {
            byte[] buffer = new byte[len];
            fixed (byte* dst = buffer)
            {
                NativeMethods.CopyBuffer(ptr, (UIntPtr)len, dst);
            }
            return buffer;
        }

        /// <summary>
        /// Retrieves a pointer from data.
        /// </summary>
        /// <param name="data">The data buffer.</param>
        /// <returns>The pointer value.</returns>
        public unsafe ulong RetrievePtr(byte[] data)
        {
            fixed (byte* ptr = data)
            {
                return NativeMethods.RetrievePtr(ptr, (UIntPtr)data.Length);
            }
        }

        private void OnFfiCallback(IntPtr dataPtr, UIntPtr dataLen)
        {
            try
            {
                byte[] eventData = new byte[(int)dataLen];
                Marshal.Copy(dataPtr, eventData, 0, (int)dataLen);

                FfiEvent ffiEvent = FfiEvent.Parser.ParseFrom(eventData);

                // Handle logs separately
                if (ffiEvent.MessageCase == FfiEvent.MessageOneofCase.Logs)
                {
                    foreach (var record in ffiEvent.Logs.Records)
                    {
                        LogRecord(record);
                    }
                    return;
                }

                // Handle panic
                if (ffiEvent.MessageCase == FfiEvent.MessageOneofCase.Panic)
                {
                    Console.Error.WriteLine($"FFI Panic: {ffiEvent.Panic.Message}");
                    Environment.Exit(1);
                    return;
                }

                // Queue the event and raise the event handler
                lock (_queueLock)
                {
                    _eventQueue.Enqueue(ffiEvent);
                }
                EventReceived?.Invoke(this, ffiEvent);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing FFI callback: {ex}");
            }
        }

        private void LogRecord(LogRecord record)
        {
            var level = record.Level switch
            {
                LogLevel.LogError => "ERROR",
                LogLevel.LogWarn => "WARN",
                LogLevel.LogInfo => "INFO",
                LogLevel.LogDebug => "DEBUG",
                LogLevel.LogTrace => "TRACE",
                _ => "UNKNOWN",
            };

            Console.WriteLine($"[{level}] {record.Target}:{record.Line} - {record.Message}");
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Disposes the FFI client and releases native resources.
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

                if (_initialized)
                {
                    NativeMethods.Dispose();
                    _initialized = false;
                }
            }
        }
    }
}
