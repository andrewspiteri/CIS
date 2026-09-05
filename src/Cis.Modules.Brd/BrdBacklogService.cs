using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd;

public sealed class BrdBacklogService
{
    private const string ItemsStart = "<!-- cis:brd-backlog-items:start -->";
    private const string ItemsEnd = "<!-- cis:brd-backlog-items:end -->";
    private const string ObligationsStart = "<!-- cis:brd-backlog-obligations:start -->";
    private const string ObligationsEnd = "<!-- cis:brd-backlog-obligations:end -->";

    private readonly BrdService _brd;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly RepositoryClassifier _classifier;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly IReadOnlyList<IChangeReadinessCheck> _readinessChecks;
    private readonly IReadOnlyList<ICisProductDefinitionAuthority> _productDefinitionAuthorities;
    private readonly ICisWorkspaceRegistry _workspaceRegistry;

    public BrdBacklogService(
        BrdService brd,
        ICisWorkspaceRegistry workspaceRegistry,
        ICisRepositoryContextResolver repositoryResolver,
        DocumentationCatalogMerger catalogMerger,
        IEnumerable<IChangeReadinessCheck>? readinessChecks = null,
        Func<DateTimeOffset>? clock = null,
        RepositoryClassifier? classifier = null,
        IEnumerable<ICisProductDefinitionAuthority>? productDefinitionAuthorities = null)
    {
        _brd = brd;
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _catalogMerger = catalogMerger;
        _classifier = classifier ?? new RepositoryClassifier();
        _readinessChecks = readinessChecks?.ToArray() ?? [];
        _productDefinitionAuthorities = productDefinitionAuthorities?.ToArray() ?? [];
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public BrdBacklogResult Build(string workspacePath)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        var readinessErrors = ReadinessErrors(state);
        if (readinessErrors.Count > 0) return Error("blocked", state, readinessErrors);

        var brdContent = File.ReadAllText(state.BrdPath!);
        var requirements = ParseRequirements(brdContent, "Functional requirements");
        if (requirements.Count == 0)
            return Error("blocked", state, ["The Active BRD contains no structured functional requirements."]);
        var obligations = ParseRequirements(brdContent, "Quality, regulatory, and operational requirements")
            .Concat(ParseRequirements(brdContent, "Success measures"))
            .ToArray();
        var existingContent = File.Exists(state.BacklogPath!) ? File.ReadAllText(state.BacklogPath!) : null;
        var existing = existingContent is not null
            ? ParseItems(existingContent).ToDictionary(item => item.RequirementId, StringComparer.Ordinal)
            : new Dictionary<string, BrdBacklogItem>(StringComparer.Ordinal);
        var routableRepositories = state.Workspace!.DeliveryRepositories.ToArray();
        if (routableRepositories.Length == 0 && state.Workspace.AuthorityRepository is { } authorityRepository)
            routableRepositories = [authorityRepository];
        var repositoryRoles = routableRepositories
            .ToDictionary(repository => repository.Id, repository => _classifier.Classify(repository.RepositoryPath)
                .Components.SelectMany(component => component.Roles).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var migrateDependencies = existingContent is not null
            && ReadNestedFrontMatter(existingContent, "backlog_schema") != "3"
            && !string.Equals(ReadFrontMatter(existingContent, "status"), "Active", StringComparison.OrdinalIgnoreCase);
        var items = requirements.Select(requirement => CreateItem(requirement, requirements, routableRepositories,
            repositoryRoles, existing, migrateDependencies)).ToArray();
        var title = ReadFrontMatter(brdContent, "title") ?? "Business Requirements";
        var next = Render(title, state, items, obligations);
        var approvalCanCarryForward = existingContent is not null
            && HasCurrentApproval(existingContent)
            && ContentDigest(existingContent) == ContentDigest(next)
            && UpstreamBaselinesCanCarryForward(existingContent,
                BrdBaseline(File.ReadAllText(state.BrdPath!)),
                TechnicalIntentBaseline(File.ReadAllText(state.TechnicalIntentPath!)));
        if (approvalCanCarryForward)
            next = CarryApproval(existingContent!, next);

        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var stableId = $"{state.Authority!.Id}:plan:high-level-backlog";
        var merge = _catalogMerger.Merge(state.Authority.Id, catalog,
            [new CatalogArtifactEntry(stableId, state.RelativePath!, "high-level-backlog", "review-required", "canonical")]);
        if (merge.Collisions.Count > 0) return Error("collision", state, merge.Collisions);
        var nextCatalog = UpdateCatalogStatus(merge.Content, stableId,
            approvalCanCarryForward ? "active" : "review-required");
        var contentChanged = !File.Exists(state.BacklogPath!) || !Equivalent(File.ReadAllText(state.BacklogPath!), next);
        var catalogChanged = !Equivalent(catalog, nextCatalog);
        if (!contentChanged && !catalogChanged)
            return ValidateInternal(workspacePath, "unchanged", applied: false);

        Directory.CreateDirectory(Path.GetDirectoryName(state.BacklogPath!)!);
        if (contentChanged) Write(state.BacklogPath!, next);
        if (catalogChanged) Write(state.Context.CatalogPath, nextCatalog);
        return ValidateInternal(workspacePath, "built", applied: true);
    }

    public BrdBacklogResult Validate(string workspacePath)
    {
        var result = ValidateInternal(workspacePath, "validated", applied: false);
        return result.Validation is { Valid: true, Current: false }
            ? result with { Status = "blocked" }
            : result;
    }

    public BrdBacklogResult Status(string workspacePath) => ValidateInternal(workspacePath, "status", applied: false);

    public BrdFeatureSpecificationResult StartFeature(string workspacePath, string itemId, string? slug = null)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return FeatureError("invalid", state, itemId, state.Errors);
        var assessed = Status(workspacePath);
        if (assessed.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            return FeatureError("blocked", state, itemId,
                ["An Active, current high-level backlog is required. Run `cis brd backlog status`."]);
        var productDefinition = ProductDefinition(state);
        if (productDefinition is { Active: false })
            return FeatureError("blocked", state, itemId,
                productDefinition.Errors.Count > 0
                    ? productDefinition.Errors
                    : ["The complete high-level product definition must be consolidated and activated. Run `cis definition status`."]);
        var normalizedId = itemId.Trim().ToUpperInvariant();
        var item = assessed.Items.SingleOrDefault(candidate => candidate.Id == normalizedId);
        if (item is null)
            return FeatureError("missing", state, normalizedId, [$"High-level backlog item was not found: {normalizedId}"]);
        var blockedBy = item.DependsOn
            .Select(dependency => assessed.Items.Single(candidate => candidate.Id == dependency))
            .Where(dependency => dependency.FeatureSpecification == "not-created")
            .Select(dependency => dependency.Id)
            .ToArray();
        if (blockedBy.Length > 0)
            return FeatureError("blocked", state, normalizedId,
                [$"Feature specification dependencies have not been started: {string.Join(", ", blockedBy)}"]);

        var featureSlug = string.IsNullOrWhiteSpace(slug) ? normalizedId.ToLowerInvariant() : slug.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(featureSlug, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            return FeatureError("invalid", state, normalizedId, ["Feature slug must contain lowercase letters, numbers, and single hyphens only."]);
        var docs = state.Authority!.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(state.Authority.RepositoryPath, docs, "specs", "features", featureSlug, "feature-specification.md");
        var relativePath = Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, path));
        if (item.FeatureSpecification != "not-created" && item.FeatureSpecification != relativePath)
            return FeatureError("collision", state, normalizedId,
                [$"High-level item already links a different feature specification: {item.FeatureSpecification}"]);
        var stableId = $"{state.Authority.Id}:feature:{normalizedId.ToLowerInvariant()}";
        var requirement = ParseRequirements(File.ReadAllText(state.BrdPath!), "Functional requirements")
            .Single(candidate => candidate.Id == item.RequirementId);
        var existingFeatureContent = File.Exists(path) ? File.ReadAllText(path) : null;
        var featureContent = existingFeatureContent ?? RenderFeatureSpecification(state, item, requirement, stableId,
            productDefinition?.BaselineHash);
        if (File.Exists(path) && (!featureContent.Contains($"stable_id: {stableId}", StringComparison.Ordinal)
                                || !featureContent.Contains($"high_level_item: {normalizedId}", StringComparison.Ordinal)))
            return FeatureError("collision", state, normalizedId,
                ["Feature-specification path contains a document with a different stable identity or backlog item."]);

        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var merge = _catalogMerger.Merge(state.Authority.Id, catalog,
            [new CatalogArtifactEntry(stableId, relativePath, "feature-specification", "draft", "canonical")]);
        if (merge.Collisions.Count > 0) return FeatureError("collision", state, normalizedId, merge.Collisions);
        var backlog = File.ReadAllText(state.BacklogPath!);
        var nextBacklog = ReplaceFeatureSpecificationLink(backlog, normalizedId, relativePath);
        if (ReadFrontMatter(nextBacklog, "status") == "Active")
        {
            nextBacklog = ReplaceNestedFrontMatter(nextBacklog, "approved_content_hash", "null");
            nextBacklog = ReplaceNestedFrontMatter(nextBacklog, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(nextBacklog)));
        }
        featureContent = EnsureNestedFrontMatter(featureContent, "backlog_item_hash", ItemDigest(item));
        if (ReadFrontMatter(featureContent, "status") == "Draft")
            featureContent = ReplaceNestedFrontMatter(featureContent, "backlog_item_hash", ItemDigest(item));
        featureContent = EnsureNestedFrontMatter(featureContent, "approved_by", "null");
        featureContent = EnsureNestedFrontMatter(featureContent, "approved_at", "null");
        featureContent = EnsureNestedFrontMatter(featureContent, "approval_reason", "null");
        featureContent = EnsureNestedFrontMatter(featureContent, "approved_content_hash", "null");
        featureContent = RemoveNestedFrontMatter(featureContent, "backlog_hash");
        var featureChanged = existingFeatureContent is null || !Equivalent(existingFeatureContent, featureContent);
        var backlogChanged = !Equivalent(backlog, nextBacklog);
        var catalogChanged = !Equivalent(catalog, merge.Content);
        if (!featureChanged && !backlogChanged && !catalogChanged)
            return new BrdFeatureSpecificationResult("unchanged", state.Workspace!.WorkspacePath, state.Authority.Id,
                normalizedId, item.RequirementId, relativePath, [], false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (featureChanged) Write(path, featureContent);
        if (backlogChanged) Write(state.BacklogPath!, nextBacklog);
        if (catalogChanged) Write(state.Context.CatalogPath, merge.Content);
        return new BrdFeatureSpecificationResult("started", state.Workspace!.WorkspacePath, state.Authority.Id,
            normalizedId, item.RequirementId, relativePath, [], true);
    }

