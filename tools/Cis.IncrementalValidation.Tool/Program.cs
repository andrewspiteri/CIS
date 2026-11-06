using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args is ["--pilot-child", var challenge]) { Console.Write("native-child:" + challenge); return 0; }
var resumeSetup = args.Length == 5 && args[4] == "--resume";
if (args.Length != 4 && !resumeSetup) { Console.Error.WriteLine("Arguments: control-root installed-cis.dll dotnet-host stryker.dll [--resume]"); return 2; }
var control = Path.GetFullPath(args[0]);
var cli = Path.GetFullPath(args[1]);
var dotnet = Path.GetFullPath(args[2]);
var stryker = Path.GetFullPath(args[3]);
var repository = Path.Combine(control, "s");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var records = new List<object>();
var started = Stopwatch.StartNew();
var phaseCosts = new List<object>();
var validationTool = typeof(Program).Assembly.Location;
const string actor = "Native fixture controller";
var runSuffix = resumeSetup ? ".resume-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") : "";
Directory.CreateDirectory(control);
if (Directory.Exists(repository) && !resumeSetup) { Console.Error.WriteLine("Validation requires a fresh sample directory, or explicit --resume retaining its allocation: " + repository); return 2; }
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
    if (!resumeSetup)
    {
    Write("global.json", "{\"sdk\":{\"version\":\"10.0.401\",\"rollForward\":\"disable\"}}");
    Write("Directory.Build.props", "<Project><PropertyGroup><UseArtifactsOutput>true</UseArtifactsOutput><ArtifactsPath>$(MSBuildThisFileDirectory).cis/local/native-build</ArtifactsPath></PropertyGroup></Project>");
    Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: incremental-sample\ndocumentation_root: docs\n");
    Write("docs/catalog.yml", "schema_version: 1\nrepository: incremental-sample\ndocuments:\n  - id: incremental-sample:docs:root\n    path: docs/README.md\n    type: navigation\n    status: draft\n    authority: canonical\n");
    Write("docs/README.md", "---\ntitle: Incremental execution sample\ntype: navigation\nstatus: Draft\nowner: CIS validation\nlast_reviewed: 2026-10-27\nreview_cadence: on fixture change\ncis:\n  stable_id: incremental-sample:docs:root\n---\n\n# Incremental execution sample\n\nNeutral native validation fixture.\n");
    Write("src/Sample.Library/Sample.Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
    Write("src/Sample.Library/A.cs", originalA);
    Write("src/Sample.Library/B.cs", "namespace Sample; public static class B { public static int Double(int value) => value * 2; }");
    Write("src/Sample.Library/Control.cs", "namespace Sample; public static class Control { public static int Unasserted(int value) => value + 1; }");
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
    Write("tests/Sample.Tests/ControlTests.cs", """
        using Sample;
        using Microsoft.VisualStudio.TestTools.UnitTesting;
        namespace SampleTests;
        [TestClass]
        public class ControlTests
        {
            [TestMethod]
            public void ExercisesTheDeliberatelySurvivingControl() => System.GC.KeepAlive(Control.Unasserted(2));
        }
        """);
    Write("tests/Sample.Tests/TransportTests.cs", $$"""
        using System.Diagnostics;
        using Microsoft.VisualStudio.TestTools.UnitTesting;
        namespace SampleTests;
        [TestClass]
        public class TransportTests
        {
            [TestMethod]
            public void RoundTripsThroughOwnedChild()
            {
                var start = new ProcessStartInfo({{JsonSerializer.Serialize(dotnet)}})
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                start.ArgumentList.Add({{JsonSerializer.Serialize(validationTool)}});
                start.ArgumentList.Add("--pilot-child"); start.ArgumentList.Add("bounded-native-control");
                using var child = Process.Start(start)!;
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(10000)) { child.Kill(true); Assert.Fail("The owned native child did not finish."); }
                Assert.AreEqual(0, child.ExitCode);
                Assert.AreEqual("native-child:bounded-native-control", output.GetAwaiter().GetResult());
                Assert.AreEqual("", error.GetAwaiter().GetResult());
            }
        }
        """);
    Write("Sample.slnx", "<Solution><Project Path=\"src/Sample.Library/Sample.Library.csproj\" /><Project Path=\"tests/Sample.Tests/Sample.Tests.csproj\" /></Solution>");
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
    var tools = Directory.GetFiles(Path.GetDirectoryName(stryker)!, "*")
        .Where(path => Path.GetExtension(path) is ".dll" or ".json" or ".exe")
        .Append(dotnet).Append(validationTool).Append(Path.ChangeExtension(validationTool, ".deps.json"))
        .Append(Path.ChangeExtension(validationTool, ".runtimeconfig.json")).Append(typeof(object).Assembly.Location)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    Require(tools.All(File.Exists), "A pinned native tool or host configuration is missing.");
    Require(tools.Length <= 128, "Tool closure exceeds the profile limit.");
    Write("profile.json", JsonSerializer.Serialize(new
    {
        schemaVersion = 1, id = "smoke", workingDirectory = "src/Sample.Library",
        sourceFiles = new[] { "src/Sample.Library/A.cs", "src/Sample.Library/B.cs" },
        testFiles = new[] { "tests/Sample.Tests/ATests.cs", "tests/Sample.Tests/BTests.cs", "tests/Sample.Tests/ControlTests.cs", "tests/Sample.Tests/TransportTests.cs" },
        configFiles = new[] { "global.json", "Directory.Build.props", "src/Sample.Library/Sample.Library.csproj", "tests/Sample.Tests/Sample.Tests.csproj", "src/Sample.Library/stryker.json" },
        toolFiles = tools, fullTestFilter = "FullyQualifiedName~SampleTests.",
        unitCommand = new[] { dotnet, "test", "../../tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-restore", "--filter", "{testFilter}", "--logger", "trx;LogFileName=unit.trx", "--results-directory", "{output}", "--nologo" },
        unitReportPath = "unit.trx", unitResultFormat = "trx",
        mutationCommand = new[] { dotnet, stryker, "--config-file", "{mutationConfig}", "--output", "{output}" },
        strykerConfigFile = "src/Sample.Library/stryker.json",
        canarySourceFile = "src/Sample.Library/A.cs", minimumCanaryKills = 1,
        mutationReportPath = "reports/mutation-report.json",
        recoveryCommand = new[] { dotnet, "build", "../../tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-restore", "-t:Rebuild", "-m:1", "-nr:false", "--nologo" },
        environment = new Dictionary<string, string> { ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["UseSharedCompilation"] = "false", ["NuGetAudit"] = "false", ["VSTEST_CONNECTION_TIMEOUT"] = "15" },
        correctnessPilotProfile = "pilot-profile.json",
        breakThreshold = 90, timeoutSeconds = 120, recoveryTimeoutSeconds = 60, maxRunBytes = 134217728,
        maxRetainedBytes = 268435456, minFreeBytes = 1073741824,
    }, json));
    var nativeTemplate = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "src/Sample.Library/stryker.json")))!;
    nativeTemplate["stryker-config"]!["mutate"] = JsonSerializer.SerializeToNode(new[] { "B.cs", "Control.cs" });
    const string pilotFilter = "FullyQualifiedName~SampleTests.BTests.|FullyQualifiedName~SampleTests.ControlTests.|FullyQualifiedName~SampleTests.TransportTests.";
    nativeTemplate["stryker-config"]!["test-case-filter"] = pilotFilter;
    Write("src/Sample.Library/pilot-stryker.json", nativeTemplate.ToJsonString(json));
    nativeTemplate["stryker-config"]!["mutate"] = JsonSerializer.SerializeToNode(new[] { "B.cs" });
    Write("src/Sample.Library/pilot-canary.json", nativeTemplate.ToJsonString(json));
    Write("pilot-profile.json", JsonSerializer.Serialize(new
    {
        schemaVersion = 1, id = "native-pilot", workingDirectory = "src/Sample.Library",
        sourceFiles = new[] { "src/Sample.Library/B.cs", "src/Sample.Library/Control.cs" },
        configFiles = new[] { "global.json", "Directory.Build.props", "src/Sample.Library/Sample.Library.csproj", "tests/Sample.Tests/Sample.Tests.csproj",
            "tests/Sample.Tests/BTests.cs", "tests/Sample.Tests/ControlTests.cs", "tests/Sample.Tests/TransportTests.cs",
            "src/Sample.Library/pilot-stryker.json", "src/Sample.Library/pilot-canary.json" },
        command = new[] { dotnet, stryker, "--config-file", "pilot-stryker.json", "--output", "{output}" },
        canaryCommand = new[] { dotnet, stryker, "--config-file", "pilot-canary.json", "--output", "{output}" },
        reportPath = "reports/mutation-report.json", minimumCanaryKills = 1, breakThreshold = 90,
        timeoutSeconds = 300, maxRunBytes = 134217728, maxRetainedBytes = 268435456, minFreeBytes = 1073741824,
        environment = new Dictionary<string, string> { ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["UseSharedCompilation"] = "false", ["NuGetAudit"] = "false", ["VSTEST_CONNECTION_TIMEOUT"] = "15" },
        correctnessPilot = new
        {
            baselineCommand = new[] { dotnet, "test", "../../tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-restore", "--filter", pilotFilter,
                "--logger", "trx;LogFileName=unit.trx", "--results-directory", "{output}", "--nologo" },
            baselineReportPath = "unit.trx", unitResultFormat = "trx", controlSourceFile = "src/Sample.Library/Control.cs",
            infrastructureCommand = new[] { dotnet, stryker, "--config-file", "missing-pilot-native.json", "--output", "{output}" },
            missingInfrastructurePath = "missing-pilot-native.json",
            restorationCommand = new[] { dotnet, "build", "../../tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-restore", "-t:Rebuild", "-m:1", "-nr:false", "--nologo" },
            recoveryTimeoutSeconds = 30, toolFiles = tools.Append(validationTool).Distinct(StringComparer.Ordinal).ToArray(),
            childTestName = "RoundTripsThroughOwnedChild",
        }
    }, json));
    var qualificationTemplate = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "src/Sample.Library/stryker.json")))!;
    qualificationTemplate["stryker-config"]!["mutate"] = JsonSerializer.SerializeToNode(new[] { "A.cs", "B.cs" });
    qualificationTemplate["stryker-config"]!["test-case-filter"] = "FullyQualifiedName~SampleTests.ATests.|FullyQualifiedName~SampleTests.BTests.";
    Write("src/Sample.Library/qualification-stryker.json", qualificationTemplate.ToJsonString(json));
    qualificationTemplate["stryker-config"]!["mutate"] = JsonSerializer.SerializeToNode(new[] { "A.cs" });
    Write("src/Sample.Library/qualification-canary.json", qualificationTemplate.ToJsonString(json));
    Write("qualification-profile.json", JsonSerializer.Serialize(new
    {
        schemaVersion = 1, id = "two-source-qualification", workingDirectory = "src/Sample.Library",
        sourceFiles = new[] { "src/Sample.Library/A.cs", "src/Sample.Library/B.cs" },
        configFiles = new[] { "global.json", "Directory.Build.props", "src/Sample.Library/Sample.Library.csproj", "tests/Sample.Tests/Sample.Tests.csproj",
            "tests/Sample.Tests/ATests.cs", "tests/Sample.Tests/BTests.cs", "src/Sample.Library/Shared.cs", "src/Sample.Library/qualification-stryker.json", "src/Sample.Library/qualification-canary.json" },
        command = new[] { dotnet, stryker, "--config-file", "qualification-stryker.json", "--output", "{output}" },
        canaryCommand = new[] { dotnet, stryker, "--config-file", "qualification-canary.json", "--output", "{output}" },
        correctnessPilotProfile = "pilot-profile.json", reportPath = "reports/mutation-report.json", minimumCanaryKills = 1,
        breakThreshold = 90, timeoutSeconds = 300, maxRunBytes = 134217728, maxRetainedBytes = 268435456, minFreeBytes = 1073741824,
        environment = new Dictionary<string, string> { ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["UseSharedCompilation"] = "false", ["NuGetAudit"] = "false", ["VSTEST_CONNECTION_TIMEOUT"] = "15" },
    }, json));
    // These explicit constants authorize only this disposable synthetic fixture;
    // they are not an allocation or canonical approval for the CIS repository.
    Require(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIS_WORK_PARENT_EXECUTION")),
        "The fresh independent fixture cannot rebind an inherited governed parent. Preserve that association at its owning boundary.");
    Write(".cis/engineering-defaults.json", "{\"schemaVersion\":1,\"requireTaskCompletion\":true,\"additionalGates\":[]}");
    Write("docs/changes/CIS-0001/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: false\n---\n# Synthetic native fixture plan\nVerify the bounded native fixture only.\n");
    Write("docs/changes/CIS-0001/impact.md", "---\nstatus: Accepted\n---\n# Synthetic impact\n| Finding | Status |\n|---|---|\n| IMPACT-1 | accepted |\n");
    Write("docs/changes/CIS-0001/agent-tasks/WORK-090.md", "---\ntask_id: WORK-090\ntask_status: Ready\ncategory: verification\ntask_type: core.verification\nparent: none\ntargets: [\"incremental-sample\"]\n---\n# Native fixture verification\nExercise native controls, impact reuse and bounded recovery.\n");
    Write("docs/references/agent-provider-profile.md", "| Setting | Value | Rationale |\n|---|---|---|\n| maximum-run-seconds | 3600 | Synthetic fixed ceiling, not allocation. |\n| direct-dirty-working-tree | denied | Preserve isolation. |\n");
    Write(".cis/local/native-allocation.json", JsonSerializer.Serialize(new
    {
        changeSeconds = 3600, changeAttempts = 6, restorationSeconds = 300,
        tasks = new[] { new { taskId = "WORK-090", seconds = 3300, attempts = 6, restorationSeconds = 300 } },
        decisionReference = "synthetic-fixture:explicit-native-verification-allocation",
        storageCeilingReference = "synthetic-fixture:one-gibibyte-owned-local-ceiling",
        acknowledgeHistoricalUnknown = true, controllers = new[] { actor },
        storage = new { maximumBytes = 1073741824, restorationBytes = 67108864,
            roots = new[] { new { repositoryPath = repository, path = ".cis/local" } }, cleanupControllers = new[] { actor } },
    }, json));
    Invoke("allocate", [cli, "agent", "work", "allocate", "CIS-0001", "--repo", repository, "--input", ".cis/local/native-allocation.json",
        "--actor", actor, "--reason", "Explicit bounded synthetic native verification fixture allocation.", "--format", "json"], 30);
    }
    else
    {
        Require(File.Exists(Path.Combine(repository, ".cis/local/agents/work-control.json"))
            && File.Exists(Path.Combine(repository, ".cis/local/native-allocation.json"))
            && File.Exists(Path.Combine(repository, "pilot-profile.json")),
            "Resume requires this fixture's retained canonical allocation and profiles; no new allowance is created.");
        Require(!File.Exists(Path.Combine(repository, ".cis/local/testing/incremental/smoke/state.json"))
            || JsonNode.Parse(File.ReadAllText(Path.Combine(repository, ".cis/local/testing/incremental/smoke/state.json")))!["batches"]!.AsObject().Count == 0,
            "This bounded setup resume is only for a fixture that has not completed an incremental batch; use its native continuation for later work.");
    }
    Catalog();
    if (!File.Exists(Path.Combine(repository, "docs/references/local-artifact-retention.md")))
        Write("docs/references/local-artifact-retention.md", """
            ---
            title: Native fixture artifact retention
            type: local-artifact-retention
            status: Active
            owner: Native fixture controller
            last_reviewed: 2026-11-06
            review_cadence: on fixture change
            ---

            | Family | Path | Retain days | Keep latest | Action |
            |---|---|---:|---:|---|
            | native-testing | .cis/local/testing | 7 | 2 | preserve |
            """);
    var providerProfile = Path.Combine(repository, "docs/references/agent-provider-profile.md");
    var providerText = File.ReadAllText(providerProfile);
    if (!providerText.StartsWith("---", StringComparison.Ordinal))
        Write("docs/references/agent-provider-profile.md", "---\ntitle: Native fixture provider bounds\ntype: agent-provider-profile\nstatus: Active\nowner: Native fixture controller\nlast_reviewed: 2026-11-06\nreview_cadence: on fixture change\n---\n\n" + providerText);
    if (resumeSetup)
    {
        var qualificationPath = Path.Combine(repository, "qualification-profile.json");
        var qualificationProfile = JsonNode.Parse(File.ReadAllText(qualificationPath))!;
        Require(qualificationProfile["id"]?.GetValue<string>() == "two-source-qualification", "Resume only the declared small native fixture qualification.");
        var configuration = qualificationProfile["configFiles"]!.AsArray();
        if (!configuration.Any(value => value?.GetValue<string>() == "src/Sample.Library/Shared.cs"))
        {
            configuration.Add("src/Sample.Library/Shared.cs");
            Write("qualification-profile.json", qualificationProfile.ToJsonString(json));
        }
    }
    Environment.SetEnvironmentVariable("CIS_WORK_AUTHORITY", repository);
    Environment.SetEnvironmentVariable("CIS_WORK_CHANGE", "CIS-0001");
    Environment.SetEnvironmentVariable("CIS_WORK_TASK", "WORK-090");
    Environment.SetEnvironmentVariable("CIS_WORK_ACTOR", actor);
    Invoke("restore", ["restore", "tests/Sample.Tests/Sample.Tests.csproj", "--ignore-failed-sources", "-p:NuGetAudit=false"], 120);
    Invoke("ordinary-build", ["build", "Sample.slnx", "-c", "Release", "--no-restore", "-m:1", "-nr:false"], 120);
    Invoke("ordinary-discovery", ["test", "tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-build", "--no-restore", "--list-tests"], 60);
    Invoke("ordinary-run-all", ["test", "tests/Sample.Tests/Sample.Tests.csproj", "-c", "Release", "--no-build", "--no-restore",
        "--logger", "trx;LogFileName=ordinary.trx", "--results-directory", Path.Combine(control, "ordinary-native")], 90);
    var representative = JsonNode.Parse(Invoke("representative-pilot", [cli, "test", "mutation", "run", "--repo", repository,
        "--profile", "pilot-profile.json", "--kind", "canary", "--format", "json"], 360))!;
    Require(representative["pilot"]?["ready"]?.GetValue<bool>() == true, "The real native representative pilot did not establish readiness.");
    records.Add(new { name = "representative-pilot", result = representative });
    var status = JsonNode.Parse(Invoke("pilot-owner-status", [cli, "agent", "work", "status", "CIS-0001", "--task", "WORK-090", "--repo", repository, "--format", "json"], 30))!;
    Write(".cis/local/pilot-assessment.json", JsonSerializer.Serialize(new
    {
        expectedRevision = status["work"]!["revision"]!.GetValue<string>(),
        checkpoint = new
        {
            outcome = "The native baseline passed, actual mutation was detected, the deliberately surviving control remained a survivor, missing native configuration was rejected, and restoration plus the real child round trip passed.",
            evidence = new[] { representative["pilot"]!["evidenceReference"]!.GetValue<string>(), representative["pilot"]!["summaryPath"]!.GetValue<string>() },
            approach = "Use the retained native pilot and graph-selected bounded batches; reuse current reports and retain genuine native misses without new assertion receivers.",
            decision = "Proceed to the small two-source affected fixture after inspecting native controls and measured setup cost; retain the same owner and finite allowance.",
            nextAction = "Run the first bounded incremental source, then inspect its actual result before the remaining affected source.",
            closeAttempt = false,
        },
    }, json));
    Invoke("pilot-controller-assessment", [cli, "agent", "work", "progress", "CIS-0001", "WORK-090", "--repo", repository,
        "--input", ".cis/local/pilot-assessment.json", "--actor", actor, "--format", "json"], 30);
    var qualification = JsonNode.Parse(Invoke("two-source-qualification", [cli, "test", "mutation", "run", "--repo", repository,
        "--profile", "qualification-profile.json", "--kind", "qualification", "--format", "json"], 360))!;
    Require(qualification["qualified"]?.GetValue<bool>() == true && qualification["assessment"]?["mutation"]?["validEvidence"]?.GetValue<bool>() == true
        && qualification["assessment"]?["mutation"]?["thresholdMet"]?.GetValue<bool>() == true,
        "The unchanged-threshold two-source native qualification did not qualify its complete declared fixture scope.");
    records.Add(new { name = "two-source-qualification", result = qualification });
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
    Write("src/Sample.Library/A.cs", "namespace Sample; public static class A { public static int Normalize(int value) { int result = Shared.Clamp(value); bool done = false; while (!done) { result += 1; done = true; } return result; } }");
    Graph();
    var timeout = Run("mutant-timeout", "all", 4);
    Require(Int(timeout, "timedOut") >= 1 && Int(timeout, "reusedMutationBatches") == 1
        && Int(timeout, "pendingMutationBatches") == 0 && timeout["scopeComplete"]?.GetValue<bool>() == true
        && timeout["completeScopeScore"] is not null && timeout["thresholdMet"]?.GetValue<bool>() == false
        && timeout["qualified"]?.GetValue<bool>() == false,
        "The completed native timeout must remain a conservative miss without kill credit or qualification, while preserving the unrelated completed batch.");
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
    var finalOwner = JsonNode.Parse(Invoke("final-owner-status", [cli, "agent", "work", "status", "CIS-0001", "--task", "WORK-090",
        "--repo", repository, "--format", "json"], 30))!;
    var finalStorage = JsonNode.Parse(Invoke("final-owned-storage", [cli, "artifacts", "inventory", "--repo", repository,
        "--change", "CIS-0001", "--task", "WORK-090", "--actor", actor, "--format", "json"], 30))!;
    var audit = new { status = "passed", elapsedSeconds = started.Elapsed.TotalSeconds, repositoryBytes = Bytes(repository),
        retainedResultBytes = Bytes(Path.Combine(repository, ".cis/local/testing")), phases = records, phaseCosts,
        work = finalOwner["work"], ownedStorage = finalStorage["ownedStorage"],
        batching = "Incremental execution remains per file. The native representative and complete two-source qualification group compatible fixture sources in existing native invocations; no separate scheduler is introduced.",
        ordinarySolution = Path.Combine(repository, "Sample.slnx") };
    File.WriteAllText(Path.Combine(control, "validation-summary" + runSuffix + ".json"), JsonSerializer.Serialize(audit, json));
    Console.WriteLine(JsonSerializer.Serialize(audit, json));
    return 0;
}
catch (Exception error)
{
    var failure = new { status = "failed", error = error.Message, elapsedSeconds = started.Elapsed.TotalSeconds, phases = records };
    File.WriteAllText(Path.Combine(control, "validation-summary" + runSuffix + ".json"), JsonSerializer.Serialize(failure, json));
    Console.Error.WriteLine(JsonSerializer.Serialize(failure, json));
    return 1;
}

