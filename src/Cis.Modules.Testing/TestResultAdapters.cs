using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

internal static partial class TestResultEvidence
{
    public static TestCoverageSummary? Coverage(TestResultAdapterContext context)
    {
        if (string.IsNullOrWhiteSpace(context.CoveragePath) || !File.Exists(context.CoveragePath)) return null;
        if (Path.GetExtension(context.CoveragePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(context.RepositoryPath, ChangedProductionScope.RelativePath)))
                throw new InvalidDataException("Changed-production coverage currently requires native Cobertura XML; JSON coverage remains repository-scoped.");
            using var document = JsonDocument.Parse(File.ReadAllText(context.CoveragePath));
            var root = document.RootElement;
            if (root.TryGetProperty("total", out var total)) root = total;
            return new TestCoverageSummary(
                Percent(root, "lines"),
                Percent(root, "statements"),
                Percent(root, "functions"),
                Percent(root, "branches"),
                "repository",
                Relative(context.RepositoryPath, context.CoveragePath));
        }

        var xml = XDocument.Load(context.CoveragePath);
        if (File.Exists(Path.Combine(context.RepositoryPath, ChangedProductionScope.RelativePath)))
            return ChangedCoverageProjection.Read(context, xml);
        var rootElement = xml.Root ?? throw new InvalidDataException("Coverage XML has no root element.");
        var lines = Ratio(rootElement.Attribute("line-rate")?.Value);
        var branches = Ratio(rootElement.Attribute("branch-rate")?.Value);
        return new TestCoverageSummary(lines, lines, 0, branches, "repository",
            Relative(context.RepositoryPath, context.CoveragePath));
    }

    public static TestArtifact Artifact(string repository, string kind, string path)
    {
        using var stream = File.OpenRead(path);
        return new TestArtifact(kind, Relative(repository, path),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), stream.Length);
    }

    public static IReadOnlyList<TestArtifact> Artifacts(TestResultAdapterContext context)
    {
        var artifacts = new List<TestArtifact> { Artifact(context.RepositoryPath, "test-result", context.ResultPath) };
        if (context.CoveragePath is not null && File.Exists(context.CoveragePath))
            artifacts.Add(Artifact(context.RepositoryPath, "coverage-result", context.CoveragePath));
        return artifacts;
    }

    public static string CaseId(string value)
        => TestCasePattern().Match(value) is { Success: true } match ? match.Value.ToUpperInvariant() : string.Empty;

    public static IReadOnlyList<string> CaseIds(string value)
    {
        var ids = TestCasePattern().Matches(value).Select(match => match.Value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return ids.Length == 0 ? [string.Empty] : ids;
    }

    public static string Relative(string repository, string path)
        => Path.GetRelativePath(repository, path).Replace('\\', '/');

    private static double Percent(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var direct)) return direct;
        if (value.TryGetProperty("pct", out var percent))
        {
            if (percent.ValueKind == JsonValueKind.Number && percent.TryGetDouble(out var number)) return number;
            if (percent.ValueKind == JsonValueKind.String && double.TryParse(percent.GetString(), out number)) return number;
        }
        return 0;
    }

    private static double Ratio(string? value)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var ratio) ? ratio * 100 : 0;

    [GeneratedRegex(@"\bTC-[A-Z0-9]+(?:-[A-Z0-9]+)*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TestCasePattern();
}

public sealed class JUnitTestResultAdapter : ICisTestResultAdapter
{
    public string Format => "junit";

    public TestSuiteExecution Read(TestResultAdapterContext context)
    {
        var xml = XDocument.Load(context.ResultPath);
        var cases = xml.Descendants("testcase").SelectMany(item =>
        {
            var name = item.Attribute("name")?.Value ?? "unnamed";
            var failure = item.Element("failure") ?? item.Element("error");
            var skipped = item.Element("skipped") is not null;
            return TestResultEvidence.CaseIds(name).Select(id => new TestCaseExecution(id, name,
                failure is not null ? "failed" : skipped ? "skipped" : "passed",
                Seconds(item.Attribute("time")?.Value) * 1000,
                item.Attribute("classname")?.Value,
                failure?.Attribute("message")?.Value ?? failure?.Value.Trim()));
        }).ToArray();
        return Build(context, cases);
    }

    private static TestSuiteExecution Build(TestResultAdapterContext context, IReadOnlyList<TestCaseExecution> cases)
    {
        var failed = cases.Count(item => item.Status == "failed");
        var skipped = cases.Count(item => item.Status == "skipped");
        var passed = cases.Count(item => item.Status == "passed");
        var status = cases.Count == 0 ? "invalid-evidence" : failed > 0 ? "failed" : passed == 0 ? "skipped" : "passed";
        var artifacts = TestResultEvidence.Artifacts(context);
        return new TestSuiteExecution(context.Suite.Id, context.Suite.Layer, context.Suite.Framework, status,
            cases.Count == 0 ? TestFailureKind.InvalidEvidence : failed > 0 ? TestFailureKind.Product : TestFailureKind.None,
            cases.Count, passed, failed, skipped, cases.Sum(item => item.DurationMilliseconds), cases,
            TestResultEvidence.Coverage(context), null, artifacts,
            cases.Count == 0 ? ["Result file contains no test cases."] : []);
    }

    private static double Seconds(string? value)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
}

public sealed class CucumberJUnitTestResultAdapter : ICisTestResultAdapter
{
    private readonly JUnitTestResultAdapter _inner = new();
    public string Format => "cucumber-junit";
    public TestSuiteExecution Read(TestResultAdapterContext context) => _inner.Read(context);
}

public sealed class VitestJsonResultAdapter : ICisTestResultAdapter
{
    public string Format => "vitest-json";

    public TestSuiteExecution Read(TestResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var cases = new List<TestCaseExecution>();
        if (document.RootElement.TryGetProperty("testResults", out var files))
        {
            foreach (var file in files.EnumerateArray())
            {
                var source = file.TryGetProperty("name", out var path) ? path.GetString() : null;
                if (!file.TryGetProperty("assertionResults", out var assertions)) continue;
                foreach (var assertion in assertions.EnumerateArray())
                {
                    var name = assertion.TryGetProperty("fullName", out var fullName)
                        ? fullName.GetString() ?? "unnamed"
                        : assertion.GetProperty("title").GetString() ?? "unnamed";
                    var status = assertion.TryGetProperty("status", out var state) ? state.GetString() ?? "unknown" : "unknown";
                    var duration = assertion.TryGetProperty("duration", out var elapsed) && elapsed.TryGetDouble(out var ms) ? ms : 0;
                    string? failure = null;
                    if (assertion.TryGetProperty("failureMessages", out var failures) && failures.ValueKind == JsonValueKind.Array)
                        failure = string.Join(Environment.NewLine, failures.EnumerateArray().Select(item => item.GetString()));
                    cases.AddRange(TestResultEvidence.CaseIds(name)
                        .Select(id => new TestCaseExecution(id, name, Normalize(status), duration, source, failure)));
                }
            }
        }
        return Build(context, cases);
    }

    internal static TestSuiteExecution Build(TestResultAdapterContext context, IReadOnlyList<TestCaseExecution> cases)
    {
        var failed = cases.Count(item => item.Status == "failed");
        var skipped = cases.Count(item => item.Status == "skipped");
        var passed = cases.Count(item => item.Status == "passed");
        var status = cases.Count == 0 ? "invalid-evidence" : failed > 0 ? "failed" : passed == 0 ? "skipped" : "passed";
        return new TestSuiteExecution(context.Suite.Id, context.Suite.Layer, context.Suite.Framework, status,
            cases.Count == 0 ? TestFailureKind.InvalidEvidence : failed > 0 ? TestFailureKind.Product : TestFailureKind.None,
            cases.Count, passed, failed, skipped, cases.Sum(item => item.DurationMilliseconds), cases,
            TestResultEvidence.Coverage(context), null,
            TestResultEvidence.Artifacts(context),
            cases.Count == 0 ? ["Result file contains no test cases."] : []);
    }

    private static string Normalize(string value) => value.ToLowerInvariant() switch
    {
        "passed" => "passed",
        "pending" or "todo" or "skipped" or "disabled" => "skipped",
        _ => "failed",
    };
}

public sealed class PlaywrightJsonResultAdapter : ICisTestResultAdapter
{
    public string Format => "playwright-json";

    public TestSuiteExecution Read(TestResultAdapterContext context)
    {
        var bytes = File.ReadAllBytes(context.ResultPath);
        using var document = JsonDocument.Parse(bytes);
        var cases = new List<TestCaseExecution>();
        var diagnostics = new List<string>();
        var root = Object(document.RootElement, "report");
        var reportRoot = root.TryGetProperty("config", out var config) ? Text(Object(config, "config"), "rootDir") : null;
        if (!root.TryGetProperty("suites", out var suites))
            throw new InvalidDataException("Playwright report is missing its suites array.");
        VisitSuites(Array(suites, "suites"), cases, diagnostics, [], context, reportRoot);
        var execution = VitestJsonResultAdapter.Build(context, cases);
        var reportDigest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!execution.Artifacts.Any(item => item.Kind == "test-result" && item.Bytes == bytes.LongLength && item.Digest == reportDigest))
            throw new InvalidDataException("Playwright report changed while its results were being read.");
        if (root.TryGetProperty("errors", out var errors))
        {
            foreach (var error in Array(errors, "errors").EnumerateArray())
                diagnostics.Add($"Playwright runner error: {ErrorMessage(error) ?? error.ToString()}");
            if (errors.GetArrayLength() > 0)
                execution = execution with { Status = "failed", FailureKind = TestFailureKind.Infrastructure };
        }
        return execution with { Diagnostics = execution.Diagnostics.Concat(diagnostics).ToArray() };
    }

    private static void VisitSuites(JsonElement suites, ICollection<TestCaseExecution> cases,
        ICollection<string> diagnostics, IReadOnlyList<string> parents, TestResultAdapterContext context, string? reportRoot)
    {
        foreach (var suite in suites.EnumerateArray())
        {
            Object(suite, "suite");
            var suiteTitle = Text(suite, "title");
            var titles = string.IsNullOrWhiteSpace(suiteTitle) ? parents : parents.Concat([suiteTitle]).ToArray();
            if (suite.TryGetProperty("suites", out var nested)) VisitSuites(Array(nested, "nested suites"), cases, diagnostics, titles, context, reportRoot);
            if (!suite.TryGetProperty("specs", out var specs)) continue;
            foreach (var spec in Array(specs, "specs").EnumerateArray())
            {
                Object(spec, "spec");
                var title = Text(spec, "title") ?? "unnamed";
                var source = Source(Text(spec, "file"), context, reportRoot);
                if (!spec.TryGetProperty("tests", out var tests))
                    throw new InvalidDataException("Playwright spec is missing its tests array.");
                foreach (var test in Array(tests, "tests").EnumerateArray())
                {
                    Object(test, "test");
                    var project = Text(test, "projectName") ?? Text(test, "projectId");
                    var name = string.Join(" > ", titles.Concat([title]));
                    if (!string.IsNullOrWhiteSpace(project)) name = $"[{project}] {name}";
                    if (!test.TryGetProperty("results", out var results) || Array(results, "results").GetArrayLength() == 0)
                    {
                        foreach (var id in TestResultEvidence.CaseIds(title))
                            cases.Add(new TestCaseExecution(id, name, "skipped", 0, source, null));
                        continue;
                    }
                    var attempts = results.EnumerateArray().Select(item => Text(Object(item, "result"), "status") ?? "unknown").ToArray();
                    var result = results[results.GetArrayLength() - 1];
                    var actualStatus = attempts[^1];
                    var expectedStatus = Text(test, "expectedStatus") ?? "passed";
                    var outcome = Text(test, "status");
                    var status = actualStatus == "passed" && expectedStatus == "passed" && outcome is null or "expected" or "flaky"
                        ? "passed" : actualStatus == "skipped" ? "skipped" : "failed";
                    var duration = 0d;
                    if (result.TryGetProperty("duration", out var elapsed)
                        && (elapsed.ValueKind != JsonValueKind.Number || !elapsed.TryGetDouble(out duration) || !double.IsFinite(duration) || duration < 0))
                        throw new InvalidDataException("Playwright result duration must be a nonnegative finite number.");
                    var failure = result.TryGetProperty("error", out var error) ? ErrorMessage(error) : null;
                    if (status == "failed" && failure is null)
                        failure = $"Native result status={actualStatus}; expectedStatus={expectedStatus}; outcome={outcome ?? "unreported"}.";
                    if (attempts.Length > 1 || outcome == "flaky")
                        diagnostics.Add($"Playwright case '{name}' completed with {attempts.Length} attempt(s): {string.Join(", ", attempts)}; outcome={outcome ?? "unreported"}.");
                    foreach (var id in TestResultEvidence.CaseIds(title))
                        cases.Add(new TestCaseExecution(id, name, status,
                            duration, source, failure));
                }
            }
        }
    }

    private static JsonElement Object(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object ? value
            : throw new InvalidDataException($"Playwright {name} must be an object.");

    private static JsonElement Array(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Array ? value
            : throw new InvalidDataException($"Playwright {name} must be an array.");

    private static string? Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Playwright {name} must be a string.");
        return property.GetString();
    }

    private static string? ErrorMessage(JsonElement value)
        => Text(Object(value, "error"), "message") ?? Text(value, "value");

    private static string? Source(string? file, TestResultAdapterContext context, string? reportRoot)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        try
        {
            var workingDirectory = Path.GetFullPath(context.Suite.WorkingDirectory, context.RepositoryPath);
            var root = string.IsNullOrWhiteSpace(reportRoot) ? workingDirectory : Path.GetFullPath(reportRoot, workingDirectory);
            var absolute = Path.GetFullPath(file, root);
            return CisPathSafety.IsUnderRoot(context.RepositoryPath, absolute)
                ? Path.GetRelativePath(context.RepositoryPath, absolute).Replace('\\', '/') : absolute.Replace('\\', '/');
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException("Playwright source location is invalid.", exception);
        }
    }
}

public sealed class TrxTestResultAdapter : ICisTestResultAdapter
{
    public string Format => "trx";

    public TestSuiteExecution Read(TestResultAdapterContext context)
    {
        var xml = XDocument.Load(context.ResultPath);
        var cases = xml.Descendants().Where(item => item.Name.LocalName == "UnitTestResult").SelectMany(item =>
        {
            var name = item.Attribute("testName")?.Value ?? "unnamed";
            var outcome = item.Attribute("outcome")?.Value ?? "Failed";
            return TestResultEvidence.CaseIds(name).Select(id => new TestCaseExecution(id, name,
                outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? "passed"
                    : outcome is "NotExecuted" or "Skipped" ? "skipped" : "failed",
                ParseDuration(item.Attribute("duration")?.Value), null,
                item.Descendants().FirstOrDefault(node => node.Name.LocalName == "Message")?.Value));
        }).ToArray();
        return VitestJsonResultAdapter.Build(context, cases);
    }

    private static double ParseDuration(string? value)
        => TimeSpan.TryParse(value, out var duration) ? duration.TotalMilliseconds : 0;
}
