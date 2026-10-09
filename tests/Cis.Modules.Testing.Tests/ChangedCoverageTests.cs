using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Cis.Abstractions;
using Cis.Modules.Testing;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class ChangedCoverageTests
{
    [Fact]
    public void ChangedLinesUseNativeHitsAndIgnoreUnchangedUncoveredLines()
    {
        using var fixture = new Fixture();
        fixture.Write("src/Reading.cs", "class Reading {\nint Value() => 2;\nint Other() => 0;\n}\n");
        var execution = fixture.Read((2, 1), (3, 0));
        Assert.Equal(100, execution.Coverage!.Lines);
        Assert.Equal(1, execution.Coverage.MeasuredLines);
        Assert.Equal("changed-production", execution.Coverage.Scope);
        Assert.Equal(fixture.Revision, execution.Coverage.BaseRevision);
        Assert.Contains(execution.Artifacts, artifact => artifact.Kind == "coverage-result");
    }

    [Fact]
    public void NewUninstrumentedProductionFilesCannotDisappearFromDenominator()
    {
        using var fixture = new Fixture();
        fixture.Write("src/Reading.cs", "class Reading {\nint Value() => 2;\nint Other() => 0;\n}\n");
        fixture.Write("src/Uncovered.cs", "class Uncovered { int Value() => 4; }\n");
        Assert.Equal(50, fixture.Read((2, 1)).Coverage!.Lines);
        Assert.Equal(1, fixture.Read((2, 1)).Coverage!.UninstrumentedChangedFiles);
    }

    [Fact]
    public void ZeroChangedExecutableLinesIsNotAFullCoveragePass()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Read((2, 1)).Coverage!.MeasuredLines);
        Assert.Equal(0, fixture.Read((2, 1)).Coverage!.Lines);
        Assert.Equal(0, fixture.Read((2, 1)).Coverage!.ChangedProductionLines);
        fixture.Write("src/Reading.cs", "// changed production comment\nclass Reading {}\n");
        Assert.True(fixture.Read().Coverage!.ChangedProductionLines > 0);
    }

    [Fact]
    public void CommentOnlyChangeRetainsProofThatEveryChangedFileWasInstrumented()
    {
        using var fixture = new Fixture();
        fixture.Write("src/Reading.cs", "// Comment\nclass Reading {\nint Value() => 1;\nint Other() => 0;\n}\n");
        var coverage = fixture.Read((3, 1), (4, 1)).Coverage!;
        Assert.Equal(1, coverage.ChangedProductionLines);
        Assert.Equal(0, coverage.MeasuredLines);
        Assert.Equal(0, coverage.UninstrumentedChangedFiles);
        var serialized = JsonSerializer.SerializeToElement(coverage, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1, serialized.GetProperty("changedProductionLines").GetInt32());
        Assert.Equal(0, serialized.GetProperty("uninstrumentedChangedFiles").GetInt32());
        Assert.Equal(0, serialized.GetProperty("measuredLines").GetInt32());
    }

    [Fact]
    public void VirtualGeneratedLoggingSourceDoesNotInvalidateProductionCoverage()
    {
        using var fixture = new Fixture();
        fixture.Write("src/Reading.cs", "class Reading {\nint Value() => 2;\nint Other() => 0;\n}\n");
        fixture.Read((2, 1));
        var report = Path.Combine(fixture.Root, ".cis/local/coverage.xml");
        var document = XDocument.Load(report);
        document.Descendants("classes").First().Add(new XElement("class", new XAttribute("filename", "obj/Debug/LoggerMessage.g.cs")));
        document.Save(report);
        var suite = new TestSuiteProfile("unit", "example", "unit", "native", "dotnet test", ".", "trx", "result.trx", "coverage.xml", "-", "-", "always", "pr", "retain");
        var result = new TrxTestResultAdapter().Read(new(fixture.Root, suite, Path.Combine(fixture.Root, ".cis/local/native.trx"), report, null));
        Assert.Equal(100, result.Coverage!.Lines);
    }

    [Theory]
    [InlineData("HEAD", "src")]
    [InlineData("0000000000000000000000000000000000000000", "src")]
    [InlineData("base", "../outside")]
    public void InvalidBaseOrEscapingProductionScopeFails(string revision, string production)
    {
        using var fixture = new Fixture();
        fixture.Scope(revision == "base" ? fixture.Revision : revision, production);
        Assert.Throws<InvalidDataException>(() => fixture.Read((2, 1)));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Combine(Path.GetTempPath(), "cis-coverage-tests");
        public string Root { get; } = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        public string Revision { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Git("init", "--quiet");
            Write("src/Reading.cs", "class Reading {\nint Value() => 1;\nint Other() => 0;\n}\n");
            Git("add", "src/Reading.cs");
            Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Synthetic baseline");
            Revision = Git("rev-parse", "HEAD").Trim();
            Scope(Revision, "src");
        }
        public void Scope(string revision, string production)
            => Write(".cis/coverage-scope.json", JsonSerializer.Serialize(new { schemaVersion = 1, baseRevision = revision, productionPaths = new[] { production } }));
        public void Write(string relative, string content)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public TestSuiteExecution Read(params (int Line, int Hits)[] lines)
        {
            Write(".cis/local/native.trx", "<TestRun><Results><UnitTestResult testName=\"MeaningfulContract\" outcome=\"Passed\" /></Results></TestRun>");
            var nativeLines = new XElement("lines", lines.Select(line => new XElement("line",
                new XAttribute("number", line.Line), new XAttribute("hits", line.Hits))));
            var classes = new XElement("classes", new XElement("class", new XAttribute("filename", "Reading.cs"), nativeLines));
            var report = new XElement("coverage", new XElement("sources", new XElement("source", Path.Combine(Root, "src"))),
                new XElement("packages", new XElement("package", classes)));
            Write(".cis/local/coverage.xml", report.ToString());
            var suite = new TestSuiteProfile("unit", "core", "unit", "xunit", "dotnet test", ".", "trx", ".cis/local/native.trx", ".cis/local/coverage.xml", "-", "-", "always", "pr", "retain");
            return new TrxTestResultAdapter().Read(new(Root, suite, Path.Combine(Root, suite.ResultPath), Path.Combine(Root, suite.CoveragePath), null));
        }
        private string Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = Root };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(10));
            Assert.Equal(0, result.ExitCode);
            return result.StandardOutput;
        }
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Parent, Root)) throw new InvalidOperationException();
            // Git's object files can be read-only on Windows.
            foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
