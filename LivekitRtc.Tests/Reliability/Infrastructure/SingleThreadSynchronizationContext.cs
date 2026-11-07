// author: https://github.com/pabloFuente

using System.Collections.Concurrent;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// A synchronization context with one thread, like the UI thread of a WPF, WinForms or MAUI app:
/// continuations posted to it run only when that thread is free. Calling one of the SDK's
/// synchronous methods (<c>Room.Dispose</c>, <c>Room.Disconnect</c>,
/// <c>AudioSource.CaptureFrame</c>) from such a thread deadlocks if the SDK resumes on the
/// captured context, which xUnit's own multi-threaded context would never reveal.
/// </summary>
internal sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue =
        new();

    private int _posted;

    public int Posted => Volatile.Read(ref _posted);

    public override void Post(SendOrPostCallback d, object? state)
    {
        Interlocked.Increment(ref _posted);
        _queue.Add((d, state));
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Send is not used by the code under test");

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Runs <paramref name="blockingCall"/> on a dedicated thread that owns a new context, the
    /// way a UI event handler would. Returns false if it did not return within
    /// <paramref name="timeout"/>, a deadlock: the callbacks queued for the blocked thread are
    /// then run on the calling thread, so the call completes and releases what it holds instead
    /// of leaving a thread stuck for the rest of the run.
    /// </summary>
    public static bool TryRunBlocking(
        Action blockingCall,
        TimeSpan timeout,
        out Exception? error,
        out int postedToContext
    )
    {
        var context = new SingleThreadSynchronizationContext();
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            SetSynchronizationContext(context);
            try
            {
                blockingCall();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        })
        {
            IsBackground = true,
            Name = "UI-like thread",
        };
        thread.Start();

        var completed = thread.Join(timeout);
        if (!completed)
        {
            var release = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (thread.IsAlive && DateTime.UtcNow < release)
            {
                if (context._queue.TryTake(out var item, 50))
                    item.Callback(item.State);
            }
        }

        error = caught;
        postedToContext = context.Posted;
        return completed;
    }
}
