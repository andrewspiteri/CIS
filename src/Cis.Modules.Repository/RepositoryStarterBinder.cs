namespace Cis.Modules.Repository;

internal sealed class RepositoryStarterBinder
{
    private static readonly ReferenceFamily[] ReferenceFamilies =
    [
        new(
            "api-dictionary",
            "API Dictionary",
            "API contracts, operations, routes, ownership, and lifecycle",
            [
                "API ID", "Version", "Method", "Path", "Module", "Host", "Exposure",
                "Consumer", "Request contract", "Response contract", "Permission / auth", "OpenAPI operation",
                "Trusted scope source", "Rate-limit policy", "Idempotency / concurrency", "Collection semantics",
                "Error contract", "Cache policy", "Data access path", "Status", "Evidence", "Known tests", "Notes",
            ],
            classification => HasRole(classification, "backend-api-producer", "backend-api-consumer")),
        new(
            "command-dictionary",
            "Command Dictionary",
            "durable requested actions, preconditions, results, authorization, and emitted events",
            [
                "Command ID", "Command name", "Module", "Actor / trigger", "API / screen / source",
                "Preconditions", "Result", "Emits events", "Permissions / auth", "Status", "Evidence",
            ],
            classification => HasRole(classification, "backend-api-producer", "worker")),
        new(
            "event-dictionary",
            "Event Dictionary",
            "published and consumed event contracts, ownership, and compatibility",
            [
                "Event ID", "Event name", "Event type", "Producer", "Consumers", "Triggering command",
                "Payload summary", "Ordering / idempotency", "Sensitivity", "Status", "Evidence",
            ],
            classification => HasRole(classification, "event-producer", "event-consumer")),
        new(
            "workflow-state-dictionary",
            "Workflow State Dictionary",
            "workflow states, meanings, transitions, blockers, terminal states, and visibility behavior",
            [
                "Workflow ID", "Workflow", "State", "Meaning", "Allowed transitions", "Triggering commands",
                "Blocking rules", "Terminal", "Visibility / search behavior", "Status",
            ],
            HasApplicationBehavior),
        new(
            "business-invariant-catalogue",
            "Business Invariant Catalogue",
            "durable rules, rationale, enforcement points, tests, and source specifications",
            [
                "Invariant ID", "Rule", "Applies to", "Rationale", "Enforcement point",
                "Related commands / events", "Related tests", "Source specs", "Status",
            ],
            classification => HasRole(classification, "backend-api-producer", "worker", "database")),
        new(
            "projection-dictionary",
            "Projection Dictionary",
            "read models and projections, their sources, consumers, freshness, security, and rebuild behavior",
            [
                "Projection ID", "Projection / read model", "Source of truth", "Producer / refresh", "Consumers",
                "Fields summary", "Freshness / staleness", "Sensitivity", "Status", "Evidence",
            ],
            classification => HasRole(classification, "event-consumer", "database")
                || HasCapability(classification, "persistence")),
        new(
            "permissions-dictionary",
            "Permissions Dictionary",
            "permissions, roles, enforcement points, and capability mappings",
            [
                "Permission code", "Permission name", "Domain", "Capability group", "Action", "Scope type",
                "Applies to", "Default role mappings", "Customer assignable", "Endpoint / API usage",
                "Frontend capability", "Source location", "Status", "Notes",
            ],
            classification => HasCapability(classification, "authorization")),
        new(
            "configuration-dictionary",
            "Configuration Dictionary",
            "runtime configuration keys, sources, sensitivity, and refresh behavior",
            [
                "Name", "Path", "Allowed values", "Refresh class", "Owner", "Sensitive", "Description",
                "Status", "Evidence",
            ],
            classification => classification.Components.Any(component =>
                component.Capabilities.Contains("configuration", StringComparer.Ordinal))),
        new(
            "package-catalogue",
            "Package Catalogue",
            "runtime and build dependencies, ownership, purpose, and update policy",
            ["Package", "Component", "Purpose", "Version policy", "Dependency class", "Status", "Evidence"],
            classification => classification.Components.Any(component =>
                component.Languages.Contains("csharp", StringComparer.Ordinal)
                || component.Languages.Contains("typescript", StringComparer.Ordinal)
                || component.Languages.Contains("javascript", StringComparer.Ordinal)
                || component.Languages.Contains("swift", StringComparer.Ordinal)
                || component.Languages.Contains("kotlin", StringComparer.Ordinal))),
        new(
            "screen-route-map",
            "Screen and Route Map",
            "user-facing screens, routes or navigation destinations, ownership, and access",
            [
                "Screen or route", "Component", "Platform", "Access / permission", "Destination", "Status",
                "Evidence", "Notes",
            ],
            classification => HasRole(classification, "frontend-consumer", "mobile-client", "native-frontend")),
        new(
            "problem-details-catalogue",
            "Problem Details Catalogue",
            "API error identities, status codes, contracts, and handling",
            [
                "Problem ID", "Problem type / reason", "HTTP status", "Raised by", "Meaning",
                "Frontend handling", "Sensitive fields", "Related commands / workflows", "Status", "Evidence",
            ],
            classification => classification.Components.Any(component =>
                component.Roles.Contains("backend-api-producer", StringComparer.Ordinal))),
        new(
            "module-ownership-map",
            "Module Ownership Map",
            "component boundaries, responsibilities, owners, and dependencies",
            [
                "Module ID", "Module / context", "Code area", "Owns", "Commands / events",
                "Data / projections", "API surfaces", "Boundary rules", "Related docs", "Status", "Evidence",
            ],
            classification => classification.Components.Count > 1),
        new(
            "data-dictionary",
            "Data Dictionary",
            "persistent entities, fields, constraints, sensitivity, and ownership",
            [
                "Entity", "Field", "Type", "Required", "Constraints", "Sensitive", "Owner", "Lifecycle",
                "Status", "Evidence",
            ],
            classification => HasCapability(classification, "persistence")),
        new(
            "erd",
            "Entity Relationship Diagram",
            "persistent entity relationships and aggregate boundaries",
            ["Entity", "Relationship", "Target", "Cardinality", "Owner", "Status", "Evidence"],
            classification => HasCapability(classification, "persistence")),
        new(
            "traceability-matrix",
            "Traceability Matrix",
            "requirements and specifications mapped to components, implementation, verification, and status",
            [
                "Trace ID", "Requirement / source", "Specification", "Component", "Implementation evidence",
                "Verification evidence", "Status", "Notes",
            ],
            classification => classification.Components.Count > 0),
    ];