    public BrdFeatureResult ValidateFeature(string workspacePath, string itemId)
        => ValidateFeatureInternal(workspacePath, itemId, "validated", false);

    public BrdFeatureResult FeatureStatus(string workspacePath, string itemId)
        => ValidateFeatureInternal(workspacePath, itemId, "status", false);

    public BrdFeatureResult ApproveFeature(string workspacePath, string itemId, string reviewer, string reason)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return new BrdFeatureResult("invalid", workspacePath, null, itemId, null, null, null,
                ["Reviewer and approval reason are required."], false);
        var assessed = ValidateFeature(workspacePath, itemId);
        if (assessed.Errors.Count > 0 || assessed.Validation is not { Valid: true })
            return assessed with { Status = "blocked" };
        var feature = ResolveFeature(workspacePath, itemId);
        var content = File.ReadAllText(feature.Path!);
        if (ReadFrontMatter(content, "status") == "Active"
            && ReadNestedFrontMatter(content, "approved_by") == reviewer.Trim()
            && ReadNestedFrontMatter(content, "approval_reason") == reason.Trim()
            && assessed.Validation.EffectiveStatus == "Active")
            return assessed with { Status = "unchanged" };
        var backlog = Status(workspacePath);
        if (backlog.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            return new BrdFeatureResult("blocked", backlog.WorkspacePath, backlog.AuthorityRepositoryId, itemId,
                null, null, null, ["An Active, current high-level backlog is required for feature approval."], false);
        var now = _clock().ToUniversalTime();
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNestedFrontMatter(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNestedFrontMatter(content, "approval_reason", JsonSerializer.Serialize(reason.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(content)));
        Write(feature.Path!, content);
        var stableId = $"{feature.State!.Authority!.Id}:feature:{feature.Item!.Id.ToLowerInvariant()}";
        Write(feature.State.Context!.CatalogPath,
            UpdateCatalogStatus(File.ReadAllText(feature.State.Context.CatalogPath), stableId, "active"));
        return ValidateFeatureInternal(workspacePath, itemId, "approved", true);
    }

    public BrdBacklogResult Approve(string workspacePath, string reviewer, string reason)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return new BrdBacklogResult("invalid", workspacePath, null, null, [], null,
                ["Reviewer and approval reason are required."], false);
        var assessed = Validate(workspacePath);
        if (assessed.Errors.Count > 0 || assessed.Validation is not { Valid: true, Current: true })
            return assessed with { Status = "blocked" };
        var state = Resolve(workspacePath);
        var content = File.ReadAllText(state.BacklogPath!);
        if (ReadFrontMatter(content, "status") == "Active"
            && ReadNestedFrontMatter(content, "approved_by") == reviewer.Trim()
            && ReadNestedFrontMatter(content, "approval_reason") == reason.Trim())
            return assessed with { Status = "unchanged" };

        var now = _clock().ToUniversalTime();
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNestedFrontMatter(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNestedFrontMatter(content, "approval_reason", JsonSerializer.Serialize(reason.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(content)));
        Write(state.BacklogPath!, content);
        var stableId = $"{state.Authority!.Id}:plan:high-level-backlog";
        Write(state.Context!.CatalogPath, UpdateCatalogStatus(File.ReadAllText(state.Context.CatalogPath), stableId, "active"));
        return ValidateInternal(workspacePath, "approved", applied: true);
    }

    private BrdBacklogResult ValidateInternal(string workspacePath, string operation, bool applied)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        if (!File.Exists(state.BacklogPath!))
            return new BrdBacklogResult("missing", state.Workspace!.WorkspacePath, state.Authority!.Id,
                state.RelativePath, [], new BrdBacklogValidation(false, false, "Missing", "Missing",
                    ["High-level backlog was not found. Run `cis brd backlog build`."], []), [], false);

        var content = File.ReadAllText(state.BacklogPath!);
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!content.Contains(ItemsStart, StringComparison.Ordinal) || !content.Contains(ItemsEnd, StringComparison.Ordinal))
            errors.Add("Managed high-level backlog item markers are missing.");
        if (!content.Contains(ObligationsStart, StringComparison.Ordinal) || !content.Contains(ObligationsEnd, StringComparison.Ordinal))
            errors.Add("Managed global-obligation markers are missing.");
        var items = ParseItems(content);
        var brdContent = File.ReadAllText(state.BrdPath!);
        var requirements = ParseRequirements(brdContent, "Functional requirements");
        var requiredIds = requirements.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var duplicate in items.GroupBy(item => item.RequirementId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add($"BRD requirement is covered by more than one high-level item: {duplicate.Key}");
        foreach (var missing in requiredIds.Except(items.Select(item => item.RequirementId), StringComparer.Ordinal))
            errors.Add($"BRD functional requirement has no high-level item: {missing}");
        foreach (var unknown in items.Select(item => item.RequirementId).Except(requiredIds, StringComparer.Ordinal))
            errors.Add($"High-level item references an unknown BRD requirement: {unknown}");
        var ids = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var deliveryRepositoryIds = state.Workspace!.DeliveryRepositories
            .Select(repository => repository.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (deliveryRepositoryIds.Count == 0 && state.Workspace.AuthorityRepository is { } deliveryAuthority)
            deliveryRepositoryIds.Add(deliveryAuthority.Id);
        var dependencyRepositoryIds = state.Workspace.DependencyRepositories
            .Select(repository => repository.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Outcome) || Placeholder(item.Outcome)) errors.Add($"High-level item outcome is incomplete: {item.Id}");
            if (item.Repositories.Count == 0) errors.Add($"High-level item has no affected repository: {item.Id}");
            foreach (var repository in item.Repositories.Where(repository => !deliveryRepositoryIds.Contains(repository)))
                errors.Add(dependencyRepositoryIds.Contains(repository)
                    ? $"High-level item cannot route implementation to dependency repository '{repository}': {item.Id}"
                    : $"High-level item references a repository outside the product delivery boundary: {repository} ({item.Id})");
            if (item.FrontendTypes.Any(value => value is not ("public" or "customer" or "backoffice")))
                errors.Add($"High-level item has invalid frontend type: {item.Id}");
            foreach (var dependency in item.DependsOn.Where(dependency => !ids.Contains(dependency)))
                errors.Add($"High-level item dependency does not exist: {item.Id} -> {dependency}");
        }
        if (HasCycle(items)) errors.Add("High-level backlog dependencies contain a cycle.");

