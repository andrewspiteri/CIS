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

        // The authority owns the cross-repository vocabulary and may precede every implementation
        // repository in a greenfield workspace. Seed its complete governed starter set; participant
        // repositories remain classification-selected so they do not receive irrelevant contracts.
        foreach (var family in ReferenceFamilies.Where(family => workspaceAuthority || family.Applies(classification)))
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
        last_reviewed: "2026-08-27"
        review_cadence: "on provider, model, privacy, or cost-policy change"
        ---

        # AI routing profile

        Local Ollama is the default. Remote transmission requires both a reviewed route
        and explicit `--allow-remote` authorization for the exact submitted content.

        | Capability | Provider | Model | Allow remote | Cache |
        |---|---|---|---|---|
        | index-card | ollama | repository-smallest-local | no | yes |
        | brd-question-suggestion | ollama | repository-smallest-local | no | no |
        | context-summary | ollama | repository-smallest-local | no | yes |
        | learning-summary | ollama | repository-smallest-local | no | yes |
        """);
        Add("reference.agent-provider-profile", "references/agent-provider-profile.md", "agent-provider-profile", """
        ---
        title: "Agent Provider Profile"
        type: agent-provider-profile
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-28"
        review_cadence: "on provider, permission, isolation, or retention-policy change"
        ---

        # Agent provider profile

        No direct provider is selected by default. A controller must select Codex, Claude,
        or another discovered provider until this profile is reviewed. Credentials remain
        provider-native and must never be copied into this document.

        | Setting | Value | Rationale |
        |---|---|---|
        | default-provider | none | Avoid silent vendor selection. |
        | default-transport | provider-preferred | Capability negotiation remains explicit. |
        | implementation-isolation | git-worktree | Preserve the user's current working tree while seeding the isolated worktree from its exact tracked and non-ignored untracked baseline. |
        | direct-dirty-working-tree | denied | Requires an explicit reviewed policy change. |
        | maximum-run-seconds | 3600 | Bound local foreground execution. |
        | maximum-startup-seconds | 30 | Fail closed when a provider produces no startup evidence. |
        | maximum-idle-seconds | 300 | Classify a silent stalled provider separately from total expiry. |
        | approve-within-ceiling | denied | Permission requests require explicit controller authorization per run. |

        ## Providers

        | Provider | Enabled | Preferred transport | Allowed modes | Maximum permission | Notes |
        |---|---|---|---|---|---|
        | codex | yes | app-server | plan, implement, review | workspace-write | `exec-json` is an explicit non-interactive fallback. Resolution checks `CIS_CODEX_EXECUTABLE`, process PATH, then the current Windows Codex Desktop installation. Inconclusive login status is advisory because ambient Desktop/App Server authentication can still execute. Explicit setup uses provider-native browser or device authentication through `cis agent provider authenticate codex`; CIS never receives credentials. |
        | claude | yes | stream-json | plan, implement, review | workspace-write | Headless execution uses predeclared permissions. |
        | portable | yes | envelope | plan, implement, review | read-only | Preparation only; no direct execution. |
        """);
        Add("reference.source-evidence", "references/source-evidence.md", "source-evidence-registry", """
        ---
        title: "Source Evidence Registry"
        type: source-evidence-registry
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-30"
        review_cadence: "on source selection or source revision"
        ---

        # Source evidence registry

        Original files remain authoritative. Markdown projections under
        `.cis/local/references/` are derived routing evidence and may be rebuilt without
        rewriting governed specifications.

        | ID | Source path | Format | Registered SHA-256 | Assessment | Actor | Registered at (UTC) | Rationale |
        | --- | --- | --- | --- | --- | --- | --- | --- |
        """);
        Add("reference.ai-model-registry", "references/ai-model-registry.md", "ai-model-registry", """
        ---
        title: "AI Model Registry"
        type: ai-model-registry
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-27"
        review_cadence: "on provider, model, prompt, task-class, privacy, or policy change"
        ---

        # AI model registry

        Availability is not approval. Approve a provider/model for each task class only after
        a real capability probe, deterministic benchmark, and runtime prompt-regression pass.

        | Provider | Model | Task class | State | Reviewer | Reason | Evidence digest | Approved UTC |
        |---|---|---|---|---|---|---|---|
        """);
        Add("reference.ai-evaluation-index-card", "references/ai-evaluation-datasets/index-card.md", "ai-evaluation-dataset", """
        ---
        title: "Index Card AI Evaluation Dataset"
        type: ai-evaluation-dataset
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-27"
        review_cadence: "on prompt, model, task-class, or routing change"
        task_class: index-card
        ---

        # Index card AI evaluation dataset

        | Case ID | Prompt | Required terms |
        |---|---|---|
        | concise-routing-summary | Return a concise file-routing summary that states the file purpose and important symbols. | purpose |
        """);
        Add("reference.diagnostics-profile", "references/diagnostics-profile.md", "diagnostics-profile", """
        ---
        title: "Diagnostics Evidence Profile"
        type: diagnostics-profile
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-27"
        review_cadence: "on runtime evidence source or sensitivity change"
        ---

        # Diagnostics evidence profile

        Sources are disabled until their repository-relative sanitized export exists.

        | Source | Kind | Location | Enabled | Sensitive |
        |---|---|---|---|---|
        | application-log | text-log | .cis/local/diagnostics/input/application.log | no | no |
        | structured-runtime | jsonl | .cis/local/diagnostics/input/runtime.jsonl | no | no |
        | test-log | workflow-log | .cis/local/diagnostics/input/tests.log | no | no |
        | browser-log | browser-log | .cis/local/diagnostics/input/browser.jsonl | no | no |
        | container-log | container-log | .cis/local/diagnostics/input/containers.log | no | no |
        """);
        Add("reference.local-artifact-retention", "references/local-artifact-retention.md", "local-artifact-retention", """
        ---
        title: "Local Artifact Retention"
        type: local-artifact-retention
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-27"
        review_cadence: "on evidence, storage, privacy, or recovery-policy change"
        ---

        # Local artifact retention

        Rules apply only to immediate entries beneath the declared `.cis/local/` path.
        Canonical Markdown and user-managed repository files are never eligible.

        | Family | Path | Retain days | Keep latest | Action |
        |---|---|---:|---:|---|
        | graph | .cis/local/graph | 0 | 0 | preserve |
        | api | .cis/local/api | 0 | 0 | preserve |
        | references | .cis/local/references | 0 | 0 | preserve |
        | frontend | .cis/local/frontend | 0 | 0 | preserve |
        | policy-impact | .cis/local/impact | 0 | 0 | preserve |
        | index-cards | .cis/local/index-cards | 0 | 0 | preserve |
        | brd-question-suggestions | .cis/local/brd/questions | 0 | 0 | preserve |
        | context | .cis/local/context | 30 | 20 | archive |
        | workflows | .cis/local/workflows | 30 | 20 | archive |
        | testing | .cis/local/testing/runs | 30 | 10 | archive |
        | security | .cis/local/security/runs | 30 | 10 | archive |
        | ci | .cis/local/ci | 14 | 20 | archive |
        | diagnostics | .cis/local/diagnostics | 30 | 20 | archive |
        | agents | .cis/local/agents | 30 | 20 | archive |
        | generation | .cis/local/generate | 30 | 20 | archive |
        | ai-cache | .cis/local/ai/cache | 7 | 50 | delete |
        | ai-qualification | .cis/local/ai/qualification | 90 | 20 | archive |
        """);
        var testing = RepositoryTestingStarter.Create(repositoryPath, documentationRoot, classification);
        Add("workflow.standard-delivery", "workflows/standard-delivery.md", "workflow-definition", testing.Workflow);
        Add("workflow.security-verification", "workflows/security-verification.md", "workflow-definition", testing.SecurityWorkflow);
        Add("reference.test-suite-profile", "references/test-suite-profile.md", "test-suite-profile", testing.Profile);
        Add("reference.security-suite-profile", "references/security-suite-profile.md", "security-suite-profile", testing.SecurityProfile);
        Add("reference.accepted-security-findings", "references/accepted-security-findings.md", "accepted-security-findings", testing.AcceptedSecurityFindings);
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
            "govern-solution-design",
            "Project Active technical intent into one governed overall solution-design and component-sheet bundle.",
            CreateGovernSolutionDesignSkill(),
            selections,
            artifacts);
        AddSkill(
            "govern-ui-direction",
            "Capture and govern the workspace-level UI look, feel, shell, component, responsive, and accessibility direction.",
            CreateGovernUiDirectionSkill(),
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
            "security-testing",
            "Run portable security scanners, reconcile redacted evidence, and preserve deterministic gates with optional local-only AI triage.",
            CreateSecurityTestingSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "ci-investigation",
            "Inspect remote CI checks, runs, jobs, bounded logs, artifacts, and failures before a focused fix or explicitly confirmed rerun.",
            CreateCiInvestigationSkill(),
            selections,
            artifacts);
        AddSkill(
            "ai-model-qualification",
            "Probe, benchmark, regression-test, explain, and explicitly approve model/task-class routes without treating availability as approval.",
            CreateAiModelQualificationSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "mcp-adapter",
            "Expose bounded repository context and explicitly confirmed CIS mutations to local MCP clients without duplicating domain logic.",
            CreateMcpAdapterSkill(),
            selections,
            artifacts);
        AddSkill(
            "local-artifact-retention",
            "Bound local CIS evidence growth through previewed retention, verified reversible archives, safe retrieval, conflict-aware restore, and hash tombstones.",
            CreateLocalArtifactRetentionSkill(documentationRoot),
            selections,
            artifacts);
        AddSkill(
            "template-applicability",
            "Make a deterministic repository-template applicability decision before generating repeated structures.",
            CreateTemplateApplicabilitySkill(documentationRoot), selections, artifacts);
        AddSkill(
            "tooling-evidence",
            "Record and strictly validate exact CIS routing, generation, testing, and usage evidence for changed files.",
            CreateToolingEvidenceSkill(), selections, artifacts);
        AddSkill(
            "policy-impact",
            "Generate bounded deterministic impact candidates from policy targets before remediation planning.",
            CreatePolicyImpactSkill(), selections, artifacts);
        AddSkill(
            "diagnostics-analysis",
            "Validate diagnostics profiles and inspect bounded redacted structured runtime evidence.",
            CreateDiagnosticsAnalysisSkill(documentationRoot), selections, artifacts);
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
        if (HasRole(classification, "frontend-consumer", "mobile-client", "native-frontend"))
        {
            AddSkill(
                "frontend-context",
                "Discover and validate framework-neutral frontend screens, routes, components, navigation, state, and API calls before graph and impact work.",
                CreateFrontendContextSkill(documentationRoot),
                selections,
                artifacts);
        }
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
            "agent-execution",
            "Run approved CIS tasks through discovered Codex or Claude providers with bounded permissions, durable attempts, and explicit result reconciliation.",
            CreateAgentExecutionSkill(documentationRoot),
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

        const string referenceInstructionDefinition = "guidance.instruction.reference-governance";
        selections.Add(new RepositoryStarterSelection(
            referenceInstructionDefinition,
            "Every initialized repository receives deterministic non-API reference discovery, validation, and drift instructions.",
            ["cis references module"]));
        artifacts.Add(new RepositoryStarterArtifact(
            referenceInstructionDefinition,
            referenceInstructionDefinition,
            ".github/instructions/cis-reference-governance.instructions.md",
            CreateReferenceGovernanceInstruction(documentationRoot),
            null));

        const string ciInstructionDefinition = "guidance.instruction.ci-investigation";
        selections.Add(new RepositoryStarterSelection(
            ciInstructionDefinition,
            "Every initialized repository receives remote CI evidence, privacy, authority, and mutation rules.",
            ["cis ci module"]));
        artifacts.Add(new RepositoryStarterArtifact(
            ciInstructionDefinition,
            ciInstructionDefinition,
            ".github/instructions/cis-ci-investigation.instructions.md",
            CreateCiInvestigationInstruction(),
            null));

        const string aiQualificationInstructionDefinition = "guidance.instruction.ai-model-qualification";
        selections.Add(new RepositoryStarterSelection(
            aiQualificationInstructionDefinition,
            "Every initialized repository receives model qualification, evidence, privacy, and approval rules.",
            ["cis ai model qualification"]));
        artifacts.Add(new RepositoryStarterArtifact(
            aiQualificationInstructionDefinition,
            aiQualificationInstructionDefinition,
            ".github/instructions/cis-ai-model-qualification.instructions.md",
            CreateAiModelQualificationInstruction(documentationRoot),
            null));

        const string mcpInstructionDefinition = "guidance.instruction.mcp-adapter";
        selections.Add(new RepositoryStarterSelection(
            mcpInstructionDefinition,
            "Every initialized repository receives MCP scope, transport, mutation, and evidence rules.",
            ["cis mcp module"]));
        artifacts.Add(new RepositoryStarterArtifact(
            mcpInstructionDefinition,
            mcpInstructionDefinition,
            ".github/instructions/cis-mcp-adapter.instructions.md",
            CreateMcpAdapterInstruction(),
            null));

        const string artifactInstructionDefinition = "guidance.instruction.local-artifact-retention";
        selections.Add(new RepositoryStarterSelection(
            artifactInstructionDefinition,
            "Every initialized repository receives local artifact scope, retention, archive, cleanup, and recovery rules.",
            ["cis artifacts module"]));
        artifacts.Add(new RepositoryStarterArtifact(
            artifactInstructionDefinition,
            artifactInstructionDefinition,
            ".github/instructions/cis-local-artifact-retention.instructions.md",
            CreateLocalArtifactRetentionInstruction(documentationRoot),
            null));

        const string toolkitEvidenceInstructionDefinition = "guidance.instruction.toolkit-parity-evidence";
        selections.Add(new RepositoryStarterSelection(toolkitEvidenceInstructionDefinition,
            "Every initialized repository receives template applicability, tooling evidence, policy impact, and structured diagnostics rules.",
            ["cis generate applicable", "cis agent evidence validate", "cis impact policy analyse", "cis diagnostics doctor"]));
        artifacts.Add(new RepositoryStarterArtifact(toolkitEvidenceInstructionDefinition,
            toolkitEvidenceInstructionDefinition, ".github/instructions/cis-toolkit-evidence.instructions.md",
            CreateToolkitEvidenceInstruction(documentationRoot), null));

        if (HasRole(classification, "frontend-consumer", "mobile-client", "native-frontend"))
        {
            const string frontendContextInstructionDefinition = "guidance.instruction.frontend-context";
            selections.Add(new RepositoryStarterSelection(frontendContextInstructionDefinition,
                "Frontend-capable repositories receive provider, canonical-route, graph, and framework coverage instructions.",
                ["cis frontend module", "repository classification"]));
            artifacts.Add(new RepositoryStarterArtifact(frontendContextInstructionDefinition,
                frontendContextInstructionDefinition, ".github/instructions/cis-frontend-context.instructions.md",
                CreateFrontendContextInstruction(documentationRoot), null));
        }

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

        const string securityInstructionDefinition = "guidance.instruction.security-testing";
        selections.Add(new RepositoryStarterSelection(
            securityInstructionDefinition,
            "Every initialized repository receives scanner, redaction, exception, evidence, and local-AI security rules.",
            ["cis security module"]));
        artifacts.Add(new RepositoryStarterArtifact(
            securityInstructionDefinition,
            securityInstructionDefinition,
            ".github/instructions/cis-security-testing.instructions.md",
            CreateSecurityTestingInstruction(documentationRoot),
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

        const string solutionDesignInstructionDefinition = "guidance.instruction.solution-design";
        selections.Add(new RepositoryStarterSelection(
            solutionDesignInstructionDefinition,
            "Every initialized repository receives overall solution-design bundle authority and traceability rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            solutionDesignInstructionDefinition,
            solutionDesignInstructionDefinition,
            ".github/instructions/cis-solution-design.instructions.md",
            CreateSolutionDesignInstruction(documentationRoot),
            null));

        const string uiDirectionInstructionDefinition = "guidance.instruction.ui-direction";
        selections.Add(new RepositoryStarterSelection(
            uiDirectionInstructionDefinition,
            "Every initialized repository receives high-level UI-direction provenance, reuse, and feature-handoff rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            uiDirectionInstructionDefinition,
            uiDirectionInstructionDefinition,
            ".github/instructions/cis-ui-direction.instructions.md",
            CreateUiDirectionInstruction(documentationRoot),
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

        const string agentExecutionInstructionDefinition = "guidance.instruction.agent-execution";
        selections.Add(new RepositoryStarterSelection(
            agentExecutionInstructionDefinition,
            "Every initialized repository receives provider-neutral agent execution, permission, isolation, provenance, and authority rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(
            agentExecutionInstructionDefinition,
            agentExecutionInstructionDefinition,
            ".github/instructions/cis-agent-execution.instructions.md",
            CreateAgentExecutionInstruction(documentationRoot),
            null));

        const string executionInstructionDefinition = "guidance.instruction.delivery-execution";
        selections.Add(new RepositoryStarterSelection(
            executionInstructionDefinition,
            "Every initialized repository receives execution, agent-result, verification, diagnostics, and learning authority rules.",
            ["cis curated starter"]));
        artifacts.Add(new RepositoryStarterArtifact(executionInstructionDefinition, executionInstructionDefinition,
            ".github/instructions/cis-delivery-execution.instructions.md", CreateDeliveryExecutionInstruction(documentationRoot), null));

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
            "  technical_intent_schema: 2\n  approved_by: null\n  approved_at: null\n  approval_reason: null\n  approved_content_hash: null\n---\n\n" +
            $"# {repositoryId} Technical Intent\n\n" +
            (workspaceAuthority
                ? "This is the workspace technical authority derived from the active business requirements. Run `cis technical-intent init` after BRD approval to generate the evidence-derived business baseline, repository surface, applicable-standards provenance, architecture guidelines, and open decision skeleton.\n\n"
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

        1. Run `cis feedback summary --format agent` for aggregate outcomes, duration, output estimates, and possible savings; add `--since` when historical totals would obscure the current workflow.
        2. Run `cis feedback opportunities --format agent`. It defaults to the latest 24 hours and evaluates per-invocation output, recent unresolved non-success, and duplicate query bursts.
        3. Use `cis feedback usage --limit 20 --format agent` only when recent per-invocation evidence is needed.
        4. Distinguish execution failures from invalid requests, governed blocks, cancellations, and Repository Doctor finding-bearing results. Run Doctor only when repository state or prerequisites may be involved; do not treat Doctor finding issues as a failed Doctor execution.
        5. Prefer compact structured projections and shared in-flight reads for repeated machine queries. Retain full evidence only when the workflow actually consumes it.
        6. Treat estimates as directional evidence. Report the basis and confidence; never turn an unestimated command into a savings claim.

        The ledger under `.cis/local/feedback/` is disposable and non-authoritative. CIS stores command paths, option names, counts, timing, and outcomes—not option values or command output. Do not copy the ledger into canonical documentation unless a reviewed summary is required.
        """;

    private static string CreateAiModelQualificationSkill(string documentationRoot) => $$"""
        ---
        name: cis-ai-model-qualification
        description: Qualify local or explicitly authorized remote models with runtime probes, deterministic benchmarks, prompt-regression datasets, route explanations, and task-class approvals. Use before assigning a model to repository work or after provider, model, prompt, dataset, or policy changes.
        ---

        # CIS AI model qualification

        1. Read `{{documentationRoot}}/references/ai-routing-profile.md`, `{{documentationRoot}}/references/ai-model-registry.md`, and the applicable repository-owned evaluation dataset.
        2. Run `cis ai model probe --provider <provider> --model <model> --format agent` for a real capability check.
        3. Run `cis ai model benchmark --provider <provider> --model <model> --task-class <class> --format agent`.
        4. Run `cis ai eval prompt-regression --provider <provider> --model <model> --task-class <class> --dataset <path> --format agent`.
        5. Inspect `.cis/local/ai/qualification/` evidence. Generated text is not retained; use case outcomes, latency, missing-term evidence, and hashes.
        6. Run `cis ai route explain <capability> --format agent`. Availability alone is never a selected, approved route.
        7. Only a human reviewer may run `cis ai model approve ... --reviewer <name> --reason <reason> --yes` after all three runtime evidence kinds pass.
        8. Rerun qualification after provider, model, prompt, dataset, privacy, or route-policy changes. Never lower deterministic expectations merely to make a model pass.

        Remote qualification requires explicit `--allow-remote` authorization for the exact prompts and datasets. Model output is advisory and cannot approve itself, change a score, or become completion evidence.
        """;

    private static string CreateAiModelQualificationInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**"
        ---

        # CIS AI model qualification authority

        - Canonical routes, model approvals, and evaluation datasets live beneath `{{documentationRoot}}/references/`; derived qualification evidence lives beneath `.cis/local/ai/qualification/`.
        - Provider health and model availability are not task-class approval.
        - Approval requires a passed runtime capability probe, deterministic benchmark, runtime prompt-regression execution, named human reviewer, rationale, and `--yes` confirmation.
        - Generated text is hashed but not retained in qualification evidence. Deterministic code, never model judgement, calculates pass or failure.
        - Provider-name conflicts fail closed; module registration order cannot select a provider.
        - Remote use requires both route permission and explicit `--allow-remote` authorization for the exact submitted content.
        - Use `cis ai route explain` to surface route, qualification, privacy, and approval reasons. Do not silently fall back to an unapproved model.
        """;

    private static string CreateMcpAdapterSkill() => """
        ---
        name: cis-mcp-adapter
        description: Expose bounded CIS graph, reference, and change context to a local MCP client. Use when configuring an editor or agent to query an initialized repository through stdio, or when reviewing whether a canonical mutation may be exposed.
        ---

        # CIS MCP adapter

        1. Start the adapter with an explicit fixed scope: `cis mcp serve --repo <initialized-repository>`.
        2. Keep the default read-only tool list for discovery and analysis.
        3. Add `--allow-mutations` only for a bounded session in which canonical or derived changes are intended.
        4. Every mutation call still requires `confirm=true` for that exact tool invocation.
        5. Use graph queries, reference inventory, and change listing before broad source reads.
        6. Treat MCP results exactly like the equivalent CIS CLI/service result; they do not grant approval or prove completion.
        7. Configure the client with the smallest necessary inherited environment. Do not expose credentials merely because stdio is local.

        The adapter accepts no per-tool repository override, uses line-delimited JSON-RPC over local stdio, and delegates to existing CIS services rather than maintaining another source of workflow logic.
        """;

    private static string CreateMcpAdapterInstruction() => """
        ---
        applyTo: "**"
        ---

        # CIS MCP adapter authority

        - An MCP process is fixed to the initialized repository passed through `--repo`; tools cannot switch repository scope.
        - Read tools are the default. Mutation tools are hidden unless `--allow-mutations` is present and must reject calls without `confirm=true`.
        - MCP delegates to CIS application services. Do not reimplement graph, reference, change, validation, or approval rules in the adapter.
        - Tool results are evidence, not human approval, acceptance, or completion authority.
        - Use local stdio. Minimize inherited environment variables and never emit credentials, prompts, or sensitive source content to protocol diagnostics.
        - Unknown methods, tools, invalid arguments, and unsupported protocol eras fail explicitly with stable JSON-RPC or tool errors.
        """;

    private static string CreateLocalArtifactRetentionSkill(string documentationRoot) => $$"""
        ---
        name: cis-local-artifact-retention
        description: Inventory, preview, compact, retrieve, restore, and clean derived CIS local artifacts. Use when `.cis/local/` grows, before archiving a completed delivery, or when older diagnostic/test evidence must be recovered.
        ---

        # CIS local artifact retention

        1. Read `{{documentationRoot}}/references/local-artifact-retention.md`.
        2. Run `cis artifacts inventory --format agent` and `cis artifacts plan --format agent`.
        3. For an `archive` family, run `cis artifacts compact --family <name> --yes` only after reviewing candidates. CIS verifies the ZIP before removing originals.
        4. Use `cis artifacts archives` and `cis artifacts retrieve --archive <id>` for non-destructive inspection.
        5. Use `cis artifacts restore --archive <id> --yes` only when original local paths should be recovered; conflicts are never overwritten.
        6. For a `delete` family, run `cis artifacts clean --family <name> --yes`; the cleanup record retains path, size, timestamp, and hash.
        7. Rerun inventory and repository doctor after storage maintenance.

        Only configured immediate entries beneath `.cis/local/` are eligible. Canonical Markdown, source, configuration, and user-managed files are outside this authority. Never manually delete the archive manifest before its ZIP.
        """;

    private static string CreateLocalArtifactRetentionInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**"
        ---

        # CIS local artifact retention authority

        - `{{documentationRoot}}/references/local-artifact-retention.md` is the canonical policy; inventory and archives under `.cis/local/artifacts/` are derived.
        - Cleanup is limited to configured immediate entries strictly beneath `.cis/local/`; canonical and user-managed paths are never eligible.
        - Preview before mutation. `compact`, `clean`, and `restore` require `--yes` for the exact operation.
        - Compaction must create and verify a content-hashed ZIP and manifest before originals are removed.
        - Retrieval is non-destructive. Restore must verify the archive digest and refuse different existing content.
        - Delete-policy cleanup retains a hash tombstone. Do not claim deleted content is recoverable from the tombstone.
        """;

    private static string CreateFrontendContextSkill(string documentationRoot) => $$"""
        ---
        name: cis-frontend-context
        description: Discover and validate framework-neutral frontend screens, routes, components, navigation, state, and API calls. Use before frontend impact analysis, design reconciliation, graph builds, or broad source searches in web, native, or Godot clients.
        ---

        # CIS frontend context

        1. Read `{{documentationRoot}}/references/repository-profile.md`, `{{documentationRoot}}/references/screen-route-map.md`, and the UI framework profile when present.
        2. Run `cis frontend discover --format agent`.
        3. Run `cis frontend validate --strict --format agent`; resolve provider conflicts, route collisions, missing source evidence, and canonical screen-route drift.
        4. Use `cis frontend inventory --framework <name>` or `--kind <kind>` for bounded routing.
        5. Rebuild and validate the graph. Frontend observations become derived screen, route, UI-component, navigation, state-store, and API-client nodes.
        6. Inspect source before making semantic claims. Adapters provide deterministic routing evidence, not full compiler proof.

        Built-in adapters cover React/Next.js, Angular, Vue, SwiftUI, Jetpack Compose, and Godot. Duplicate provider names fail closed; registration order never chooses an adapter.
        """;

    private static string CreateTemplateApplicabilitySkill(string documentationRoot) => $$"""
        ---
        name: cis-template-applicability
        description: Determine whether a repository-owned deterministic template applies before hand-writing repeated structure. Use before adding scaffolds, repetitive documentation, standard configuration, or generated UI/design assets.
        ---

        # CIS template applicability

        1. Inspect `{{documentationRoot}}/templates/` with `cis generate status --format agent`.
        2. Run `cis generate applicable --task <description> --changed-file <path> --format agent`.
        3. If a candidate applies, inspect its variables, validate it, and render with every required value.
        4. Record exactly one decision: `cis generate used`, `cis generate not applicable: <specific reason>`, or `cis generate unavailable: <specific reason>`.
        5. Treat applicability as deterministic routing advice; it does not authorize overwriting human-managed output.
        """;

    private static string CreateToolingEvidenceSkill() => """
        ---
        name: cis-tooling-evidence
        description: Record and validate exact CIS routing, generation, testing, run, and usage evidence for a changed workstream. Use before handoff, verification, or pull-request readiness checks.
        ---

        # CIS tooling evidence

        1. Add a `## CIS tooling evidence` section to the task handoff.
        2. Record exact context, graph, frontend, test-reconciliation, and run-ID evidence that applies.
        3. Record the deterministic generation decision and a specific reason when no template applies.
        4. Preserve the local usage ledger, or record a specific bounded bypass reason.
        5. Run `cis agent evidence validate --evidence <path> --changed-file <path> --strict --format agent`.
        6. Replace placeholders and resolve every warning before claiming readiness.
        """;

    private static string CreatePolicyImpactSkill() => """
        ---
        name: cis-policy-impact
        description: Analyse the repository surfaces governed by a changed policy, standard, ADR, or architecture rule. Use before creating remediation work from governance changes.
        ---

        # CIS policy impact

        1. Add explicit `targets:` front matter using backend, frontend, documentation, infrastructure, security, api, testing, verification, release, or repository-governance.
        2. Run `cis impact policy analyse --policy <path> --strict --format agent`.
        3. Review the bounded JSON and Markdown reports under `.cis/local/impact/policy/`.
        4. Use candidates as deterministic routing input, not proof that every candidate needs a code change.
        5. Create governed findings or remediation work only after reviewing target meaning and source evidence.
        """;

    private static string CreateDiagnosticsAnalysisSkill(string documentationRoot) => $$"""
        ---
        name: cis-diagnostics-analysis
        description: Validate and inspect bounded redacted text or structured diagnostics. Use when a test, browser, workflow, container, or application failure needs evidence-led diagnosis.
        ---

        # CIS diagnostics analysis

        1. Read `{{documentationRoot}}/references/diagnostics-profile.md` and run `cis diagnostics doctor --format agent`.
        2. Inspect `summary`, then narrow with `events --source --level --contains --since-minutes`.
        3. Run `analyse` to group stable redacted fingerprints without replacing raw evidence.
        4. Use `export` only for the bounded normalized JSONL handoff.
        5. Never enable a sensitive raw source; produce a sanitized repository-relative export first.
        """;

    private static string CreateToolkitEvidenceInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**"
        ---

        # CIS deterministic toolkit evidence

        - Before hand-writing repeated structure, run template applicability and record used, not-applicable, or unavailable with a specific reason.
        - Changed source work requires exact routing and reconciled test run evidence; placeholders are not evidence.
        - Policy roots declare governed `targets:`. Derived impact reports route review but cannot approve remediation.
        - Diagnostics may read only enabled, repository-relative, non-sensitive sources from `{{documentationRoot}}/references/diagnostics-profile.md`.
        - Structured diagnostics remain bounded and redacted. Fingerprints group evidence but do not replace raw test or runtime artifacts.
        - Run strict tooling-evidence validation before handoff or final verification.
        """;

    private static string CreateFrontendContextInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**/*.{ts,tsx,js,jsx,vue,swift,kt,kts,gd,tscn}"
        ---

        # CIS frontend context authority

        - Canonical screen, route, access, and destination meaning remains in `{{documentationRoot}}/references/screen-route-map.md`; `.cis/local/frontend/context.json` is derived.
        - Run frontend discovery and strict validation before graph-based frontend impact analysis.
        - Provider-name conflicts and route collisions fail closed. Module registration order is not a conflict policy.
        - Adapters cover React/Next.js, Angular, Vue, SwiftUI, Jetpack Compose, and Godot while preserving a common observation contract.
        - Derived observations and graph nodes route agents to source; they do not prove behavior, access control, accessibility, or design approval.
        - Exclude dependency, build, generated, coverage, Git, and `.cis/local/` trees.
        """;

    private static string CreateAgentExecutionSkill(string documentationRoot) => $$"""
    ---
    name: cis-agent-execution
    description: Draft one Review Required BRD or execute one approved CIS task through a discovered Codex or Claude provider with explicit evidence, permissions, isolation, durable provenance, and result reconciliation.
    ---

    # CIS agent execution

    1. For an existing implementation, dry-run `cis repo import --workspace <repository> --source <repository> --root <documentation-root>` and confirm the self-import; use `cis repo init` only for a genuinely new empty project or later reconciliation. If onboarding or command discovery fails, run `cis repo doctor` before retrying.
    2. For pre-change BRD drafting, explicitly select non-sensitive repository-owned plain-text or Word Open XML (`.docx`) reference files and run `cis agent author brd --reference <file> --provider <provider> --actor <human>`. CIS registers each source, builds stable local Markdown anchors, and reconciles the managed BRD source assessment before authoring. Then use `cis agent review brd` with a different provider, review mode, and read-only isolation; include extracted authoring evidence only after explicit disclosure authority. CIS preserves human review and approval. For delivery work, read `{{documentationRoot}}/references/agent-provider-profile.md`, the approved change plan, the selected ready task, and repository delivery policy.
    3. Run `cis agent providers --format agent` and `cis agent provider diagnose <provider> --format agent`; provider availability never grants authority.
    4. Run `cis agent prepare <change-id> <task-id> --provider <provider> --mode <plan|implement|review> --permission <read-only|workspace-write> --format agent` and inspect the bound digest and permission ceiling.
    5. Run `cis agent run` with an explicit provider, mode, permission, target, transport, actor, and reason. Workspace-write defaults to an isolated Git worktree whose baseline includes current tracked and non-ignored untracked changes.
    6. Use `--approve-requests` only when the user explicitly authorizes provider requests within the declared ceiling. Network access and paths outside the bounded workspace remain denied.
    7. Inspect durable state with `cis agent runs` and `cis agent show`; cancel with an actor and reason. Use `recover` only for a proven orphaned process before an explicit `resume`. Use `revalidate` only for a retained read-only Claude BRD review that fails solely under the corrected legacy telemetry-retention rule. Neither operation erases the original event history.
    8. Inspect the structured result before `cis agent import-result`. Imported output is evidence only; it cannot approve plans, accept designs, complete tasks, or verify delivery.

    Never copy provider credentials into CIS, infer permission from tool availability, run a non-ready task, bypass the design barrier for downstream work, or treat an agent's completion claim as canonical CIS state. Coordination, wireframe, and visual-design preparation establish the barrier and are therefore eligible before approval.
    """;

    private static string CreateAgentExecutionInstruction(string documentationRoot) => $$"""
    ---
    applyTo: "{{documentationRoot}}/changes/**/tasks/*.md"
    ---

    # CIS agent execution authority

    - `{{documentationRoot}}/references/agent-provider-profile.md` defines enabled providers, transports, isolation, timeout, and maximum permissions; credentials remain provider-native.
    - Direct execution requires an approved plan, accepted impacts, and a ready or in-progress task. The global design barrier blocks downstream work, not coordination, wireframe, or visual-design preparation needed to establish it.
    - Select provider, mode, permission, target, transport, actor, and rationale explicitly. Availability is not authorization.
    - Workspace-write execution uses an isolated Git worktree, seeded from the exact tracked and non-ignored untracked source baseline when dirty, unless the reviewed profile explicitly permits direct dirty-working-tree execution.
    - Approve provider requests only when the user authorizes per-run approval and the request is inside the declared filesystem and command ceiling. Network permission remains denied unless a future reviewed contract adds it.
    - Runs, attempts, events, permissions, process identity, artifact hashes, and results are durable derived evidence under `.cis/local/agents/runs/`.
    - Cancellation and resumption append provenance. They never discard the first attempt or rewrite canonical Markdown.
    - A provider result is untrusted evidence. Import it explicitly and preserve CIS as the only authority for plan approval, design approval, task transitions, verification, and final acceptance.
    """;

    private static string CreateDeliveryExecutionSkill(string documentationRoot) => $$"""
    ---
    name: cis-delivery-execution
    description: Execute governed CIS delivery through AI routes, deterministic templates, resumable workflows, portable agent envelopes, verification, diagnostics, and reviewed learning.
    ---

    # CIS delivery execution

    1. Read `{{documentationRoot}}/references/ai-routing-profile.md`, `{{documentationRoot}}/references/ai-model-registry.md`, the applicable change plan, task, repository delivery policy, and command manual.
    2. Prefer deterministic `cis generate` and `cis workflow` commands before model generation.
    3. Use `cis ai route explain` and the model-qualification skill before `cis ai evaluate`. Availability is not task-class approval. Never pass `--allow-remote` without explicit authorization for the exact content.
    4. Use `cis agent prepare`, `cis agent run`, and `cis agent show` for one ready task under the reviewed provider profile. Import an inspected structured result explicitly; it is evidence, not completion or approval.
    5. Record exact checks with `cis verify evidence`; run diff, compare, and validate before requesting human acceptance.
    6. Read only enabled, non-sensitive diagnostic sources. Test harnesses write bounded, redacted evidence beneath `.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/`; inspect the exact workflow attempt log before a diagnostic rerun.
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
    - Preserve the first failing workflow attempt. Inspect its live bounded log before a diagnostic rerun, and place sanitized suite evidence beneath `.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/` for reconciliation.
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
        description: Create CIS for a new empty project or import an existing repository, respect confirmation gates, and run repository doctor after onboarding errors or collisions.
        ---

        # CIS Repository Bootstrap

        ## Purpose

        Establish or reconcile CIS repository state without guessing the documentation root or bypassing human review.

        ## When to Use

        Use when onboarding a new or existing repository, adding projects to an initialized repository, upgrading CIS starters, or recovering from a failed create or import run.

        ## Inputs

        Obtain the target repository path and an explicit repository-relative documentation root chosen by the maintainer.

        ## Workflow

        1. Inspect the target without mutation. If recognized project manifests or implementation source already exist, run `cis repo import --workspace <repository> --source <repository> --root <documentation-root> --dry-run --format agent`. For a genuinely empty new project, run `cis repo init --repo <repository> --root <documentation-root> --dry-run --format agent`.
        2. Review the selected create/import mode, classification evidence, planned creates and updates, warnings, and collisions.
        3. For obsolete managed artifacts, keep the default retention unless the maintainer explicitly requests recoverable cleanup. Preview that cleanup with `--quarantine-obsolete --dry-run`; only unchanged CIS-managed files are eligible, while edited or human-owned files remain in place.
        4. Treat exit code `3` as a confirmation gate, not a failure. After maintainer review and authorization, rerun the same create/import command with `--yes`; include `--quarantine-obsolete` only for init reconciliation when its moves were also reviewed.
        5. If create or import returns exit code `2`, exit code `4`, an error, or a collision, run `cis repo doctor --repo <repository> --root <documentation-root> --format agent` immediately.
        6. Use doctor evidence and suggested fixes to explain the blocker. Apply no suggested fix without the required human review.
        7. Rerun the same onboarding command after the blocker is resolved and confirm that the final plan is applied or unchanged.

        ## Output Expectations

        Report the chosen root, create/import mode, status and exit code, doctor findings when onboarding failed, applied changes, unresolved collisions, and the exact next action.

        ## Guardrails

        Do not choose a documentation root silently, use `--yes` before reviewing a non-empty root, quarantine edited or human-owned files, overwrite quarantine destinations or collisions, execute target-repository assemblies, or treat doctor suggestions as automatic authorization.

        ## Related Files

        Read `.cis/repository.yml` when it exists and use the `cis repo init`, `cis repo import`, and `cis repo doctor` command manuals from the CIS distribution.
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
        description: Import, initialize, register, list, graph, or validate one or several existing repositories as a CIS workspace. Use for standalone or multi-repository onboarding and repeatable workspace reconciliation without copying source repositories.
        ---

        # Import CIS Repositories

        ## Inputs

        Obtain the workspace path, every source repository path, and one explicit repository-relative documentation root approved for the import batch.

        ## Workflow

        1. Run `cis repo import --workspace <workspace> --source <repository>... --root <documentation-root> --dry-run --format agent`. For a standalone existing repository, use that same path for workspace and source so import bootstraps it as authority.
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
        5. To delegate the initial draft, select explicit non-sensitive plain-text or Word Open XML (`.docx`) references and run `cis agent author brd --reference <file> --provider <provider> --actor <human>`. Word text extraction is bounded and does not execute embedded content. Review the one-file result; agent drafting cannot alter frontmatter or managed blocks and grants no approval.
        6. After implementation graph rebuilds, run `cis technical-intent refresh --workspace <workspace> --format agent` before starting the next feature. Do not request renewed BRD, technical-intent, or backlog approval when this safe refresh succeeds. If its BRD stage blocks on new or materially changed source evidence, use `cis brd reconcile --workspace <workspace> --format agent`, review the exact semantic delta, and request only the authority that delta requires.
        7. Review every source row. Set Assessment to `Adopted`, `Reference`, or `Rejected` and record rationale. For an Adopted feature specification, incorporate its business intent into the relevant BRD sections and cite its `BRD-SRC-*` ID in Traceability.
        8. When an agent drafted the BRD and another review-capable provider is available, run `cis agent review brd --provider <different-provider> --actor <human>`. Include extracted authoring evidence only with explicit disclosure authority. Treat the structured findings as advisory evidence, never approval or stakeholder answers.
        9. If review findings exist, run `cis brd review init <review-run-id>` and present each independently. Let the human approve it as written or provide exact edited remediation text; record that text and actor with `cis brd review decide` without inventing a separate acceptance rationale or aggregate approval. The final individual decision mechanically locks the exact set. Use `cis brd review approve` only to recover a complete legacy set that predates automatic locking.
        10. Apply approved recommendations with `cis agent revise brd --review <review-run-id> --provider <provider-different-from-reviewer> --actor <human>`. Legacy rejected findings remain guardrails. After the bounded one-file revision, run `cis agent review brd` through a provider different from the reviser for secondary review.
        11. Run `cis brd questions guidance` and present all unresolved `BRD-Q-*` questions with bounded canonical context. `cis brd questions suggest` may generate digest-bound local advisory answers; use remote generation only with explicit disclosure authority. Only answers with at least medium model confidence and valid citations to the displayed context are supported; low-confidence, uncited, malformed, or insufficient output must remain absent. Record only a suggestion explicitly accepted by the human or the human's edited text with `cis brd questions answer <id> --answer <answer> --actor <human>`. Never treat generation as stakeholder authority. Partial progress may be preserved. After an answer, use `cis brd review freshness <review-run-id>`; `question-answers-only` avoids redundant review while resolution is incomplete, while `stale` requires another review.
        12. When all questions are answered, run `cis agent incorporate brd-questions --provider <implement-provider> --actor <human>` to reflect the exact digest-bound decisions in relevant business sections while preserving the question table and managed evidence. Then run `cis agent review brd` through a provider different from the question reviser. Never route directly from answered questions to approval.
        13. Rebuild workspace graphs after canonical edits, then run `cis brd validate` and `cis brd status`.
        14. Present validation errors, warnings, participant baseline drift, unresolved sources, unanswered questions, independent-review findings, and approval readiness to the user.
        15. Run `cis brd approve --reviewer <human> --reason <rationale>` only after the user explicitly authorizes that exact approval. Rebuild the authority graph after approval.
        16. After technical intent is Active/current, run `cis solution-design init`. Review and approve the overall architecture and component sheet as one bundle; UI-facing design follows as a distinct stage within those boundaries.
        17. After the solution-design bundle is Active/current, capture and approve high-level UI direction through `cis ui-direction questions init`, `cis ui-direction init`, validation, and explicit human approval. Then run `cis brd backlog build`. Accept governed functional requirements expressed as canonical `BRD-FR-*` table rows or bold `BR-FR-*` narrative bullets; do not rewrite or reapprove the BRD solely to change between those presentation forms. Route to participants in a multi-repository workspace and to the authority repository when it is the only greenfield repository. Review one-to-one functional-requirement coverage, normalized `HLT-FR-*` identities, repository routing, frontend types, dependencies, and global obligations, then validate and present approval readiness.
        18. Run `cis brd backlog approve --reviewer <human> --reason <rationale>` only with explicit authority. An approved high-level item may then become a feature specification; it is not an implementation task.
        19. Start only a dependency-ready item with `cis brd backlog start --item <HLT-ID>`. Expand the generated scaffold manually or through `cis agent author feature --item <HLT-ID> --provider <provider> --actor <human>`, then run `cis brd feature validate --item <HLT-ID>` until it is Ready for Approval.
        20. Present the exact feature scope, validation result, and approval rationale. Run `cis brd feature approve --item <HLT-ID> --reviewer <human> --reason <rationale>` only with explicit human authority.
        21. After feature approval, rebuild the graph and reconcile the now-eligible feature evidence into the BRD. Use `cis technical-intent refresh`; renew downstream authority only when it reports an actual semantic change.

        ## Guardrails

        Never infer currency from file existence, choose source or review-finding dispositions, claim semantic absorption without updating BRD content and traceability, invent business requirements, approve on a user's behalf, or describe Review Required, Ready for Approval, or Stale content as Active. Agents cannot accept, reject, or approve findings. Do not edit managed candidate IDs, approval hashes, source hashes, baseline rows, review-disposition blocks, or block markers. CIS may mark an approved BRD, backlog, or feature Stale from content/evidence drift; only explicit human approval may restore Active status. Feature approval accepts scope but never authorizes implementation.
        """;

    private static string CreateGovernTechnicalIntentSkill() => """
        ---
        name: cis-govern-technical-intent
        description: Capture high-level technical choices, then initialize, review, approve, or recheck the canonical workspace technical intent derived from an Active BRD, questionnaire, standards, classifications, and exact graph baselines. Use after BRD approval and before change-dossier creation or bounded planning.
        ---

        # Govern Technical Intent

        ## Workflow

        1. Confirm `cis brd status --workspace <workspace>` reports Active, valid, and current.
        2. Run `cis technical-intent questions init`. For an existing implementation, retain evidence-supported `Derived` answers with confidence and provenance; present only unresolved or intentionally overridden choices for human input. For a greenfield project, present the complete `TI-Q-*` set. Record each human answer with `cis technical-intent questions answer`; advisory directions are not answers.
        3. Build and strictly validate the workspace graph.
        4. Run `cis technical-intent init --workspace <workspace> --format agent` to generate or reconcile the deterministic scaffold from the completed questionnaire, Active BRD, classifications, standards, and graph builds.
        5. Review the generated choices, component map, BRD-derived `TI-MOD-*` module tree and responsibility profiles, `TI-INT-*` integration-point catalog, business baseline, technical surface, standard provenance, and architecture guidelines without broadening the BRD or silently contradicting a human answer.
        6. Confirm that each business capability has one owning module; each module states purpose, BRD coverage, owned and excluded responsibilities, inputs, outputs, data/state, security, failure/recovery, and verification. Merge or split candidates only with preserved requirement traceability and explicit ownership migration.
        7. Confirm every cross-module, external-system, identity, persistence, model, event/file, operator, and runtime handoff records owner, direction, contract/data, authentication and authorization, compatibility, consistency, idempotency, timeout/retry, failure/recovery, observability, and test expectations. Exact endpoints and schemas belong in linked canonical reference dictionaries.
        8. Complete architecture boundaries, data and consistency, contracts, security/privacy, operations/recovery, quality evidence, and delivery constraints. Preserve substantive human-authored sections.
        9. Record later bounded unresolved choices in `Open technical decisions` with stable `TI-DEC-*` identity, gate, status, and rationale. Agents may propose options but never select or defer them without explicit human authority.
        10. Run `cis technical-intent validate` and `status`. Resolve every placeholder, unresolved decision, stale questionnaire/BRD/standard digest, and participant-baseline drift.
        11. Run `cis technical-intent approve --reviewer <human> --reason <rationale>` only after explicit authority.
        12. After approved implementation changes participant graph IDs, run `cis technical-intent refresh`; request renewed approval only for material semantic change.
        13. Rebuild and strictly validate the workspace graph after approval.
        14. Run `cis solution-design init`; review and approve the overall architecture and component sheet as one bundle. Then complete and approve `cis ui-direction` before backlog, feature design, change, or plan commands.

        ## Guardrails

        Preserve managed baseline and derived-evidence markers and identities. Do not turn ambiguous evidence or absence of code into a derived fact, record advisory questionnaire text without human action, treat Draft, Ready for Approval, or Stale as Active, approve on a user's behalf, edit approval hashes, or bypass the readiness gate.
        """;

    private static string CreateGovernSolutionDesignSkill() => """
        ---
        name: cis-govern-solution-design
        description: Generate, refine, validate, and approve the atomic overall solution design and component sheet after technical-intent approval and before backlog or UI-facing design work.
        ---

        # Govern Overall Solution Design

        1. Confirm `cis technical-intent status --workspace <workspace>` is Active, valid, and current.
        2. Run `cis solution-design init --workspace <workspace> --format agent`.
        3. Review `architecture/overall-solution-design.md` for system context, logical topology, data ownership, integration, trust, deployment, recovery, verification, traceability, and a bounded UI-design handoff.
        4. Review `references/component-sheet.md` for one stable `TI-MOD-*` row per logical component, explicit responsibility and ownership, explicit exclusions, and BRD authority.
        5. Do not equate a logical component with a repository, process, service, or deployment unit unless the approved topology says so. Keep cross-component access behind owned `TI-INT-*` contracts.
        6. Record substantive refinements outside managed blocks or through an upstream technical-intent decision. Use an ADR for a durable exception.
        7. Run `cis solution-design validate` and resolve every structural, traceability, collision, drift, and bundle-integrity finding.
        8. Present both files as one review point. Run `cis solution-design approve --reviewer <human> --reason <rationale>` only with explicit human authority.
        9. Rebuild the graph after approval. Continue with `cis ui-direction questions init` and `cis ui-direction init` for the shared shell, navigation, reusable interaction patterns, accessibility, and visual direction. Detailed screens remain feature-level work.

        Never approve per component, invent business scope, overwrite human sections on rerun, bypass stale source evidence, or treat generated content as Active.
        """;

    private static string CreateGovernUiDirectionSkill() => """
        ---
        name: cis-govern-ui-direction
        description: Capture, generate, validate, and approve the workspace-level UI look and feel after solution-design approval and before high-level backlog or feature design.
        ---

        # Govern High-Level UI Direction

        1. Confirm `cis solution-design status --workspace <workspace>` reports Active, valid, and current.
        2. Run `cis ui-direction questions init --workspace <workspace>`. Preserve evidence-supported Derived choices from approved surfaces and UI-framework profiles; leave subjective product character, shell, visual language, density, accessibility, state, and constraint choices for a human.
        3. Present the full `UI-Q-*` set in context. Record only the human's accepted or edited direction through `cis ui-direction questions answer <id> --answer <text> --actor <human>`; an advisory starting direction is not authority by itself.
        4. Run `cis ui-direction init` after every question is resolved. Review `design/ui-direction.md` as the common authority for surfaces, product character, shell/navigation, tokens, density, typography/content, reusable components, state/feedback/motion, responsive adaptation, accessibility, and exclusions.
        5. Preserve the evidenced UI framework already used by an existing product. When none exists, use the platform-specific default in `references/ui-framework-profile.md`; do not install or replace packages during this stage.
        6. Keep this artifact high level. Feature textual wireframes later define screen structure, actions, paths, and states; deterministic Sharp/SVG packs prove each feature's visual interpretation. Neither may silently contradict the active direction.
        7. Run `cis ui-direction validate`, resolve provenance or completeness findings, and present the exact document once. Run `cis ui-direction approve --reviewer <human> --reason <rationale>` only with explicit human authority.
        8. Rebuild the graph after approval. A solution-design, questionnaire, design-guideline, UI-framework-profile, or approved-content change makes the direction stale and blocks backlog and feature delivery until reconciled.

        Never derive subjective brand choices from weak code markers, approve for the user, redraw shared controls per feature, or treat a generated document as Active.
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
        2. Start or locate the feature with `cis brd backlog start --item <high-level-item>` and `cis brd feature status --item <high-level-item>`; do not invent a second source of truth.
        3. Replace a generated scaffold through bounded agent authoring with `cis agent author feature --item <high-level-item> --provider <provider> --actor <human>`, or complete it manually. Agent authoring may edit only the feature file and cannot approve it.
        4. Specify actors, outcomes, business rules, state transitions, contracts, authorization and non-disclosure, caching, persistence, migration/recovery, accessibility, observability, rollout, exclusions, and measurable acceptance criteria proportionate to the feature. Use only the controlled surfaces `frontend`, `backend`, `full-stack`, `mobile`, `native`, `api`, `contract`, `data`, `security`, `delivery`, or `documentation`, and frontend types `public`, `customer`, `backoffice`, or `not-applicable`; put descriptive boundary names in requirement text.
        5. Route frontend scope as public, customer, or backoffice. Describe required user journeys and review points without selecting unapproved visual implementation details.
        6. Define layered test obligations: unit, component, integration, business, architecture, frontend component, browser, security, operational, coverage, and mutation where applicable. Credential-dependent provider smoke may be explicitly unavailable; it is never silently passed.
        7. Run `cis brd feature validate --item <high-level-item>`. Resolve structural, currency, traceability, and cross-document findings before requesting review.
        8. Only after explicit human authorization run `cis brd feature approve --item <high-level-item> --reviewer <human> --reason <rationale>`.
        9. A material BRD or technical-intent change makes the feature stale and requires reconciliation. Mechanical validation with no semantic change adds no approval gate.

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
        7. Run `cis references discover` and preview safe additive canonical updates with `cis references reconcile`. Apply them with `--yes` only after review.
        8. Run `cis references validate --strict` and `cis references diff --base <delivery-baseline>` for non-API references.
        9. Run strict documentation validation, deterministic OpenAPI export, and forward-transitive API diff against every supported baseline in the same major version.

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

    private static string CreateReferenceGovernanceInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**/*.{cs,fs,vb,ts,tsx,js,jsx,swift,kt,kts,json,yml,yaml,csproj,fsproj,vbproj,props,targets,toml,md}"
        ---

        # CIS reference governance instructions

        Before changing a configuration key, permission, command, event, workflow state,
        invariant, projection, Problem Details identity, package, route, module boundary,
        entity, or relationship, inspect its canonical row beneath `{{documentationRoot}}/references/`.

        After changing a governed surface:

        1. Run `cis references discover --repo .`.
        2. Preview additive canonical rows with `cis references reconcile --repo .`; apply with `--yes` only after review.
        3. Run `cis references validate --repo . --strict` and resolve every finding.
        4. Run `cis references diff --repo . --base <delivery-baseline>`.
        5. Update canonical Markdown only from reviewed facts; never copy derived state into it blindly.
        6. If discovery fails, run `cis repo doctor --repo .`.

        Do not edit `.cis/local/references/`, resolve provider conflicts by load order, or remove
        a canonical row merely because deterministic source discovery did not find it.
        """;

    private static string CreateCiInvestigationSkill() => """
        ---
        name: cis-ci-investigation
        description: Inspect remote CI checks, runs, jobs, redacted logs, artifacts, and failure classifications before proposing a focused fix or explicitly confirmed rerun.
        ---

        # CIS CI investigation

        1. Resolve the exact repository and PR or run identity; do not investigate an inferred target without checking it.
        2. Use `cis ci status --pr <number>` or bounded `cis ci runs` before fetching logs.
        3. Inspect jobs and artifact metadata. Download logs only for the failed jobs needed for diagnosis.
        4. Use `cis ci diagnose --run <id>` and preserve the first-failure classification and evidence path.
        5. Use `cis ci reproduce --run <id>` to select a focused repository-owned local workflow.
        6. Treat remote failures as evidence, not as permission to change scope, tests, gates, or policy.
        7. `cis ci rerun-failed --run <id> --yes` is a remote mutation and requires explicit authorization.

        Never print credentials, upload local evidence, erase the first failing attempt, or claim a rerun passed before retrieving its new result.
        """;

    private static string CreateCiInvestigationInstruction() => """
        ---
        applyTo: ".github/workflows/**/*.{yml,yaml}"
        ---

        # CIS CI investigation instructions

        Use `cis ci` for remote CI evidence. Start with status/runs, retrieve only the
        necessary failed jobs and logs, and preserve bounded redacted evidence beneath
        `.cis/local/ci/`. Remote state cannot approve or complete CIS work. Never run
        `rerun-failed --yes` without explicit authorization for that remote mutation.
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
        5. Run `cis references discover`, `cis references validate --strict`, and baseline `cis references diff` before handoff.
        6. Validate documentation and implementation evidence before handoff.

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

    private static string CreateSecurityTestingSkill(string documentationRoot) => $$"""
        ---
        name: cis-security-testing
        description: Run and reconcile portable security scanners, inspect redacted evidence, optionally obtain local-only AI triage, and preserve deterministic release gates.
        ---

        # CIS Security Testing

        1. Read `{{documentationRoot}}/standards/security-testing-standard.md` and `{{documentationRoot}}/references/security-suite-profile.md`.
        2. Run `cis security validate --strict` and `cis security exceptions validate --strict`.
        3. Execute the repository workflow once and retain first-failure logs.
        4. Run `cis security reconcile --run <workflow-run-id>` and inspect unresolved exact findings.
        5. Run `cis security summarise --run <workflow-run-id>` for advisory local triage, or add `--no-llm`.
        6. Fix findings and use a distinct run; never overwrite prior evidence.
        7. Before release, prove revision freshness and exact scanned image identity.

        Scanner evidence and governed exceptions decide the verdict. Never reveal secret values, send findings to a remote model, or create wildcard, indefinite, self-approved, or model-approved exceptions.
        """;

    private static string CreateSecurityTestingInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "**"
        ---

        # CIS security testing

        - Read `{{documentationRoot}}/standards/security-testing-standard.md` before security-significant work.
        - Keep scanner results and summaries beneath `.cis/local/security/`; canonical profiles and exceptions remain Markdown.
        - Treat missing declared output as invalid evidence even when the scanner process exits successfully.
        - Never preserve raw secret material in reports, logs, model prompts, snapshots, or committed fixtures.
        - Local AI security summaries are advisory only and cannot change scanner severity, exceptions, or verdicts.
        - Exceptions require exact scanner and fingerprint, reason, expiry, owner, explicit human approver, and approval reference.
        - Scan the exact releaseable image identity before promotion and do not rebuild it afterward.
        """;

    private static string CreateRepositoryInstruction(string documentationRoot) =>
        "---\napplyTo: \"**\"\n---\n\n" +
        "# CIS repository guidance\n\n" +
        "- When onboarding or reconciling a repository, use `.github/skills/cis-repository-bootstrap/SKILL.md`; import existing source and initialize only a genuinely new empty project.\n" +
        "- When importing one or several existing repositories, use `.github/skills/cis-import-repositories/SKILL.md`; dry-run the complete selection before confirmation.\n" +
        "- For BRD intake or currency review, use `.github/skills/cis-govern-business-requirements/SKILL.md`; discovery never proves currency and approval is human-only.\n" +
        "- After BRD approval and before change dossiers, use `.github/skills/cis-govern-technical-intent/SKILL.md`; technical decisions and approval remain human-authority actions.\n" +
        "- Run `cis repo import` for existing source or `cis repo init` for a new empty project with an explicit maintainer-selected `--root`; if onboarding returns an error or collision, run `cis repo doctor` with the same `--repo` and `--root`.\n" +
        "- Read `.cis/repository.yml` before repository-wide work.\n" +
        $"- Start with `{documentationRoot}/README.md`, `{documentationRoot}/catalog.yml`, and the repository profile before broad searches.\n" +
        $"- Treat `{documentationRoot}/specs/product-intent-spec.md` and `{documentationRoot}/specs/technical-intent-spec.md` as foundational intent documents.\n" +
        $"- Read `{documentationRoot}/specs/repository-delivery-policy-spec.md` before any branch, commit, push, pull-request, merge, tag, release, or remote-issue action; Draft or unresolved policy means local-only unless the user explicitly authorizes the exact action.\n" +
        "- Use the smallest matching `cis-*` skill before inventing a repository workflow.\n" +
        "- Before governed implementation, use `.github/skills/cis-standards-governance/SKILL.md` and `cis standards applicable` for every affected target and stack; model advice is never conformance proof.\n" +
        "- Use `.github/skills/cis-skill-governance/SKILL.md` after initialization, classification changes, imports, or skill edits; audit is local-first with configured remote fallback, and only explicit `--fix` authorizes reversible quarantine.\n" +
        "- Use `.github/skills/cis-file-index/SKILL.md` and `cis index find` before broad source searches; cards are non-authoritative and disposable.\n" +
        "- Use `.github/skills/cis-feedback-loop/SKILL.md` to review automatic local usage, possible token savings, repeated failures, and compact-output opportunities.\n" +
        "- Use `.github/skills/cis-security-testing/SKILL.md` for portable SAST, secret, filesystem, configuration, image, and DAST evidence; model summaries are local-only and advisory.\n" +
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
        - A controller may delegate a Review Required draft with `cis agent author brd --reference <file> --provider <provider> --actor <human>`. CIS reads bounded plain text or extracts bounded text from Word Open XML (`.docx`), binds the original selected evidence by digest, runs in an isolated scratch repository, and applies only an exact one-file BRD diff whose frontmatter and managed blocks remain unchanged.
        - After an agent-authored draft, prefer a different review-capable provider through `cis agent review brd`. Keep the run read-only and isolated, disclose extracted authoring references only with explicit authority, and treat every finding as advisory evidence rather than approval or stakeholder fact.
        - For review findings, use `cis brd review init/status/decide`. Require a named human to approve each recommendation as written or with exact edited text, without a separate rationale or aggregate approval. The final decision mechanically locks the set; `cis brd review approve` is legacy recovery only. Agents never receive disposition authority.
        - Apply each finding's exact approved recommendation with `cis agent revise brd --review <run-id>` through a provider different from the reviewer. Preserve legacy rejected findings as guardrails and require a secondary review through a provider different from the reviser.
        - Use `cis brd questions guidance` to expose recognized open questions with bounded canonical context. `cis brd questions suggest` may generate digest-bound local advisory answers, or use a remote provider only after explicit disclosure authority. Only answers with at least medium model confidence and valid citations to the displayed context are supported; low-confidence, uncited, malformed, or insufficient output must produce no answer. Record only a suggestion explicitly accepted by a named human or that human's edited text through `cis brd questions answer`. Never treat generation as stakeholder authority; preserve partial progress. `question-answers-only` review freshness avoids redundant review while questions remain. After every answer exists, run `cis agent incorporate brd-questions` and require a different-provider independent review of the resulting BRD revision before validation and approval.
        - Preserve CIS baseline and source block markers, candidate IDs, source and approval hashes, and participant graph identities.
        - After implementation graph rebuilds, run `cis technical-intent refresh`. Pure managed-baseline drift preserves the original BRD, technical-intent, and backlog approvals. If its BRD stage blocks, use `cis brd reconcile` to expose the exact source/semantic delta and request review only for that delta.
        - `Adopted` feature specifications must be incorporated into relevant BRD sections and cited by their managed source ID in Traceability.
        - Agents may discover, draft, propose reconciliation edits, reconcile managed evidence, and validate. They may not select source assessments, invent stakeholder decisions, claim semantic absorption without corresponding BRD edits, or run `cis brd approve` without explicit user authorization for the reviewer and rationale.
        - `Active` requires recorded human approval. Evidence drift may reduce it to `Stale`; automation may never restore `Active`.
        - After BRD and technical-intent approval, use `cis solution-design init` and approve the overall architecture plus component sheet as one bundle. Then capture and approve the high-level UI direction before `cis brd backlog build`; do not jump directly from the BRD to executable tasks.
        - High-level backlog approval authorizes feature-specification preparation only; use `cis brd backlog start --item <HLT-ID>` for dependency-ready items, expand the scaffold manually or through `cis agent author feature`, and run `cis brd feature validate --item <HLT-ID>` before review.
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
        - Use structured `FEAT-*` requirements with a bounded surface, frontend type, testable requirement, and acceptance criteria. Surface must be exactly `frontend`, `backend`, `full-stack`, `mobile`, `native`, `api`, `contract`, `data`, `security`, `delivery`, or `documentation`; frontend type must be exactly `public`, `customer`, `backoffice`, or `not-applicable`.
        - Complete every required section explicitly; use a reasoned `Not applicable` statement rather than a placeholder.
        - A generated scaffold is not a feature draft. Use `cis agent author feature --item <HLT-ID> --provider <provider> --actor <human>` for isolated evidence-bounded expansion, or author it manually, before validation.
        - Run `cis brd feature validate --item <HLT-ID>` and present the exact scope and validation result before requesting approval.
        - Never run `cis brd feature approve` without explicit human reviewer identity and rationale. Approval accepts scope and may be carried by `cis plan derive` only through eligible deterministic impacts and the exact validated plan; it does not directly authorize implementation or release.
        - After approval, rebuild the graph and reconcile the feature as BRD evidence before change planning.
        - For the reconciled current feature, prefer `cis plan derive` and do not request separate impact or plan approvals when it succeeds. Surface only exceptional low-confidence, deferred, truncated, conflicting, stale, or invalid results for human review.
        """;

    private static string CreateTechnicalIntentInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/specs/technical-intent-*.md"
        ---

        # CIS technical intent authority

        - The workspace authority owns one canonical workspace-scoped technical intent; participant repositories retain repository-scoped supporting intent documents.
        - An Active/current BRD, completed/current governed technical questionnaire, and fresh participant graphs are required before technical-intent initialization or approval.
        - Existing implementations derive only evidence-supported technical facts with confidence and repository provenance; ambiguous choices remain human questions. Greenfield projects require human answers for every direction. Advisory starting directions become authority only through an explicit human answer.
        - Initialization creates a Draft from questionnaire answers, the BRD, classifications, graph, and Active standards, including logical components, BRD-derived product modules, detailed responsibility profiles, and stable integration points.
        - Review every `TI-MOD-*` candidate against the module completeness fields: purpose, BRD authority, ownership and exclusions, inputs, outputs, data/state, security/policy, failure/recovery, and verification. Do not equate a module with a deployable unless topology requires it.
        - Review every `TI-INT-*` handoff for source, trigger, target, contract/data, delivery/consistency, trust/authorization, failure/recovery, observability, compatibility, and test evidence. Refine exact operations in the owning API, event, permission, data, or operational reference dictionary.
        - Preserve the baseline, business, questionnaire, component-map, module-architecture, integration-point, technical-surface, standards, and decision-evidence markers, their source digests, stable identity, schema, and approval digest.
        - Preserve substantive human-authored sections on rerun; CIS upgrades only recognized untouched starter sections.
        - Complete every required technical section and keep unresolved choices in the structured `TI-DEC-*` table.
        - Agents may draft technical direction and options. Only explicit human authority may resolve or defer decisions and run `cis technical-intent approve` with the exact reviewer and rationale.
        - After approval, project the intent through `cis solution-design init` and approve the overall design plus component sheet as one bundle. Then capture and approve `cis ui-direction`; downstream backlog, change, and plan work requires all three authorities to remain Active and current.
        - `cis change create`, `cis plan build`, `cis plan import-spec`, and `cis plan derive` are blocked in a workspace authority unless technical intent, overall solution design, and high-level UI direction are Active and current.
        - Rebuild the workspace graph after canonical edits or approval. Questionnaire, BRD, participant-baseline, or approved-content drift makes the intent non-current.
        """;

    private static string CreateSolutionDesignInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/{architecture/overall-solution-design.md,references/component-sheet.md}"
        ---

        # CIS overall solution-design authority

        - Treat the overall design and component sheet as one atomic review and approval bundle.
        - Preserve stable identity, managed markers, technical-intent hash, `TI-MOD-*` component IDs, and approval metadata.
        - Keep the architecture implementation-independent: components are logical ownership boundaries unless deployment is explicitly approved.
        - Give every component one clear responsibility, owned concerns, exclusions, and BRD traceability. Cross-component access uses an explicit owned integration contract.
        - Cover context, topology, data consistency, integrations, security, operations/recovery, verification, traceability, and the UI-design handoff.
        - Do not add business requirements or contradict the Active technical intent. Promote durable changes through technical intent or an ADR first.
        - Run `cis solution-design validate` before asking for one whole-bundle approval. Agents may not approve on the user's behalf.
        - An upstream or bundle-content change makes both artifacts stale; rerun `cis solution-design init`, review the delta, and renew the one bundle approval.
        """;

    private static string CreateUiDirectionInstruction(string documentationRoot) => $$"""
        ---
        applyTo: "{{documentationRoot}}/{specs/ui-direction-questionnaire.md,design/ui-direction.md}"
        ---

        # CIS high-level UI-direction authority

        - Require an Active/current overall solution-design bundle before capturing or generating UI direction.
        - Preserve stable identity, managed markers, questionnaire and source hashes, design-guideline and UI-framework-profile provenance, and approval metadata.
        - Derive only objective facts supported by approved architecture or repository evidence. Product character, shell, visual language, density, accessibility, and experience constraints remain explicit human choices.
        - Preserve an existing evidenced design system. When none exists, follow the classification-selected framework in `{{documentationRoot}}/references/ui-framework-profile.md` and the Active `{{documentationRoot}}/specs/design-guidelines.md` baseline.
        - Treat shell, navigation, tokens, common controls, feedback, responsive behavior, and accessibility as reusable product contracts. Do not redraw common buttons, fields, menus, tables, lists, dialogs, or states inside individual features.
        - High-level direction contains no detailed feature screens or PNG approvals. A feature wireframe defines structure, actions, paths, and states; its deterministic Sharp/SVG pack remains a later human review point.
        - Run `cis ui-direction validate` before requesting approval. Only explicit human authority may run `cis ui-direction approve`.
        - Source or approved-content drift makes UI direction stale and blocks backlog, change, and plan work until it is reconciled and reapproved.
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
