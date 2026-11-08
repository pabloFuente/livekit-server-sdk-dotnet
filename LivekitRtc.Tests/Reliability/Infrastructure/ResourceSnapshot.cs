// author: https://github.com/pabloFuente

using System.Diagnostics;
using System.Runtime.InteropServices;
using LiveKit.Rtc.Internal;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Process-wide resource counters, taken after a full garbage collection.
/// <para>
/// The primary signal is the SDK's own handle accounting (<see cref="FfiHandle.LiveCount"/> and
/// <see cref="FfiHandle.FailedReleaseCount"/>): it says whether the .NET layer released every
/// native handle it wrapped, exactly once. The other counters are coarse end-to-end guards for
/// what the handle accounting cannot see, such as an owned handle the SDK received but never
/// wrapped (its native buffer stays allocated) or a room that was never closed (its sockets and
/// threads stay open). The native heap is read from the C allocator (glibc <c>mallinfo2</c> on
/// Linux, <c>malloc_zone_statistics</c> on macOS) that both the Rust FFI and libwebrtc use; on
/// Windows only the private bytes of the process are available, which are much noisier.
/// </para>
/// </summary>
internal readonly record struct ResourceSnapshot(
    long LiveHandles,
    long FailedReleases,
    long ManagedBytes,
    long? NativeHeapBytes,
    long? OpenHandles,
    int Threads,
    long PrivateBytes
)
{
    public static ResourceSnapshot Capture()
    {
        CollectGarbage();
        using var process = Process.GetCurrentProcess();
        return new ResourceSnapshot(
            FfiHandle.LiveCount,
            FfiHandle.FailedReleaseCount,
            GC.GetTotalMemory(forceFullCollection: false),
            NativeHeap.AllocatedBytes(),
            CountOpenHandles(process),
            process.Threads.Count,
            process.PrivateMemorySize64
        );
    }

    /// <summary>
    /// Runs the finalizers of everything unreachable, so the handles of objects that were never
    /// disposed are released before a measurement.
    /// </summary>
    public static void CollectGarbage()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    // File descriptors on Linux and macOS (sockets included), kernel handles on Windows
    private static long? CountOpenHandles(Process process)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                return Directory.EnumerateFileSystemEntries("/proc/self/fd").LongCount();
            if (OperatingSystem.IsMacOS())
                return Directory.EnumerateFileSystemEntries("/dev/fd").LongCount();
            if (OperatingSystem.IsWindows())
                return process.HandleCount;
        }
        catch (Exception)
        {
            // Not available in this environment
        }
        return null;
    }

    public string Describe(ResourceSnapshot baseline) =>
        $"handles {LiveHandles} ({LiveHandles - baseline.LiveHandles:+#;-#;0}), "
        + $"failed releases {FailedReleases} ({FailedReleases - baseline.FailedReleases:+#;-#;0}), "
        + $"managed {Mb(ManagedBytes)} ({Mb(ManagedBytes - baseline.ManagedBytes)}), "
        + $"native heap {Mb(NativeHeapBytes)} ({Mb(NativeHeapBytes - baseline.NativeHeapBytes)}), "
        + $"open handles {OpenHandles} ({OpenHandles - baseline.OpenHandles:+#;-#;0}), "
        + $"threads {Threads} ({Threads - baseline.Threads:+#;-#;0}), "
        + $"private {Mb(PrivateBytes)} ({Mb(PrivateBytes - baseline.PrivateBytes)})";

    private static string Mb(long? bytes) =>
        bytes == null ? "n/a" : $"{bytes.Value / (1024.0 * 1024.0):0.##} MB";

    /// <summary>
    /// Waits (bounded) until the process is back within <paramref name="budget"/> of
    /// <paramref name="baseline"/>, and fails with both snapshots otherwise. Native teardown is
    /// partly asynchronous (a closed room releases its sockets from a background task), so the
    /// check is retried instead of taken once.
    /// </summary>
    public static async Task AssertWithinAsync(
        ResourceSnapshot baseline,
        LeakBudget budget,
        ITestOutputHelper output,
        string what,
        TimeSpan? settle = null
    )
    {
        var deadline = Stopwatch.StartNew();
        var timeout = settle ?? TimeSpan.FromSeconds(10);
        ResourceSnapshot current;
        string? violation;
        while (true)
        {
            current = Capture();
            violation = budget.Violation(baseline, current);
            if (violation == null || deadline.Elapsed > timeout)
                break;
            await Task.Delay(100);
        }

        output.WriteLine($"[{what}] baseline: {baseline.Describe(baseline)}");
        output.WriteLine($"[{what}] after:    {current.Describe(baseline)}");
        Assert.True(
            violation == null,
            $"{what}: {violation}{Environment.NewLine}baseline: {baseline.Describe(baseline)}"
                + $"{Environment.NewLine}after:    {current.Describe(baseline)}"
        );
    }
}