        var current = string.Equals(ReadNestedFrontMatter(content, "brd_hash"), BrdBaseline(brdContent), StringComparison.Ordinal)
            && string.Equals(ReadNestedFrontMatter(content, "technical_intent_hash"), TechnicalIntentBaseline(File.ReadAllText(state.TechnicalIntentPath!)), StringComparison.Ordinal);
        if (!current) warnings.Add("BRD or technical-intent content changed after the high-level backlog was built.");
        var readiness = ReadinessErrors(state);
        if (readiness.Count > 0)
        {
            current = false;
            warnings.AddRange(readiness);
        }
        var documentStatus = ReadFrontMatter(content, "status") ?? "Unknown";
        if (documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            var digest = ReadNestedFrontMatter(content, "approved_content_hash");
            if (string.IsNullOrWhiteSpace(digest) || digest == "null" || !ApprovalDigestMatches(content, digest))
            {
                current = false;
                warnings.Add("High-level backlog content changed after approval.");
            }
        }
        var valid = errors.Count == 0;
        var effective = documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? valid && current ? "Active" : "Stale"
            : valid && current ? "Ready for Approval" : "Review Required";
        var validation = new BrdBacklogValidation(valid, current, effective, documentStatus,
            errors.Distinct(StringComparer.Ordinal).Order().ToArray(), warnings.Distinct(StringComparer.Ordinal).Order().ToArray());
        return new BrdBacklogResult(operation, state.Workspace!.WorkspacePath, state.Authority!.Id,
            state.RelativePath, items, validation, [], applied);
    }

    private State Resolve(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
            return new State(null, null, null, null, null, null, resolution.Errors);
        var authority = resolution.Workspace.AuthorityRepository;
        if (authority is null) return new State(resolution.Workspace, null, null, null, null, null, ["Workspace has no authority repository."]);
        var contextResolution = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!contextResolution.IsSuccess || contextResolution.Context is null)
            return new State(resolution.Workspace, authority, null, null, null, null, contextResolution.Errors);
        var docs = authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar);
        var brdPath = Path.Combine(authority.RepositoryPath, docs, "specs", "business-requirements.md");
        var technicalPath = Path.Combine(authority.RepositoryPath, docs, "specs", "technical-intent-spec.md");
        var backlogPath = Path.Combine(authority.RepositoryPath, docs, "plans", "high-level-backlog.md");
        return new State(resolution.Workspace, authority, contextResolution.Context, brdPath, technicalPath,
            backlogPath, [], Normalize(Path.GetRelativePath(authority.RepositoryPath, backlogPath)));
    }

    private IReadOnlyList<string> ReadinessErrors(State state)
    {
        if (state.Authority is null || state.Workspace is null) return [];
        var errors = new List<string>();
        var brdStatus = _brd.Status(state.Workspace.WorkspacePath);
        if (brdStatus.Validation is not { } brdValidation
            || !CisDefinitionDraftScope.Accepts(brdValidation.Valid, brdValidation.Current, brdValidation.EffectiveStatus))
            errors.Add("An Active, current BRD is required. Run `cis brd status`.");
        if (!File.Exists(state.TechnicalIntentPath!))
            errors.Add("Technical intent is missing. Run `cis technical-intent init`.");
        errors.AddRange(_readinessChecks.Select(check => check.Evaluate(state.Authority.RepositoryPath))
            .Where(result => result.Applicable && !result.Ready)
            .SelectMany(result => result.Errors.Select(error => $"[{result.Check}] {error}")));
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private BrdFeatureResult ValidateFeatureInternal(string workspacePath, string itemId, string operation, bool applied)
    {
        var feature = ResolveFeature(workspacePath, itemId);
        if (feature.Errors.Count > 0)
            return new BrdFeatureResult(feature.Path is null ? "missing" : "invalid", feature.State?.Workspace?.WorkspacePath,
                feature.State?.Authority?.Id, itemId.Trim().ToUpperInvariant(), feature.Item?.RequirementId,
                feature.RelativePath, null, feature.Errors, applied);

        var content = File.ReadAllText(feature.Path!);
        var errors = new List<string>();
        var warnings = new List<string>();
        var item = feature.Item!;
        var authority = feature.State!.Authority!;
        var stableId = $"{authority.Id}:feature:{item.Id.ToLowerInvariant()}";
        if (ReadFrontMatter(content, "type") != "feature-specification")
            errors.Add("Feature specification front matter must declare `type: feature-specification`.");
        if (ReadNestedFrontMatter(content, "stable_id") != stableId)
            errors.Add($"Feature specification stable identity must be `{stableId}`.");
        if (ReadNestedFrontMatter(content, "high_level_item") != item.Id)
            errors.Add($"Feature specification must reference high-level item `{item.Id}`.");
        if (ReadNestedFrontMatter(content, "brd_requirement") != item.RequirementId)
            errors.Add($"Feature specification must reference BRD requirement `{item.RequirementId}`.");
        foreach (var repository in item.Repositories.Where(repository =>
                     !Regex.IsMatch(content, $"(?m)^  - {Regex.Escape(repository)}\\s*$", RegexOptions.CultureInvariant,
                         TimeSpan.FromSeconds(1))))
            errors.Add($"Feature specification targets do not include backlog repository `{repository}`.");

        string[] requiredSections =
        [
            "Feature summary", "Goals", "Non-goals and explicit exclusions", "Actors and scenarios",
            "Functional requirements", "Workflows, states, and invariants", "Domain model, data, audit, and migrations",
            "API, contracts, permissions, and visibility", "UX, screens, and accessibility",
            "Cross-module integrations, commands, and events", "Search, projection, and retrieval boundaries",
            "Lifecycle, conversion, and carry-forward", "Operational and security considerations",
            "Testing and regression requirements", "Traceability and related decisions",
        ];
        foreach (var section in requiredSections.Where(section => string.IsNullOrWhiteSpace(Section(content, section))))
            errors.Add($"Required feature-specification section is missing or empty: {section}");
        if (Placeholder(content)) errors.Add("Feature specification contains TODO, TBD, or TO BE COMPLETED placeholders.");

        var requirements = ParseFeatureRequirements(content);
        if (requirements.Count == 0) errors.Add("Feature specification contains no structured FEAT requirements.");
        var allowedSurfaces = new HashSet<string>(
            ["frontend", "backend", "full-stack", "mobile", "native", "api", "contract", "data", "security", "delivery", "documentation"],
            StringComparer.Ordinal);
        var allowedFrontendTypes = new HashSet<string>(["public", "customer", "backoffice", "not-applicable"], StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            if (!allowedSurfaces.Contains(requirement.Surface))
                errors.Add($"Feature requirement has invalid surface: {requirement.Id} -> {requirement.Surface}");
            if (!allowedFrontendTypes.Contains(requirement.FrontendType))
                errors.Add($"Feature requirement has invalid frontend type: {requirement.Id} -> {requirement.FrontendType}");
            if (string.IsNullOrWhiteSpace(requirement.Requirement) || Placeholder(requirement.Requirement))
                errors.Add($"Feature requirement is incomplete: {requirement.Id}");
            if (string.IsNullOrWhiteSpace(requirement.Acceptance) || Placeholder(requirement.Acceptance))
                errors.Add($"Feature requirement acceptance criteria are incomplete: {requirement.Id}");
        }
        var uiSurfaces = new HashSet<string>(["frontend", "full-stack", "mobile", "native"], StringComparer.Ordinal);
        foreach (var frontendType in item.FrontendTypes.Where(frontendType => !requirements.Any(requirement =>
                     requirement.FrontendType == frontendType && uiSurfaces.Contains(requirement.Surface))))
            errors.Add($"Backlog frontend type `{frontendType}` has no matching UI-surface feature requirement.");

        var recordedItemDigest = ReadNestedFrontMatter(content, "backlog_item_hash");
        var current = string.Equals(recordedItemDigest, ItemDigest(item), StringComparison.Ordinal);
        if (!current) warnings.Add("High-level backlog item changed after the feature specification was started.");
        var productDefinition = ProductDefinition(feature.State);
        if (productDefinition is { Active: false })
        {
            current = false;
            errors.AddRange(productDefinition.Errors.Count > 0
                ? productDefinition.Errors.Select(error => "Product-definition baseline: " + error)
                : ["Product-definition baseline: the complete high-level product definition is not activated."]);
        }
        else if (productDefinition is { Active: true, BaselineHash: not null })
        {
            var recordedProductDefinition = ReadNestedFrontMatter(content, "product_definition_hash");
            if (!string.Equals(recordedProductDefinition, productDefinition.BaselineHash, StringComparison.Ordinal))
            {
                current = false;
                errors.Add("Product-definition baseline: the feature specification was not authored from the current consolidated product definition.");
            }
        }
        var documentStatus = ReadFrontMatter(content, "status") ?? "Unknown";
        if (!documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            var backlog = Status(workspacePath);
            if (backlog.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            {
                current = false;
                warnings.Add("An Active, current high-level backlog is required before feature approval.");
            }
        }
        if (documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approved_by"))
                || ReadNestedFrontMatter(content, "approved_by") == "null"
                || string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approved_at"))
                || ReadNestedFrontMatter(content, "approved_at") == "null"
                || string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approval_reason"))
                || ReadNestedFrontMatter(content, "approval_reason") == "null")
                errors.Add("Active feature specification is missing approval authority, timestamp, or rationale.");
            var digest = ReadNestedFrontMatter(content, "approved_content_hash");
            if (string.IsNullOrWhiteSpace(digest) || digest == "null" || digest != ContentDigest(content))
            {
                current = false;
                warnings.Add("Feature specification content changed after approval.");
            }
        }

        var valid = errors.Count == 0;
        var effective = documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? valid && current ? "Active" : "Stale"
            : valid && current ? "Ready for Approval" : "Review Required";
        var validation = new BrdFeatureValidation(valid, current, effective, documentStatus,
            errors.Distinct(StringComparer.Ordinal).Order().ToArray(), warnings.Distinct(StringComparer.Ordinal).Order().ToArray());
        return new BrdFeatureResult(operation, feature.State.Workspace!.WorkspacePath, authority.Id, item.Id,
            item.RequirementId, feature.RelativePath, validation, [], applied);
    }

    private FeatureState ResolveFeature(string workspacePath, string itemId)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return new FeatureState(state, null, null, null, state.Errors);
        if (!File.Exists(state.BacklogPath!))
            return new FeatureState(state, null, null, null, ["High-level backlog was not found. Run `cis brd backlog build`."]);
        var normalizedId = itemId.Trim().ToUpperInvariant();
        var item = ParseItems(File.ReadAllText(state.BacklogPath!)).SingleOrDefault(candidate => candidate.Id == normalizedId);
        if (item is null) return new FeatureState(state, null, null, null, [$"High-level backlog item was not found: {normalizedId}"]);
        if (item.FeatureSpecification == "not-created")
            return new FeatureState(state, item, null, null, [$"Feature specification has not been started for {normalizedId}."]);
        if (!CisPathSafety.TryResolveUnderRoot(state.Authority!.RepositoryPath, item.FeatureSpecification, out var path)
            || CisPathSafety.ContainsReparsePoint(state.Authority.RepositoryPath, path))
            return new FeatureState(state, item, null, item.FeatureSpecification, ["Feature-specification path escapes the authority repository or traverses a linked directory."]);
        if (!File.Exists(path))
            return new FeatureState(state, item, null, item.FeatureSpecification, [$"Feature specification file was not found: {item.FeatureSpecification}"]);
        return new FeatureState(state, item, path, item.FeatureSpecification, []);
    }

    private static BrdBacklogItem CreateItem(Requirement requirement, IReadOnlyList<Requirement> requirements,
        IReadOnlyList<CisWorkspaceRepository> routableRepositories,
        IReadOnlyDictionary<string, HashSet<string>> repositoryRoles,
        IReadOnlyDictionary<string, BrdBacklogItem> existing, bool migrateDependencies)
    {
        var text = requirement.Outcome + " " + requirement.Text + " " + requirement.Acceptance;
        var participants = routableRepositories.ToArray();
        var localRuntime = HasAny(text, "local startup", "clean environment", "smoke tests");
        var infrastructure = HasAny(text, "infrastructure", "health check", "persistent storage", "configuration boundaries");
        var authentication = HasAny(text, "authentication", "session", "sessions", "sign-in", "registration", "supertokens");
        var frontendOnly = HasAny(text, "web application", "screen", "screens", "wireframe", "wireframes", "visual design", "visual designs")
            && !HasAny(text, "api", "endpoint", "backend", "storage", "database", "authentication route", "authentication routes");
        var repositories = participants
            .Where(repository => localRuntime
                || infrastructure && HasRepositoryRole(repository.Id, repositoryRoles, "infrastructure", "backend-api-producer", "worker", "database")
                || authentication && HasRepositoryRole(repository.Id, repositoryRoles, "frontend-consumer", "mobile-client", "native-frontend", "backend-api-producer", "infrastructure")
                || frontendOnly && HasRepositoryRole(repository.Id, repositoryRoles, "frontend-consumer", "mobile-client", "native-frontend")
                || !infrastructure && !authentication && !frontendOnly && HasRepositoryRole(repository.Id, repositoryRoles, "frontend-consumer", "mobile-client", "native-frontend", "backend-api-producer"))
            .Select(repository => repository.Id).ToArray();
        if (repositories.Length == 0) repositories = participants.Select(repository => repository.Id).ToArray();
        var frontend = new List<string>();
        if (HasAny(text, "public", "unauthenticated", "visitor", "sign-in", "registration")) frontend.Add("public");
        if (!infrastructure && HasAny(text, "user", "owner", "member", "customer", "web", "todo", "list", "session", "sessions")) frontend.Add("customer");
        var id = BacklogItemId(requirement.Id);
        existing.TryGetValue(requirement.Id, out var previous);
        var inferredDependencies = InferDependencies(requirement, requirements);
        var dependencies = previous is null || migrateDependencies
            ? inferredDependencies
            : previous.DependsOn;
        return new BrdBacklogItem(id, requirement.Id, requirement.Outcome, requirement.Priority, repositories,
            frontend.Distinct(StringComparer.Ordinal).ToArray(), dependencies,
            previous?.FeatureSpecification ?? "not-created", previous?.Notes ?? string.Empty);
    }

    private static IReadOnlyList<string> InferDependencies(Requirement requirement, IReadOnlyList<Requirement> requirements)
    {
        var text = requirement.Outcome + " " + requirement.Text + " " + requirement.Acceptance;
        var authentication = requirements.FirstOrDefault(candidate =>
            HasAny(candidate.Text + " " + candidate.Acceptance, "sign-in", "registration", "authentication")
            && HasAny(candidate.Text + " " + candidate.Acceptance, "session", "sessions", "identity"));
        var infrastructure = requirements.FirstOrDefault(candidate =>
            HasAny(candidate.Text + " " + candidate.Acceptance, "infrastructure", "local startup")
            && HasAny(candidate.Text + " " + candidate.Acceptance, "persistent storage", "health check", "supertokens"));
        var aggregate = requirements.FirstOrDefault(candidate => Regex.IsMatch(candidate.Text,
            @"(?i)(?<![A-Za-z0-9])create(?![A-Za-z0-9]).{0,80}(?<![A-Za-z0-9])(?:todo\s+)?list(?![A-Za-z0-9])",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        var sharedLink = requirements.FirstOrDefault(candidate =>
            HasAny(candidate.Text + " " + candidate.Acceptance, "common link", "access-request link")
            && HasAny(candidate.Text + " " + candidate.Acceptance, "copy", "share", "replace"));

        Requirement? dependency = null;
        var linkConsumer = HasAny(text, "access request") && HasAny(text, "recipient", "approve", "reject");
        var aggregateConsumer = HasAny(text, "list", "lists")
            && HasAny(text, "todo", "shared", "link", "access request", "member", "membership");
        var authenticatedConsumer = HasAny(text, "authenticated", "user", "owner", "member", "session", "sessions");
        if (linkConsumer && sharedLink is not null && sharedLink.Id != requirement.Id)
            dependency = sharedLink;
        else if (aggregateConsumer && aggregate is not null && aggregate.Id != requirement.Id)
            dependency = aggregate;
        else if (authenticatedConsumer && authentication is not null && authentication.Id != requirement.Id)
            dependency = authentication;
        else if (authentication?.Id == requirement.Id && infrastructure is not null && infrastructure.Id != requirement.Id
                 && HasAny(text, "supertokens"))
            dependency = infrastructure;

        return dependency is null
            ? []
            : ["HLT-" + dependency.Id.Replace("BRD-", string.Empty, StringComparison.Ordinal)];
    }

    private static bool HasRepositoryRole(string repositoryId,
        IReadOnlyDictionary<string, HashSet<string>> repositoryRoles, params string[] roles)
        => repositoryRoles.TryGetValue(repositoryId, out var actual)
           && roles.Any(actual.Contains);

    private static bool HasAny(string text, params string[] terms)
        => terms.Any(term => Regex.IsMatch(text,
            $@"(?i)(?<![A-Za-z0-9]){Regex.Escape(term).Replace("\\ ", @"\s+")}(?![A-Za-z0-9])",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    private static string Render(string brdTitle, State state, IReadOnlyList<BrdBacklogItem> items, IReadOnlyList<Requirement> obligations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: \"{Cell(brdTitle)} High-Level Backlog\"");
        builder.AppendLine("type: high-level-backlog");
        builder.AppendLine("status: Review Required");
        builder.AppendLine("owner: Product owner and delivery leads");
        builder.AppendLine("last_reviewed: null");
        builder.AppendLine("review_cadence: on approved BRD or technical-intent change");
        builder.AppendLine("cis:");
        builder.AppendLine($"  stable_id: {state.Authority!.Id}:plan:high-level-backlog");
        builder.AppendLine("  backlog_schema: 3");
        builder.AppendLine($"  brd_hash: {BrdBaseline(File.ReadAllText(state.BrdPath!))}");
        builder.AppendLine($"  technical_intent_hash: {TechnicalIntentBaseline(File.ReadAllText(state.TechnicalIntentPath!))}");
        builder.AppendLine("  approved_by: null");
        builder.AppendLine("  approved_at: null");
        builder.AppendLine("  approval_reason: null");
        builder.AppendLine("  approved_content_hash: null");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {brdTitle} High-Level Backlog");
        builder.AppendLine();
        builder.AppendLine("This reviewed bridge decomposes each functional BRD requirement into one high-level product outcome. It is not an executable implementation plan. Accepted items become feature specifications and then change dossiers with bounded tasks.");
        builder.AppendLine();
        builder.AppendLine("## High-level items");
        builder.AppendLine();
        builder.AppendLine(ItemsStart);
        builder.AppendLine("| ID | BRD requirement | Outcome | Priority | Repositories | Frontend types | Depends on | Feature specification | Notes |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in items)
            builder.AppendLine($"| {item.Id} | {item.RequirementId} | {Cell(item.Outcome)} | {Cell(item.Priority)} | {Cell(string.Join(", ", item.Repositories))} | {Cell(item.FrontendTypes.Count == 0 ? "none" : string.Join(", ", item.FrontendTypes))} | {Cell(item.DependsOn.Count == 0 ? "none" : string.Join(", ", item.DependsOn))} | {Cell(item.FeatureSpecification)} | {Cell(item.Notes)} |");
        builder.AppendLine(ItemsEnd);
        builder.AppendLine();
        builder.AppendLine("## Global acceptance obligations");
        builder.AppendLine();
        builder.AppendLine("These requirements constrain every applicable feature specification and the final delivery sweep; they are not silently converted into implementation tasks here.");
        builder.AppendLine();
        builder.AppendLine(ObligationsStart);
        builder.AppendLine("| BRD requirement | Obligation |");
        builder.AppendLine("| --- | --- |");
        foreach (var obligation in obligations) builder.AppendLine($"| {obligation.Id} | {Cell(obligation.Text)} |");
        builder.AppendLine(ObligationsEnd);
        builder.AppendLine();
        builder.AppendLine("## Review rules");
        builder.AppendLine();
        builder.AppendLine("- Every functional BRD requirement must appear exactly once.");
        builder.AppendLine("- Reviewers may add dependencies, notes, and a feature-specification path without changing stable item identity.");
        builder.AppendLine("- Frontend types identify public, customer, and backoffice scope; a later feature specification splits mixed UI scope into one requirement row per type.");
        builder.AppendLine("- Approval authorizes feature-specification preparation, not implementation, deployment, or release.");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IReadOnlyList<Requirement> ParseRequirements(string content, string heading)
    {
        var section = Section(content, heading);
        var result = new List<Requirement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? currentId = null;
        string? currentOutcome = null;
        string? currentBody = null;

        void FlushNarrative()
        {
            if (currentId is null)
                return;

            var body = Regex.Replace(currentBody?.Trim() ?? string.Empty, @"\s+", " ",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var outcome = string.IsNullOrWhiteSpace(currentOutcome) ? body : currentOutcome.Trim();
            if (seen.Add(currentId) && !string.IsNullOrWhiteSpace(outcome))
                result.Add(new Requirement(currentId, outcome, body, "Must", body));
            currentId = null;
            currentOutcome = null;
            currentBody = null;
        }

        foreach (var line in section.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length >= 2 && IsBusinessRequirementId(cells[0]))
            {
                FlushNarrative();
                if (seen.Add(cells[0]))
                    result.Add(new Requirement(cells[0], cells[1], cells[1],
                        cells.Length > 2 ? cells[2] : "Must",
                        cells.Length > 3 ? cells[3] : string.Empty));
                continue;
            }

            if (TryParseNarrativeRequirement(line, out var id, out var outcome, out var body))
            {
                FlushNarrative();
                currentId = id;
                currentOutcome = outcome;
                currentBody = body;
                continue;
            }

            if (currentId is null)
                continue;

            var continuation = line.Trim();
            if (continuation.Length == 0)
                continue;
            if (continuation.StartsWith('|') || Regex.IsMatch(continuation, @"^[-*+]\s+",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                FlushNarrative();
                continue;
            }

            currentBody = string.IsNullOrWhiteSpace(currentBody)
                ? continuation
                : currentBody + " " + continuation;
        }

        FlushNarrative();
        return result;
    }

    private static bool TryParseNarrativeRequirement(string line, out string id, out string outcome, out string body)
    {
        id = string.Empty;
        outcome = string.Empty;
        body = string.Empty;
        var bullet = Regex.Match(line,
            @"^\s*[-*+]\s+\*\*(?<label>.+?)\*\*\s*:?[ \t]*(?<body>.*)$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!bullet.Success)
            return false;

        var label = bullet.Groups["label"].Value.Trim().TrimEnd(':').Trim();
        var identity = Regex.Match(label,
            @"^(?<id>BR(?:D)?-[A-Z0-9]+(?:-[A-Z0-9]+)+)(?:\s+(?:—|–|-)\s+(?<outcome>.+))?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!identity.Success || !IsBusinessRequirementId(identity.Groups["id"].Value))
            return false;

        id = identity.Groups["id"].Value;
        outcome = identity.Groups["outcome"].Value.Trim();
        body = bullet.Groups["body"].Value.Trim();
        return true;
    }

    private static bool IsBusinessRequirementId(string value)
        => Regex.IsMatch(value, @"^BR(?:D)?-[A-Z0-9]+(?:-[A-Z0-9]+)+$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string BacklogItemId(string requirementId)
    {
        var suffix = requirementId.StartsWith("BRD-", StringComparison.Ordinal)
            ? requirementId[4..]
            : requirementId.StartsWith("BR-", StringComparison.Ordinal)
                ? requirementId[3..]
                : requirementId;
        return "HLT-" + suffix;
    }

    private static IReadOnlyList<BrdBacklogItem> ParseItems(string content)
    {
        var block = Block(content, ItemsStart, ItemsEnd);
        var result = new List<BrdBacklogItem>();
        foreach (var line in block.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length != 9 || !cells[0].StartsWith("HLT-", StringComparison.Ordinal)) continue;
            result.Add(new BrdBacklogItem(cells[0], cells[1], cells[2], cells[3], List(cells[4]),
                cells[5] == "none" ? [] : List(cells[5]), cells[6] == "none" ? [] : List(cells[6]), cells[7], cells[8]));
        }
        return result;
    }

    private static IReadOnlyList<FeatureRequirement> ParseFeatureRequirements(string content)
    {
        var section = Section(content, "Functional requirements");
        var result = new List<FeatureRequirement>();
        foreach (var line in section.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length != 5 || !cells[0].StartsWith("FEAT-", StringComparison.Ordinal)) continue;
            result.Add(new FeatureRequirement(cells[0], cells[1].ToLowerInvariant(), cells[2].ToLowerInvariant(), cells[3], cells[4]));
        }
        return result;
    }

    private string RenderFeatureSpecification(State state, BrdBacklogItem item, Requirement requirement, string stableId,
        string? productDefinitionHash)
    {
        var created = _clock().ToUniversalTime().ToString("O");
        var targetLines = string.Join('\n', item.Repositories.Select(repository => $"  - {repository}"));
        var surface = item.Repositories.Any(repository => repository.Contains("infra", StringComparison.OrdinalIgnoreCase))
            ? "delivery"
            : item.FrontendTypes.Count > 0 ? "full-stack" : "backend";
        var frontendTypes = item.FrontendTypes.Count == 0 ? ["not-applicable"] : item.FrontendTypes;
        var requirementRows = string.Join('\n', frontendTypes.Select((frontendType, index) =>
            $"| FEAT-{item.Id.Replace("HLT-", string.Empty, StringComparison.Ordinal)}-{index + 1:000} | {surface} | {frontendType} | {Cell(item.Outcome)} | {Cell(requirement.Acceptance)} |"));
        return $"""
---
title: "{Cell(item.Outcome)}"
type: feature-specification
status: Draft
scope: Workspace
owner: Product owner and delivery leads
last_reviewed: null
review_cadence: on feature scope or accepted-source change
targets:
{targetLines}
references:
  - {Normalize(Path.GetRelativePath(state.Authority!.RepositoryPath, state.BrdPath!))}
  - {Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.TechnicalIntentPath!))}
  - {state.RelativePath}
cis:
  stable_id: {stableId}
  feature_spec_schema: 1
  high_level_item: {item.Id}
  brd_requirement: {item.RequirementId}
  backlog_item_hash: {ItemDigest(item)}
  product_definition_hash: {productDefinitionHash ?? "null"}
  created_at: "{created}"
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# {item.Outcome}

## Feature summary

{item.Outcome}

## Goals

1. Deliver `{item.Id}` exactly within the approved BRD and technical-intent boundaries.
2. Satisfy the source acceptance intent: {requirement.Acceptance}

## Non-goals and explicit exclusions

1. Do not expand beyond `{item.RequirementId}` or silently absorb dependent backlog items.
2. TODO: record feature-specific exclusions during review.

## Actors and scenarios

| Actor | Scenario | Expected outcome |
| --- | --- | --- |
| TODO | TODO | TODO |

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
| --- | --- | --- | --- | --- |
{requirementRows}

## Workflows, states, and invariants

- TODO

## Domain model, data, audit, and migrations

- TODO or Not applicable.

## API, contracts, permissions, and visibility

- TODO or Not applicable.

## UX, screens, and accessibility

- TODO or Not applicable.

## Cross-module integrations, commands, and events

- TODO or Not applicable.

## Search, projection, and retrieval boundaries

- Not applicable unless review identifies a governed retrieval surface.

## Lifecycle, conversion, and carry-forward

- TODO or Not applicable.

## Operational and security considerations

- TODO

## Testing and regression requirements

| Layer | Required evidence |
| --- | --- |
| Documentation, policy, and contract drift | TODO |
| Domain and application unit tests | TODO or Not applicable |
| Architecture and structural tests | TODO or Not applicable |
| API, service, persistence, and migration integration tests | TODO or Not applicable; name any Testcontainers-provisioned dependency |
| Business acceptance tests | TODO or Not applicable |
| Frontend component and accessibility tests | TODO or Not applicable |
| Browser or platform journeys using page objects or screen models | TODO or Not applicable |
| Mutation testing and test-quality assurance | TODO or Not applicable |
| Coverage, independent assurance, and unrun-check reporting | TODO |

## Traceability and related decisions

- Product requirement: `{item.RequirementId}` in `{Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.BrdPath!))}`.
- High-level backlog item: `{item.Id}` in `{state.RelativePath}`.
- Technical intent: `{Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.TechnicalIntentPath!))}`.
- ADRs: TODO or none.
- References to update: TODO.
""";
    }

    private static string ReplaceFeatureSpecificationLink(string content, string itemId, string relativePath)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inside = false;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index] == ItemsStart) { inside = true; continue; }
            if (lines[index] == ItemsEnd) break;
            if (!inside) continue;
            var cells = Cells(lines[index]);
            if (cells.Length != 9 || cells[0] != itemId) continue;
            cells[7] = relativePath;
            lines[index] = "| " + string.Join(" | ", cells.Select(Cell)) + " |";
            break;
        }
        return string.Join('\n', lines);
    }

    private CisProductDefinitionAuthority? ProductDefinition(State state)
    {
        if (state.Authority is null) return null;
        return _productDefinitionAuthorities
            .Select(authority => authority.Evaluate(state.Authority.RepositoryPath))
            .FirstOrDefault(result => result.Applicable);
    }

    private static bool HasCycle(IReadOnlyList<BrdBacklogItem> items)
    {
        var graph = items.ToDictionary(item => item.Id, item => item.DependsOn, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id) { if (visiting.Contains(id)) return true; if (!visited.Add(id)) return false; visiting.Add(id); foreach (var next in graph.GetValueOrDefault(id) ?? []) if (graph.ContainsKey(next) && Visit(next)) return true; visiting.Remove(id); return false; }
        return items.Any(item => Visit(item.Id));
    }

    private static string Section(string content, string heading) { var match = Regex.Match(content, $"(?ms)^## {Regex.Escape(heading)}\\s*$\\n(?<body>.*?)(?=^## |\\z)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return match.Success ? match.Groups["body"].Value.Trim() : string.Empty; }
    private static string Block(string content, string start, string end) { var a = content.IndexOf(start, StringComparison.Ordinal); var b = content.IndexOf(end, StringComparison.Ordinal); return a < 0 || b < a ? string.Empty : content[(a + start.Length)..b].Trim(); }
    private static string[] Cells(string line) => !line.TrimStart().StartsWith('|') ? [] : line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
    private static string[] List(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static bool Placeholder(string value) => Regex.IsMatch(value, @"\b(?:TODO|TBD)\b|(?i:\bTO BE COMPLETED\b)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string? ReadFrontMatter(string content, string key) { var end = content.IndexOf("\n---", 4, StringComparison.Ordinal); if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return null; var match = Regex.Match(content[..end], $"(?m)^{Regex.Escape(key)}:\\s*(?<value>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return match.Success ? match.Groups["value"].Value.Trim().Trim('"') : null; }
    private static string? ReadNestedFrontMatter(string content, string key) { var end = content.IndexOf("\n---", 4, StringComparison.Ordinal); if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return null; var match = Regex.Match(content[..end], $"(?m)^  {Regex.Escape(key)}:\\s*(?<value>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return match.Success ? match.Groups["value"].Value.Trim().Trim('"') : null; }
    private static string ReplaceFrontMatter(string content, string key, string value) => Regex.Replace(content, $"(?m)^{Regex.Escape(key)}:.*$", $"{key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string ReplaceNestedFrontMatter(string content, string key, string value) => Regex.Replace(content, $"(?m)^  {Regex.Escape(key)}:.*$", $"  {key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string RemoveNestedFrontMatter(string content, string key) => Regex.Replace(content,
        $"(?m)^  {Regex.Escape(key)}:.*(?:\\n|$)", string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string EnsureNestedFrontMatter(string content, string key, string value)
    {
        if (ReadNestedFrontMatter(content, key) is not null) return content;
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        return end < 0 ? content : content.Insert(end, $"\n  {key}: {value}");
    }
    private static string ItemDigest(BrdBacklogItem item) => Hash(string.Join('\n',
        item.Id, item.RequirementId, item.Outcome, item.Priority, string.Join(',', item.Repositories),
        string.Join(',', item.FrontendTypes), string.Join(',', item.DependsOn)));
    private static string Hash(string content) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static string ContentDigest(string content)
    {
        var normalized = NormalizeApprovalMetadata(content);
        foreach (var key in new[] { "brd_hash", "technical_intent_hash" })
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <upstream-baseline>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Hash(normalized.TrimEnd());
    }
    private static string LegacyContentDigest(string content) => Hash(NormalizeApprovalMetadata(content).TrimEnd());
    private static string NormalizeApprovalMetadata(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
            normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return normalized;
    }
    private static bool ApprovalDigestMatches(string content, string digest)
        => digest == ContentDigest(content) || digest == LegacyContentDigest(content);
    private static bool HasCurrentApproval(string content)
        => ReadFrontMatter(content, "status") == "Active"
           && ReadNestedFrontMatter(content, "approved_by") is { Length: > 0 } reviewer && reviewer != "null"
           && ReadNestedFrontMatter(content, "approval_reason") is { Length: > 0 } reason && reason != "null"
           && ReadNestedFrontMatter(content, "approved_content_hash") is { Length: > 0 } digest && digest != "null"
           && ApprovalDigestMatches(content, digest);
    private static bool UpstreamBaselinesCanCarryForward(string content, string brd, string technicalIntent)
        => BaselineCanCarryForward(ReadNestedFrontMatter(content, "brd_hash"), brd)
           && BaselineCanCarryForward(ReadNestedFrontMatter(content, "technical_intent_hash"), technicalIntent);
    private static bool BaselineCanCarryForward(string? recorded, string current)
        => recorded == current || recorded is { Length: > 0 } && !recorded.StartsWith("semantic-v1:", StringComparison.Ordinal);
    private static string CarryApproval(string existing, string next)
    {
        next = ReplaceFrontMatter(next, "status", "Active");
        next = ReplaceFrontMatter(next, "last_reviewed", RawFrontMatter(existing, "last_reviewed") ?? "null");
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason" })
            next = ReplaceNestedFrontMatter(next, key, RawNestedFrontMatter(existing, key) ?? "null");
        next = ReplaceNestedFrontMatter(next, "approved_content_hash", "null");
        return ReplaceNestedFrontMatter(next, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(next)));
    }
    private static string? RawFrontMatter(string content, string key) { var end = content.IndexOf("\n---", 4, StringComparison.Ordinal); if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return null; var match = Regex.Match(content[..end], $"(?m)^{Regex.Escape(key)}:\\s*(?<value>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return match.Success ? match.Groups["value"].Value.Trim() : null; }
    private static string? RawNestedFrontMatter(string content, string key) { var end = content.IndexOf("\n---", 4, StringComparison.Ordinal); if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return null; var match = Regex.Match(content[..end], $"(?m)^  {Regex.Escape(key)}:\\s*(?<value>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return match.Success ? match.Groups["value"].Value.Trim() : null; }
    private static string BrdBaseline(string content) => "semantic-v1:" + BrdDocumentDigest.Compute(content);
    private static string TechnicalIntentBaseline(string content)
    {
        var normalized = NormalizeApprovalMetadata(content);
        normalized = Regex.Replace(normalized,
            Regex.Escape("<!-- cis:technical-intent-baseline:start -->") + ".*?" + Regex.Escape("<!-- cis:technical-intent-baseline:end -->"),
            "<managed-technical-intent-baseline>", RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return "semantic-v1:" + Hash(normalized.TrimEnd());
    }
    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static bool Equivalent(string left, string right) => left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static void Write(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content, new UTF8Encoding(false)); File.Move(temporary, path, true); }
    private static string UpdateCatalogStatus(string catalog, string id, string status) { var lines = catalog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToArray(); for (var index = 0; index < lines.Length; index++) { if (lines[index].Trim() != $"- id: {id}") continue; for (var cursor = index + 1; cursor < lines.Length && !lines[cursor].StartsWith("  - id:", StringComparison.Ordinal); cursor++) if (lines[cursor].TrimStart().StartsWith("status:", StringComparison.Ordinal)) { lines[cursor] = "    status: " + status; return string.Join('\n', lines); } } return catalog; }
    private static BrdBacklogResult Error(string status, State state, IReadOnlyList<string> errors) => new(status, state.Workspace?.WorkspacePath, state.Authority?.Id, state.RelativePath, [], null, errors, false);
    private static BrdFeatureSpecificationResult FeatureError(string status, State state, string? itemId, IReadOnlyList<string> errors)
        => new(status, state.Workspace?.WorkspacePath, state.Authority?.Id, itemId, null, null, errors, false);

    private sealed record Requirement(string Id, string Outcome, string Text, string Priority, string Acceptance);
    private sealed record FeatureRequirement(string Id, string Surface, string FrontendType, string Requirement, string Acceptance);
    private sealed record FeatureState(State? State, BrdBacklogItem? Item, string? Path, string? RelativePath,
        IReadOnlyList<string> Errors);
    private sealed record State(CisWorkspace? Workspace, CisWorkspaceRepository? Authority, CisRepositoryContext? Context,
        string? BrdPath, string? TechnicalIntentPath, string? BacklogPath, IReadOnlyList<string> Errors, string? RelativePath = null);
}
