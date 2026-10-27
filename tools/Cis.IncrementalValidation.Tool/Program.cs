using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 4) { Console.Error.WriteLine("Arguments: fresh-control-root installed-cis.dll dotnet-host stryker.dll"); return 2; }
var control = Path.GetFullPath(args[0]);
var cli = Path.GetFullPath(args[1]);
var dotnet = Path.GetFullPath(args[2]);
var stryker = Path.GetFullPath(args[3]);
var repository = Path.Combine(control, "s");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var records = new List<object>();
var started = Stopwatch.StartNew();
Directory.CreateDirectory(control);
if (Directory.Exists(repository)) { Console.Error.WriteLine("Validation requires a fresh sample directory: " + repository); return 2; }
Environment.SetEnvironmentVariable("DOTNET_ROOT", Path.GetDirectoryName(dotnet));
Environment.SetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER", "0");
Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
Environment.SetEnvironmentVariable("NuGetAudit", "false");
Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(dotnet) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
var originalA = "namespace Sample; public static class A { public static int Normalize(int value) => Shared.Clamp(value) + 1; }";
var sourceShared = "namespace Sample; public static class Shared { public static int Clamp(int value) => System.Math.Max(value, 0); }";
var testsA = """
    using Sample;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    namespace SampleTests;
    [TestClass]
    public class ATests
    {
        [TestMethod]
        public void Normalizes()
        {
            Assert.AreEqual(1, A.Normalize(-2));
            Assert.AreEqual(1, A.Normalize(0));
            Assert.AreEqual(4, A.Normalize(3));
        }
    }
    """;
