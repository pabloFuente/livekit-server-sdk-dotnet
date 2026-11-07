// author: https://github.com/pabloFuente

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Scale of the reliability suite. The default (pull request) level keeps every loop short; the
/// extended level, selected with <c>LIVEKIT_STRESS=extended</c>, runs the long loops and the
/// tests marked <see cref="ExtendedFactAttribute"/> (scheduled CI and local investigation).
/// </summary>
internal static class StressLevel
{
    public const string EnvironmentVariable = "LIVEKIT_STRESS";

    public static bool Extended { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            "extended",
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// The iteration count for the current level.
    /// </summary>
    public static int Scale(int pullRequest, int extended) => Extended ? extended : pullRequest;
}

/// <summary>
/// A test of the extended reliability tier: long-running (server outages, thousands of cycles),
/// skipped unless <c>LIVEKIT_STRESS=extended</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ExtendedFactAttribute : FactAttribute
{
    public ExtendedFactAttribute()
    {
        if (!StressLevel.Extended)
            Skip =
                $"Extended reliability test: set {StressLevel.EnvironmentVariable}=extended to run it";
    }
}

/// <summary>
/// The reliability tests that load the native library but start no container. They run serially
/// (nothing else in the process allocates handles meanwhile), so the process-wide handle, memory
/// and log measurements belong to the test that takes them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeReliabilityCollection
{
    public const string Name = "RTC reliability (native, serial)";
}

/// <summary>
/// The reliability tests that need a LiveKit server. Serial for the same reason as
/// <see cref="NativeReliabilityCollection"/>, and because some of them pause the server.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RoomReliabilityCollection : ICollectionFixture<ReliabilityServerFixture>
{
    public const string Name = "RTC reliability (LiveKit server, serial)";
}
