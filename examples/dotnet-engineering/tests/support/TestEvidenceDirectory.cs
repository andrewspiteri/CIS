namespace Example.TestSupport;

internal static class TestEvidenceDirectory
{
    internal static string Create(string repository, string fallback)
    {
        var run = Environment.GetEnvironmentVariable("CIS_WORKFLOW_RUN_ID");
        var suite = Environment.GetEnvironmentVariable("CIS_WORKFLOW_STEP_ID");
        var attempt = Environment.GetEnvironmentVariable("CIS_WORKFLOW_ATTEMPT");
        var parts = new[] { run, suite, attempt };
        var correlated = parts.All(part => !string.IsNullOrWhiteSpace(part) && part.Length <= 100
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
        var directory = correlated
            ? Path.Combine(repository, ".cis/local/testing/diagnostics", suite!, run!, "attempt-" + attempt)
            : Path.Combine(repository, ".cis/local", fallback);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