    public RepositoryStarterBinding Bind(
        string repositoryPath,
        string repositoryId,
        string documentationRoot,
        RepositoryClassification classification,
        bool workspaceAuthority = false)
    {
        var selections = new List<RepositoryStarterSelection>();
        var artifacts = new List<RepositoryStarterArtifact>();

        AddRepositoryProfile(repositoryId, documentationRoot, classification, selections, artifacts);
        AddUiFrameworkProfile(repositoryId, documentationRoot, classification, selections, artifacts);
        AddTaskTypeCapabilitySelections(repositoryId, documentationRoot, selections, artifacts);
        AddApiGovernanceProfile(repositoryId, documentationRoot, selections, artifacts);
        AddExternalTrackerProfile(repositoryId, documentationRoot, selections, artifacts);
        AddExecutionGovernanceProfiles(repositoryPath, repositoryId, documentationRoot, classification, selections, artifacts);
        AddSeedSpecifications(repositoryId, documentationRoot, classification, workspaceAuthority, selections, artifacts);
        AddStandardsGovernance(repositoryId, documentationRoot, classification, selections, artifacts);
        AddImplementationSkillPacks(repositoryId, documentationRoot, classification, selections, artifacts);
        AddAgentGuidance(documentationRoot, classification, selections, artifacts);
        var referenceSeeds = RepositoryReferenceSeeder.Seed(repositoryPath, classification);

        foreach (var family in ReferenceFamilies.Where(family => family.Applies(classification)))
        {
            var definition = $"reference.{family.Slug}";
            var evidence = EvidenceForFamily(family.Slug, classification);
            selections.Add(new RepositoryStarterSelection(
                definition,
                $"Selected from the confirmed repository classification for {family.Title}.",
                evidence));
            var rows = referenceSeeds.GetValueOrDefault(family.Slug) ?? [];
            AddReferenceFamily(repositoryId, documentationRoot, family, rows, artifacts);
        }

        return new RepositoryStarterBinding(
            selections.OrderBy(selection => selection.Definition, StringComparer.Ordinal).ToArray(),
            artifacts
                .GroupBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AddRepositoryProfile(
        string repositoryId,
        string documentationRoot,
        RepositoryClassification classification,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        const string definition = "repository.profile";
        var path = $"{documentationRoot}/references/repository-profile.md";
        selections.Add(new RepositoryStarterSelection(
            definition,
            "Every repository needs a reviewable component and role profile.",
            classification.Components.SelectMany(component => component.Evidence).Take(10).ToArray()));
        artifacts.Add(new RepositoryStarterArtifact(
            "repository.profile",
            definition,
            path,
            CreateRepositoryProfile(repositoryId, classification),
            new CatalogArtifactEntry(
                $"{repositoryId}:reference:repository-profile",
                path,
                "repository-profile",
                "active",
                "canonical")));
    }

    private static void AddSeedSpecifications(
        string repositoryId,
        string documentationRoot,
        RepositoryClassification classification,
        bool workspaceAuthority,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "product-intent",
            "Product Intent",
            "A reviewable starting point for outcomes, actors, scope, capabilities, requirements, and exclusions.",
            CreateProductIntentSpecification(repositoryId, classification),
            selections,
            artifacts);
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "technical-intent",
            "Technical Intent",
            "A reviewable starting point for architecture, boundaries, data, security, operations, and quality attributes.",
            CreateTechnicalIntentSpecification(repositoryId, classification, workspaceAuthority),
            selections,
            artifacts);
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "system-context",
            "System Context",
            "A reviewable starting point for system purpose, boundaries, actors, components, and external dependencies.",
            CreateSystemContextSpecification(repositoryId, classification),
            selections,
            artifacts);
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "delivery-and-assurance",
            "Delivery and Assurance",
            "A repository-level policy for human control, deterministic verification, documentation maintenance, and independent assurance.",
            CreateDeliveryAndAssuranceSpecification(repositoryId, classification),
            selections,
            artifacts);
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "repository-delivery-policy",
            "Repository Delivery Policy",
            "A repository-owned policy for branch, commit, push, pull-request, and remote-mutation authority.",
            CreateRepositoryDeliveryPolicySpecification(repositoryId),
            selections,
            artifacts);
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "api-design-and-governance",
            "API Design and Governance",
            "A PARR-derived, repository-owned baseline for secure, compatible, observable, and reviewable HTTP APIs.",
            CreateApiDesignAndGovernanceSpecification(repositoryId),
            selections,
            artifacts);
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "public-endpoint-caching-policy",
            "Public Endpoint Caching Policy",
            "A mandatory architecture boundary for cacheable unauthenticated endpoints and database isolation.",
            CreatePublicEndpointCachingPolicySpecification(repositoryId),
            selections,
            artifacts);
        AddSeedSpecification(
            repositoryId,
            documentationRoot,
            "external-tracker-synchronization",
            "External Tracker Synchronization",
            "A canonical-authority, identity, conflict, deletion, authorization, retry, and provider contract for GitHub Issues, Jira, and extension trackers.",
            CreateExternalTrackerSynchronizationSpecification(repositoryId),
            selections,
            artifacts);
        AddDocumentationTemplate(
            repositoryId,
            documentationRoot,
            "feature-spec",
            "Feature Specification Template",
            CreateFeatureSpecificationTemplate(repositoryId),
            selections,
            artifacts);
        AddDocumentationTemplate(
            repositoryId,
            documentationRoot,
            "adr",
            "Architecture Decision Record Template",
            CreateArchitectureDecisionTemplate(repositoryId),
            selections,
            artifacts);
        AddDesignGuidelines(repositoryId, documentationRoot, selections, artifacts);
    }

    private static void AddTaskTypeCapabilitySelections(
        string repositoryId,
        string documentationRoot,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        const string definition = "reference.task-type-capability-selections";
        var path = $"{documentationRoot}/references/task-type-capability-selections.md";
        selections.Add(new RepositoryStarterSelection(
            definition,
            "Every repository needs a canonical human-approved record for mutually exclusive extension task providers and explicit task-type replacement.",
            ["cis task-type extension policy"]));
        artifacts.Add(new RepositoryStarterArtifact(
            definition,
            definition,
            path,
            CreateTaskTypeCapabilitySelections(repositoryId),
            new CatalogArtifactEntry(
                $"{repositoryId}:reference:task-type-capability-selections",
                path,
                "task-type-capability-selections",
                "active",
                "canonical")));
    }

    private static void AddStandardsGovernance(
        string repositoryId,
        string documentationRoot,
        RepositoryClassification classification,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        const string standardDefinition = "standard.documentation-governance";
        var standardPath = $"{documentationRoot}/standards/documentation-governance-standard.md";
        selections.Add(new RepositoryStarterSelection(
            standardDefinition,
            "Every repository needs one canonical contract defining standards, stable rules, lifecycle, exceptions, and enforcement evidence.",
            ["PARR standards methodology", "cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            standardDefinition,
            standardDefinition,
            standardPath,
            CreateDocumentationGovernanceStandard(repositoryId),
            new CatalogArtifactEntry(
                $"{repositoryId}:standard:documentation-governance",
                standardPath,
                "standard",
                "active",
                "canonical")));

        const string specificationDefinition = "specification.standards-governance";
        var specificationPath = $"{documentationRoot}/specs/standards-governance-spec.md";
        selections.Add(new RepositoryStarterSelection(
            specificationDefinition,
            "Every repository receives the command, applicability, validation, and conformance contract for standards governance.",
            ["cis standards module"]));
        artifacts.Add(new RepositoryStarterArtifact(
            specificationDefinition,
            specificationDefinition,
            specificationPath,
            CreateStandardsGovernanceSpecification(repositoryId, documentationRoot),
            new CatalogArtifactEntry(
                $"{repositoryId}:spec:standards-governance",
                specificationPath,
                "governance-specification",
                "active",
                "canonical")));

        var defaultStandards = DefaultStandardRegistry.Select(classification);
        foreach (var standard in defaultStandards)
        {
            var definition = $"standard.default.{standard.Slug}";
            var path = $"{documentationRoot}/standards/{standard.Slug}-standard.md";
            selections.Add(new RepositoryStarterSelection(
                definition,
                $"Selected from the repository classification as a safe PARR-derived {standard.Title} baseline.",
                standard.SourcePaths));
            artifacts.Add(new RepositoryStarterArtifact(
                definition,
                definition,
                path,
                DefaultStandardRegistry.Render(repositoryId, standard),
                new CatalogArtifactEntry(
                    $"{repositoryId}:standard:{standard.Slug}",
                    path,
                    "standard",
                    "active",
                    "canonical")));
        }

        const string matrixDefinition = "reference.standards-conformance-matrix";
        var matrixPath = $"{documentationRoot}/references/standards-conformance-matrix.md";
        selections.Add(new RepositoryStarterSelection(
            matrixDefinition,
            "Every standard rule needs an explicit enforcement route and evidence boundary.",
            ["PARR standards conformance matrix"]));
        artifacts.Add(new RepositoryStarterArtifact(
            matrixDefinition,
            matrixDefinition,
            matrixPath,
            CreateStandardsConformanceMatrix(repositoryId, defaultStandards),
            new CatalogArtifactEntry(
                $"{repositoryId}:reference:standards-conformance-matrix",
                matrixPath,
                "standards-conformance-reference",
                "active",
                "canonical")));

        const string templateDefinition = "template.standard";
        var templatePath = $"{documentationRoot}/templates/standard-template.md";
        selections.Add(new RepositoryStarterSelection(
            templateDefinition,
            "Every repository receives a reusable standard template with stable rules and explicit verification.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            templateDefinition,
            templateDefinition,
            templatePath,
            CreateStandardTemplate(),
            new CatalogArtifactEntry(
                $"{repositoryId}:template:standard",
                templatePath,
                "template",
                "active",
                "canonical")));

        const string patternTemplateDefinition = "template.standard-inference-pattern";
        var patternTemplatePath = $"{documentationRoot}/templates/standard-pattern-template.md";
        selections.Add(new RepositoryStarterSelection(
            patternTemplateDefinition,
            "Repositories may extend the known-standard-pattern catalogue through canonical, validated Markdown without replacing provider patterns by load order.",
            ["cis standards patterns", "compiler graph inference"]));
        artifacts.Add(new RepositoryStarterArtifact(
            patternTemplateDefinition,
            patternTemplateDefinition,
            patternTemplatePath,
            CreateStandardPatternTemplate(),
            new CatalogArtifactEntry(
                $"{repositoryId}:template:standard-inference-pattern",
                patternTemplatePath,
                "template",
                "active",
                "canonical")));
    }

    private static void AddApiGovernanceProfile(
        string repositoryId,
        string documentationRoot,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        const string definition = "reference.api-governance-profile";
        var path = $"{documentationRoot}/references/api-governance-profile.md";
        selections.Add(new RepositoryStarterSelection(
            definition,
            "Every repository owns explicit API routing, compatibility, OpenAPI, header, and supported-version decisions.",
            ["cis API governance"]));
        artifacts.Add(new RepositoryStarterArtifact(
            definition,
            definition,
            path,
            CreateApiGovernanceProfile(repositoryId),
            new CatalogArtifactEntry(
                $"{repositoryId}:reference:api-governance-profile",
                path,
                "api-governance-profile",
                "draft",
                "canonical")));
    }

    private static string CreateApiGovernanceProfile(string repositoryId) => $"""
        ---
        title: "{repositoryId} API Governance Profile"
        type: api-governance-profile
        status: Draft
        owner: Repository maintainer
        review_cadence: on API policy, host, or supported-version change
        cis:
          stable_id: {repositoryId}:reference:api-governance-profile
        ---

        # {repositoryId} API Governance Profile

        This repository-owned profile supplies decisions that CIS cannot infer universally.
        Replace `repository-defined` and register OpenAPI documents and supported versions
        before marking governed operations `Verified`. `cis repo init` preserves reviewed edits.

        ## Settings

        | Setting | Value | Status | Rationale |
        |---|---|---|---|
        | route-template | module/version/resource-path | Proposed | PARR-derived external route baseline, for example `/sales/v1/orders`. |
        | compatibility-mode | forward-transitive | Proposed | Compare the current contract with every supported baseline in the same major version. |
        | deprecation-window-days | 180 | Proposed | PARR baseline; repository may approve another window. |
        | production-openapi-exposure | restricted | Proposed | Avoid exposing operational contract metadata unintentionally. |
        | correlation-header | repository-defined | Review Required | Select or preserve the established correlation convention. |
        | client-header | repository-defined | Review Required | Select or preserve the established client identity convention. |

        ## OpenAPI documents

        Paths are repository-relative JSON files. Add one `current` row per governed
        deployment/host and the corresponding `baseline` used for compatibility comparison.

        | Document role | Path | Host / deployment | Status | Evidence |
        |---|---|---|---|---|
        | current | none | none | Review Required | No current OpenAPI document registered. |
        | baseline | none | none | Review Required | No compatibility baseline registered. |

        ## Supported versions

        | Module | Major version | Status | Sunset | Consumers | Evidence |
        |---|---|---|---|---|---|
        | TODO | TODO | Draft | none | unknown | Register each supported major version. |
        """;

    private static void AddExternalTrackerProfile(
        string repositoryId,
        string documentationRoot,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        const string definition = "reference.external-tracker-profile";
        var path = $"{documentationRoot}/references/external-tracker-profile.md";
        selections.Add(new RepositoryStarterSelection(definition,
            "Every repository owns explicit external tracker targets and keeps them disabled until reviewed provider configuration and credentials exist.",
            ["cis external tracker synchronization"]));
        artifacts.Add(new RepositoryStarterArtifact(definition, definition, path,
            CreateExternalTrackerProfile(repositoryId),
            new CatalogArtifactEntry($"{repositoryId}:reference:external-tracker-profile", path,
                "external-tracker-profile", "draft", "canonical")));
    }

    private static string CreateExternalTrackerProfile(string repositoryId) => $"""
        ---
        title: "{repositoryId} External Tracker Profile"
        type: external-tracker-profile
        status: Draft
        owner: Repository maintainer
        review_cadence: on tracker, project, workflow, or credential-source change
        cis:
          stable_id: {repositoryId}:reference:external-tracker-profile
        ---

        # {repositoryId} External Tracker Profile

        External trackers mirror canonical CIS task documents. They do not approve plans,
        designs, risks, deferrals, completion, or final acceptance. Enable a row only after
        its provider assembly, target, field mapping, least-privilege credential source, and
        dry-run output have been reviewed. Never store credentials in this document.

        | Provider | Kind | Enabled | Target | Base URL | Direction | Issue type | Credential source |
        |---|---|---|---|---|---|---|---|
        | github | github | no | owner/repository | https://api.github.com | cis-to-remote | issue | environment:GITHUB_TOKEN |
        | jira | jira | no | PROJECT | https://example.atlassian.net | cis-to-remote | Task | basic-environment:JIRA_EMAIL:JIRA_API_TOKEN |

        The built-in GitHub transport uses the GitHub REST API and the built-in Jira
        transport uses Jira Cloud REST API v3 with Atlassian document format descriptions.
        Enabling a row is an explicit remote-write decision. Environment credentials are
        read by the invoking process and are never copied into CIS state or Markdown.

        ## Field and lifecycle mappings

        | Provider | CIS field | Remote field | Direction | Required | Notes |
        |---|---|---|---|---|---|
        | github | title | title | CIS to remote | yes | Canonical task title wins after reviewed conflict resolution. |
        | github | projected body | body | CIS to remote | yes | Includes stable identity and canonical path. |
        | jira | title | summary | CIS to remote | yes | Canonical task title wins after reviewed conflict resolution. |
        | jira | projected body | description | CIS to remote | yes | Adapter selects the server-supported document format. |

        ## Provider-specific status mappings

        | Provider | CIS status | Remote status | Remote transition | Authority |
        |---|---|---|---|---|
        | github | repository-defined | repository-defined | repository-defined | CIS remains authoritative. |
        | jira | repository-defined | repository-defined | repository-defined | CIS remains authoritative. |
        """;

    private static void AddExecutionGovernanceProfiles(
        string repositoryPath,
        string repositoryId,
        string documentationRoot,
        RepositoryClassification classification,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        void Add(string definition, string relative, string type, string content)
        {
            var path = $"{documentationRoot}/{relative}";
            selections.Add(new RepositoryStarterSelection(definition,
                "Every initialized repository receives safe execution, diagnostics, and learning defaults.",
                ["cis curated starter"]));
            artifacts.Add(new RepositoryStarterArtifact(definition, definition, path, content,
                new CatalogArtifactEntry($"{repositoryId}:{definition.Replace('.', ':')}", path, type, "active", "canonical")));
        }

        Add("reference.ai-routing-profile", "references/ai-routing-profile.md", "ai-routing-profile", """
        ---
        title: "AI Routing Profile"
        type: ai-routing-profile
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-14"
        review_cadence: "on provider, model, privacy, or cost-policy change"
        ---

        # AI routing profile

        Local Ollama is the default. Remote transmission requires both a reviewed route
        and explicit `--allow-remote` authorization for the exact submitted content.

        | Capability | Provider | Model | Allow remote | Cache |
        |---|---|---|---|---|
        | index-card | ollama | repository-smallest-local | no | yes |
        | context-summary | ollama | repository-smallest-local | no | yes |
        | learning-summary | ollama | repository-smallest-local | no | yes |
        """);
        Add("reference.diagnostics-profile", "references/diagnostics-profile.md", "diagnostics-profile", """
        ---
        title: "Diagnostics Evidence Profile"
        type: diagnostics-profile
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-14"
        review_cadence: "on runtime evidence source or sensitivity change"
        ---

        # Diagnostics evidence profile

        Sources are disabled until their repository-relative sanitized export exists.

        | Source | Kind | Location | Enabled | Sensitive |
        |---|---|---|---|---|
        | application-log | text-log | .cis/local/diagnostics/input/application.log | no | no |
        | test-log | text-log | .cis/local/diagnostics/input/tests.log | no | no |
        """);
        var testing = RepositoryTestingStarter.Create(repositoryPath, documentationRoot, classification);
        Add("workflow.standard-delivery", "workflows/standard-delivery.md", "workflow-definition", testing.Workflow);
        Add("reference.test-suite-profile", "references/test-suite-profile.md", "test-suite-profile", testing.Profile);
        Add("reference.learning-history", "references/learning-history.md", "learning-history", """
        ---
        title: "Governed Learning History"
        type: learning-history
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-14"
        review_cadence: "on learning application"
        ---

        # Governed learning history

        | Applied UTC | Proposal | Recommendation | Reviewer | Rationale | Source digest |
        |---|---|---|---|---|---|
        """);
    }

    private static string CreateTaskTypeCapabilitySelections(string repositoryId) => $"""
        ---
        title: "{repositoryId} Task-Type Capability Selections"
        type: task-type-capability-selections
        status: Active
        owner: Repository maintainer
        review_cadence: on task-provider or capability change
        cis:
          stable_id: {repositoryId}:reference:task-type-capability-selections
        ---

        # {repositoryId} Task-Type Capability Selections

        This canonical record resolves mutually exclusive extension providers and
        explicitly authorizes compatible extension task-type replacement. Core task
        types are non-replaceable. Use `cis plan capability select`; do not hand-edit
        the managed table.

        <!-- cis:task-type-capability-selections:start -->
        | Capability | Selected type | Replaces | Reviewer | Timestamp UTC | Rationale |
        |---|---|---|---|---|---|
        | None | None | None | Pending | Pending | No explicit selections. |
        <!-- cis:task-type-capability-selections:end -->

        Selection does not migrate an existing task automatically. Use
        `cis plan task migrate-type` for each affected task instance so its evidence,
        approvals, deferrals, external links, and migration history remain visible.
        """;

    private static void AddUiFrameworkProfile(
        string repositoryId,
        string documentationRoot,
        RepositoryClassification classification,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var resolutions = UiFrameworkResolver.Resolve(classification);
        if (resolutions.Count == 0)
        {
            return;
        }

        const string definition = "reference.ui-framework-profile";
        var path = $"{documentationRoot}/references/ui-framework-profile.md";
        selections.Add(new RepositoryStarterSelection(
            definition,
            "UI-bearing components need an evidence-first framework choice before design and frontend implementation.",
            resolutions.SelectMany(resolution => resolution.Evidence).Distinct(StringComparer.Ordinal).Take(20).ToArray()));
        artifacts.Add(new RepositoryStarterArtifact(
            definition,
            definition,
            path,
            CreateUiFrameworkProfile(repositoryId, resolutions),
            new CatalogArtifactEntry(
                $"{repositoryId}:reference:ui-framework-profile",
                path,
                "ui-framework-profile",
                "active",
                "canonical")));
    }

    private static void AddDesignGuidelines(
        string repositoryId,
        string documentationRoot,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var defaultPath = $"{documentationRoot}/specs/design-guidelines.md";
        const string defaultDefinition = "specification.design-guidelines";
        var defaultContent = LoadEmbeddedDesignDocument("default-design-guidelines.md")
            .Replace("change-impact-studio:design-guidelines:default", $"{repositoryId}:spec:design-guidelines", StringComparison.Ordinal)
            .Replace("scope: \"Product:ChangeImpactStudio\"", "scope: Repository", StringComparison.Ordinal);
        selections.Add(new RepositoryStarterSelection(defaultDefinition,
            "Every repository receives populated, overridable visual defaults for deterministic design rendering.",
            ["cis design system"]));
        artifacts.Add(new RepositoryStarterArtifact(
            defaultDefinition,
            defaultDefinition,
            defaultPath,
            defaultContent,
            new CatalogArtifactEntry($"{repositoryId}:spec:design-guidelines", defaultPath,
                "design-guidelines", "active", "canonical")));

        var templatePath = $"{documentationRoot}/templates/design-guidelines-template.md";
        const string templateDefinition = "template.design-guidelines";
        var templateContent = LoadEmbeddedDesignDocument("design-guidelines-template.md")
            .Replace("change-impact-studio:template:design-guidelines", $"{repositoryId}:template:design-guidelines", StringComparison.Ordinal);
        selections.Add(new RepositoryStarterSelection(templateDefinition,
            "Every repository receives a reusable design-guidelines template for approved product-specific overrides.",
            ["cis design system"]));
        artifacts.Add(new RepositoryStarterArtifact(
            templateDefinition,
            templateDefinition,
            templatePath,
            templateContent,
            new CatalogArtifactEntry($"{repositoryId}:template:design-guidelines", templatePath,
                "template", "active", "canonical")));
    }

    private static string LoadEmbeddedDesignDocument(string suffix)
    {
        var assembly = typeof(RepositoryStarterBinder).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded design document was not found: {suffix}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AddSeedSpecification(
        string repositoryId,
        string documentationRoot,
        string slug,
        string title,
        string reason,
        string content,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var definition = $"specification.{slug}";
        var path = $"{documentationRoot}/specs/{slug}-spec.md";
        selections.Add(new RepositoryStarterSelection(definition, reason, ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            definition,
            definition,
            path,
            content,
            new CatalogArtifactEntry(
                $"{repositoryId}:spec:{slug}",
                path,
                "repository-specification",
                "draft",
                "canonical")));
    }

    private static void AddDocumentationTemplate(
        string repositoryId,
        string documentationRoot,
        string slug,
        string title,
        string content,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var definition = $"template.{slug}";
        var path = $"{documentationRoot}/templates/{slug}-template.md";
        selections.Add(new RepositoryStarterSelection(
            definition,
            $"Every repository receives a reusable {title.ToLowerInvariant()}.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            definition,
            definition,
            path,
            content,
            new CatalogArtifactEntry(
                $"{repositoryId}:template:{slug}",
                path,
                "template",
                "active",
                "canonical")));
    }

    private static void AddAgentGuidance(
        string documentationRoot,
        RepositoryClassification classification,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        const string skillDefinition = "guidance.skill.change-impact";
        selections.Add(new RepositoryStarterSelection(
            skillDefinition,
            "Every initialized repository receives a common change-impact workflow skill.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            "guidance.skill.change-impact",
            skillDefinition,
            ".github/skills/cis-change-impact/SKILL.md",
            CreateChangeImpactSkill(),
            null));

        AddSkill(
            "repository-bootstrap",
            "Initialize or reconcile CIS repository state and route failed initialization through repository doctor.",
            CreateRepositoryBootstrapSkill(),
            selections,
            artifacts);
        AddSkill(
            "skill-governance",
            "Inventory, validate, safely repair, import, and audit repository skills without replacing human authority.",
            CreateSkillGovernanceSkill(),
            selections,
            artifacts);
        AddSkill(
            "import-repositories",
            "Initialize and register several existing repositories, then build and validate their graphs as one CIS workspace.",
            CreateImportRepositoriesSkill(),
            selections,
            artifacts);
        AddSkill(
            "govern-business-requirements",
            "Discover, reconcile, validate, and preserve human approval of a canonical workspace BRD.",
            CreateGovernBusinessRequirementsSkill(),
            selections,
            artifacts);
        AddSkill(
            "govern-technical-intent",
            "Derive, validate, and preserve human approval of workspace technical direction from the active BRD.",
            CreateGovernTechnicalIntentSkill(),
            selections,
            artifacts);
        AddSkill(
            "feature-specification-governance",
            "Author, validate, review, and keep a classification-aware feature specification current before detailed planning.",
            CreateFeatureSpecificationGovernanceSkill(),
            selections,
            artifacts);
        AddSkill(
            "documentation",
            "Maintain CIS specifications, references, and catalog relationships.",
            CreateDocumentationSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "standards-governance",
            "Find applicable standards, author stable normative rules, validate them, and maintain conformance evidence without treating model advice as proof.",
            CreateStandardsGovernanceSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "verification",
            "Build a proportionate deterministic verification plan and preserve its evidence.",
            CreateVerificationSkill(),
            selections,
            artifacts);
        AddSkill(
            "maintain-contracts",
            "Keep API, data, configuration, package, and permission references aligned with implementation changes.",
            CreateMaintainContractsSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "api-contract-governance",
            "Apply the repository API standard, contract inventories, compatibility rules, and deterministic drift checks.",
            CreateApiContractGovernanceSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "maintain-domain-behaviour",
            "Keep commands, events, workflows, invariants, projections, problems, and module boundaries aligned.",
            CreateMaintainDomainBehaviourSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "add-documentation",
            "Create and register documentation without bypassing catalog, lifecycle, and source-of-truth rules.",
            CreateAddDocumentationSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "validate-completion",
            "Test completion claims against requested scope, acceptance criteria, deterministic checks, and residual risk.",
            CreateValidateCompletionSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "write-adr",
            "Record durable technical decisions and their alternatives, consequences, and affected documentation.",
            CreateWriteAdrSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "graph-context",
            "Build, validate, query, trace, and package evidence from the CIS graph and context commands.",
            CreateGraphContextSkill(),
            selections,
            artifacts);
        AddSkill(
            "file-index",
            "Build and query low-token, non-authoritative per-file routing cards before broad source discovery.",
            CreateFileIndexSkill(),
            selections,
            artifacts);
        AddSkill(
            "feedback-loop",
            "Review local CIS tool usage, possible token savings, repeated failures, and compact-output opportunities.",
            CreateFeedbackLoopSkill(),
            selections,
            artifacts);
        AddSkill(
            "change-dossier",
            "Create and manage an exact-baseline repository-owned CIS change dossier.",
            CreateChangeDossierSkill(),
            selections,
            artifacts);
        AddSkill(
            "impact-review",
            "Run bounded impact analysis and preserve explicit human finding dispositions.",
            CreateImpactReviewSkill(),
            selections,
            artifacts);
        AddSkill(
            "decision-review",
            "Record change-local questions and preserve human authority over resolution, deferral, and ADR promotion.",
            CreateDecisionReviewSkill(),
            selections,
            artifacts);
        AddSkill(
            "bounded-planning",
            "Build and validate bounded work from accepted impacts while preserving human plan approval.",
            CreateBoundedPlanningSkill(),
            selections,
            artifacts);
        AddSkill(
            "external-tracker-sync",
            "Preview, synchronize, and reconcile external issue mirrors without transferring canonical CIS authority.",
            CreateExternalTrackerSyncSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "design-review",
            "Create textual wireframes, scaffold reusable shell/component renderers, render Sharp/SVG PNGs, and preserve the global human design gate.",
            CreateDesignReviewSkill(),
            selections,
            artifacts);
        AddSkill(
            "delivery-execution",
            "Route model work, render deterministic templates, run resumable workflows, prepare agent envelopes, verify delivery, inspect diagnostics, and govern learning.",
            CreateDeliveryExecutionSkill(documentationRoot),
            selections,
            artifacts);

        const string repositoryInstructionDefinition = "guidance.instruction.repository";
        selections.Add(new RepositoryStarterSelection(
            repositoryInstructionDefinition,
            "Every initialized repository receives common CIS operating instructions.",
            ["cis repo init"]));
        artifacts.Add(new RepositoryStarterArtifact(
            repositoryInstructionDefinition,
            repositoryInstructionDefinition,
            ".github/instructions/cis-repository.instructions.md",
            CreateRepositoryInstruction(documentationRoot),
            null));

        const string standardsInstructionDefinition = "guidance.instruction.standards-governance";
        selections.Add(new RepositoryStarterSelection(
            standardsInstructionDefinition,
            "Every initialized repository receives standards routing, rule stability, exception, and evidence instructions.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            standardsInstructionDefinition,
            standardsInstructionDefinition,
            ".github/instructions/cis-standards-governance.instructions.md",
            CreateStandardsGovernanceInstruction(documentationRoot),
            null));

        const string deliveryInstructionDefinition = "guidance.instruction.change-delivery";
        selections.Add(new RepositoryStarterSelection(
            deliveryInstructionDefinition,
            "Every initialized repository receives authority and command rules for CIS change dossiers.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            deliveryInstructionDefinition,
            deliveryInstructionDefinition,
            ".github/instructions/cis-change-delivery.instructions.md",
            CreateChangeDeliveryInstruction(documentationRoot),
            null));

        const string businessRequirementsInstructionDefinition = "guidance.instruction.business-requirements";
        selections.Add(new RepositoryStarterSelection(
            businessRequirementsInstructionDefinition,
            "Every initialized repository receives BRD authority, currency, and approval rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            businessRequirementsInstructionDefinition,
            businessRequirementsInstructionDefinition,
            ".github/instructions/cis-business-requirements.instructions.md",
            CreateBusinessRequirementsInstruction(documentationRoot),
            null));

        const string featureSpecificationInstructionDefinition = "guidance.instruction.feature-specifications";
        selections.Add(new RepositoryStarterSelection(
            featureSpecificationInstructionDefinition,
            "Every initialized repository receives feature-specification traceability, validation, and approval rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            featureSpecificationInstructionDefinition,
            featureSpecificationInstructionDefinition,
            ".github/instructions/cis-feature-specifications.instructions.md",
            CreateFeatureSpecificationInstruction(documentationRoot),
            null));

        const string technicalIntentInstructionDefinition = "guidance.instruction.technical-intent";
        selections.Add(new RepositoryStarterSelection(
            technicalIntentInstructionDefinition,
            "Every initialized repository receives workspace technical-intent authority and readiness rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            technicalIntentInstructionDefinition,
            technicalIntentInstructionDefinition,
            ".github/instructions/cis-technical-intent.instructions.md",
            CreateTechnicalIntentInstruction(documentationRoot),
            null));

        const string trackerInstructionDefinition = "guidance.instruction.external-tracker";
        selections.Add(new RepositoryStarterSelection(
            trackerInstructionDefinition,
            "Every initialized repository receives external tracker authority, dry-run, conflict, and credential rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            trackerInstructionDefinition,
            trackerInstructionDefinition,
            ".github/instructions/cis-external-tracker.instructions.md",
            CreateExternalTrackerInstruction(documentationRoot),
            null));

        if (HasRole(classification, "backend-api-producer", "backend-api-consumer"))
        {
            const string apiInstructionDefinition = "guidance.instruction.api-governance";
            selections.Add(new RepositoryStarterSelection(
                apiInstructionDefinition,
                "API-bearing repositories receive deterministic discovery, validation, compatibility, and co-change instructions.",
                ["backend API role"]));
            artifacts.Add(new RepositoryStarterArtifact(
                apiInstructionDefinition,
                apiInstructionDefinition,
                ".github/instructions/cis-api-governance.instructions.md",
                CreateApiGovernanceInstruction(documentationRoot),
                null));
        }

        const string feedbackInstructionDefinition = "guidance.instruction.feedback-loop";
        selections.Add(new RepositoryStarterSelection(
            feedbackInstructionDefinition,
            "Every initialized repository receives privacy and evidence rules for the CIS feedback loop.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            feedbackInstructionDefinition,
            feedbackInstructionDefinition,
            ".github/instructions/cis-feedback-loop.instructions.md",
            CreateFeedbackLoopInstruction(),
            null));

        const string executionInstructionDefinition = "guidance.instruction.delivery-execution";
        selections.Add(new RepositoryStarterSelection(
            executionInstructionDefinition,
            "Every initialized repository receives execution, agent-result, verification, diagnostics, and learning authority rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            executionInstructionDefinition,
            executionInstructionDefinition,
            ".github/instructions/cis-delivery-execution.instructions.md",
            CreateDeliveryExecutionInstruction(documentationRoot),
            null));

        AddUiFrameworkGuidance(documentationRoot, classification, selections, artifacts);

        foreach (var component in classification.Components)
        {
            if (component.Languages.Contains("csharp", StringComparer.Ordinal))
            {
                AddInstruction(
                    component,
                    "csharp",
                    "C# and .NET",
                    "Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.",
                    selections,
                    artifacts);
            }

            if (component.Frameworks.Contains("angular", StringComparer.Ordinal))
            {
                AddInstruction(
                    component,
                    "angular",
                    "Angular",
                    "Use the repository package manager. Validate affected Angular builds, tests, routes, API clients, and templates.",
                    selections,
                    artifacts);
            }

            if (component.Frameworks.Contains("nextjs", StringComparer.Ordinal)
                || component.Frameworks.Contains("react", StringComparer.Ordinal)
                || component.Frameworks.Contains("vue", StringComparer.Ordinal))
            {
                var framework = component.Frameworks.First(value =>
                    value is "nextjs" or "react" or "vue");
                AddInstruction(
                    component,
                    framework,
                    framework == "nextjs" ? "Next.js" : char.ToUpperInvariant(framework[0]) + framework[1..],
                    "Use the repository package manager. Validate affected builds, routes, server/client boundaries, API calls, rendering behavior, and frontend tests.",
                    selections,
                    artifacts);
            }

            if (component.Languages.Contains("swift", StringComparer.Ordinal))
            {
                AddInstruction(
                    component,
                    "swift",
                    "Swift and Apple platforms",
                    "Preserve platform deployment targets and concurrency rules. Validate affected Swift packages or Xcode schemes, navigation, API clients, and UI behavior.",
                    selections,
                    artifacts);
            }

            if (component.Languages.Contains("kotlin", StringComparer.Ordinal))
            {
                AddInstruction(
                    component,
                    "kotlin",
                    "Kotlin and Android",
                    "Use the repository Gradle wrapper. Validate affected modules, variants, Compose or native UI, navigation, API clients, and tests.",
                    selections,
                    artifacts);
            }

            if (component.Frameworks.Contains("godot", StringComparer.Ordinal))
            {
                AddInstruction(
                    component,
                    "godot",
                    "Godot",
                    "Preserve scene, resource, autoload, input, plugin, and export relationships. Validate affected projects with the repository's Godot version, including headless tests or scene checks where available.",
                    selections,
                    artifacts);
            }
        }
    }

    private static void AddImplementationSkillPacks(
        string repositoryId,
        string documentationRoot,
        RepositoryClassification classification,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var packs = ImplementationSkillPackRegistry.Select(classification);
        const string profileDefinition = "guidance.implementation-skill-packs";
        var profilePath = $"{documentationRoot}/references/implementation-skill-packs.md";
        selections.Add(new RepositoryStarterSelection(
            profileDefinition,
            "Record the implementation skill packs selected from repository classification evidence.",
            packs.SelectMany(pack => pack.Evidence).Distinct(StringComparer.Ordinal).Take(20).ToArray()));
        artifacts.Add(new RepositoryStarterArtifact(
            profileDefinition,
            profileDefinition,
            profilePath,
            CreateImplementationSkillPackProfile(repositoryId, packs),
            new CatalogArtifactEntry(
                $"{repositoryId}:reference:implementation-skill-packs",
                profilePath,
                "implementation-skill-pack-profile",
                "active",
                "canonical")));

        foreach (var pack in packs)
        {
            selections.Add(new RepositoryStarterSelection(
                $"guidance.skill-pack.{pack.Id}",
                pack.Reason,
                pack.Evidence));
            foreach (var skill in pack.Skills)
            {
                AddSkill(
                    skill.Name,
                    $"Selected by the {pack.Title} implementation skill pack.",
                    skill.Content,
                    pack.Evidence,
                    selections,
                    artifacts);
            }
        }
    }

    private static string CreateImplementationSkillPackProfile(
        string repositoryId,
        IReadOnlyList<SelectedImplementationSkillPack> packs)
    {
        var rows = packs.Count == 0
            ? "| _None_ | No implementation pack matched the current classification. |  |\n"
            : string.Join("\n", packs.Select(pack =>
                $"| `{pack.Id}` | {pack.Reason} | {string.Join(", ", pack.Skills.Select(skill => $"`cis-{skill.Name}`"))} |")) + "\n";
        var evidence = packs.Count == 0
            ? "- No matching component evidence was found. Rerun initialization after adding or reclassifying components.\n"
            : string.Join("\n", packs.SelectMany(pack => pack.Evidence.Select(item => $"- `{pack.Id}`: {item}"))) + "\n";

        return $"""
            ---
            title: "Implementation Skill Packs"
            type: implementation-skill-pack-profile
            status: Active
            owner: "Repository maintainers"
            last_reviewed: "2026-08-15"
            review_cadence: "on repository classification change"
            cis:
              stable_id: {repositoryId}:reference:implementation-skill-packs
            ---

            # Implementation skill packs

            This profile records the implementation skills selected by `cis repo init` from confirmed repository classification evidence. Markdown skills under `.github/skills/` remain reviewable guidance; they do not grant tools, credentials, or approval authority.

            | Pack | Selection reason | Seeded skills |
            | --- | --- | --- |
            {rows}
            ## Classification evidence

            {evidence}
            ## Reconciliation

            Rerun `cis repo init --root <documentation-root> --dry-run` after component, language, framework, role, or capability changes. Review the plan, then apply with `--yes`. Run `cis skills validate --strict` after reconciliation.
            """ + "\n";
    }

    private static void AddUiFrameworkGuidance(
        string documentationRoot,
        RepositoryClassification classification,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        foreach (var resolution in UiFrameworkResolver.Resolve(classification))
        {
            var definition = $"guidance.instruction.ui-framework.{resolution.ComponentId}";
            var path = $".github/instructions/{resolution.ComponentId}-ui-framework.instructions.md";
            var applyTo = resolution.ComponentRoot == "."
                ? "**/*.{ts,tsx,js,jsx,vue,html,css,scss,swift,kt,kts,gd,tscn,tres}"
                : $"{resolution.ComponentRoot}/**/*.{{ts,tsx,js,jsx,vue,html,css,scss,swift,kt,kts,gd,tscn,tres}}";
            selections.Add(new RepositoryStarterSelection(
                definition,
                $"UI framework resolution for '{resolution.ComponentId}' is {resolution.Resolution.ToLowerInvariant()}.",
                resolution.Evidence));
            artifacts.Add(new RepositoryStarterArtifact(
                definition,
                definition,
                path,
                $"---\napplyTo: \"{applyTo}\"\n---\n\n" +
                $"# UI framework guidance for {resolution.ComponentId}\n\n" +
                $"Canonical profile: `{documentationRoot}/references/ui-framework-profile.md`.\n\n" +
                $"- Application framework: {resolution.ApplicationFramework}\n" +
                $"- Resolution: {resolution.Resolution}\n" +
                $"- Effective UI framework: {resolution.UiFramework}\n" +
                $"- Styling and theming: {resolution.StylingAndTheming}\n" +
                $"- Animation and motion: {resolution.AnimationAndMotion}\n\n" +
                "Before frontend or design work, inspect package manifests, framework configuration, imports, shared components, shell code, and theme assets. " +
                "A detected existing framework takes precedence over CIS defaults. Use the recorded default only when that inspection confirms no framework is already in use. " +
                "Do not introduce a second component system, install packages, or replace an existing system merely to match a CIS default. " +
                "If source evidence conflicts with the profile, stop, rerun `cis repo init`, and surface the reconciliation for human review.\n",
                null));
        }
    }

    private static void AddSkill(
        string name,
        string reason,
        string content,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var definition = $"guidance.skill.{name}";
        selections.Add(new RepositoryStarterSelection(definition, reason, ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            definition,
            definition,
            $".github/skills/cis-{name}/SKILL.md",
            content,
            null));
    }

    private static void AddSkill(
        string name,
        string reason,
        string content,
        IReadOnlyList<string> evidence,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var definition = $"guidance.skill.{name}";
        selections.Add(new RepositoryStarterSelection(definition, reason, evidence));
        artifacts.Add(new RepositoryStarterArtifact(
            definition,
            definition,
            $".github/skills/cis-{name}/SKILL.md",
            content,
            null));
    }

    private static void AddInstruction(
        RepositoryComponentClassification component,
        string kind,
        string title,
        string guidance,
        ICollection<RepositoryStarterSelection> selections,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var definition = $"guidance.instruction.{kind}";
        var artifactId = $"{definition}.{component.Id}";
        var path = $".github/instructions/{component.Id}-{kind}.instructions.md";
        var root = component.Root == "." ? "**" : component.Root;
        var extensionPattern = kind switch
        {
            "csharp" => "**/*.cs",
            "swift" => "**/*.swift",
            "kotlin" => "**/*.{kt,kts}",
            "godot" => "**/*.{godot,gd,tscn,tres,res}",
            _ => "**/*.{ts,tsx,js,jsx,html,css,scss}",
        };
        selections.Add(new RepositoryStarterSelection(
            definition,
            $"{title} component '{component.Id}' was detected.",
            component.Evidence));
        artifacts.Add(new RepositoryStarterArtifact(
            artifactId,
            definition,
            path,
            $"---\napplyTo: \"{root}/{extensionPattern}\"\n---\n\n" +
            $"# {title} guidance for {component.Id}\n\n" +
            $"Component root: `{component.Root}`.\n\n" +
            $"{guidance}\n\n" +
            "Before changing behavior, inspect the applicable specifications, references, callers, and tests.\n",
            null));
    }

    private static void AddReferenceFamily(
        string repositoryId,
        string documentationRoot,
        ReferenceFamily family,
        IReadOnlyList<IReadOnlyList<string>> rows,
        ICollection<RepositoryStarterArtifact> artifacts)
    {
        var definition = $"reference.{family.Slug}";
        var specPath = $"{documentationRoot}/specs/{family.Slug}-spec.md";
        var referencePath = $"{documentationRoot}/references/{family.Slug}.md";
        var specId = $"{repositoryId}:spec:{family.Slug}";
        var referenceId = $"{repositoryId}:reference:{family.Slug}";
        artifacts.Add(new RepositoryStarterArtifact(
            $"{definition}.spec",
            definition,
            specPath,
            CreateReferenceSpecification(family, specId, referenceId),
            new CatalogArtifactEntry(
                specId,
                specPath,
                GetSpecificationType(family.Slug),
                "draft",
                "canonical")));
        artifacts.Add(new RepositoryStarterArtifact(
            $"{definition}.reference",
            definition,
            referencePath,
            CreateReferenceDocument(family, referenceId, specId, rows),
            new CatalogArtifactEntry(
                referenceId,
                referencePath,
                GetReferenceType(family.Slug),
                "draft",
                "canonical")));
    }

    private static string CreateRepositoryProfile(
        string repositoryId,
        RepositoryClassification classification)
    {
        var lines = new List<string>
        {
            "---",
            $"title: \"{repositoryId} Repository Profile\"",
            "type: reference",
            "status: Active",
            "owner: Repository maintainer",
            "review_cadence: on repository change",
            "cis:",
            $"  stable_id: {repositoryId}:reference:repository-profile",
            "---",
            string.Empty,
            $"# {repositoryId} Repository Profile",
            string.Empty,
            $"Repository shape: **{classification.Shape}**.",
            string.Empty,
            "## Components",
            string.Empty,
        };

        foreach (var component in classification.Components)
        {
            lines.Add($"### {component.Id}");
            lines.Add(string.Empty);
            lines.Add($"- Root: `{component.Root}`");
            lines.Add($"- Languages: {string.Join(", ", component.Languages)}");
            lines.Add($"- Frameworks: {string.Join(", ", component.Frameworks)}");
            lines.Add($"- Roles: {string.Join(", ", component.Roles)}");
            lines.Add($"- Capabilities: {string.Join(", ", component.Capabilities)}");
            lines.Add($"- Confidence: {component.Confidence}");
            lines.Add("- Evidence:");
            lines.AddRange(component.Evidence.Select(evidence => $"  - {evidence}"));
            lines.Add(string.Empty);
        }

        lines.Add("This profile was deterministically classified by `cis repo init`. Review classification changes before applying them.");
        return string.Join('\n', lines) + "\n";
    }

    private static string CreateUiFrameworkProfile(
        string repositoryId,
        IReadOnlyList<UiFrameworkResolution> resolutions)
    {
        var lines = new List<string>
        {
            "---",
            $"title: \"{repositoryId} UI Framework Profile\"",
            "type: ui-framework-profile",
            "status: Active",
            "owner: Repository maintainer",
            "review_cadence: on frontend dependency or architecture change",
            "cis:",
            $"  stable_id: {repositoryId}:reference:ui-framework-profile",
            "---",
            string.Empty,
            $"# {repositoryId} UI Framework Profile",
            string.Empty,
            "CIS resolves UI frameworks in two stages: preserve a framework demonstrated by repository evidence; otherwise use the classification-bound default below. Initialization records policy and guidance but does not install dependencies.",
            string.Empty,
            "| Component | Application | Resolution | Effective UI framework | Styling / theming | Animation / motion | Evidence |",
            "|---|---|---|---|---|---|---|",
        };

        lines.AddRange(resolutions.Select(resolution =>
            $"| {EscapeTableValue(resolution.ComponentId)} | {EscapeTableValue(resolution.ApplicationFramework)} | " +
            $"{EscapeTableValue(resolution.Resolution)} | {EscapeTableValue(resolution.UiFramework)} | " +
            $"{EscapeTableValue(resolution.StylingAndTheming)} | {EscapeTableValue(resolution.AnimationAndMotion)} | " +
            $"{EscapeTableValue(string.Join("; ", resolution.Evidence))} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Resolution rules",
            string.Empty,
            "1. Inspect manifests, lockfiles, imports, component source, framework configuration, application shell, and theme assets before applying a default.",
            "2. Preserve an existing framework and its conventions even when it differs from the CIS default.",
            "3. Apply a default only when no existing component framework is evidenced for that UI component.",
            "4. Treat Tailwind CSS as styling/theming infrastructure, not by itself as a complete component framework.",
            "5. Prefer CSS transitions for simple effects. Add Motion or another animation dependency only when an approved interaction requires complex, coordinated, gesture, or layout motion.",
            "6. Reconcile this profile with `cis repo init` after frontend dependencies, projects, or architecture change. Human-authored exceptions require a linked ADR or approved design-system decision.",
            string.Empty,
            "## Default matrix",
            string.Empty,
            "| Classification | Default when no UI framework exists | Styling / theming | Motion policy |",
            "|---|---|---|---|",
            "| React or Next.js | shadcn/ui | Tailwind CSS | CSS first; Motion for React only when approved behavior needs it |",
            "| Angular | Angular Material | Angular Material theming | CSS/framework-native first |",
            "| Vue or Nuxt | Nuxt UI | Tailwind CSS | CSS first; motion library only when required |",
            "| Apple native | SwiftUI | SwiftUI styles and environment tokens | SwiftUI animation APIs |",
            "| Android native | Jetpack Compose Material 3 | Material 3 theme and tokens | Compose animation APIs |",
            "| Godot | Control nodes and Theme resources | Godot Theme resources | AnimationPlayer/Tween only when required |",
            string.Empty,
            "Sera UI is a compatible React/Next source-owned component registry and may be selected explicitly or preserved when detected. It is not the cross-platform default, and its use does not make animation mandatory.",
            string.Empty,
            "## Rationale by component",
            string.Empty,
        ]);

        foreach (var resolution in resolutions)
        {
            lines.Add($"- **{resolution.ComponentId}:** {resolution.Rationale}");
        }

        return string.Join('\n', lines) + "\n";
    }

    private static string CreateReferenceSpecification(
        ReferenceFamily family,
        string stableId,
        string referenceId)
    {
        return $"---\ntitle: \"{family.Title} Specification\"\ntype: specification\nstatus: Draft\n" +
            "owner: Repository maintainer\nreview_cadence: on change\ncis:\n" +
            $"  stable_id: {stableId}\n---\n\n# {family.Title} Specification\n\n" +
            $"## Purpose\n\nThis specification governs {family.Description}.\n\n" +
            "## Source of truth\n\n" +
            $"The paired `{referenceId}` reference is the current repository inventory. This specification owns its maintenance rules.\n\n" +
            "## Required fields\n\n" +
            string.Join('\n', family.Columns.Select(column => $"- {column}")) +
            "\n\n## Maintenance rules\n\n" +
            "- Use stable, append-only row identities.\n" +
            "- Use row lifecycle values `Draft`, `Planned`, `Verified`, `Deprecated`, or `Withdrawn`.\n" +
            "- Keep deterministic discoveries `Draft` until a maintainer verifies their meaning and relationships.\n" +
            "- Record ownership, evidence, provenance, compatibility, and sensitivity where applicable.\n" +
            "- Update the reference in the same change that changes the governed behavior.\n" +
            "- Retain deprecated rows until the compatibility and replacement story is documented.\n" +
            "- Validate deterministic implementation evidence where available.\n";
    }

    private static string CreateDocumentationGovernanceStandard(string repositoryId) => $$"""
        ---
        title: Documentation Governance Standard
        type: standard
        status: Active
        targets:
          - documentation
          - repository-governance
        stacks:
          - generic
        owner: Repository maintainer
        last_reviewed: 2026-08-15
        review_cadence: on change
        source_of_truth: This file
        cis:
          stable_id: {{repositoryId}}:standard:documentation-governance
        ---

        # Documentation Governance Standard

        ## Purpose

        Define the required structure, authority, lifecycle, and verification expectations for repository-owned standards.

        ## Scope

        This standard applies to every document cataloged with `type: standard`, its stable rules, its conformance mappings, and agent or human work governed by those rules.

        ## Normative language

        `MUST` and `MUST NOT` are mandatory. `SHOULD` requires recorded rationale when not followed. `MAY` is optional. A policy defines authority or mandatory outcomes; a standard defines the expected way of satisfying them; a specification defines a precise product or tool contract; a procedure describes ordered execution steps.

        ## Rules

        - **CIS-STD-DOC-001** A standard MUST be canonical Markdown below the configured `standards/` directory and MUST be registered in `catalog.yml` with `type: standard` and `authority: canonical`.
        - **CIS-STD-DOC-002** A standard MUST declare title, lifecycle status, targets, owner, review date, review cadence, source of truth, and an immutable `cis.stable_id` matching its catalog ID.
        - **CIS-STD-DOC-003** Every normative requirement MUST use a repository-unique stable rule ID. Retired rule IDs MUST NOT be reused.
        - **CIS-STD-DOC-004** A standard MUST distinguish mandatory, recommended, and optional expectations using the normative language defined above.
        - **CIS-STD-DOC-005** Every active rule MUST have a row in the standards conformance matrix, even when its current enforcement is manual review.
        - **CIS-STD-DOC-006** Advisory model or heuristic evidence MUST NOT be reported as a confirmed breach or proof of compliance without deterministic evidence or explicit human review.
        - **CIS-STD-DOC-007** Exceptions MUST name the approving authority, rationale, scope, expiry or review condition, and compensating controls. An agent MUST NOT approve its own exception.
        - **CIS-STD-DOC-008** A relevant standard MUST be resolved before governed implementation begins and changed standards MUST be validated before completion.
        - **CIS-STD-DOC-009** Imported standards MUST be validated in staging, previewed before canonical admission, confirmation-gated, and reconciled with the catalog and conformance matrix without overwriting divergent existing content.
        - **CIS-STD-DOC-010** Standards audits MUST preserve deterministic and model provenance. Model findings MUST remain advisory, and automated remediation MUST be limited to reversible quarantine that preserves historical catalog and conformance evidence.

        ## Verification

        Run `cis standards applicable` with the affected targets and stacks, then run `cis standards validate --strict`. Review `cis standards conformance --gaps-only` for unmapped or advisory-only enforcement. After importing or changing the standards collection, run `cis standards audit`; use `--no-llm` when only deterministic comparison is authorized.

        ## Exceptions

        Record approved exceptions in the affected change dossier or ADR and link the rule ID. Absence of automated enforcement is not an exception; map the rule to `manual-review` until stronger enforcement exists.

        ## Related documents

        - `../specs/standards-governance-spec.md`
        - `../references/standards-conformance-matrix.md`
        - `../templates/standard-template.md`
        """;

    private static string CreateStandardsGovernanceSpecification(string repositoryId, string documentationRoot) => $$"""
        ---
        title: Standards Governance Specification
        type: governance-specification
        status: Active
        owner: Repository maintainer
        review_cadence: on change
        cis:
          stable_id: {{repositoryId}}:spec:standards-governance
        ---

        # Standards Governance Specification

        ## Intent

        Standards define expected ways of working through testable, stable rules. Markdown below `{{documentationRoot}}/standards/` is canonical; command output and graph or index state are derived routing aids.

        ## Document contract

        Each standard must declare `title`, `type: standard`, `status`, `targets`, optional `stacks`, `owner`, `last_reviewed`, `review_cadence`, `source_of_truth`, and `cis.stable_id`. Required sections are Purpose, Scope, Normative language, Rules, Verification, and Exceptions. Active and draft standards require stable rule IDs.

        Lifecycle statuses are `Draft`, `Active`, `Deprecated`, and `Archived`. Stable IDs and rule IDs are immutable; retired IDs are never reused. A standard becomes applicable by target and, when declared, stack. `generic` and `all` stacks match every stack.

        ## Command behavior

        - `cis standards inventory` lists cataloged standards and supports target, stack, and status filters.
        - `cis standards applicable` requires at least one target and returns matching active standards.
        - `cis standards validate` checks location, catalog authority, metadata, sections, stable rule uniqueness, lifecycle, and conformance coverage. `--strict` turns warnings into failures.
        - `cis standards conformance` reads the canonical matrix; `--gaps-only` isolates unmapped, advisory-only, or inactive mappings.
        - `cis standards import` accepts local Markdown files/directories, ZIPs, GitHub repository/tree URLs, and direct ZIP URLs. It validates staged copies, supports explicit safe structural `--fix`, may assign stable IDs to existing normative statements but refuses to invent semantics, requires `--yes` for canonical changes, and atomically adds catalog entries plus conservative `manual-review` mappings.
        - `cis standards audit` detects deterministic duplicate rules and bodies, bounds semantic pair review, prefers a local model, falls back to configured remote generation unless `--no-llm` is supplied, and persists derived evidence below `.cis/local/standards/`.
        - `cis standards audit --fix` may move redundant deterministic duplicates and evidence-backed conflicts to `standards-quarantine/`. It changes their catalog entries to historical quarantined records and preserves former matrix rows as non-active quoted history; it never merges rules or approves a resolution.
        - `cis standards patterns` inventories and validates versioned built-in, extension-provider, and canonical repository pattern definitions. Duplicate stable IDs are conflicts and are never resolved by provider load order.
        - `cis standards infer` requires a fresh compiler-backed graph, evaluates only applicable patterns whose required graph capabilities are present, preserves matches and counterexamples below `.cis/local/standards/inference/`, and emits unreviewed candidates without changing canonical standards.
        - Commands support human, JSON, and compact agent output.

        ## Conformance model

        Every active rule has a canonical matrix row with standard ID, rule ID, surface, enforcement, evidence, lifecycle status, and notes. Allowed enforcement states are:

        | State | Meaning |
        |---|---|
        | `deterministic` | A reproducible evaluator can establish pass or fail for the mapped rule. |
        | `architecture-test` | A structural test enforces the mapped rule. |
        | `manual-review` | Named human review and recorded evidence are required. |
        | `advisory-model` | A local or remote model may identify candidates but cannot prove a breach or compliance. |
        | `not-mapped` | No enforcement route exists; this is a visible governance gap. |

        ## Authority and exceptions

        Tools and agents may inventory, route, validate, and propose remediation. Only the named human authority may accept an exception or decide that advisory evidence is a confirmed finding. Exceptions record rule ID, approver, rationale, bounded scope, expiry or review condition, and compensating controls.

        ## Initialization and reconciliation

        `cis repo init` creates the standards directory and idempotently seeds this specification, the documentation governance standard, a classification-selected PARR-derived default set, standard and inference-pattern templates, the conformance matrix, and agent guidance. Defaults preserve source provenance and omit product-specific PARR assumptions. Existing human changes are preserved by managed-starter collision rules.
        """;

    private static string CreateStandardsConformanceMatrix(
        string repositoryId,
        IReadOnlyList<DefaultStandardDefinition> defaultStandards)
    {
        var content = $$"""
        ---
        title: Standards Conformance Matrix
        type: standards-conformance-reference
        status: Active
        owner: Repository maintainer
        review_cadence: on change
        cis:
          stable_id: {{repositoryId}}:reference:standards-conformance-matrix
        ---

        # Standards Conformance Matrix

        This canonical matrix maps every active standard rule to an enforcement route. Advisory model output identifies candidates only; it does not prove a breach or compliance.

        | Standard ID | Rule ID | Governed surface | Enforcement | Evidence | Status | Notes |
        |---|---|---|---|---|---|---|
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-001 | `{{repositoryId}} documentation root` | deterministic | `cis docs validate --strict`; `cis standards validate --strict` | Active | Catalog and location checks. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-002 | `standards/*.md` | deterministic | `cis standards validate --strict` | Active | Required metadata and stable-ID alignment. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-003 | `standards/*.md` | deterministic | `cis standards validate --strict` | Active | Rule presence and repository-wide uniqueness. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-004 | `standards/*.md` | manual-review | maintainer standard review | Active | Normative clarity requires semantic review. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-005 | `references/standards-conformance-matrix.md` | deterministic | `cis standards validate --strict` | Active | Every active rule requires a row. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-006 | model and heuristic findings | manual-review | finding disposition record | Active | Human or deterministic evidence confirms findings. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-007 | change dossiers and ADRs | manual-review | recorded exception approval | Active | Agents cannot approve exceptions. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-008 | governed implementation | manual-review | change plan and completion evidence | Active | Applicable standards are routed before work. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-009 | standard imports | deterministic | `cis standards import --dry-run`; catalog and matrix validation | Active | Canonical writes require confirmation and reject divergent destinations. |
        | {{repositoryId}}:standard:documentation-governance | CIS-STD-DOC-010 | standards collection | deterministic | `cis standards audit`; quarantine history | Active | Model classifications remain advisory candidates. |
        """;
        var builder = new System.Text.StringBuilder(content.TrimEnd()).AppendLine();
        foreach (var standard in defaultStandards)
        foreach (var rule in standard.Rules)
            builder.AppendLine($"| {repositoryId}:standard:{standard.Slug} | {rule.Id} | {string.Join(", ", standard.Targets)} | manual-review | {rule.Verification.Replace("|", "¦", StringComparison.Ordinal)} | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |");
        return builder.ToString();
    }

    private static string CreateStandardTemplate() => """
        ---
        title: <Standard title>
        type: standard
        status: Draft
        targets:
          - <backend|frontend|mobile|documentation|infrastructure|operations|repository-governance>
        stacks:
          - <generic|csharp|typescript|swift|kotlin|other>
        owner: <accountable human or team>
        last_reviewed: YYYY-MM-DD
        review_cadence: <on change|quarterly|annual>
        source_of_truth: This file
        cis:
          stable_id: <repository-id>:standard:<slug>
        ---

        # <Standard title>

        ## Purpose

        <Expectation and reason.>

        ## Scope

        <Governed and excluded surfaces.>

        ## Normative language

        `MUST` and `MUST NOT` are mandatory. `SHOULD` requires recorded rationale when not followed. `MAY` is optional.

        ## Rules

        - **<PREFIX>-001** <Subject> MUST <testable expectation>.

        ## Verification

        <Deterministic checks, architecture tests, or named human review. Add every rule to the conformance matrix.>

        ## Exceptions

        <Approver, rationale, scope, expiry or review condition, and compensating controls.>

        ## Related documents

        - `<relative path>`
        """;

    private static string CreateStandardPatternTemplate() => """
        ---
        title: <Pattern title>
        type: standard-inference-pattern
        status: Draft
        owner: <accountable human or team>
        review_cadence: on compiler graph or architecture change
        cis:
          stable_id: <repository-id>.pattern.<language>.<pattern-name>
        pattern:
          version: 1
          description: <Graph convention recognized by this pattern.>
          languages:
            - csharp
          roles: []
          capabilities: []
          required_graph_capabilities:
            - compiler.symbols
            - compiler.calls
          subject:
            kind: symbol
            subtype: method
            facets:
              - compiler-bound
            property_equals: {}
            property_contains: {}
            path_prefixes:
              - src/
            excluded_path_prefixes: []
          requires:
            - direction: outgoing
              edge_type: calls
              target:
                kind: symbol
                facets:
                  - compiler-bound
                property_equals: {}
                property_contains: {}
                path_prefixes:
                  - src/
                excluded_path_prefixes: []
          forbids: []
          thresholds:
            minimum_occurrences: 5
            minimum_consistency: 0.80
          candidate:
            target: architecture
            proposed_level: SHOULD
            wording: <Complete proposed normative statement.>
            suggested_enforcement: architecture-test
            verification: <How a maintainer could verify the proposed rule.>
        ---

        # <Pattern title>

        ## Intent

        <Why this recurring graph shape may represent a repository standard.>

        ## Review cautions

        <Known legacy patterns, generated-code exclusions, false positives, and architectural alternatives.>
        """;

    private static string CreateReferenceDocument(
        ReferenceFamily family,
        string stableId,
        string specificationId,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var header = "| " + string.Join(" | ", family.Columns) + " |";
        var separator = "|" + string.Join("|", family.Columns.Select(_ => "---")) + "|";
        var body = rows.Count == 0
            ? "| TODO |" + string.Concat(family.Columns.Skip(1).Select(_ => "  |"))
            : string.Join('\n', rows.Select(row =>
                "| " + string.Join(" | ", row.Select(EscapeTableValue)) + " |"));
        var seedNotice = rows.Count == 0
            ? "No deterministic facts were extracted. Replace the placeholder row with verified repository facts."
            : $"CIS seeded {rows.Count} row(s) from deterministic repository evidence. Review them before changing this document to Active.";
        return $"---\ntitle: \"{family.Title}\"\ntype: reference\nstatus: Draft\n" +
            "owner: Repository maintainer\nreview_cadence: on change\ncis:\n" +
            $"  stable_id: {stableId}\n---\n\n# {family.Title}\n\n" +
            $"Governed by `{specificationId}`. {seedNotice}\n\n" +
            $"{header}\n{separator}\n{body}\n";
    }

    private static string EscapeTableValue(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string GetSpecificationType(string slug)
        => slug switch
        {
            "command-dictionary" or "event-dictionary" or "workflow-state-dictionary"
                or "business-invariant-catalogue" or "projection-dictionary"
                or "problem-details-catalogue" or "module-ownership-map"
                => "domain-behaviour-governance-specification",
            "traceability-matrix" => "traceability-governance-specification",
            _ => "contract-governance-specification",
        };

    private static string GetReferenceType(string slug)
        => slug switch
        {
            "command-dictionary" or "event-dictionary" or "workflow-state-dictionary"
                or "business-invariant-catalogue" or "projection-dictionary"
                or "problem-details-catalogue" or "module-ownership-map"
                => "domain-behaviour-reference",
            "traceability-matrix" => "traceability-reference",
            _ => "contract-reference",
        };

    private static string CreateProductIntentSpecification(
        string repositoryId,
        RepositoryClassification classification)
    {
        var capabilities = classification.Components
            .SelectMany(component => component.Capabilities)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var capabilityText = capabilities.Length == 0
            ? "- TODO: Describe the product or repository capabilities."
            : string.Join('\n', capabilities.Select(capability => $"- Detected capability: `{capability}`"));
        return $"---\ntitle: \"{repositoryId} Product Intent\"\ntype: specification\nstatus: Draft\n" +
            "scope: Repository\nowner: Repository maintainer\nreview_cadence: on product change\ncis:\n" +
            $"  stable_id: {repositoryId}:spec:product-intent\n---\n\n" +
            $"# {repositoryId} Product Intent\n\n" +
            "## Purpose and outcomes\n\nTODO: Describe the problem, intended users, and measurable outcomes.\n\n" +
            "## Actors and stakeholders\n\n| Actor | Need or responsibility | Success signal |\n|---|---|---|\n| TODO | TODO | TODO |\n\n" +
            "## Scope\n\n### In scope\n\n- TODO\n\n### Out of scope\n\n- TODO\n\n" +
            "## Capabilities\n\n" + capabilityText + "\n\n" +
            "## Requirements and acceptance\n\n" +
            "| Requirement ID | Requirement | Rationale | Acceptance evidence | Status |\n|---|---|---|---|---|\n" +
            "| REQ-TODO-001 | TODO | TODO | TODO | Draft |\n\n" +
            "## Business rules and controlled vocabulary\n\n- TODO: Record durable rules and terms, then link them to governed references.\n\n" +
            "## Success measures and risks\n\n- TODO: Define success measures, material risks, assumptions, and dependencies.\n";
    }

    private static string CreateTechnicalIntentSpecification(
        string repositoryId,
        RepositoryClassification classification,
        bool workspaceAuthority)
    {
        var components = classification.Components.Count == 0
            ? "| TODO | TODO | TODO | TODO |"
            : string.Join('\n', classification.Components.Select(component =>
                $"| {EscapeTableValue(component.Id)} | `{EscapeTableValue(component.Root)}` | " +
                $"{EscapeTableValue(string.Join(", ", component.Languages.Concat(component.Frameworks).Distinct(StringComparer.Ordinal)))} | " +
                $"{EscapeTableValue(string.Join(", ", component.Roles))} |"));
        var scope = workspaceAuthority ? "Workspace" : "Repository";
        var owner = workspaceAuthority ? "Product owner and repository maintainers" : "Repository maintainer";
        return $"---\ntitle: \"{repositoryId} Technical Intent\"\ntype: specification\nstatus: Draft\n" +
            $"scope: {scope}\nowner: {owner}\nlast_reviewed: null\nreview_cadence: on architecture change\ncis:\n" +
            $"  stable_id: {repositoryId}:spec:technical-intent\n" +
            "  technical_intent_schema: 1\n  approved_by: null\n  approved_at: null\n  approval_reason: null\n---\n\n" +
            $"# {repositoryId} Technical Intent\n\n" +
            (workspaceAuthority
                ? "This is the workspace technical authority derived from the active business requirements. Run `cis technical-intent init` to bind its reviewed baselines.\n\n"
                : string.Empty) +
            "## Design goals and principles\n\n- TODO: Record the principles that constrain implementation choices.\n\n" +
            "## Detected technical surface\n\n" +
            "| Component | Root | Languages and frameworks | Roles |\n|---|---|---|---|\n" + components + "\n\n" +
            "## Architecture and boundaries\n\n- TODO: Define module, service, process, deployment, and trust boundaries.\n\n" +
            "## Data and consistency\n\n- TODO: Define systems of record, lifecycle, consistency, migration, and recovery intent.\n\n" +
            "## Integration and contracts\n\n- TODO: Define API, event, package, external-system, and compatibility expectations.\n\n" +
            "## Security and privacy\n\n- TODO: Define identity, authorization, secret, sensitive-data, audit, and threat boundaries.\n\n" +
            "## Operations and observability\n\n- TODO: Define deployment, health, telemetry, diagnostics, rollback, and support expectations.\n\n" +
            "## Quality attributes\n\n" +
            "| Attribute | Requirement | Verification |\n|---|---|---|\n" +
            "| Reliability | TODO | TODO |\n| Performance | TODO | TODO |\n| Maintainability | TODO | TODO |\n" +
            "| Accessibility | TODO | TODO |\n| Security | TODO | TODO |\n\n" +
            "## Decisions and delivery constraints\n\n- Record durable choices as ADRs and link them here.\n\n" +
            "## Open technical decisions\n\n" +
            "| ID | Decision | Required before | Status | Resolution or rationale |\n|---|---|---|---|---|\n" +
            "| TI-DEC-001 | TODO | Change dossier creation | Open | TODO |\n";
    }

    private static string CreateFeatureSpecificationTemplate(string repositoryId) =>
        $"---\ntitle: \"Feature Specification Template\"\ntype: template\nstatus: Active\n" +
        "scope: Repository\nowner: Repository maintainer\nreview_cadence: on template change\n" +
        "targets:\n  - TODO: documentation, backend, frontend, mobile, native, data, integration, or delivery\n" +
        "stack:\n  - TODO: relevant languages and frameworks\n" +
        "references:\n  - TODO: governed BRD, architecture, contract, and reference paths\ncis:\n" +
        $"  stable_id: {repositoryId}:template:feature-spec\n---\n\n" +
        "# <Feature name>\n\nCopy this template into `specs/` and replace every placeholder, including the title, owner, and stable ID. Change front matter `type` to `feature-specification` and `status` to `Draft` so BRD reconciliation can discover it.\n\n" +
        "## Feature summary\n\nTODO\n\n## Goals\n\n1. TODO\n\n## Non-goals and explicit exclusions\n\n1. TODO\n\n" +
        "## Actors and scenarios\n\n| Actor | Scenario | Expected outcome |\n|---|---|---|\n| TODO | TODO | TODO |\n\n" +
        "## Functional requirements\n\nUse this table when requirements already have stable IDs. For narrative specifications,\nthe numbered Goals section is also accepted and receives derived `GOAL-NNN` IDs.\n\n" +
        "For every UI-bearing requirement, set `Frontend type` to exactly `public`, `customer`, or `backoffice`. Use `not-applicable` for non-frontend requirements. If one logical requirement affects multiple frontend types, use one row with a stable ID per type so scope, acceptance criteria, design, and implementation remain independently traceable.\n\n" +
        "| ID | Surface | Frontend type | Requirement | Acceptance criteria |\n|---|---|---|---|---|\n" +
        "| FEAT-TODO-001 | TODO: frontend, backend, full-stack, mobile, native, API, contract, data, security, delivery, or documentation | TODO: public, customer, backoffice, or not-applicable | TODO | TODO |\n\n" +
        "## Workflows, states, and invariants\n\n- TODO\n\n## Domain model, data, audit, and migrations\n\n- TODO\n\n" +
        "## API, contracts, permissions, and visibility\n\n- TODO\n- For every unauthenticated/public endpoint: TODO cache key/Vary dimensions, TTL and freshness, invalidation/refresh owner, HTTP cache semantics, stampede and failure behavior, cache-population abstraction, and proof the endpoint boundary does not directly access a database or repository; or Not applicable.\n\n" +
        "## UX, screens, and accessibility\n\n- TODO: routes, primary and alternate flows, empty/loading/error/denied states, responsive behavior, and accessibility\n\n" +
        "## Cross-module integrations, commands, and events\n\n- TODO\n\n" +
        "## Search, projection, and retrieval boundaries\n\n- TODO or Not applicable\n\n" +
        "## Lifecycle, conversion, and carry-forward\n\n- TODO or Not applicable\n\n" +
        "## Operational and security considerations\n\n- TODO\n\n" +
        "## Testing and regression requirements\n\n| Layer | Required evidence |\n|---|---|\n" +
        "| Documentation, policy, and contract drift | TODO |\n| Domain and application unit tests | TODO or Not applicable |\n" +
        "| Architecture and structural tests | TODO or Not applicable |\n| API, service, persistence, and migration integration tests | TODO or Not applicable; name any Testcontainers-provisioned dependency |\n" +
        "| Business acceptance tests | TODO or Not applicable |\n| Frontend component and accessibility tests | TODO or Not applicable |\n" +
        "| Browser or platform journeys using page objects or screen models | TODO or Not applicable |\n| Mutation testing and test-quality assurance | TODO or Not applicable |\n" +
        "| Coverage, independent assurance, and unrun-check reporting | TODO |\n\n" +
        "## Traceability and related decisions\n\n- Product requirement:\n- Technical intent:\n- ADRs:\n- References to update:\n";

    private static string CreateArchitectureDecisionTemplate(string repositoryId) =>
        $"---\ntitle: \"Architecture Decision Record Template\"\ntype: template\nstatus: Active\n" +
        "scope: Repository\nowner: Repository maintainer\nreview_cadence: on template change\ncis:\n" +
        $"  stable_id: {repositoryId}:template:adr\n---\n\n" +
        "# ADR-XXXX: <Decision title>\n\nCopy this template into `architecture/decisions/` and replace every placeholder, including the stable ID.\n\n" +
        "## Status\n\nProposed\n\n## Context\n\nTODO\n\n## Decision\n\nTODO\n\n" +
        "## Alternatives considered\n\n| Alternative | Advantages | Disadvantages |\n|---|---|---|\n| TODO | TODO | TODO |\n\n" +
        "## Consequences\n\n### Positive\n\n- TODO\n\n### Negative and risks\n\n- TODO\n\n" +
        "## Affected specifications and references\n\n- TODO\n\n## Verification and revisit conditions\n\n- TODO\n";

    private static string CreateSystemContextSpecification(
        string repositoryId,
        RepositoryClassification classification)
    {
        var components = classification.Components.Count == 0
            ? "| TODO | TODO | TODO | TODO |"
            : string.Join('\n', classification.Components.Select(component =>
                $"| {EscapeTableValue(component.Id)} | `{EscapeTableValue(component.Root)}` | " +
                $"{EscapeTableValue(string.Join(", ", component.Roles))} | " +
                $"{EscapeTableValue(string.Join(", ", component.Capabilities))} |"));
        return $"---\ntitle: \"{repositoryId} System Context\"\ntype: specification\nstatus: Draft\n" +
            "owner: Repository maintainer\nreview_cadence: on architecture change\ncis:\n" +
            $"  stable_id: {repositoryId}:spec:system-context\n---\n\n" +
            $"# {repositoryId} System Context\n\n" +
            "## Purpose and outcomes\n\nTODO: Describe the user or business outcome this repository supports.\n\n" +
            "## Boundary\n\nTODO: State what is inside this system boundary and what is explicitly outside it.\n\n" +
            $"## Detected components\n\nRepository shape: **{classification.Shape}**.\n\n" +
            "| Component | Root | Detected roles | Detected capabilities |\n|---|---|---|---|\n" +
            components + "\n\n" +
            "## Actors and external systems\n\n| Actor or system | Relationship | Contract or reference | Owner |\n|---|---|---|---|\n| TODO | TODO | TODO | TODO |\n\n" +
            "## Constraints and invariants\n\n- TODO: Record material technical, regulatory, operational, and compatibility constraints.\n";
    }

    private static string CreateDeliveryAndAssuranceSpecification(
        string repositoryId,
        RepositoryClassification classification)
    {
        var verification = classification.Components.Count == 0
            ? "- TODO: Add repository-specific build, test, analysis, and packaging commands."
            : string.Join('\n', classification.Components.Select(component =>
                $"- `{component.Id}` (`{component.Root}`): validate the affected " +
                $"{string.Join(", ", component.Languages.Concat(component.Frameworks).Distinct(StringComparer.Ordinal))} surface."));
        return $"---\ntitle: \"{repositoryId} Delivery and Assurance\"\ntype: specification\nstatus: Draft\n" +
            "owner: Repository maintainer\nreview_cadence: on delivery-policy change\ncis:\n" +
            $"  stable_id: {repositoryId}:spec:delivery-and-assurance\n---\n\n" +
            $"# {repositoryId} Delivery and Assurance\n\n" +
            "## Human control\n\n- A human owns scope, risk acceptance, canonical decisions, and release approval.\n" +
            "- Agents may propose and implement bounded changes but must surface ambiguity, collisions, and unverified assumptions.\n\n" +
            "## Documentation obligations\n\n- Update affected specifications and living references in the same change as behavior.\n" +
            "- Preserve stable IDs and catalog entries. Do not silently delete or replace canonical records.\n\n" +
            "## Deterministic verification\n\n" + verification + "\n\n" +
            "## Independent assurance\n\n- TODO: Define the required reviewer, automated gate, or independent check for each risk class.\n\n" +
            "## Completion evidence\n\n- Record commands, results, residual risks, and any checks that could not be run.\n";
    }

    private static string CreateRepositoryDeliveryPolicySpecification(string repositoryId) =>
        $"---\ntitle: \"{repositoryId} Repository Delivery Policy\"\ntype: specification\nstatus: Draft\n" +
        "owner: Repository maintainer\nreview_cadence: on delivery-workflow change\ncis:\n" +
        $"  stable_id: {repositoryId}:spec:repository-delivery-policy\n---\n\n" +
        $"# {repositoryId} Repository Delivery Policy\n\n" +
        "Until a maintainer reviews every field in this document, the effective mode is `local-only`: agents may make approved working-tree changes but must not infer authority to create/switch branches, commit, push, open or update pull requests, merge, tag, release, or mutate remote issues.\n\n" +
        "## Branch policy\n\n" +
        "- Default/base branch: TODO\n- Protected branches: TODO\n- Branch required: TODO (`yes`, `no`, or `repository workflow`)\n" +
        "- Naming convention: TODO\n- Creation point and branch reuse/worktree rules: TODO\n- Who may create or switch branches: TODO\n\n" +
        "## Commit policy\n\n" +
        "- Commit creation authority: TODO\n- Scope/commit-per-task rule: TODO\n- Message, signing, authorship, and co-author rules: TODO\n" +
        "- Required checks before commit: TODO\n- Generated artifacts and evidence allowed in commits: TODO\n\n" +
        "## Push and remote policy\n\n" +
        "- Remote and branch rules: TODO\n- Push authority: TODO\n- Force-push/lease policy: prohibited unless explicitly approved here\n" +
        "- Required checks before push: TODO\n- Tags/releases authority: TODO\n\n" +
        "## Pull-request policy\n\n" +
        "- Pull request required: TODO\n- Target branch and draft/final rules: TODO\n- Template, labels, reviewers, and checks: TODO\n" +
        "- Merge method and merge authority: TODO\n- Branch deletion/retention: TODO\n\n" +
        "## Change and tracker linkage\n\n" +
        "- CIS change/task linkage: TODO\n- GitHub/Jira issue linkage: TODO or Not applicable\n" +
        "- External tracker status never grants CIS plan, design, risk, or final-acceptance authority.\n\n" +
        "## Exceptions\n\n| Exception | Reason | Approver | Effective period/review condition |\n|---|---|---|---|\n| None | | | |\n\n" +
        "## Approval\n\n| Decision | Reviewer | Timestamp | Rationale |\n|---|---|---|---|\n| Not reviewed | Pending | Pending | Pending |\n";

    private static string CreateChangeImpactSkill() => """
        ---
        name: cis-change-impact
        description: Orchestrate CIS change preparation from exact-baseline dossier creation through graph context, reviewed impact, decisions, and validated bounded planning. Use for features, fixes, refactors, dependency/configuration/schema/API/workflow changes, or any request that must be assessed before implementation.
        ---

        # CIS Change Impact

        ## Workflow

        1. Read `.cis/repository.yml`, the repository profile, catalog, and applicable specifications.
        2. Use `cis-graph-context` to ensure a fresh graph and identify exact roots.
        3. In a workspace authority, run `cis technical-intent status`; stop unless the intent is Active and current.
        4. Use `cis-change-dossier` to create or inspect the exact-baseline change record.
        5. Use `cis-impact-review` to analyse and present findings for human disposition.
        6. Use `cis-decision-review` for unresolved blocking or advisory questions.
        7. Use `cis-bounded-planning` to build and validate work after impact review.
        8. Recheck planned versus actual scope with `cis-validate-completion` before closure.

        ## Guardrails

        Never infer finding disposition, decision resolution, plan approval, completion,
        or closure. Run authority-changing commands only after the user explicitly
        authorizes the exact record and rationale.
        """;

    private static string CreateExternalTrackerSynchronizationSpecification(string repositoryId) => $"""
        ---
        title: "External Tracker Synchronization"
        type: specification
        status: Draft
        owner: Repository maintainer
        review_cadence: on tracker synchronization change
        cis:
          stable_id: {repositoryId}:spec:external-tracker-synchronization
        ---

        # External Tracker Synchronization

        ## Authority and scope

        Canonical task Markdown remains authoritative. GitHub Issues, Jira work items,
        and extension trackers are projections for coordination. Remote edits never
        approve a plan or design, accept risk or deferral, prove completion, close the
        CIS change, or override a canonical task automatically.

        ## Identity and durable state

        Each projection embeds repository, change, and task identity and links its
        canonical task path. The task's `External issue links` table preserves provider,
        remote ID/URL, canonical and remote digests, last synchronization, and link state.
        Disposable three-way state and conflicts live under `.cis/local/trackers/`.

        ## Reconciliation rules

        - No mapping produces a proposed remote create.
        - Canonical-only drift produces a proposed remote update.
        - Remote-only or concurrent drift produces an open conflict; pull never rewrites canonical task fields.
        - Remote deletion produces `remote-deleted` and never causes automatic recreation or canonical deletion.
        - Retry may repeat reads and idempotent updates; create retry requires recovered remote identity or provider idempotency.
        - Rate limiting, authentication failure, offline state, or missing providers return an unavailable result without discarding the last healthy mapping.

        ## Provider and credential boundary

        Loaded assemblies register provider kinds through `ICisTrackerProvider`.
        Repository profiles select provider instances and targets. GitHub and Jira rows
        are disabled by default. Credentials come from environment variables or a
        provider credential store, use least privilege, are never logged, and are never
        written to Markdown or `.cis/local/`.

        ## Commands

        `cis tracker plan` is read-only. `push` creates/updates permitted remote mirrors,
        `pull` persists drift conflicts without changing canonical tasks, `status` reports
        provider/mapping/conflict state, and `resolve` records explicit human CIS, remote-
        divergence, or unlink decisions. Push never overwrites an open conflict.

        ## Acceptance

        Synchronization is idempotent, stable identities survive local-state deletion,
        conflicts preserve both digests, external closure grants no CIS authority, and
        every human resolution records reviewer, timestamp, rationale, and disposition.
        """;

    private static string CreateApiDesignAndGovernanceSpecification(string repositoryId) => $"""
        ---
        title: "{repositoryId} API Design and Governance"
        type: specification
        status: Draft
        owner: Repository maintainer
        review_cadence: on API policy or contract change
        cis:
          stable_id: {repositoryId}:spec:api-design-and-governance
        ---

        # {repositoryId} API Design and Governance

        ## Purpose and provenance

        This repository-owned standard adapts the reusable API controller, contract
        dictionary, Problem Details, and OpenAPI drift practices proven in PARR. It
        deliberately omits PARR-specific host names, identity providers, issuer values,
        and mandatory proprietary headers. Repository decisions fill those values.

        `MUST`, `MUST NOT`, `SHOULD`, and `MAY` are normative. Rule IDs are stable and
        should be cited by tasks, exceptions, tests, and review findings.

        ## Exposure and trusted scope

        - `API-EXP-01`: Every operation MUST declare one exposure class: `public`
          (unauthenticated application data), `identity-protocol` (no-store credential
          establishment), `customer`, `backoffice`, `internal/service`, `webhook`, or `probe`.
        - `API-EXP-02`: Exposure, authentication, authorization, and scope source MUST be
          visible in code, the API dictionary, and OpenAPI where the operation is published.
        - `API-SCOPE-01`: Customer or tenant scope MUST come from trusted server-side identity
          context. A caller MUST NOT select trusted scope through route, query, header, or body.
        - `API-SCOPE-02`: Collections MUST be filtered by trusted scope and each resource access
          MUST enforce object-level authorization. Property-level exposure MUST use explicit DTOs.
        - `API-PUB-01`: Every `public` operation MUST also satisfy the Public Endpoint Caching
          Policy, including the prohibition on endpoint-to-database access on cache miss.
        - `API-PUB-02`: Anonymous exposure MUST be explicit, approved, rate/size limited,
          monitored, and tested for abuse. It MUST NOT arise from an omitted authorization rule.
        - `API-IDP-01`: `identity-protocol` operations MUST be `no-store`, use a named
          abuse-control policy, and keep persistence behind an application-service or SDK
          boundary. This narrow class MUST NOT be used to bypass `API-PUB-01` for application data.

        ## Routes, resources, and surface area

        - `API-ROUTE-01`: Externally consumed routes SHOULD use a module, major-version,
          and resource path structure such as `/sales/v1/orders`. Approved probes, callbacks, and provider-constrained
          webhooks MAY use another stable scheme when their versioning contract is recorded.
        - `API-ROUTE-02`: Routes MUST use resource nouns and HTTP method semantics rather than
          RPC verbs where a resource design is practical. Nesting SHOULD remain shallow.
        - `API-ROUTE-03`: Module and major version MUST agree across routing, grouping, the API
          dictionary, supported-version registry, and OpenAPI.
        - `API-SURF-01`: A new operation MUST first assess whether an existing resource can be
          extended with compatible filters, projection, or representation. Consolidation MUST NOT
          blur authorization boundaries, side effects, or distinct resources.
        - `API-ID-01`: Sensitive or customer-owned resources MUST NOT expose sequential internal
          primary keys. Opaque identifiers do not replace authorization or rate limiting.

        ## Transport boundary and contracts

        - `API-THIN-01`: Controllers and route handlers MUST remain transport adapters: bind and
          validate, obtain trusted scope, enforce policy, invoke one use case, map the response,
          and emit safe telemetry. They MUST NOT contain persistence, domain decisions, workflow
          orchestration, ad-hoc authorization, or raw exception handling.
        - `API-DTO-01`: Requests and responses MUST use explicit boundary contracts. Domain and
          persistence models MUST NOT be bound directly, and writable fields MUST be allow-listed.
        - `API-OAS-01`: Every externally reachable operation MUST be represented in the governed
          OpenAPI document with purpose, schemas, statuses, authentication/authorization,
          pagination/filter/sort, scope source, idempotency, concurrency, rate limits, and errors.
        - `API-DICT-01`: OpenAPI does not replace the row-level API dictionary. Adding, removing,
          renaming, re-versioning, re-hosting, or materially changing an operation MUST update the
          implementation, API dictionary, affected permissions/problems, OpenAPI, and tests in the
          same change.

        ## Authentication, authorization, and abuse resistance

        - `API-AUTHN-01`: Operations MUST be authenticated unless their explicit exposure class
          permits anonymous access. Basic authentication, query-string credentials, unsigned
          tokens, and non-expiring bearer tokens MUST NOT be introduced.
        - `API-AUTHZ-01`: Protected operations MUST use explicit policy/capability authorization.
          Inline raw-role checks MUST NOT replace the repository policy model.
        - `API-AUTHZ-02`: Function-, object-, and property-level authorization MUST be tested on
          positive and negative paths. Sensitive forbidden and nonexistent resources SHOULD be
          indistinguishable in body, headers, and practical timing where existence is sensitive.
        - `API-RATE-01`: Every operation MUST name an explicit rate-limit policy. A `429` response
          MUST have documented retry behavior, including `Retry-After` where applicable.

        ## Errors, telemetry, and protocol controls

        - `API-ERR-01`: Expected failures MUST map to stable 4xx contracts; unexpected failures
          MUST use a consistent Problem Details representation and MUST NOT expose stack traces,
          SQL, storage paths, topology, secrets, or sensitive existence/state.
        - `API-ERR-02`: Problem identities MUST be stable and catalogued. Logs MUST retain a safe
          correlation value while responses contain only approved diagnostic detail.
        - `API-LOG-01`: Logs MUST NOT contain credentials, authorization headers, connection
          strings, cryptographic material, raw evidence, or unjustified personal data. Body logging
          requires an explicit, time-bounded, audited approval.
        - `API-HDR-01`: External transport MUST use TLS and appropriate security headers. CORS
          MUST use explicit origins; wildcard origins MUST NOT be combined with credentials.
          Principal-scoped responses SHOULD normally use `Cache-Control: no-store`.

        ## Reliability and collection behavior

        - `API-IDEM-01`: Retry-sensitive creates and commands MUST define an idempotency or
          deduplication strategy that prevents duplicate writes and side effects.
        - `API-CONC-01`: Resources with realistic concurrent updates MUST use ETag/`If-Match`, a
          version token, or an equivalent lost-update control.
        - `API-EXEC-01`: Cancellation MUST flow downstream. Database, cache, broker, HTTP, and
          overall execution MUST have bounded timeouts. Long-running work SHOULD return `202` with
          a status locator and execute outside the request.
        - `API-COLL-01`: Collections MUST document pagination, supported filters and sorts, and a
          server-enforced maximum page size. Pagination MUST be stable and filters MUST NOT widen
          the caller's trusted scope.

        ## Files, probes, and webhooks

        - `API-FILE-01`: Uploads MUST constrain type and size and apply required scanning. Downloads
          MUST authorize before resolving storage and MUST NOT expose internal paths or over-broad URLs.
        - `API-PROBE-01`: Health/readiness responses MUST be minimal and MUST NOT reveal secrets,
          connections, dependency versions, or internal diagnostics. Public probes require abuse controls.
        - `API-WH-01`: Incoming webhooks MUST verify signatures, resist replay, and be idempotent.
          Outgoing webhooks MUST be scoped, signed with a timestamp, bounded in retry, and protected
          from server-side request forgery.

        ## Compatibility and lifecycle

        - `API-VER-01`: Breaking changes require a new major version and an explicit consumer
          migration. Supported major versions MUST remain independently routable, documented, and tested.
        - `API-VER-02`: The repository MUST define its compatibility mode and deprecation window.
          The default is forward-transitive: compare the current contract with every supported
          baseline in the same major version. Public consumers retain at least a six-month migration
          window; adopting a weaker mode or shorter window requires a reviewed decision.
        - `API-VER-03`: Deprecated operations MUST be marked in OpenAPI and SHOULD emit standard
          deprecation/sunset metadata. Retirement requires expiration of the approved window,
          migration guidance, and disposition of known consumers.

        ## Governed inventories and completion gate

        The API dictionary owns operation inventory and endpoint permission usage. The permissions
        dictionary owns permission meaning and role/capability mappings. The Problem Details catalogue
        owns stable error semantics. The data dictionary owns stored schema. None replaces the others.

        An operation cannot become `Verified` while required governance fields are `unknown` or `TBD`.
        The normalized operation model records module, host/deployment, exposure, consumers,
        contracts, permission/authentication, trusted scope source, OpenAPI identity, rate limit,
        idempotency/concurrency, collection semantics, errors, caching, data-access path, lifecycle,
        source locations, and known tests beneath `.cis/local/api/`. Markdown remains canonical.
        Completion requires deterministic OpenAPI export, comparison with the governed baseline,
        dictionary drift validation, authorization and abuse tests, compatibility evidence, and
        explicit disposition of every known consumer. Production OpenAPI exposure MUST be explicitly
        approved and SHOULD default to disabled or restricted.
        """;

    private static string CreatePublicEndpointCachingPolicySpecification(string repositoryId) => $"""
        ---
        title: "{repositoryId} Public Endpoint Caching Policy"
        type: repository-specification
        status: Draft
        scope: Repository
        owner: Repository maintainer
        review_cadence: on public endpoint, caching, or data-access change
        cis:
          stable_id: {repositoryId}:spec:public-endpoint-caching-policy
        ---

        # Public Endpoint Caching Policy

        ## Rule

        Every unauthenticated HTTP/API endpoint must use a governed server-side or
        edge cache. Its route, controller, endpoint handler, or transport adapter must
        not access a database, database client/context, query provider, or repository
        directly, including on cache miss.

        Cache population is delegated behind an application/query abstraction. The
        endpoint response path always traverses the cache abstraction. A documented
        exception requires an explicit human-approved architecture decision; omission
        or convenience is not an exception.

        ## Required contract

        Each public endpoint records its cache key and representation dimensions,
        TTL/freshness/staleness, invalidation/refresh owner, HTTP cache headers,
        stampede protection, miss and dependency-failure behavior, payload sensitivity,
        and the internal cache-population boundary.

        ## Required evidence

        Tests prove cache hits, misses, refresh/invalidation, concurrency/stampede and
        stale/failure behavior, safe key variation, payload safety, and architecture
        enforcement that rejects a direct database/repository dependency from the
        unauthenticated endpoint boundary. Operations provide redacted hit/miss,
        refresh, stale, eviction, latency, and failure telemetry.
        """;

    private static string CreateGraphContextSkill() => """
        ---
        name: cis-graph-context
        description: Build, validate, search, traverse, trace, and package the CIS repository graph. Use when locating components, contracts, references, symbols, callers, tests, dependencies, decisions, or bounded evidence before impact analysis, planning, implementation, or verification.
        ---

        # CIS Graph and Context

        1. Resolve the repository from `.cis/repository.yml`.
        2. Run `cis graph validate --format agent`. If the graph is missing or stale, run `cis graph build --format agent`, then validate again.
        3. Locate exact roots with `cis graph find` or `cis context search`; disambiguate with full IDs and kinds.
        4. Use `cis graph related` or `cis graph trace` for evidence paths.
        5. Use focused `cis context contract|symbol|references|callers|tests-for` queries or `cis context pack` for bounded source evidence.
        6. Treat C# invocation arguments as privacy-safe shapes, and `dataflow` as intraprocedural call ordering only. Inspect canonical source before making semantic or runtime claims.
        7. Report graph schema/build, freshness, compiler capabilities, diagnostics, truncation, roots, and omitted evidence.

        Do not edit `.cis/local/`, imply complete semantic coverage, include proposed edges silently, or replace canonical Markdown with graph output.
        """;

    private static string CreateFileIndexSkill() => """
        ---
        name: cis-file-index
        description: Build, refresh, and query low-token per-file CIS routing cards. Use before broad repository searches, when locating likely implementation or documentation files, or when repository changes may have made cached routing summaries stale.
        ---

        # CIS File Index

        1. Run `cis index status --format agent`.
        2. If the index is missing or stale, run `cis index build --limit 100 --format agent`; repeat bounded batches until pending coverage is zero.
        3. Use `cis index find --text <task terms> --format agent` before broad file searches.
        4. Open the reported source files, not only their cards, before making factual claims or changes.
        5. Rebuild changed cards after implementation and report provider, model, truncation, pending files, and errors.

        Cards under `.cis/local/index-cards/` are disposable, non-authoritative routing aids. Auto-selection may use local Ollama only. Never use `--allow-remote` unless the user explicitly authorizes transmitting the selected repository content, and never submit sensitive files.
        """;

    private static string CreateFeedbackLoopSkill() => """
        ---
        name: cis-feedback-loop
        description: Review automatic local CIS tool-usage evidence, possible token savings, repeated failures, and high-output commands. Use after a workflow, when commands repeatedly fail, or when deciding what CIS capability to optimize next.
        ---

        # CIS Feedback Loop

        1. Run `cis feedback summary --format agent` for aggregate outcomes, duration, output estimates, and possible savings.
        2. Run `cis feedback opportunities --format agent` to locate repeated failures and commands that need compact output or a defensible estimator.
        3. Use `cis feedback usage --limit 20 --format agent` only when recent per-invocation evidence is needed.
        4. If a repository-aware command repeatedly fails, run `cis repo doctor` before retrying it.
        5. Treat estimates as directional evidence. Report the basis and confidence; never turn an unestimated command into a savings claim.

        The ledger under `.cis/local/feedback/` is disposable and non-authoritative. CIS stores command paths, option names, counts, timing, and outcomes—not option values or command output. Do not copy the ledger into canonical documentation unless a reviewed summary is required.
        """;

    private static string CreateDeliveryExecutionSkill(string documentationRoot) => $$"""
    ---
    name: cis-delivery-execution
    description: Execute governed CIS delivery through AI routes, deterministic templates, resumable workflows, portable agent envelopes, verification, diagnostics, and reviewed learning.
    ---

    # CIS delivery execution

    1. Read `{{documentationRoot}}/references/ai-routing-profile.md`, the applicable change plan, task, repository delivery policy, and command manual.
    2. Prefer deterministic `cis generate` and `cis workflow` commands before model generation.
    3. Use `cis ai evaluate` through a reviewed capability route. Never pass `--allow-remote` without explicit authorization for the exact content.
    4. Use `cis agent prepare` for one ready task. An imported agent result is evidence, not completion or approval.
    5. Record exact checks with `cis verify evidence`; run diff, compare, and validate before requesting human acceptance.
    6. Read only enabled, non-sensitive diagnostic sources. Test harnesses write bounded, redacted evidence beneath `.cis/local/testing/diagnostics/<suite-id>/`; inspect the exact workflow attempt log before a diagnostic rerun.
    7. Reconcile test results so log and diagnostic hashes bind to the run, attempt, suite, component, revision, and executed `TC-*` identities.
    8. Run `cis learn collect` and `cis learn propose`; only a human may review, and only approved proposals may be promoted to canonical history.
    9. If repository initialization or command discovery fails, run `cis repo doctor` and follow evidence-backed fixes.
    """;

    private static string CreateDeliveryExecutionInstruction(string documentationRoot) => $$"""
    ---
    applyTo: "**"
    ---

    # CIS delivery execution authority

    - Canonical Markdown under `{{documentationRoot}}` remains authoritative; `.cis/local/` state is disposable.
    - Prefer deterministic generation and workflows. Model output is never approval, acceptance, or completion evidence.
    - Remote model use requires a reviewed route and explicit `--allow-remote` authorization for the exact content.
    - Agent envelopes bind to a task digest. Reject stale results and never infer task completion from an import.
    - Workflow definitions are repository-owned; CIS executes argument lists without a shell and resumes only an unchanged definition.
    - Verification acceptance requires an explicit human reviewer and rationale after deterministic validation passes.
    - Diagnostics may read only enabled, repository-relative, non-sensitive evidence sources.
    - Preserve the first failing workflow attempt. Inspect its live bounded log before a diagnostic rerun, and place sanitized suite evidence beneath `.cis/local/testing/diagnostics/<suite-id>/` for reconciliation.
    - Learning proposals cannot self-apply. Human review precedes promotion into canonical learning history.
    - After command failure, inspect the result and run `cis repo doctor` when repository health or configuration may be involved.
    """;

    private static string CreateFeedbackLoopInstruction() => """
        ---
        applyTo: "**"
        ---

        # CIS feedback-loop guidance

        - CIS automatically records every CLI invocation that can be associated with an initialized repository or workspace.
        - Treat `.cis/local/feedback/tool-usage.jsonl` as disposable local evidence, not canonical documentation.
        - Use `cis feedback summary` and `cis feedback opportunities`; do not parse or edit the ledger directly in normal workflows.
        - Token savings are estimates. Preserve the recorded basis and confidence, and report zero when no defensible counterfactual exists.
        - Command output and option values must not be persisted. Never add secrets, prompts, source content, or credentials to the ledger schema.
        - After repeated failures, inspect recent evidence and run `cis repo doctor` before retrying blindly.
        """;

    private static string CreateChangeDossierSkill() => """
        ---
        name: cis-change-dossier
        description: Create, inspect, and manage CIS repository-owned change dossiers against an exact Git or graph baseline. Use when beginning a reviewed engineering change, checking its lifecycle, or closing it after verified completion.
        ---

        # CIS Change Dossier

        1. Confirm the outcome and exact graph roots.
        2. Run `cis change create --title <title> --outcome <outcome> --root <id>#<kind> --format agent`.
        3. Use `cis change list`, `cis change show <id>`, and `cis change status <id>` to recover durable state.
        4. If genuine source or governance input changed before any impact finding or generated task exists, run `cis change rebaseline <id> --actor <identity> --reason <rationale>`; inspect the appended event. Managed dossier edits alone never require rebaseline.
        5. Keep proposal outcome criteria human-reviewable before plan approval.
        6. Run `cis change close <id>` only after the user explicitly authorizes closure and completion evidence exists.

        Do not invent an outcome, create duplicate dossiers, rebaseline reviewed impact or task evidence, change baselines silently, hand-edit managed tables, or treat closure as verification.
        """;

    private static string CreateImpactReviewSkill() => """
        ---
        name: cis-impact-review
        description: Discover and review deterministic CIS impact findings for a change dossier. Use when assessing affected documentation, contracts, dependencies, implementation, delivery, tests, risks, or planning readiness before implementation.
        ---

        # CIS Impact Review

        1. Confirm the change baseline and roots with `cis change show <change-id>`.
        2. Run `cis impact analyse <change-id> --format agent`; broaden explicit roots, depth, or limit when evidence is truncated.
        3. Run `cis impact findings <change-id>` and present evidence, confidence, uncertainty, and categories.
        4. Ask the user to disposition unresolved findings.
        5. Run `cis impact accept|reject|defer <change-id> <finding-id> --reason <reason>` only for the exact user-authorized finding and rationale.
        6. Run `cis impact completeness <change-id>` and report every remaining gap.

        Never disposition findings autonomously, hide deferred scope, accept truncated analysis, or analyse against a changed baseline.
        """;

    private static string CreateDecisionReviewSkill() => """
        ---
        name: cis-decision-review
        description: Capture and review CIS change-local decisions, options, evidence, blocking gates, human resolutions, deferrals, and durable ADR promotion. Use when impact analysis or planning exposes architecture, scope, contract, data, security, operations, delivery, or implementation choices.
        ---

        # CIS Decision Review

        1. Run `cis decision list <change-id>` before adding a question.
        2. Create a grounded question with `cis decision create <change-id> --question <text> --category <category> --option <a> --option <b> --evidence <evidence>`; add `--advisory` only when it cannot block safe delivery.
        3. Present options, trade-offs, evidence, and gate effect to the user.
        4. Run `cis decision resolve <change-id> <decision-id> --option <recorded-option> --rationale <rationale>` or `cis decision defer ... --reason ...` only after explicit user authorization.
        5. Run `cis decision promote <change-id> <decision-id>` only when the user confirms the resolved choice is durable architecture.

        Never select an option, defer a blocking choice, change a resolved decision, or promote an ADR autonomously.
        """;

    private static string CreateBoundedPlanningSkill() => """
        ---
        name: cis-bounded-planning
        description: Build, import feature specifications into, inspect, and validate dependency-aware CIS work plans from accepted impact findings. Use when impact review is complete and work must be bounded by complexity, objectives, dependencies, acceptance criteria, validation, and approval gates before implementation.
        ---

        # CIS Bounded Planning

        1. Run `cis impact analyse <change-id>` against the approved feature scope.
        2. For a current explicitly approved CIS feature, run `cis plan derive <change-id> --file <repo-relative-spec.md>`. This atomically carries the existing authority through eligible deterministic impacts, any untouched proposal-acceptance placeholder, and the exact validated plan; do not request separate impact or plan approvals when it succeeds. Existing human-managed proposal criteria are preserved.
        3. If derivation stops, surface only the reported low-confidence, deferred, truncated, conflicting, stale, or invalid exception. Use explicit `cis impact accept|reject|defer`, `cis plan import-spec`, and `cis plan approve` only as the fallback after the human resolves that exception. When no governed feature exists, use `cis plan build <change-id>`.
        4. Run `cis plan show <change-id>` and open every linked `agent-tasks/WORK-NNN.md`; the plan table is only the dependency ledger, not the complete task contract. After re-import, inspect `agent-tasks/retired/`, archived catalog routes, and `plan-task-retired|reactivated` events; preserved evidence never grants renewed completion.
        5. Verify each task has bounded required changes, exclusions, accepted graph evidence, dependencies and gates, acceptance checklists, targeted validation, a completion-evidence table, and explicit deferral/residual-risk handling. Review generated `test-cases.md` and `test-cases.csv`; every feature requirement must have one stable manual case and the Markdown-recorded CSV hash must match. During implementation, place each exact `TC-*` ID in its automated test name, framework metadata, or adjacent traceability annotation, then rerun the unchanged feature import to refresh `repository::path:line` mappings. `Pending` is allowed while planning, but final verification requires every case to be `Automated`. Record executions in `verification.md`, not in the generated catalogue.
        6. Classify every UI-bearing requirement, screen, wireframe task, design task, and frontend implementation task as exactly `public`, `customer`, or `backoffice`. Generate a matched wireframe -> design -> frontend chain for each affected type.
        7. For every unauthenticated endpoint, require `PUBLIC-ENDPOINT-CACHE` obligations in Security, API contract, Backend, Observability, Verification, and Independent assurance: every response traverses a governed cache and the public route/controller/handler never directly accesses a database or repository, including on cache miss.
        8. For UI-bearing scope, require `coordination -> validated wireframes -> design authority -> every downstream task`. Treat `wireframes.md` and `design.md` as canonical review records. By default `cis design approve` approves the exact wireframe digest and rendered design pack together; use standalone wireframe approval only when an early behavior-only checkpoint is intentionally requested. When design enters `ReadyForReview`, stop all non-review work until explicit approval, or use `cis design reconcile` only when a current approved feature and Approved plan authorize the refresh and the regenerated PNG manifest exactly matches the earlier approved manifest.
        9. Require applicable documentation, security, data, database-migration, backfill, API, backend, frontend, integration, infrastructure, observability, lifecycle, rollout, verification, assurance, and final-sweep workstreams. Product-specific search/projection belongs to an extension provider, not CIS core.
        10. Confirm every task is low, medium, or high complexity and every high item is a decomposed parent with at least two bounded children.
        11. Run `cis plan validate <change-id>` and resolve every error. Use `cis plan task transition <change-id> <task-id> --status <state> --actor <identity> --reason <rationale>` for lifecycle changes. Completion requires resolved evidence and snapshots sanitized tool-usage counts/savings into the task and `verification.md`.
        12. If planning reports an extension capability conflict, run `cis plan capability status`, present all candidates, and use `cis plan capability select` only after explicit human selection. Selection never migrates existing tasks; use `cis plan task migrate-type` with human reviewer and rationale for each compatible instance.
        13. Run `cis plan approve <change-id> --reviewer <identity> --reason <rationale>` only when new plan authority is required and the user explicitly approves the exact validated issue pack. Never duplicate a successful `cis plan derive` approval.

        Never forge or infer feature authority, approve a plan autonomously, hide advisory warnings, remove accepted scope to make validation pass, or treat a draft plan as implementation authority.
        """;

    private static string CreateExternalTrackerSyncSkill(string documentationRoot) => $$"""
        ---
        name: cis-external-tracker-sync
        description: Preview, push, pull, inspect, and reconcile GitHub Issues, Jira, or extension tracker mirrors for canonical CIS task documents without transferring approval or completion authority.
        ---

        # CIS External Tracker Synchronization

        1. Read `{{documentationRoot}}/specs/external-tracker-synchronization-spec.md`, the external tracker profile, repository delivery policy, plan, and affected task documents.
        2. Run `cis tracker status`, then `cis tracker plan <change-id> --provider <key>` before any remote mutation.
        3. Confirm the provider is enabled, its loaded assembly is available, the target is exact, and the credential source is least-privilege. Never print or persist credentials.
        4. Run `cis tracker push` only when the user or repository policy authorizes the exact remote issue create/update scope.
        5. Use `cis tracker pull` to record drift. It must not rewrite canonical task fields or lifecycle state.
        6. Surface every conflict. Use `cis tracker resolve` only with an explicit human reviewer, choice, and rationale.
        7. Re-run plan/status and preserve remote identity links in each canonical task.

        Remote closure, labels, assignees, milestones, or workflow transitions never approve CIS plans, designs, deferrals, risks, task completion, or final acceptance.
        """;

    private static string CreateExternalTrackerInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/changes/**/agent-tasks/*.md"
        ---

        # CIS external tracker guidance

        - Canonical task Markdown is authoritative; external issues are mirrors.
        - Read `{{documentationRoot}}/references/external-tracker-profile.md` and the synchronization specification before using tracker commands.
        - Run `cis tracker plan` before `push`. Do not overwrite open conflicts or recreate a deleted remote issue automatically.
        - `pull` records remote drift; it never changes canonical approval, lifecycle, evidence, deferral, or acceptance fields.
        - Human reviewer identity and rationale are mandatory for `cis tracker resolve`.
        - Credentials come only from the reviewed provider credential source and must not appear in Markdown, `.cis/local/`, logs, or command output.
        """;

    private static string CreateDesignReviewSkill() => """
        ---
        name: cis-design-review
        description: Define textual screen behavior, reuse application-shell and component templates, generate self-contained Sharp/SVG renderers and PNG packs, and preserve human design approval as a global delivery gate.
        ---

        # CIS Design Review

        1. Read `.cis/repository.yml`, then read `<documentation-root>/references/ui-framework-profile.md` when present and verify its evidence against manifests, imports, shared components, shell code, and theme assets. Preserve an existing UI framework; use the classification-bound default only when no framework is in use.
        2. Classify every screen as exactly `public`, `customer`, or `backoffice`, then complete `wireframes.md` with stable routes, states, actions, side effects, destination paths, and negative behavior.
        3. Run `cis design wireframe-validate <change-id>` and resolve every screen, classification, state, action, destination-path, coverage, or placeholder error before review.
        4. A separate `cis design wireframe-approve` is optional and is used only when the team explicitly wants an early behavior-only checkpoint.
        5. Run `cis design templates --format agent` before writing renderer helpers. Select `shell.standard-app` when persistent navigation is justified or `shell.minimal-app` for a simple single-surface product, then reuse every applicable governed component template—including buttons, fields, selects/dropdowns, choice controls, tabs/navigation, dialogs/alerts, cards, forms, tables, and states—to reduce repeated code and token usage.
        6. Before scaffolding, run `cis design reuse` for each exact earlier approved source screen that remains compatible with a target wireframe screen. Record why it is unchanged; do not infer reuse from similarity alone. CIS must verify source authority and hashes.
        7. Run `cis design scaffold <change-id> --feature <slug> --component <template-id> --format agent`; customize only uncovered feature content while preserving the shared shell and component behavior. Reused target screens must be absent from the renderer.
        8. Run `cis design validate <change-id>` and resolve guideline, shell, component, provenance, reuse-drift, coverage, or renderer errors.
        9. Run `cis design render <change-id>`. Successful rendering enters `PausedForReview`; stop all non-review work immediately.
        10. Present changed PNGs plus the declared reuse mappings. Only a human may run `cis design approve|reject` with reviewer identity and rationale. Approval records the exact validated wireframe digest, renderer, and PNG manifest together; that manifest combines reused and newly rendered evidence. For a provenance-only refresh after an approved feature change, `cis design reconcile` may carry existing authority forward only when the Approved plan pins the current feature and the combined manifest is unchanged.
        11. On rejection, revise only wireframes/design assets and resubmit; all other delivery work remains paused. Reused source PNGs are never deleted by target rejection.
        12. On approval, preserve exact source-approval, renderer, and combined PNG-manifest hashes before downstream work resumes.

        Never introduce a second UI framework merely to match a default, install framework packages during repository initialization, hand-edit generated PNGs, bypass the application shell, redraw a common control where a governed template applies, use network assets, continue implementation during review, infer compatibility, or infer approval.
        """;

    private static string CreateStandardsGovernanceSkill(string documentationRoot) => $$"""
        ---
        name: cis-standards-governance
        description: Resolve, import, audit, author, or revise repository standards; validate stable normative rules and conformance mappings; and review enforcement gaps before governed implementation or completion.
        ---

        # CIS Standards Governance

        1. Read `.cis/repository.yml`, `{{documentationRoot}}/standards/`, and `{{documentationRoot}}/references/standards-conformance-matrix.md`.
        2. Treat classification-selected PARR-derived standards as repository-owned canonical starters, not immutable upstream policy. Verify their provenance and applicability, then strengthen or supersede them through reviewed changes when repository architecture requires it.
        3. Before implementation, run `cis standards applicable --target <target> [--stack <stack>] --format agent` for every affected surface. Read the returned canonical standards; do not rely on routing output alone.
        4. When authoring a standard, start from `{{documentationRoot}}/templates/standard-template.md`. Keep it below `standards/`, register it as canonical `type: standard`, and assign an immutable document ID plus repository-unique stable rule IDs.
        5. Express mandatory rules with `MUST` or `MUST NOT`, recommendations with `SHOULD`, and options with `MAY`. Make each rule independently testable.
        6. Add every active rule to the conformance matrix. Prefer deterministic checks or architecture tests; use manual review where semantic judgment is necessary.
        7. Treat `advisory-model` results as candidates only. Never report them as confirmed breaches or proof of compliance without deterministic evidence or human acceptance.
        8. Record exceptions with the rule ID, human approver, rationale, bounded scope, expiry or review condition, and compensating controls. Never approve an exception yourself.
        9. Run `cis standards validate --strict`, `cis standards conformance --gaps-only`, and `cis docs validate --strict` after changes. Rebuild the graph so standards remain queryable.
        10. Before importing, run `cis standards import --source <path-or-url> --dry-run`. Use `--fix` only for staged structural repair and stable IDs on existing normative statements; it never invents semantics. Review the file, catalog, and default manual-review mappings, then rerun with `--yes` only when the canonical admission is intended.
        11. Run `cis standards audit` after imports or material standard changes. Audit is local-first and may use a configured remote provider when local generation is unavailable; use `--no-llm` for deterministic-only review. Treat all model findings as advisory.
        12. Use `cis standards audit --fix` only when reversible quarantine is intended. Inspect the preserved historical catalog and matrix records; never infer that quarantine resolves the underlying governance decision.
        13. Run `cis standards patterns --strict` before using repository or extension inference patterns. Duplicate IDs are conflicts; never select a pattern by provider load order.
        14. To discover observed conventions, build a fresh compiler graph and run `cis standards infer`. Review matches and counterexamples in `.cis/local/standards/inference/`; inferred candidates are not standards and cannot grant normative authority.

        Keep policies, standards, specifications, and procedures distinct. A policy defines authority or required outcomes; a standard defines the expected way; a specification defines a precise contract; a procedure defines ordered steps.
        """;

    private static string CreateDocumentationSkill(string documentationRoot) => $$"""
        ---
        name: cis-documentation
        description: Maintain CIS specifications, references, templates, and catalog metadata when repository behavior or documentation authority changes.
        ---

        # CIS Documentation Maintenance

        ## Purpose

        Keep repository-owned documentation navigable, authoritative, and synchronized with behavior.

        ## When to Use

        Use when adding or changing a specification, reference, ADR, template, guide, policy, standard, procedure, or implementation contract.

        ## Inputs

        Read `.cis/repository.yml`, `{{documentationRoot}}/README.md`, `{{documentationRoot}}/catalog.yml`, and related canonical documents.

        ## Workflow

        1. Classify the document and confirm its scope, authority, owner, lifecycle, and source-of-truth relationship.
        2. Preserve stable IDs and update the nearest navigation and catalog entry.
        3. Update implementation and documentation together when behavior changes.
        4. Distinguish discovered, proposed, verified, deprecated, and withdrawn facts.
        5. Run `cis docs validate --strict` before completion.

        ## Output Expectations

        Leave valid front matter, catalog registration, navigation, cross-links, and explicit review status.

        ## Guardrails

        Do not create duplicate authorities, treat generated summaries as canonical, or silently replace human-authored content.

        ## Related Files

        See `{{documentationRoot}}/catalog.yml` and `.github/skills/cis-add-documentation/SKILL.md`.
        """;

    private static string CreateRepositoryBootstrapSkill() => """
        ---
        name: cis-repository-bootstrap
        description: Initialize or reconcile CIS in a target repository, respect confirmation gates, and run repository doctor after initialization errors or collisions.
        ---

        # CIS Repository Bootstrap

        ## Purpose

        Establish or reconcile CIS repository state without guessing the documentation root or bypassing human review.

        ## When to Use

        Use when onboarding a repository, adding projects to an initialized repository, upgrading CIS starters, or recovering from a failed `cis repo init` run.

        ## Inputs

        Obtain the target repository path and an explicit repository-relative documentation root chosen by the maintainer.

        ## Workflow

        1. Run `cis repo init --repo <repository> --root <documentation-root> --dry-run --format agent`.
        2. Review classification evidence, planned creates and updates, warnings, and collisions.
        3. For obsolete managed artifacts, keep the default retention unless the maintainer explicitly requests recoverable cleanup. Preview that cleanup with `--quarantine-obsolete --dry-run`; only unchanged CIS-managed files are eligible, while edited or human-owned files remain in place.
        4. Treat exit code `3` as a confirmation gate, not a failure. After maintainer review and authorization, rerun with `--yes`; include `--quarantine-obsolete` only when its moves were also reviewed.
        5. If init returns exit code `2`, exit code `4`, an error, or a collision, run `cis repo doctor --repo <repository> --root <documentation-root> --format agent` immediately.
        6. Use doctor evidence and suggested fixes to explain the blocker. Apply no suggested fix without the required human review.
        7. Rerun init after the blocker is resolved and confirm that the final plan is applied or unchanged.

        ## Output Expectations

        Report the chosen root, init status and exit code, doctor findings when init failed, applied changes, unresolved collisions, and the exact next action.

        ## Guardrails

        Do not choose a documentation root silently, use `--yes` before reviewing a non-empty root, quarantine edited or human-owned files, overwrite quarantine destinations or collisions, execute target-repository assemblies, or treat doctor suggestions as automatic authorization.

        ## Related Files

        Read `.cis/repository.yml` when it exists and use the `cis repo init` and `cis repo doctor` command manuals from the CIS distribution.
        """;

    private static string CreateSkillGovernanceSkill() => """
        ---
        name: cis-skill-governance
        description: Inventory, validate, repair, import, and audit portable repository skills without silently replacing or disabling human-managed guidance.
        ---

        # CIS Skill Governance

        ## Purpose

        Keep the repository's agent skills valid, discoverable, non-duplicative, and free of unresolved instruction conflicts.

        ## When to Use

        Use after repository initialization, classification changes, skill imports, or edits beneath `.github/skills/`, and when agents receive overlapping instructions.

        ## Workflow

        1. Run `cis skills inventory --repo <repository> --format agent` to establish the installed set.
        2. Run `cis skills validate --repo <repository> --strict --format agent`.
        3. If validation reports only safely repairable missing YAML fields or headings, review and run `cis skills validate --fix --strict`; otherwise correct the source manually.
        4. For preexisting bundles, preview `cis skills import --source <path-or-url> --dry-run`; import with `--yes` only after the plan is reviewed and authorized.
        5. Run `cis skills audit --repo <repository> --format agent`. Audit prefers a local model and falls back to a configured remote provider; use `--no-llm` when skill content must stay local.
        6. Inspect `.cis/local/skills/audit.md` and the named skills before deciding whether to consolidate, specialize, or clarify them. Use `--strict` when unresolved findings must gate the workflow.
        7. Use `cis skills audit --fix` only when quarantine of redundant deterministic duplicate copies and both sides of evidence-backed conflicts is intended. Overlaps remain active.
        8. Re-run validation and audit after restoration or any approved canonical edit.

        ## Guardrails

        Treat model findings as advisory candidates, not approval authority. Running audit without `--no-llm` authorizes bounded candidate content to use a configured remote provider when local generation is unavailable. Only explicit `--fix` authorizes quarantine moves; it never overwrites or deletes bundles. Preserve complete imported bundles and resolve same-name content conflicts manually.

        ## Output Expectations

        Report validation state, applied safe fixes, import decisions, deterministic duplicates, advisory overlaps or conflicts, remote fallback, quarantine moves, omitted unsubstantiated model findings, and the exact human decision still required.

        ## Related Files

        Read the `cis skills inventory`, `validate`, `import`, and `audit` command manuals. Treat `.cis/local/skills/` as disposable derived state.
        """;

    private static string CreateImportRepositoriesSkill() => """
        ---
        name: cis-import-repositories
        description: Import, initialize, register, list, graph, or validate several existing repositories as one CIS workspace. Use for multi-repository onboarding and repeatable workspace reconciliation without copying source repositories.
        ---

        # Import CIS Repositories

        ## Inputs

        Obtain the workspace path, every source repository path, and one explicit repository-relative documentation root approved for the import batch.

        ## Workflow

        1. Run `cis repo import --workspace <workspace> --source <repository>... --root <documentation-root> --dry-run --format agent`.
        2. Review every classification, planned initialization change, warning, collision, and workspace registry entry.
        3. If any repository cannot initialize, run `cis repo doctor` for that repository with the same root and report the evidence before retrying the batch.
        4. After explicit authorization, repeat import with `--yes`; never add `--yes` to the first run.
        5. Run `cis repo list --workspace <workspace> --format agent` and verify all expected repository IDs and paths.
        6. Run `cis graph build --workspace <workspace> --format agent`, followed by `cis graph validate --workspace <workspace> --format agent`.
        7. Use registered repository paths as explicit roots when producing federated context packs. Treat each local graph identity, freshness, diagnostics, and omissions independently.

        ## Guardrails

        Treat import as registration and initialization, not source copying. Do not silently choose a documentation root, import more than 20 repositories in one batch, hand-edit derived `.cis/local/` graph state, invent cross-repository edges, or hide a partial repository result behind an aggregate success claim.
        """;

    private static string CreateGovernBusinessRequirementsSkill() => """
        ---
        name: cis-govern-business-requirements
        description: Discover possible BRDs, domain-equivalent product-design documents such as GDDs, and development feature specifications, create or reconcile the canonical workspace business requirements document, assess and trace source evidence, validate completeness and drift, and present approval readiness. Use for BRD intake, product-design intake, feature-spec absorption, review, currency checks, or workspace business-requirement governance.
        ---

        # Govern Business Requirements

        ## Workflow

        1. Confirm `.cis/workspace.yml` identifies exactly one `authority` repository. If not, dry-run `cis workspace init --root <documentation-root>` and request review before using `--yes`.
        2. Run `cis graph build --workspace <workspace>` and `cis graph validate --workspace <workspace>` before intake.
        3. Run `cis brd discover --workspace <workspace> --format agent`. Treat every found BRD, product-design document such as a GDD, or feature specification as unverified source evidence; absence creates no implied requirements.
        4. Run `cis brd init --workspace <workspace> --title <title>`. Preserve the authority repository's canonical document and catalog entry.
        5. After implementation graph rebuilds, run `cis technical-intent refresh --workspace <workspace> --format agent` before starting the next feature. Do not request renewed BRD, technical-intent, or backlog approval when this safe refresh succeeds. If its BRD stage blocks on new or materially changed source evidence, use `cis brd reconcile --workspace <workspace> --format agent`, review the exact semantic delta, and request only the authority that delta requires.
        6. Review every source row. Set Assessment to `Adopted`, `Reference`, or `Rejected` and record rationale. For an Adopted feature specification, incorporate its business intent into the relevant BRD sections and cite its `BRD-SRC-*` ID in Traceability.
        7. Rebuild workspace graphs after canonical edits, then run `cis brd validate` and `cis brd status`.
        8. Present validation errors, warnings, participant baseline drift, unresolved sources, and approval readiness to the user.
        9. Run `cis brd approve --reviewer <human> --reason <rationale>` only after the user explicitly authorizes that exact approval. Rebuild the authority graph after approval.
        10. After technical intent is Active/current, run `cis brd backlog build`, review one-to-one functional-requirement coverage, repository routing, frontend types, dependencies, and global obligations, then validate and present approval readiness.
        11. Run `cis brd backlog approve --reviewer <human> --reason <rationale>` only with explicit authority. An approved high-level item may then become a feature specification; it is not an implementation task.
        12. Start only a dependency-ready item with `cis brd backlog start --item <HLT-ID>`. Complete the generated Draft specification and run `cis brd feature validate --item <HLT-ID>` until it is Ready for Approval.
        13. Present the exact feature scope, validation result, and approval rationale. Run `cis brd feature approve --item <HLT-ID> --reviewer <human> --reason <rationale>` only with explicit human authority.
        14. After feature approval, rebuild the graph and reconcile the now-eligible feature evidence into the BRD. Use `cis technical-intent refresh`; renew downstream authority only when it reports an actual semantic change.

        ## Guardrails

        Never infer currency from file existence, choose source dispositions, claim semantic absorption without updating BRD content and traceability, invent business requirements, approve on a user's behalf, or describe Review Required, Ready for Approval, or Stale content as Active. Do not edit managed candidate IDs, approval hashes, source hashes, baseline rows, or block markers. CIS may mark an approved BRD, backlog, or feature Stale from content/evidence drift; only explicit human approval may restore Active status. Feature approval accepts scope but never authorizes implementation.
        """;

    private static string CreateGovernTechnicalIntentSkill() => """
        ---
        name: cis-govern-technical-intent
        description: Initialize, draft, validate, review, approve, or recheck the canonical workspace technical intent derived from an Active BRD and exact participant graph baselines. Use after BRD approval and before change-dossier creation or bounded planning.
        ---

        # Govern Technical Intent

        ## Workflow

        1. Confirm `cis brd status --workspace <workspace>` reports Active, valid, and current.
        2. Build and strictly validate the workspace graph.
        3. Run `cis technical-intent init --workspace <workspace> --format agent` to bind the canonical authority document to the active BRD hash and participant graph builds.
        4. Complete architecture boundaries, data and consistency, contracts, security/privacy, operations/recovery, quality evidence, and delivery constraints without broadening the BRD.
        5. Record each bounded unresolved choice in `Open technical decisions` with a stable `TI-DEC-*` ID, required gate, status, and rationale. Agents may propose options but never select or defer them without explicit human authority.
        6. Run `cis technical-intent validate` and `status`. Resolve every placeholder, unresolved decision, stale BRD hash, and participant-baseline drift.
        7. Run `cis technical-intent approve --reviewer <human> --reason <rationale>` only after the user explicitly authorizes that exact approval.
        8. After approved implementation changes participant graph IDs, run `cis technical-intent refresh`. Never request renewed BRD, technical-intent, or backlog approval when all three remain Active/current; review only the stage that blocks on a material semantic change.
        9. Rebuild and strictly validate the workspace graph after approval. Recheck status before `cis change create` and `cis plan build|import-spec|derive`.

        ## Guardrails

        Preserve managed baseline markers and identities. Do not treat Draft, Ready for Approval, or Stale as Active; infer architecture decisions; approve on a user's behalf; edit approval hashes; or bypass the readiness gate. Pure managed-baseline drift is reconciled by `cis technical-intent refresh`; material business or technical drift requires renewed human review.
        """;

    private static string CreateVerificationSkill() => """
        ---
        name: cis-verification
        description: Plan and execute proportionate deterministic verification, preserve evidence, and report limitations without overstating completion.
        ---

        # CIS Verification

        ## Purpose

        Make completion claims auditable and proportionate to change risk.

        ## When to Use

        Use before implementation planning, after meaningful changes, and before handoff or release acceptance.

        ## Inputs

        Gather affected components, contracts, risks, acceptance criteria, available tooling, and required assurance independence.

        ## Workflow

        1. Run `cis test inventory` and `cis test validate --strict`; reconcile the canonical suite profile before execution.
        2. Start with targeted build, test, lint, analysis, security, and packaging checks through `cis workflow run standard-delivery --run-id <id>`.
        3. Run each evidence gate as an independently observed command, or use fail-fast orchestration that preserves every gate's exit status; never let a later successful command mask an earlier failure. Canonical retries are zero.
        4. If a result could be environmental, inspect the recorded failure classification. A diagnostic rerun uses the same run ID as a new attempt and never erases the first product, infrastructure, prerequisite, timeout, cancellation, or unknown result.
        5. Run `cis test reconcile --run <id>` and `cis test trace <change-id> --run <id>`. A successful process without readable declared result evidence is `invalid-evidence`, not passed.
        6. Expand to component or repository checks only when impact or failures justify it. Record unavailable credential-dependent checks and any bounded human-approved exception truthfully.
        7. Record the run ID, profile digest, repository revision, artifact hashes, implementer, assurer, and assurance technique. Keep independent assurance separate from the implementing agent's self-assessment or use a genuinely independent mechanical technique.
        8. After all implementation and evidence are stable, run `cis verify diff` and confirm the snapshot names every planned repository, includes tracked and untracked changes, and is non-empty.
        9. Run `cis verify validate`. If the human explicitly accepts the outcome, use `cis verify finalize --reviewer <human> --reason <rationale>` so lifecycle completion, closure, recapture, and acceptance remain coordinated.

        ## Output Expectations

        Report a clear verdict, exact evidence, unrun checks, blockers, and residual risk.

        ## Guardrails

        Do not claim checks that were not run, infer earlier success from the final exit code of a non-fail-fast command chain, accept an empty or stale snapshot, or equate compilation with complete verification.

        ## Related Files

        Read the delivery-and-assurance specification and `.github/skills/cis-validate-completion/SKILL.md`.
        """;

    private static string CreateFeatureSpecificationGovernanceSkill() => """
        ---
        name: cis-feature-specification-governance
        description: Author, validate, review, approve, and refresh classification-aware feature specifications before detailed CIS planning.
        ---

        # CIS feature specification governance

        1. Read the active BRD, technical intent, approved high-level backlog item, repository profiles, standards, references, and applicable public/customer/backoffice frontend classification.
        2. Start or locate the feature with `cis brd feature status <high-level-item>` and create the governed draft through `cis brd feature validate` workflow; do not invent a second source of truth.
        3. Specify actors, outcomes, business rules, state transitions, contracts, authorization and non-disclosure, caching, persistence, migration/recovery, accessibility, observability, rollout, exclusions, and measurable acceptance criteria proportionate to the feature.
        4. Route frontend scope as public, customer, or backoffice. Describe required user journeys and review points without selecting unapproved visual implementation details.
        5. Define layered test obligations: unit, component, integration, business, architecture, frontend component, browser, security, operational, coverage, and mutation where applicable. Credential-dependent provider smoke may be explicitly unavailable; it is never silently passed.
        6. Run `cis brd feature validate <high-level-item>`. Resolve structural, currency, traceability, and cross-document findings before requesting review.
        7. Only after explicit human authorization run `cis brd feature approve <high-level-item> --reviewer <human> --reason <rationale>`.
        8. A material BRD or technical-intent change makes the feature stale and requires reconciliation. Mechanical validation with no semantic change adds no approval gate.

        Preserve stable identities, approval evidence, exclusions, and history. Never approve for the user, silently broaden MVP scope, treat a generated draft as current, or report an unexecuted test obligation as passed.
        """;

    private static string CreateMaintainContractsSkill(string documentationRoot) => $$"""
        ---
        name: cis-maintain-contracts
        description: Keep API, data, configuration, package, permission, route, and ERD references synchronized with implementation contract changes.
        ---

        # CIS Maintain Contracts

        ## Purpose

        Prevent implementation and contract references from drifting apart.

        ## When to Use

        Use for routes, DTOs, schemas, migrations, options, environment keys, dependencies, authorization, roles, UI routes, or entity relationships.

        ## Inputs

        Gather the diff, relevant governance specifications, references under `{{documentationRoot}}/references/`, and deterministic source evidence.

        ## Workflow

        1. Classify every changed contract surface.
        2. Treat OpenAPI as a generated contract baseline, not as a replacement for the row-level API dictionary.
        3. Update each affected reference using stable identities and actual implementation evidence in the same change as the implementation.
        4. For an API change, co-update its API row, permission usage and semantics, Problem Details identities, consumers, supported version, and OpenAPI where affected.
        5. Do not remove an older operation or version until its lifecycle, compatibility window, and known consumers are dispositioned.
        6. Update governance specifications only when maintenance rules change.
        7. Run strict documentation validation, deterministic OpenAPI export, forward-transitive diff against every supported baseline in the same major version, and relevant contract/reference drift checks.

        ## Output Expectations

        Produce implementation and reference co-changes, evidence, lifecycle status, and validation results.

        ## Guardrails

        Do not invent facts, document test fixtures as production contracts, expose secrets, or defer required co-changes silently.

        ## Related Files

        Use `{{documentationRoot}}/specs/`, `{{documentationRoot}}/references/`, and `{{documentationRoot}}/catalog.yml`.
        """;

    private static string CreateApiContractGovernanceSkill(string documentationRoot) => $$"""
        ---
        name: cis-api-contract-governance
        description: Design, change, and verify HTTP APIs against repository exposure, security, compatibility, inventory, OpenAPI, and drift rules.
        ---

        # CIS API Contract Governance

        ## Purpose

        Apply the repository API standard as an executable delivery checklist while preserving the governed contract inventories.

        ## When to Use

        Use for every new or changed endpoint, route, request/response contract, permission, error, version, webhook, file transfer, or externally consumed behavior.

        ## Workflow

        1. Read `{{documentationRoot}}/specs/api-design-and-governance-spec.md`, the Public Endpoint Caching Policy, and affected contract references.
        2. Classify the operation as `public`, `identity-protocol`, `customer`, `backoffice`, `internal/service`, `webhook`, or `probe`; identify module, host/deployment, consumers, trusted scope, permission, and data boundary.
        3. Prefer a compatible resource extension before adding a near-duplicate route. Record resource path, major version, lifecycle, and compatibility decision.
        4. Keep the controller/handler thin and use explicit DTOs. Define validation, statuses, stable Problem Details, pagination, rate limits, idempotency, concurrency, cancellation, timeouts, and safe telemetry as applicable.
        5. For `public` endpoints, enforce `PUBLIC-ENDPOINT-CACHE`: every response traverses a governed cache and the endpoint never directly accesses a database or repository, including on cache miss.
        6. Co-update the API dictionary, permissions dictionary, Problem Details catalogue, supported-version record, OpenAPI, implementation, and tests wherever the change affects them.
        7. Export OpenAPI deterministically and compare it with every supported baseline in the same major-version line using forward-transitive compatibility, then run authorization/abuse, integration, schema-diff, and documentation drift checks.

        ## Guardrails

        Do not accept caller-selected trusted scope, bind domain/persistence models, bury business logic in controllers, expose sensitive resource existence, treat OpenAPI as the only inventory, or leave derivable governance fields as `TBD`.

        ## Output Expectations

        Produce aligned implementation and contract artifacts with stable identities, explicit lifecycle/consumer disposition, commands, results, and evidence paths or digests.
        """;

    private static string CreateApiGovernanceInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**/*.{cs,ts,tsx,js,jsx,json,yml,yaml}"
        ---

        # CIS API governance instructions

        Before changing an endpoint, read `{{documentationRoot}}/specs/api-design-and-governance-spec.md`,
        `{{documentationRoot}}/references/api-governance-profile.md`, and the affected API, permission,
        and Problem Details rows. Preserve the exposure and trusted-scope boundary.

        After an API change:

        1. Run `cis api discover --repo .`.
        2. Run `cis api validate --repo . --strict` and resolve every error and warning.
        3. Run `cis api diff --repo .` when a governed OpenAPI baseline exists.
        4. Rebuild and validate the context graph.
        5. If discovery or repository configuration fails, run `cis repo doctor --repo .`.

        Do not edit `.cis/local/api/`, treat OpenAPI as the only inventory, accept caller-selected
        trusted scope, or bypass `PUBLIC-ENDPOINT-CACHE` for unauthenticated endpoints.
        """;

    private static string CreateMaintainDomainBehaviourSkill(string documentationRoot) => $$"""
        ---
        name: cis-maintain-domain-behaviour
        description: Keep commands, events, workflow states, invariants, projections, problem details, and module boundaries aligned with behavior changes.
        ---

        # CIS Maintain Domain Behaviour

        ## Purpose

        Preserve a reviewable model of durable system behavior across implementation and documentation.

        ## When to Use

        Use when a change adds, removes, renames, or alters commands, events, states, transitions, rules, read models, errors, or ownership boundaries.

        ## Inputs

        Gather feature and technical specifications, affected code and tests, and the domain-behavior references under `{{documentationRoot}}/references/`.

        ## Workflow

        1. Classify every changed behavior surface.
        2. Update matching reference rows with stable IDs, source evidence, lifecycle, and relationships.
        3. Keep command/event identities distinct and connect transitions, invariants, projections, and problems.
        4. Update module boundaries and contract references where the same change crosses them.
        5. Validate documentation and implementation evidence before handoff.

        ## Output Expectations

        Leave aligned behavior references, related contract updates, and explicit validation evidence.

        ## Guardrails

        Do not mark discovered behavior verified without evidence or make a generated summary authoritative.

        ## Related Files

        Use `{{documentationRoot}}/specs/`, `{{documentationRoot}}/references/`, and the repository profile.
        """;

    private static string CreateAddDocumentationSkill(string documentationRoot) => $$"""
        ---
        name: cis-add-documentation
        description: Create and register repository documentation with correct placement, metadata, authority, navigation, and validation.
        ---

        # CIS Add Documentation

        ## Purpose

        Add documentation without creating orphaned files or competing sources of truth.

        ## When to Use

        Use when creating a policy, standard, specification, workflow, procedure, ADR, reference, template, guide, checklist, or change record.

        ## Inputs

        Gather title, document type, scope, audience, owner, authority, lifecycle, related documents, and whether the item is normative.

        ## Workflow

        1. Classify and place the item under `{{documentationRoot}}/`.
        2. Add front matter with a stable ID and truthful lifecycle state.
        3. Register it in `{{documentationRoot}}/catalog.yml` and update nearby navigation.
        4. Link related authorities and avoid duplicated source-of-truth content.
        5. Run `cis docs validate --strict`.

        ## Output Expectations

        Leave a correctly placed, cataloged, navigable, and validated document.

        ## Guardrails

        Do not invent top-level roots casually, misuse lifecycle status, or create a second canonical source for the same contract.

        ## Related Files

        See `{{documentationRoot}}/README.md`, `{{documentationRoot}}/catalog.yml`, and `{{documentationRoot}}/templates/`.
        """;

    private static string CreateValidateCompletionSkill(string documentationRoot) => $$"""
        ---
        name: cis-validate-completion
        description: Validate completion claims against requested scope, acceptance criteria, documentation co-changes, deterministic checks, and residual risk.
        ---

        # CIS Validate Completion

        ## Purpose

        Prevent partial or weakly evidenced work from being presented as complete.

        ## When to Use

        Use before handoff, review readiness, release acceptance, or closing a change.

        ## Inputs

        Gather the request, plan, acceptance criteria, changed files, documentation delta, validation results, and unresolved risks.

        ## Workflow

        1. Confirm the intended output exists and matches scope.
        2. Check every acceptance criterion against direct evidence.
        3. Confirm required specifications and references changed with behavior.
        4. Verify deterministic checks and independent assurance requirements.
        5. Report a pass/fail verdict and exact remaining work.

        ## Output Expectations

        Produce an auditable verdict with evidence, limitations, blockers, and next action.

        ## Guardrails

        Do not equate drafting, compilation, or self-review with completion.

        ## Related Files

        Read `{{documentationRoot}}/specs/delivery-and-assurance-spec.md` and the active change record.
        """;

    private static string CreateWriteAdrSkill(string documentationRoot) => $$"""
        ---
        name: cis-write-adr
        description: Draft or update an architecture decision record for a durable technical choice with alternatives, consequences, and affected contracts.
        ---

        # CIS Write ADR

        ## Purpose

        Preserve why a durable technical choice was made and when it should be revisited.

        ## When to Use

        Use for long-lived architecture, boundary, data, security, integration, deployment, or operational decisions.

        ## Inputs

        Gather decision context, constraints, alternatives, evidence, affected specifications/references, stakeholders, and revisit conditions.

        ## Workflow

        1. Copy `{{documentationRoot}}/templates/adr-template.md` into `architecture/decisions/`.
        2. Assign a stable ID and Proposed status.
        3. Record context, decision, alternatives, consequences, risks, and affected contracts.
        4. Update technical intent and related specifications after acceptance.
        5. Register and validate the ADR.

        ## Output Expectations

        Leave a reviewable ADR with explicit status, tradeoffs, relationships, and verification or revisit triggers.

        ## Guardrails

        Do not use an ADR for temporary implementation detail or hide rejected alternatives.

        ## Related Files

        See `{{documentationRoot}}/templates/adr-template.md` and `{{documentationRoot}}/specs/technical-intent-spec.md`.
        """;

    private static string CreateRepositoryInstruction(string documentationRoot) =>
        "---\napplyTo: \"**\"\n---\n\n" +
        "# CIS repository guidance\n\n" +
        "- When onboarding or reconciling a repository, use `.github/skills/cis-repository-bootstrap/SKILL.md`.\n" +
        "- When onboarding several repositories, use `.github/skills/cis-import-repositories/SKILL.md`; dry-run the whole batch before confirmation.\n" +
        "- For BRD intake or currency review, use `.github/skills/cis-govern-business-requirements/SKILL.md`; discovery never proves currency and approval is human-only.\n" +
        "- After BRD approval and before change dossiers, use `.github/skills/cis-govern-technical-intent/SKILL.md`; technical decisions and approval remain human-authority actions.\n" +
        "- Run `cis repo init` with an explicit maintainer-selected `--root`; if it returns an error or collision, run `cis repo doctor` with the same `--repo` and `--root`.\n" +
        "- Read `.cis/repository.yml` before repository-wide work.\n" +
        $"- Start with `{documentationRoot}/README.md`, `{documentationRoot}/catalog.yml`, and the repository profile before broad searches.\n" +
        $"- Treat `{documentationRoot}/specs/product-intent-spec.md` and `{documentationRoot}/specs/technical-intent-spec.md` as foundational intent documents.\n" +
        $"- Read `{documentationRoot}/specs/repository-delivery-policy-spec.md` before any branch, commit, push, pull-request, merge, tag, release, or remote-issue action; Draft or unresolved policy means local-only unless the user explicitly authorizes the exact action.\n" +
        "- Use the smallest matching `cis-*` skill before inventing a repository workflow.\n" +
        "- Before governed implementation, use `.github/skills/cis-standards-governance/SKILL.md` and `cis standards applicable` for every affected target and stack; model advice is never conformance proof.\n" +
        "- Use `.github/skills/cis-skill-governance/SKILL.md` after initialization, classification changes, imports, or skill edits; audit is local-first with configured remote fallback, and only explicit `--fix` authorizes reversible quarantine.\n" +
        "- Use `.github/skills/cis-file-index/SKILL.md` and `cis index find` before broad source searches; cards are non-authoritative and disposable.\n" +
        "- Use `.github/skills/cis-feedback-loop/SKILL.md` to review automatic local usage, possible token savings, repeated failures, and compact-output opportunities.\n" +
        "- Use `.github/skills/cis-graph-context/SKILL.md` before broad repository searches or impact analysis; never edit `.cis/local/` derived state.\n" +
        "- Use `.github/skills/cis-change-dossier/SKILL.md`, `cis-impact-review`, `cis-decision-review`, and `cis-bounded-planning` for reviewed change delivery.\n" +
        "- Treat `impact accept|reject|defer`, `decision resolve|defer|promote`, `plan approve`, and `change close` as explicit human-authority commands. `cis plan derive` may reuse a tool-confirmed current feature approval for eligible deterministic impacts and the exact plan; it does not create new human authority.\n" +
        "- Inspect callers, contracts, tests, configuration, permissions, events, and operational effects before changing behavior.\n" +
        "- Update contract and domain-behavior references in the same change as implementation.\n" +
        "- For API changes, use `.github/skills/cis-api-contract-governance/SKILL.md`; run `cis api discover`, strict validation, compatibility diff when baselined, then rebuild the graph.\n" +
        "- Record durable technical choices as ADRs and link affected specifications and references.\n" +
        "- Keep generated discoveries marked as unverified until a maintainer reviews them.\n" +
        "- Never silently overwrite human-authored canonical documentation or claim checks that were not run.\n" +
        "- Update affected specifications and references in the same change, then run strict documentation validation.\n";

    private static string CreateStandardsGovernanceInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**"
        ---

        # CIS standards governance

        - Resolve applicable active standards before implementation with `cis standards applicable --target <target> [--stack <stack>]` and read each returned canonical file below `{{documentationRoot}}/standards/`.
        - Classification-selected PARR-derived defaults are repository-owned starter standards. Review their provenance and adapt them through canonical, human-reviewed changes rather than assuming PARR product choices apply unchanged.
        - Treat policies as authority or outcome rules, standards as required ways of working, specifications as precise contracts, and procedures as ordered steps.
        - Preserve immutable document IDs and repository-unique rule IDs; retired IDs are never reused.
        - Use `MUST` and `MUST NOT` for mandatory rules, `SHOULD` for expectations requiring deviation rationale, and `MAY` for optional behavior.
        - Map every active rule in `{{documentationRoot}}/references/standards-conformance-matrix.md` to deterministic, architecture-test, manual-review, advisory-model, or not-mapped enforcement.
        - Never turn an advisory-model or heuristic candidate into a confirmed breach or compliance claim without deterministic evidence or explicit human review.
        - Never approve a standards exception. Record the human approver, rationale, bounded scope, expiry or review condition, and compensating controls.
        - After standard or conformance changes, run `cis standards validate --strict`, inspect `cis standards conformance --gaps-only`, run `cis docs validate --strict`, and rebuild the graph.
        - Preview imports with `cis standards import --source <path-or-url> --dry-run`; canonical file, catalog, and conformance changes require `--yes`. Structural `--fix` acts on staged copies, may assign IDs to existing normative statements, and never invents semantics.
        - Run `cis standards audit` after imports or material changes. It prefers local generation, may use a configured remote provider when local is unavailable, and becomes deterministic-only with `--no-llm`.
        - `cis standards audit --fix` authorizes reversible quarantine only. It does not authorize semantic merges, conflict resolution, exception approval, or deletion of historical evidence.
        - Validate the known-standard-pattern catalogue with `cis standards patterns --strict`. Repository patterns live as cataloged canonical Markdown below `{{documentationRoot}}/references/standard-patterns/`; duplicate IDs are blocking conflicts.
        - Run `cis standards infer` only against a fresh compiler-backed graph. Treat matches, prevalence, and counterexamples as derived observations; an inferred candidate is not a standard and cannot be promoted or accepted without explicit human review.
        """;

    private static string CreateChangeDeliveryInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/changes/**"
        ---

        # CIS change-delivery records

        - Treat proposal, impact, decision, and plan Markdown as canonical review records.
        - Use `cis change`, `cis impact`, `cis decision`, and `cis plan` commands for managed lifecycle and table changes.
        - For a current approved CIS feature, prefer `cis plan derive`; it carries the exact existing authority through eligible deterministic impacts and the generated plan atomically. Use `cis plan import-spec` and explicit impact/plan review only for reported exceptions or sources without reusable approval authority. Never copy the feature into a competing source of truth.
        - Treat `plan.md` as the dependency ledger and each `agent-tasks/WORK-NNN.md` file as the executable task contract.
        - Require complexity, bounded required changes, exclusions, evidence, dependencies, acceptance checklists, validation, completion evidence, and deferral rationale in every task.
        - For UI-bearing scope, validate `wireframes.md`, carry exact earlier approved PNGs through `cis design reuse`, reuse `cis design templates`, and render only uncovered screens inside the governed application shell. Never infer reuse without a compatibility rationale and verified source authority. By default the rendered-design decision approves the exact wireframe digest and combined reused-and-rendered visual pack together; a separate wireframe approval is optional.
        - Classify every frontend requirement, screen, design artifact, and frontend task as `public`, `customer`, or `backoffice`; do not merge affected types into an unclassified UI task.
        - Every unauthenticated endpoint must traverse a governed cache. Its route/controller/handler must not directly access a database, database context/client, query provider, or repository, including on cache miss; apply and verify `PUBLIC-ENDPOINT-CACHE` obligations.
        - A successful `cis design render` sets the global design gate to `PausedForReview`; stop all non-review work until a human runs `cis design approve` or `cis design reject` with reviewer identity and rationale, or until `cis design reconcile` deterministically carries current feature authority across an unchanged PNG manifest. Reconciliation is not a new approval and fails closed on any mismatch.
        - Rejection preserves renderer/PNG hashes and rationale, removes rejected PNGs, and permits only wireframe/design revision while the pause remains active.
        - Record exact cross-task validation and assurance evidence in `verification.md`.
        - Treat `test-cases.md` and `test-cases.csv` as synchronized derived feature artifacts. Review requirement coverage. Put every stable `TC-*` identity in its corresponding automated test name, framework metadata, or adjacent traceability annotation, then regenerate both through `cis plan import-spec` or `cis plan derive` after test or source changes to refresh exact cross-repository mappings. Planning may retain `Pending`; final verification must not. Keep execution results in `verification.md`.
        - Final Delivery Sweep owns the deterministic planned-versus-actual audit and readiness recommendation. Coordination owns explicit human final acceptance and may complete only after the sweep and all child dispositions are resolved.
        - Capture a workspace-aware `cis verify diff` after evidence stabilizes. Never accept a legacy, empty, digest-invalid, or stale snapshot; use `cis verify finalize` for explicitly authorized final acceptance.
        - Apply the target repository's `{{documentationRoot}}/specs/repository-delivery-policy-spec.md`; plan approval does not itself authorize branch, commit, push, pull-request, merge, tag, release, or remote-issue mutation.
        - Do not hand-edit stable IDs, exact baselines, finding disposition, decision state, plan approval, or audit events.
        - Deterministic analysis and AI may propose findings, questions, options, and work; they may not invent human disposition, resolution, approval, or closure authority. `cis plan derive` may reuse tool-confirmed current feature authority with exact provenance and must stop on uncertainty.
        - Preserve rejected and deferred records with rationale rather than deleting them.
        - Rebuild and validate the graph after canonical decisions or promoted ADRs change.
        - Validate the plan before implementation and run strict documentation validation after canonical updates.
        """;

    private static string CreateBusinessRequirementsInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/specs/business-requirements.md"
        ---

        # CIS business requirements authority

        - The canonical BRD belongs only to the repository registered with workspace role `authority`.
        - Treat BRDs, domain-equivalent product-design documents such as GDDs, and development feature specifications found in authority or participant repositories as source evidence until a human assesses each source row.
        - File existence, deterministic extraction, graph freshness, or agent review never proves business currency.
        - Complete business outcomes, scope, actors, capabilities, requirements, constraints, success measures, traceability, and open questions through human review.
        - Preserve CIS baseline and source block markers, candidate IDs, source and approval hashes, and participant graph identities.
        - After implementation graph rebuilds, run `cis technical-intent refresh`. Pure managed-baseline drift preserves the original BRD, technical-intent, and backlog approvals. If its BRD stage blocks, use `cis brd reconcile` to expose the exact source/semantic delta and request review only for that delta.
        - `Adopted` feature specifications must be incorporated into relevant BRD sections and cited by their managed source ID in Traceability.
        - Agents may discover, draft, propose reconciliation edits, reconcile managed evidence, and validate. They may not select source assessments, invent stakeholder decisions, claim semantic absorption without corresponding BRD edits, or run `cis brd approve` without explicit user authorization for the reviewer and rationale.
        - `Active` requires recorded human approval. Evidence drift may reduce it to `Stale`; automation may never restore `Active`.
        - After BRD and technical-intent approval, use `cis brd backlog build` to create the reviewed high-level bridge. Do not jump directly from the BRD to executable tasks.
        - High-level backlog approval authorizes feature-specification preparation only; use `cis brd backlog start --item <HLT-ID>` for dependency-ready items, complete the Draft, and run `cis brd feature validate --item <HLT-ID>` before review.
        - Run `cis brd feature approve --item <HLT-ID> --reviewer <human> --reason <rationale>` only with explicit authority. Rebuild the graph and reconcile the approved feature into the BRD before change planning.
        - Feature approval accepts detailed scope only; it does not directly authorize implementation, deployment, or release. Its exact current authority may be reused by `cis plan derive` for eligible deterministic impacts and the exact validated bounded plan; uncertainty or expanded scope requires another human decision.
        """;

    private static string CreateFeatureSpecificationInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/specs/features/**/feature-specification.md"
        ---

        # CIS feature-specification authority

        - Preserve `cis.stable_id`, `cis.high_level_item`, `cis.brd_requirement`, `cis.backlog_item_hash`, and approval metadata.
        - Cover every affected repository and every public, customer, or backoffice frontend type recorded by the selected high-level backlog item.
        - Use structured `FEAT-*` requirements with a bounded surface, frontend type, testable requirement, and acceptance criteria.
        - Complete every required section explicitly; use a reasoned `Not applicable` statement rather than a placeholder.
        - Run `cis brd feature validate --item <HLT-ID>` and present the exact scope and validation result before requesting approval.
        - Never run `cis brd feature approve` without explicit human reviewer identity and rationale. Approval accepts scope and may be carried by `cis plan derive` only through eligible deterministic impacts and the exact validated plan; it does not directly authorize implementation or release.
        - After approval, rebuild the graph and reconcile the feature as BRD evidence before change planning.
        - For the reconciled current feature, prefer `cis plan derive` and do not request separate impact or plan approvals when it succeeds. Surface only exceptional low-confidence, deferred, truncated, conflicting, stale, or invalid results for human review.
        """;

    private static string CreateTechnicalIntentInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/specs/technical-intent-spec.md"
        ---

        # CIS technical intent authority

        - The workspace authority owns one canonical workspace-scoped technical intent; participant repositories retain repository-scoped supporting intent documents.
        - An Active, current BRD and fresh participant graphs are required before initialization or approval.
        - Preserve `cis:technical-intent-baseline` markers, the BRD hash, participant build IDs, stable identity, schema, and approval digest.
        - Complete every required technical section and keep unresolved choices in the structured `TI-DEC-*` table.
        - Agents may draft technical direction and options. Only explicit human authority may resolve or defer decisions and run `cis technical-intent approve` with the exact reviewer and rationale.
        - `cis change create`, `cis plan build`, `cis plan import-spec`, and `cis plan derive` are blocked in a workspace authority unless technical intent is Active and current.
        - Rebuild the workspace graph after canonical edits or approval. BRD, participant-baseline, or approved-content drift makes the intent non-current.
        """;

    private static IReadOnlyList<string> EvidenceForFamily(
        string slug,
        RepositoryClassification classification)
    {
        var relevant = slug switch
        {
            "api-dictionary" or "command-dictionary" or "problem-details-catalogue" => classification.Components.Where(component =>
                component.Roles.Contains("backend-api-producer", StringComparer.Ordinal)
                || component.Roles.Contains("backend-api-consumer", StringComparer.Ordinal)),
            "event-dictionary" => classification.Components.Where(component =>
                component.Roles.Contains("event-producer", StringComparer.Ordinal)
                || component.Roles.Contains("event-consumer", StringComparer.Ordinal)),
            "permissions-dictionary" => classification.Components.Where(component =>
                component.Capabilities.Contains("authorization", StringComparer.Ordinal)),
            "data-dictionary" or "erd" => classification.Components.Where(component =>
                component.Capabilities.Contains("persistence", StringComparer.Ordinal)),
            "workflow-state-dictionary" or "business-invariant-catalogue" => classification.Components.Where(component =>
                component.Roles.Any(role => role is "backend-api-producer" or "worker" or "frontend-consumer")),
            "projection-dictionary" => classification.Components.Where(component =>
                component.Roles.Contains("event-consumer", StringComparer.Ordinal)
                || component.Capabilities.Contains("persistence", StringComparer.Ordinal)),
            "screen-route-map" => classification.Components.Where(component =>
                component.Roles.Contains("frontend-consumer", StringComparer.Ordinal)
                || component.Roles.Contains("mobile-client", StringComparer.Ordinal)
                || component.Roles.Contains("native-frontend", StringComparer.Ordinal)),
            _ => classification.Components,
        };
        return relevant.SelectMany(component => component.Evidence).Distinct(StringComparer.Ordinal).Take(10).ToArray();
    }

    private static bool HasRole(RepositoryClassification classification, params string[] roles)
        => classification.Components.Any(component => roles.Any(role => component.Roles.Contains(role, StringComparer.Ordinal)));

    private static bool HasCapability(RepositoryClassification classification, string capability)
        => classification.Components.Any(component => component.Capabilities.Contains(capability, StringComparer.Ordinal));

    private static bool HasApplicationBehavior(RepositoryClassification classification)
        => classification.Components.Any(component => component.Roles.Any(role =>
            role is "backend-api-producer" or "worker" or "frontend-consumer" or "mobile-client" or "native-frontend"));

    private sealed record ReferenceFamily(
        string Slug,
        string Title,
        string Description,
        IReadOnlyList<string> Columns,
        Func<RepositoryClassification, bool> Applies);
}