/// <summary>
/// Allowed growth between two snapshots. A double release is never allowed. Budgets are sized so
/// that a leak of one handle, buffer or socket per cycle exceeds them by far, while allocator and
/// thread-pool noise does not.
/// </summary>
internal sealed class LeakBudget
{
    public long Handles { get; init; }
    public long NativeHeapBytes { get; init; } = 16L * 1024 * 1024;
    public long ManagedBytes { get; init; } = 32L * 1024 * 1024;

    // Windows counts every kernel handle (events, thread pool waits), which varies more than the
    // file descriptors of Linux and macOS
    public long OpenHandles { get; init; } = OperatingSystem.IsWindows() ? 96 : 16;
    public int Threads { get; init; } = 24;

    // Used instead of the native heap where it cannot be measured (Windows)
    public long PrivateBytes { get; init; } = 160L * 1024 * 1024;

    public string? Violation(ResourceSnapshot baseline, ResourceSnapshot current)
    {
        if (current.FailedReleases != baseline.FailedReleases)
            return $"{current.FailedReleases - baseline.FailedReleases} native handle(s) were released twice or by the wrong owner";
        if (current.LiveHandles - baseline.LiveHandles > Handles)
            return $"{current.LiveHandles - baseline.LiveHandles} native handle(s) were never released (budget {Handles})";
        if (current.ManagedBytes - baseline.ManagedBytes > ManagedBytes)
            return $"the managed heap grew by {current.ManagedBytes - baseline.ManagedBytes} bytes (budget {ManagedBytes})";
        if (current.NativeHeapBytes != null && baseline.NativeHeapBytes != null)
        {
            if (current.NativeHeapBytes - baseline.NativeHeapBytes > NativeHeapBytes)
                return $"the native heap grew by {current.NativeHeapBytes - baseline.NativeHeapBytes} bytes (budget {NativeHeapBytes})";
        }
        else if (current.PrivateBytes - baseline.PrivateBytes > PrivateBytes)
        {
            return $"the private bytes grew by {current.PrivateBytes - baseline.PrivateBytes} (budget {PrivateBytes})";
        }
        if (
            current.OpenHandles != null
            && baseline.OpenHandles != null
            && current.OpenHandles - baseline.OpenHandles > OpenHandles
        )
            return $"{current.OpenHandles - baseline.OpenHandles} more open file descriptors/handles (budget {OpenHandles})";
        if (current.Threads - baseline.Threads > Threads)
            return $"{current.Threads - baseline.Threads} more threads (budget {Threads})";
        return null;
    }
}

/// <summary>
/// Bytes currently allocated through the C allocator of the process, or null where it cannot be
/// read precisely.
/// </summary>
internal static unsafe class NativeHeap
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MallInfo2
    {
        public nuint Arena;
        public nuint Ordblks;
        public nuint Smblks;
        public nuint Hblks;
        public nuint Hblkhd; // bytes in mmapped regions
        public nuint Usmblks;
        public nuint Fsmblks;
        public nuint Uordblks; // bytes allocated from the arenas
        public nuint Fordblks;
        public nuint Keepcost;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MallocStatistics
    {
        public uint BlocksInUse;
        public nuint SizeInUse;
        public nuint MaxSizeInUse;
        public nuint SizeAllocated;
    }

    private static readonly delegate* unmanaged<MallInfo2> s_mallinfo2 = LoadMallinfo2();
    private static readonly delegate* unmanaged<IntPtr, MallocStatistics*, void> s_zoneStatistics =
        LoadZoneStatistics();

    public static string Source =>
        s_mallinfo2 != null ? "glibc mallinfo2"
        : s_zoneStatistics != null ? "malloc_zone_statistics"
        : "unavailable";

    public static long? AllocatedBytes()
    {
        if (s_mallinfo2 != null)
        {
            var info = s_mallinfo2();
            return (long)(info.Uordblks + info.Hblkhd);
        }
        if (s_zoneStatistics != null)
        {
            MallocStatistics stats;
            s_zoneStatistics(IntPtr.Zero, &stats); // NULL: every malloc zone
            return (long)stats.SizeInUse;
        }
        return null;
    }

    private static delegate* unmanaged<MallInfo2> LoadMallinfo2()
    {
        if (
            OperatingSystem.IsLinux()
            && NativeLibrary.TryLoad("libc.so.6", out var libc)
            && NativeLibrary.TryGetExport(libc, "mallinfo2", out var export)
        )
            return (delegate* unmanaged<MallInfo2>)export;
        return null;
    }

    private static delegate* unmanaged<IntPtr, MallocStatistics*, void> LoadZoneStatistics()
    {
        if (
            OperatingSystem.IsMacOS()
            && NativeLibrary.TryLoad("/usr/lib/libSystem.B.dylib", out var system)
            && NativeLibrary.TryGetExport(system, "malloc_zone_statistics", out var export)
        )
            return (delegate* unmanaged<IntPtr, MallocStatistics*, void>)export;
        return null;
    }
}
