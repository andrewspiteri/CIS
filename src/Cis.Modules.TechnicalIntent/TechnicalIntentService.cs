using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Brd;
using Cis.Modules.Graph;
using Cis.Modules.Repository;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentService : IChangeReadinessCheck, ICisTechnicalIntentDraftPreparer
{
    private const string BaselineStart = "<!-- cis:technical-intent-baseline:start -->";
    private const string BaselineEnd = "<!-- cis:technical-intent-baseline:end -->";
    private const string BusinessEvidenceStart = "<!-- cis:technical-intent-business-evidence:start -->";
    private const string BusinessEvidenceEnd = "<!-- cis:technical-intent-business-evidence:end -->";
    private const string SurfaceEvidenceStart = "<!-- cis:technical-intent-surface-evidence:start -->";
    private const string SurfaceEvidenceEnd = "<!-- cis:technical-intent-surface-evidence:end -->";
    private const string StandardsEvidenceStart = "<!-- cis:technical-intent-standards-evidence:start -->";
    private const string StandardsEvidenceEnd = "<!-- cis:technical-intent-standards-evidence:end -->";
    private const string QuestionnaireEvidenceStart = "<!-- cis:technical-intent-questionnaire-evidence:start -->";
    private const string QuestionnaireEvidenceEnd = "<!-- cis:technical-intent-questionnaire-evidence:end -->";
    private const string ComponentMapStart = "<!-- cis:technical-intent-component-map:start -->";
    private const string ComponentMapEnd = "<!-- cis:technical-intent-component-map:end -->";
    private const string ModuleArchitectureStart = "<!-- cis:technical-intent-module-architecture:start -->";
    private const string ModuleArchitectureEnd = "<!-- cis:technical-intent-module-architecture:end -->";
    private const string IntegrationPointStart = "<!-- cis:technical-intent-integration-points:start -->";
    private const string IntegrationPointEnd = "<!-- cis:technical-intent-integration-points:end -->";
    private const string DecisionEvidenceStart = "<!-- cis:technical-intent-decision-evidence:start -->";
    private const string DecisionEvidenceEnd = "<!-- cis:technical-intent-decision-evidence:end -->";

    private static readonly string[][] RequiredSectionAliases =
    [
        ["Approved business baseline"],
        ["Design goals and principles"],
        ["Technical surface and ownership", "Detected technical surface"],
        ["Runtime architecture and trust boundaries", "Architecture and boundaries"],
        ["Application model and invariants", "Data and consistency"],
        ["API, integration, and compatibility intent", "Integration and contracts"],
        ["Security and privacy intent", "Security and privacy"],
        ["Operations, migration, and recovery intent", "Operations and observability"],
        ["Quality attributes and verification direction", "Quality attributes"],
        ["Decisions and delivery constraints"],
        ["Open technical decisions"],
    ];

    private static readonly string[] ArchitectureGuidelineAliases =
    [
        "Architecture guidelines and applicable standards",
        "Architecture guidelines and standards",
    ];

    private readonly BrdService _brd;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ICisGraphSnapshotReader _graphReader;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly ICisWorkspaceRegistry _workspaceRegistry;

    public TechnicalIntentService(
        ICisWorkspaceRegistry workspaceRegistry,
        ICisRepositoryContextResolver repositoryResolver,
        ICisGraphSnapshotReader graphReader,
        BrdService brd,
        DocumentationCatalogMerger catalogMerger,
        Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _graphReader = graphReader;
        _brd = brd;
        _catalogMerger = catalogMerger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TechnicalIntentResult Initialize(string workspacePath)
        => InitializeCore(workspacePath, existingDraft: false);

    public CisTechnicalIntentDraftPreparation PrepareExistingDraft(string workspacePath)
    {
        var state = ResolveState(workspacePath);
        if (state.Errors.Count > 0) return new(state.Errors);
        if (state.Brd is null) return new(["A canonical BRD is required before existing-system technical discovery."]);
        if (!state.Surfaces.Any(item => item.Participation == "owned" && item.Component != "unclassified"
                && item.Roles.Any(role => role != "documentation")))
            return new(["Existing-system technical discovery requires a classified product-owned implementation."]);
        if (File.Exists(state.CanonicalPath) && ReadFrontMatter(File.ReadAllText(state.CanonicalPath), "status") is not ("Draft" or "Review Required"))
            return new(["Existing-system inference may update only Draft or Review Required technical intent."]);
        var questionnaire = new TechnicalIntentQuestionnaireService(_workspaceRegistry, _repositoryResolver, _brd, _catalogMerger, _clock)
            .InitializeForDiscovery(workspacePath);
        if (questionnaire.Errors.Count > 0) return new(questionnaire.Errors);
        var result = InitializeCore(workspacePath, existingDraft: true);
        return new(result.Errors);
    }

    private TechnicalIntentResult InitializeCore(string workspacePath, bool existingDraft)
    {
        var state = ResolveState(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state);
        var blocking = existingDraft ? state.ReadinessErrors.Where(error => !error.StartsWith("An Active, current BRD", StringComparison.Ordinal)
            && !error.StartsWith("The high-level technical questionnaire", StringComparison.Ordinal)).ToArray() : state.ReadinessErrors.ToArray();
        if (blocking.Length > 0) return Error("blocked", state, blocking);

        var stableId = $"{state.Authority!.Id}:spec:technical-intent";
        var canonicalPath = state.CanonicalPath!;
        var relativePath = NormalizePath(Path.GetRelativePath(state.Authority.RepositoryPath, canonicalPath));
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var merge = _catalogMerger.Merge(
            state.Authority.Id,
            catalog,
            [new CatalogArtifactEntry(stableId, relativePath, "repository-specification", "draft", "canonical")]);
        if (merge.Collisions.Count > 0)
            return new TechnicalIntentResult("collision", state.Workspace!.WorkspacePath, state.Authority.Id,
                relativePath, null, state.Baselines, state.Warnings, merge.Collisions, false);

        string content;
        string? existing = null;
        var approvalCanCarryForward = false;
        if (!File.Exists(canonicalPath))
        {
            content = RenderNew(state.Authority.Id, stableId, state);
        }
        else
        {
            existing = CisTechnicalIntentPresentation.RestoreManagedEvidence(File.ReadAllText(canonicalPath));
            content = existing;
            if (!content.Contains($"stable_id: {stableId}", StringComparison.Ordinal))
                return Error("collision", state, "The canonical technical-intent path contains a document with a different stable identity.");
            content = EnsureMetadata(content);
            content = ReplaceOrInsertBaseline(content, state.Baselines);
            if (!content.Contains("<!-- cis:technical-intent-implementation-authored -->", StringComparison.Ordinal)
                && (state.ScaffoldEligible || IsUpgradeableGeneratedSchema3Draft(content)))
                content = EnrichStarter(content, state);
            content = RefreshDerivedEvidence(content, state);
            approvalCanCarryForward = HasCurrentApproval(existing)
                && BaselinesCanCarryForward(existing, state.Baselines, state.Brd?.Content);
        }

        var changed = existing is null || !Equivalent(existing, content);
        if (changed)
        {
            if (approvalCanCarryForward)
                content = ReplaceNestedFrontMatter(content, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(content)));
            else
            {
                content = ReplaceFrontMatter(content, "status", "Draft");
                content = ReplaceFrontMatter(content, "last_reviewed", "null");
                content = ReplaceNestedFrontMatter(content, "approved_by", "null");
                content = ReplaceNestedFrontMatter(content, "approved_at", "null");
                content = ReplaceNestedFrontMatter(content, "approval_reason", "null");
                content = ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
            }
        }

        var nextCatalog = changed
            ? UpdateCatalogStatus(merge.Content, stableId, approvalCanCarryForward ? "active" : "draft")
            : merge.Content;
        var catalogChanged = !Equivalent(catalog, nextCatalog);
        if (!changed && !catalogChanged)
            return ValidateInternal(workspacePath, "unchanged", applied: false);

        Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);
        Write(canonicalPath, content);
        if (catalogChanged) Write(state.Context.CatalogPath, nextCatalog);
        return ValidateInternal(workspacePath, "initialized", applied: true);
    }

    public TechnicalIntentResult Validate(string workspacePath)
        => ValidateInternal(workspacePath, "validated", applied: false);

    public TechnicalIntentResult Status(string workspacePath)
        => CisReadScope.Read(this, nameof(Status), workspacePath,
            () => ValidateInternal(workspacePath, "status", applied: false));

    public TechnicalIntentResult Approve(string workspacePath, string reviewer, string reason)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return new TechnicalIntentResult("invalid", workspacePath, null, null, null, [], [],
                ["Reviewer and approval reason are required."], false);

        var assessed = Validate(workspacePath);
        if (assessed.Errors.Count > 0 || assessed.Validation is not { Valid: true, Current: true })
            return assessed with { Status = "blocked" };

        var state = ResolveState(workspacePath);
        var path = state.CanonicalPath!;
        var content = File.ReadAllText(path);
        if (string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadNestedFrontMatter(content, "approved_by"), reviewer.Trim(), StringComparison.Ordinal)
            && string.Equals(ReadNestedFrontMatter(content, "approval_reason"), reason.Trim(), StringComparison.Ordinal)
            && assessed.Validation.EffectiveStatus == "Active")
            return assessed with { Status = "unchanged" };

        var now = _clock();
        content = ReplaceOrInsertBaseline(content, state.Baselines);
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNestedFrontMatter(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNestedFrontMatter(content, "approval_reason", JsonSerializer.Serialize(reason.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(content)));
        Write(path, content);

        var stableId = $"{state.Authority!.Id}:spec:technical-intent";
        var catalog = UpdateCatalogStatus(File.ReadAllText(state.Context!.CatalogPath), stableId, "active");
        Write(state.Context.CatalogPath, catalog);
        return ValidateInternal(workspacePath, "approved", applied: true);
    }

    public ChangeReadinessResult Evaluate(string repositoryPath)
    {
        var workspaceMarker = Path.Combine(Path.GetFullPath(repositoryPath), ".cis", "workspace.yml");
        if (!File.Exists(workspaceMarker))
            return new ChangeReadinessResult("technical-intent", Applicable: false, Ready: true, []);

        var result = Status(repositoryPath);
        var ready = result.Validation is { } validation
            && CisDefinitionDraftScope.Accepts(validation.Valid, validation.Current, validation.EffectiveStatus);
        var errors = ready
            ? []
            : result.Errors.Concat(result.Validation?.Errors ?? [])
                .Append("An Active, current workspace technical intent is required. Run `cis technical-intent status`.")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        return new ChangeReadinessResult("technical-intent", Applicable: true, ready, errors);
    }

    private TechnicalIntentResult ValidateInternal(string workspacePath, string operation, bool applied)
    {
        var state = ResolveState(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state);
        var relativePath = NormalizePath(Path.GetRelativePath(state.Authority!.RepositoryPath, state.CanonicalPath!));
        if (!File.Exists(state.CanonicalPath))
            return new TechnicalIntentResult("missing", state.Workspace!.WorkspacePath, state.Authority.Id,
                relativePath, new TechnicalIntentValidation(false, false, "Missing", "Missing",
                    ["Canonical technical intent was not found. Run `cis technical-intent init`."], state.Warnings),
                state.Baselines, state.Warnings, [], false);

        var document = File.ReadAllText(state.CanonicalPath);
        var content = CisTechnicalIntentPresentation.RestoreManagedEvidence(document);
        var errors = new List<string>();
        var warnings = new List<string>(state.Warnings);
        var stableId = $"{state.Authority.Id}:spec:technical-intent";
        if (!content.Contains($"stable_id: {stableId}", StringComparison.Ordinal))
            errors.Add("Canonical technical-intent stable identity is missing or incorrect.");
        if (!string.Equals(ReadFrontMatter(content, "scope"), "Workspace", StringComparison.OrdinalIgnoreCase))
            errors.Add("Authority technical intent must declare `scope: Workspace`.");
        var schema = ReadNestedFrontMatter(content, "technical_intent_schema");
        if (schema is not ("1" or "2" or "3" or "4"))
            errors.Add("Technical-intent schema metadata is missing or unsupported.");
        if (!content.Contains(BaselineStart, StringComparison.Ordinal) || !content.Contains(BaselineEnd, StringComparison.Ordinal))
            errors.Add("Managed technical-intent baseline is missing. Run `cis technical-intent init`.");

        foreach (var aliases in RequiredSectionAliases)
        {
            var body = aliases.Select(alias => ExtractSection(content, alias)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(body))
                errors.Add($"Required technical-intent section is missing or empty: {string.Join(" or ", aliases)}");
            else if (PlaceholderPattern().IsMatch(body))
                errors.Add($"Required technical-intent section still contains a placeholder: {aliases[0]}");
        }

        if (schema is "2" or "3" or "4")
        {
            var body = ArchitectureGuidelineAliases
                .Select(alias => ExtractSection(content, alias))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(body))
                errors.Add($"Required technical-intent section is missing or empty: {ArchitectureGuidelineAliases[0]}");
            else if (PlaceholderPattern().IsMatch(body))
                errors.Add($"Required technical-intent section still contains a placeholder: {ArchitectureGuidelineAliases[0]}");
            foreach (var (start, end, label) in new[]
                     {
                         (BusinessEvidenceStart, BusinessEvidenceEnd, "business evidence"),
                         (SurfaceEvidenceStart, SurfaceEvidenceEnd, "technical-surface evidence"),
                         (StandardsEvidenceStart, StandardsEvidenceEnd, "standards evidence"),
                     })
                if (!content.Contains(start, StringComparison.Ordinal) || !content.Contains(end, StringComparison.Ordinal))
                    errors.Add($"Managed technical-intent {label} is missing. Run `cis technical-intent init`.");
        }

        if (schema is "3" or "4")
        {
            foreach (var heading in new[] { "High-level technology and architecture choices", "Component and interaction map" })
            {
                var body = ExtractSection(content, heading);
                if (string.IsNullOrWhiteSpace(body)) errors.Add($"Required technical-intent section is missing or empty: {heading}");
            }
            foreach (var (start, end, label) in new[]
                     {
                         (QuestionnaireEvidenceStart, QuestionnaireEvidenceEnd, "questionnaire evidence"),
                         (ComponentMapStart, ComponentMapEnd, "component and interaction map"),
                         (DecisionEvidenceStart, DecisionEvidenceEnd, "questionnaire-derived decisions"),
                     })
                if (!content.Contains(start, StringComparison.Ordinal) || !content.Contains(end, StringComparison.Ordinal))
                    errors.Add($"Managed technical-intent {label} is missing. Run `cis technical-intent init`.");
        }

        if (schema == "4")
        {
            foreach (var heading in new[] { "Product module architecture", "Integration point catalog" })
            {
                var body = ExtractSection(content, heading);
                if (string.IsNullOrWhiteSpace(body)) errors.Add($"Required technical-intent section is missing or empty: {heading}");
                else if (PlaceholderPattern().IsMatch(body)) errors.Add($"Required technical-intent section still contains a placeholder: {heading}");
            }
            foreach (var (start, end, label) in new[]
                     {
                         (ModuleArchitectureStart, ModuleArchitectureEnd, "product module architecture"),
                         (IntegrationPointStart, IntegrationPointEnd, "integration-point catalog"),
                     })
                if (!content.Contains(start, StringComparison.Ordinal) || !content.Contains(end, StringComparison.Ordinal))
                    errors.Add($"Managed technical-intent {label} is missing. Run `cis technical-intent init`.");

            var moduleArchitecture = ReadBlock(content, ModuleArchitectureStart, ModuleArchitectureEnd);
            foreach (var required in new[] { "### Proposed module tree", "### Module ownership summary", "### Module ownership rules", "### Module responsibility profiles", "**Purpose:**", "**Data and state:**", "**Failure and recovery:**", "**Verification:**" })
                if (!moduleArchitecture.Contains(required, StringComparison.Ordinal))
                    errors.Add($"Product module architecture is incomplete: missing {required}.");
            if (!Regex.IsMatch(moduleArchitecture, @"\bTI-MOD-[A-Z0-9-]+\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                errors.Add("Product module architecture contains no stable `TI-MOD-*` identity.");

            var integrationCatalog = ReadBlock(content, IntegrationPointStart, IntegrationPointEnd);
            foreach (var required in new[] { "| Integration | Source | Trigger and flow | Target |", "### Integration governance", "Trust and authorization", "Failure and recovery" })
                if (!integrationCatalog.Contains(required, StringComparison.Ordinal))
                    errors.Add($"Integration-point catalog is incomplete: missing {required}.");
            if (!Regex.IsMatch(integrationCatalog, @"\bTI-INT-[A-Z0-9-]+\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                errors.Add("Integration-point catalog contains no stable `TI-INT-*` identity.");
        }

        var decisionSection = ExtractSection(content, "Open technical decisions");
        var decisions = new List<CisTechnicalDecisionReview>();
        var documentRows = document.Split('\n').Select((line, index) => (Cells: Cells(line), Line: index + 1))
            .Where(row => row.Cells.Length == 5 && row.Cells[0].StartsWith("TI-DEC-", StringComparison.Ordinal))
            .ToLookup(row => row.Cells[0], StringComparer.Ordinal);
        foreach (var decision in ParseDecisionRows(decisionSection))
        {
            var findings = new List<string>();
            if (decision.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)
                || decision.Status.Equals("Proposed", StringComparison.OrdinalIgnoreCase))
                findings.Add($"Technical decision remains unresolved: {decision.Id}");
            else if (decision.Status.Equals("Deferred", StringComparison.OrdinalIgnoreCase)
                     && decision.RequiredBefore.Contains("change dossier", StringComparison.OrdinalIgnoreCase))
                findings.Add($"Technical decision cannot be deferred beyond its required gate: {decision.Id}");
            else if (decision.Status is not ("Resolved" or "Accepted" or "Deferred"))
                findings.Add($"Technical decision has unsupported status '{decision.Status}': {decision.Id}");
            if (string.IsNullOrWhiteSpace(decision.Resolution) || PlaceholderPattern().IsMatch(decision.Resolution))
                findings.Add($"Technical decision requires a resolution or deferral rationale: {decision.Id}");
            var locations = documentRows[decision.Id].ToArray();
            if (locations.Length != 1) findings.Add($"Technical decision row is ambiguous: {decision.Id}");
            errors.AddRange(findings);
            decisions.Add(DescribeDecision(new(decision.Id, decision.Decision, decision.RequiredBefore, decision.Status,
                decision.Resolution, findings.Count > 0, locations.Length == 1 ? locations[0].Line : null, findings), state, document));
        }

        var recorded = ParseBaselines(content);
        var current = state.ReadinessErrors.Count == 0;
        warnings.AddRange(state.ReadinessErrors);
        foreach (var baseline in state.Baselines)
        {
            var key = baseline.Kind + "\u001f" + baseline.Id;
            if (!recorded.TryGetValue(key, out var version) || version != baseline.Version)
            {
                warnings.Add($"Technical-intent baseline differs from {baseline.Kind} '{baseline.Id}'. Human review is required.");
                current = false;
            }
        }
        foreach (var key in recorded.Keys.Except(state.Baselines.Select(item => item.Kind + "\u001f" + item.Id), StringComparer.Ordinal))
        {
            warnings.Add($"Technical-intent baseline references an unregistered source: {key.Replace("\u001f", "/", StringComparison.Ordinal)}");
            current = false;
        }

        var documentStatus = ReadFrontMatter(content, "status") ?? "Unknown";
        if (documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            var approvedHash = ReadNestedFrontMatter(content, "approved_content_hash");
            if (string.IsNullOrWhiteSpace(approvedHash) || approvedHash == "null" || !ApprovalDigestMatches(content, approvedHash))
            {
                warnings.Add("Technical-intent content changed after approval.");
                current = false;
            }
            if (string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approved_by"))
                || ReadNestedFrontMatter(content, "approved_by") == "null"
                || string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approval_reason"))
                || ReadNestedFrontMatter(content, "approval_reason") == "null")
                errors.Add("Active technical intent requires approval identity and rationale.");
        }

        var valid = errors.Count == 0;
        var effective = documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? valid && current ? "Active" : "Stale"
            : valid && current ? "Ready for Approval" : "Review Required";
        var validation = new TechnicalIntentValidation(valid, current, effective, documentStatus,
            errors.Distinct(StringComparer.Ordinal).Order().ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order().ToArray()) { Decisions = decisions };
        return new TechnicalIntentResult(operation, state.Workspace!.WorkspacePath, state.Authority.Id,
            relativePath, validation, state.Baselines, validation.Warnings, [], applied);
    }

    private State ResolveState(string workspacePath)
    {
        using var graphReads = GraphReadScope.Enter();
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
            return new State(null, null, null, null, [], [], [], null, null, false, [], resolution.Errors, []);
        var workspace = resolution.Workspace;
        var authority = workspace.AuthorityRepository;
        if (authority is null)
            return new State(workspace, null, null, null, [], [], [], null, null, false, [], ["Workspace has no authority repository."], []);
        var contextResolution = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!contextResolution.IsSuccess || contextResolution.Context is null)
            return new State(workspace, authority, null, null, [], [], [], null, null, false, [], contextResolution.Errors, []);

        var warnings = new List<string>();
        var errors = new List<string>();
        var readinessErrors = new List<string>();
        var baselines = new List<TechnicalIntentBaseline>();
        var brdResult = _brd.Status(workspacePath);
        var brdReady = brdResult.Validation is { } brdValidation
            && CisDefinitionDraftScope.Accepts(brdValidation.Valid, brdValidation.Current, brdValidation.EffectiveStatus);
        if (!brdReady)
            readinessErrors.Add("An Active, current BRD is required before technical intent. Run `cis brd status`.");
        var brdPath = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "specs", "business-requirements.md");
        BusinessEvidence? brd = null;
        if (File.Exists(brdPath))
        {
            var brdContent = File.ReadAllText(brdPath);
            brd = ReadBusinessEvidence(authority, brdPath, brdContent);
            baselines.Add(new TechnicalIntentBaseline("brd", $"{authority.Id}:spec:business-requirements", "semantic-v1:" + BrdDocumentDigest.Compute(brdContent),
                $"{brdResult.Validation?.EffectiveStatus ?? "Unknown"} canonical BRD"));
        }

        var questionnairePath = Path.Combine(authority.RepositoryPath,
            authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "specs", "technical-intent-questionnaire.md");
        var expectedBrdVersion = brd is null ? string.Empty : brd.Digest;
        var questionnaire = TechnicalIntentQuestionnaireService.ReadSnapshot(questionnairePath, expectedBrdVersion);
        if (brdReady)
        {
            if (questionnaire is null)
                readinessErrors.Add("The high-level technical questionnaire is missing. Run `cis technical-intent questions init`.");
            else if (!questionnaire.Current)
                readinessErrors.Add("The high-level technical questionnaire is stale against the Active BRD. Reinitialize and review its answers.");
            else if (!questionnaire.Complete)
                readinessErrors.Add("The high-level technical questionnaire is incomplete. Answer every `TI-Q-*` item before generating technical intent.");
        }
        if (questionnaire is { Current: true, Complete: true })
            baselines.Add(new TechnicalIntentBaseline("questionnaire", $"{authority.Id}:spec:technical-intent-questionnaire",
                questionnaire.Digest, $"{questionnaire.Questions.Count} governed human technical choices"));

        var surfaces = new List<TechnicalSurface>();
        var standards = new List<KnownStandard>();
        var classifier = new RepositoryClassifier();

        foreach (var repository in workspace.Repositories.Where(repository =>
                     brdResult.Discovery?.DeferredRepositoryIds.Contains(repository.Id, StringComparer.Ordinal) != true))
        {
            try
            {
                var classification = classifier.Classify(repository.RepositoryPath);
                warnings.AddRange(classification.Warnings.Select(warning => $"Repository '{repository.Id}' classification: {warning}"));
                var classifiedComponents = repository.Components.Count == 0
                    ? classification.Components
                    : classification.Components.Where(component => repository.Components.Contains(component.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
                if (repository.Components.Count > 0 && classifiedComponents.Count == 0)
                    readinessErrors.Add($"Repository '{repository.Id}' declares component scope {string.Join(", ", repository.Components)}, but none matched its current classification.");
                if (classifiedComponents.Count == 0)
                {
                    surfaces.Add(new TechnicalSurface(repository.Id, repository.Role, repository.Participation,
                        repository.Relationship, "unclassified", ".", [], [], [], [], "unknown"));
                }
                else
                {
                    surfaces.AddRange(classifiedComponents.Select(component => new TechnicalSurface(
                        repository.Id,
                        repository.Role,
                        repository.Participation,
                        repository.Relationship,
                        component.Id,
                        component.Root,
                        component.Languages,
                        component.Frameworks,
                        component.Roles,
                        component.Capabilities,
                        component.Confidence)));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Repository '{repository.Id}' could not be classified for the technical-intent scaffold: {exception.Message}");
                surfaces.Add(new TechnicalSurface(repository.Id, repository.Role, repository.Participation,
                    repository.Relationship, "unclassified", ".", [], [], [], [], "unknown"));
            }

            standards.AddRange(DiscoverStandards(repository, warnings));

            var snapshot = _graphReader.ReadMetadata(repository.RepositoryPath);
            if (snapshot.Build is null)
            {
                if (repository.Role == "participant") readinessErrors.Add($"Repository '{repository.Id}' graph is unavailable. Run `cis graph build --workspace {workspace.WorkspacePath}`.");
                else warnings.Add($"Authority repository graph is unavailable: {repository.Id}.");
                continue;
            }
            if (repository.Role == "participant")
            {
                baselines.Add(new TechnicalIntentBaseline("repository", repository.Id, snapshot.Build.Id,
                    snapshot.Build.Head ?? "uncommitted"));
                if (snapshot.Freshness != "fresh") readinessErrors.Add($"Repository '{repository.Id}' graph is {snapshot.Freshness}. Rebuild the workspace.");
            }
            else if (snapshot.Freshness != "fresh")
                warnings.Add($"Authority repository graph is {snapshot.Freshness}; rebuild after canonical edits.");
        }

        var canonical = Path.Combine(authority.RepositoryPath,
            authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "specs", "technical-intent-spec.md");
        var existing = File.Exists(canonical) ? File.ReadAllText(canonical) : null;
        var scaffoldEligible = existing is null || IsScaffoldEligible(existing) || IsGeneratedDraft(existing);
        if (scaffoldEligible || ReadNestedFrontMatter(existing ?? string.Empty, "technical_intent_schema") is "2" or "3" or "4")
        {
            baselines.AddRange(standards
                .Where(item => item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                .Select(item => new TechnicalIntentBaseline("standard", item.Id, item.Digest,
                    $"{item.RepositoryId}: {item.Title} ({item.Path})")));
        }

        return new State(workspace, authority, contextResolution.Context, canonical,
            baselines.OrderBy(item => item.Kind).ThenBy(item => item.Id).ToArray(),
            surfaces.OrderBy(item => item.RepositoryId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Root, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Component, StringComparer.OrdinalIgnoreCase).ToArray(),
            standards.DistinctBy(item => item.RepositoryId + "\u001f" + item.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item.RepositoryId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            brd, questionnaire,
            scaffoldEligible,
            warnings.Distinct(StringComparer.Ordinal).ToArray(), errors.Distinct(StringComparer.Ordinal).ToArray(),
            readinessErrors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string RenderNew(string authorityId, string stableId, State state)
        => $"""
---
title: "{authorityId} Technical Intent"
type: specification
status: Draft
scope: Workspace
owner: Product owner and repository maintainers
last_reviewed: null
review_cadence: on architecture or approved-requirement change
cis:
  stable_id: {stableId}
  technical_intent_schema: 4
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# {authorityId} Technical Intent

## CIS technical baseline

{BaselineStart}
{RenderBaselines(state.Baselines)}{BaselineEnd}

## Approved business baseline

{RenderManagedEvidence(BusinessEvidenceStart, RenderBusinessBaseline(state.Brd), BusinessEvidenceEnd)}

## High-level technology and architecture choices

{RenderManagedEvidence(QuestionnaireEvidenceStart, RenderQuestionnaireChoices(state), QuestionnaireEvidenceEnd)}

## Design goals and principles

{RenderDesignGoals(state)}

## Technical surface and ownership

{RenderManagedEvidence(SurfaceEvidenceStart, RenderTechnicalSurface(state.Surfaces), SurfaceEvidenceEnd)}

## Component and interaction map

{RenderManagedEvidence(ComponentMapStart, RenderComponentMap(state), ComponentMapEnd)}

## Product module architecture

{RenderManagedEvidence(ModuleArchitectureStart, RenderProductModuleArchitecture(state), ModuleArchitectureEnd)}

## Integration point catalog

{RenderManagedEvidence(IntegrationPointStart, RenderIntegrationPointCatalog(state), IntegrationPointEnd)}

## Architecture guidelines and applicable standards

{RenderArchitectureGuidelines(state)}

## Runtime architecture and trust boundaries

{RenderRuntimeArchitecture(state)}

## Application model and invariants

{RenderApplicationModel(state)}

## API, integration, and compatibility intent

{RenderIntegrationIntent(state)}

## Security and privacy intent

{RenderSecurityIntent(state)}

## Operations, migration, and recovery intent

{RenderOperationsIntent(state)}

## Quality attributes and verification direction

{RenderQualityIntent(state)}

## Decisions and delivery constraints

{RenderDeliveryConstraints(state)}

## Open technical decisions

{RenderManagedEvidence(DecisionEvidenceStart, RenderOpenDecisions(state), DecisionEvidenceEnd)}
""";

    private static string RefreshDerivedEvidence(string content, State state)
    {
        var implementationAuthored = content.Contains("<!-- cis:technical-intent-implementation-authored -->", StringComparison.Ordinal);
        if (content.Contains(BusinessEvidenceStart, StringComparison.Ordinal)
            && content.Contains(BusinessEvidenceEnd, StringComparison.Ordinal))
            content = ReplaceBlock(content, BusinessEvidenceStart, BusinessEvidenceEnd, RenderBusinessBaseline(state.Brd));
        if (content.Contains(SurfaceEvidenceStart, StringComparison.Ordinal)
            && content.Contains(SurfaceEvidenceEnd, StringComparison.Ordinal))
            content = ReplaceBlock(content, SurfaceEvidenceStart, SurfaceEvidenceEnd, RenderTechnicalSurface(state.Surfaces));
        if (content.Contains(StandardsEvidenceStart, StringComparison.Ordinal)
            && content.Contains(StandardsEvidenceEnd, StringComparison.Ordinal))
            content = ReplaceBlock(content, StandardsEvidenceStart, StandardsEvidenceEnd, RenderStandardsEvidence(state));
        if (state.Questionnaire is { Current: true, Complete: true })
        {
            if (content.Contains(QuestionnaireEvidenceStart, StringComparison.Ordinal)
                && content.Contains(QuestionnaireEvidenceEnd, StringComparison.Ordinal))
                content = ReplaceBlock(content, QuestionnaireEvidenceStart, QuestionnaireEvidenceEnd, RenderQuestionnaireChoices(state));
            if (!implementationAuthored && content.Contains(ComponentMapStart, StringComparison.Ordinal)
                && content.Contains(ComponentMapEnd, StringComparison.Ordinal))
                content = ReplaceBlock(content, ComponentMapStart, ComponentMapEnd, RenderComponentMap(state));
            if (!implementationAuthored && content.Contains(ModuleArchitectureStart, StringComparison.Ordinal)
                && content.Contains(ModuleArchitectureEnd, StringComparison.Ordinal))
                content = ReplaceBlock(content, ModuleArchitectureStart, ModuleArchitectureEnd, RenderProductModuleArchitecture(state));
            if (!implementationAuthored && content.Contains(IntegrationPointStart, StringComparison.Ordinal)
                && content.Contains(IntegrationPointEnd, StringComparison.Ordinal))
                content = ReplaceBlock(content, IntegrationPointStart, IntegrationPointEnd, RenderIntegrationPointCatalog(state));
            if (!implementationAuthored && content.Contains(DecisionEvidenceStart, StringComparison.Ordinal)
                && content.Contains(DecisionEvidenceEnd, StringComparison.Ordinal))
                content = ReplaceBlock(content, DecisionEvidenceStart, DecisionEvidenceEnd, RenderOpenDecisions(state));
        }
        return content;
    }

    private static string RenderManagedEvidence(string start, string body, string end)
        => $"{start}\n{body.Trim()}\n{end}";

    private static string EnrichStarter(string content, State state)
    {
        var generatedSchema2 = IsGeneratedDraft(content);
        content = ReplaceNestedFrontMatter(content, "technical_intent_schema", "4");
        content = content.Replace(
            "This is the workspace technical authority derived from the active business requirements. Run `cis technical-intent init` to bind its reviewed baselines.",
            "This Draft workspace technical authority was scaffolded from the Active BRD, registered repository classifications, Active standards, and current graph evidence. Generated direction remains subject to technical review and explicit approval.",
            StringComparison.Ordinal);
        content = UpsertStarterSection(content, ["Approved business baseline"], "Approved business baseline",
            RenderManagedEvidence(BusinessEvidenceStart, RenderBusinessBaseline(state.Brd), BusinessEvidenceEnd),
            ["High-level technology and architecture choices", "Design goals and principles"]);
        content = UpsertStarterSection(content, ["High-level technology and architecture choices"],
            "High-level technology and architecture choices",
            RenderManagedEvidence(QuestionnaireEvidenceStart, RenderQuestionnaireChoices(state), QuestionnaireEvidenceEnd),
            ["Design goals and principles"]);
        content = UpsertStarterSection(content, ["Design goals and principles"], "Design goals and principles",
            RenderDesignGoals(state), ["Technical surface and ownership", "Detected technical surface"]);
        content = UpsertStarterSection(content, ["Technical surface and ownership", "Detected technical surface"],
            "Technical surface and ownership",
            RenderManagedEvidence(SurfaceEvidenceStart, RenderTechnicalSurface(state.Surfaces), SurfaceEvidenceEnd),
            ["Component and interaction map", "Architecture guidelines and applicable standards", "Architecture guidelines and standards", "Runtime architecture and trust boundaries", "Architecture and boundaries"]);
        content = UpsertStarterSection(content, ["Component and interaction map"], "Component and interaction map",
            RenderManagedEvidence(ComponentMapStart, RenderComponentMap(state), ComponentMapEnd),
            ["Product module architecture", "Integration point catalog", "Architecture guidelines and applicable standards", "Architecture guidelines and standards", "Runtime architecture and trust boundaries", "Architecture and boundaries"]);
        content = UpsertStarterSection(content, ["Product module architecture"], "Product module architecture",
            RenderManagedEvidence(ModuleArchitectureStart, RenderProductModuleArchitecture(state), ModuleArchitectureEnd),
            ["Integration point catalog", "Architecture guidelines and applicable standards", "Architecture guidelines and standards", "Runtime architecture and trust boundaries", "Architecture and boundaries"]);
        content = UpsertStarterSection(content, ["Integration point catalog"], "Integration point catalog",
            RenderManagedEvidence(IntegrationPointStart, RenderIntegrationPointCatalog(state), IntegrationPointEnd),
            ["Architecture guidelines and applicable standards", "Architecture guidelines and standards", "Runtime architecture and trust boundaries", "Architecture and boundaries"]);
        content = UpsertStarterSection(content, ArchitectureGuidelineAliases,
            "Architecture guidelines and applicable standards", RenderArchitectureGuidelines(state),
            ["Runtime architecture and trust boundaries", "Architecture and boundaries"]);
        content = UpsertStarterSection(content, ["Runtime architecture and trust boundaries", "Architecture and boundaries"],
            "Runtime architecture and trust boundaries", RenderRuntimeArchitecture(state),
            ["Application model and invariants", "Data and consistency"]);
        content = UpsertStarterSection(content, ["Application model and invariants", "Data and consistency"],
            "Application model and invariants", RenderApplicationModel(state),
            ["API, integration, and compatibility intent", "Integration and contracts"]);
        content = UpsertStarterSection(content, ["API, integration, and compatibility intent", "Integration and contracts"],
            "API, integration, and compatibility intent", RenderIntegrationIntent(state),
            ["Security and privacy intent", "Security and privacy"]);
        content = UpsertStarterSection(content, ["Security and privacy intent", "Security and privacy"],
            "Security and privacy intent", RenderSecurityIntent(state),
            ["Operations, migration, and recovery intent", "Operations and observability"]);
        content = UpsertStarterSection(content, ["Operations, migration, and recovery intent", "Operations and observability"],
            "Operations, migration, and recovery intent", RenderOperationsIntent(state),
            ["Quality attributes and verification direction", "Quality attributes"]);
        content = UpsertStarterSection(content, ["Quality attributes and verification direction", "Quality attributes"],
            "Quality attributes and verification direction", RenderQualityIntent(state),
            ["Decisions and delivery constraints"]);
        content = UpsertStarterSection(content, ["Decisions and delivery constraints"],
            "Decisions and delivery constraints", RenderDeliveryConstraints(state), ["Open technical decisions"]);
        content = UpsertStarterSection(content, ["Open technical decisions"], "Open technical decisions",
            RenderManagedEvidence(DecisionEvidenceStart, RenderOpenDecisions(state), DecisionEvidenceEnd), []);
        return generatedSchema2 && !content.Contains(DecisionEvidenceStart, StringComparison.Ordinal)
            ? ReplaceSectionBody(content, "Open technical decisions",
                RenderManagedEvidence(DecisionEvidenceStart, RenderOpenDecisions(state), DecisionEvidenceEnd))
            : content;
    }

    private static string ReplaceSectionBody(string content, string heading, string body)
    {
        var match = Regex.Match(content,
            $"(?ms)^## {Regex.Escape(heading)}\\s*$\\n(?<body>.*?)(?=^## |\\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return content;
        var replacement = $"## {heading}\n\n{body.Trim()}\n\n";
        return content[..match.Index] + replacement + content[(match.Index + match.Length)..].TrimStart('\r', '\n');
    }

    private static string UpsertStarterSection(
        string content,
        IReadOnlyList<string> aliases,
        string heading,
        string body,
        IReadOnlyList<string> beforeAliases)
    {
        foreach (var alias in aliases)
        {
            var match = Regex.Match(content,
                $"(?ms)^## {Regex.Escape(alias)}\\s*$\\n(?<body>.*?)(?=^## |\\z)",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success) continue;
            if (!IsRecognizedStarterBody(match.Groups["body"].Value)) return content;
            var replacement = $"## {heading}\n\n{body.Trim()}\n\n";
            return content[..match.Index] + replacement + content[(match.Index + match.Length)..].TrimStart('\r', '\n');
        }

        var section = $"## {heading}\n\n{body.Trim()}\n\n";
        foreach (var before in beforeAliases)
        {
            var index = content.IndexOf($"## {before}", StringComparison.Ordinal);
            if (index >= 0) return content.Insert(index, section);
        }
        return content.TrimEnd() + "\n\n" + section;
    }

    private static bool IsScaffoldEligible(string content)
    {
        if (string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)) return false;
        var recognized = RequiredSectionAliases
            .SelectMany(aliases => aliases)
            .Select(alias => ExtractSection(content, alias))
            .Where(body => body.Length > 0)
            .Count(IsRecognizedStarterBody);
        return recognized >= 3;
    }

    private static bool IsGeneratedDraft(string content)
        => !string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
           && ReadNestedFrontMatter(content, "technical_intent_schema") == "2"
           && content.Contains(BusinessEvidenceStart, StringComparison.Ordinal)
           && content.Contains(SurfaceEvidenceStart, StringComparison.Ordinal)
           && content.Contains(StandardsEvidenceStart, StringComparison.Ordinal)
           && ExtractSection(content, "Open technical decisions").Contains(
               "Initialization proposes decision questions", StringComparison.Ordinal);

    private static bool IsUpgradeableGeneratedSchema3Draft(string content)
        => !string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
           && ReadNestedFrontMatter(content, "technical_intent_schema") == "3"
           && content.Contains(QuestionnaireEvidenceStart, StringComparison.Ordinal)
           && content.Contains(ComponentMapStart, StringComparison.Ordinal)
           && content.Contains(DecisionEvidenceStart, StringComparison.Ordinal);

    private static bool IsRecognizedStarterBody(string body)
    {
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0 || normalized.Equals("TODO", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalized.Equals("- Record durable choices as ADRs and link them here.", StringComparison.Ordinal)) return true;
        if (!PlaceholderPattern().IsMatch(normalized)) return false;
        if (!normalized.Contains('\n'))
            return normalized.StartsWith("TODO:", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("- TODO:", StringComparison.OrdinalIgnoreCase);
        var lines = normalized.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return lines.All(line => line.TrimStart().StartsWith('|') || PlaceholderPattern().IsMatch(line));
    }

    private static string RenderBusinessBaseline(BusinessEvidence? brd)
    {
        if (brd is null)
            return "No canonical BRD content was readable. Initialization must remain blocked until the Active BRD is restored.";

        var builder = new StringBuilder()
            .AppendLine($"This draft is grounded in the current canonical [{brd.Title}]({brd.RelativePath}) (document status: {ReadFrontMatter(brd.Content, "status") ?? "unknown"}) and must not broaden its scope, actors, outcomes, exclusions, or acceptance boundaries. This evidence does not grant business approval.")
            .AppendLine()
            .AppendLine($"- Semantic source: `{brd.Digest}`")
            .AppendLine($"- Requirement coverage ({brd.RequirementIds.Count}): {RenderInlineCodes(brd.RequirementIds)}")
            .AppendLine($"- Business outcomes detected: {brd.Outcomes.Count}");
        if (!string.IsNullOrWhiteSpace(brd.Summary))
            builder.AppendLine().AppendLine("### Business context carried forward").AppendLine()
                .AppendLine(brd.Summary);
        if (brd.Outcomes.Count > 0)
        {
            builder.AppendLine().AppendLine("### Outcome drivers").AppendLine();
            foreach (var outcome in brd.Outcomes) builder.AppendLine("- " + outcome);
        }
        return builder.ToString().TrimEnd();
    }

    private static string RenderQuestionnaireChoices(State state)
    {
        if (state.Questionnaire is not { Current: true, Complete: true } questionnaire)
            return "The governed high-level technical questionnaire is missing, stale, or incomplete. Technical-intent generation must remain blocked.";
        var builder = new StringBuilder()
            .AppendLine("These governed choices establish the product's high-level technical direction. Existing-project facts may be derived from deterministic repository evidence; ambiguous and greenfield decisions require human answers. The detailed sections below elaborate them without silently selecting a different technology, topology, or provider.")
            .AppendLine()
            .AppendLine("| Decision input | Area | Recorded direction | Authority and provenance |")
            .AppendLine("| --- | --- | --- | --- |");
        foreach (var question in questionnaire.Questions)
        {
            var provenance = question.ResolutionSource == "repository-evidence"
                ? $"Derived by CIS ({question.Confidence ?? "unrated"}) from {string.Join(", ", question.Evidence ?? [])}"
                : $"{question.AnsweredBy ?? "Not recorded"} at {question.AnsweredAtUtc ?? "Not recorded"}";
            builder.AppendLine($"| `{Cell(question.Id)}` | {Cell(question.Area)} | {Cell(question.Answer ?? "Not answered")} | {Cell(provenance)} |");
        }
        return builder.ToString().TrimEnd();
    }

    private static string RenderComponentMap(State state)
    {
        var answer = (string id) => QuestionnaireAnswer(state, id);
        var builder = new StringBuilder()
            .AppendLine("This logical map is generated from the governed questionnaire. It defines the initial component responsibilities and interactions; implementation-level components and exact interfaces are refined through ADRs and feature design.")
            .AppendLine()
            .AppendLine("### Logical components")
            .AppendLine()
            .AppendLine("| Component boundary | Direction | Owns | Does not own | Source |")
            .AppendLine("| --- | --- | --- | --- | --- |")
            .AppendLine($"| Experience surfaces | {Cell(answer("TI-Q-001"))} Technology: {Cell(answer("TI-Q-002"))} | Presentation, interaction, accessibility, and client state for the selected surfaces. | Server-side authorization, durable business policy, or another component's data. | `TI-Q-001`, `TI-Q-002` |")
            .AppendLine($"| Application and domain boundary | {Cell(answer("TI-Q-003"))} Architecture: {Cell(answer("TI-Q-004"))} Topology: {Cell(answer("TI-Q-005"))} | Use cases, business invariants, authorization orchestration, and component contracts. | UI rendering, provider SDK policy, or raw infrastructure provisioning. | `TI-Q-003`–`TI-Q-005` |")
            .AppendLine($"| Data boundary | Primary: {Cell(answer("TI-Q-006"))} Supporting: {Cell(answer("TI-Q-007"))} | Systems of record, transactions, migrations, retention, backup, restore, and derived-store rebuilds. | Presentation or identity-provider behavior. | `TI-Q-006`, `TI-Q-007` |")
            .AppendLine($"| Identity and policy boundary | {Cell(answer("TI-Q-009"))} Security constraints: {Cell(answer("TI-Q-013"))} | Identity establishment, trusted-boundary authorization, ownership/tenancy, and auditable policy decisions. | Product data beyond the minimum identity and policy need. | `TI-Q-009`, `TI-Q-013` |")
            .AppendLine($"| Integration and asynchronous boundary | Contracts: {Cell(answer("TI-Q-008"))} Async: {Cell(answer("TI-Q-011"))} | Cross-boundary schemas, compatibility, delivery, retry, idempotency, and recovery. | Internal domain ownership. | `TI-Q-008`, `TI-Q-011` |")
            .AppendLine($"| Runtime and operations boundary | Hosting: {Cell(answer("TI-Q-010"))} Operations: {Cell(answer("TI-Q-012"))} | Environments, deployment, configuration, secrets, telemetry, support, capacity, and recovery. | Business authority or product scope. | `TI-Q-010`, `TI-Q-012` |")
            .AppendLine($"| AI/model boundary | {Cell(answer("TI-Q-015"))} | Model execution, evaluation, versioning, fallback, cost, and human oversight when applicable. | Deterministic business authority. | `TI-Q-015` |")
            .AppendLine()
            .AppendLine("### Primary interactions")
            .AppendLine()
            .AppendLine("| From | Interaction | To | Governing direction |")
            .AppendLine("| --- | --- | --- | --- |")
            .AppendLine($"| Experience surfaces | User or platform command/query | Application and domain boundary | {Cell(answer("TI-Q-008"))} |")
            .AppendLine($"| Application and domain boundary | Identity and authorization decision | Identity and policy boundary | {Cell(answer("TI-Q-009"))} |")
            .AppendLine($"| Application and domain boundary | Transactional read/write | Data boundary | {Cell(answer("TI-Q-006"))} |")
            .AppendLine($"| Application and domain boundary | External or deferred work | Integration and asynchronous boundary | {Cell(answer("TI-Q-011"))} |")
            .AppendLine($"| Every runtime boundary | Privacy-safe diagnostic signal | Runtime and operations boundary | {Cell(answer("TI-Q-012"))} |")
            .AppendLine($"| Application and domain boundary | Bounded model request/result, if applicable | AI/model boundary | {Cell(answer("TI-Q-015"))} |")
            .AppendLine()
            .AppendLine($"Quality direction: {answer("TI-Q-014")}")
            .AppendLine()
            .AppendLine($"Preserved constraints and exclusions: {answer("TI-Q-016")}");
        return builder.ToString().TrimEnd();
    }

    private static string RenderProductModuleArchitecture(State state)
    {
        var modules = BuildLogicalModules(state);
        var builder = new StringBuilder()
            .AppendLine("The module view below turns the approved business capabilities into reviewable ownership boundaries. A capability is not automatically a deployable: the architecture and topology choices decide whether these modules remain in-process, share a repository, or require an independently deployed boundary.")
            .AppendLine()
            .AppendLine($"Architecture direction: {QuestionnaireAnswer(state, "TI-Q-004")}")
            .AppendLine()
            .AppendLine($"Repository and deployment direction: {QuestionnaireAnswer(state, "TI-Q-005")}")
            .AppendLine()
            .AppendLine("### Proposed module tree")
            .AppendLine()
            .AppendLine("```text")
            .AppendLine(state.Brd?.Title ?? "Product");
        for (var index = 0; index < modules.Count; index++)
            builder.AppendLine($"  {(index == modules.Count - 1 ? "└─" : "├─")} {modules[index].Name} [{modules[index].Id}] ({modules[index].Classification})");
        builder.AppendLine("  └─ Cross-cutting technical boundaries (identity, persistence, integration, observability, delivery)")
            .AppendLine("```")
            .AppendLine()
            .AppendLine("### Module ownership summary")
            .AppendLine()
            .AppendLine("| Module | Classification | Purpose | Owns | Must not own | BRD authority |")
            .AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var module in modules)
            builder.AppendLine($"| `{Cell(module.Id)}` — {Cell(module.Name)} | {Cell(module.Classification)} | {Cell(module.Purpose)} | {Cell(module.Ownership)} | {Cell(module.Exclusions)} | {RenderInlineCodes(module.RequirementIds)} |");

        builder.AppendLine()
            .AppendLine("### Module ownership rules")
            .AppendLine()
            .AppendLine("- One module owns each business rule, state transition, durable record, and public contract; another module may consume that authority but may not duplicate it.")
            .AppendLine("- Product modules may depend on shared technical capabilities through explicit contracts. Shared capabilities must not depend on a product module's internal types or storage schema.")
            .AppendLine("- In-process calls still cross a module boundary through its public application contract. Cross-deployable calls require an independently versioned contract and forward-transitive compatibility unless an accepted decision states otherwise.")
            .AppendLine("- Cross-module database writes, shared mutable tables, cyclic module references, and transport types inside domain rules are prohibited unless an ADR records the bounded exception and verification.")
            .AppendLine("- Feature specifications may merge, split, or rename these proposed modules, but must preserve every BRD trace and explicitly migrate affected ownership and integration identities.")
            .AppendLine()
            .AppendLine("### Module responsibility profiles");
        foreach (var module in modules)
        {
            builder.AppendLine()
                .AppendLine($"#### {module.Id} — {module.Name}")
                .AppendLine()
                .AppendLine($"- **Purpose:** {module.Purpose}")
                .AppendLine($"- **Business authority:** {RenderInlineCodes(module.RequirementIds)}; source: {module.Evidence}.")
                .AppendLine($"- **Owns:** {module.Ownership}")
                .AppendLine($"- **Accepts:** {module.Inputs}")
                .AppendLine($"- **Produces:** {module.Outputs}")
                .AppendLine($"- **Data and state:** {module.DataOwnership}")
                .AppendLine($"- **Security and policy:** Authorization is enforced at the trusted application boundary before this module executes; capability-specific policy remains inside this module.")
                .AppendLine($"- **Failure and recovery:** {module.FailureAndRecovery}")
                .AppendLine($"- **Verification:** Exercise {RenderInlineCodes(module.RequirementIds)} through isolated policy tests, contract/component tests at each exposed boundary, and integration or recovery tests for owned durable state.")
                .AppendLine($"- **Excluded ownership:** {module.Exclusions}");
        }
        return builder.ToString().TrimEnd();
    }

    private static string RenderIntegrationPointCatalog(State state)
    {
        var integrations = BuildIntegrationPoints(state);
        var builder = new StringBuilder()
            .AppendLine("This catalog identifies the initial cross-boundary handoffs implied by the governed technical choices and the Active BRD. It is architecture intent rather than an endpoint dictionary: feature design supplies exact operations, schemas, event names, and service-level objectives without changing the owner or direction silently.")
            .AppendLine()
            .AppendLine("| Integration | Source | Trigger and flow | Target | Contract or data | Delivery and consistency | Trust and authorization | Failure and recovery | BRD authority |")
            .AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var point in integrations)
            builder.AppendLine($"| `{Cell(point.Id)}` | {Cell(point.Source)} | {Cell(point.Flow)} | {Cell(point.Target)} | {Cell(point.Contract)} | {Cell(point.Delivery)} | {Cell(point.Trust)} | {Cell(point.FailureAndRecovery)} | {RenderInlineCodes(point.RequirementIds)} |");

        builder.AppendLine()
            .AppendLine("### Integration governance")
            .AppendLine()
            .AppendLine($"- Default contract direction: {QuestionnaireAnswer(state, "TI-Q-008")}")
            .AppendLine($"- Default asynchronous-work direction: {QuestionnaireAnswer(state, "TI-Q-011")}")
            .AppendLine("- The target owner defines the accepted command/query contract; the source owns adaptation, correlation, timeout, retry, and user-visible recovery at its side of the handoff.")
            .AppendLine("- Every network, external-provider, event, file, model, or human handoff records schema/version, authentication, authorization, privacy classification, idempotency, ordering, timeout, retry, dead-letter or repair behavior, observability, and contract tests before implementation.")
            .AppendLine("- Synchronous failure must be explicit and bounded. Retry is allowed only for a classified transient failure and an idempotent operation. Asynchronous delivery must define duplicate, out-of-order, poison-message, replay, and reconciliation behavior.")
            .AppendLine("- A module never integrates by reading or writing another module's store directly. Read models and search indexes are derived projections with a named rebuild path, not competing systems of record.")
            .AppendLine("- Exact API, event, permission, data, problem-details, and operational records are maintained in their canonical reference dictionaries and linked back to the stable `TI-INT-*` identity.");
        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<LogicalModule> BuildLogicalModules(State state)
    {
        var candidates = new List<(string Name, string Purpose, IReadOnlyList<string> Requirements, string Evidence)>();
        if (state.Brd is { Capabilities.Count: > 0 } brd)
            candidates.AddRange(brd.Capabilities.Select(item => (item.Name, item.Description, item.RequirementIds, item.Source)));
        else if (state.Brd is { Outcomes.Count: > 0 } outcomes)
            candidates.AddRange(outcomes.Outcomes.Select(item => (CapabilityName(item), item, RequirementIds(item), "BRD Business outcomes")));
        else
            candidates.AddRange(state.Surfaces.Where(item => item.Participation == "owned" && item.Component != "unclassified")
                .Select(item => (item.Component, $"Preserve the detected {item.Component} implementation boundary in {item.RepositoryId}.",
                    (IReadOnlyList<string>)(state.Brd?.RequirementIds ?? []), $"Repository classification: {item.RepositoryId}/{item.Root}")));

        if (candidates.Count == 0)
            candidates.Add(("Product core", state.Brd?.Summary is { Length: > 0 } summary ? summary : "Own the product use cases and business rules established by the Active BRD.",
                state.Brd?.RequirementIds ?? [], "Active BRD"));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modules = new List<LogicalModule>();
        foreach (var candidate in candidates.Take(24))
        {
            var id = UniqueId("TI-MOD-" + Slug(candidate.Name), used);
            var role = ModuleRole(candidate.Name + " " + candidate.Purpose);
            modules.Add(new LogicalModule(id, candidate.Name, ModuleClassification(role), candidate.Purpose,
                $"The {candidate.Name} use cases, capability-specific policies, state transitions, and authoritative business records.",
                "Presentation rendering, identity-provider behavior, deployment provisioning, observability transport, or another module's authoritative records.",
                candidate.Requirements.Count > 0 ? candidate.Requirements : state.Brd?.RequirementIds ?? [], candidate.Evidence,
                ModuleInputs(role, candidate.Name), ModuleOutputs(role, candidate.Name), ModuleData(role, candidate.Name),
                ModuleFailure(role)));
        }
        return modules;
    }

    private static IReadOnlyList<IntegrationPoint> BuildIntegrationPoints(State state)
    {
        var modules = BuildLogicalModules(state);
        var points = new List<IntegrationPoint>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contract = QuestionnaireAnswer(state, "TI-Q-008");
        var identity = QuestionnaireAnswer(state, "TI-Q-009");
        var data = QuestionnaireAnswer(state, "TI-Q-006");
        var asyncDirection = QuestionnaireAnswer(state, "TI-Q-011");
        var operations = QuestionnaireAnswer(state, "TI-Q-012");
        var allRequirements = state.Brd?.RequirementIds ?? [];

        void Add(string id, string source, string flow, string target, string payload, string delivery,
            string trust, string failure, IReadOnlyList<string> requirements)
        {
            var unique = UniqueId(id, used);
            points.Add(new IntegrationPoint(unique, source, flow, target, payload, delivery, trust, failure,
                requirements.Count > 0 ? requirements : allRequirements));
        }

        Add("TI-INT-EXPERIENCE-APPLICATION", "Experience surfaces or machine clients", "A user or caller submits a capability command/query and receives an explicit result.",
            "Owning product module", "Versioned capability request, response, validation failure, and correlation identity.", contract,
            identity, "Reject invalid or unauthorized requests without business mutation; expose bounded retry or recovery guidance.", allRequirements);
        Add("TI-INT-APPLICATION-DATA", "Owning product module", "An accepted use case reads or changes its authoritative state.",
            "Primary data boundary", "Owned aggregates, concurrency token, transaction outcome, and audit correlation.", data,
            "The module service identity has least-privilege access only to its owned data.", "Rollback the transaction on failure; migrations, backup, restore, and reconciliation retain deterministic evidence.", allRequirements);
        Add("TI-INT-APPLICATION-IDENTITY", "Trusted application boundary", "Before a protected command/query, establish the actor and materialize the applicable permissions and ownership scope.",
            "Identity and policy boundary", "Principal identity, session or token context, permissions, tenancy/ownership scope, and policy decision.", identity,
            identity, "Fail closed, distinguish unavailable identity from denied authorization internally, and avoid disclosing protected resource existence.", allRequirements);
        Add("TI-INT-RUNTIME-OBSERVABILITY", "Every runtime and product module", "At boundary crossings, state transitions, failures, and recovery actions, emit a privacy-safe diagnostic signal.",
            "Runtime and operations boundary", "Correlation identity, event/metric name, bounded non-sensitive attributes, outcome, duration, and failure classification.", operations,
            "Telemetry contains no secrets or unrestricted business payloads and is access-controlled by operational role.", "Telemetry failure does not falsify the business outcome; health, alerting, and retained diagnostics expose the observability gap.", allRequirements);

        foreach (var module in modules)
            Add($"TI-INT-{Slug(module.Name)}-ENTRY", "Experience surface, scheduler, or upstream module", $"Invoke the {module.Name} capability through its public application boundary.",
                module.Name, $"Command/query and result for: {module.Purpose}", contract,
                identity, module.FailureAndRecovery, module.RequirementIds);

        AddRoleFlow("intake", "assessment", "Prepared and validated business facts", "Prepared input becomes available for assessment.");
        AddRoleFlow("assessment", "decision", "Assessment result and explanation", "A result requires governed decision or referral handling.");
        AddRoleFlow("decision", "feedback", "Human review request and accountable outcome", "A referred decision is reviewed and its outcome is recorded.");
        AddRoleFlow("feedback", "oversight", "Governed outcome and evaluation signal", "Reviewed outcomes become available for reporting, evaluation, or learning.");
        AddRoleFlow("assessment", "oversight", "Assessment quality and performance signal", "An assessment completes or a quality threshold is evaluated.");
        AddRoleFlow("decision", "customer", "Approved customer-visible state", "A governed decision changes externally visible state.");

        foreach (var module in modules)
        {
            var source = module.Name + " " + module.Purpose;
            var requirements = module.RequirementIds;
            if (HasAny(source, "business intelligence", " BI ", "prefilter", "data source", "data feed"))
                Add($"TI-INT-{Slug(module.Name)}-EXTERNAL-DATA", "External data or BI system", "Authorized source data is published for processing.", module.Name,
                    "Source identity, schema version, permitted-use metadata, business payload, and correlation/checkpoint.", contract,
                    identity, "Quarantine malformed, unauthorized, duplicate, or incomplete inputs; retain checkpoint and reconciliation evidence.", requirements);
            if (HasAny(source, "rule engine", "rules engine"))
                Add($"TI-INT-{Slug(module.Name)}-RULE-ENGINE", module.Name, "A governed suggestion or policy input is ready for deterministic rule evaluation.", "External or existing rule engine",
                    "Suggestion, explanation reference, policy version, correlation identity, and rule outcome.", contract,
                    identity, "Timeout or rejection must not be interpreted as approval; reconcile uncertain outcomes by correlation identity.", requirements);
            if (HasAny(source, "current system", "legacy system", "parallel running", "parallel run"))
                Add($"TI-INT-{Slug(module.Name)}-LEGACY", module.Name, "A result must run beside or be compared with the incumbent behavior.", "Current or legacy system",
                    "Comparable input identity, old/new outcomes, policy/model versions, divergence classification, and human disposition.", "Independent execution followed by deterministic reconciliation; no automatic cutover.",
                    identity, "Retain divergence and unavailable-system evidence; the approved incumbent safeguard remains authoritative until human cutover.", requirements);
            if (HasAny(source, "payment", "transaction", "SEPA", "SWIFT", "bank transfer"))
                Add($"TI-INT-{Slug(module.Name)}-TRANSACTION-SYSTEM", "Payment or transaction system", "A covered transaction or governed outcome crosses the prototype boundary.", module.Name,
                    "Transaction reference, permitted attributes, state/version, assessment correlation, and accepted outcome.", contract,
                    identity, "Do not lose or duplicate financial-state transitions; uncertain delivery requires correlation-based status resolution and manual recovery.", requirements);
            if (HasAny(source, "blob", "immutable", "audit evidence", "archive"))
                Add($"TI-INT-{Slug(module.Name)}-EVIDENCE-STORE", module.Name, "A material input, decision, explanation, policy, or action reaches its retention boundary.", "Immutable evidence or object store",
                    "Content digest, immutable object/version identity, retention class, region, access classification, and source correlation.", "Append-only durable write with read-after-write verification where required.",
                    identity, "A failed evidence write blocks completion where audit integrity is mandatory; repair is idempotent and hash-verified.", requirements);
            if (HasAny(source, " AI ", "model", "learning", "retrain", "classification", "embedding"))
                Add($"TI-INT-{Slug(module.Name)}-MODEL", module.Name, "A bounded inference, evaluation, or training operation is requested.", "AI/model lifecycle boundary",
                    "Approved feature set or dataset version, model/version, policy context, result, explanation, evaluation metadata, and correlation.", asyncDirection,
                    identity, "Timeout, invalid output, drift, or unavailable model follows the approved fallback; model output never becomes deterministic business authority by itself.", requirements);
            if (HasAny(source, "notification", "email", "message", "communication"))
                Add($"TI-INT-{Slug(module.Name)}-NOTIFICATION", module.Name, "A governed business event requires an external communication.", "Notification provider",
                    "Template/version, recipient reference, privacy-safe parameters, correlation, delivery status, and suppression reason.", asyncDirection,
                    identity, "Use idempotency and delivery reconciliation; provider acceptance is not proof of recipient delivery.", requirements);
            if (HasAny(source, "search", "retrieval", "index"))
                Add($"TI-INT-{Slug(module.Name)}-SEARCH", module.Name, "Approved authoritative content changes or a scoped retrieval is requested.", "Search and retrieval boundary",
                    "Pointer-based projection or security-scoped query with source version and rebuild identity.", asyncDirection,
                    identity, "Search remains derived and rebuildable; stale, missing, or unauthorized results never change the system of record.", requirements);
        }

        foreach (var dependency in state.Surfaces.Where(item => item.Participation == "dependency")
                     .GroupBy(item => new { item.RepositoryId, item.Relationship }))
        {
            var componentNames = dependency.Where(item => item.Component != "unclassified")
                .Select(item => item.Component).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var boundary = componentNames.Length == 0
                ? dependency.Key.RepositoryId
                : $"{dependency.Key.RepositoryId} ({string.Join(", ", componentNames)})";
            const string payload = "Versioned dependency contract, correlation identity, compatibility evidence, and bounded failure result.";
            if (dependency.Key.Relationship is "producer" or "bidirectional")
                Add($"TI-INT-{Slug(dependency.Key.RepositoryId)}-PRODUCT", boundary,
                    "The dependency produces a capability, contract, event, or data result consumed by this product.",
                    "Owning product module", payload, contract, identity,
                    "Dependency unavailability or incompatibility is isolated, diagnosed, and recovered without silently changing product authority.", allRequirements);
            if (dependency.Key.Relationship is "consumer" or "bidirectional")
                Add($"TI-INT-PRODUCT-{Slug(dependency.Key.RepositoryId)}", "Owning product module",
                    "The product produces a capability, contract, event, or data result consumed by the dependency.",
                    boundary, payload, contract, identity,
                    "Consumer compatibility is retained or migrated explicitly; delivery does not modify the dependency without separate authority.", allRequirements);
        }

        return points;

        void AddRoleFlow(string sourceRole, string targetRole, string payload, string trigger)
        {
            var source = modules.FirstOrDefault(item => ModuleRole(item.Name + " " + item.Purpose) == sourceRole);
            var target = modules.FirstOrDefault(item => ModuleRole(item.Name + " " + item.Purpose) == targetRole);
            if (source is null || target is null || source.Id == target.Id) return;
            Add($"TI-INT-{Slug(source.Name)}-{Slug(target.Name)}", source.Name, trigger, target.Name, payload,
                contract, identity, "The source retains a classified failure and correlation identity; retry or compensation follows the target contract without duplicating state.",
                source.RequirementIds.Concat(target.RequirementIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private static string ModuleRole(string source)
    {
        if (HasAny(source, "preparation", "intake", "ingest", "source data", "data feed", "capture")) return "intake";
        if (HasAny(source, "assessment", "scoring", "classification", "detection", "analysis")) return "assessment";
        if (HasAny(source, "threshold", "decision", "referral", "review", "case handling", "workflow")) return "decision";
        if (HasAny(source, "feedback", "outcome capture", "label")) return "feedback";
        if (HasAny(source, "performance", "report", "monitor", "drift", "oversight")) return "oversight";
        if (HasAny(source, "audit", "evidence", "archive", "retention")) return "audit";
        if (HasAny(source, "customer", "transaction handling", "customer-facing")) return "customer";
        if (HasAny(source, "identity", "authorization", "permission", "membership")) return "identity";
        return "capability";
    }

    private static string ModuleClassification(string role) => role switch
    {
        "identity" or "audit" or "oversight" => "Cross-cutting capability candidate",
        "intake" => "Integration-facing product capability",
        _ => "Product capability candidate",
    };

    private static string ModuleInputs(string role, string name) => role switch
    {
        "intake" => "Authorized source data, source/version identity, permitted-use context, and ingestion checkpoint.",
        "assessment" => "Prepared business facts, applicable policy/model version, and a correlation identity.",
        "decision" => "Assessment or workflow state, explanation, applicable policy, actor identity, and concurrency state.",
        "feedback" => "An accountable human outcome, rationale or classification, source decision identity, and reviewer provenance.",
        "oversight" => "Versioned outcomes, quality/performance measures, drift signals, and reporting period.",
        "audit" => "Material business inputs, decisions, policy/model versions, actor actions, and immutable content identities.",
        "customer" => "Only an approved customer-visible state and the minimum authorized transaction context.",
        "identity" => "Authentication evidence, principal/customer context, role/membership state, and requested capability.",
        _ => $"A validated {name} command/query with actor, correlation, policy, and concurrency context where applicable.",
    };

    private static string ModuleOutputs(string role, string name) => role switch
    {
        "intake" => "Validated and normalized facts, rejected-input evidence, and an idempotent source checkpoint.",
        "assessment" => "A versioned assessment result, explanation, influential facts, quality metadata, and correlation identity.",
        "decision" => "An allowed, blocked, referred, rejected, or pending state plus the accountable decision evidence.",
        "feedback" => "Governed feedback suitable for evaluation or later learning, without treating an unreviewed label as fact.",
        "oversight" => "Reproducible reports, alerts, trend/drift results, and action-required signals.",
        "audit" => "Immutable evidence identity, integrity proof, retrieval metadata, and access-audit result.",
        "customer" => "A non-disclosing customer-visible state or action result consistent with the authoritative decision.",
        "identity" => "An immutable request-scoped actor and permission context, or a non-disclosing denial.",
        _ => $"An explicit {name} result, state transition, domain event where needed, and retained audit correlation.",
    };

    private static string ModuleData(string role, string name) => role switch
    {
        "intake" => "Source registrations, schema/checkpoint state, validation outcomes, and normalized intake records; the external source remains owner of upstream data.",
        "assessment" => "Assessment requests/results, input and policy/model version references, explanations, and evaluation metadata.",
        "decision" => "Cases or workflow instances, decision state/history, assignee/reviewer provenance, policy snapshot, and concurrency version.",
        "feedback" => "Reviewed outcomes, label/provenance history, disagreement/adjudication state when defined, and learning eligibility.",
        "oversight" => "Reporting definitions, immutable report snapshots, measure versions, drift observations, and review actions.",
        "audit" => "Append-only evidence manifests, content digests, retention/access classification, object pointers, and retrieval audit.",
        "customer" => "Customer-visible status projection only; authoritative assessment and decision data remain with their owning modules.",
        "identity" => "Principal linkage, memberships, roles, permissions, policy versions, sessions, and access-decision audit; no unrelated product data.",
        _ => $"The aggregates and state transitions unique to {name}; exact entity names and consistency boundaries are refined before the first implementing feature.",
    };

    private static string ModuleFailure(string role) => role switch
    {
        "intake" => "Reject or quarantine invalid input, retain the source checkpoint, and support safe idempotent replay after correction.",
        "assessment" => "Return an explicit unavailable/invalid result, preserve the request identity, and follow the approved deterministic fallback rather than inventing a score.",
        "decision" => "Fail closed where authorization or financial/risk state is uncertain; retain concurrency and decision evidence for safe retry or manual recovery.",
        "feedback" => "Keep ambiguous or unreviewed feedback in a non-learning state and retain provenance for later adjudication.",
        "oversight" => "Mark incomplete periods and missing inputs explicitly; reports are reproducible from versioned source evidence.",
        "audit" => "Where evidence is mandatory, an unverified durable write blocks completion; restore and retrieval drills prove recoverability.",
        "customer" => "Use a non-disclosing unavailable state and never expose a status that is not backed by the authoritative decision.",
        "identity" => "Fail closed with non-disclosing errors; revoked, expired, unknown, or unavailable identity state grants no product access.",
        _ => "Return a classified failure without partial unauthorized mutation; retry requires idempotency, and durable state has a tested reconciliation or recovery path.",
    };

    private static string CapabilityName(string source)
    {
        var value = Regex.Replace(source, @"^\*\*(?<name>.+?)\*\*.*$", "${name}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        value = value.Split(':', 2)[0].Trim().Trim('*', '_', '`', '.', '-', ' ');
        return value.Length == 0 ? "Product capability" : value.Length <= 100 ? value : value[..100].TrimEnd();
    }

    private static IReadOnlyList<string> RequirementIds(string source)
        => RequirementIdPattern().Matches(source).Select(match => match.Value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string Slug(string source)
    {
        var slug = Regex.Replace(source.ToUpperInvariant(), "[^A-Z0-9]+", "-",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim('-');
        if (slug.Length == 0) slug = "CAPABILITY";
        return slug.Length <= 40 ? slug : slug[..40].TrimEnd('-');
    }

    private static string UniqueId(string proposed, ISet<string> used)
    {
        var id = proposed;
        for (var suffix = 2; !used.Add(id); suffix++) id = proposed + "-" + suffix;
        return id;
    }

    private static string QuestionnaireAnswer(State state, string id)
        => state.Questionnaire?.Questions.FirstOrDefault(item => item.Id == id)?.Answer?.Trim() is { Length: > 0 } value
            ? value : "Not answered";

    private static string RenderDesignGoals(State state)
    {
        var goals = new List<string>
        {
            "Preserve the Active BRD as business authority; technical direction may satisfy it but may not silently add product scope.",
            "Keep business policy and invariants independent of transport, storage, model, user-interface, and hosting providers unless an approved decision explicitly couples them.",
            "Make component ownership, trust boundaries, external contracts, data ownership, failure behavior, and recovery responsibilities explicit before detailed delivery planning.",
            "Trace durable choices to BRD requirement IDs, applicable standard rule IDs, and an ADR where the choice has meaningful alternatives or long-lived consequences.",
            "Prefer deterministic verification and observable evidence for architecture constraints; model output is advisory evidence only.",
        };
        if (state.Surfaces.Where(item => item.Participation == "owned").All(item => item.Component == "unclassified"))
            goals.Add("Retain technology neutrality until the runtime shape and repository boundaries are explicitly selected; repository initialization found no implementation component.");
        return string.Join('\n', goals.Select(goal => "- " + goal));
    }

    private static string RenderTechnicalSurface(IReadOnlyList<TechnicalSurface> surfaces)
    {
        var builder = new StringBuilder()
            .AppendLine("Repository classifications are deterministic routing evidence, not an architecture decision.")
            .AppendLine()
            .AppendLine("| Repository | Workspace role | Product participation | Relationship | Component | Root | Languages and frameworks | Roles | Capabilities | Confidence |")
            .AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in surfaces)
        {
            builder.AppendLine($"| {Cell(item.RepositoryId)} | {Cell(item.WorkspaceRole)} | {Cell(item.Participation)} | {Cell(item.Relationship)} | {Cell(item.Component)} | `{Cell(item.Root)}` | " +
                               $"{Cell(JoinOrNone(item.Languages.Concat(item.Frameworks)))} | {Cell(JoinOrNone(item.Roles))} | " +
                               $"{Cell(JoinOrNone(item.Capabilities))} | {Cell(item.Confidence)} |");
        }
        return builder.ToString().TrimEnd();
    }

    private static string RenderArchitectureGuidelines(State state)
    {
        var builder = new StringBuilder()
            .AppendLine("The following directions are a reviewable starting point derived from the BRD, repository classification, and Active standards. They become authority only when this technical intent is approved.")
            .AppendLine()
            .AppendLine("| Guideline | Draft architectural direction | Basis |")
            .AppendLine("| --- | --- | --- |");
        var guidelines = BuildArchitectureGuidelines(state);
        for (var index = 0; index < guidelines.Count; index++)
            builder.AppendLine($"| TI-ARCH-{index + 1:000} | {Cell(guidelines[index].Direction)} | {Cell(guidelines[index].Basis)} |");

        builder.AppendLine().AppendLine("### Applicable standards provenance").AppendLine()
            .Append(RenderManagedEvidence(StandardsEvidenceStart, RenderStandardsEvidence(state), StandardsEvidenceEnd));
        return builder.ToString().TrimEnd();
    }

    private static string RenderStandardsEvidence(State state)
    {
        if (state.Standards.Count == 0)
            return "No Active standards were discovered in the registered repositories. Seed or import the applicable standards, or record the bounded governance gap before implementation.";

        var builder = new StringBuilder()
            .AppendLine("Product-owned standards are architectural constraints. Dependency standards are context for contract reconciliation and are not silently adopted by this product.")
            .AppendLine()
            .AppendLine("| Repository | Participation | Standard | Digest | Targets or stacks | Rules | Path |")
            .AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var standard in state.Standards.Where(item => item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)))
            builder.AppendLine($"| {Cell(standard.RepositoryId)} | {Cell(standard.Participation)} | `{Cell(standard.Id)}` — {Cell(standard.Title)} | `{Cell(standard.Digest)}` | " +
                               $"{Cell(JoinOrNone(standard.Targets.Concat(standard.Stacks)))} | {Cell(JoinOrNone(standard.RuleIds))} | `{Cell(standard.Path)}` |");
        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<ArchitectureGuideline> BuildArchitectureGuidelines(State state)
    {
        var surfaces = state.Surfaces.Where(item => item.Participation == "owned").ToArray();
        var guidelines = new List<ArchitectureGuideline>
        {
            new("Keep business rules and policy ownership inside an explicit domain or application boundary; transport, persistence, model, UI, and infrastructure concerns depend on that boundary rather than owning it.", Basis(state, "architecture", "application", "backend", "frontend")),
            new("Name every runtime, deployment, data, and human trust boundary; authenticate and authorize at the trusted boundary with least privilege and non-disclosing failure behavior.", Basis(state, "security", "authorization")),
            new("Give every durable datum, contract, decision, and operational signal one declared owner, lifecycle, compatibility expectation, and recovery path.", Basis(state, "documentation", "data", "api", "operations")),
            new("Keep technology and provider choices neutral until an approved technical decision selects them against BRD constraints and active standards.", "Active BRD scope and repository classification"),
            new("Enforce material dependency, contract, security, and lifecycle rules with deterministic architecture or policy tests and retain exact evidence.", Basis(state, "testing", "verification")),
        };
        if (surfaces.Any(item => item.Roles.Any(role => role.Contains("backend", StringComparison.OrdinalIgnoreCase))))
            guidelines.Add(new("Keep HTTP or RPC endpoints as transport adapters; version externally observable contracts and define compatibility, error, authorization, caching, and idempotency behavior explicitly.", Basis(state, "backend", "api", "security")));
        if (surfaces.Any(item => item.Roles.Any(role => role is "frontend-consumer" or "mobile-client" or "native-frontend")))
            guidelines.Add(new("Keep clients presentation-focused and treat server or platform policy as authoritative; use the existing UI framework and shared accessible components before introducing alternatives.", Basis(state, "frontend", "accessibility")));
        if (surfaces.Any(item => item.Capabilities.Contains("persistence", StringComparer.OrdinalIgnoreCase)))
            guidelines.Add(new("Isolate persistence behind owned application contracts; define transaction, concurrency, migration, retention, backup, restore, and backfill behavior before schema implementation.", Basis(state, "data", "persistence", "migration")));
        if (surfaces.Any(item => item.Capabilities.Contains("events", StringComparer.OrdinalIgnoreCase)
                                 || item.Roles.Any(role => role is "event-producer" or "event-consumer")))
            guidelines.Add(new("Treat events as versioned contracts; define delivery, idempotency, ordering, retry, dead-letter, replay, and projection recovery semantics.", Basis(state, "events", "messaging", "projection")));
        if (surfaces.Any(item => item.Roles.Contains("infrastructure", StringComparer.OrdinalIgnoreCase)))
            guidelines.Add(new("Keep deployment configuration reproducible and secrets external to source; isolate provider-specific resources behind a documented operational boundary.", Basis(state, "infrastructure", "deployment", "security")));
        return guidelines;
    }

    private static string RenderRuntimeArchitecture(State state)
    {
        var unclassified = state.Surfaces.Where(item => item.Participation == "owned" && item.Component == "unclassified").Select(item => item.RepositoryId).Distinct().ToArray();
        var builder = new StringBuilder()
            .AppendLine("- Use the component and repository table above as discovery evidence; the approved component model must be recorded here or in linked ADRs.")
            .AppendLine("- Identify process, network, identity, model/provider, persistence, operator, and external-system trust boundaries before implementation.")
            .AppendLine("- Preserve one named owner for each boundary and keep cross-boundary communication behind explicit contracts.");
        if (unclassified.Length > 0)
            builder.AppendLine($"- No implementation shape was detected for {RenderInlineCodes(unclassified)}; `TI-DEC-001` must select or confirm the runtime and repository topology.");
        return builder.ToString().TrimEnd();
    }

    private static string RenderApplicationModel(State state)
        => $"""
- Treat the {state.Brd?.RequirementIds.Count ?? 0} detected BRD requirement IDs as traceability anchors, not as a substitute for a technical domain model.
- Define the owned entities, value objects, state transitions, policy inputs, invariants, concurrency rules, and audit events needed to satisfy those requirements.
- Keep mutable policy owned by the business role named in the BRD and distinguish it from code or model configuration.
- Resolve storage, consistency, retention, deletion, migration, backfill, and recovery choices through the open decisions below; no database or provider is inferred by initialization.
""".Trim();

    private static string RenderIntegrationIntent(State state)
    {
        var frameworks = state.Surfaces.Where(item => item.Participation == "owned").SelectMany(item => item.Frameworks).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var contractDirection = frameworks.Length == 0
            ? "No repository framework was detected; select and record the applicable contract standards with the runtime architecture."
            : $"Prefer repository-supported contract standards for {RenderInlineCodes(frameworks)}; where no standard applies, make the choice explicit in a `TI-DEC-*` record.";
        return $"""
- Enumerate every external API, event, file, package, identity, data, model, and operator contract named or implied by the Active BRD.
- For each contract, record owner, direction, schema, authentication, authorization, versioning, compatibility window, timeout, retry, idempotency, error, observability, and test expectations.
- {contractDirection}
- Do not treat a provider, protocol, or framework mentioned in source evidence as approved technical authority unless the BRD, this document, or an accepted ADR selects it.
""".Trim();
    }

    private static string RenderSecurityIntent(State state)
    {
        var standards = StandardsFor(state, "security", "privacy", "authorization");
        var assurance = standards.Count == 0
            ? "No Active security standard was discovered; seed or import one before implementation and record any interim manual assurance boundary."
            : $"Map threat cases and negative tests to the applicable rules from {RenderInlineCodes(standards)}.";
        return $"""
- Classify actors, data, secrets, model inputs/outputs, logs, retained evidence, and administrative actions before selecting implementation controls.
- Enforce authentication, authorization, tenant or object ownership, and non-disclosure at each trusted server or platform boundary; client-side visibility is not authorization.
- Bound and validate untrusted input before domain, persistence, command, rendering, model, or external-service boundaries.
- Keep secrets and sensitive content out of source, canonical documents, prompts, logs, diagnostics, and user-visible errors; retain only governed, redacted evidence.
- {assurance}
""".Trim();
    }

    private static string RenderOperationsIntent(State state)
        => """
- Define environment topology, configuration ownership, health and readiness truth, telemetry, alerting, support diagnostics, capacity, and cost controls for each runtime boundary.
- Define forward and rollback migration behavior, backup and restore evidence, retained-data recovery, and compatibility windows before release planning.
- Keep deployment and hosting provider neutral unless the BRD or an accepted technical decision requires a specific provider.
- Preserve privacy-safe logs and metrics at boundary crossings and material business state transitions; test and operational runs must retain bounded diagnostic evidence.
""".Trim();

    private static string RenderQualityIntent(State state)
    {
        var hasFrontend = state.Surfaces.Any(item => item.Participation == "owned" && item.Roles.Any(role => role is "frontend-consumer" or "mobile-client" or "native-frontend"));
        return $"""
| Attribute | Draft requirement | Verification direction |
| --- | --- | --- |
| Reliability | Deterministic behavior, explicit failure states, idempotency where retry is possible, and tested recovery for durable state. | Unit, component/integration, business, recovery, and regression evidence selected by risk. |
| Performance and cost | Derive measurable budgets from BRD volumes and operational constraints; do not invent unsupported targets. | Representative benchmark, load, resource, and cost evidence at the owning boundary. |
| Maintainability | Explicit component ownership, inward dependency direction, small provider adapters, and versioned contracts. | Compiler-backed architecture tests plus standards conformance. |
| Accessibility | {(hasFrontend ? "Accessible keyboard, semantic, responsive, loading, empty, error, and recovery behavior is required for user-facing surfaces." : "Not applicable to the currently detected surface; reassess if a user interface is introduced.")} | {(hasFrontend ? "Component accessibility tests and critical browser or platform journeys." : "Classification and scope review.")} |
| Security and privacy | Least privilege, default-deny trusted-boundary enforcement, bounded inputs, protected secrets, redacted evidence, and negative abuse cases. | SAST, secret and dependency scanning, negative unit/integration tests, and independent security assurance. |
| Traceability | Requirements, decisions, standards, contracts, code, tests, and release evidence retain stable identities and source digests. | CIS graph, test reconciliation, verification, and documentation validation. |
""".Trim();
    }

    private static string RenderDeliveryConstraints(State state)
        => $"""
- The Active BRD and {state.Standards.Count(item => item.Participation == "owned" && item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))} Active product-owned standards are upstream constraints; dependency standards remain contract context, and any conflict is surfaced for human resolution rather than silently bypassed.
- Durable choices with meaningful alternatives or consequences are recorded as ADRs and linked to the affected `TI-DEC-*`, BRD requirements, standards, contracts, and repositories.
- A Draft technical intent authorizes no implementation, provider disclosure, deployment, or release. Detailed planning begins only after all required decisions are resolved or validly deferred and this document is explicitly approved.
- Delivery must preserve exact build, test, security, architecture, operational, and independent-assurance evidence; unavailable checks remain visible.
""".Trim();

    private static string RenderOpenDecisions(State state)
    {
        if (state.Questionnaire is { Current: true, Complete: true })
        {
            var groups = new[]
            {
                ("Implementation shape, surfaces, bounded components, ownership, runtime processes, and dependency direction.", new[] { "TI-Q-001", "TI-Q-002", "TI-Q-003", "TI-Q-004", "TI-Q-005" }),
                ("Systems of record, consistency, storage technologies, retention, migration, backup, restore, and deletion behavior.", new[] { "TI-Q-006", "TI-Q-007" }),
                ("Integration contracts, compatibility, authentication, failure, retry, idempotency, and asynchronous delivery.", new[] { "TI-Q-008", "TI-Q-011" }),
                ("Model execution, evaluation, explainability, versioning, promotion, rollback, cost, and human oversight.", new[] { "TI-Q-015" }),
                ("Identity, authorization, sensitive-data, privacy, compliance, threat, and non-disclosure boundaries.", new[] { "TI-Q-009", "TI-Q-013" }),
                ("Deployment topology, observability, quality gates, recovery, constraints, exclusions, and future options.", new[] { "TI-Q-010", "TI-Q-012", "TI-Q-014", "TI-Q-016" }),
            };
            var answered = new StringBuilder()
                .AppendLine("The governed questionnaire resolves the first high-level decision pass. Technical review may refine these directions or create linked ADRs, but must not silently contradict the recorded human choices.")
                .AppendLine()
                .AppendLine("| ID | Decision | Required before | Status | Resolution or rationale |")
                .AppendLine("| --- | --- | --- | --- | --- |");
            for (var index = 0; index < groups.Length; index++)
            {
                var evidence = string.Join("; ", groups[index].Item2.Select(id => $"{id}: {QuestionnaireAnswer(state, id)}"));
                answered.AppendLine($"| TI-DEC-{index + 1:000} | {Cell(groups[index].Item1)} | Change dossier creation | Accepted | {Cell(evidence)} | ");
            }
            return answered.ToString().TrimEnd();
        }

        var decisions = new List<string>();
        var noImplementation = state.Surfaces.Where(item => item.Participation == "owned").All(item => item.Component == "unclassified");
        decisions.Add(noImplementation
            ? "Select the implementation shape, bounded components, repository ownership, runtime processes, and dependency direction."
            : "Confirm the bounded components, repository ownership, runtime processes, and dependency direction for the detected technical surfaces.");

        var source = state.Brd?.Content ?? string.Empty;
        if (HasAny(source, "data", "database", "storage", "retain", "retention", "audit", "history", "migration"))
            decisions.Add("Select systems of record, consistency boundaries, storage technologies, retention enforcement, migration, backup, restore, and deletion behavior.");
        if (HasAny(source, "api", "event", "integration", "external", "system", "provider", "rule engine", "business-intelligence", " BI "))
            decisions.Add("Define external integration contracts, ownership, versioning, compatibility, authentication, failure, retry, idempotency, and test strategy.");
        if (HasAny(source, " AI ", "model", "learning", "retraining", "classification", "explanation", "drift"))
            decisions.Add("Select the model execution, evaluation, explainability, versioning, learning, promotion, rollback, and human-oversight architecture.");
        if (HasAny(source, "security", "privacy", "customer", "identity", "authorization", "regulated", "fraud", "risk", "sensitive"))
            decisions.Add("Define identity, authorization, policy-administration, sensitive-data, audit-access, threat, and non-disclosure boundaries.");
        decisions.Add("Define deployment topology, environments, observability, capacity and cost budgets, recovery objectives, release gates, and independent assurance.");

        var builder = new StringBuilder()
            .AppendLine("Initialization proposes decision questions but does not choose an option. Resolve or explicitly defer each row with human authority before approval.")
            .AppendLine()
            .AppendLine("| ID | Decision | Required before | Status | Resolution or rationale |")
            .AppendLine("| --- | --- | --- | --- | --- |");
        for (var index = 0; index < decisions.Count; index++)
            builder.AppendLine($"| TI-DEC-{index + 1:000} | {Cell(decisions[index])} | Change dossier creation | Open | Unresolved; assess options against the BRD and applicable standards. |");
        return builder.ToString().TrimEnd();
    }

    private static BusinessEvidence ReadBusinessEvidence(CisWorkspaceRepository authority, string path, string content)
    {
        var title = ReadFrontMatter(content, "title") ?? $"{authority.Id} Business Requirements";
        var relativePath = NormalizePath(Path.GetRelativePath(Path.GetDirectoryName(path)!, path));
        var summary = FirstParagraph(ExtractSection(content, "Executive summary"));
        var outcomes = ExtractSection(content, "Business outcomes").Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim()).Take(12).ToArray();
        var capabilities = ParseBusinessCapabilities(ExtractSection(content, "Business capabilities and processes"));
        var requirements = RequirementIdPattern().Matches(content).Select(match => match.Value)
            .Where(id => !id.StartsWith("BRD-Q-", StringComparison.OrdinalIgnoreCase)
                         && !id.StartsWith("BRD-SRC-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return new BusinessEvidence(title, relativePath, "semantic-v1:" + BrdDocumentDigest.Compute(content), summary, outcomes, capabilities, requirements, content);
    }

    private static IReadOnlyList<BusinessCapability> ParseBusinessCapabilities(string section)
    {
        var capabilities = new List<BusinessCapability>();
        string? pendingLabel = null;
        var pendingDescription = new StringBuilder();

        void Flush()
        {
            if (pendingLabel is null) return;
            var rawLabel = pendingLabel.Trim().TrimEnd(':');
            var requirements = RequirementIds(rawLabel + " " + pendingDescription);
            var name = Regex.Replace(rawLabel,
                @"\s*\([^)]*\bBRD?-(?:FR|NFR)-\d{3,}[^)]*\)\s*$", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();
            var description = CleanInlineMarkdown(pendingDescription.ToString());
            if (name.Length > 0)
            {
                if (description.Length == 0) description = $"Own the business capability described by {name}.";
                capabilities.Add(new BusinessCapability(name, description, requirements,
                    $"BRD Business capabilities and processes: {rawLabel}"));
            }
            pendingLabel = null;
            pendingDescription.Clear();
        }

        foreach (var line in section.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var match = Regex.Match(line,
                @"^\s*(?:\d+[.)]|[-*])\s+\*\*(?<label>.+?)\*\*\s*:?[ \t]*(?<description>.*)$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (match.Success)
            {
                Flush();
                pendingLabel = match.Groups["label"].Value;
                pendingDescription.Append(match.Groups["description"].Value.Trim());
                continue;
            }
            if (pendingLabel is not null && !string.IsNullOrWhiteSpace(line)
                && !line.TrimStart().StartsWith('|') && !line.TrimStart().StartsWith('#'))
                pendingDescription.Append(' ').Append(line.Trim().TrimStart('-', '*', ' '));
        }
        Flush();

        if (capabilities.Count == 0)
        {
            foreach (var line in section.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var cells = Cells(line);
                if (cells.Length < 2 || cells.All(cell => cell.All(character => character is '-' or ':' or ' '))) continue;
                if (cells.Any(cell => cell.Equals("Capability", StringComparison.OrdinalIgnoreCase)
                                      || cell.Equals("Business capability", StringComparison.OrdinalIgnoreCase))) continue;
                var firstRequirements = RequirementIds(cells[0]);
                var nameIndex = firstRequirements.Count > 0 && cells.Length > 2 ? 1 : 0;
                var name = CleanInlineMarkdown(cells[nameIndex]).Trim();
                if (name.Length == 0) continue;
                var description = CleanInlineMarkdown(string.Join(" ", cells.Skip(nameIndex + 1)));
                if (description.Length == 0) description = $"Own the business capability described by {name}.";
                var requirements = RequirementIds(string.Join(" ", cells));
                capabilities.Add(new BusinessCapability(name, description, requirements,
                    $"BRD Business capabilities and processes: table row {name}"));
            }
        }
        return capabilities.Take(24).ToArray();
    }

    private static string CleanInlineMarkdown(string value)
    {
        var cleaned = Regex.Replace(value, @"\[(?<label>[^]]+)\]\([^)]+\)", "${label}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        cleaned = cleaned.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal).Trim();
        return cleaned.Length <= 1600 ? cleaned : cleaned[..1597].TrimEnd() + "...";
    }

    private static IReadOnlyList<KnownStandard> DiscoverStandards(CisWorkspaceRepository repository, ICollection<string> warnings)
    {
        var directory = Path.Combine(repository.RepositoryPath,
            repository.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "standards");
        if (!Directory.Exists(directory)) return [];
        var standards = new List<KnownStandard>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var content = File.ReadAllText(path);
                var id = ReadNestedFrontMatter(content, "stable_id");
                var type = ReadFrontMatter(content, "type");
                if (string.IsNullOrWhiteSpace(id) || !string.Equals(type, "standard", StringComparison.OrdinalIgnoreCase)) continue;
                standards.Add(new KnownStandard(repository.Id, repository.Participation, id, ReadFrontMatter(content, "title") ?? Path.GetFileNameWithoutExtension(path),
                    ReadFrontMatter(content, "status") ?? "Unknown", ReadFrontMatterSequence(content, "targets"),
                    ReadFrontMatterSequence(content, "stacks").Concat(ReadFrontMatterSequence(content, "stack")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    StandardRuleIdPattern().Matches(content).Select(match => match.Groups["id"].Value).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                    NormalizePath(Path.GetRelativePath(repository.RepositoryPath, path)), Hash(content)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Unable to read standard '{NormalizePath(Path.GetRelativePath(repository.RepositoryPath, path))}' for technical-intent scaffolding: {exception.Message}");
            }
        }
        return standards;
    }

    private static IReadOnlyList<string> ReadFrontMatterSequence(string content, string key)
    {
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return [];
        var lines = content[..end].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Trim().Equals(key + ":", StringComparison.OrdinalIgnoreCase)) continue;
            var values = new List<string>();
            for (var next = index + 1; next < lines.Length; next++)
            {
                if (!char.IsWhiteSpace(lines[next].FirstOrDefault())) break;
                var value = lines[next].Trim();
                if (value.StartsWith("- ", StringComparison.Ordinal) && value.Length > 2) values.Add(value[2..].Trim().Trim('"', '\''));
            }
            return values;
        }
        return [];
    }

    private static string FirstParagraph(string content)
    {
        var paragraph = Regex.Split(content.Trim(), "(?:\\r?\\n){2,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        paragraph = string.Join(" ", Regex.Split(paragraph, @"(?<=[.!?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Where(sentence => !sentence.Contains("Review Required", StringComparison.OrdinalIgnoreCase)
                               && !sentence.Contains("Ready for Approval", StringComparison.OrdinalIgnoreCase)
                               && !sentence.Contains("evidence-backed draft", StringComparison.OrdinalIgnoreCase)));
        return paragraph.Length <= 1600 ? paragraph : paragraph[..1597].TrimEnd() + "...";
    }

    private static string Basis(State state, params string[] targets)
    {
        var ids = StandardsFor(state, targets);
        return ids.Count == 0 ? "Active BRD and repository classification" : string.Join(", ", ids);
    }

    private static IReadOnlyList<string> StandardsFor(State state, params string[] targets)
        => state.Standards.Where(item => item.Participation == "owned"
                                         && item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)
                                         && item.Targets.Concat(item.Stacks).Any(value => targets.Contains(value, StringComparer.OrdinalIgnoreCase)))
            .Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool HasAny(string content, params string[] signals)
        => signals.Any(signal => content.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string RenderInlineCodes(IEnumerable<string> values)
    {
        var selected = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return selected.Length == 0 ? "none detected" : string.Join(", ", selected.Select(value => $"`{value.Replace("`", string.Empty, StringComparison.Ordinal)}`"));
    }

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var selected = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return selected.Length == 0 ? "none detected" : string.Join(", ", selected);
    }

    private static string EnsureMetadata(string content)
    {
        content = EnsureTopLevelFrontMatter(content, "scope", "Workspace", beforeKey: "owner");
        content = EnsureTopLevelFrontMatter(content, "last_reviewed", "null", beforeKey: "review_cadence");
        content = EnsureNestedFrontMatter(content, "technical_intent_schema", "1");
        content = EnsureNestedFrontMatter(content, "approved_by", "null");
        content = EnsureNestedFrontMatter(content, "approved_at", "null");
        content = EnsureNestedFrontMatter(content, "approval_reason", "null");
        content = EnsureNestedFrontMatter(content, "approved_content_hash", "null");
        return content;
    }

    private static string ReplaceOrInsertBaseline(string content, IReadOnlyList<TechnicalIntentBaseline> baselines)
    {
        var rendered = RenderBaselines(baselines).TrimEnd();
        if (content.Contains(BaselineStart, StringComparison.Ordinal) && content.Contains(BaselineEnd, StringComparison.Ordinal))
            return ReplaceBlock(content, BaselineStart, BaselineEnd, rendered);
        var section = $"## CIS technical baseline\n\n{BaselineStart}\n{rendered}\n{BaselineEnd}\n\n";
        var firstSection = content.IndexOf("\n## ", StringComparison.Ordinal);
        return firstSection < 0 ? content.TrimEnd() + "\n\n" + section : content.Insert(firstSection + 1, section);
    }

    private static string RenderBaselines(IReadOnlyList<TechnicalIntentBaseline> baselines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Kind | ID | Version | Evidence |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var item in baselines)
            builder.AppendLine($"| {Cell(item.Kind)} | {Cell(item.Id)} | {Cell(item.Version)} | {Cell(item.Evidence)} |");
        return builder.ToString();
    }

    private static Dictionary<string, string> ParseBaselines(string content)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in ReadBlock(content, BaselineStart, BaselineEnd).Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length == 4 && cells[0] is not ("Kind" or "---")) rows[cells[0] + "\u001f" + cells[1]] = cells[2];
        }
        return rows;
    }

    private static IReadOnlyList<DecisionRow> ParseDecisionRows(string content)
    {
        var rows = new List<DecisionRow>();
        foreach (var line in content.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length == 5 && cells[0].StartsWith("TI-DEC-", StringComparison.Ordinal))
                rows.Add(new DecisionRow(cells[0], cells[1], cells[2], cells[3], cells[4]));
        }
        return rows;
    }

    private static string[] Cells(string line)
        => !line.TrimStart().StartsWith('|') ? [] : Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();

    private static string ExtractSection(string content, string heading)
    {
        var match = Regex.Match(content, $"(?ms)^## {Regex.Escape(heading)}\\s*$\\n(?<body>.*?)(?=^## |\\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
    }

    private static string EnsureTopLevelFrontMatter(string content, string key, string value, string beforeKey)
    {
        if (ReadFrontMatter(content, key) is not null) return ReplaceFrontMatter(content, key, value);
        return Regex.Replace(content, $"(?m)^{Regex.Escape(beforeKey)}:", $"{key}: {value}\n{beforeKey}:",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string EnsureNestedFrontMatter(string content, string key, string value)
    {
        if (ReadNestedFrontMatter(content, key) is not null) return content;
        return Regex.Replace(content, "(?m)^(  stable_id:.*)$", $"$1\n  {key}: {value}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string ReplaceFrontMatter(string content, string key, string value)
        => Regex.Replace(content, $"(?m)^{Regex.Escape(key)}:.*$", $"{key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string ReplaceNestedFrontMatter(string content, string key, string value)
        => Regex.Replace(content, $"(?m)^  {Regex.Escape(key)}:.*$", $"  {key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? ReadFrontMatter(string content, string key) => ReadFrontMatterValue(content, $"^{Regex.Escape(key)}:");
    private static string? ReadNestedFrontMatter(string content, string key) => ReadFrontMatterValue(content, $"^  {Regex.Escape(key)}:");

    private static string? ReadFrontMatterValue(string content, string prefix)
    {
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return null;
        var match = Regex.Match(content[..end], $"(?m){prefix}\\s*(?<value>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? Unquote(match.Groups["value"].Value.Trim()) : null;
    }

    private static string Unquote(string value)
    {
        if (!value.StartsWith('"')) return value;
        try { return JsonSerializer.Deserialize<string>(value) ?? string.Empty; }
        catch (JsonException) { return value.Trim('"'); }
    }

    private static string ReplaceBlock(string content, string start, string end, string replacement)
    {
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < startIndex) return content;
        var replacementStart = startIndex + start.Length;
        return content[..replacementStart] + "\n" + replacement.TrimEnd() + "\n" + content[endIndex..];
    }

    private static string ReadBlock(string content, string start, string end)
    {
        content = CisTechnicalIntentPresentation.RestoreManagedEvidence(content);
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        return startIndex < 0 || endIndex < startIndex ? string.Empty : content[(startIndex + start.Length)..endIndex].Trim();
    }

    private static string ContentDigest(string content)
    {
        content = CisTechnicalIntentPresentation.RestoreManagedEvidence(content);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
            normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        normalized = RemoveManagedBlock(normalized, BaselineStart, BaselineEnd);
        return Hash(normalized.TrimEnd());
    }

    private static string LegacyContentDigest(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
            normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Hash(normalized.TrimEnd());
    }

    private static bool ApprovalDigestMatches(string content, string approvedHash)
        => approvedHash == ContentDigest(content) || approvedHash == LegacyContentDigest(content);

    private static bool HasCurrentApproval(string content)
        => string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
           && ReadNestedFrontMatter(content, "approved_by") is { Length: > 0 } reviewer && reviewer != "null"
           && ReadNestedFrontMatter(content, "approval_reason") is { Length: > 0 } reason && reason != "null"
           && ReadNestedFrontMatter(content, "approved_content_hash") is { Length: > 0 } digest && digest != "null"
           && ApprovalDigestMatches(content, digest);

    private static bool BaselinesCanCarryForward(
        string content,
        IReadOnlyList<TechnicalIntentBaseline> baselines,
        string? currentBrdContent)
    {
        var recorded = ParseBaselines(content);
        if (recorded.Count != baselines.Count) return false;
        foreach (var baseline in baselines)
        {
            var key = baseline.Kind + "\u001f" + baseline.Id;
            if (!recorded.TryGetValue(key, out var version)) return false;
            if (baseline.Kind.Equals("repository", StringComparison.OrdinalIgnoreCase)) continue;
            if (version == baseline.Version) continue;
            if (baseline.Kind.Equals("brd", StringComparison.OrdinalIgnoreCase)
                && currentBrdContent is not null
                && version == Hash(currentBrdContent)
                && baseline.Version == "semantic-v1:" + BrdDocumentDigest.Compute(currentBrdContent))
                continue;
            return false;
        }
        return true;
    }

    private static string RemoveManagedBlock(string content, string start, string end)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var startIndex = normalized.IndexOf(start, StringComparison.Ordinal);
        var endIndex = normalized.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < startIndex) return normalized;
        var after = endIndex + end.Length;
        while (after < normalized.Length && normalized[after] == '\n') after++;
        return normalized[..startIndex] + normalized[after..];
    }

    private static string Hash(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    private static bool Equivalent(string left, string right) => left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    private static string NormalizePath(string path) => path.Replace('\\', '/');
    private static void Write(string path, string content)
    {
        if (content.Contains("<!-- cis:technical-intent-implementation-authored -->", StringComparison.Ordinal))
            content = CisTechnicalIntentPresentation.HideManagedEvidence(content);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    }

    private static string UpdateCatalogStatus(string catalog, string id, string status)
    {
        var lines = catalog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() != $"- id: {id}") continue;
            for (var entry = index + 1; entry < lines.Length && !lines[entry].StartsWith("  - id:", StringComparison.Ordinal); entry++)
                if (lines[entry].TrimStart().StartsWith("status:", StringComparison.Ordinal))
                {
                    lines[entry] = "    status: " + status;
                    return string.Join('\n', lines);
                }
        }
        return catalog;
    }

    private static TechnicalIntentResult Error(string status, State state, params string[] errors)
        => new(status, state.Workspace?.WorkspacePath, state.Authority?.Id,
            state.CanonicalPath is null || state.Authority is null ? null : NormalizePath(Path.GetRelativePath(state.Authority.RepositoryPath, state.CanonicalPath)),
            null, state.Baselines, state.Warnings, errors.Length == 0 ? state.Errors : errors, false);

    [GeneratedRegex("\\b(?:TODO|TBD)\\b|(?i:\\bTO BE COMPLETED\\b)", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\bBRD?-(?:FR|NFR)-\d{3,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequirementIdPattern();

    [GeneratedRegex(@"\*\*(?<id>[A-Z][A-Z0-9-]+-\d{3,})\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex StandardRuleIdPattern();

    private sealed record State(CisWorkspace? Workspace, CisWorkspaceRepository? Authority,
        CisRepositoryContext? Context, string? CanonicalPath, IReadOnlyList<TechnicalIntentBaseline> Baselines,
        IReadOnlyList<TechnicalSurface> Surfaces, IReadOnlyList<KnownStandard> Standards,
        BusinessEvidence? Brd, QuestionnaireSnapshot? Questionnaire, bool ScaffoldEligible,
        IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, IReadOnlyList<string> ReadinessErrors);

    private sealed record DecisionRow(string Id, string Decision, string RequiredBefore, string Status, string Resolution);
    private sealed record BusinessEvidence(string Title, string RelativePath, string Digest, string Summary,
        IReadOnlyList<string> Outcomes, IReadOnlyList<BusinessCapability> Capabilities,
        IReadOnlyList<string> RequirementIds, string Content);
    private sealed record BusinessCapability(string Name, string Description, IReadOnlyList<string> RequirementIds, string Source);
    private sealed record LogicalModule(string Id, string Name, string Classification, string Purpose, string Ownership,
        string Exclusions, IReadOnlyList<string> RequirementIds, string Evidence, string Inputs, string Outputs,
        string DataOwnership, string FailureAndRecovery);
    private sealed record IntegrationPoint(string Id, string Source, string Flow, string Target, string Contract,
        string Delivery, string Trust, string FailureAndRecovery, IReadOnlyList<string> RequirementIds);
    private sealed record TechnicalSurface(string RepositoryId, string WorkspaceRole, string Participation, string Relationship, string Component, string Root,
        IReadOnlyList<string> Languages, IReadOnlyList<string> Frameworks, IReadOnlyList<string> Roles,
        IReadOnlyList<string> Capabilities, string Confidence);
    private sealed record KnownStandard(string RepositoryId, string Participation, string Id, string Title, string Status,
        IReadOnlyList<string> Targets, IReadOnlyList<string> Stacks, IReadOnlyList<string> RuleIds,
        string Path, string Digest);
    private sealed record ArchitectureGuideline(string Direction, string Basis);
}
