using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class TestingService
{
    private static TestSuiteExecution ApplyMutationRunContext(string repository, TestSuiteExecution execution, JsonElement run)
    {
        if (run.ValueKind != JsonValueKind.Object || !run.TryGetProperty("schemaVersion", out var schema)
            || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1)
            throw new InvalidDataException("Mutation run context requires schemaVersion 1.");
        var kind = RunText(run, "kind");
        if (kind is not ("canary" or "diagnostic" or "qualification"))
            throw new InvalidDataException("Unknown mutation run kind.");
        var partial = RunBoolean(run, "partial");
        if (partial != (kind != "qualification"))
            throw new InvalidDataException("Mutation run kind and partial-scope flag disagree.");
        var declaredQualified = RunBoolean(run, "qualified");
        var sourceUnchanged = RunBoolean(run, "sourceUnchanged");
        var scopeMatched = RunBoolean(run, "scopeMatched");
        var executionStatus = RunText(run, "executionStatus");
        if (!run.TryGetProperty("nativeExitCode", out var nativeExit))
            throw new InvalidDataException("Mutation run context lacks its native exit observation.");
        var normalExit = nativeExit.ValueKind == JsonValueKind.Number
            && nativeExit.TryGetInt32(out var exit) && exit is 0 or 2;
        if (!run.TryGetProperty("sourceIdentity", out var identity) || identity.ValueKind != JsonValueKind.Object
            || !identity.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array
            || inputs.GetArrayLength() is 0 or > 2048)
            throw new InvalidDataException("Mutation run context requires bounded source/configuration input pins.");
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var current = RunBoolean(identity, "unchanged") && sourceUnchanged;
        foreach (var input in inputs.EnumerateArray())
        {
            var relative = RunText(input, "path");
            var digest = RunText(input, "sha256");
            if (Path.IsPathFullyQualified(relative) || relative.Split('/', '\\').Any(segment => segment is ".." or ".")
                || digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidDataException("Mutation input identity contains an unsafe path or invalid digest.");
            var path = Path.GetFullPath(relative, repository);
            if (!CisPathSafety.IsUnderRoot(repository, path, allowRoot: false)
                || CisPathSafety.ContainsReparsePoint(repository, path) || !paths.Add(path))
                throw new InvalidDataException("Mutation input identity is linked, duplicated, or outside the repository.");
            if (!File.Exists(path)) { current = false; continue; }
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), digest, StringComparison.OrdinalIgnoreCase))
                current = false;
        }
        var diagnostics = execution.Diagnostics.ToList();
        var runId = RunText(run, "runId");
        var profileId = RunText(run, "profileId");
        if (profileId.Length > 64 || profileId.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-'))
            throw new InvalidDataException("Mutation run context has an invalid profile id.");
        var latestPath = Path.Combine(repository, MutationRunService.LocalRoot, profileId, "latest-run.json");
        if (CisPathSafety.ContainsReparsePoint(repository, latestPath) || !File.Exists(latestPath)
            || new FileInfo(latestPath).Length > 16384)
            throw new InvalidDataException("Mutation run context requires a bounded, unlinked latest-attempt marker.");
        using var latest = JsonDocument.Parse(File.ReadAllBytes(latestPath));
        var latestRun = latest.RootElement;
        if (latestRun.ValueKind != JsonValueKind.Object || !latestRun.TryGetProperty("schemaVersion", out var latestSchema)
            || latestSchema.ValueKind != JsonValueKind.Number || !latestSchema.TryGetInt32(out var latestVersion) || latestVersion != 1)
            throw new InvalidDataException("Latest mutation attempt requires schemaVersion 1.");
        var sameAttempt = RunText(latestRun, "profileId") == profileId && RunText(latestRun, "runId") == runId;
        if (!sameAttempt)
        {
            current = false;
            diagnostics.Add("A later mutation attempt superseded this report; its counts remain historical.");
        }
        var completedAttempt = RunText(latestRun, "status") is "qualified" or "canary-passed" or "diagnostic-completed"
            && latestRun.TryGetProperty("exitCode", out var latestExit) && latestExit.ValueKind == JsonValueKind.Number
            && latestExit.TryGetInt32(out var latestExitCode) && latestExitCode == 0;
        var healthy = executionStatus == "exited" && normalExit && sameAttempt && completedAttempt;
        if (!partial) declaredQualified &= RunBoolean(latestRun, "qualified");
        if (RunBoolean(run, "healthCheckConfigured"))
        {
            if (!run.TryGetProperty("healthCheckExecution", out var health) || health.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Configured mutation health check has no execution observation.");
            healthy &= RunText(health, "status") == "exited" && health.TryGetProperty("exitCode", out var code)
                && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var healthExit) && healthExit == 0;
        }
        if (partial) diagnostics.Add($"The {kind} score is diagnostic; the complete required scope remains unqualified.");
        if (!current) diagnostics.Add("Source, configuration, or profile input changed since mutation execution; these counts describe an earlier snapshot.");
        if (!healthy) diagnostics.Add("Native mutation execution or its configured health check failed; report counts do not clear that failure.");
        if (!partial && !scopeMatched) diagnostics.Add("The reported source scope does not match the declared qualification profile.");
        if (!partial && !declaredQualified) diagnostics.Add("The producing mutation run did not qualify its declared scope.");
        var qualified = execution.Mutation?.Qualified == true && declaredQualified && !partial && current && healthy && scopeMatched;
        var status = !current ? "stale" : !healthy || !partial && (!scopeMatched || !declaredQualified) ? "failed"
            : partial ? "partial" : execution.Status;
        var failure = !current ? TestFailureKind.InvalidEvidence : !healthy
            ? executionStatus == "timeout" ? TestFailureKind.Timeout : TestFailureKind.Infrastructure
            : partial ? TestFailureKind.MissingPrerequisite : status == "failed" ? TestFailureKind.InvalidEvidence : execution.FailureKind;
        return execution with
        {
            Status = status,
            FailureKind = failure,
            Mutation = execution.Mutation is { } mutation ? mutation with
            {
                Qualified = qualified,
                Scope = partial ? $"{kind}-declared-files" : "declared-profile-files",
            } : null,
            Diagnostics = diagnostics,
        };
    }

    private static string RunText(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidDataException($"Mutation run context requires a nonempty '{name}'.");
        return property.GetString()!;
    }

    private static bool RunBoolean(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Mutation run context requires Boolean '{name}'.");
        return property.GetBoolean();
    }
}
