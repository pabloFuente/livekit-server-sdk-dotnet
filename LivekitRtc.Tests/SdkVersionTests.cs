// author: https://github.com/pabloFuente

using LiveKit.Rtc.Internal;
using Xunit;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Guards that the SDK version reported to the FFI follows the package version in
/// LivekitRtc.csproj instead of a hard-coded constant that drifts on every release.
/// </summary>
public class SdkVersionTests
{
    [Fact]
    public void SdkVersion_FollowsAssemblyVersion()
    {
        // AssemblyVersion is stamped from the same csproj <Version> property, through a different
        // attribute than the one SdkVersion reads, so this catches a hard-coded value.
        var assemblyVersion = typeof(FfiClient).Assembly.GetName().Version;
        Assert.NotNull(assemblyVersion);

        Assert.StartsWith(
            $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}",
            FfiClient.SdkVersion
        );
    }

    [Fact]
    public void SdkVersion_HasNoBuildMetadata()
    {
        // SourceLink appends "+<commit sha>" to the informational version; the FFI must not see it
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", FfiClient.SdkVersion);
    }
}
