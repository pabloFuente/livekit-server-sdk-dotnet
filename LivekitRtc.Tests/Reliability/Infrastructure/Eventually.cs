// author: https://github.com/pabloFuente

using System.Diagnostics;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Bounded waits on a condition, for state that settles asynchronously (the server's participant
/// list, a background release). Never a fixed sleep: the wait ends as soon as the condition holds.
/// </summary>
internal static class Eventually
{
    public static Task TrueAsync(Func<bool> condition, TimeSpan timeout, string what) =>
        TrueAsync(() => Task.FromResult(condition()), timeout, what);

    /// <summary>
    /// <paramref name="what"/> is built at the timeout, so it can describe the state then.
    /// </summary>
    public static async Task TrueAsync(Func<bool> condition, TimeSpan timeout, Func<string> what)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > timeout)
                Assert.Fail($"Timed out after {timeout.TotalSeconds:0.#} s waiting until {what()}");
            await Task.Delay(25);
        }
    }

    public static async Task TrueAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > timeout)
                Assert.Fail($"Timed out after {timeout.TotalSeconds:0.#} s waiting until {what}");
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// Awaits <paramref name="task"/>, failing with <paramref name="what"/> instead of hanging if
    /// it does not complete in time (a deadlock or a lost callback).
    /// </summary>
    public static async Task<T> Within<T>(this Task<T> task, TimeSpan timeout, string what)
    {
        var winner = await Task.WhenAny(task, Task.Delay(timeout));
        if (winner != task)
            Assert.Fail($"Did not complete within {timeout.TotalSeconds:0.#} s: {what}");
        return await task;
    }

    public static async Task Within(this Task task, TimeSpan timeout, string what)
    {
        var winner = await Task.WhenAny(task, Task.Delay(timeout));
        if (winner != task)
            Assert.Fail($"Did not complete within {timeout.TotalSeconds:0.#} s: {what}");
        await task;
    }

    public static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