void Write(string relative, string content) { var path = Path.Combine(repository, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
void Catalog() => Write("docs/catalog.yml", """
    schema_version: 1
    repository: incremental-sample
    documents:
      - { id: incremental-sample:docs:root, path: docs/README.md, type: navigation, status: draft, authority: canonical }
      - { id: incremental-sample:change:plan, path: docs/changes/CIS-0001/plan.md, type: delivery-plan, status: approved, authority: canonical }
      - { id: incremental-sample:change:impact, path: docs/changes/CIS-0001/impact.md, type: impact-analysis, status: accepted, authority: canonical }
      - { id: incremental-sample:task:work-090, path: docs/changes/CIS-0001/agent-tasks/WORK-090.md, type: agent-task, status: ready, authority: canonical }
      - { id: incremental-sample:reference:agent-provider-profile, path: docs/references/agent-provider-profile.md, type: agent-provider-profile, status: active, authority: canonical }
      - { id: incremental-sample:reference:artifact-retention, path: docs/references/local-artifact-retention.md, type: local-artifact-retention, status: active, authority: canonical }
    """);
void Graph()
{
    var result = JsonNode.Parse(Invoke("graph", [cli, "graph", "build", "--repo", repository, "--format", "json"], 90))!;
    Require(result["errorCount"]!.GetValue<int>() == 0 && result["warningCount"]!.GetValue<int>() == 0,
        "The native fixture graph must be complete and warning-free before incremental execution.");
}
JsonNode Run(string name, string layer, int batches, int exit = 0)
{
    var text = Invoke(name, [cli, "test", "incremental", "run", "--repo", repository, "--profile", "profile.json", "--layer", layer,
        "--max-batches", batches.ToString(System.Globalization.CultureInfo.InvariantCulture), "--max-seconds", "300", "--format", "json"], 360, exit);
    var node = JsonNode.Parse(text)!;
    records.Add(new { name, result = node });
    File.WriteAllText(Path.Combine(control, "validation-progress" + runSuffix + ".json"), JsonSerializer.Serialize(records, json));
    Console.WriteLine($"{name}: status={node["status"]}; unit={node["executedUnitBatches"]}; mutation={node["executedMutationBatches"]}; reused={node["reusedMutationBatches"]}");
    return node;
}
string Invoke(string name, string[] arguments, int seconds, int expected = 0)
{
    var phase = Stopwatch.StartNew();
    var start = new ProcessStartInfo(dotnet) { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native command.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(seconds * 1000)) { process.Kill(true); throw new InvalidOperationException("Validation command exceeded its bound: " + name); }
    var output = stdout.GetAwaiter().GetResult();
    var errors = stderr.GetAwaiter().GetResult();
    phaseCosts.Add(new { name, elapsedMilliseconds = phase.ElapsedMilliseconds, exitCode = process.ExitCode });
    File.WriteAllText(Path.Combine(control, name + runSuffix + ".json"), output);
    if (errors.Length > 0) File.WriteAllText(Path.Combine(control, name + runSuffix + ".stderr.log"), errors);
    Require(process.ExitCode == expected, name + " exited " + process.ExitCode + " instead of " + expected + ": " + errors + " " + output);
    return output;
}
static int Int(JsonNode value, string name) => value[name]!.GetValue<int>();
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static long Bytes(string root) => Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0;
