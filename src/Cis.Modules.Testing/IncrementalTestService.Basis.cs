using System.Runtime.InteropServices;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private static IncrementalInputFact[] ProfileMaterialFacts(string repository, IncrementalTestProfile profile,
        string profilePath, string[] tools, IReadOnlyDictionary<string, string> environmentDigests,
        IReadOnlyDictionary<string, string> configurationHashes, IReadOnlyDictionary<string, string> toolHashes)
    {
        var facts = new List<IncrementalInputFact>();
        void Value(string category, string name, object? value, bool mutationOnly = false)
            => facts.Add(new(category, name, HashObject(value)) { Origin = "profile", MutationOnly = mutationOnly });
        Value("configuration", "working-directory", Relative(repository, Safe(repository, profile.WorkingDirectory, true)));
        Value("configuration", "unit-command", profile.UnitCommand);
        Value("configuration", "unit-report", new { reportPath = Relative(repository, Safe(repository, profile.UnitReportPath)), profile.UnitResultFormat });
        Value("selection", "full-test-filter", profile.FullTestFilter);
        Value("platform", "runtime-identifier", RuntimeInformation.RuntimeIdentifier);
        Value("configuration", "mutation:command", profile.MutationCommand, true);
        Value("configuration", "mutation:recovery", profile.RecoveryCommand, true);
        Value("configuration", "mutation:assessment", new { profile.MutationReportPath, profile.CanarySourceFile,
            profile.MinimumCanaryKills, profile.BreakThreshold, profile.RerunAdditionalTimeoutMilliseconds }, true);
        if (UnitRendersMutationConfiguration(profile))
            Value("configuration", "unit-rendered-mutation-configuration", profile.StrykerConfigFile is null ? null
                : Relative(repository, Safe(repository, profile.StrykerConfigFile)));
        foreach (var file in profile.ConfigFiles.Where(file => !PathComparer.Equals(Safe(repository, file), profilePath)))
        {
            var unit = UnitConfigurationInput(repository, profile, profilePath, file);
            facts.Add(new("configuration", file, configurationHashes[file]) { MutationOnly = !unit });
        }
        foreach (var file in tools) facts.Add(new("tool", file, toolHashes[file]));
        foreach (var pair in environmentDigests) facts.Add(new("environment", pair.Key, pair.Value));
        return facts.ToArray();
    }

    private static IncrementalInputFact[] MaterialBasis(Input input, string source, string[] tests,
        string filter, string[] dependencies, string[] edges, bool mutation, IReadOnlyDictionary<string, string>? fileHashes = null)
    {
        var facts = input.MaterialFacts.Where(fact => mutation || !fact.MutationOnly).ToList();
        facts.Add(new("selection", "source", HashObject(source)) { Origin = "profile" });
        facts.Add(new("selection", "tests", HashObject(tests)) { Origin = "profile" });
        facts.Add(new("selection", "filter", HashObject(filter)) { Origin = "profile" });
        facts.Add(new("selection", "dependency-edges", HashObject(edges)) { Origin = "profile" });
        foreach (var file in dependencies)
        {
            var category = input.Profile.SourceFiles.Contains(file, PathComparer) ? "source"
                : tests.Contains(file, PathComparer) ? "test" : "dependency";
            facts.Add(new(category, file, fileHashes?.GetValueOrDefault(file) ?? HashFile(Safe(input.Repository, file))));
        }
        return facts.Distinct().OrderBy(fact => fact.Category, StringComparer.Ordinal)
            .ThenBy(fact => fact.Input, StringComparer.Ordinal).ToArray();
    }

    private static string RetainBasis(IncrementalTestState state, IncrementalInputFact[] facts)
    {
        var ids = facts.Select(fact =>
        {
            var id = HashObject(fact);
            state.InputFacts.TryAdd(id, fact);
            return id;
        }).ToArray();
        var basis = HashObject(ids);
        state.InputBases.TryAdd(basis, ids);
        return basis;
    }

    private static void PruneInputBases(IncrementalTestState state)
    {
        var keep = state.Batches.Values.SelectMany(batch => new[] { batch.Unit?.InputBasisId, batch.Mutation?.InputBasisId })
            .Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var id in state.InputBases.Keys.Where(id => !keep.Contains(id)).ToArray()) state.InputBases.Remove(id);
        var facts = state.InputBases.Values.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);
        foreach (var id in state.InputFacts.Keys.Where(id => !facts.Contains(id)).ToArray()) state.InputFacts.Remove(id);
    }

    private IncrementalReuseReason[] ExplainReuse(Input input, IncrementalTestState state,
        IncrementalStageResult? prior, IncrementalInputFact[] facts, bool current, bool mutation)
    {
        var unsettled = StateSettlementErrors(input.Repository, state, Safe(input.Root, "latest-run.json"), input.Work?.Reference).Length > 0;
        if (current) return unsettled ? [new("unsettled-execution", mutation ? "mutation" : "unit", "Reconcile the original execution reference.")] : [];
        if (prior is null) return [new("result-missing", "result", "missing")];
        var reasons = new List<IncrementalReuseReason>();
        if (prior.InputBasisId is { } id && state.InputBases.TryGetValue(id, out var saved)
            && saved.All(state.InputFacts.ContainsKey))
        {
            var previous = saved.Select(key => state.InputFacts[key]).ToDictionary(fact => (fact.Category, fact.Input, fact.Origin));
            var next = facts.ToDictionary(fact => (fact.Category, fact.Input, fact.Origin));
            foreach (var key in previous.Keys.Union(next.Keys).OrderBy(key => key.Category, StringComparer.Ordinal).ThenBy(key => key.Input, StringComparer.Ordinal))
            {
                var before = previous.GetValueOrDefault(key);
                var after = next.GetValueOrDefault(key);
                if (before?.Sha256 == after?.Sha256) continue;
                reasons.Add(new(key.Category, key.Input, before is null ? "added" : after is null ? "removed" : "changed"));
            }
        }
        else reasons.Add(new("input-detail-unavailable", "historical-result", "No executed input breakdown was retained; compatibility checks still apply."));
        if (!prior.Completed) reasons.Add(new("incomplete-result", "result", prior.Status));
        if (prior.AssessmentContractVersion != CisIncrementalResultContract.Version && !(input.LegacyIdentity && prior.AssessmentContractVersion == 0))
            reasons.Add(new("incompatible-assessment", "assessment-contract", "changed"));
        try
        {
            var path = Safe(input.Root, prior.ReportPath);
            if (!File.Exists(path)) reasons.Add(new("report-missing", prior.ReportPath, "missing"));
            else if (HashFile(path) != prior.ReportSha256) reasons.Add(new("report-altered", prior.ReportPath, "changed"));
        }
        catch (Exception error) when (Expected(error)) { reasons.Add(new("report-invalid", "report", "unreadable")); }
        if (unsettled)
            reasons.Add(new("unsettled-execution", mutation ? "mutation" : "unit", "Reconcile the original execution reference."));
        if (reasons.Count == 0) reasons.Add(new("execution-validation", mutation ? "mutation" : "unit", "Native command, counts or execution evidence does not validate."));
        return reasons.ToArray();
    }
}