try
{
    Write("global.json", "{\"sdk\":{\"version\":\"10.0.401\",\"rollForward\":\"disable\"}}");
    Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: incremental-sample\ndocumentation_root: docs\n");
    Write("docs/catalog.yml", "schema_version: 1\nrepository: incremental-sample\ndocuments:\n  - id: incremental-sample:docs:root\n    path: docs/README.md\n    type: navigation\n    status: draft\n    authority: canonical\n");
    Write("docs/README.md", "---\ntitle: Incremental execution sample\ntype: navigation\nstatus: Draft\nowner: CIS validation\nlast_reviewed: 2026-10-27\nreview_cadence: on fixture change\ncis:\n  stable_id: incremental-sample:docs:root\n---\n\n# Incremental execution sample\n\nNeutral native validation fixture.\n");
    Write("src/Sample.Library/Sample.Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
    Write("src/Sample.Library/A.cs", originalA);
    Write("src/Sample.Library/B.cs", "namespace Sample; public static class B { public static int Double(int value) => value * 2; }");
    Write("src/Sample.Library/Shared.cs", sourceShared);
    Write("tests/Sample.Tests/Sample.Tests.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
          <ItemGroup><ProjectReference Include="../../src/Sample.Library/Sample.Library.csproj" /></ItemGroup>
          <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" /><PackageReference Include="MSTest.TestFramework" Version="4.5.1" /><PackageReference Include="MSTest.TestAdapter" Version="4.5.1" /></ItemGroup>
        </Project>
        """);
    Write("tests/Sample.Tests/ATests.cs", testsA);
    Write("tests/Sample.Tests/BTests.cs", """
        using Sample;
        using Microsoft.VisualStudio.TestTools.UnitTesting;
        namespace SampleTests;
        [TestClass]
        public class BTests
        {
            [TestMethod]
            public void Doubles()
            {
                Assert.AreEqual(0, B.Double(0));
                Assert.AreEqual(4, B.Double(2));
                Assert.AreEqual(-6, B.Double(-3));
            }
        }
        """);
    Write("src/Sample.Library/stryker.json", JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["stryker-config"] = new Dictionary<string, object>
        {
            ["project"] = "Sample.Library.csproj", ["test-projects"] = new[] { "../../tests/Sample.Tests/Sample.Tests.csproj" },
            ["test-runner"] = "vstest", ["target-framework"] = "net10.0", ["configuration"] = "Release", ["mutation-level"] = "Complete",
            ["coverage-analysis"] = "perTest", ["reporters"] = new[] { "json", "dots" }, ["concurrency"] = 2,
            ["disable-bail"] = false, ["disable-mix-mutants"] = true, ["additional-timeout"] = 1500,
            ["thresholds"] = new { high = 100, low = 90, @break = 90 }, ["verbosity"] = "info", ["break-on-initial-test-failure"] = true,
        },
    }, json));
    var tools = Directory.GetFiles(Path.GetDirectoryName(stryker)!, "*.dll").Append(dotnet).Order(StringComparer.Ordinal).ToArray();
    Require(tools.Length <= 128, "Tool closure exceeds the profile limit.");
    Write("profile.json", JsonSerializer.Serialize(new
    {
        schemaVersion = 1, id = "smoke", workingDirectory = "src/Sample.Library",
        sourceFiles = new[] { "src/Sample.Library/A.cs", "src/Sample.Library/B.cs" },
        testFiles = new[] { "tests/Sample.Tests/ATests.cs", "tests/Sample.Tests/BTests.cs" },
        configFiles = new[] { "global.json", "src/Sample.Library/Sample.Library.csproj", "tests/Sample.Tests/Sample.Tests.csproj", "src/Sample.Library/stryker.json" },
        toolFiles = tools, fullTestFilter = "FullyQualifiedName~SampleTests.",
        unitCommand = new[] { dotnet, "test", "../../tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-restore", "--filter", "{testFilter}", "--logger", "trx;LogFileName=unit.trx", "--results-directory", "{output}", "--nologo" },
        unitReportPath = "unit.trx", unitResultFormat = "trx",
        mutationCommand = new[] { dotnet, stryker, "--config-file", "{mutationConfig}", "--output", "{output}" },
        strykerConfigFile = "src/Sample.Library/stryker.json",
        canarySourceFile = "src/Sample.Library/A.cs", minimumCanaryKills = 1,
        mutationReportPath = "reports/mutation-report.json",
        recoveryCommand = new[] { dotnet, "build", "../../tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-restore", "-t:Rebuild", "-m:1", "-nr:false", "--nologo" },
        environment = new Dictionary<string, string> { ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["UseSharedCompilation"] = "false", ["NuGetAudit"] = "false", ["VSTEST_CONNECTION_TIMEOUT"] = "15" },
        breakThreshold = 90, timeoutSeconds = 120, recoveryTimeoutSeconds = 60, maxRunBytes = 134217728,
        maxRetainedBytes = 268435456, minFreeBytes = 1073741824,
    }, json));
    Invoke("restore", ["restore", "tests/Sample.Tests/Sample.Tests.csproj", "--ignore-failed-sources", "-p:NuGetAudit=false"], 120);
    Graph();
    var first = Run("initial", "all", 1, 3);
    Require(Int(first, "executedUnitBatches") == 1 && Int(first, "executedMutationBatches") == 1 && Int(first, "pendingMutationBatches") == 1
        && first["completeScopeScore"] is null, "The first bounded batch did not save exactly one completed source.");
    Require(Int(first, "killed") >= 1, "The native canary did not demonstrate mutant activation and detection.");
    var resume = Run("resume", "all", 4);
    Require(Int(resume, "executedUnitBatches") == 1 && Int(resume, "executedMutationBatches") == 1 && Int(resume, "reusedMutationBatches") == 1,
        "Resume repeated completed work or skipped pending work.");
    var unchanged = Run("unchanged", "all", 4);
    Require(Int(unchanged, "executedUnitBatches") == 0 && Int(unchanged, "executedMutationBatches") == 0
        && Int(unchanged, "reusedUnitBatches") == 2 && Int(unchanged, "reusedMutationBatches") == 2, "Unchanged inputs were not reused.");
    Write("tests/Sample.Tests/ATests.cs", testsA.Replace("Assert.AreEqual(4, A.Normalize(3));", "Assert.AreEqual(4, A.Normalize(3));\n        Assert.AreEqual(9, A.Normalize(8));", StringComparison.Ordinal));
    Graph();
    var units = Run("changed-test-units", "unit", 4);
    Require(Int(units, "executedUnitBatches") == 1 && Int(units, "executedMutationBatches") == 0 && Int(units, "pendingMutationBatches") == 1,
        "Changed tests did not invalidate just their affected unit and mutation work.");
    var mutations = Run("changed-test-mutations", "mutation", 4);
    Require(Int(mutations, "executedUnitBatches") == 0 && Int(mutations, "executedMutationBatches") == 1 && Int(mutations, "reusedMutationBatches") == 1,
        "Mutation execution repeated the already completed unit update or the unrelated source.");
    Write("src/Sample.Library/Shared.cs", "namespace Sample; public static class Shared { public static int Clamp(int value) => value < 0 ? 0 : value; }");
    Graph();
    var shared = Run("changed-dependency", "all", 4);
    Require(Int(shared, "executedUnitBatches") == 1 && Int(shared, "executedMutationBatches") == 1 && Int(shared, "reusedMutationBatches") == 1,
        "A shared dependency change was not isolated to its affected consumer.");
    Write("src/Sample.Library/A.cs", "namespace Sample; public static class A { public static int Normalize(int value) { int result = Shared.Clamp(value); for (int i = 0; i < 1; i++) result += 1; return result; } }");
    Graph();
    var timeout = Run("mutant-timeout", "all", 4, 4);
    Require(Int(timeout, "pendingMutationBatches") == 1 && Int(timeout, "reusedMutationBatches") == 1 && timeout["completeScopeScore"] is null,
        "An unresolved mutant lost unrelated results or obtained a complete-scope score.");
    var state = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, ".cis/local/testing/incremental/smoke/state.json")))!;
    Require(state["recoveryRequired"]!.GetValue<bool>() == false, "Baseline recovery did not complete after the native timeout.");
    Require(state["batches"]!.AsObject().Any(item => (item.Value?["mutation"]?["mutation"]?["timedOut"]?.GetValue<int>() ?? 0) > 0),
        "The intended native infinite-loop mutation did not produce a timeout outcome.");
    Write("src/Sample.Library/A.cs", originalA);
    Graph();
    var fixedRun = Run("fixed-timeout", "all", 4);
    Require(Int(fixedRun, "executedMutationBatches") == 1 && Int(fixedRun, "reusedMutationBatches") == 1, "Fixing the timed-out source repeated unrelated completed mutation work.");
    var complete = Run("final-unchanged", "all", 4);
    Require(Int(complete, "executedUnitBatches") == 0 && Int(complete, "executedMutationBatches") == 0 && complete["scopeComplete"]!.GetValue<bool>(), "Final results were not reusable.");
    var audit = new { status = "passed", elapsedSeconds = started.Elapsed.TotalSeconds, repositoryBytes = Bytes(repository),
        retainedResultBytes = Bytes(Path.Combine(repository, ".cis/local/testing")), phases = records };
    File.WriteAllText(Path.Combine(control, "validation-summary.json"), JsonSerializer.Serialize(audit, json));
    Console.WriteLine(JsonSerializer.Serialize(audit, json));
    return 0;
}
catch (Exception error)
{
    var failure = new { status = "failed", error = error.Message, elapsedSeconds = started.Elapsed.TotalSeconds, phases = records };
    File.WriteAllText(Path.Combine(control, "validation-summary.json"), JsonSerializer.Serialize(failure, json));
    Console.Error.WriteLine(JsonSerializer.Serialize(failure, json));
    return 1;
}

void Write(string relative, string content) { var path = Path.Combine(repository, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
void Graph() => Invoke("graph", [cli, "graph", "build", "--repo", repository, "--format", "json"], 90);
JsonNode Run(string name, string layer, int batches, int exit = 0)
{
    var text = Invoke(name, [cli, "test", "incremental", "run", "--repo", repository, "--profile", "profile.json", "--layer", layer,
        "--max-batches", batches.ToString(System.Globalization.CultureInfo.InvariantCulture), "--max-seconds", "300", "--format", "json"], 360, exit);
    var node = JsonNode.Parse(text)!;
    records.Add(new { name, result = node });
    File.WriteAllText(Path.Combine(control, "validation-progress.json"), JsonSerializer.Serialize(records, json));
    Console.WriteLine($"{name}: status={node["status"]}; unit={node["executedUnitBatches"]}; mutation={node["executedMutationBatches"]}; reused={node["reusedMutationBatches"]}");
    return node;
}
string Invoke(string name, string[] arguments, int seconds, int expected = 0)
{
    var start = new ProcessStartInfo(dotnet) { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native command.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(seconds * 1000)) { process.Kill(true); throw new InvalidOperationException("Validation command exceeded its bound: " + name); }
    var output = stdout.GetAwaiter().GetResult();
    var errors = stderr.GetAwaiter().GetResult();
    File.WriteAllText(Path.Combine(control, name + ".json"), output);
    if (errors.Length > 0) File.WriteAllText(Path.Combine(control, name + ".stderr.log"), errors);
    Require(process.ExitCode == expected, name + " exited " + process.ExitCode + " instead of " + expected + ": " + errors + " " + output);
    return output;
}
static int Int(JsonNode value, string name) => value[name]!.GetValue<int>();
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static long Bytes(string root) => Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0;
