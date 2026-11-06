using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    public const string LocalRoot = ".cis/local/testing/mutation";
    private readonly ICisNativeWorkController? _nativeWork;
    private readonly ICisOwnedStorageController? _ownedStorage;

    public MutationRunService(ICisNativeWorkController? nativeWork = null, ICisOwnedStorageController? ownedStorage = null)
        => (_nativeWork, _ownedStorage) = (nativeWork, ownedStorage);

    public MutationRunResult Run(MutationRunOptions options) => Run(options, CancellationToken.None);

    public MutationRunResult Run(MutationRunOptions options, CancellationToken cancellationToken)
    {
        string repository;
        (MutationRunProfile Profile, string Path, string Hash) input;
        try
        {
            repository = Path.GetFullPath(options.RepositoryPath);
            Require(options.MaximumSeconds is >= 1 and <= 3600, "Outer native work ceiling must be 1-3600 seconds.");
            input = ReadProfile(repository, options.ProfilePath, options.Kind);
        }
        catch (Exception error) when (Expected(error))
        { return new("invalid-profile", 2, options.Kind) { Diagnostics = [error.Message] }; }
        NativePilotWorkContext? pilotContext;
        try
        {
            pilotContext = input.Profile.CorrectnessPilot is null ? null : CorrectnessPilotWorkContext(repository, input.Profile, input.Path,
            options.Work?.RecoveryOfExecutionId);
        }
        catch (Exception error) when (Expected(error))
        { return new("held", 4, options.Kind) { ProfileId = input.Profile.Id, Diagnostics = [error.Message] }; }
        var recoveryOf = options.Work?.RecoveryOfExecutionId ?? pilotContext?.RecoveryOfExecutionId;
        var restorationOnly = recoveryOf is not null || pilotContext?.RestorationRequired == true;
        var association = options.Work ?? pilotContext?.Association;
        if (pilotContext?.Association is { } retained && options.Work is { } explicitAssociation)
            association = explicitAssociation with
            {
                ChangeId = explicitAssociation.ChangeId ?? retained.ChangeId,
                TaskId = explicitAssociation.TaskId ?? retained.TaskId,
                AuthorityPath = explicitAssociation.AuthorityPath ?? retained.AuthorityPath
            };
        var admission = AdmitNative(_nativeWork, new(repository, "native/mutation/" + Guid.NewGuid().ToString("N"),
            "mutation-" + options.Kind, options.MaximumSeconds, association)
        { RecoveryBasis = pilotContext?.RecoveryBasis, RecoveryOfExecutionId = recoveryOf });
        if (!admission.Allowed)
            return new("held", 4, options.Kind)
            {
                ProfileId = input.Profile.Id,
                Diagnostics = admission.Errors,
                WorkCoverage = admission.Coverage,
                WorkReference = admission.Reference,
                WorkControl = admission.Control
            };
        using var work = admission.Scope;
        MutationRunResult Complete(MutationRunResult result) => result with
        {
            WorkReference = admission.Reference,
            WorkCoverage = admission.Coverage,
            WorkControl = work?.Finish(result.Pilot?.RecoveryRequired == true
                    || pilotContext is not null && result.Pilot?.Restored != true ? "RecoveryRequired"
                    : recoveryOf is not null && result.Pilot?.Restored == true ? "Restored" : "native-finished", "Native mutation " + result.Kind + ": " + result.Status,
                result.SummaryPath is { } summary ? [summary] : []) ?? admission.Control
        };
        try
        {
            Require(restorationOnly || !admission.Required || options.Kind != "canary" || input.Profile.CanaryCommand is { Length: > 0 },
                "Governed canary execution requires a separate narrow canaryCommand; the broad mutation command cannot supply this prerequisite.");
            NativeCorrectnessPilotResult? pilot = null;
            if (!restorationOnly && options.Kind != "canary" && (options.Kind == "qualification" || admission.Required || input.Profile.CorrectnessPilotProfile is not null))
            {
                pilot = InspectMutationCorrectnessPilot(repository, input.Profile, admission, _nativeWork);
                if (!pilot.Ready || admission.Required && !pilot.ControllerAssessed)
                    return Complete(new("pilot-required", 4, options.Kind)
                    {
                        ProfileId = input.Profile.Id,
                        Pilot = pilot,
                        Diagnostics = pilot.Diagnostics.Append(pilot.NextAction).ToArray()
                    });
            }
            var storage = Resolve(repository, LocalRoot);
            Directory.CreateDirectory(storage);
            // Serialize publication and aggregate-budget decisions across profiles/processes.
            using var lease = new FileStream(Resolve(repository, LocalRoot + "/run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (restorationOnly)
            {
                Require(input.Profile.CorrectnessPilot is not null,
                    "This mutation profile has no pinned restoration recipe. Use the original representative profile for recovery.");
                var restored = RunCorrectnessPilot(repository, input.Profile, input.Path, work, cancellationToken,
                    _ownedStorage, _nativeWork, restorationOnly: true, recoveryOfExecutionId: recoveryOf);
                return Complete(new(restored.Restored ? "restored" : "recovery-failed", restored.Restored ? 0 : 4, options.Kind)
                { ProfileId = input.Profile.Id, Pilot = restored, Diagnostics = restored.Diagnostics, Qualified = false });
            }
            var identity = Identity(repository, input.Profile, input.Path);
            Require(identity.Inputs.Single(item => string.Equals(Resolve(repository, item.Path), input.Path, CisPathSafety.PlatformComparison)).Sha256 == input.Hash,
                "Mutation profile changed before execution.");
            MutationRunResult? canary = null;
            if (options.Kind == "qualification")
            {
                canary = Attempt(repository, input.Profile, input.Path, input.Hash, identity, "canary", storage,
                    work, _ownedStorage, cancellationToken);
                if (canary.ExitCode != 0)
                    return Complete(BlockedByCanary(repository, input.Profile, input.Hash, identity, canary, work, _ownedStorage));
            }
            var result = Attempt(repository, input.Profile, input.Path, input.Hash, identity, options.Kind, storage,
                work, _ownedStorage, cancellationToken, canary);
            if (options.Kind == "canary" && input.Profile.CorrectnessPilot is not null && result.ExitCode == 0)
            {
                pilot = RunCorrectnessPilot(repository, input.Profile, input.Path, work, cancellationToken, _ownedStorage, _nativeWork);
                result = result with { Pilot = pilot };
                if (!pilot.Ready) result = FailedResult(result, pilot.NextAction);
            }
            else if (options.Kind == "canary" && input.Profile.CorrectnessPilot is not null)
            {
                var summary = PilotSummaryPath(repository, input.Profile);
                pilot = new("canary-failed", false, null,
                    "Inspect the failed narrow canary, then restore the unchanged pinned baseline recipe before another pilot."
                        + (admission.Reference is null ? " Resume this same profile for restoration only."
                            : " Retain original execution '" + admission.Reference.ExecutionId + "'; use --recovery-of with this same profile if its protected local record is unavailable."))
                {
                    ProfilePath = Path.GetRelativePath(repository, input.Path).Replace('\\', '/'),
                    SummaryPath = Path.GetRelativePath(repository, summary).Replace('\\', '/'),
                    RecoveryRequired = true,
                    RecoveryBasis = pilotContext!.RecoveryBasis,
                    WorkReference = admission.Reference,
                    AuthorityPath = admission.AuthorityPath,
                    StartedUtc = result.StartedUtc,
                    FinishedUtc = result.FinishedUtc,
                    Diagnostics = result.Diagnostics,
                };
                OwnedJson(summary, pilot, repository, work, _ownedStorage);
                result = result with { Pilot = pilot };
            }
            else result = result with
            {
                Pilot = pilot ?? new NativeCorrectnessPilotResult("legacy-unaccounted", false, null,
                "This non-adopted diagnostic does not supply governed representative-runner readiness.")
            };
            return Complete(result);
        }
        catch (Exception error) when (Expected(error))
        {
            return Complete(new("failed", 4, options.Kind)
            { ProfileId = input.Profile.Id, ProfileSha256 = input.Hash, Diagnostics = [error.Message] });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Complete(new("cancelled", 4, options.Kind)
            {
                ProfileId = input.Profile.Id,
                Diagnostics = ["Native work was cancelled; owned-process and restoration holds remain independent."]
            });
        }
    }

    internal static CisNativeWorkAdmission AdmitNative(ICisNativeWorkController? controller, CisNativeWorkRequest request)
        => controller?.Admit(request) ?? (CisNativeWorkEnvironment.RequiresController(request.RepositoryPath, request.Association)
            ? new(true, "held", "missing-work-controller", null, null,
                ["Native work needs the installed composed work controller. Use the CIS CLI with its authority module and an explicit --change/--task association when ownership is ambiguous."])
            : new(false, "unaccounted", "unaccounted-non-adopted", null, null, []));

    private static MutationRunResult Attempt(string repository, MutationRunProfile profile, string profilePath,
        string profileHash, MutationSourceIdentity identity, string kind, string storage,
        ICisNativeWorkScope? parent = null, ICisOwnedStorageController? ownedStorage = null,
        CancellationToken cancellationToken = default, MutationRunResult? canary = null)
    {
        var started = DateTimeOffset.UtcNow;
        var runId = kind + "-" + started.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        var admission = parent?.AdmitChild("native/mutation/" + runId, "mutation-" + kind,
            Math.Min(3600, Math.Max(1, (int)Math.Floor(parent.Remaining.TotalSeconds))));
        if (admission is { Allowed: false })
            return new("held", 4, kind)
            {
                ProfileId = profile.Id,
                RunId = runId,
                Diagnostics = admission.Errors,
                WorkReference = admission.Reference,
                WorkCoverage = admission.Coverage,
                WorkControl = admission.Control
            };
        using var work = admission?.Scope;
        var relativeRoot = LocalRoot + "/" + profile.Id + "/runs/" + runId;
        var output = Resolve(repository, relativeRoot);
        Require(!Directory.Exists(output) && !File.Exists(output), "Mutation run output must be fresh.");
        var previousSummary = PreviousMutationSummary(repository, profile.Id);
        using var storageReservation = BeginNativeStorage(repository, output, storage, profile, work, ownedStorage);
        profile = StorageLimits(profile, storageReservation);
        Directory.CreateDirectory(output);
        WriteLatest(repository, new MutationRunResult("running", 4, kind)
        {
            ProfileId = profile.Id,
            RunId = runId,
            Partial = kind != "qualification",
            SummaryPath = relativeRoot + "/summary.json",
            StartedUtc = started,
        }, work, ownedStorage, storageReservation);
        var diagnostics = new List<string>();
        MutationNativeExecution? execution = null;
        MutationNativeExecution? healthCheck = null;
        TestSuiteExecution? assessment = null;
        string? retainedReport = null;
        string? publishedReport = null;
        var unchanged = false;
        var scopeMatched = false;
        var successful = false;
        var enteredNative = false;
        var enteredHealth = false;
        try
        {
            enteredNative = true;
            execution = Execute(repository, output, storage, profile, kind == "canary" ? profile.CanaryCommand ?? profile.Command : profile.Command,
                work, cancellationToken, ownedStorage, storageReservation);
            Require(execution.SafelyStopped,
                "Owned native work has not been proven stopped; its output and reservation remain held before report admission or cleanup.");
            if (execution.Error is not null) diagnostics.Add(execution.Error);
            try { unchanged = Identity(repository, profile, profilePath).Digest == identity.Digest; }
            catch (Exception error) when (Expected(error)) { diagnostics.Add("Declared input identity could not be rechecked: " + error.Message); }
            if (!unchanged) diagnostics.Add("Declared source/config/profile inputs changed during execution.");
            var report = Resolve(output, profile.ReportPath);
            Require(new FileInfo(report).Length is > 0 and <= 100L * 1024 * 1024, "Native report must contain between 1 byte and 100 MiB.");
            retainedReport = Resolve(output, "native-result.json");
            if (!string.Equals(report, retainedReport, CisPathSafety.PlatformComparison)) File.Move(report, retainedReport, overwrite: false);
            var suite = new TestSuiteProfile(profile.Id, profile.Id, "mutation", "native", "run", profile.WorkingDirectory,
                "stryker-json", Path.GetRelativePath(repository, retainedReport).Replace('\\', '/'), "-", "-", "-", "always", "local", "transient");
            assessment = new StrykerJsonResultAdapter().Read(new(repository, suite, retainedReport, null, null, profile.BreakThreshold));
            if (profile.HealthCheckCommand is not null && execution.Status == "exited")
            {
                var healthAdmission = work?.AdmitChild("native/mutation/health/" + Guid.NewGuid().ToString("N"), "postflight", Math.Min(3600, profile.TimeoutSeconds + 15));
                if (healthAdmission is { Allowed: false }) throw new InvalidDataException(string.Join(" ", healthAdmission.Errors));
                using var healthWork = healthAdmission?.Scope;
                enteredHealth = true;
                healthCheck = Execute(repository, Resolve(output, "health"), storage, profile, profile.HealthCheckCommand,
                    healthWork, cancellationToken, ownedStorage, storageReservation);
                Require(healthCheck.SafelyStopped,
                    "Owned postflight work has not been proven stopped; its output and reservation remain held before publication or cleanup.");
                healthWork?.Finish("native-finished", "Mutation postflight completed: " + healthCheck.Status, []);
            }
            // Recheck after the optional hook as it is also target-owned execution.
            if (unchanged)
            {
                try { unchanged = Identity(repository, profile, profilePath).Digest == identity.Digest; }
                catch (Exception error) when (Expected(error))
                { unchanged = false; diagnostics.Add("Declared input identity could not be rechecked after postflight: " + error.Message); }
            }
            if (!unchanged && !diagnostics.Any(message => message.Contains("inputs changed", StringComparison.Ordinal)))
                diagnostics.Add("Declared source/config/profile inputs changed during execution or postflight.");
            scopeMatched = assessment.Mutation?.ValidEvidence == true && MatchScope(repository, profile, retainedReport, kind == "qualification", diagnostics);
            var mutation = assessment.Mutation!;
            var nativeHealthy = execution.Status == "exited" && execution.Error is null
                && execution.ExitCode is 0 or 2;
            if (!nativeHealthy) diagnostics.Add($"Native execution did not complete successfully: {execution.Status}, exit={execution.ExitCode}.");
            var gate = kind switch
            {
                "canary" => mutation.ValidEvidence && mutation.TimedOut == 0 && mutation.NoCoverage == 0 && mutation.Killed >= profile.MinimumCanaryKills,
                "diagnostic" => mutation.ValidEvidence && mutation.TimedOut == 0,
                _ => mutation.Qualified,
            };
            if (kind == "canary" && !gate) diagnostics.Add("Canary did not produce the required kills with valid, complete native outcomes.");
            var healthPassed = profile.HealthCheckCommand is null || healthCheck is { Status: "exited", ExitCode: 0, Error: null };
            if (!healthPassed) diagnostics.Add("Configured postflight health check did not pass.");
            successful = nativeHealthy && healthPassed && unchanged && scopeMatched && gate;
            var partial = kind != "qualification";
            assessment = assessment with
            {
                Status = successful ? partial ? "partial" : "passed" : "failed",
                FailureKind = successful ? TestFailureKind.None : !unchanged || !scopeMatched ? TestFailureKind.InvalidEvidence
                    : !nativeHealthy || !healthPassed ? TestFailureKind.Infrastructure : assessment.FailureKind,
                Mutation = mutation with
                {
                    Qualified = successful && !partial,
                    Scope = partial ? "partial-reported-mutants" : "declared-profile-source-files",
                    Limitations = ["Qualification covers only the declared report scope; whole-release readiness is not certified."],
                },
            };
            // Retain immutable native bytes only; never retain native build/runtime trees.
            Require(FileHash(retainedReport) == mutation.ReportSha256, "Native report changed after assessment.");
            assessment = Rebind(assessment, repository, retainedReport);
            AtomicJson(retainedReport + ".assessment.json", Sidecar(assessment, profile.Id, runId, kind, false,
                identity with { Unchanged = unchanged }, scopeMatched, execution, profile.HealthCheckCommand is not null, healthCheck, profile.CorrectnessPilotProfile, Path.GetRelativePath(repository, profilePath).Replace('\\', '/')));
            // Clear build/runtime/scratch output before publishing qualification.
            Prune(output, successful: false);
            EnsureBudget(output, storage, profile);
            if (kind != "canary")
            {
                var latest = Resolve(repository, LocalRoot + "/" + profile.Id + "/latest-result.json");
                Publish(retainedReport, latest, assessment, repository, profile.Id, runId, kind, false,
                    identity with { Unchanged = unchanged }, scopeMatched, execution, profile.HealthCheckCommand is not null, healthCheck, profile.CorrectnessPilotProfile, Path.GetRelativePath(repository, profilePath).Replace('\\', '/'));
                publishedReport = latest;
                if (successful) Prune(output, successful: true);
            }
            else if (successful) Prune(output, successful: true);
        }
        catch (Exception error) when (Expected(error)) { successful = false; diagnostics.Add(error.Message); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { successful = false; diagnostics.Add("Native mutation work was cancelled; required cleanup and owned-process holds remain independent."); }

        var safelyStopped = storageReservation?.SafelyStopped
            ?? ((!enteredNative || execution?.SafelyStopped == true) && (!enteredHealth || healthCheck?.SafelyStopped == true));
        if (safelyStopped)
        {
            try { Prune(output, successful); }
            catch (Exception error) when (Expected(error)) { successful = false; diagnostics.Add("Owned run cleanup failed: " + error.Message); }
        }
        if (successful) retainedReport = null;
        else if (retainedReport is not null && !File.Exists(retainedReport)) retainedReport = null;
        var availableReport = publishedReport ?? retainedReport;
        if (assessment is not null)
        {
            assessment = availableReport is null
                ? assessment with { Artifacts = [], Mutation = assessment.Mutation! with { SourcePath = "", Qualified = false } }
                : Rebind(assessment, repository, availableReport);
            assessment = assessment with
            {
                Status = successful ? kind == "qualification" ? "passed" : "partial" : "failed",
                FailureKind = !successful && assessment.FailureKind == TestFailureKind.None ? TestFailureKind.Infrastructure : assessment.FailureKind,
                Mutation = assessment.Mutation! with { Qualified = successful && kind == "qualification" },
            };
            if (publishedReport is not null && execution is not null)
                AtomicJson(publishedReport + ".assessment.json", Sidecar(assessment, profile.Id, runId, kind, false,
                    identity with { Unchanged = unchanged }, scopeMatched, execution, profile.HealthCheckCommand is not null, healthCheck, profile.CorrectnessPilotProfile, Path.GetRelativePath(repository, profilePath).Replace('\\', '/')));
        }
        var result = new MutationRunResult(successful ? kind == "qualification" ? "qualified" : kind == "canary" ? "canary-passed" : "diagnostic-completed"
            : execution?.Status == "cancelled" ? "cancelled" : "failed", successful ? 0 : 4, kind)
        {
            ProfileId = profile.Id,
            RunId = runId,
            Partial = kind != "qualification",
            Qualified = successful && kind == "qualification",
            ProfileSha256 = profileHash,
            SourceIdentity = identity with { Unchanged = unchanged },
            NativeExecution = execution,
            HealthCheckExecution = healthCheck,
            Assessment = assessment,
            Canary = canary,
            ReportPath = availableReport is null ? null : Path.GetRelativePath(repository, availableReport).Replace('\\', '/'),
            ReportAvailability = availableReport is not null ? "retained" : successful ? "cleaned-after-assessment" : "unavailable",
            SummaryPath = relativeRoot + "/summary.json",
            Limits = Limits(profile),
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Diagnostics = diagnostics.Concat(assessment?.Diagnostics ?? []).ToArray(),
            WorkReference = work?.Reference,
            WorkCoverage = work is null ? "unaccounted-non-adopted" : "governed-native",
        };
        var summaryBytes = JsonSerializer.SerializeToUtf8Bytes(result, Json).LongLength;
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(LatestState(result), Json).LongLength;
        if (successful && publishedReport is null)
        {
            try { EnsureBudget(output, storage, profile, summaryBytes + indexBytes, summaryBytes); }
            catch (Exception error) when (Expected(error)) { successful = false; result = FailedResult(result, error.Message); }
        }
        if (successful && publishedReport is not null && assessment is not null && execution is not null)
        {
            var qualification = Resolve(repository, LocalRoot + "/" + profile.Id + "/last-qualification.json");
            var publishedQualification = false;
            try
            {
                var finalSidecar = Sidecar(assessment, profile.Id, runId, kind, true, identity with { Unchanged = unchanged },
                    scopeMatched, execution, profile.HealthCheckCommand is not null, healthCheck, profile.CorrectnessPilotProfile, Path.GetRelativePath(repository, profilePath).Replace('\\', '/'));
                var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(finalSidecar, Json).LongLength;
                var reportCopyBytes = kind == "qualification" && (!File.Exists(qualification) || FileHash(qualification) != assessment.Mutation!.ReportSha256)
                    ? new FileInfo(publishedReport).Length : 0;
                // Include both atomic sidecar writes and a possible last-qualification report copy.
                EnsureBudget(output, storage, profile, reportCopyBytes + metadataBytes * 2 + summaryBytes + indexBytes, summaryBytes);
                if (kind == "qualification")
                {
                    Publish(publishedReport, qualification, assessment, repository, profile.Id, runId, kind, true,
                        identity with { Unchanged = unchanged }, scopeMatched, execution, profile.HealthCheckCommand is not null, healthCheck, profile.CorrectnessPilotProfile, Path.GetRelativePath(repository, profilePath).Replace('\\', '/'));
                    publishedQualification = true;
                }
                AtomicJson(publishedReport + ".assessment.json", finalSidecar);
                EnsureBudget(output, storage, profile, summaryBytes + indexBytes, summaryBytes);
            }
            catch (Exception error) when (Expected(error))
            {
                assessment = assessment with { Status = "failed", FailureKind = TestFailureKind.Infrastructure, Mutation = assessment.Mutation! with { Qualified = false } };
                result = FailedResult(result, error.Message) with { Assessment = assessment };
                AtomicJson(publishedReport + ".assessment.json", Sidecar(assessment, profile.Id, runId, kind, false,
                    identity with { Unchanged = unchanged }, scopeMatched, execution, profile.HealthCheckCommand is not null, healthCheck, profile.CorrectnessPilotProfile, Path.GetRelativePath(repository, profilePath).Replace('\\', '/')));
                if (publishedQualification)
                    AtomicJson(qualification + ".assessment.json", Sidecar(Rebind(assessment, repository, qualification), profile.Id, runId, kind, false,
                        identity with { Unchanged = unchanged }, scopeMatched, execution, profile.HealthCheckCommand is not null, healthCheck, profile.CorrectnessPilotProfile, Path.GetRelativePath(repository, profilePath).Replace('\\', '/')));
            }
        }
        AtomicJson(Resolve(output, "summary.json"), result);
        var stableEvidence = new[] { "latest-result.json", "latest-result.json.assessment.json", "last-qualification.json", "last-qualification.json.assessment.json" }
            .Select(name => Resolve(repository, LocalRoot + "/" + profile.Id + "/" + name)).Where(File.Exists);
        var storageResult = SettleNativeStorage(storageReservation, ownedStorage, safelyStopped,
            stableEvidence.Append(Resolve(output, "summary.json")).ToArray());
        if (storageResult is { Allowed: false })
        {
            result = FailedResult(result, string.Join(" ", storageResult.Errors));
            AtomicJson(Resolve(output, "summary.json"), result);
        }
        WriteLatest(repository, result, work, ownedStorage, supersededSummary: storageResult?.Allowed == false ? null : previousSummary);
        return result with
        {
            WorkReference = work?.Reference,
            WorkCoverage = work is null ? "unaccounted-non-adopted" : "governed-native",
            WorkControl = work?.Finish("native-finished", "Native mutation result admitted: " + result.Status,
                availableReport is null ? [relativeRoot + "/summary.json"] : [Path.GetRelativePath(repository, availableReport).Replace('\\', '/')])
        };
    }

    private static MutationRunResult BlockedByCanary(string repository, MutationRunProfile profile, string profileHash,
        MutationSourceIdentity identity, MutationRunResult canary, ICisNativeWorkScope? scope = null,
        ICisOwnedStorageController? ownedStorage = null)
    {
        var runId = "qualification-blocked-" + Guid.NewGuid().ToString("N")[..8];
        var relative = LocalRoot + "/" + profile.Id + "/runs/" + runId + "/summary.json";
        var path = Resolve(repository, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var result = new MutationRunResult("canary-failed", 4, "qualification")
        {
            ProfileId = profile.Id,
            RunId = runId,
            ProfileSha256 = profileHash,
            SourceIdentity = identity with { Unchanged = canary.SourceIdentity?.Unchanged == true },
            Canary = canary,
            SummaryPath = relative,
            Limits = Limits(profile),
            StartedUtc = canary.StartedUtc,
            FinishedUtc = DateTimeOffset.UtcNow,
            Diagnostics = ["The failed canary prevented the qualification command from starting."],
        };
        OwnedJson(path, result, repository, scope, ownedStorage);
        WriteLatest(repository, result, scope, ownedStorage);
        return result;
    }

    private static void WriteLatest(string repository, MutationRunResult result, ICisNativeWorkScope? scope = null,
        ICisOwnedStorageController? ownedStorage = null, NativeStorageReservation? activeStorage = null,
        string? supersededSummary = null)
    {
        var path = Resolve(repository, LocalRoot + "/" + result.ProfileId + "/latest-run.json");
        var state = LatestState(result);
        if (activeStorage is null)
        {
            supersededSummary ??= PreviousMutationSummary(repository, result.ProfileId!);
            var superseded = supersededSummary is null || supersededSummary == result.SummaryPath ? []
                : new[] { "summary.json", "native-result.json", "native-result.json.assessment.json" }
                    .Select(name => Path.Combine(Path.GetDirectoryName(Resolve(repository, supersededSummary))!, name)).ToArray();
            OwnedJson(path, state, repository, scope, ownedStorage, supersededRequiredPaths: superseded);
        }
        else
        {
            // The attempt already holds this growth; a second reservation would
            // double-charge its full pending allowance before native launch.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Json).LongLength;
            Require(TreeBytes(Resolve(repository, LocalRoot)) + bytes
                <= checked(activeStorage.InitialNativeBytes + activeStorage.Admission.MaximumGrowthBytes),
                "The existing owned native reservation cannot fit its atomic running marker.");
            AtomicJson(path, state);
        }
    }

    private static string? PreviousMutationSummary(string repository, string profileId)
    {
        var marker = Resolve(repository, LocalRoot + "/" + profileId + "/latest-run.json");
        if (!File.Exists(marker)) return null;
        Require(new FileInfo(marker).Length is > 0 and <= 16384, "The current mutation marker exceeds its bounded metadata size.");
        using var json = JsonDocument.Parse(File.ReadAllBytes(marker));
        var root = json.RootElement;
        Require(root.ValueKind == JsonValueKind.Object && root.TryGetProperty("profileId", out var profile)
            && profile.GetString() == profileId, "The current mutation marker does not match its profile.");
        if (!root.TryGetProperty("summaryPath", out var summary) || summary.ValueKind == JsonValueKind.Null) return null;
        Require(summary.ValueKind == JsonValueKind.String, "The current mutation summary reference is invalid.");
        var relative = summary.GetString()!;
        var path = Resolve(repository, relative);
        var runs = Resolve(repository, LocalRoot + "/" + profileId + "/runs");
        Require(CisPathSafety.IsUnderRoot(runs, path, false) && Path.GetFileName(path) == "summary.json"
            && string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(path)), runs, CisPathSafety.PlatformComparison),
            "The current mutation summary must be an exact run summary in this profile's owned runs directory.");
        return relative;
    }

    private static object LatestState(MutationRunResult result) => new
    {
        schemaVersion = 1,
        result.ProfileId,
        result.RunId,
        result.Kind,
        result.Status,
        result.ExitCode,
        result.Partial,
        result.Qualified,
        result.SummaryPath,
        result.StartedUtc,
        result.FinishedUtc,
        result.WorkReference,
        result.WorkCoverage,
    };

    private static MutationRunResult FailedResult(MutationRunResult result, string diagnostic) => result with
    {
        Status = "failed",
        ExitCode = 4,
        Qualified = false,
        Diagnostics = result.Diagnostics.Append(diagnostic).ToArray(),
        Assessment = result.Assessment is null ? null : result.Assessment with
        {
            Status = "failed",
            FailureKind = TestFailureKind.Infrastructure,
            Mutation = result.Assessment.Mutation! with { Qualified = false },
        },
    };

    private static MutationRunLimits Limits(MutationRunProfile profile) => new(profile.TimeoutSeconds, profile.MaxRunBytes, profile.MaxRetainedBytes, profile.MinFreeBytes);
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
        or ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception;

    internal static bool MatchScope(string repository, MutationRunProfile profile, string report, bool exact, List<string> diagnostics)
    {
        if (profile.SourceFiles.Length == 0) return !exact;
        var declared = profile.SourceFiles.Select(path => Resolve(repository, path)).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var observed = new HashSet<string>(declared.Comparer);
        using var document = JsonDocument.Parse(File.ReadAllBytes(report));
        var working = Resolve(repository, profile.WorkingDirectory, allowRoot: true);
        foreach (var file in document.RootElement.GetProperty("files").EnumerateObject())
        {
            var path = Path.GetFullPath(file.Name.Replace('\\', Path.DirectorySeparatorChar), working);
            if (!declared.Contains(path))
            {
                if (file.Value.GetProperty("mutants").EnumerateArray().All(mutant => mutant.GetProperty("status").GetString() is "Ignored" or "CompileError")) continue;
                diagnostics.Add("Native report contains mutation outcomes outside the declared source scope: " + file.Name);
                return false;
            }
            var nativeSource = file.Value.GetProperty("source").GetString();
            // Stryker emits this placeholder for files outside a canary's
            // mutate filter. It proves no coverage and cannot satisfy a full run.
            var omittedCanaryFile = !exact && nativeSource == "File ignored by mutate filter"
                && file.Value.GetProperty("mutants").GetArrayLength() == 0;
            if (CisPathSafety.ContainsReparsePoint(repository, path)
                || !File.Exists(path) || !omittedCanaryFile && nativeSource != File.ReadAllText(path))
            { diagnostics.Add("Native report does not match the declared source bytes: " + file.Name); return false; }
            if (omittedCanaryFile) continue;
            if (!observed.Add(path)) { diagnostics.Add("Native report repeats a declared source through a path alias."); return false; }
        }
        if (exact && !observed.SetEquals(declared)) { diagnostics.Add("Qualification report does not cover exactly the declared source file set."); return false; }
        return true;
    }

    private static TestSuiteExecution Rebind(TestSuiteExecution assessment, string repository, string report)
    {
        var relative = Path.GetRelativePath(repository, report).Replace('\\', '/');
        return assessment with
        {
            Mutation = assessment.Mutation is null ? null : assessment.Mutation with { SourcePath = relative },
            Artifacts = assessment.Artifacts.Select(artifact => artifact.Kind == "mutation-result" ? artifact with { Path = relative } : artifact).ToArray(),
        };
    }

    private static JsonObject Sidecar(TestSuiteExecution assessment, string profileId, string runId, string kind,
        bool successful, MutationSourceIdentity identity, bool matched, MutationNativeExecution execution, bool healthConfigured, MutationNativeExecution? healthCheck,
        string? correctnessPilotProfile = null, string? profilePath = null)
    {
        assessment = assessment with
        {
            Status = successful ? kind == "qualification" ? "passed" : "partial" : "failed",
            Mutation = assessment.Mutation! with { Qualified = successful && kind == "qualification" },
        };
        var node = JsonSerializer.SerializeToNode(assessment, Json)!.AsObject();
        node["cisRun"] = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1,
            runId,
            profileId,
            kind,
            partial = kind != "qualification",
            qualified = successful && kind == "qualification",
            sourceIdentity = identity,
            sourceUnchanged = identity.Unchanged,
            scopeMatched = matched,
            executionStatus = execution.Status,
            nativeExitCode = execution.ExitCode,
            nativeExecution = execution,
            correctnessPilotProfile,
            profilePath,
            healthCheckConfigured = healthConfigured,
            healthCheckExecuted = healthCheck is not null,
            healthCheckExecution = healthCheck,
        }, Json);
        return node;
    }

    private static void Publish(string source, string destination, TestSuiteExecution assessment, string repository,
        string profileId, string runId, string kind, bool successful, MutationSourceIdentity identity, bool matched, MutationNativeExecution execution,
        bool healthConfigured, MutationNativeExecution? healthCheck, string? correctnessPilotProfile = null, string? profilePath = null)
    {
        Require(FileHash(source) == assessment.Mutation?.ReportSha256, "Published native report failed hash verification.");
        if (Path.GetFileName(destination) == "latest-result.json") File.Move(source, destination, overwrite: true);
        else if (!File.Exists(destination) || FileHash(destination) != assessment.Mutation?.ReportSha256)
        {
            var temporary = destination + "." + runId + ".tmp";
            try
            {
                File.Copy(source, temporary, overwrite: false);
                Require(FileHash(temporary) == assessment.Mutation?.ReportSha256, "Published native report copy failed hash verification.");
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary) && !CisPathSafety.IsReparsePoint(temporary)) File.Delete(temporary); }
        }
        AtomicJson(destination + ".assessment.json", Sidecar(Rebind(assessment, repository, destination), profileId, runId, kind,
            successful, identity, matched, execution, healthConfigured, healthCheck, correctnessPilotProfile, profilePath));
    }

    private static void Prune(string output, bool successful)
    {
        Require(!CisPathSafety.IsReparsePoint(output) && !CisPathSafety.ContainsReparsePointInTree(output), "Linked native output cannot be cleaned as an owned regular tree.");
        var keep = new HashSet<string>(successful ? Array.Empty<string>() : new[] { "stdout.log", "stderr.log", "native-result.json", "native-result.json.assessment.json",
            "health" + Path.DirectorySeparatorChar + "stdout.log", "health" + Path.DirectorySeparatorChar + "stderr.log" }, StringComparer.Ordinal);
        var files = CisPathSafety.EnumerateFiles(output).ToArray();
        if (!successful)
        {
            const long maximumLogBytes = 16 * 1024 * 1024;
            var logCount = 0;
            long logBytes = 0;
            foreach (var file in files.OrderBy(path => Path.GetRelativePath(output, path), StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(output, file);
                if (!relative.StartsWith("logs" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || !(Path.GetExtension(relative).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(relative).Equals(".log", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                var length = new FileInfo(file).Length;
                if (length > maximumLogBytes - logBytes)
                {
                    continue;
                }
                keep.Add(relative);
                logBytes += length;
                if (++logCount == 32)
                {
                    break;
                }
            }
        }
        foreach (var file in files)
        {
            if (!keep.Contains(Path.GetRelativePath(output, file)))
            {
                File.Delete(file);
            }
        }
        if (!File.Exists(Path.Combine(output, "native-result.json"))) File.Delete(Path.Combine(output, "native-result.json.assessment.json"));
        foreach (var directory in CisPathSafety.EnumerateDirectories(output).OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static void EnsureBudget(string output, string storage, MutationRunProfile profile, long additionalRetainedBytes = 0, long additionalRunBytes = 0)
    {
        var run = TreeBytes(output);
        Require(run < profile.MaxRunBytes && additionalRunBytes < profile.MaxRunBytes - run, "Mutation run storage limit prevents publication or qualification.");
        var retained = TreeBytes(storage);
        Require(retained < profile.MaxRetainedBytes && additionalRetainedBytes < profile.MaxRetainedBytes - retained,
            "Mutation retained storage limit prevents publication or qualification.");
        var free = new DriveInfo(storage).AvailableFreeSpace;
        Require(free >= profile.MinFreeBytes && additionalRetainedBytes <= free - profile.MinFreeBytes,
            "Mutation free-space limit prevents publication or qualification.");
    }

    private static long TreeBytes(string root)
    {
        Require(!CisPathSafety.IsReparsePoint(root) && !CisPathSafety.ContainsReparsePointInTree(root), "Linked mutation storage cannot be measured safely.");
        long bytes = 0;
        foreach (var path in CisPathSafety.EnumerateFiles(root))
        {
            var length = new FileInfo(path).Length;
            Require(length <= long.MaxValue - bytes, "Mutation storage exceeds the supported byte range.");
            bytes += length;
        }
        return bytes;
    }
}
