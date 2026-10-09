using Example.TestSupport;

namespace Example.UnitTests;

public sealed class DiagnosticCaptureTests(ITestOutputHelper output)
{
    [Fact]
    public void DiagnosticCaptureIsBoundedAndRedactsKnownCredentialFields()
    {
        using var signals = new TestSignals(output);
        for (var index = 0; index < 300; index++)
            signals.Log(Microsoft.Extensions.Logging.LogLevel.Debug, new(1), "Password=fixture-secret; token=fixture-token; Authorization: Bearer fixture-bearer", null, (state, _) => state);
        Assert.Equal(256, signals.Logs.Count);
        Assert.All(signals.Logs, log =>
        {
            Assert.DoesNotContain("fixture-secret", log, StringComparison.Ordinal);
            Assert.DoesNotContain("fixture-token", log, StringComparison.Ordinal);
            Assert.DoesNotContain("fixture-bearer", log, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", log, StringComparison.Ordinal);
        });
        using var journal = new StreamReader(new FileStream(signals.JournalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var retained = journal.ReadToEnd();
        Assert.DoesNotContain("fixture-secret", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-token", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-bearer", retained, StringComparison.Ordinal);
    }

}
