using System.Xml.Linq;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private const string ChangedExecutionInputs = "Relevant inputs changed during execution; this result cannot be reused.";
    private static TestSuiteExecution ReadUnit(Input input, string path)
    {
        Require(new FileInfo(path).Length is > 0 and <= 100 * 1024 * 1024, "Unit report must contain 1 byte through 100 MiB.");
        var profile = new TestSuiteProfile(input.Profile.Id, input.Profile.Id, "unit", "native", "run", input.Profile.WorkingDirectory,
            input.Profile.UnitResultFormat, path, "-", "-", "-", "always", "local", "current-result");
        var context = new TestResultAdapterContext(input.Repository, profile, path, null, null);
        var xml = XDocument.Load(path);
        var rows = xml.Descendants().Where(node => node.Name.LocalName == (input.Profile.UnitResultFormat == "trx" ? "UnitTestResult" : "testcase")).ToArray();
        Require(rows.Length > 0, "Unit report contains no executed tests.");
        if (input.Profile.UnitResultFormat == "trx")
        {
            var summary = xml.Descendants().Single(node => node.Name.LocalName == "ResultSummary");
            Require(summary.Attribute("outcome")?.Value is "Completed" or "Passed" or "Failed", "Unit host did not complete normally.");
            var counters = summary.Elements().Single(node => node.Name.LocalName == "Counters");
            Require(Count(counters, "total") == rows.Length && Count(counters, "executed") == rows.Length
                && Count(counters, "passed") + Count(counters, "failed") == rows.Length,
                "Unit completion counters do not match the executed rows.");
            Require(Count(counters, "passed") == rows.Count(node => node.Attribute("outcome")?.Value == "Passed")
                && Count(counters, "failed") == rows.Count(node => node.Attribute("outcome")?.Value == "Failed"), "Unit row outcomes do not match completion counters.");
            foreach (var name in new[] { "error", "timeout", "aborted", "notExecuted", "disconnected", "inconclusive", "pending" })
                if (counters.Attribute(name) is not null) Require(Count(counters, name) == 0, "Unit completion has unresolved " + name + " outcomes.");
            Require(!summary.Descendants().Any(node => node.Name.LocalName == "RunInfo" && node.Attribute("outcome")?.Value == "Error"), "Unit host reported a run error.");
        }
        else
        {
            Require(xml.Root?.Name.LocalName is "testsuite" or "testsuites", "Unit JUnit report has an unexpected root.");
            if (xml.Root?.Attribute("tests") is not null) Require(Count(xml.Root, "tests") == rows.Length, "JUnit summary does not match executed rows.");
        }
        ICisTestResultAdapter adapter = input.Profile.UnitResultFormat == "trx" ? new TrxTestResultAdapter() : new JUnitTestResultAdapter();
        return adapter.Read(context);
    }

    private static int Count(XElement element, string name)
    {
        Require(int.TryParse(element.Attribute(name)?.Value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0, "Unit report has invalid completion counters.");
        return value;
    }

    private static TestSuiteExecution ReadMutation(Input input, string source, string path)
    {
        var scope = new MutationRunProfile { WorkingDirectory = input.Profile.WorkingDirectory, SourceFiles = [source] };
        var diagnostics = new List<string>();
        Require(MutationRunService.MatchScope(input.Repository, scope, path, true, diagnostics),
            "Mutation batch scope is incomplete or changed. " + string.Join("; ", diagnostics));
        var suite = new TestSuiteProfile(input.Profile.Id, input.Profile.Id, "mutation", "native", "run", input.Profile.WorkingDirectory,
            "stryker-json", path, "-", "-", "-", "always", "local", "current-result");
        var result = new StrykerJsonResultAdapter().Read(new(input.Repository, suite, path, null, null, input.Profile.BreakThreshold));
        Require(result.Mutation is not null, "Native mutation report has no assessment.");
        if (result.Diagnostics is ["Mutation report contains no eligible mutants."])
        {
            using var native = JsonDocument.Parse(File.ReadAllBytes(path));
            var working = Safe(input.Repository, input.Profile.WorkingDirectory, true);
            var owned = native.RootElement.GetProperty("files").EnumerateObject().Single(file => PathComparer.Equals(
                Path.GetFullPath(file.Name.Replace('\\', Path.DirectorySeparatorChar), working), Safe(input.Repository, source)));
            if (owned.Value.GetProperty("mutants").GetArrayLength() == 0)
                result = result with { Mutation = result.Mutation with { ValidEvidence = true }, Diagnostics = [] };
        }
        return result with { Mutation = result.Mutation! with { Qualified = false, Scope = "incremental-batch", SourcePath = "" } };
    }

    private static IncrementalStageResult Assess(Input input, IncrementalTestBatch batch, bool mutation,
        string output, MutationNativeExecution execution, bool unchanged)
    {
        var fingerprint = mutation ? batch.MutationFingerprint : batch.UnitFingerprint;
        var diagnostics = new List<string>();
        var report = Safe(output, mutation ? input.Profile.MutationReportPath : input.Profile.UnitReportPath);
        TestSuiteExecution? assessment = null;
        try { if (mutation) assessment = ReadMutation(input, batch.SourceFile, report); else assessment = ReadUnit(input, report); }
        catch (Exception error) when (Expected(error)) { diagnostics.Add(error.Message); }
        var healthy = execution.Status == "exited" && execution.Error is null && !execution.OutputTruncated
            && (mutation ? execution.ExitCode is 0 or 2 : execution.ExitCode == 0);
        if (!healthy) diagnostics.Add($"Native execution: {execution.Status}, exit={execution.ExitCode}. {execution.Error}");
        if (!unchanged) diagnostics.Add(ChangedExecutionInputs);
        var completed = healthy && unchanged && (mutation
            ? assessment?.Mutation is { ValidEvidence: true, RuntimeErrors: 0, Pending: 0, Unknown: 0 }
            : assessment is { Status: "passed", Total: > 0, Failed: 0, Skipped: 0 });
        if (mutation && PathComparer.Equals(batch.SourceFile, input.Profile.CanarySourceFile)
            && (assessment?.Mutation?.Killed ?? 0) < input.Profile.MinimumCanaryKills)
        {
            completed = false;
            diagnostics.Add("Mutation canary did not produce the required known kills. Check native mutant activation and assertions before running further batches.");
        }
        var relative = "";
        var hash = "";
        if (File.Exists(report) && new FileInfo(report).Length <= input.Profile.MaxRunBytes)
        {
            var destination = Safe(input.Root, "reports/" + batch.Id + (mutation ? "-mutation.json" : "-unit.xml"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            hash = HashFile(report);
            File.Move(report, destination, true);
            relative = Path.GetRelativePath(input.Root, destination).Replace('\\', '/');
        }
        return new(fingerprint, completed ? "completed" : "failed", completed, relative, hash, execution,
            assessment?.Total ?? 0, assessment?.Passed ?? 0, assessment?.Failed ?? 0, assessment?.Skipped ?? 0,
            assessment?.Mutation, DateTimeOffset.UtcNow, diagnostics.Concat(assessment?.Diagnostics ?? []).ToArray());
    }
}
