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
        var artifacts = new[] { TestResultEvidence.Artifact(context.RepositoryPath, "test-result", context.ResultPath) };
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
            [TestResultEvidence.Artifact(context.RepositoryPath, "test-result", context.ResultPath)],
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
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var cases = new List<TestCaseExecution>();
        if (document.RootElement.TryGetProperty("suites", out var suites)) VisitSuites(suites, cases);
        return VitestJsonResultAdapter.Build(context, cases);
    }

    private static void VisitSuites(JsonElement suites, ICollection<TestCaseExecution> cases)
    {
        foreach (var suite in suites.EnumerateArray())
        {
            if (suite.TryGetProperty("suites", out var nested)) VisitSuites(nested, cases);
            if (!suite.TryGetProperty("specs", out var specs)) continue;
            foreach (var spec in specs.EnumerateArray())
            {
                var title = spec.TryGetProperty("title", out var name) ? name.GetString() ?? "unnamed" : "unnamed";
                var source = spec.TryGetProperty("file", out var file) ? file.GetString() : null;
                if (!spec.TryGetProperty("tests", out var tests)) continue;
                foreach (var test in tests.EnumerateArray())
                {
                    if (!test.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    {
                        foreach (var id in TestResultEvidence.CaseIds(title))
                            cases.Add(new TestCaseExecution(id, title, "skipped", 0, source, null));
                        continue;
                    }
                    var result = results[results.GetArrayLength() - 1];
                    var status = result.TryGetProperty("status", out var state) ? state.GetString() ?? "failed" : "failed";
                    var duration = result.TryGetProperty("duration", out var elapsed) && elapsed.TryGetDouble(out var ms) ? ms : 0;
                    var failure = result.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                        ? message.GetString()
                        : null;
                    foreach (var id in TestResultEvidence.CaseIds(title))
                        cases.Add(new TestCaseExecution(id, title,
                            status is "passed" or "expected" ? "passed" : status is "skipped" ? "skipped" : "failed",
                            duration, source, failure));
                }
            }
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

public sealed class StrykerJsonResultAdapter : ICisTestResultAdapter
{
    public string Format => "stryker-json";

    public TestSuiteExecution Read(TestResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var statuses = new List<string>();
        Visit(document.RootElement, statuses);
        var killed = statuses.Count(item => item.Equals("Killed", StringComparison.OrdinalIgnoreCase));
        var survived = statuses.Count(item => item.Equals("Survived", StringComparison.OrdinalIgnoreCase));
        var timedOut = statuses.Count(item => item.Equals("Timeout", StringComparison.OrdinalIgnoreCase));
        var noCoverage = statuses.Count(item => item.Equals("NoCoverage", StringComparison.OrdinalIgnoreCase));
        var denominator = killed + survived + timedOut;
        var score = denominator == 0 ? 0 : (killed + timedOut) * 100d / denominator;
        var mutation = new TestMutationSummary(score, killed, survived, timedOut, noCoverage,
            null, null, null, TestResultEvidence.Relative(context.RepositoryPath, context.ResultPath));
        var status = statuses.Count == 0 ? "invalid-evidence" : survived > 0 || noCoverage > 0 ? "findings" : "passed";
        return new TestSuiteExecution(context.Suite.Id, context.Suite.Layer, context.Suite.Framework, status,
            statuses.Count == 0 ? TestFailureKind.InvalidEvidence : TestFailureKind.None,
            statuses.Count, killed + timedOut, survived, noCoverage, 0, [], null, mutation,
            [TestResultEvidence.Artifact(context.RepositoryPath, "mutation-result", context.ResultPath)],
            statuses.Count == 0 ? ["Mutation result contains no mutants."] : []);
    }

    private static void Visit(JsonElement element, ICollection<string> statuses)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("status") && property.Value.ValueKind == JsonValueKind.String)
                    statuses.Add(property.Value.GetString() ?? string.Empty);
                else Visit(property.Value, statuses);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) Visit(item, statuses);
        }
    }
}
