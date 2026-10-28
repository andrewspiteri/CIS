using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

[CollectionDefinition("Source-variant native evidence", DisableParallelization = true)]
public sealed class SourceVariantNativeCollection;

[Collection("Source-variant native evidence")]
public sealed class SourceVariantEvidenceTests
{
    [Fact]
    public void ActualMSTestAttemptsProduceCanonicalPassFailPassEvidenceWithoutChangingRawHistoryOrApplyingCredit()
    {
        using var fixture = new NativeFixture();
        fixture.RunPhases();
        var before = fixture.AllArtifacts();

        var verified = fixture.Verify();

        Assert.True(verified.ExitCode == 0, string.Join("; ", verified.Diagnostics));
        Assert.Equal("verified-source-variant", verified.Status);
        Assert.False(verified.Qualified);
        Assert.True(verified.Partial);
        Assert.Equal("Survived", verified.OriginalStatus);
        Assert.Equal(["Passed", "Failed", "Passed"], verified.Phases.Select(phase => phase.Outcome));
        Assert.Equal(verified.Phases[0].SourceSha256, verified.Phases[2].SourceSha256);
        Assert.NotEqual(verified.Phases[0].SourceSha256, verified.Phases[1].SourceSha256);
        Assert.Equal("NeutralFixture.SubjectTests.ValueIsPresent", verified.NativeTestMethod);
        foreach (var phase in verified.Phases)
        {
            Assert.Equal(phase.SourceSha256, phase.Body.DocumentSha256);
            Assert.Equal(DigestFile(phase.Body.AssemblyPath), phase.Body.AssemblySha256);
            Assert.Equal(DigestFile(phase.Body.PdbPath), phase.Body.PdbSha256);
            Assert.Equal(DigestFile(phase.TestBody.AssemblyPath), phase.TestBody.AssemblySha256);
        }
        foreach (var artifact in before) { Assert.Equal(artifact.Value, DigestFile(artifact.Key)); }
        Assert.True(fixture.UnitState(1).Batches.Single().Value.Unit is { Completed: false, Failed: 1 });

        // These controls mutate already-produced evidence. They do not fabricate
        // successful execution flags or replace the three actual native attempts.
        fixture.WithFile(fixture.Source(1), File.ReadAllText(fixture.Source(1)) + "\n", () => AssertFailed(fixture.Verify()));
        fixture.WithFile(fixture.Report, File.ReadAllText(fixture.Report).Replace("Survived", "Killed", StringComparison.Ordinal),
            () => AssertFailed(fixture.Verify()));
        AssertFailed(fixture.Service.VerifySourceVariant(fixture.Options with { NativeTestId = Guid.NewGuid().ToString() }));
        AssertFailed(fixture.Service.VerifySourceVariant(fixture.Options with { SubjectAssemblyPath = "bin/Release/net10.0/Other.dll" }));
        var variantPdb = Path.ChangeExtension(fixture.Assembly(1), ".pdb");
        fixture.WithBytes(variantPdb, File.ReadAllBytes(Path.ChangeExtension(fixture.Assembly(0), ".pdb")), () => AssertFailed(fixture.Verify()));
        fixture.ChangeState(1, state =>
        {
            var key = state.Batches.Keys.Single();
            var saved = state.Batches[key];
            state.Batches[key] = saved with { Unit = saved.Unit! with { Execution = saved.Unit.Execution! with { Status = "timeout" } } };
        }, () => AssertFailed(fixture.Verify()));
        fixture.ChangeState(1, state =>
        {
            var key = state.Batches.Keys.Single();
            var saved = state.Batches[key];
            state.Batches[key] = saved with { Unit = saved.Unit! with { Execution = saved.Unit.Execution! with { CisToolAssemblySha256 = new string('0', 64) } } };
        }, () => AssertFailed(fixture.Verify()));
        fixture.WithFile(fixture.SavedStatePath(1), JsonSerializer.Serialize(fixture.UnitState(1) with { RecoveryRequired = true },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), () => AssertFailed(fixture.Verify()));
        fixture.AssertActualChangedInputHold();
        var final = fixture.Verify();
        Assert.True(final.ExitCode == 0, string.Join("; ", final.Diagnostics));
    }

