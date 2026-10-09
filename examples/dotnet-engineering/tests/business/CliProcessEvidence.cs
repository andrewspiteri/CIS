using Npgsql;

namespace Example.BusinessTests;

/// <summary>Persists sanitized partial output with a cleanup deadline independent of test cancellation.</summary>
internal static class CliProcessEvidence
{
    internal static async Task RetainAsync(BusinessEvidence? evidence, ProcessDiagnostic diagnostic, string? connectionString)
    {
        if (evidence is null) return;
        var password = string.IsNullOrEmpty(connectionString) ? null : new NpgsqlConnectionStringBuilder(connectionString).Password;
        string Redact(string value)
        {
            value = RedactSecret(value, connectionString, diagnostic.Failure is not null);
            value = RedactSecret(value, password, diagnostic.Failure is not null);
            return value.Length > BoundedProcessOutput.Limit ? value[..BoundedProcessOutput.Limit] : value;
        }
        using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await evidence.WriteDiagnosticAsync("cli-" + Guid.NewGuid().ToString("N") + ".json",
            new { exitCode = diagnostic.ExitCode, processId = diagnostic.ProcessId, failure = diagnostic.Failure, cleanupFailure = diagnostic.CleanupFailure, stdout = Redact(diagnostic.Stdout), stderr = Redact(diagnostic.Stderr) }, persistence.Token);
    }

    private static string RedactSecret(string value, string? secret, bool partial)
    {
        if (string.IsNullOrEmpty(secret)) return value;
        value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        if (!partial) return value;
        for (var length = Math.Min(secret.Length - 1, value.Length); length > 0; length--)
        {
            if (value.AsSpan().EndsWith(secret.AsSpan(0, length), StringComparison.Ordinal))
                return value[..^length] + "[redacted-partial]";
        }
        return value;
    }
}
