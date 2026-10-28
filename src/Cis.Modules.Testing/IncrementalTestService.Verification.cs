using System.Runtime.InteropServices;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    /// <summary>Recheck current native evidence and return a bounded receipt without executing or saving work.</summary>
    public IncrementalEvidenceResult Verify(IncrementalEvidenceOptions options)
    {
        try
        {
            Require(options.RefinementProfilePaths is null or { Length: <= 32 }, "At most 32 explicit rerun profiles are supported.");
            var runOptions = new IncrementalTestOptions(options.RepositoryPath, options.ProfilePath);
            var input = Read(runOptions);
            var leasePath = Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock");
            // Existing native invocations use this lease. Verification never creates a cache or lock file.
            using var lease = File.Exists(leasePath) ? new FileStream(leasePath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
            var state = State(input);
            Require(!state.RecoveryRequired && state.ActiveExecutionUntilUtc is null,
                "Native work or baseline recovery remains active.");
            var selection = Select(input, state, false);
            Require(selection.Batches.All(batch => batch.UnitCurrent && (input.Profile.MutationCommand is null || batch.MutationCurrent)),
                "Every declared source requires completed current unit and configured mutation evidence.");
            var sources = new List<(Input Input, IncrementalTestState State, Selection Selection, string StateHash)>
                { (input, state, selection, HashFile(input.StatePath)) };
            var measured = new Dictionary<string, TestMutationSummary>(PathComparer);
            foreach (var batch in selection.Batches)
            {
                var saved = state.Batches[batch.Id];
                VerifyRefinementUnit(input, batch, saved.Unit!);
                if (input.Profile.MutationCommand is null) continue;
                VerifyFullFile(input, batch);
                VerifyRefinementStage(input, batch, saved.Mutation!);
                var native = ReadRefinementReport(input, batch, saved.Mutation!);
                VerifyKilledDefinitions(native);
                measured.Add(batch.SourceFile, ReadMutation(input, batch.SourceFile, Safe(input.Root, saved.Mutation!.ReportPath)).Mutation!);
            }
            if (input.Profile.MutationCommand is not null)
            {
                Require(measured.Single(pair => PathComparer.Equals(Safe(input.Repository, pair.Key),
                    Safe(input.Repository, input.Profile.CanarySourceFile!))).Value.Killed >= input.Profile.MinimumCanaryKills,
                    "The original native canary does not contain the required kills.");
                Require(measured.Values.Sum(item => item.Denominator) > 0,
                    "The declared mutation scope contains no eligible mutants.");
            }
            var refinements = new List<IncrementalEvidenceRefinement>();
            var credits = new Dictionary<string, IncrementalRefinementCredit>(StringComparer.Ordinal);
            var profilePaths = new HashSet<string>(PathComparer) { input.ProfilePath };
            foreach (var path in options.RefinementProfilePaths ?? [])
            {
                var rerun = Read(new(input.Repository, path));
                Require(profilePaths.Add(rerun.ProfilePath), "Rerun profiles must be distinct and must not repeat the original profile.");
                CompatibleRefinementProfiles(input, rerun);
                var result = EvaluateRefinement(new(input.Repository, options.ProfilePath, path,
                    "Read-only verification of explicit native rerun profile."), input, rerun);
                Require(!result.RequiresBaseline, "The narrowed rerun still requires a fresh original-profile unit baseline.");
                var rerunState = State(rerun);
                var rerunSelection = Select(rerun, rerunState, false);
                foreach (var batch in rerunSelection.Batches)
                    VerifyKilledDefinitions(ReadRefinementReport(rerun, batch, rerunState.Batches[batch.Id].Mutation!));
                sources.Add((rerun, rerunState, rerunSelection, HashFile(rerun.StatePath)));
                refinements.Add(new(Relative(input.Repository, rerun.ProfilePath), rerun.ProfileHash, result.Bindings, result.BaselineBindings)
                {
                    RerunReports = rerunSelection.Batches.SelectMany(batch => new[]
                    {
                        EvidenceReportPin(rerun, batch, rerunState.Batches[batch.Id].Unit!, "unit"),
                        EvidenceReportPin(rerun, batch, rerunState.Batches[batch.Id].Mutation!, "mutation"),
                    }).ToArray(),
                });
                foreach (var credit in result.Credits)
                {
                    var key = credit.SourceFile + "\0" + credit.MutantId;
                    if (credits.TryGetValue(key, out var existing))
                        Require(existing.DefinitionSha256 == credit.DefinitionSha256,
                            "Reruns disagree on a credited native mutant definition.");
                    else credits.Add(key, credit);
                }
            }

            var inputFiles = new Dictionary<string, HashSet<string>>(PathComparer);
            foreach (var source in sources)
            {
                AddInput(source.Input.ProfilePath, "profile");
                foreach (var file in source.Input.Profile.SourceFiles) AddInput(Safe(input.Repository, file), "source");
                foreach (var file in source.Input.Profile.TestFiles) AddInput(Safe(input.Repository, file), "test");
                foreach (var file in source.Input.Profile.ConfigFiles) AddInput(Safe(input.Repository, file), "configuration");
                foreach (var file in source.Input.Profile.ToolFiles) AddInput(Path.GetFullPath(file, input.Repository), "tool");
                foreach (var batch in source.Selection.Batches)
                {
                    foreach (var file in batch.TestFiles) AddInput(Safe(input.Repository, file), "test");
                    foreach (var file in batch.DependencyFiles) AddInput(Safe(input.Repository, file), "dependency");
                    var saved = source.State.Batches[batch.Id];
                    var stages = source.Input.Profile.MutationCommand is null ? new[] { saved.Unit } : new[] { saved.Unit, saved.Mutation };
                    foreach (var stage in stages.Where(item => item is not null))
                        foreach (var history in stage!.ProducerRevalidations)
                        {
                            AddInput(history.PreviousTestingAssemblyPath, "producer-history");
                            AddInput(history.PreviousHelperAssemblyPath, "producer-history");
                        }
                }
            }
            AddInput(typeof(IncrementalTestService).Assembly.Location, "producer");
            AddInput(typeof(CisProcessJob).Assembly.Location, "producer");
            var ordered = inputFiles.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            var indices = ordered.Select((pair, index) => (pair.Key, index)).ToDictionary(pair => pair.Key, pair => pair.index, PathComparer);
            var inventory = ordered.Select(pair => new IncrementalEvidenceInput(
                CisPathSafety.IsUnderRoot(input.Repository, pair.Key) ? Relative(input.Repository, pair.Key) : pair.Key,
                HashFile(pair.Key), pair.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();
            var closures = new Dictionary<string, IncrementalEvidenceClosure>(StringComparer.Ordinal);
            var batches = new List<IncrementalEvidenceBatch>();
            foreach (var batch in selection.Batches)
            {
                var tests = batch.TestFiles.Select(file => indices[Safe(input.Repository, file)]).Order().ToArray();
                var dependencies = batch.DependencyFiles.Select(file => indices[Safe(input.Repository, file)]).Order().ToArray();
                var closure = new IncrementalEvidenceClosure(HashObject(new { tests, dependencies }), tests, dependencies);
                closures.TryAdd(closure.Id, closure);
                var saved = state.Batches[batch.Id];
                var unit = saved.Unit!;
                var mutation = input.Profile.MutationCommand is null ? null : saved.Mutation!;
                var unitPath = Safe(input.Root, unit.ReportPath);
                var mutationPath = mutation is null ? null : Safe(input.Root, mutation.ReportPath);
                var executions = new List<IncrementalEvidenceExecution> { EvidenceExecution("unit", unit.Execution!) };
                if (mutation is not null)
                {
                    executions.Add(EvidenceExecution("mutation", mutation.Execution!));
                    executions.Add(EvidenceExecution("recovery", mutation.RecoveryExecution!));
                    executions.Add(EvidenceExecution("postflight", mutation.PostflightExecution!));
                }
                var counts = measured.GetValueOrDefault(batch.SourceFile);
                batches.Add(new(batch.SourceFile, closure.Id, batch.UnitFingerprint, batch.MutationFingerprint, HashObject(batch.TestFilter), mutation is not null,
                    Relative(input.Repository, unitPath), unit.ReportSha256, new FileInfo(unitPath).Length, HashObject(unit), unit.FinishedUtc, unit.Passed,
                    mutationPath is null ? null : Relative(input.Repository, mutationPath), mutation?.ReportSha256,
                    mutationPath is null ? null : new FileInfo(mutationPath).Length, mutation is null ? null : HashObject(mutation),
                    counts is null ? null : Counts(counts.Killed, counts.Survived, counts.TimedOut, counts.NoCoverage, counts.Denominator),
                    executions.ToArray()) { FullPostflightReceiptVerified = mutation is not null });
            }
            var raw = input.Profile.MutationCommand is null ? null : Counts(measured.Values.Sum(item => item.Killed),
                measured.Values.Sum(item => item.Survived), measured.Values.Sum(item => item.TimedOut),
                measured.Values.Sum(item => item.NoCoverage), measured.Values.Sum(item => item.Denominator));
            var effective = raw is null ? null : Counts(raw.Killed + credits.Count, raw.Survived,
                raw.TimedOut - credits.Count, raw.NoCoverage, raw.Denominator);

            // Reread mutable source, graph selection, cache state and immutable reports before publishing a receipt.
            foreach (var source in sources)
            {
                var fresh = Read(new(input.Repository, Relative(input.Repository, source.Input.ProfilePath)));
                Require(fresh.CommonIdentity == source.Input.CommonIdentity && HashFile(fresh.StatePath) == source.StateHash,
                    "Inputs or cache state changed during verification.");
                var current = Select(fresh, State(fresh), false);
                Require(current.Batches.All(batch => batch.UnitCurrent && (fresh.Profile.MutationCommand is null || batch.MutationCurrent))
                    && HashObject(current.Batches) == HashObject(source.Selection.Batches),
                    "Graph selection, fingerprints or reports changed during verification.");
            }
            for (var index = 0; index < ordered.Length; index++)
                Require(HashFile(ordered[index].Key) == inventory[index].Sha256, "An input changed during verification.");
            var receipt = new IncrementalEvidenceResult("verified", 0)
            {
                ProfileId = input.Profile.Id, ProfilePath = Relative(input.Repository, input.ProfilePath), ProfileSha256 = input.ProfileHash,
                GraphBuildId = selection.Snapshot.Graph!.Build.Id, RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                OsDescription = RuntimeInformation.OSDescription,
                EnvironmentSha256 = HashObject(new { inherited = AmbientEnvironmentIdentity(input.Profile, false), declared = input.Profile.Environment }),
                TestingProducerSha256 = HashFile(typeof(IncrementalTestService).Assembly.Location),
                HelperProducerSha256 = HashFile(typeof(CisProcessJob).Assembly.Location), VerifiedUtc = DateTimeOffset.UtcNow,
                ScopeComplete = true, Raw = raw, Effective = effective,
                ThresholdMet = effective is not null && effective.Score >= input.Profile.BreakThreshold && effective.TimedOut == 0 && effective.NoCoverage == 0,
                Inputs = inventory, Closures = closures.Values.ToArray(), Batches = batches, Refinements = refinements, Credits = credits.Values.ToArray(),
                Diagnostics = [input.Profile.MutationCommand is null ? "Current declared unit scope verified. Release qualification remains separate."
                    : "Current declared-scope evidence verified. Historical postflight XML is not retained; full-filter native postflight receipts were checked. Release qualification remains separate."],
            };
            Require(JsonSerializer.SerializeToUtf8Bytes(receipt, Json).Length <= 1024 * 1024,
                "Verification receipt exceeds 1 MiB. Use smaller declared source profiles.");
            return receipt;

            void AddInput(string file, string role)
            {
                file = Path.GetFullPath(file);
                if (!inputFiles.TryGetValue(file, out var roles)) inputFiles.Add(file, roles = new(StringComparer.Ordinal));
                roles.Add(role);
            }
        }
        catch (Exception error) when (Expected(error) || error is KeyNotFoundException or FormatException)
        {
            // Do not echo native command arguments, profile environment values or report contents into exported diagnostics.
            return new("verification-failed", 4) { Diagnostics = [VerificationDiagnostic(error)] };
        }
    }

    private static string VerificationDiagnostic(Exception error)
        => error is InvalidDataException ? error.Message : "Evidence could not be verified: " + error.GetType().Name + ".";

    private static IncrementalEvidenceExecution EvidenceExecution(string role, MutationNativeExecution execution)
        => new(role, execution.Status, execution.ExitCode, execution.DurationMilliseconds, HashObject(execution.Command),
            execution.CisToolAssemblySha256!, execution.OutputTruncated, execution.Error is not null);

    private static IncrementalEvidenceReportPin EvidenceReportPin(Input input, IncrementalTestBatch batch,
        IncrementalStageResult stage, string role)
    {
        var path = Safe(input.Root, stage.ReportPath);
        var executions = new List<IncrementalEvidenceExecution> { EvidenceExecution(role, stage.Execution!) };
        if (role == "mutation")
        {
            executions.Add(EvidenceExecution("recovery", stage.RecoveryExecution!));
            executions.Add(EvidenceExecution("postflight", stage.PostflightExecution!));
        }
        return new(batch.SourceFile, role, Relative(input.Repository, path), stage.ReportSha256,
            new FileInfo(path).Length, stage.Fingerprint, HashObject(stage), stage.FinishedUtc, executions.ToArray());
    }

    private static void VerifyKilledDefinitions(RefinementReport report)
    {
        foreach (var mutant in report.Mutants.Values.Where(mutant => mutant.Status == "Killed"))
        {
            Require(mutant.KilledBy.Length > 0, "Every native Killed outcome requires killing-test identities.");
            foreach (var id in mutant.KilledBy)
                Require(report.Tests.ContainsKey(id), "A native killing test is missing or outside the current test inventory.");
        }
    }

    private static void VerifyFullFile(Input input, IncrementalTestBatch batch)
    {
        var command = input.Profile.MutationCommand!;
        var relative = Relative(Safe(input.Repository, input.Profile.WorkingDirectory, true), Safe(input.Repository, batch.SourceFile));
        Require(input.Profile.StrykerConfigFile is not null || command.Contains("{sourceFile}", StringComparer.Ordinal),
            "Original native mutation must receive a complete source file or a CIS-rendered per-file configuration.");
        string? level = null;
        var configurations = 0;
        if (input.Profile.StrykerConfigFile is not null)
        {
            var template = ReadJson<JsonElement>(Safe(input.Repository, input.Profile.StrykerConfigFile), 262144).GetProperty("stryker-config");
            if (template.TryGetProperty("mutation-level", out var configured)) level = configured.GetString();
            foreach (var name in new[] { "since", "baseline" })
                if (template.TryGetProperty(name, out var partial))
                    Require(partial.ValueKind == JsonValueKind.Object && partial.TryGetProperty("enabled", out var enabled)
                        && enabled.ValueKind == JsonValueKind.False, "Native incremental configuration cannot establish full-file original evidence.");
        }
        else
        {
            var working = Safe(input.Repository, input.Profile.WorkingDirectory, true);
            foreach (var name in new[] { "stryker-config.json", "stryker-config.yaml", "stryker-config.yml" })
                Require(!File.Exists(Path.Combine(working, name)),
                    "Implicit native configuration is unsupported for full-file verification. Use a declared CIS-rendered template.");
        }
        foreach (var argument in command)
            Require(!argument.Contains("{sourceFile}{", StringComparison.Ordinal), "Original mutation evidence must cover a full source file, without native ranges.");
        for (var index = 0; index < command.Length; index++)
        {
            var option = command[index];
            if (option is "--config-file" or "-f")
            {
                Require(input.Profile.StrykerConfigFile is not null && ++index < command.Length && command[index] == "{mutationConfig}",
                    "Original native configuration must use the declared CIS-rendered template.");
                configurations++;
                continue;
            }
            if (option.StartsWith("--config-file=", StringComparison.Ordinal) || option.StartsWith("--config-file:", StringComparison.Ordinal))
            {
                Require(input.Profile.StrykerConfigFile is not null && option[14..] == "{mutationConfig}",
                    "Original native configuration must use the declared CIS-rendered template.");
                configurations++;
                continue;
            }
            Require(!(option.StartsWith("-f", StringComparison.Ordinal) && !option.StartsWith("--", StringComparison.Ordinal) && option.Length > 2),
                "Use an explicit --config-file argument for original native configuration.");
            Require(option is not ("--since" or "--with-baseline") && !option.StartsWith("--since=", StringComparison.Ordinal)
                && !option.StartsWith("--with-baseline=", StringComparison.Ordinal) && !option.StartsWith("--since:", StringComparison.Ordinal)
                && !option.StartsWith("--with-baseline:", StringComparison.Ordinal), "Native incremental filtering cannot establish full-file original evidence.");
            Require(!option.StartsWith("--mutate=", StringComparison.Ordinal) && !option.StartsWith("--mutate:", StringComparison.Ordinal)
                && !(option.StartsWith("-m", StringComparison.Ordinal) && !option.StartsWith("--", StringComparison.Ordinal) && option.Length > 2),
                "Use an explicit full-source --mutate argument for original evidence.");
            if (option is "--mutation-level" or "-l")
            {
                Require(++index < command.Length, "Native mutation level is missing.");
                level = command[index];
                continue;
            }
            if (option.StartsWith("--mutation-level=", StringComparison.Ordinal))
                level = option["--mutation-level=".Length..];
            if (option.StartsWith("--mutation-level:", StringComparison.Ordinal))
                level = option["--mutation-level:".Length..];
            if (option.StartsWith("-l", StringComparison.Ordinal) && !option.StartsWith("--", StringComparison.Ordinal) && option.Length > 2)
                level = option[2..].TrimStart('=', ':');
            if (option is not ("--mutate" or "-m")) continue;
            Require(++index < command.Length && (command[index] == "{sourceFile}" || command[index] == relative),
                "Original native mutation overrides must select the complete current source file.");
        }
        Require(input.Profile.StrykerConfigFile is null || configurations == 1,
            "Original native evidence requires exactly one declared CIS-rendered configuration selector.");
        Require(level == "Complete", "Original native mutation evidence requires an explicit Complete mutation level.");
    }
}