    [Fact]
    public void CaptureRechecksTheLoadedTestAssemblyAndItsActualPortableSourceDocument()
    {
        var observed = SourceVariantBodyCapture.Observe(typeof(SourceVariantEvidenceTests).Assembly,
            "tests/Cis.Modules.Testing.Tests/SourceVariantEvidenceTests.cs");
        Assert.Equal(typeof(SourceVariantEvidenceTests).Module.ModuleVersionId.ToString("D"), observed.ModuleVersionId);
        Assert.Equal(observed, SourceVariantBodyCapture.Inspect(observed.AssemblyPath,
            "tests/Cis.Modules.Testing.Tests/SourceVariantEvidenceTests.cs"));
        Assert.Throws<InvalidDataException>(() => SourceVariantBodyCapture.Inspect(observed.AssemblyPath, "../Subject.cs"));
        Assert.Throws<InvalidDataException>(() => SourceVariantBodyCapture.Inspect(observed.AssemblyPath, "NotPresent.cs"));
        string root = Directory.CreateTempSubdirectory("cis-pdb-integrity-").FullName;
        try
        {
            string assembly = Path.Combine(root, Path.GetFileName(observed.AssemblyPath)), pdb = Path.ChangeExtension(assembly, ".pdb");
            File.Copy(observed.AssemblyPath, assembly); File.Copy(observed.PdbPath, pdb);
            byte[] bytes = File.ReadAllBytes(pdb), checksum = Convert.FromHexString(observed.DocumentSha256);
            int position = bytes.AsSpan().IndexOf(checksum);
            Assert.True(position >= 0);
            bytes[position] ^= 1;
            File.WriteAllBytes(pdb, bytes);
            using var stream = File.OpenRead(pdb);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            Assert.Equal(observed.PdbId, Convert.ToHexStringLower(provider.GetMetadataReader().DebugMetadataHeader!.Id.AsSpan()));
            Assert.Contains("checksum", Assert.Throws<InvalidDataException>(() => SourceVariantBodyCapture.Inspect(assembly,
                "tests/Cis.Modules.Testing.Tests/SourceVariantEvidenceTests.cs")).Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AssertFailed(SourceVariantEvidenceResult result)
    {
        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Qualified);
        Assert.True(result.Partial);
        Assert.Empty(result.Phases);
    }
    private static string DigestFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal sealed class NativeFixture : IDisposable, ICisGraphSnapshotReader
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private const string Original = "namespace NeutralFixture; public static class Subject { public static bool Value => true; }\n";
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-source-variant-" + Guid.NewGuid().ToString("N"));
        private readonly string[] _repositories;
        private readonly string _dotnet;
        internal IncrementalTestService Service { get; }
        internal SourceVariantEvidenceOptions Options { get; private set; } = null!;
        internal string Report => Full(0, "native-report.json");

        internal NativeFixture()
        {
            _repositories = Enumerable.Range(0, 3).Select(index => System.IO.Path.Combine(_root, "phase-" + index.ToString(CultureInfo.InvariantCulture))).ToArray();
            _dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? System.IO.Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
            Service = new(this);
            for (int index = 0; index < 3; index++)
            {
                Directory.CreateDirectory(_repositories[index]);
                File.WriteAllText(Source(index), index == 1 ? Original.Replace("true", "false", StringComparison.Ordinal) : Original);
                File.WriteAllText(Full(index, "SubjectTests.cs"), TestSource);
                File.WriteAllText(Full(index, "run-input.txt"), "current input");
                string helper = System.Security.SecurityElement.Escape(typeof(SourceVariantBodyCapture).Assembly.Location)!;
                File.WriteAllText(Full(index, "NativeTests.csproj"), $$"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject>
                        <AssemblyName>NativeVariant.Tests</AssemblyName><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
                        <DebugType>portable</DebugType><ChecksumAlgorithm>SHA256</ChecksumAlgorithm><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                        <UseSharedCompilation>false</UseSharedCompilation><NuGetAudit>false</NuGetAudit>
                      </PropertyGroup>
                      <ItemGroup>
                        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.6.0" />
                        <PackageReference Include="MSTest.TestFramework" Version="4.5.1" />
                        <PackageReference Include="MSTest.TestAdapter" Version="4.5.1" />
                        <Reference Include="Cis.Abstractions"><HintPath>{{helper}}</HintPath></Reference>
                      </ItemGroup>
                    </Project>
                    """);
                var restore = new ProcessStartInfo(_dotnet) { WorkingDirectory = _repositories[index] };
                foreach (string argument in new[] { "restore", "NativeTests.csproj", "--ignore-failed-sources", "--disable-parallel" }) { restore.ArgumentList.Add(argument); }
                restore.Environment["MSBUILDDISABLENODEREUSE"] = "1";
                restore.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
                var restored = CisProcessSafety.Run(restore, TimeSpan.FromSeconds(120), 16384);
                Assert.True(restored.ExitCode == 0 && !restored.TimedOut && !restored.OutputTruncated, restored.StandardOutput + restored.StandardError);
                var profile = new IncrementalTestProfile
                {
                    SchemaVersion = 1,
                    Id = "native-variant",
                    SourceFiles = ["Subject.cs"],
                    TestFiles = ["SubjectTests.cs"],
                    ConfigFiles = ["NativeTests.csproj", "packages.lock.json", "run-input.txt"],
                    ToolFiles = [_dotnet, typeof(SourceVariantBodyCapture).Assembly.Location],
                    FullTestFilter = "FullyQualifiedName=NeutralFixture.SubjectTests.ValueIsPresent",
                    UnitReportPath = "unit.trx",
                    UnitCommand = [_dotnet, "test", "NativeTests.csproj", "-c", "Release", "--no-restore", "--filter", "{testFilter}", "--logger", "trx;LogFileName=unit.trx", "--results-directory", "{output}", "-m:1", "-nr:false"],
                    Environment = new() { ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["UseSharedCompilation"] = "false" },
                    TimeoutSeconds = 120,
                    MinFreeBytes = 0,
                };
                File.WriteAllText(Full(index, "profile.json"), JsonSerializer.Serialize(profile, Json));
            }
        }

        internal void RunPhases()
        {
            for (int index = 0; index < 3; index++)
            {
                var result = Service.Run(new(_repositories[index], "profile.json", "unit", 1, 180));
                Assert.True(result.ExitCode == (index == 1 ? 4 : 0), string.Join("; ", result.Diagnostics));
                Assert.Equal(1, result.ExecutedUnitBatches);
            }
            var state = UnitState(0);
            var unit = state.Batches.Single().Value.Unit!;
            var trx = XDocument.Load(Full(0, IncrementalTestService.LocalRoot + "/native-variant/" + unit.ReportPath));
            string id = trx.Descendants().Single(node => node.Name.LocalName == "UnitTestResult").Attribute("testId")!.Value;
            int from = Original.IndexOf("true", StringComparison.Ordinal);
            File.WriteAllText(Report, JsonSerializer.Serialize(new
            {
                schemaVersion = "2",
                files = new Dictionary<string, object>
                {
                    ["Subject.cs"] = new
                    {
                        source = Original,
                        mutants = new[] { new { id = "17", status = "Survived", mutatorName = "Boolean mutation", replacement = "false",
                            location = new { start = new { line = 1, column = from + 1 }, end = new { line = 1, column = from + 5 } } } }
                    }
                },
                testFiles = new Dictionary<string, object>
                {
                    ["SubjectTests.cs"] = new
                    {
                        source = TestSource,
                        tests = new[] { new { id, name = "NeutralFixture.SubjectTests.ValueIsPresent",
                        location = new { start = new { line = 1, column = 1 }, end = new { line = 2, column = 1 } } } }
                    }
                }
            }, Json));
            Options = new(_repositories[0], "native-report.json", DigestFile(Report), "Subject.cs", "bin/Release/net10.0/NativeVariant.Tests.dll", "17", id,
                new(_repositories[0], "profile.json"), new(_repositories[1], "profile.json"), new(_repositories[2], "profile.json"));
        }
        internal SourceVariantEvidenceResult Verify() => Service.VerifySourceVariant(Options);
        internal string WriteManifest()
        {
            const string relative = ".cis/source-variant-evidence.json";
            File.WriteAllText(Full(0, relative), JsonSerializer.Serialize(new { schemaVersion = 1, evidence = Options }, Json));
            return relative;
        }
        internal string Source(int phase) => Full(phase, "Subject.cs");
        internal string Assembly(int phase) => Full(phase, "bin/Release/net10.0/NativeVariant.Tests.dll");
        internal IncrementalTestState UnitState(int phase) => JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllText(StatePath(phase)), Json)!;
        internal string SavedStatePath(int phase) => StatePath(phase);
        internal Dictionary<string, string> AllArtifacts() => _repositories.SelectMany(root => Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToDictionary(path => path, DigestFile);
        internal void ChangeState(int phase, Action<IncrementalTestState> change, Action check)
        {
            var state = UnitState(phase); change(state);
            WithFile(StatePath(phase), JsonSerializer.Serialize(state, Json), check);
        }
        internal void AssertActualChangedInputHold()
        {
            byte[] state = File.ReadAllBytes(StatePath(1));
            string report = Full(1, IncrementalTestService.LocalRoot + "/native-variant/" + UnitState(1).Batches.Single().Value.Unit!.ReportPath);
            byte[] native = File.ReadAllBytes(report);
            try
            {
                File.WriteAllText(Full(1, "change-input.flag"), "exercise actual changed-input detection");
                var attempt = Service.Run(new(_repositories[1], "profile.json", "unit", 1, 180, Force: true));
                Assert.Equal(4, attempt.ExitCode);
                Assert.Contains(UnitState(1).Batches.Single().Value.Unit!.Diagnostics, diagnostic => diagnostic.Contains("Relevant inputs changed", StringComparison.Ordinal));
                File.WriteAllText(Full(1, "run-input.txt"), "current input");
                var result = Verify();
                AssertFailed(result);
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("input-change or execution hold", StringComparison.Ordinal));
            }
            finally
            {
                File.Delete(Full(1, "change-input.flag")); File.WriteAllText(Full(1, "run-input.txt"), "current input");
                File.WriteAllBytes(StatePath(1), state); File.WriteAllBytes(report, native);
            }
        }
        internal void WithFile(string path, string content, Action check) => WithBytes(path, Encoding.UTF8.GetBytes(content), check);
        internal void WithBytes(string path, byte[] bytes, Action check)
        {
            byte[] original = File.ReadAllBytes(path);
            try { File.WriteAllBytes(path, bytes); check(); }
            finally { File.WriteAllBytes(path, original); }
        }
        private string StatePath(int phase) => Full(phase, IncrementalTestService.LocalRoot + "/native-variant/state.json");
        private string Full(int phase, string relative) => System.IO.Path.Combine(_repositories[phase], relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        // Selection is a bounded synthetic graph; native builds, MSTest attempts,
        // CIS process receipts and PE/PDB body observations are real.
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            string[] files = ["Subject.cs", "SubjectTests.cs", "NativeTests.csproj", "packages.lock.json", "run-input.txt"];
            var build = new CisGraphBuildMetadata("native-fixture", "fixture", null, true, "complete");
            var graph = new CisGraphDocument(6, build, [], [], new(0, 0, 0));
            var manifest = new CisGraphManifest(6, build.Id, "fixture", "docs", null, true,
                files.Select(file => new CisGraphInput(file, DigestFile(System.IO.Path.Combine(repositoryPath, file)))).ToArray(), ["native-fixture/1"]);
            return new("available", 0, repositoryPath, "fresh", graph, manifest, [], [], []);
        }
        public void Dispose() { if (Directory.Exists(_root)) { Directory.Delete(_root, true); } }

        private const string TestSource = """
            using Cis.Abstractions;
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            namespace NeutralFixture;
            [TestClass]
            public sealed class SubjectTests
            {
                public TestContext TestContext { get; set; } = null!;
                [TestMethod]
                public void ValueIsPresent()
                {
                    TestContext.WriteLine(SourceVariantBodyCapture.Encode(typeof(Subject).Assembly, "Subject.cs", typeof(SubjectTests).Assembly, "SubjectTests.cs"));
                    var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Subject).Assembly.Location)!, "../../.."));
                    if (File.Exists(Path.Combine(root, "change-input.flag"))) { File.AppendAllText(Path.Combine(root, "run-input.txt"), "changed"); }
                    Assert.IsTrue(Subject.Value);
                }
            }
            """;
    }
}
