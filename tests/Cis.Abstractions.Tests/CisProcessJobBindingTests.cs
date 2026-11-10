using System.Diagnostics;
using System.Text.Json;

namespace Cis.Abstractions.Tests;

public sealed class CisProcessJobBindingTests
{
    [Fact]
    public void NativeJob_RecordedHandlePathBindsEmptyAndDestroyedInspection()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Requires actual Windows named-job inspection."); return; }
        using var job = new CisProcessJob(@"Local\cis-native-" + Guid.NewGuid().ToString("N"));
        var recorded = job.CaptureNativeContainment();
        Assert.NotNull(recorded.WindowsObjectPath);
        Assert.StartsWith("\\", recorded.WindowsObjectPath, StringComparison.Ordinal);
        Assert.Equal(recorded, JsonSerializer.Deserialize<CisNativeWorkContainment>(JsonSerializer.Serialize(recorded)));
        Assert.True(CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(recorded));

        job.Dispose();

        Assert.True(CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(recorded));
    }

    [Fact]
    public void NativeJob_LegacyMissingNamespaceHoldsEvenWhenCurrentLocalNameIsMissing()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Requires actual Windows named-job inspection."); return; }
        using var current = Process.GetCurrentProcess();
        var historical = new CisNativeWorkContainment(CisNativeWorkContainment.WindowsOuterJob,
            @"Local\cis-native-" + Guid.NewGuid().ToString("N"), current.SessionId);
        var before = JsonSerializer.Serialize(historical);

        var error = Assert.Throws<InvalidDataException>(() =>
        {
            if (OperatingSystem.IsWindows()) CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(historical);
        });

        Assert.Contains("original Windows named-object namespace was not recorded", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
        {
            if (OperatingSystem.IsWindows()) CisProcessJob.IsNamedNativeJobMember(historical, current);
        });
        Assert.Equal(before, JsonSerializer.Serialize(historical));
        Assert.Null(historical.WindowsObjectPath);
    }

    [Fact]
    public void NativeJob_DifferentRecordedNamespaceHoldsForBothFoundAndMissingNames()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Requires actual Windows named-job inspection."); return; }
        using var job = new CisProcessJob(@"Local\cis-native-" + Guid.NewGuid().ToString("N"));
        var recorded = job.CaptureNativeContainment();
        var path = Assert.IsType<string>(recorded.WindowsObjectPath);
        var split = path.LastIndexOf('\\');
        // Altered retained binding tests rejection; it is not an actual second
        // AppContainer/silo execution or qualification of cross-context access.
        var other = recorded with { WindowsObjectPath = path[..split] + @"\different-original-namespace" + path[split..] };
        using var current = Process.GetCurrentProcess();

        Assert.Throws<InvalidDataException>(() =>
        {
            if (OperatingSystem.IsWindows()) CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(other);
        });
        Assert.Throws<InvalidDataException>(() =>
        {
            if (OperatingSystem.IsWindows()) CisProcessJob.IsNamedNativeJobMember(other, current);
        });
        Assert.True(CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(recorded));
        job.Dispose();
        var error = Assert.Throws<InvalidDataException>(() =>
        {
            if (OperatingSystem.IsWindows()) CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(other);
        });
        Assert.Contains("namespace differs", error.Message, StringComparison.Ordinal);
        Assert.True(CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(recorded));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("session")]
    [InlineData("name")]
    [InlineData("path")]
    public void NativeJob_InvalidRetainedBindingsNeverProveClosure(string defect)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Requires actual Windows named-job inspection."); return; }
        using var job = new CisProcessJob(@"Local\cis-native-" + Guid.NewGuid().ToString("N"));
        var original = job.CaptureNativeContainment();
        var invalid = defect switch
        {
            "protocol" => original with { Protocol = "unproved-outer-job" },
            "session" => original with { WindowsSessionId = original.WindowsSessionId + 1 },
            "name" => original with { JobName = @"Local\unrelated-job" },
            _ => original with { WindowsObjectPath = "unqualified-original-name" },
        };

        Assert.Throws<InvalidDataException>(() =>
        {
            if (OperatingSystem.IsWindows()) CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(invalid);
        });
        Assert.True(CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(original));
    }
}
