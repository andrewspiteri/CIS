using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private sealed record NativeMutant(string Id, string Status, string Definition, string[] KilledBy);
    private sealed record NativeTest(string File, string Definition);
    private sealed record RefinementReport(Dictionary<string, NativeMutant> Mutants, Dictionary<string, NativeTest> Tests);
    private sealed record NativeSpan(int StartLine, int StartColumn, int EndLine, int EndColumn);

    /// <summary>Attest only exact native Timeout-to-Killed reruns; never execute native work.</summary>
    public IncrementalRefinementResult Refine(IncrementalRefinementOptions options)
    {
        try
        {
            Require(!string.IsNullOrWhiteSpace(options.Reason) && options.Reason.Length <= 2048 && !options.Reason.Contains('\0'),
                "A bounded reason recording review of the rerun filter and mutation ranges is required.");
            var originalOptions = new IncrementalTestOptions(options.RepositoryPath, options.ProfilePath);
            var rerunOptions = new IncrementalTestOptions(options.RepositoryPath, options.RerunProfilePath);
            var original = Read(originalOptions);
            var rerun = Read(rerunOptions);
            CompatibleRefinementProfiles(original, rerun);
            Directory.CreateDirectory(Path.GetDirectoryName(Safe(original.Repository, MutationRunService.LocalRoot + "/run.lock"))!);
            using var lease = new FileStream(Safe(original.Repository, MutationRunService.LocalRoot + "/run.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var result = EvaluateRefinement(options, original, rerun);
            if (!result.RequiresBaseline)
            {
                Require(JsonSerializer.SerializeToUtf8Bytes(result, Json).Length <= 1024 * 1024,
                    "Refinement sidecar exceeds 1 MiB. Use a smaller rerun source subset.");
                Directory.CreateDirectory(Safe(original.Root, "refinements"));
                Write(original, Safe(original.Repository, result.SidecarPath!), result);
            }
            return result;
        }
        catch (Exception error) when (Expected(error) || error is KeyNotFoundException or FormatException)
        {
            return new("refinement-failed", 4) { Diagnostics = [error.Message] };
        }
    }

    private IncrementalRefinementResult EvaluateRefinement(IncrementalRefinementOptions options, Input original, Input rerun)
    {
        var originalOptions = new IncrementalTestOptions(original.Repository, Relative(original.Repository, original.ProfilePath));
        var rerunOptions = new IncrementalTestOptions(rerun.Repository, Relative(rerun.Repository, rerun.ProfilePath));
        var state = State(original);
        var rerunState = State(rerun);
        Require(!state.RecoveryRequired && state.ActiveExecutionUntilUtc is null
            && !rerunState.RecoveryRequired && rerunState.ActiveExecutionUntilUtc is null,
            "Both profiles must finish native work and healthy recovery before refinement.");
        var selection = Select(original, state, false);
        var rerunSelection = Select(rerun, rerunState, false);
        Require(selection.Batches.All(batch => batch.UnitCurrent && batch.MutationCurrent)
            && rerunSelection.Batches.All(batch => batch.UnitCurrent && batch.MutationCurrent),
            "Both profiles require completed current unit and native mutation stages with matching input and report hashes.");
        var bindings = new List<IncrementalRefinementBinding>();
        var credits = new List<IncrementalRefinementCredit>();
        foreach (var batch in selection.Batches)
        {
            VerifyRefinementUnit(original, batch, state.Batches[batch.Id].Unit!);
            VerifyRefinementStage(original, batch, state.Batches[batch.Id].Mutation!);
        }
        foreach (var batch in rerunSelection.Batches)
        {
            var prior = selection.Batches.Single(item => PathComparer.Equals(item.SourceFile, batch.SourceFile));
            Require(prior.TestFiles.SequenceEqual(batch.TestFiles, PathComparer)
                && prior.DependencyFiles.SequenceEqual(batch.DependencyFiles, PathComparer),
                "The rerun changed graph-selected test or dependency inventories.");
            var before = state.Batches[prior.Id].Mutation!;
            var after = rerunState.Batches[batch.Id].Mutation!;
            VerifyRefinementUnit(rerun, batch, rerunState.Batches[batch.Id].Unit!);
            Require(after.FinishedUtc >= before.FinishedUtc, "The native rerun predates the original timeout report.");
            VerifyRefinementStage(rerun, batch, after);
            var baseReport = ReadRefinementReport(original, prior, before);
            var rerunReport = ReadRefinementReport(rerun, batch, after);
            foreach (var mutant in rerunReport.Mutants.Values)
            {
                Require(baseReport.Mutants.TryGetValue(mutant.Id, out var old) && old.Definition == mutant.Definition,
                    "Rerun mutant ID or source/operator/replacement/location definition differs: " + batch.SourceFile + "#" + mutant.Id);
                if (old.Status != "Timeout" || mutant.Status != "Killed") continue;
                Require(mutant.KilledBy.Length > 0, "A refined kill requires native killedBy test identities.");
                var tests = new List<string>();
                foreach (var id in mutant.KilledBy)
                {
                    Require(rerunReport.Tests.TryGetValue(id, out var test) && baseReport.Tests.TryGetValue(id, out var originalTest)
                        && test == originalTest, "A killing test is missing, changed or outside the original current test inventory: " + id);
                    tests.Add(test.Definition);
                }
                credits.Add(new(batch.SourceFile, mutant.Id, mutant.Definition, mutant.KilledBy, tests.ToArray()));
            }
            bindings.Add(new(batch.SourceFile, Relative(original.Repository, Safe(original.Root, before.ReportPath)),
                before.ReportSha256, before.Fingerprint, Relative(rerun.Repository, Safe(rerun.Root, after.ReportPath)),
                after.ReportSha256, after.Fingerprint, HashObject(before), HashObject(after)));
        }
        Require(credits.Count > 0, "The rerun contains no verified native Timeout-to-Killed outcomes.");
        var summary = Summarize(original, originalOptions, selection, state, "completed", 0);
        Require(summary.ScopeComplete && summary.CompleteScopeScore is not null,
            "The original declared scope must be current and complete before refinement.");
        var raw = Counts(summary.Killed, summary.Survived, summary.TimedOut, summary.NoCoverage, summary.KnownDenominator);
        var narrow = original.Profile.FullTestFilter != rerun.Profile.FullTestFilter;
        var finished = rerunSelection.Batches.Max(batch => rerunState.Batches[batch.Id].Mutation!.FinishedUtc);
        var needsBaseline = narrow && selection.Batches.Any(batch => state.Batches[batch.Id].Unit!.FinishedUtc < finished);
        var effective = needsBaseline ? raw : Counts(raw.Killed + credits.Count, raw.Survived,
            raw.TimedOut - credits.Count, raw.NoCoverage, raw.Denominator);
        Require(Read(originalOptions).CommonIdentity == original.CommonIdentity && Read(rerunOptions).CommonIdentity == rerun.CommonIdentity
            && Select(original, state, false).Batches.All(batch => batch.UnitCurrent && batch.MutationCurrent)
            && Select(rerun, rerunState, false).Batches.All(batch => batch.UnitCurrent && batch.MutationCurrent),
            "Inputs or reports changed during refinement.");
        var sidecar = "refinements/" + rerun.Profile.Id + ".json";
        var result = new IncrementalRefinementResult(needsBaseline ? "needs-baseline" : "refined", needsBaseline ? 3 : 0)
        {
            ProfileId = original.Profile.Id, RerunProfileId = rerun.Profile.Id,
            OriginalProfileSha256 = original.ProfileHash, RerunProfileSha256 = rerun.ProfileHash,
            OriginalProfilePath = Relative(original.Repository, original.ProfilePath), RerunProfilePath = Relative(rerun.Repository, rerun.ProfilePath),
            EnvironmentSha256 = HashObject(new { inherited = AmbientEnvironmentIdentity(original.Profile, false), declared = original.Profile.Environment }),
            TestingProducerSha256 = HashFile(typeof(IncrementalTestService).Assembly.Location),
            HelperProducerSha256 = HashFile(typeof(CisProcessJob).Assembly.Location),
            Reason = options.Reason, VerifiedUtc = DateTimeOffset.UtcNow, RequiresBaseline = needsBaseline,
            Raw = raw, Effective = effective, Bindings = bindings, Credits = needsBaseline ? [] : credits,
            BaselineBindings = selection.Batches.Select(batch =>
            {
                var unit = state.Batches[batch.Id].Unit!;
                return new IncrementalRefinementBaselineBinding(batch.SourceFile,
                    Relative(original.Repository, Safe(original.Root, unit.ReportPath)), unit.ReportSha256, unit.Fingerprint, unit.FinishedUtc);
            }).ToArray(),
            Inputs = original.Profile.SourceFiles.Concat(original.Profile.TestFiles).Concat(original.Profile.ConfigFiles)
                .Concat(selection.Batches.SelectMany(batch => batch.DependencyFiles)).Select(file => Safe(original.Repository, file))
                .Concat(original.Profile.ToolFiles.Select(file => Path.GetFullPath(file, original.Repository)))
                .Distinct(PathComparer).Order(StringComparer.Ordinal).Select(file => new MutationInputIdentity(
                    CisPathSafety.IsUnderRoot(original.Repository, file) ? Relative(original.Repository, file) : file, HashFile(file))).ToArray(),
            ThresholdMet = !needsBaseline && effective.Score >= original.Profile.BreakThreshold
                && effective.TimedOut == 0 && effective.NoCoverage == 0,
            SidecarPath = needsBaseline ? null : Relative(original.Repository, Safe(original.Root, sidecar)),
            Diagnostics = needsBaseline
                ? ["The rerun narrows the test filter. Run the original profile's complete forced unit layer after the rerun, then refine again. No kill credit was published."]
                : ["Verified native rerun kills are separate from unchanged raw outcomes. Release qualification remains separate."],
        };
        return result;
    }

    private static IncrementalMutationCounts Counts(int killed, int survived, int timeout, int uncovered, int denominator)
        => new(killed, survived, timeout, uncovered, denominator, denominator > 0 ? killed * 100d / denominator : null);

    private static void CompatibleRefinementProfiles(Input original, Input rerun)
    {
        Require(original.Profile.Id != rerun.Profile.Id && original.Profile.MutationCommand is not null && rerun.Profile.MutationCommand is not null,
            "Refinement requires distinct original and rerun mutation profile IDs in the same repository.");
        Require(rerun.Profile.SourceFiles.All(file => original.Profile.SourceFiles.Contains(file, PathComparer)),
            "Rerun source scope is outside the original profile.");
        Require(rerun.Profile.TimeoutSeconds <= original.Profile.TimeoutSeconds,
            "The rerun outer invocation guard must not be longer than the original guard.");
        var expected = original.Profile with { MutationCommand = RefinementCommand(original) };
        var normalized = rerun.Profile with
        {
            Id = original.Profile.Id, SourceFiles = original.Profile.SourceFiles,
            CanarySourceFile = original.Profile.CanarySourceFile, FullTestFilter = original.Profile.FullTestFilter,
            TimeoutSeconds = original.Profile.TimeoutSeconds,
            MutationCommand = RefinementCommand(rerun),
        };
        Require(HashObject(expected) == HashObject(normalized),
            "Only reviewed test-filter narrowing, source subset, canary selection, native mutation ranges and a shorter outer invocation guard may differ between profiles.");
    }

    private static string[] RefinementCommand(Input input)
    {
        var command = input.Profile.MutationCommand!;
        var prefixes = new List<string> { "{sourceFile}" };
        if (input.Profile.SourceFiles.Length == 1)
            prefixes.Add(Relative(Safe(input.Repository, input.Profile.WorkingDirectory, true), Safe(input.Repository, input.Profile.SourceFiles[0])));
        var normalized = new List<string>();
        for (var index = 0; index < command.Length; index++)
        {
            if (command[index] is not ("--mutate" or "-m")) { normalized.Add(command[index]); continue; }
            Require(++index < command.Length && prefixes.Any(prefix => Regex.IsMatch(command[index],
                "\\A" + Regex.Escape(prefix) + @"(?:\{\d+\.\.\d+(?:,\d+\.\.\d+)*\})*\z", RegexOptions.CultureInvariant)),
                "Native mutation overrides must select only {sourceFile} and explicit character ranges.");
            foreach (Match range in Regex.Matches(command[index], @"(\d+)\.\.(\d+)", RegexOptions.CultureInvariant))
                Require(int.TryParse(range.Groups[1].Value, out var start) && int.TryParse(range.Groups[2].Value, out var end)
                    && start <= end && input.Profile.SourceFiles.All(file => end <= File.ReadAllText(Safe(input.Repository, file)).Length),
                    "Native mutation character range is outside the current source text.");
        }
        return normalized.ToArray();
    }

    private void VerifyRefinementStage(Input input, IncrementalTestBatch batch, IncrementalStageResult stage)
    {
        Require(stage.Completed && stage.Status is "completed" or "completed-with-timeouts"
            && stage.Execution is { Status: "exited", ExitCode: 0 or 2, Error: null, OutputTruncated: false }
            && stage.RecoveryExecution is { } recovery && Healthy(recovery)
            && stage.PostflightExecution is { } postflight && Healthy(postflight) && stage.PostflightPassed > 0,
            "Native mutation, recovery and postflight must all have completed healthy executions.");
        var helper = RefinementProducer(input, batch, stage);
        VerifyRefinementCommand(input, batch, stage.Execution!, input.Profile.MutationCommand!, false, helper);
        VerifyRefinementCommand(input, batch, stage.RecoveryExecution!, input.Profile.RecoveryCommand!, true, helper);
        VerifyRefinementCommand(input, batch, stage.PostflightExecution!, input.Profile.UnitCommand, true, helper);
    }

    private void VerifyRefinementUnit(Input input, IncrementalTestBatch batch, IncrementalStageResult stage)
    {
        Require(stage.Completed && stage.Status == "completed" && stage.Execution is { } execution && Healthy(execution),
            "Original and rerun unit stages require completed healthy native execution.");
        var helper = RefinementProducer(input, batch, stage, false);
        VerifyRefinementCommand(input, batch, stage.Execution!, input.Profile.UnitCommand, false, helper);
    }

    private static void VerifyRefinementCommand(Input input, IncrementalTestBatch batch, MutationNativeExecution execution,
        string[] template, bool fullTests, string helper)
    {
        var actual = execution.Command;
        Require(actual is not null && actual.Length == template.Length
            && execution.CisToolAssemblySha256 == helper
            && PathComparer.Equals(execution.ExecutablePath, actual[0]),
            "Native execution command or installed helper identity is missing or changed.");
        string? output = null;
        for (var index = 0; index < template.Length; index++)
            if (template[index] is "{output}" or "{mutationConfig}")
            {
                var candidate = template[index] == "{output}" ? actual[index] : Path.GetDirectoryName(actual[index])!;
                Require(Path.IsPathFullyQualified(candidate) && CisPathSafety.IsUnderRoot(Safe(input.Root, "work"), candidate)
                    && Regex.IsMatch(Path.GetFileName(candidate), @"\A[a-f0-9]{12}\z", RegexOptions.CultureInvariant),
                    "Native output must belong to the profile's bounded work directory.");
                Require(output is null || PathComparer.Equals(output, candidate), "Native output placeholders disagree.");
                output = candidate;
            }
        var working = Safe(input.Repository, input.Profile.WorkingDirectory, true);
        var expanded = template.Select(value => value.Replace("{sourceFile}", Relative(working, Safe(input.Repository, batch.SourceFile)), StringComparison.Ordinal)
            .Replace("{testFilter}", fullTests ? input.Profile.FullTestFilter : batch.TestFilter, StringComparison.Ordinal)
            .Replace("{output}", output ?? "{output}", StringComparison.Ordinal)
            .Replace("{mutationConfig}", output is null ? "{mutationConfig}" : Path.Combine(output, "stryker-config.json"), StringComparison.Ordinal)).ToArray();
        Require(expanded.SequenceEqual(actual, StringComparer.Ordinal), "Native execution does not match its current profile command.");
    }

    private string RefinementProducer(Input input, IncrementalTestBatch batch, IncrementalStageResult stage, bool mutation = true)
    {
        var history = stage.ProducerRevalidations;
        Require(history is { Length: <= 8 }, "Producer revalidation history is missing or exceeds eight links.");
        var testing = HashFile(typeof(IncrementalTestService).Assembly.Location);
        var helper = HashFile(typeof(CisProcessJob).Assembly.Location);
        if (history.Length == 0) return helper;
        var options = new IncrementalTestOptions(input.Repository, Relative(input.Repository, input.ProfilePath));
        IncrementalProducerRevalidation? prior = null;
        foreach (var link in history)
        {
            Require(link is not null, "Producer revalidation link is missing.");
            Require(link.ReportSha256 == stage.ReportSha256 && !string.IsNullOrWhiteSpace(link.Reason) && link.Reason.Length <= 1024
                && !link.Reason.Contains('\0') && link.VerifiedUtc >= stage.FinishedUtc && link.VerifiedUtc <= DateTimeOffset.UtcNow,
                "Producer revalidation does not bind this completed native report and compatibility review.");
            foreach (var digest in new[] { link.PreviousTestingSha256, link.PreviousHelperSha256, link.TestingSha256, link.HelperSha256,
                link.PreviousFingerprint, link.Fingerprint, link.PreviousEnvironmentSha256, link.EnvironmentSha256 })
                Require(digest is not null && Regex.IsMatch(digest, @"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant),
                    "Producer revalidation has a malformed identity digest.");
            foreach (var artifact in new[] { (link.PreviousTestingAssemblyPath, link.PreviousTestingSha256),
                (link.PreviousHelperAssemblyPath, link.PreviousHelperSha256) })
                Require(Path.IsPathFullyQualified(artifact.Item1) && File.Exists(artifact.Item1)
                    && !CisPathSafety.IsUnderRoot(input.Repository, artifact.Item1, allowRoot: true)
                    && !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(artifact.Item1)!, artifact.Item1)
                    && HashFile(artifact.Item1) == artifact.Item2, "The reviewed previous producer binary is missing, linked or changed.");
            Require(PathComparer.Equals(link.PreviousHelperAssemblyPath,
                Path.Combine(Path.GetDirectoryName(link.PreviousTestingAssemblyPath)!, "Cis.Abstractions.dll")),
                "Previous producer binaries do not form the reviewed testing/helper pair.");
            if (prior is not null)
                Require(link.PreviousFingerprint == prior.Fingerprint && link.PreviousTestingSha256 == prior.TestingSha256
                    && link.PreviousHelperSha256 == prior.HelperSha256 && link.VerifiedUtc >= prior.VerifiedUtc,
                    "Producer revalidation chain is disconnected.");
            var oldInput = Read(options, link.PreviousTestingSha256, link.PreviousHelperSha256,
                link.LegacySessionEnvironment, link.PreviousEnvironmentSha256);
            var newInput = Read(options, link.TestingSha256, link.HelperSha256, false, link.EnvironmentSha256);
            var oldBatch = Select(oldInput, new(), false).Batches.Single(item => item.Id == batch.Id);
            var newBatch = Select(newInput, new(), false).Batches.Single(item => item.Id == batch.Id);
            Require((mutation ? oldBatch.MutationFingerprint : oldBatch.UnitFingerprint) == link.PreviousFingerprint
                && (mutation ? newBatch.MutationFingerprint : newBatch.UnitFingerprint) == link.Fingerprint,
                "Producer revalidation fingerprints do not match the unchanged current source and input graph.");
            prior = link;
        }
        Require(prior!.Fingerprint == stage.Fingerprint && prior.TestingSha256 == testing && prior.HelperSha256 == helper
            && prior.EnvironmentSha256 == AmbientEnvironmentIdentity(input.Profile, false),
            "Producer revalidation does not reach the current testing/helper inputs.");
        return history[0].PreviousHelperSha256;
    }

    private static RefinementReport ReadRefinementReport(Input input, IncrementalTestBatch batch, IncrementalStageResult stage)
    {
        var path = Safe(input.Root, stage.ReportPath);
        Require(new FileInfo(path).Length is > 0 and <= 100 * 1024 * 1024, "Native refinement report exceeds its size bound.");
        var bytes = File.ReadAllBytes(path);
        Require(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) == stage.ReportSha256,
            "Native refinement report hash changed.");
        using var document = JsonDocument.Parse(bytes);
        var errors = new List<string>();
        StrykerJsonResultAdapter.CheckDuplicateProperties(document.RootElement, "$", errors);
        Require(errors.Count == 0, "Native refinement report contains duplicate properties.");
        var working = Safe(input.Repository, input.Profile.WorkingDirectory, true);
        var file = document.RootElement.GetProperty("files").EnumerateObject().Single(item =>
            PathComparer.Equals(Path.GetFullPath(item.Name.Replace('\\', Path.DirectorySeparatorChar), working), Safe(input.Repository, batch.SourceFile))).Value;
        Require(file.GetProperty("source").GetString() == File.ReadAllText(Safe(input.Repository, batch.SourceFile)),
            "Native mutation source text is not current.");
        var mutants = new Dictionary<string, NativeMutant>(StringComparer.Ordinal);
        foreach (var mutant in file.GetProperty("mutants").EnumerateArray())
        {
            var id = mutant.GetProperty("id").GetString()!;
            var definition = HashObject(new
            {
                source = file.GetProperty("source").GetString(), id,
                mutatorName = mutant.GetProperty("mutatorName").GetString(), replacement = mutant.GetProperty("replacement").GetString(),
                location = RefinementLocation(mutant.GetProperty("location"), file.GetProperty("source").GetString()!),
            });
            var killedBy = mutant.TryGetProperty("killedBy", out var ids)
                ? ids.EnumerateArray().Select(item => item.GetString()!).ToArray() : [];
            Require(killedBy.Length <= 4096 && killedBy.All(id => !string.IsNullOrWhiteSpace(id))
                && killedBy.Distinct(StringComparer.Ordinal).Count() == killedBy.Length, "Native killing test IDs are malformed or duplicated.");
            Require(mutants.TryAdd(id, new(id, mutant.GetProperty("status").GetString()!, definition, killedBy)), "Native mutant IDs are duplicated.");
        }
        var tests = new Dictionary<string, NativeTest>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("testFiles", out var testFiles))
        {
            foreach (var testFile in testFiles.EnumerateObject())
            {
                var full = Path.GetFullPath(testFile.Name.Replace('\\', Path.DirectorySeparatorChar), working);
                Require(CisPathSafety.IsUnderRoot(input.Repository, full) && !CisPathSafety.ContainsReparsePoint(input.Repository, full),
                    "Native test source path leaves the repository or crosses a linked path.");
                var relative = Relative(input.Repository, full);
                // A native report may include helper files with no test definitions.
                var definitions = testFile.Value.GetProperty("tests");
                var entries = definitions.ValueKind == JsonValueKind.Array ? definitions.EnumerateArray().ToArray()
                    : definitions.EnumerateObject().Select(item => item.Value).ToArray();
                if (entries.Length == 0) continue;
                if (!batch.TestFiles.Contains(relative, PathComparer)) continue;
                Require(File.Exists(full) && testFile.Value.GetProperty("source").GetString() == File.ReadAllText(full),
                    "Native killing-test source is outside the current declared test inventory or its text changed.");
                foreach (var test in entries)
                {
                    var id = test.GetProperty("id").GetString()!;
                    Require(!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(test.GetProperty("name").GetString()),
                        "Native test identity or name is missing.");
                    var location = RefinementLocation(test.GetProperty("location"), testFile.Value.GetProperty("source").GetString()!);
                    Require(tests.TryAdd(id, new(relative, HashObject(new { file = relative,
                        source = testFile.Value.GetProperty("source").GetString(), name = test.GetProperty("name").GetString(), location }))),
                        "Native test IDs are duplicated.");
                }
            }
        }
        return new(mutants, tests);
    }

    private static NativeSpan RefinementLocation(JsonElement location, string source)
    {
        var start = location.GetProperty("start");
        var end = location.GetProperty("end");
        var span = new NativeSpan(start.GetProperty("line").GetInt32(), start.GetProperty("column").GetInt32(),
            end.GetProperty("line").GetInt32(), end.GetProperty("column").GetInt32());
        var lines = source.Split('\n');
        Require(span.StartLine >= 1 && span.EndLine >= span.StartLine && span.EndLine <= lines.Length
            && span.StartColumn >= 1 && span.EndColumn >= 1
            && span.StartColumn <= lines[span.StartLine - 1].Length + 1 && span.EndColumn <= lines[span.EndLine - 1].Length + 1
            && (span.EndLine > span.StartLine || span.EndColumn >= span.StartColumn), "Native source location is outside its current source text.");
        return span;
    }
}
