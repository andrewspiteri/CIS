using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record DeliveryNativeSource(DeliveryFile File, IReadOnlyList<string> StoryIds,
        IReadOnlyList<string> Imports, IReadOnlyList<string> DeclaredTypes, IReadOnlyList<string> Namespaces,
        IReadOnlyList<string> UsingNamespaces, bool DynamicImport, IReadOnlyList<string> ExternalImports,
        IReadOnlyList<string> QualifiedReferences, IReadOnlyList<string> ExplicitTypeReferences);
    private sealed record DeliveryCompilerIndex(CisGraphSnapshotReadResult Snapshot, IReadOnlyDictionary<string, CisGraphNode> Nodes,
        IReadOnlyDictionary<string, CisGraphNode[]> Symbols, IReadOnlyDictionary<string, CisGraphEdge[]> Calls,
        IReadOnlyDictionary<string, string> Inputs, IReadOnlyDictionary<string, CisGraphNode> Components);
    private sealed record DeliveryCompilerResolution(string Proof, IReadOnlyList<string> Targets);
    private sealed record DeliveryAsset(string RepositoryId, string Path, string Absolute);
    private static readonly HashSet<string> DeliveryStaticAssets = new(".css .json .svg .png .jpg .jpeg .gif .webp .avif .ico .woff .woff2 .ttf .otf .eot".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DeliveryNodeModules = new(("assert assert/strict async_hooks buffer child_process cluster console constants crypto dgram diagnostics_channel dns dns/promises domain events fs fs/promises http http2 https inspector inspector/promises module net os path path/posix path/win32 perf_hooks process punycode querystring readline readline/promises repl stream stream/consumers stream/promises stream/web string_decoder sys timers timers/promises tls trace_events tty url util util/types v8 vm wasi worker_threads zlib node:sea node:sqlite node:test node:test/reporters").Split(' '), StringComparer.Ordinal);

    private static string DeliveryContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string DeliveryBasisHash(IReadOnlyDictionary<string, string> basis)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(basis.OrderBy(item => item.Key, StringComparer.Ordinal), Json)));

    private static string DeliveryMeaning(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static string DeliveryLanguageFamily(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ts" or ".tsx" or ".js" or ".jsx" => "ecmascript",
        var extension => extension
    };

    private static bool IsDeliveryCompilerSymbol(CisGraphNode node)
        => node.Kind == "symbol" && node.Facets.Contains("csharp", StringComparer.Ordinal)
            && node.Facets.Contains("compiler-bound", StringComparer.Ordinal) && node.Properties.GetValueOrDefault("binding") == "compiler"
            && node.Properties.GetValueOrDefault("external") == "false" && node.Properties.GetValueOrDefault("language") == "csharp"
            && !string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("component"));

    private static DeliveryCompilerResolution? ResolveDeliveryCompilerAmbiguity(DeliveryCompilerIndex compiler, DeliveryNativeSource source,
        string name, IReadOnlyList<string> candidates, IReadOnlyDictionary<string, DeliveryNativeSource> code, IReadOnlyList<CisWorkspaceRepository> repositories)
    {
        var snapshot = compiler.Snapshot; var graph = snapshot.Graph!; var manifest = snapshot.Manifest!;
        if (snapshot.Errors.Count > 0 || graph.Build.Status != "complete" || graph.DiagnosticSummary.Errors > 0 || graph.DiagnosticSummary.Warnings > 0
            || snapshot.Freshness is not ("fresh" or "stale") || snapshot.Status is not ("available" or "stale")
            || snapshot.Freshness == "fresh" && snapshot.ExitCode != 0 || snapshot.Freshness == "stale" && snapshot.ExitCode != 5
            || graph.SchemaVersion != 2 || manifest.SchemaVersion != 2 || graph.Build.Id != manifest.BuildId
            || graph.Build.RepositoryId != source.File.RepositoryId || manifest.RepositoryId != source.File.RepositoryId
            || !manifest.Extractors.Contains("cis.csharp.compiler/6", StringComparer.Ordinal)
            || !manifest.Extractors.Contains("cis.csharp.semantic-facts/3", StringComparer.Ordinal)
            || snapshot.Warnings.Any(warning => warning.Contains("extractor", StringComparison.OrdinalIgnoreCase))) return null;
        if (!compiler.Symbols.TryGetValue(source.File.Path, out var symbols)
            || compiler.Inputs.GetValueOrDefault(source.File.Path) != source.File.ContentHash) return null;
        var repository = repositories.Single(item => item.Id == source.File.RepositoryId);
        var currentComponents = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Captured(CisGraphNode node, string path) => node.Provenance.Any(evidence => evidence.Path.Replace('\\', '/') == path
            && evidence.Method == "compiler-binding" && evidence.Extractor == "cis.csharp.compiler/6"
            && evidence.ContentHash == compiler.Inputs.GetValueOrDefault(path));
        bool ComponentCurrent(CisGraphNode node)
        {
            var id = node.Properties.GetValueOrDefault("component") ?? "";
            if (currentComponents.TryGetValue(id, out var current)) return current;
            compiler.Components.TryGetValue(source.File.RepositoryId + "/" + id, out var component);
            return currentComponents[id] = component is not null && component.Locations.Count > 0 && component.Locations.All(location =>
                CisPathSafety.TryResolveUnderRoot(repository.RepositoryPath, location.Path, out var absolute) && SafeAbsolutePath(absolute)
                && File.Exists(absolute) && DeliveryContentHash(absolute) == compiler.Inputs.GetValueOrDefault(location.Path.Replace('\\', '/')));
        }
        if (symbols.Any(node => !Captured(node, source.File.Path) || !ComponentCurrent(node))) return null;
        var declaredOwners = symbols.Where(node => node.Subtype is "class" or "record" or "struct" or "interface" or "enum" or "delegate")
            .Select(node => node.Properties.GetValueOrDefault("qualifiedName") ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var topOwners = declaredOwners.Where(owner => !declaredOwners.Any(other => other != owner && owner.StartsWith(other + ".", StringComparison.Ordinal))).ToArray();
        if (topOwners.Length != 1 || symbols.Any(node => node.Properties.GetValueOrDefault("qualifiedName") is not { } qualified
            || qualified != topOwners[0] && !qualified.StartsWith(topOwners[0] + ".", StringComparison.Ordinal))) return null;
        bool NamedType(CisGraphNode node) => node.Subtype is "class" or "record" or "struct" or "interface" or "enum" or "delegate"
            && Regex.IsMatch(node.Properties.GetValueOrDefault("qualifiedName") ?? "", @"(?:^|\.)" + Regex.Escape(name) + @"(?:<.*>)?$", RegexOptions.CultureInvariant);
        var types = new List<CisGraphNode>();
        foreach (var candidate in candidates.Where(path => code[path].File.RepositoryId == source.File.RepositoryId))
        {
            var path = code[candidate].File.Path;
            var owned = (compiler.Symbols.GetValueOrDefault(path) ?? []).Where(NamedType).ToArray();
            if (owned.Length == 0 || owned.Any(node => !Captured(node, path) || !ComponentCurrent(node))) return null;
            types.AddRange(owned);
        }
        var nestedIdentity = topOwners[0] + "." + name;
        if (!types.Any(node => Regex.IsMatch(node.Properties.GetValueOrDefault("qualifiedName") ?? "", "^" + Regex.Escape(nestedIdentity) + @"(?:<.*>)?$")
            && node.Locations.All(location => location.Path.Replace('\\', '/') == source.File.Path))) return null;
        var bindings = new List<CisGraphEdge>(); var used = new Dictionary<string, CisGraphNode>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
            foreach (var edge in compiler.Calls.GetValueOrDefault(symbol.Key) ?? [])
            {
                if (!compiler.Nodes.TryGetValue(edge.To, out var target) || !IsDeliveryCompilerSymbol(target)
                    || !Regex.IsMatch(target.Properties.GetValueOrDefault("qualifiedName") ?? "", @"(?:^|\.)" + Regex.Escape(name) + @"(?:\.|\(|<|$)")
                    || !edge.Observations.Any(evidence => evidence.Method == "compiler-call" && evidence.Extractor == "cis.csharp.semantic-facts/3"
                        && evidence.Path.Replace('\\', '/') == source.File.Path && evidence.ContentHash == source.File.ContentHash)) continue;
                bindings.Add(edge); used[target.Key] = target;
            }
        // Positive qualified constructor/call facts resolve this collision.
        // Missing edges alone never prove that another State is unrelated.
        if (bindings.Count == 0) return null;
        foreach (var type in types)
        {
            var qualified = type.Properties["qualifiedName"];
            if (symbols.Any(symbol => Regex.IsMatch(symbol.Properties.GetValueOrDefault("signature") ?? "",
                @"(?<![A-Za-z0-9_])" + Regex.Escape(qualified) + @"(?![A-Za-z0-9_])"))) used[type.Key] = type;
        }
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in used.Values)
        {
            if (!ComponentCurrent(node) || node.Locations.Count == 0) return null;
            foreach (var location in node.Locations)
            {
                var path = location.Path.Replace('\\', '/'); var key = node.RepositoryId + "/" + path;
                if (!code.TryGetValue(key, out var target) || compiler.Inputs.GetValueOrDefault(path) != target.File.ContentHash || !Captured(node, path)) return null;
                targets.Add(key);
            }
        }
        var candidatesCurrent = candidates.Where(path => code[path].File.RepositoryId == source.File.RepositoryId)
            .All(path => compiler.Inputs.GetValueOrDefault(code[path].File.Path) == code[path].File.ContentHash);
        if (snapshot.Freshness != "fresh" || !candidatesCurrent)
        {
            // An unchanged, positively bound nested type in this exact source
            // cannot be rebound by an unrelated sibling's private State edit.
            var ownNested = used.Values.All(node => node.Locations.All(location => location.Path.Replace('\\', '/') == source.File.Path)
                && Regex.IsMatch(node.Properties.GetValueOrDefault("qualifiedName") ?? "", "^" + Regex.Escape(nestedIdentity) + @"(?:\.|\(|<|$)"));
            if (!ownNested) return null;
        }
        var proof = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            symbols = symbols.OrderBy(node => node.Key, StringComparer.Ordinal).Select(node => new { node.Key, Properties = node.Properties.OrderBy(item => item.Key, StringComparer.Ordinal) }),
            targets = used.Values.OrderBy(node => node.Key, StringComparer.Ordinal).Select(node => new { node.Key, Properties = node.Properties.OrderBy(item => item.Key, StringComparer.Ordinal) }),
            bindings = bindings.OrderBy(edge => edge.Key, StringComparer.Ordinal).Select(edge => new { edge.Type, edge.From, edge.To }),
            components = graph.Nodes.Where(node => node.Kind == "component" && symbols.Concat(used.Values)
                .Any(symbol => symbol.Properties.GetValueOrDefault("component") == node.LocalId)).OrderBy(node => node.Key, StringComparer.Ordinal)
                .Select(node => new { node.LocalId, node.Authority, node.Lifecycle, Properties = node.Properties.Where(item => item.Key is "root" or "languages" or "frameworks" or "roles" or "capabilities").OrderBy(item => item.Key, StringComparer.Ordinal) })
        }, Json)));
        return new(proof, targets.ToArray());
    }

    // Contract scope deliberately excludes implementation and review bookkeeping:
    // implementing an approved task must not revoke the plan that authorizes it.
    private static IReadOnlyDictionary<string, string> StoryContractBasis(WizardState state, DeliveryInput input, string storyId)
    {
        if (input.ContractBasisByStory.TryGetValue(storyId, out var existing)) return existing;
        var draft = input.Drafts.Single(item => item.Id == storyId);
        var basis = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["subject"] = draft.Id,
            ["phase"] = draft.Phase,
            ["future-candidate"] = draft.Story.FutureCandidate.ToString(),
            ["title"] = DeliveryMeaning(draft.Story.Title),
            ["narrative"] = DeliveryMeaning(draft.Story.Narrative),
            ["acceptance"] = JsonSerializer.Serialize(draft.Story.Acceptance.Select(DeliveryMeaning), Json),
            ["request"] = DeliveryMeaning(FeatureRequestMaterial(state)),
            ["constraints"] = DeliverySourceBoundaries(state.Source, legacy: true)
        };
        if (draft.ParentId is { } parentId)
        {
            basis["parent"] = parentId;
            basis["parent accepted requirements"] = JsonSerializer.Serialize(CurrentStoryAcceptance(input, draft), Json);
            basis["story prerequisites"] = JsonSerializer.Serialize(draft.StoryDependencies.Order(StringComparer.Ordinal), Json);
            basis["shared task prerequisites"] = JsonSerializer.Serialize(draft.SharedPrerequisiteTaskIds.Order(StringComparer.Ordinal), Json);
            foreach (var reference in draft.SharedPrerequisiteTaskIds)
            {
                var parts = reference.Split('/');
                var prerequisite = parts.Length == 2 ? ReadStoryRecord(state, parts[0])?.Plan.Tasks.SingleOrDefault(task => task.Id == parts[1]) : null;
                basis["shared task contract/" + reference] = prerequisite is null ? "missing" : StoryTaskContractJson(prerequisite).GetRawText();
            }
        }
        var terms = DeliveryWords(draft.Story.Title + " " + draft.Story.Narrative + " " + string.Join(' ', draft.Story.Acceptance));
        var work = state.Review.RepositoryWork ?? [];
        var selectedWork = work.Where(item => item.Id == storyId || DeliveryWords(item.Title + " " + item.Scope).Overlaps(terms))
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var pendingWork = new Queue<string>(selectedWork);
        while (pendingWork.TryDequeue(out var id))
        {
            var item = work.FirstOrDefault(item => item.Id == id);
            if (item is null) { basis["prerequisite/" + id] = "missing"; continue; }
            basis["prerequisite/" + id] = JsonSerializer.Serialize(new
            {
                item.Id,
                item.RepositoryId,
                item.Title,
                item.Scope,
                DependsOn = item.DependsOn.Order(StringComparer.Ordinal),
                ChangeIds = item.ChangeIds.Order(StringComparer.Ordinal)
            }, Json);
            foreach (var dependency in item.DependsOn) if (selectedWork.Add(dependency)) pendingWork.Enqueue(dependency);
        }
        foreach (var page in state.Review.Pages.Where(page => page.Key != "review"))
            foreach (var answer in page.Value.Answers.Where(answer => !answer.Key.StartsWith(StoryFieldPrefix, StringComparison.Ordinal)))
            {
                var crossCutting = page.Key == "technical" && Regex.IsMatch(answer.Key, "stack|platform|security|standard|constraint|permission|architecture", RegexOptions.IgnoreCase);
                if (crossCutting || DeliveryWords(answer.Value).Overlaps(terms))
                    basis["direction/" + page.Key + "/" + answer.Key] = DeliveryMeaning(answer.Value);
            }
        if (state.Record.Plan.SourceBrds is { Count: > 0 })
        {
            foreach (var field in StructuredFields(state, "technical", state.Review.Pages.GetValueOrDefault("technical")).Where(field => field.Inherited))
                basis["inherited/technical/" + field.Id] = DeliveryMeaning(field.SuggestedAnswer ?? "");
            basis["shared-boundaries"] = DeliverySourceBoundaries(state.PlanningContext, legacy: true);
        }
        var repositories = state.Record.Plan.IntegrationRepositories.Append(input.Repositories.FirstOrDefault(repository =>
            SamePath(repository.RepositoryPath, state.Record.Plan.RepositoryPath))?.Id ?? "").Where(id => id.Length > 0).Distinct(StringComparer.Ordinal);
        foreach (var id in repositories)
            basis["repository/" + id] = DeliveryRepositoryIdentity(input.Repositories.FirstOrDefault(repository => repository.Id == id));
        return basis;
    }

    private const string StoryBoundaryBasisKey = "delivery boundaries/3b";

    private static bool StoryHasExternalPrerequisites(DeliveryInput input, string storyId, IReadOnlyList<CisFeatureStoryTask> tasks)
    {
        var draft = input.Drafts.Single(item => item.Id == storyId);
        return tasks.Any(task => task.DependsOn.Any(reference => reference.Contains('/')))
            || draft.SharedPrerequisiteTaskIds.Count > 0 || draft.StoryDependencies.Count > 0;
    }

    // Bind exact prerequisite instances, including their own material prerequisites.
    // Approval actors, events, execution IDs and timestamps do not change this contract.
    private IReadOnlyDictionary<string, string> StoryTaskPrerequisiteBasis(WizardState state, DeliveryInput input,
        string storyId, CisFeatureStoryTask task)
    {
        var basis = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        // Existing intake bounds permit twenty tasks and one outcome per retained story.
        var maximumSubjects = input.Drafts.Count * 21;
        IReadOnlyList<CisFeatureStoryTask> Tasks(string owner)
        {
            var record = ReadStoryRecord(state, owner);
            if (record is not null) return record.Plan.Tasks;
            var cache = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-stories", state.Record.Plan.Slug, owner + ".json");
            return SafeAbsolutePath(cache) ? ReadStoryCache(cache)?.Tasks ?? [] : [];
        }
        bool Enter(string key)
        {
            if (done.Contains(key)) return false;
            if (active.Count + done.Count >= maximumSubjects) throw new InvalidDataException("Exact prerequisite material coverage exceeds the supported tasks and outcomes in this feature.");
            if (!active.Add(key)) { basis["cycle/" + key] = "Unresolved prerequisite cycle."; return false; }
            return true;
        }
        void CaptureTask(string owner, string id)
        {
            var key = owner + "/" + id;
            if (!Enter(key)) return;
            var draft = input.Drafts.SingleOrDefault(item => item.Id == owner);
            var prerequisite = draft is null ? null : Tasks(owner).SingleOrDefault(item => item.Id == id);
            if (prerequisite is null) basis[key] = "Missing exact prerequisite task.";
            else
            {
                var material = ApprovedStoryBasis(state, input, owner, [prerequisite], includePrerequisites: false)
                    .Where(item => item.Key is not ("title" or "narrative" or "acceptance" or "parent accepted requirements"))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                var accepted = CurrentStoryAcceptance(input, draft!);
                material["selected accepted requirements"] = JsonSerializer.Serialize(prerequisite.RequirementNumbers.Select(number =>
                    number > 0 && number <= accepted.Count ? accepted[number - 1] : "Missing accepted requirement " + number), Json);
                basis[key] = DeliveryBasisHash(material);
                foreach (var reference in prerequisite.DependsOn.Concat(draft!.SharedPrerequisiteTaskIds).Distinct(StringComparer.Ordinal)) CaptureReference(owner, reference);
                foreach (var outcome in draft.StoryDependencies) CaptureOutcome(outcome);
            }
            active.Remove(key); done.Add(key);
        }
        void CaptureReference(string owner, string reference)
        {
            var parts = reference.Split('/');
            if (parts.Length == 1) CaptureTask(owner, reference);
            else if (parts.Length == 2 && input.Drafts.Any(item => item.Id == parts[0])) CaptureTask(parts[0], parts[1]);
            else basis[reference] = "Unresolved exact prerequisite reference.";
        }
        void CaptureOutcome(string owner)
        {
            var key = "outcome/" + owner;
            if (!Enter(key)) return;
            var draft = input.Drafts.SingleOrDefault(item => item.Id == owner);
            var children = StoryPlanning(state, owner)?.Children ?? [];
            basis[key] = draft is null ? "Missing prerequisite story outcome." : Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { draft.Story.Title, draft.Story.Narrative, Requirements = CurrentStoryAcceptance(input, draft), Children = children.Select(child => child.Id).Order(StringComparer.Ordinal) }, Json)));
            if (draft is not null)
            {
                if (children.Count > 0)
                {
                    // Whole outcomes require retained own preparation as well as child delivery.
                    // Unapproved cache proposals and superseded parent implementation do not contribute.
                    foreach (var item in ReadStoryRecord(state, owner)?.Plan.Tasks.Where(item => StoryOutcomePreparation(state, item)) ?? [])
                        CaptureTask(owner, item.Id);
                    foreach (var child in children) CaptureOutcome(child.Id!);
                }
                else foreach (var item in Tasks(owner)) CaptureTask(owner, item.Id);
                foreach (var dependency in draft.StoryDependencies) CaptureOutcome(dependency);
            }
            active.Remove(key); done.Add(key);
        }
        var own = input.Drafts.Single(item => item.Id == storyId);
        // Local edges already bind the full retained task list. Cross-story references
        // need a material snapshot; their bounded closure also captures local prerequisites.
        foreach (var reference in task.DependsOn.Concat(own.SharedPrerequisiteTaskIds).Where(reference => reference.Contains('/')).Distinct(StringComparer.Ordinal))
            CaptureReference(storyId, reference);
        foreach (var outcome in own.StoryDependencies) CaptureOutcome(outcome);
        return basis;
    }

    private static IReadOnlyDictionary<string, string> WithStoryDeliveryBoundaries(WizardState state,
        IReadOnlyDictionary<string, string> basis)
    {
        var current = new SortedDictionary<string, string>(basis.ToDictionary(item => item.Key, item => item.Value), StringComparer.Ordinal)
        {
            [StoryBoundaryBasisKey] = JsonSerializer.Serialize(new
            {
                source = DeliverySourceBoundaries(state.Source),
                shared = state.Record.Plan.SourceBrds is { Count: > 0 } ? DeliverySourceBoundaries(state.PlanningContext) : ""
            }, Json)
        };
        return current;
    }

    private static IReadOnlyDictionary<string, string> StoryBasisForRecordedContract(
        IReadOnlyDictionary<string, string>? recorded, IReadOnlyDictionary<string, string> current)
        => recorded is not null && !recorded.ContainsKey(StoryBoundaryBasisKey)
            ? current.Where(item => item.Key != StoryBoundaryBasisKey).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
            : current;

    private static int StoryContractVersion(IReadOnlyDictionary<string, string>? basis)
        => basis is null || !basis.ContainsKey(StoryBoundaryBasisKey) ? 1 : 2;

    private static string EffectiveStoryConstraints(DeliveryInput input, int contractVersion)
        => contractVersion < 2 || string.IsNullOrWhiteSpace(input.ContractBoundaries) ? input.Constraints
            : input.Constraints + "\n\nInherited delivery defaults, compatibility and interfaces:\n" + input.ContractBoundaries;

    private static string DeliverySourceBoundaries(string source, bool legacy = false)
    {
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var headings = Regex.Matches(BrdDocumentLayout.Visible(source), @"(?m)^(?<level>#{1,6})\s+(?<title>[^\r\n]+)\r?$");
        var ranges = new List<(int Start, int End)>();
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            if (!Regex.IsMatch(heading.Groups["title"].Value,
                legacy ? "exclud|exclusion|out.of.scope|non.goals|constraint|assumption|ownership|principles|boundar|responsibilit|separate|authoritative|decisions|dependenc|prerequisit|permission|standards"
                    : "exclud|exclusion|out.of.scope|non.goals|constraint|assumption|ownership|principles|boundar|responsibilit|separate|authoritative|decisions|dependenc|prerequisit|permission|standards|default|compatibil|interface|contract", RegexOptions.IgnoreCase)) continue;
            var next = index + 1;
            while (next < headings.Count && headings[next].Groups["level"].Length > heading.Groups["level"].Length) next++;
            var end = next < headings.Count ? headings[next].Index : source.Length;
            if (ranges.Count == 0 || heading.Index >= ranges[^1].End) ranges.Add((heading.Index, end));
        }
        return string.Join("\n\n", ranges.Select(range => source[range.Start..range.End].Trim()));
    }

    private static string DeliveryRepositoryIdentity(CisWorkspaceRepository? repository)
        => repository is null ? "missing" : JsonSerializer.Serialize(new
        {
            repository.Id,
            repository.RepositoryPath,
            repository.Role,
            repository.Participation,
            repository.Relationship,
            repository.DocumentationRoot,
            Components = repository.Components.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        }, Json);

    private DeliveryInput ReadDeliveryBasis(WizardState state, DeliveryInput input, IReadOnlyList<DeliveryFile> files)
    {
        var code = new Dictionary<string, DeliveryNativeSource>(StringComparer.Ordinal);
        var incomplete = new Dictionary<string, string>(StringComparer.Ordinal);
        var omitted = new Dictionary<string, (DeliveryFile File, string Reason)>(StringComparer.Ordinal);
        var unbounded = new Dictionary<string, string>(StringComparer.Ordinal);
        var unclassifiedInventories = new HashSet<string>(StringComparer.Ordinal);
        var metadataBytes = new Dictionary<string, long>(StringComparer.Ordinal);
        var queries = input.Drafts.ToDictionary(draft => draft.Id, DeliveryQueryFor, StringComparer.Ordinal);
        foreach (var file in files.OrderBy(file => file.RepositoryId, StringComparer.Ordinal).ThenBy(file => file.Path, StringComparer.Ordinal))
        {
            if (file.Length > 262_144)
            {
                incomplete[file.RepositoryId] = "Implementation files larger than 256 KiB were not searched.";
                omitted[file.RepositoryId + "/" + file.Path] = (file, "larger-than-256-KiB");
                continue;
            }
            var content = File.ReadAllText(file.Absolute);
            if (DeliverySensitive(content))
            {
                incomplete[file.RepositoryId] = "Sensitive-looking implementation was omitted; dependency coverage is unknown.";
                omitted[file.RepositoryId + "/" + file.Path] = (file, "sensitive-content-omitted"); continue;
            }
            var parsed = ReadDeliverySymbols(file, content);
            var searchable = DeliveryNonCode.Replace(content, " ");
            var scriptImports = DeliveryLanguageFamily(file.Path) is "ecmascript" or ".vue" or ".svelte";
            var projection = new DeliveryNativeSource(file,
                input.Drafts.Where(draft => MatchDeliveryCode(parsed, queries[draft.Id]) is not null).Select(draft => draft.Id).ToArray(),
                scriptImports ? Regex.Matches(content, "(?:from\\s*|require\\s*\\(\\s*|import\\s*\\(\\s*|import\\s*)[\"'](?<path>\\.[^\"']+)[\"']")
                    .Select(match => match.Groups["path"].Value).Distinct(StringComparer.Ordinal).ToArray() : [],
                Regex.Matches(searchable, @"\b(?:class|interface|record|struct|enum|type)\s+(?<name>[A-Za-z_]\w*)")
                    .Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToArray(),
                Regex.Matches(searchable, @"\bnamespace\s+(?<name>[A-Za-z_]\w*(?:\.\w+)*)")
                    .Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToArray(),
                Regex.Matches(searchable, @"(?m)^\s*using\s+(?<name>[A-Za-z_]\w*(?:\.\w+)*)\s*;")
                    .Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToArray(),
                scriptImports && Regex.IsMatch(content, "\\b(?:import|require)\\s*\\(\\s*[^\\s\"']"),
                scriptImports ? Regex.Matches(content, "(?:from\\s*|require\\s*\\(\\s*|import\\s*)[\"'](?<path>[^.][^\"']*)[\"']")
                    .Select(match => match.Groups["path"].Value).Distinct(StringComparer.Ordinal).ToArray() : [],
                Path.GetExtension(file.Path) == ".cs" ? Regex.Matches(searchable, @"\b[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+\b")
                    .SelectMany(match => match.Value.Split('.').SkipLast(1)).Distinct(StringComparer.Ordinal).ToArray() : [],
                Path.GetExtension(file.Path) == ".cs" ? Regex.Matches(searchable, @"\b(?:typeof|sizeof)\s*\((?<type>[^)\r\n]+)\)|(?m)^\s*(?:global\s+)?using\s+(?<alias>[A-Za-z_]\w*)\s*=\s*(?<type>[^;\r\n]+)")
                    .SelectMany(match => Regex.Matches(match.Groups["type"].Value.Replace("global::", "", StringComparison.Ordinal), @"\b[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\b")
                        .Where(value => value.Value != "global").Take(match.Groups["alias"].Success ? 1 : int.MaxValue)
                        .Select(value => (match.Groups["alias"].Success ? match.Groups["alias"].Value + "=" : "") + value.Value))
                    .Distinct(StringComparer.Ordinal).ToArray() : []);
            var size = new[] { projection.Imports, projection.DeclaredTypes, projection.Namespaces, projection.UsingNamespaces, projection.ExternalImports, projection.QualifiedReferences, projection.ExplicitTypeReferences }
                .Sum(values => 32L + values.Count * 8L + values.Sum(value => 32L + value.Length * 2L));
            if (metadataBytes.GetValueOrDefault(file.RepositoryId) + size > 41_943_040)
            {
                incomplete[file.RepositoryId] = unbounded[file.RepositoryId] = "Native dependency metadata for " + file.RepositoryId
                    + " exceeded its 40 MiB projection bound; dependency coverage needs a narrower scope.";
                projection = projection with { Imports = [], DeclaredTypes = [], Namespaces = [], UsingNamespaces = [], ExternalImports = [], QualifiedReferences = [], ExplicitTypeReferences = [] };
            }
            else metadataBytes[file.RepositoryId] = metadataBytes.GetValueOrDefault(file.RepositoryId) + size;
            code[file.RepositoryId + "/" + file.Path] = projection;
        }
        foreach (var repository in input.Repositories)
            if (input.Warnings.Any(warning => warning.Contains(repository.Id, StringComparison.Ordinal)
                && (warning.Contains("unavailable", StringComparison.Ordinal) || warning.Contains("exceeded", StringComparison.Ordinal))))
            {
                incomplete[repository.Id] = "Implementation inventory is unavailable or incomplete for " + repository.Id + ".";
                unbounded[repository.Id] = incomplete[repository.Id];
                unclassifiedInventories.Add(repository.Id);
            }

        var snapshots = new Dictionary<string, CisGraphSnapshotReadResult>(StringComparer.Ordinal);
        var compilers = new Dictionary<string, DeliveryCompilerIndex>(StringComparer.Ordinal);
        if (graphReader is not null)
            foreach (var repository in input.Repositories)
            {
                var snapshot = graphReader.Read(repository.RepositoryPath); snapshots[repository.Id] = snapshot;
                if (snapshot.Graph is null || snapshot.Manifest is null) continue;
                var symbols = snapshot.Graph.Nodes.Where(IsDeliveryCompilerSymbol).ToArray();
                compilers[repository.Id] = new(snapshot, snapshot.Graph.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal),
                    symbols.SelectMany(node => node.Locations.Select(location => (Path: location.Path.Replace('\\', '/'), Node: node)))
                        .GroupBy(item => item.Path, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Select(item => item.Node).DistinctBy(node => node.Key).ToArray(), StringComparer.Ordinal),
                    snapshot.Graph.Edges.Where(edge => edge.Type == "calls" && edge.State == "discovered" && edge.Confidence == "high"
                        && edge.Properties.GetValueOrDefault("scope") == "repository").GroupBy(edge => edge.From, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal),
                    snapshot.Manifest.Inputs.ToDictionary(item => item.Path.Replace('\\', '/'), item => item.Hash, StringComparer.Ordinal),
                    snapshot.Graph.Nodes.Where(node => node.Kind == "component").ToDictionary(node => node.RepositoryId + "/" + node.LocalId, StringComparer.Ordinal));
            }

        // A bounded native fallback follows explicit imports and declared types.
        // Unlike selected excerpts, its seed set contains EVERY relevance match.
        var links = code.Keys.ToDictionary(key => key, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var linkCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var linkLimited = new HashSet<string>(StringComparer.Ordinal);
        void Link(string source, string target)
        {
            if (links[source].Contains(target)) return;
            var id = code[source].File.RepositoryId;
            if (linkCounts.GetValueOrDefault(id) >= 100_000)
            {
                linkLimited.Add(id);
                incomplete[id] = unbounded[id] = "Native dependency traversal for " + id
                    + " exceeded its 100,000-link bound; ambiguous dependency coverage needs review.";
                return;
            }
            links[source].Add(target);
            linkCounts[id] = linkCounts.GetValueOrDefault(id) + 1;
        }
        var absolutePaths = code.ToDictionary(item => Path.GetFullPath(item.Value.File.Absolute), item => item.Key, StringComparer.OrdinalIgnoreCase);
        var unresolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var unboundDependencies = new HashSet<string>(StringComparer.Ordinal);
        var absentDependencies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var assets = new Dictionary<string, Dictionary<string, DeliveryAsset>>(StringComparer.Ordinal);
        var runtimes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var compilerProofs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var candidateFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var candidateNotes = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidateLimits = new HashSet<string>(StringComparer.Ordinal);
        var omittedPaths = omitted.Values.Select(item => Path.GetFullPath(item.File.Absolute)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var namespaces = code.Values.GroupBy(source => source.File.RepositoryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(source => source.Namespaces).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var declarations = code.SelectMany(item => item.Value.DeclaredTypes.Select(name => (Name: name, Path: item.Key)))
            .GroupBy(item => item.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Select(item => item.Path).Distinct().ToArray(), StringComparer.Ordinal);
        foreach (var (path, source) in code)
        {
            if (linkLimited.Contains(source.File.RepositoryId)) continue;
            foreach (var import in source.Imports)
            {
                var suffixAt = import.IndexOfAny(['?', '#']);
                var literal = suffixAt < 0 ? import : import[..suffixAt];
                if (literal.Contains('\\') || literal.Contains('%') || suffixAt >= 0 && !Regex.IsMatch(import[suffixAt..], @"^(?:\?(?:raw|url|inline|direct|import)(?:&(?:raw|url|inline|direct|import))*)?(?:#[A-Za-z0-9_.:-]{1,128})?$"))
                {
                    unresolved[path] = "A relative dependency has an unsupported literal path or loader suffix: " + import + ".";
                    unboundDependencies.Add(path); continue;
                }
                var relative = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source.File.Absolute)!, literal));
                var paths = new List<string> { relative };
                if (!Path.HasExtension(relative))
                    paths.AddRange(DeliveryExtensions.Concat([".json", ".d.ts", ".mjs", ".cjs", ".mts", ".cts"]).SelectMany(extension => new[] { relative + extension, Path.Combine(relative, "index" + extension) }));
                var importExtension = Path.GetExtension(relative).ToLowerInvariant();
                if (importExtension is ".js" or ".jsx")
                    paths.AddRange(new[] { Path.ChangeExtension(relative, ".ts"), Path.ChangeExtension(relative, ".tsx"), Path.ChangeExtension(relative, ".mts"), Path.ChangeExtension(relative, ".cts") });
                if (importExtension is ".mjs" or ".cjs")
                    paths.Add(Path.ChangeExtension(relative, importExtension == ".mjs" ? ".mts" : ".cts"));
                var targets = paths.Select(path => absolutePaths.GetValueOrDefault(path)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
                if (paths.Any(candidate => new[] { ".mjs", ".cjs", ".mts", ".cts" }.Contains(Path.GetExtension(candidate), StringComparer.OrdinalIgnoreCase) && File.Exists(candidate)))
                {
                    unresolved[path] = "A relative executable dependency is outside the native code projection: " + import + ".";
                    unboundDependencies.Add(path);
                }
                var staticAssets = paths.Select(candidate => (Path: candidate, Repository: input.Repositories.FirstOrDefault(repository => CisPathSafety.IsUnderRoot(repository.RepositoryPath, candidate))))
                    .Where(item => item.Repository is not null && SafeAbsolutePath(item.Path) && File.Exists(item.Path) && DeliveryStaticAssets.Contains(Path.GetExtension(item.Path)))
                    .Select(item => new DeliveryAsset(item.Repository!.Id, Path.GetRelativePath(item.Repository.RepositoryPath, item.Path).Replace('\\', '/'), item.Path)).ToArray();
                if (staticAssets.Length > 0)
                {
                    if (!assets.TryGetValue(path, out var references)) assets[path] = references = new(StringComparer.Ordinal);
                    foreach (var asset in staticAssets) references[asset.RepositoryId + "/" + asset.Path] = asset;
                    if (!unboundDependencies.Contains(path)) unresolved[path] = "Owned static asset bytes are captured; rendered or schema coverage remains to be verified.";
                }
                if (targets.Length == 0 && staticAssets.Length == 0)
                {
                    var owned = input.Repositories.Any(repository => CisPathSafety.IsUnderRoot(repository.RepositoryPath, relative));
                    if (paths.Any(omittedPaths.Contains))
                    {
                        if (!unboundDependencies.Contains(path)) unresolved[path] = "A relative dependency's exact bytes are retained as omitted implementation: " + import + ".";
                    }
                    else if (owned && !paths.Any(File.Exists) && !Directory.Exists(relative))
                    {
                        if (!unboundDependencies.Contains(path)) unresolved[path] = "A relative implementation dependency is absent from the owned inventory: " + import + ".";
                        if (!absentDependencies.TryGetValue(path, out var absent)) absentDependencies[path] = absent = [];
                        absent.Add(import);
                    }
                    else
                    {
                        unresolved[path] = "A relative dependency is outside owned inventory or has an unsupported target: " + import + ".";
                        unboundDependencies.Add(path);
                    }
                }
                foreach (var target in targets) Link(path, target);
            }
            // All type declarations are now known. A second streaming pass keeps
            // only actual file dependency edges, rather than every source body
            // or every identifier in the repository.
            var identifiers = DeliveryIdentifiers.Matches(DeliveryNonCode.Replace(File.ReadAllText(source.File.Absolute), " "))
                .Select(match => match.Value).Where(declarations.ContainsKey).Distinct(StringComparer.Ordinal);
            foreach (var name in identifiers)
            {
                var candidates = (declarations.GetValueOrDefault(name) ?? []).Where(target => code[target].File.RepositoryId == source.File.RepositoryId
                    && DeliveryLanguageFamily(code[target].File.Path) == DeliveryLanguageFamily(source.File.Path)).ToArray();
                if (candidates.Length > 1 && Path.GetExtension(source.File.Path) == ".cs")
                {
                    var explicitReferences = source.ExplicitTypeReferences.Select(reference => reference.Split('=', 2))
                        .Where(parts => (parts.Length == 2 ? parts[0] : parts[0].Split('.')[^1]) == name)
                        .Select(parts => parts[^1]).ToArray();
                    var unsupported = explicitReferences.Length > 0 || source.QualifiedReferences.Contains(name, StringComparer.Ordinal);
                    var resolution = !unsupported && candidates.Length <= 256 && compilers.TryGetValue(source.File.RepositoryId, out var compiler)
                        ? ResolveDeliveryCompilerAmbiguity(compiler, source, name, candidates, code, input.Repositories) : null;
                    if (resolution is null)
                    {
                        var outside = explicitReferences.Any(reference => !DeliveryOwnedTypeReference(reference, source, code));
                        if (outside)
                        {
                            unresolved[path] = "Ambiguous C# name '" + name + "' has an explicit type reference outside the bounded owned inventory.";
                            unboundDependencies.Add(path);
                        }
                        else if (candidates.Length > 256)
                        {
                            candidateNotes[path] = "Ambiguous C# name '" + name + "' exceeded the 256-candidate compiler evidence bound; narrow dependency coverage.";
                            candidateLimits.Add(path);
                        }
                        else
                        {
                            if (!candidateLimits.Contains(path)) candidateNotes[path] = "Member/type interpretation for '" + name + "' is uncertain; all " + candidates.Length + " owned candidate files are captured conservatively.";
                            if (!candidateFiles.TryGetValue(path, out var capturedCandidates)) candidateFiles[path] = capturedCandidates = new(StringComparer.Ordinal);
                            foreach (var target in candidates.Where(target => target != path)) capturedCandidates.Add(target);
                        }
                    }
                    else
                    {
                        if (!compilerProofs.TryGetValue(path, out var proofs)) compilerProofs[path] = proofs = new(StringComparer.Ordinal);
                        proofs[name] = resolution.Proof;
                        foreach (var target in resolution.Targets.Where(target => target != path)) Link(path, target);
                    }
                    continue;
                }
                foreach (var target in candidates)
                {
                    if (linkLimited.Contains(source.File.RepositoryId)) break;
                    if (target != path && code[target].File.RepositoryId == source.File.RepositoryId) Link(path, target);
                }
            }
            if (source.DynamicImport)
            {
                unresolved[path] = "A dynamic implementation dependency cannot be resolved by the native scoped fallback.";
                unboundDependencies.Add(path);
            }
            if (Path.GetExtension(source.File.Path) is ".ts" or ".tsx" or ".js" or ".jsx")
            {
                foreach (var import in source.ExternalImports)
                {
                    var repository = input.Repositories.Single(repository => repository.Id == source.File.RepositoryId);
                    if (DeliveryRuntimeContract(repository, source.File.Absolute, import) is { } runtime)
                    {
                        if (!runtimes.TryGetValue(path, out var runtimeContracts)) runtimes[path] = runtimeContracts = new(StringComparer.Ordinal);
                        runtimeContracts[import] = runtime;
                    }
                    else if (!DeliveryDeclaredPackage(repository, source.File.Absolute, import))
                    {
                        unresolved[path] = "An imported package or path alias has no declared, captured dependency contract; its target coverage is unbound.";
                        unboundDependencies.Add(path);
                    }
                }
            }
            if (Path.GetExtension(source.File.Path) == ".cs" && DeliveryUnsupportedProjectReference(
                input.Repositories.Single(repository => repository.Id == source.File.RepositoryId), source.File.Absolute) is { } projectReason)
            {
                unresolved[path] = projectReason; unboundDependencies.Add(path);
            }
            if (Path.GetExtension(source.File.Path) == ".cs"
                && source.UsingNamespaces.Any(import => !import.StartsWith("System", StringComparison.Ordinal)
                    && !import.StartsWith("Microsoft", StringComparison.Ordinal)
                    && !(namespaces.GetValueOrDefault(source.File.RepositoryId) ?? []).Any(value => value == import || value.StartsWith(import + ".", StringComparison.Ordinal)))
                && !DeliveryHasDependencyManifest(input.Repositories.Single(repository => repository.Id == source.File.RepositoryId), source.File.Absolute, true))
            {
                unresolved[path] = "An external namespace has no captured project dependency manifest; its contract coverage is unknown.";
                unboundDependencies.Add(path);
            }
        }

        var contracts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var materials = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var unboundReasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var draft in input.Drafts)
        {
            var contract = StoryContractBasis(state, input, draft.Id);
            contracts[draft.Id] = contract;
            var basis = new SortedDictionary<string, string>(contract.ToDictionary(item => item.Key, item => item.Value), StringComparer.Ordinal);
            var selectedRoots = (state.Review.DeliveryReviews?.LastOrDefault(review => review.StoryId == draft.Id)?.EvidenceHashes.Keys ?? [])
                .Select(reference =>
                {
                    try
                    {
                        var absolute = DeliveryReference(state, reference).Absolute;
                        var file = files.FirstOrDefault(file => SamePath(file.Absolute, absolute));
                        return file is null ? reference : file.RepositoryId + "/" + file.Path;
                    }
                    catch (InvalidDataException) { return reference; }
                }).ToArray();
            var seeds = code.Where(item => item.Value.StoryIds.Contains(draft.Id)).Select(item => item.Key)
                .Concat(selectedRoots.Where(code.ContainsKey)).Distinct(StringComparer.Ordinal).ToArray();
            if (selectedRoots.Any(path => !code.ContainsKey(path) && !omitted.ContainsKey(path)))
                reasons[draft.Id] = unboundReasons[draft.Id] = "A retained selected implementation is outside or unavailable in the supported dependency inventory. Restore missing owned source, or renew this decision using an inventoried owned implementation reference; excluded scripts, tests and migrations cannot supply dependency coverage.";
            var pending = new Queue<string>(seeds); var scope = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out var path))
            {
                if (!scope.Add(path)) continue;
                foreach (var target in links[path]) pending.Enqueue(target);
            }
            // Ambiguous declarations support the source directly. They are not
            // dependency edges: following their own ambiguous candidates would
            // turn common private names into repository-wide connectivity.
            var directScope = scope.ToHashSet(StringComparer.Ordinal);
            var candidatesForStory = directScope.SelectMany(path => candidateFiles.GetValueOrDefault(path) ?? []).Distinct(StringComparer.Ordinal).ToArray();
            if (candidatesForStory.Length > 4096)
                unboundReasons[draft.Id] = "Owned candidate coverage exceeded this story's 4,096-file bound.";
            var candidateScope = new HashSet<string>(StringComparer.Ordinal);
            var candidatePending = new Queue<string>(candidatesForStory.Take(4096));
            while (candidatePending.TryDequeue(out var candidate))
            {
                if (!candidateScope.Add(candidate)) continue;
                foreach (var target in links[candidate]) candidatePending.Enqueue(target);
            }
            scope.UnionWith(candidateScope);
            // These languages have declaration evidence, without adequate native
            // import/binding coverage. Compare the owned same-family bytes,
            // including safe scripts/tests omitted from search, conservatively.
            foreach (var familyScope in scope.Select(path => code[path].File)
                .Where(file => DeliveryLanguageFamily(file.Path) is not (".cs" or "ecmascript"))
                .GroupBy(file => (file.RepositoryId, Family: DeliveryLanguageFamily(file.Path))))
            {
                var familyKey = familyScope.Key.RepositoryId + "/" + familyScope.Key.Family;
                var uncertain = input.ConservativeFiles.Where(file => file.RepositoryId == familyScope.Key.RepositoryId
                    && DeliveryLanguageFamily(file.Path) == familyScope.Key.Family).ToArray();
                var note = "Dependency interpretation for " + familyKey
                    + " is uncertain; owned same-language files are compared conservatively. Verify imported and runtime contracts before qualifying implementation evidence.";
                reasons[draft.Id] = note;
                basis["uncertain-dependency-coverage/" + familyKey] = note;
                foreach (var file in uncertain) basis["uncertain-dependency/" + file.RepositoryId + "/" + file.Path] = file.ContentHash;
                var repository = input.Repositories.Single(repository => repository.Id == familyScope.Key.RepositoryId);
                foreach (var config in DeliveryConfigurationBasis(repository, uncertain.Select(file => file.Absolute))) basis[config.Key] = config.Value;
                if (input.ConservativeLimits.TryGetValue(familyKey, out var limit))
                    reasons[draft.Id] = unboundReasons[draft.Id] = limit;
            }
            var relevantRepositories = scope.Select(path => code[path].File.RepositoryId).Distinct(StringComparer.Ordinal).ToArray();
            if (omitted.Count > 0)
                reasons[draft.Id] = "A potentially relevant implementation input was not searched: " + string.Join("; ", omitted.Values
                    .GroupBy(item => item.File.RepositoryId).OrderBy(group => group.Key).Take(3)
                    .Select(group => group.Key + ": " + string.Join(", ", group.Select(item => item.Reason).Distinct(StringComparer.Ordinal))));
            // A human can review a bounded, unchanged uncertainty. Retain the
            // actual omitted bytes and conditions without presenting them as
            // searched implementation or proof that a capability is absent.
            foreach (var (path, item) in omitted)
            {
                basis["unsearched/" + path] = item.File.ContentHash;
                basis["coverage/" + path] = item.Reason;
            }
            foreach (var id in omitted.Values.Select(item => item.File.RepositoryId).Distinct(StringComparer.Ordinal))
            {
                var repository = input.Repositories.Single(repository => repository.Id == id);
                basis["repository/" + id] = DeliveryRepositoryIdentity(repository);
                foreach (var config in DeliveryConfigurationBasis(repository, omitted.Values.Where(item => item.File.RepositoryId == id).Select(item => item.File.Absolute)))
                    basis[config.Key] = config.Value;
            }
            var scopedUnbounded = unbounded.Where(item => relevantRepositories.Length == 0 || relevantRepositories.Contains(item.Key, StringComparer.Ordinal)
                || unclassifiedInventories.Contains(item.Key)).OrderBy(item => item.Key).Select(item => item.Value).ToArray();
            if (scopedUnbounded.Length > 0) unboundReasons[draft.Id] = string.Join("; ", scopedUnbounded);
            if (scopedUnbounded.Length > 0) reasons[draft.Id] = unboundReasons[draft.Id];
            foreach (var path in scope.Order(StringComparer.Ordinal))
            {
                basis["implementation/" + path] = code[path].File.ContentHash;
                if (unresolved.TryGetValue(path, out var reason)) { reasons[draft.Id] = reason; basis["coverage/" + path] = reason; }
                if (unboundDependencies.Contains(path)) unboundReasons[draft.Id] = unresolved[path];
                if (directScope.Contains(path) && candidateNotes.TryGetValue(path, out var candidateReason))
                {
                    basis["candidate-coverage/" + path] = candidateReason;
                    if (!unboundDependencies.Contains(path)) reasons[draft.Id] = candidateReason;
                    if (candidateLimits.Contains(path) && !unboundDependencies.Contains(path)) unboundReasons[draft.Id] = candidateReason;
                }
                foreach (var absent in absentDependencies.GetValueOrDefault(path) ?? [])
                    basis["absent-dependency/" + path + "/" + absent] = "absent-in-owned-inventory";
                foreach (var proof in compilerProofs.GetValueOrDefault(path) ?? [])
                    basis["compiler-binding/" + path + "/" + proof.Key] = proof.Value;
                foreach (var runtime in runtimes.GetValueOrDefault(path) ?? [])
                    basis["runtime-dependency/" + path + "/" + runtime.Key] = runtime.Value;
                var assetReferences = assets.GetValueOrDefault(path);
                if (assetReferences?.Count > 512)
                    reasons[draft.Id] = unboundReasons[draft.Id] = "Static asset dependency coverage exceeded its 512-file source bound.";
                foreach (var asset in assetReferences?.Take(512).Select(item => item.Value) ?? [])
                {
                    try
                    {
                        if (!SafeAbsolutePath(asset.Absolute) || !File.Exists(asset.Absolute) || new FileInfo(asset.Absolute).Length > 16_777_216)
                        { reasons[draft.Id] = unboundReasons[draft.Id] = "An asset dependency is unsafe, unavailable or exceeds 16 MiB: " + asset.Path + "."; continue; }
                        basis["asset-dependency/" + asset.RepositoryId + "/" + asset.Path] = DeliveryContentHash(asset.Absolute);
                        basis["repository/" + asset.RepositoryId] = DeliveryRepositoryIdentity(input.Repositories.Single(repository => repository.Id == asset.RepositoryId));
                        var extension = Path.GetExtension(asset.Path);
                        var assetText = extension.Equals(".css", StringComparison.OrdinalIgnoreCase) || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                            ? File.ReadAllText(asset.Absolute) : "";
                        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)) assetText = System.Net.WebUtility.HtmlDecode(assetText);
                        if ((extension.Equals(".css", StringComparison.OrdinalIgnoreCase) || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                            && Regex.IsMatch(assetText, @"(?i)@(?:import|use|forward)\b|url\s*\(\s*(?!['""\s]*(?:data:|#))"))
                            reasons[draft.Id] = unboundReasons[draft.Id] = "A stylesheet has import or URL dependencies outside captured transitive coverage: " + asset.Path + ".";
                        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                            && Regex.IsMatch(assetText, @"(?i)<script\b|(?:href|src)\s*=\s*['""](?!#|data:)"))
                            reasons[draft.Id] = unboundReasons[draft.Id] = "An SVG has executable or external dependencies outside captured transitive coverage: " + asset.Path + ".";
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { reasons[draft.Id] = unboundReasons[draft.Id] = "An owned asset dependency cannot be read: " + asset.Path + "."; }
                }
            }
            // When there is no match, every searched repository remains a possible
            // owner. Omitted content cannot establish that the work is new.
            foreach (var id in relevantRepositories.Length == 0 ? input.Repositories.Select(repository => repository.Id) : relevantRepositories)
            {
                var repository = input.Repositories.Single(repository => repository.Id == id);
                basis["repository/" + id] = DeliveryRepositoryIdentity(repository);
                if (incomplete.TryGetValue(id, out var reason)) reasons[draft.Id] = reason;
                foreach (var config in DeliveryConfigurationBasis(repository, scope.Where(path => code[path].File.RepositoryId == id).Select(path => code[path].File.Absolute)))
                    basis[config.Key] = config.Value;
                if (snapshots.TryGetValue(id, out var snapshot))
                {
                    var previousReason = reasons.GetValueOrDefault(draft.Id);
                    AddDeliveryGraphBasis(repository, snapshot, scope, basis, reasons, draft.Id);
                    if (reasons.GetValueOrDefault(draft.Id) != previousReason)
                        unboundReasons[draft.Id] = unboundReasons.TryGetValue(draft.Id, out var previousUnbound)
                            ? previousUnbound + " " + reasons[draft.Id] : reasons[draft.Id];
                }
            }
            materials[draft.Id] = basis;
        }
        return input with
        {
            ContractBasisByStory = contracts,
            MaterialBasisByStory = materials,
            BasisReasonsByStory = reasons,
            UnboundBasisReasonsByStory = unboundReasons,
            Hash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                inventory = input.Hash,
                material = materials.OrderBy(item => item.Key).Select(item => new { item.Key, Hash = DeliveryBasisHash(item.Value) })
            }, Json)))
        };
    }

    private static bool DeliveryHasDependencyManifest(CisWorkspaceRepository repository, string file, bool csharp)
    {
        for (var directory = Path.GetDirectoryName(file); directory is not null && CisPathSafety.IsUnderRoot(repository.RepositoryPath, directory, allowRoot: true); directory = Path.GetDirectoryName(directory))
            if (Directory.EnumerateFiles(directory).Any(path => csharp ? Path.GetExtension(path) is ".csproj" or ".fsproj" or ".vbproj"
                : Path.GetFileName(path) == "package.json")) return true;
        return false;
    }

    private static bool DeliveryOwnedTypeReference(string reference, DeliveryNativeSource source, IReadOnlyDictionary<string, DeliveryNativeSource> code)
    {
        if (reference.StartsWith("System.", StringComparison.Ordinal) || reference.StartsWith("Microsoft.", StringComparison.Ordinal)
            || new[] { "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal", "char", "string", "object", "void", "dynamic" }.Contains(reference, StringComparer.Ordinal)) return true;
        if (code.Values.Any(item => item.File.RepositoryId == source.File.RepositoryId
            && item.Namespaces.Any(value => value == reference || value.StartsWith(reference + ".", StringComparison.Ordinal)))) return true;
        var parts = reference.Split('.'); var name = parts[^1];
        foreach (var candidate in code.Values.Where(item => item.File.RepositoryId == source.File.RepositoryId
            && DeliveryLanguageFamily(item.File.Path) == DeliveryLanguageFamily(source.File.Path) && item.DeclaredTypes.Contains(name, StringComparer.Ordinal)))
        {
            if (parts.Length == 1 || parts[..^1].All(candidate.DeclaredTypes.Contains)) return true;
            foreach (var ns in candidate.Namespaces)
                if (reference.StartsWith(ns + ".", StringComparison.Ordinal)
                    && reference[(ns.Length + 1)..].Split('.').All(candidate.DeclaredTypes.Contains)) return true;
        }
        return false;
    }

    private static string? DeliveryRuntimeContract(CisWorkspaceRepository repository, string file, string import)
    {
        var builtin = DeliveryNodeModules.Contains(import) || import.StartsWith("node:", StringComparison.Ordinal) && DeliveryNodeModules.Contains(import[5..]);
        for (var directory = Path.GetDirectoryName(file); directory is not null && CisPathSafety.IsUnderRoot(repository.RepositoryPath, directory, allowRoot: true); directory = Path.GetDirectoryName(directory))
        {
            var path = Path.Combine(directory, "package.json");
            if (!SafeAbsolutePath(path)) return null;
            if (!File.Exists(path)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (document.RootElement.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object
                    && engines.TryGetProperty(builtin ? "node" : import, out var version) && version.ValueKind == JsonValueKind.String && version.GetString() is { Length: > 0 } value)
                    return (builtin ? "Node.js runtime module " : "Declared host module ") + import + "; engine contract: " + value;
                return builtin ? "Node.js runtime module " + import + "; runtime version unspecified by this package." : null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
        }
        return builtin ? "Node.js runtime module " + import + "; runtime version unspecified by this package." : null;
    }

    private static bool DeliveryDeclaredPackage(CisWorkspaceRepository repository, string file, string import)
    {
        var parts = import.Split('/');
        var package = import.StartsWith('@') && parts.Length > 1 ? parts[0] + "/" + parts[1] : parts[0];
        for (var directory = Path.GetDirectoryName(file); directory is not null && CisPathSafety.IsUnderRoot(repository.RepositoryPath, directory, allowRoot: true); directory = Path.GetDirectoryName(directory))
        {
            var path = Path.Combine(directory, "package.json");
            if (!SafeAbsolutePath(path)) return false;
            if (!File.Exists(path)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
                    if (document.RootElement.TryGetProperty(section, out var values) && values.ValueKind == JsonValueKind.Object
                        && values.TryGetProperty(package, out var dependency) && dependency.ValueKind == JsonValueKind.String && dependency.GetString() is { Length: > 0 } value)
                        return !value.StartsWith("file:", StringComparison.Ordinal) && !value.StartsWith("link:", StringComparison.Ordinal)
                            && !value.StartsWith("workspace:", StringComparison.Ordinal) && !value.StartsWith(".", StringComparison.Ordinal) && !Path.IsPathRooted(value);
            }
            catch (JsonException) { return false; }
            return false;
        }
        return false;
    }

    private static string? DeliveryUnsupportedProjectReference(CisWorkspaceRepository repository, string file)
    {
        for (var directory = Path.GetDirectoryName(file); directory is not null && CisPathSafety.IsUnderRoot(repository.RepositoryPath, directory, allowRoot: true); directory = Path.GetDirectoryName(directory))
        {
            var projects = Directory.EnumerateFiles(directory, "*.csproj").ToArray();
            if (projects.Length == 0) continue;
            foreach (var project in projects)
            {
                if (!SafeAbsolutePath(project)) return "The project dependency manifest uses an unsafe linked path; owned dependency coverage is unbound.";
                try
                {
                    using var reader = System.Xml.XmlReader.Create(project, new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262_144 });
                    var document = System.Xml.Linq.XDocument.Load(reader);
                    foreach (var reference in document.Descendants().Where(element => element.Name.LocalName is "ProjectReference" or "Compile" && element.Attribute("Include") is not null))
                    {
                        var include = reference.Attribute("Include")!.Value;
                        if (include.IndexOfAny(['$', '@', '*', '?', ';']) >= 0 || reference.AncestorsAndSelf().Any(element => element.Attribute("Condition") is not null))
                            return "A project dependency uses conditional or expanded inputs whose owned scope cannot be established.";
                        var target = Path.GetFullPath(Path.Combine(directory, include));
                        if (!CisPathSafety.IsUnderRoot(repository.RepositoryPath, target) || !SafeAbsolutePath(target) || !File.Exists(target))
                            return "A project dependency is outside the owned implementation inventory or unavailable: " + include + ".";
                    }
                }
                catch (System.Xml.XmlException) { return "The project dependency manifest cannot be safely interpreted; dependency coverage is unbound."; }
            }
            return null;
        }
        return null;
    }

    private static IEnumerable<KeyValuePair<string, string>> DeliveryConfigurationBasis(CisWorkspaceRepository repository, IEnumerable<string> implementation)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { repository.RepositoryPath };
        foreach (var file in implementation)
            for (var directory = Path.GetDirectoryName(file); directory is not null && CisPathSafety.IsUnderRoot(repository.RepositoryPath, directory); directory = Path.GetDirectoryName(directory))
                directories.Add(directory);
        foreach (var directory in directories.Order(StringComparer.Ordinal))
        {
            if (!Directory.Exists(directory) || !SafeAbsolutePath(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory).Where(path => Regex.IsMatch(Path.GetFileName(path),
                @"^(?:AGENTS\.md|\.editorconfig|global\.json|NuGet\.Config|packages\.lock\.json|Directory\..*\.(?:props|targets)|.*\.(?:csproj|fsproj|vbproj)|package(?:-lock)?\.json|(?:pnpm-lock|yarn)\.(?:yaml|yml|lock)|.*(?:config|settings|standards).*\.(?:json|yaml|yml|toml|xml)|(?:Cargo|pyproject)\.toml|(?:Cargo|uv)\.lock|requirements.*\.txt|go\.(?:mod|sum))$", RegexOptions.IgnoreCase)))
                if (SafeAbsolutePath(path)) yield return new("configuration/" + repository.Id + "/" + Path.GetRelativePath(repository.RepositoryPath, path).Replace('\\', '/'), DeliveryContentHash(path));
        }
    }

    private static void AddDeliveryGraphBasis(CisWorkspaceRepository repository, CisGraphSnapshotReadResult snapshot,
        IReadOnlySet<string> nativeScope, IDictionary<string, string> basis, IDictionary<string, string> reasons, string storyId)
    {
        // Missing graph data supplies no negative evidence. Native imports, types,
        // complete relevance search and configuration hashes remain the fallback.
        if (snapshot.Graph is null || snapshot.Manifest is null) return;
        var graph = snapshot.Graph;
        var nodes = graph.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
        var selected = graph.Nodes.Where(node => node.Locations.Any(location => nativeScope.Contains(repository.Id + "/" + location.Path.Replace('\\', '/'))))
            .Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) return;
        var edges = graph.Edges.Where(edge => edge.State != "proposed" && Regex.IsMatch(edge.Type,
            "depend|call|implement|inherit|reference|configuration|contract|consume|produce|read|write|route", RegexOptions.IgnoreCase)).ToArray();
        var pending = new Queue<string>(selected);
        while (pending.TryDequeue(out var key))
        {
            foreach (var edge in edges.Where(edge => edge.From == key))
            {
                basis["dependency/" + repository.Id + "/" + edge.Key] = JsonSerializer.Serialize(new
                {
                    edge.Type,
                    edge.From,
                    edge.To,
                    edge.State,
                    Properties = edge.Properties.OrderBy(item => item.Key, StringComparer.Ordinal)
                }, Json);
                var target = edge.To;
                if (selected.Count >= 8192) { reasons[storyId] = "Relevant graph dependency traversal reached its 8,192-node bound; coverage is unknown."; break; }
                if (selected.Add(target)) pending.Enqueue(target);
            }
        }
        foreach (var key in selected)
        {
            if (!nodes.TryGetValue(key, out var node)) { reasons[storyId] = "A relevant graph dependency endpoint is missing."; continue; }
            foreach (var location in node.Locations)
            {
                if (!CisPathSafety.TryResolveUnderRoot(repository.RepositoryPath, location.Path, out var path) || !SafeAbsolutePath(path))
                { reasons[storyId] = "A relevant graph dependency has an unsafe or unavailable source location."; continue; }
                var fullHash = File.Exists(path) ? DeliveryContentHash(path) : "missing";
                var scoped = fullHash;
                var locator = "";
                if (File.Exists(path) && Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)
                    && location.LocatorKind is "heading" or "table-row")
                {
                    locator = "#" + location.LocatorKind + ":" + location.LocatorValue;
                    scoped = DeliveryGraphDocumentScope(path, location);
                    if (scoped == "unknown") reasons[storyId] = "A relevant graph document locator is missing or ambiguous; its material scope needs review.";
                }
                basis["graph-input/" + repository.Id + "/" + location.Path + locator] = scoped;
                var captured = snapshot.Manifest.Inputs.FirstOrDefault(input => input.Path == location.Path);
                if (captured is not null && captured.Hash != fullHash)
                    reasons[storyId] = "A relevant graph dependency changed since extraction; its current dependency coverage is unknown.";
            }
        }
        if (graph.Build.Status == "partial" || graph.DiagnosticSummary.Errors > 0 || snapshot.Errors.Count > 0)
            reasons[storyId] = "Relevant graph extraction is incomplete or has errors; dependency coverage needs review.";
        if (snapshot.Manifest.Extractors.Count == 0 || snapshot.Warnings.Any(warning => warning.Contains("extractor-set-changed", StringComparison.Ordinal))
            || snapshot.Manifest.SchemaVersion != graph.SchemaVersion || snapshot.Manifest.BuildId != graph.Build.Id)
            reasons[storyId] = "Relevant graph extraction or schema coverage is incompatible; dependency coverage is unknown.";
    }

    private static string DeliveryGraphDocumentScope(string path, CisGraphLocation location)
    {
        var lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (location.LocatorKind == "heading")
        {
            var headings = lines.Select((line, index) => (Match: Regex.Match(line, @"^(?<level>#{1,6})\s+(?<title>.+?)\s*#*\s*$"), Index: index))
                .Where(item => item.Match.Success && Regex.IsMatch(item.Match.Groups["title"].Value,
                    @"(?<![A-Za-z0-9_])" + Regex.Escape(location.LocatorValue) + @"(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)).ToArray();
            if (headings.Length != 1) return "unknown";
            var start = headings[0].Index; var level = headings[0].Match.Groups["level"].Length; var end = start + 1;
            while (end < lines.Length)
            {
                var heading = Regex.Match(lines[end], @"^(#{1,6})\s+");
                if (heading.Success && heading.Groups[1].Length <= level) break;
                end++;
            }
            return Hash(Encoding.UTF8.GetBytes(string.Join('\n', lines[start..end]).Trim()));
        }
        var identities = location.LocatorValue.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var rows = lines.Select((line, index) => (Cells: Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|").Select(cell => cell.Trim()).ToArray(), Index: index))
            .Where(item => item.Cells.Length > 1 && identities.Length > 0 && identities.All(identity => item.Cells.Contains(identity, StringComparer.Ordinal))).ToArray();
        if (rows.Length != 1) return "unknown";
        var row = rows[0]; var separator = row.Index - 1;
        while (separator > 0 && !Regex.IsMatch(lines[separator], @"^\s*\|?\s*:?-{3,}:?\s*\|")) separator--;
        if (separator <= 0) return "unknown";
        return Hash(Encoding.UTF8.GetBytes(lines[separator - 1].Trim() + "\n" + string.Join('|', row.Cells)));
    }
}
