using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    /// <summary>Verify one exact source variant against three canonical unit-only native attempts; never apply mutation credit.</summary>
    public SourceVariantEvidenceResult VerifySourceVariant(SourceVariantEvidenceOptions options)
    {
        try
        {
            Require(Guid.TryParse(options.NativeTestId, out var selectedId) && selectedId != Guid.Empty, "Select one native test GUID.");
            var canonical = Path.GetFullPath(options.RepositoryPath);
            var currentSource = Safe(canonical, options.SourceFile);
            var originalBytes = BoundedSource(currentSource);
            var originalText = File.ReadAllText(currentSource);
            var report = Safe(canonical, options.ReportPath);
            Require(new FileInfo(report).Length is > 0 and <= 100 * 1024 * 1024 && HashFile(report) == options.ReportSha256,
                "The original native report is missing, excessive or changed.");
            using var native = JsonDocument.Parse(File.ReadAllBytes(report));
            var duplicates = new List<string>();
            StrykerJsonResultAdapter.CheckDuplicateProperties(native.RootElement, "$", duplicates);
            Require(duplicates.Count == 0 && native.RootElement.GetProperty("schemaVersion").GetString() == "2", "Invalid original native report.");

            var phases = new[] { options.Original, options.Variant, options.Restored };
            var inputs = phases.Select(phase => Read(new(phase.RepositoryPath, phase.ProfilePath, "unit"))).ToArray();
            Require(inputs.Select(input => input.Repository).Distinct(PathComparer).Count() == 3
                && inputs.All(input => inputs.All(other => input == other || !CisPathSafety.IsUnderRoot(input.Repository, other.Repository, true))),
                "Use three distinct, non-nested canonical unit snapshots.");
            Require(inputs.All(input => input.Profile.MutationCommand is null && input.Profile.SourceFiles is { Length: 1 }
                && input.Profile.SourceFiles[0] == options.SourceFile && input.Profile.UnitResultFormat == "trx"),
                "Source-variant evidence requires one declared source and three unit-only TRX profiles.");
            Require(inputs.All(input => input.CommonIdentity == inputs[0].CommonIdentity),
                "Source-variant profiles, configuration, tools, producers or native environment differ.");
            var working = Safe(canonical, inputs[0].Profile.WorkingDirectory, true);
            var file = native.RootElement.GetProperty("files").EnumerateObject().Single(item => NativePathMatches(item.Name, options.SourceFile, canonical, working)).Value;
            Require(file.GetProperty("source").GetString() == originalText, "Original native source is not current.");
            var definition = file.GetProperty("mutants").EnumerateArray().Single(item => item.GetProperty("id").GetString() == options.MutantId);
            Require(definition.GetProperty("status").GetString() == "Survived", "This diagnostic path binds an original native Survived definition only.");
            var span = RefinementLocation(definition.GetProperty("location"), originalText);
            int start = VariantOffset(originalText, span.StartLine, span.StartColumn), end = VariantOffset(originalText, span.EndLine, span.EndColumn);
            Require(end > start, "A source replacement must bind a nonempty exact native span.");
            string replacement = definition.GetProperty("replacement").GetString() ?? throw new InvalidDataException("Missing native replacement.");
            Require(definition.GetProperty("mutatorName").GetString() is { Length: > 0 and <= 512 } && replacement.Length <= 1048576,
                "Native operator or replacement is missing or excessive.");
            string variantText = originalText[..start] + replacement + originalText[end..];
            Require(variantText != originalText, "A source variant must change its original definition.");
            byte[] variantBytes = SourceBytes(variantText, originalBytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            string definitionHash = HashObject(new
            {
                source = originalText, id = options.MutantId, mutatorName = definition.GetProperty("mutatorName").GetString(),
                replacement, location = span
            });

            var pins = new Dictionary<string, string>(PathComparer) { [currentSource] = HashFile(currentSource), [report] = options.ReportSha256 };
            var results = new List<SourceVariantEvidencePhaseResult>();
            var saved = new List<(Input Input, string StateHash, string SelectionHash)>();
            Selection? firstSelection = null;
            string? method = null;
            DateTimeOffset? finished = null;
            foreach (var (input, index) in inputs.Select((input, index) => (input, index)))
            {
                var leasePath = Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock");
                using var lease = File.Exists(leasePath) ? new FileStream(leasePath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                var state = State(input);
                Require(!state.RecoveryRequired && state.ActiveExecutionUntilUtc is null, "A source-variant snapshot has unfinished native work or recovery.");
                var selection = Select(input, state, false);
                var batch = selection.Batches.Single();
                if (firstSelection is null) { firstSelection = selection; }
                else
                {
                    var prior = firstSelection.Batches.Single();
                    Require(batch.TestFilter == prior.TestFilter && batch.TestFiles.SequenceEqual(prior.TestFiles, PathComparer)
                        && batch.DependencyFiles.SequenceEqual(prior.DependencyFiles, PathComparer), "Source-variant selected tests or dependency closure differ.");
                }
                foreach (var relative in input.Profile.SourceFiles.Concat(input.Profile.TestFiles).Concat(input.Profile.ConfigFiles).Concat(batch.DependencyFiles).Distinct(PathComparer))
                {
                    var path = Safe(input.Repository, relative);
                    if (relative == options.SourceFile)
                    {
                        Require(BoundedSource(path).SequenceEqual(index == 1 ? variantBytes : originalBytes), "A phase is not the exact original, single replacement or restored source.");
                    }
                    else
                    {
                        string current = Safe(canonical, relative);
                        Require(HashFile(path) == HashFile(current), "A phase changed a current test, dependency or build input.");
                        pins[current] = HashFile(current);
                    }
                    pins[path] = HashFile(path);
                }
                foreach (var path in input.Profile.ToolFiles.Select(path => Path.GetFullPath(path, input.Repository))) { pins[path] = HashFile(path); }
                pins[input.ProfilePath] = HashFile(input.ProfilePath);
                var unit = state.Batches.GetValueOrDefault(batch.Id)?.Unit ?? throw new InvalidDataException("Missing canonical native unit attempt.");
                Require(unit.Fingerprint == batch.UnitFingerprint && unit.Execution is { Status: "exited", Error: null, OutputTruncated: false }
                    && unit.Execution.ExitCode == (index == 1 ? 1 : 0) && unit.Total == 1 && unit.Skipped == 0
                    && unit.Completed == (index != 1) && unit.Status == (index == 1 ? "failed" : "completed"),
                    "A canonical native phase is incomplete or has an unexpected execution/outcome.");
                var helper = RefinementProducer(input, batch, unit, false);
                VerifyRefinementCommand(input, batch, unit.Execution!, input.Profile.UnitCommand, false, helper);
                var phaseReport = Safe(input.Root, unit.ReportPath);
                Require(HashFile(phaseReport) == unit.ReportSha256, "A canonical phase report changed.");
                var measured = ReadUnit(input, phaseReport);
                Require(measured.Total == 1 && measured.Skipped == 0 && measured.Passed == (index == 1 ? 0 : 1)
                    && measured.Failed == (index == 1 ? 1 : 0) && measured.Passed == unit.Passed && measured.Failed == unit.Failed,
                    "Canonical native TRX outcomes do not match the phase record.");
                var expectedDiagnostics = index == 1
                    ? new[] { $"Native execution: {unit.Execution!.Status}, exit={unit.Execution.ExitCode}. {unit.Execution.Error}" }.Concat(measured.Diagnostics)
                    : measured.Diagnostics;
                Require(unit.Diagnostics.SequenceEqual(expectedDiagnostics, StringComparer.Ordinal),
                    "A canonical native phase retains an input-change or execution hold beyond the expected native outcome.");
                Require(finished is null || unit.FinishedUtc >= finished, "Native source-variant phases are out of order.");
                finished = unit.FinishedUtc;
                var (nativeMethod, observation) = ReadSourceVariantTrx(phaseReport, selectedId, index == 1 ? "Failed" : "Passed", input,
                    options.SourceFile, options.SubjectAssemblyPath);
                var body = observation.Subject;
                method ??= nativeMethod;
                Require(nativeMethod == method, "The three native phases executed different methods.");
                string testSource = VerifyOriginalVariantTest(native.RootElement, input, batch, selectedId, method, canonical, working);
                Require(body.DocumentSha256 == HashFile(Safe(input.Repository, options.SourceFile))
                    && body.CaptureHelperSha256 == HashFile(typeof(SourceVariantBodyCapture).Assembly.Location),
                    "Loaded body source or capture helper differs from the exact current phase.");
                Require(observation.TestBody == SourceVariantBodyCapture.Inspect(observation.TestBody.AssemblyPath, testSource)
                    && observation.TestBody.DocumentSha256 == HashFile(Safe(input.Repository, testSource)),
                    "Executing native test body/source differs from the current original-report test.");
                pins[phaseReport] = unit.ReportSha256;
                pins[body.AssemblyPath] = body.AssemblySha256;
                pins[body.PdbPath] = body.PdbSha256;
                pins[observation.TestBody.AssemblyPath] = observation.TestBody.AssemblySha256;
                pins[observation.TestBody.PdbPath] = observation.TestBody.PdbSha256;
                string stateHash = HashFile(input.StatePath);
                saved.Add((input, stateHash, HashObject(selection.Batches)));
                results.Add(new(index == 0 ? "original" : index == 1 ? "variant" : "restored", HashFile(Safe(input.Repository, options.SourceFile)),
                    unit.ReportSha256, stateHash, batch.UnitFingerprint, index == 1 ? "Failed" : "Passed", body, observation.TestBody));
            }
            foreach (var phase in saved)
            {
                var current = Read(new(phase.Input.Repository, Relative(phase.Input.Repository, phase.Input.ProfilePath), "unit"));
                Require(current.CommonIdentity == phase.Input.CommonIdentity && HashFile(current.StatePath) == phase.StateHash
                    && HashObject(Select(current, State(current), false).Batches) == phase.SelectionHash, "A source-variant cache, graph selection or input changed during verification.");
            }
            foreach (var pin in pins) { Require(HashFile(pin.Key) == pin.Value, "A source-variant artifact changed during verification."); }
            var result = new SourceVariantEvidenceResult("verified-source-variant", 0)
            {
                OriginalReportSha256 = options.ReportSha256, DefinitionSha256 = definitionHash, OriginalStatus = "Survived",
                NativeTestId = selectedId.ToString(), NativeTestMethod = method,
                EnvironmentSha256 = HashObject(new { inherited = AmbientEnvironmentIdentity(inputs[0].Profile, false), declared = inputs[0].Profile.Environment }),
                TestingProducerSha256 = HashFile(typeof(IncrementalTestService).Assembly.Location),
                HelperProducerSha256 = HashFile(typeof(CisProcessJob).Assembly.Location), Phases = results,
                Inputs = pins.OrderBy(pin => pin.Key, StringComparer.Ordinal).Select(pin => new MutationInputIdentity(pin.Key, pin.Value)).ToArray(),
                Diagnostics = ["Exact source-variant native failure verified. Diagnostic only: raw native history, scores, scope and all other holds are unchanged."],
            };
            Require(JsonSerializer.SerializeToUtf8Bytes(result, Json).Length <= 1048576, "Source-variant receipt exceeds 1 MiB; use a smaller proof closure.");
            return result;
        }
        catch (Exception error) when (Expected(error) || error is KeyNotFoundException or FormatException or InvalidOperationException or BadImageFormatException)
        {
            return new("source-variant-verification-failed", 4)
            { Diagnostics = [error is InvalidDataException ? error.Message : "Source-variant evidence could not be verified: " + error.GetType().Name + "."] };
        }
    }

    private static (string Method, SourceVariantNativeObservation Observation) ReadSourceVariantTrx(string path, Guid id, string outcome,
        Input input, string sourceFile, string subjectAssembly)
    {
        var xml = XDocument.Load(path);
        var row = xml.Descendants().Single(node => node.Name.LocalName == "UnitTestResult");
        Require(Guid.TryParse(row.Attribute("testId")?.Value, out var actual) && actual == id && row.Attribute("outcome")?.Value == outcome,
            "Native GUID/outcome does not match the selected source-variant test.");
        var definition = xml.Descendants().Single(node => node.Name.LocalName == "UnitTest" && Guid.TryParse(node.Attribute("id")?.Value, out var test) && test == id);
        var testMethod = definition.Elements().Single(node => node.Name.LocalName == "TestMethod");
        string method = (testMethod.Attribute("className")?.Value.Split(',')[0] ?? "") + "." + (testMethod.Attribute("name")?.Value ?? "");
        Require(!method.StartsWith('.') && !method.EndsWith('.'), "Native method identity is missing.");
        string[] lines = row.Descendants().Where(node => node.Name.LocalName == "StdOut").SelectMany(node => node.Value.Split('\n'))
            .Select(line => line.Trim()).Where(line => line.StartsWith(SourceVariantBodyCapture.Prefix, StringComparison.Ordinal)).ToArray();
        Require(lines is { Length: 1 } && lines[0].Length <= 32768, "A native test needs one bounded loaded-body observation from the capture helper.");
        var bytes = Convert.FromBase64String(lines[0][SourceVariantBodyCapture.Prefix.Length..]);
        using var document = JsonDocument.Parse(bytes);
        var errors = new List<string>(); StrykerJsonResultAdapter.CheckDuplicateProperties(document.RootElement, "$", errors);
        Require(errors.Count == 0, "Duplicate loaded-body observation properties.");
        var observation = JsonSerializer.Deserialize<SourceVariantNativeObservation>(bytes, new JsonSerializerOptions
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Missing native loaded-body observation.");
        Require(observation.Subject is not null && observation.TestBody is not null, "Native loaded-body observations cannot be null.");
        var body = observation.Subject;
        string codeBase = testMethod.Attribute("codeBase")?.Value ?? throw new InvalidDataException("Native test codeBase is missing.");
        string nativeTestPath = Path.GetFullPath(codeBase, Safe(input.Repository, input.Profile.WorkingDirectory, true));
        Require(observation.SchemaVersion == 1 && observation.TestBody.SchemaVersion == 1 && body.SchemaVersion == 1
            && CisPathSafety.IsUnderRoot(input.Repository, nativeTestPath) && PathComparer.Equals(observation.TestBody.AssemblyPath, nativeTestPath)
            && PathComparer.Equals(body.AssemblyPath, Safe(input.Repository, subjectAssembly))
            && body == SourceVariantBodyCapture.Inspect(body.AssemblyPath, sourceFile), "Native loaded-body/PDB provenance is missing or changed.");
        return (method, observation);
    }

    private static string VerifyOriginalVariantTest(JsonElement report, Input input, IncrementalTestBatch batch, Guid id, string method, string canonical, string working)
    {
        Require(report.TryGetProperty("testFiles", out var files), "The original native report has no test identities.");
        int found = 0;
        string? selected = null;
        foreach (var file in files.EnumerateObject())
        {
            var tests = file.Value.GetProperty("tests");
            var definitions = tests.ValueKind == JsonValueKind.Array ? tests.EnumerateArray().ToArray() : tests.EnumerateObject().Select(item => item.Value).ToArray();
            foreach (var test in definitions.Where(test => Guid.TryParse(test.GetProperty("id").GetString(), out var native) && native == id))
            {
                string relative = batch.TestFiles.Single(path => NativePathMatches(file.Name, path, canonical, working));
                string source = File.ReadAllText(Safe(input.Repository, relative));
                Require(file.Value.GetProperty("source").GetString() == source && test.GetProperty("name").GetString() == method,
                    "Original native test identity/source does not match the current selected method.");
                _ = RefinementLocation(test.GetProperty("location"), source);
                found++;
                selected = relative;
            }
        }
        Require(found == 1, "The selected native test is missing or ambiguous in the original report.");
        return selected!;
    }

    private static bool NativePathMatches(string native, string relative, string canonical, string working)
    {
        string portable = native.Replace('\\', '/');
        return PathComparer.Equals(Path.GetFullPath(native.Replace('\\', Path.DirectorySeparatorChar), working), Safe(canonical, relative))
            || portable.EndsWith("/" + relative, StringComparison.Ordinal);
    }
    private static byte[] BoundedSource(string path)
    {
        Require(new FileInfo(path).Length is > 0 and <= 4 * 1024 * 1024, "Source-variant text exceeds its bound.");
        return File.ReadAllBytes(path);
    }
    private static byte[] SourceBytes(string text, bool bom)
    {
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
        return bom ? [0xef, 0xbb, 0xbf, .. bytes] : bytes;
    }
    private static int VariantOffset(string source, int line, int column)
    {
        int offset = 0;
        for (int current = 1; current < line; current++) { offset = source.IndexOf('\n', offset) + 1; Require(offset > 0, "Native source line is invalid."); }
        Require(offset + column - 1 <= source.Length, "Native source column is invalid.");
        return offset + column - 1;
    }
}
