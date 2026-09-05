using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Brd;
using Cis.Modules.Repository;

namespace Cis.Modules.TechnicalIntent;

public sealed class TechnicalIntentQuestionnaireService
{
    private static readonly IReadOnlyList<QuestionDefinition> Definitions =
    [
        new("TI-Q-001", "Product surfaces", "Which product surfaces are in scope?", "The architecture must distinguish user-facing, machine-facing, and operational entry points before components are selected.",
            ["Public web", "Authenticated customer web", "Backoffice web", "iOS", "Android", "Desktop", "API", "Background workers", "External integrations"],
            "Start with only the surfaces required by the Active BRD; classify each frontend as public, customer, or backoffice and record mobile or machine-facing surfaces separately."),
        new("TI-Q-002", "Frontend technology", "What frontend technology should each user-facing surface use?", "Framework, rendering model, accessibility tooling, and supported platforms shape component ownership and verification.",
            ["Existing repository framework", "React / Next.js", "Angular", "Vue / Nuxt", "SwiftUI", "Jetpack Compose", ".NET MAUI", "No frontend"],
            "Reuse a detected framework when one exists. Otherwise choose one maintained framework per surface and avoid a second UI stack without a distinct platform need."),
        new("TI-Q-003", "Backend technology", "What language, runtime, and application framework should own server-side behavior?", "This establishes the application boundary, package ecosystem, deployment unit, and compiler-backed architecture rules.",
            [".NET / ASP.NET Core", "TypeScript / Node.js", "Java / Spring Boot", "Kotlin / Ktor", "Python / FastAPI", "Go", "No backend"],
            "Reuse the strongest detected backend stack. For a new product, select one primary runtime based on team capability and operational fit."),
        new("TI-Q-004", "Architecture style", "What high-level architecture style should the product use?", "The first technical intent should fix dependency direction and deployment boundaries without prematurely designing every component.",
            ["Modular monolith", "Microservices", "Serverless functions", "Event-driven services", "Client-only application", "Extend existing architecture"],
            "Default to a modular monolith with explicit module boundaries; split a service only for independent ownership, scaling, security, or deployment needs."),
        new("TI-Q-005", "Code and ownership topology", "How should components be divided across repositories and deployable units?", "Repository layout should follow ownership and release boundaries rather than create accidental distributed-system complexity.",
            ["Single repository and deployable", "Monorepo with multiple deployables", "Separate frontend/backend/infra repositories", "Service-per-repository", "Extend current repository topology"],
            "Keep the smallest topology that supports independent ownership and release. Record one owner for every component and deployable."),
        new("TI-Q-006", "Primary data store", "What should be the primary transactional system of record?", "Consistency, migrations, concurrency, backup, restore, and operational cost depend on this choice.",
            ["SQLite", "PostgreSQL", "SQL Server", "MySQL/MariaDB", "Managed document database", "No durable application data"],
            "Use the simplest transactional store that satisfies concurrency, scale, hosting, and recovery needs; keep business policy storage-neutral where practical."),
        new("TI-Q-007", "Supporting data services", "Which additional data services are required?", "Caches, search, object storage, analytics, and graph stores add separate ownership and recovery obligations.",
            ["None", "Distributed cache", "Search index", "Object/blob storage", "Graph database", "Analytics warehouse", "Vector store"],
            "Add no supporting store until a BRD outcome or measurable quality constraint requires it; every derived store needs a rebuild path."),
        new("TI-Q-008", "Contracts and integrations", "How will clients, components, and external systems communicate and evolve contracts?", "Interaction style determines compatibility, failure, authentication, idempotency, and test boundaries.",
            ["Versioned HTTP/JSON APIs", "GraphQL", "gRPC", "Events/messages", "Files/batches", "In-process module contracts", "Platform-native SDKs"],
            "Use in-process contracts inside a deployable and versioned forward-transitive contracts across deployable or external boundaries unless a different compatibility policy is stated."),
        new("TI-Q-009", "Identity and access", "What identity, authentication, authorization, and tenancy model is required?", "Identity is a trust boundary and must be selected before exposing components or data flows.",
            ["No identity", "Managed OIDC/OAuth", "Self-hosted identity provider", "Enterprise SSO", "Anonymous/local identity", "Single tenant", "Multi-tenant"],
            "Use a maintained standards-based identity provider when authentication is required; enforce authorization and object ownership at the trusted backend or platform boundary."),
        new("TI-Q-010", "Hosting and deployment", "Where and how will the product run?", "Runtime topology defines networks, secrets, configuration, availability, release, cost, and recovery responsibilities.",
            ["Local/self-hosted containers", "Provider-neutral containers", "Managed cloud application platform", "Kubernetes", "Serverless", "Mobile stores", "Static hosting", "Hybrid"],
            "Stay provider neutral at product level unless a requirement selects a provider; define local development, test, staging, and production topology separately."),
        new("TI-Q-011", "Asynchronous work", "Are background jobs, queues, scheduled work, or domain events required?", "Asynchrony changes ordering, retry, idempotency, dead-letter, replay, and observability behavior.",
            ["None", "In-process background jobs", "Durable queue", "Event bus", "Scheduled jobs", "Workflow engine"],
            "Keep work synchronous unless latency, reliability, fan-out, or scheduling requires an asynchronous boundary; then define delivery and recovery semantics explicitly."),
        new("TI-Q-012", "Operations and observability", "What operational model, telemetry, and support expectations apply?", "The architecture must be diagnosable and recoverable, including during automated test and delivery runs.",
            ["Structured logs", "Metrics", "Distributed tracing", "Health/readiness", "Alerts/dashboards", "Audit trail", "Support runbooks"],
            "Use structured privacy-safe logs, truthful health/readiness, boundary metrics, retained test diagnostics, and runbooks proportionate to the runtime topology."),
        new("TI-Q-013", "Security, privacy, and compliance", "Which data classifications, threats, privacy duties, or compliance constraints shape the design?", "These constraints can change trust boundaries, storage, retention, audit, hosting, and assurance.",
            ["Public data", "Personal data", "Sensitive/regulated data", "Secrets", "Financial data", "Health data", "Data residency", "No special compliance"],
            "Classify data and actors explicitly, minimize retained sensitive data, use least privilege and default deny, and record any applicable regulatory or residency boundary."),
        new("TI-Q-014", "Quality and assurance", "Which quality attributes and verification layers are required?", "Performance, reliability, accessibility, security, cost, and maintainability need measurable direction before detailed plans are produced.",
            ["Unit", "Component", "Integration", "Business acceptance", "Architecture", "Accessibility", "Security", "Performance", "Mutation", "Operational recovery"],
            "Select layered tests by risk, require deterministic architecture and security checks, and retain exact run evidence; define measurable budgets only where evidence supports them."),
        new("TI-Q-015", "AI and model lifecycle", "Does the product use AI or learned models, and if so how are they hosted and governed?", "Model execution introduces provider, data, evaluation, explainability, drift, cost, and human-oversight boundaries.",
            ["Not applicable", "Local model", "Managed model API", "Self-hosted model", "Retrieval-augmented generation", "Training/fine-tuning", "Human approval required"],
            "Answer Not applicable unless the BRD requires AI. Otherwise separate deterministic policy from model advice and define evaluation, versioning, fallback, privacy, cost, and human oversight."),
        new("TI-Q-016", "Constraints and exclusions", "Which technical constraints, mandated choices, explicit exclusions, and future options must be preserved?", "Explicit boundaries prevent a high-level intent from silently expanding scope or hard-coding a future option into the MVP.",
            ["Mandated technology", "Existing platform constraint", "Offline requirement", "Cost ceiling", "Licensing", "Provider neutrality", "Deferred option", "Explicitly excluded technology"],
            "Record only evidenced constraints and distinguish current decisions from post-MVP options and explicit exclusions."),
    ];

    private readonly ICisWorkspaceRegistry _workspaceRegistry;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly BrdService _brd;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly Func<DateTimeOffset> _clock;

    public TechnicalIntentQuestionnaireService(
        ICisWorkspaceRegistry workspaceRegistry,
        ICisRepositoryContextResolver repositoryResolver,
        BrdService brd,
        DocumentationCatalogMerger catalogMerger,
        Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _brd = brd;
        _catalogMerger = catalogMerger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TechnicalIntentQuestionnaireResult Initialize(string workspacePath)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Result("invalid", state, [], false);
        if (!state.BrdReady) return Result("blocked", state, [], false,
            "An Active, current BRD is required before the technical questionnaire. Run `cis brd status`.");

        var existing = File.Exists(state.Path!) ? File.ReadAllText(state.Path) : null;
        var previous = existing is null ? new Dictionary<string, TechnicalIntentQuestion>(StringComparer.Ordinal) : ParseQuestions(existing);
        var brdChanged = existing is not null && ReadNested(existing, "brd_version") != state.BrdVersion;
        var profile = AnalyzeWorkspace(state.Workspace!);
        var questions = profile.Definitions.Select(definition =>
        {
            if (previous.TryGetValue(definition.Id, out var item) && !string.IsNullOrWhiteSpace(item.Answer))
            {
                if (item.ResolutionSource == "repository-evidence")
                    return profile.Derived.TryGetValue(definition.Id, out var refreshed)
                        ? ToDerivedQuestion(definition, refreshed)
                        : ToQuestion(definition, null, null, null);
                if (brdChanged)
                    return ToQuestion(definition with
                    {
                        SuggestedAnswer = $"Previously recorded direction — recheck against the renewed BRD before accepting or editing: {item.Answer}",
                    }, null, null, null);
                return ToQuestion(definition, item.Answer, item.AnsweredBy, item.AnsweredAtUtc);
            }
            return profile.Derived.TryGetValue(definition.Id, out var derived)
                ? ToDerivedQuestion(definition, derived)
                : ToQuestion(definition, null, null, null);
        }).ToArray();
        var complete = questions.All(IsResolved);
        var content = Render(state.Authority!.Id, state.BrdVersion!, complete, questions);
        var stableId = $"{state.Authority.Id}:spec:technical-intent-questionnaire";
        var relative = Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path!));
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var merge = _catalogMerger.Merge(state.Authority.Id, catalog,
            [new CatalogArtifactEntry(stableId, relative, "repository-specification", complete ? "active" : "draft", "canonical")]);
        if (merge.Collisions.Count > 0) return Result("collision", state, questions, false, merge.Collisions.ToArray());
        var changed = existing is null || !Equivalent(existing, content);
        var catalogChanged = !Equivalent(catalog, merge.Content);
        if (changed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(state.Path)!);
            Write(state.Path!, content);
        }
        if (catalogChanged) Write(state.Context.CatalogPath, merge.Content);
        return StatusInternal(state.Workspace!.WorkspacePath, changed || catalogChanged ? "initialized" : "unchanged", changed || catalogChanged);
    }

    public TechnicalIntentQuestionnaireResult Status(string workspacePath)
        => StatusInternal(workspacePath, "status", false);

    public TechnicalIntentQuestionnaireResult Answer(string workspacePath, string id, string answer, string actor)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(actor))
            return new TechnicalIntentQuestionnaireResult("invalid", workspacePath, null, null, null, false, false, 0, 0, [], [],
                ["Question ID, substantive answer, and human actor are required."], false);
        if (answer.Trim().Length > 16_384)
            return new TechnicalIntentQuestionnaireResult("invalid", workspacePath, null, null, null, false, false, 0, 0, [], [],
                ["The answer exceeds the 16 KiB limit."], false);

        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Result("invalid", state, [], false);
        if (!state.BrdReady) return Result("blocked", state, [], false,
            "An Active, current BRD is required before the technical questionnaire.");
        if (!File.Exists(state.Path!))
        {
            var initialized = Initialize(workspacePath);
            if (initialized.ExitCode != 0) return initialized;
            state = Resolve(workspacePath);
        }
        var questions = ParseQuestions(File.ReadAllText(state.Path!));
        if (!questions.TryGetValue(id.Trim(), out var question))
            return Result("invalid", state, questions.Values.ToArray(), false, $"Unknown technical-intent question '{id.Trim()}'.");
        var updated = questions.Values.Select(item => item.Id == question.Id
            ? item with
            {
                Answer = answer.Trim(),
                AnsweredBy = actor.Trim(),
                AnsweredAtUtc = _clock().ToString("O"),
                Status = "Answered",
                ResolutionSource = "human",
                Confidence = "confirmed",
                Evidence = [],
            }
            : item).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var content = Render(state.Authority!.Id, state.BrdVersion!, updated.All(IsResolved), updated);
        Write(state.Path!, content);

        var stableId = $"{state.Authority.Id}:spec:technical-intent-questionnaire";
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var nextCatalog = UpdateCatalogStatus(catalog, stableId,
            updated.All(IsResolved) ? "active" : "draft");
        if (!Equivalent(catalog, nextCatalog)) Write(state.Context.CatalogPath, nextCatalog);
        var complete = updated.All(IsResolved);
        return new TechnicalIntentQuestionnaireResult("answered", state.Workspace!.WorkspacePath, state.Authority.Id,
            Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path!)), state.BrdVersion, true, complete,
            updated.Count(IsResolved), updated.Count(item => !IsResolved(item)), updated,
            complete ? [] : [$"{updated.Count(item => !IsResolved(item))} high-level technical decision(s) remain unanswered."], [], true);
    }

    internal static QuestionnaireSnapshot? ReadSnapshot(string path, string expectedBrdVersion)
    {
        if (!File.Exists(path)) return null;
        var content = File.ReadAllText(path);
        var questions = ParseQuestions(content).Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var version = ReadNested(content, "brd_version");
        return new QuestionnaireSnapshot(path, Hash(content), version ?? string.Empty,
            version == expectedBrdVersion, questions.Length == Definitions.Count && questions.All(IsResolved), questions);
    }

    private TechnicalIntentQuestionnaireResult StatusInternal(string workspacePath, string operation, bool applied)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Result("invalid", state, [], false);
        if (!File.Exists(state.Path!)) return Result("missing", state, [], false);
        var content = File.ReadAllText(state.Path);
        var questions = ParseQuestions(content).Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var errors = new List<string>();
        if (questions.Length != Definitions.Count) errors.Add($"The questionnaire contains {questions.Length} of {Definitions.Count} required questions. Run `cis technical-intent questions init`.");
        var recordedBrd = ReadNested(content, "brd_version");
        var current = state.BrdReady && recordedBrd == state.BrdVersion;
        var complete = questions.Length == Definitions.Count && questions.All(IsResolved);
        var warnings = new List<string>();
        if (!current) warnings.Add("The technical questionnaire does not match the current Active BRD. Reinitialize and review its answers.");
        if (!complete) warnings.Add($"{questions.Count(item => !IsResolved(item))} high-level technical decision(s) remain unanswered.");
        return new TechnicalIntentQuestionnaireResult(operation, state.Workspace!.WorkspacePath, state.Authority!.Id,
            Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path)), state.BrdVersion, current, complete,
            questions.Count(IsResolved), questions.Count(item => !IsResolved(item)), questions,
            warnings, errors, applied);
    }

    private QuestionnaireState Resolve(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
            return new(null, null, null, null, null, false, resolution.Errors);
        var authority = resolution.Workspace.AuthorityRepository;
        if (authority is null) return new(resolution.Workspace, null, null, null, null, false, ["Workspace has no authority repository."]);
        var context = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!context.IsSuccess || context.Context is null)
            return new(resolution.Workspace, authority, null, null, null, false, context.Errors);
        var brdStatus = _brd.Status(workspacePath);
        var brdPath = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "specs", "business-requirements.md");
        var version = File.Exists(brdPath) ? "semantic-v1:" + BrdDocumentDigest.Compute(File.ReadAllText(brdPath)) : null;
        var path = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "specs", "technical-intent-questionnaire.md");
        var ready = brdStatus.Validation is { } brdValidation
            && CisDefinitionDraftScope.Accepts(brdValidation.Valid, brdValidation.Current, brdValidation.EffectiveStatus)
            && version is not null;
        return new(resolution.Workspace, authority, context.Context, path, version, ready, []);
    }

    private static TechnicalIntentQuestion ToQuestion(QuestionDefinition definition, string? answer, string? actor, string? at)
        => new(definition.Id, definition.Area, definition.Question, definition.Why, definition.CommonOptions,
            definition.SuggestedAnswer, string.IsNullOrWhiteSpace(answer) ? "Unanswered" : "Answered", answer, actor, at,
            string.IsNullOrWhiteSpace(answer) ? "unresolved" : "human",
            string.IsNullOrWhiteSpace(answer) ? null : "confirmed", []);

    private static TechnicalIntentQuestion ToDerivedQuestion(QuestionDefinition definition, DerivedDirection derived)
        => new(definition.Id, definition.Area, definition.Question, definition.Why, definition.CommonOptions,
            definition.SuggestedAnswer, "Derived", derived.Answer, "CIS deterministic repository analysis", null,
            "repository-evidence", derived.Confidence, derived.Evidence);

    private static bool IsResolved(TechnicalIntentQuestion question)
        => question.Status is "Answered" or "Derived" && !string.IsNullOrWhiteSpace(question.Answer);

    private static WorkspaceAnalysis AnalyzeWorkspace(CisWorkspace workspace)
    {
        var evidence = new List<(CisWorkspaceRepository Repository, RepositoryClassification Classification, RepositoryComponentClassification Component)>();
        var dependencyEvidence = new List<(CisWorkspaceRepository Repository, RepositoryClassification Classification, RepositoryComponentClassification Component)>();
        var classifier = new RepositoryClassifier();
        foreach (var repository in workspace.Repositories)
        {
            try
            {
                var classification = classifier.Classify(repository.RepositoryPath);
                var components = repository.Components.Count == 0
                    ? classification.Components
                    : classification.Components.Where(component => repository.Components.Contains(component.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
                var target = repository.IsDependency ? dependencyEvidence : evidence;
                target.AddRange(components.Select(component => (repository, classification, component)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Unreadable evidence leaves a human question; it never becomes a derived answer.
            }
        }

        var detected = new Dictionary<string, string>(StringComparer.Ordinal);
        var derived = new Dictionary<string, DerivedDirection>(StringComparer.Ordinal);
        string Describe(IEnumerable<(CisWorkspaceRepository Repository, RepositoryClassification Classification, RepositoryComponentClassification Component)> selected)
        {
            var values = selected.Select(item => $"{item.Repository.Id}/{item.Component.Id}: " +
                string.Join(", ", item.Component.Languages.Concat(item.Component.Frameworks).Concat(item.Component.Roles)
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)))
                .Where(value => !value.EndsWith(": ", StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return values.Length == 0 ? string.Empty : string.Join("; ", values);
        }

        string[] Cite(IEnumerable<(CisWorkspaceRepository Repository, RepositoryClassification Classification, RepositoryComponentClassification Component)> selected)
            => selected.SelectMany(item => new[] { $"{item.Repository.Id}:{item.Component.Root}" }
                    .Concat(item.Component.Evidence.Select(value => $"{item.Repository.Id}:{value}")))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();

        void Derive(string id, string answer, string confidence, IEnumerable<string> citations)
        {
            if (!string.IsNullOrWhiteSpace(answer))
                derived[id] = new DerivedDirection(answer, confidence,
                    citations.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray());
        }

        var frontendItems = evidence.Where(item => item.Component.Roles.Any(role =>
            role is "frontend-consumer" or "mobile-client" or "native-frontend")).ToArray();
        var backendItems = evidence.Where(item => item.Component.Roles.Any(role =>
            role.Contains("backend", StringComparison.OrdinalIgnoreCase) || role is "worker")).ToArray();
        var dataItems = evidence.Where(item => item.Component.Capabilities.Contains("persistence", StringComparer.OrdinalIgnoreCase)).ToArray();
        var integrationItems = evidence.Where(item => item.Component.Roles.Any(role => role.Contains("api", StringComparison.OrdinalIgnoreCase)
            || role.Contains("event", StringComparison.OrdinalIgnoreCase))).ToArray();
        var infrastructureItems = evidence.Where(item => item.Component.Roles.Contains("infrastructure", StringComparer.OrdinalIgnoreCase)).ToArray();
        var aiItems = evidence.Where(item => item.Component.Capabilities.Any(capability => capability.Contains("ai", StringComparison.OrdinalIgnoreCase)
            || capability.Contains("model", StringComparison.OrdinalIgnoreCase))).ToArray();
        var testItems = evidence.Where(item => item.Component.Roles.Contains("test-automation", StringComparer.OrdinalIgnoreCase)).ToArray();
        var authItems = evidence.Where(item => item.Component.Capabilities.Any(capability =>
            capability is "authentication" or "authorization")).ToArray();
        var asyncItems = evidence.Where(item => item.Component.Roles.Any(role => role is "worker" or "event-producer" or "event-consumer")
            || item.Component.Capabilities.Contains("events", StringComparer.OrdinalIgnoreCase)).ToArray();

        var frontends = Describe(frontendItems);
        var backends = Describe(backendItems);
        var data = Describe(dataItems);
        var integration = Describe(integrationItems);
        var infrastructure = Describe(infrastructureItems);
        var ai = Describe(aiItems);
        var markerEvidence = DetectTechnologyMarkers(workspace, evidence);
        if (frontends.Length > 0)
        {
            detected["TI-Q-001"] = $"Repository evidence currently contains these user-facing surfaces: {frontends}. Confirm which are product scope and classify each as public, customer, or backoffice.";
            detected["TI-Q-002"] = $"Prefer the detected frontend technology unless an evidenced migration is intended: {frontends}.";
            Derive("TI-Q-002", $"Preserve the existing frontend stack(s): {frontends}. A migration requires an explicit replacement decision and rollout path.",
                "high", Cite(frontendItems));
        }
        var surfaceItems = frontendItems.Concat(backendItems.Where(item => item.Component.Roles.Any(role => role.Contains("api", StringComparison.OrdinalIgnoreCase))))
            .Concat(asyncItems.Where(item => item.Component.Roles.Contains("worker", StringComparer.OrdinalIgnoreCase))).Distinct().ToArray();
        if (surfaceItems.Length > 0)
            Derive("TI-Q-001", $"The existing implementation exposes these classified surfaces: {Describe(surfaceItems)}. Preserve them as the current scope; frontend access classification must follow existing route and authorization evidence.",
                "high", Cite(surfaceItems));
        if (backends.Length > 0)
        {
            detected["TI-Q-003"] = $"Prefer the detected backend runtime unless an evidenced migration is intended: {backends}.";
            Derive("TI-Q-003", $"Preserve the existing backend runtime and framework stack(s): {backends}.", "high", Cite(backendItems));
        }
        var productRepositories = workspace.Repositories.Where(repository => repository.IsProductOwned).ToArray();
        if (productRepositories.Length > 1)
            detected["TI-Q-005"] = $"The product boundary already owns {productRepositories.Length} repositories ({string.Join(", ", productRepositories.Select(item => item.Id))}); confirm their ownership and deployable boundaries before adding another split.";
        if (evidence.Count > 0)
        {
            var repositorySummary = string.Join("; ", productRepositories.Select(repository =>
            {
                var items = evidence.Where(item => item.Repository.Id == repository.Id).ToArray();
                return $"{repository.Id} ({items.Length} classified component{(items.Length == 1 ? string.Empty : "s")}: {string.Join(", ", items.Select(item => item.Component.Id))})";
            }));
            var style = productRepositories.Length > 1
                ? $"Extend the existing multi-repository product architecture across {productRepositories.Length} owned repositories. Repository evidence establishes current boundaries but does not by itself justify reclassifying them as microservices."
                : evidence.Select(item => item.Classification.Shape).Contains("monorepo", StringComparer.OrdinalIgnoreCase)
                    ? "Extend the existing monorepo architecture and preserve its classified component boundaries; do not introduce independently deployed services without an evidenced ownership, scale, security, or release need."
                    : "Extend the existing single-repository application architecture; no repository evidence currently justifies introducing microservices.";
            Derive("TI-Q-004", style, "medium", Cite(evidence));
            Derive("TI-Q-005", $"Preserve the registered repository and component topology: {repositorySummary}. Treat observed components as current ownership boundaries until a reviewed ADR changes them.",
                "high", Cite(evidence));
        }
        if (data.Length > 0)
        {
            var databases = MarkerLabels(markerEvidence, "database");
            detected["TI-Q-006"] = databases.Length > 0
                ? $"Persistence capability is detected in {data}; database markers identify {string.Join(", ", databases)}. Confirm system-of-record ownership when more than one store is present."
                : $"Persistence capability is detected in {data}. Confirm the actual system of record, migration, and recovery boundary rather than inferring it from packages alone.";
            if (databases.Length == 1)
                Derive("TI-Q-006", $"Preserve the detected {databases[0]} transactional persistence boundary as the current system of record. Retain explicit migration, concurrency, backup, restore, and recovery ownership.",
                    "high", MarkerCitations(markerEvidence, "database"));
        }
        var supportingStores = MarkerLabels(markerEvidence, "supporting-data");
        if (supportingStores.Length > 0)
            Derive("TI-Q-007", $"Preserve the existing supporting data service(s): {string.Join(", ", supportingStores)}. Each derived store requires an owner and rebuild or recovery path.",
                "high", MarkerCitations(markerEvidence, "supporting-data"));
        if (integration.Length > 0)
        {
            detected["TI-Q-008"] = $"Contract or integration evidence is detected in {integration}. Preserve its supported compatibility policy unless this intent explicitly changes it.";
            var styles = new List<string>();
            if (integrationItems.Any(item => item.Component.Roles.Any(role => role.Contains("api", StringComparison.OrdinalIgnoreCase))))
                styles.Add("versioned HTTP/API contracts");
            if (integrationItems.Any(item => item.Component.Roles.Any(role => role.Contains("event", StringComparison.OrdinalIgnoreCase))))
                styles.Add("events/messages");
            if (evidence.GroupBy(item => item.Repository.Id).Any(group => group.Count() > 1)) styles.Add("in-process component contracts");
            Derive("TI-Q-008", $"Preserve the detected interaction styles: {string.Join(", ", styles.Distinct(StringComparer.OrdinalIgnoreCase))}. Maintain explicit compatibility, authentication, failure, idempotency, and test contracts at every cross-component boundary.",
                "medium", Cite(integrationItems));
        }
        if (dependencyEvidence.Count > 0)
        {
            var dependencies = dependencyEvidence.GroupBy(item => new { item.Repository.Id, item.Repository.Relationship })
                .Select(group => $"{group.Key.Id} ({group.Key.Relationship}; {string.Join(", ", group.Select(item => item.Component.Id).Distinct(StringComparer.OrdinalIgnoreCase))})")
                .ToArray();
            detected["TI-Q-008"] = $"The product has registered directional dependencies: {string.Join("; ", dependencies)}. Define their exact contracts and compatibility boundaries without treating their implementations as product-owned scope.";
            Derive("TI-Q-008", $"Preserve the registered producer/consumer directions for {string.Join("; ", dependencies)}. Dependency repositories provide context and contract evidence only; modifying them requires separately governed product authority.",
                "high", Cite(dependencyEvidence));
        }
        var identities = MarkerLabels(markerEvidence, "identity");
        if (authItems.Length > 0 || identities.Length > 0)
            Derive("TI-Q-009", $"Preserve the existing authentication and authorization boundary{(identities.Length > 0 ? $" using the detected {string.Join(", ", identities)} integration" : string.Empty)}. Continue enforcing authorization and object ownership at the trusted application boundary.",
                identities.Length > 0 ? "high" : "medium", Cite(authItems).Concat(MarkerCitations(markerEvidence, "identity")));
        if (infrastructure.Length > 0)
        {
            detected["TI-Q-010"] = $"Deployment or infrastructure evidence is detected in {infrastructure}. Confirm which topology applies to local, test, staging, and production environments.";
            Derive("TI-Q-010", $"Preserve the existing deployment and infrastructure mechanisms: {infrastructure}. Environment-specific topology, secrets, recovery, and release ownership remain explicit.",
                "high", Cite(infrastructureItems));
        }
        if (asyncItems.Length > 0)
            Derive("TI-Q-011", $"Preserve the detected asynchronous boundaries: {Describe(asyncItems)}. Keep ordering, retry, idempotency, terminal failure, replay, and observability behavior explicit.",
                "high", Cite(asyncItems));
        var operations = MarkerLabels(markerEvidence, "operations");
        if (operations.Length > 0)
            Derive("TI-Q-012", $"Preserve the detected operational capabilities: {string.Join(", ", operations)}. Keep telemetry privacy-safe and retain truthful health, failure, and recovery evidence.",
                "high", MarkerCitations(markerEvidence, "operations"));
        var testFrameworks = MarkerLabels(markerEvidence, "testing");
        if (testItems.Length > 0 || testFrameworks.Length > 0)
            Derive("TI-Q-014", $"Preserve the existing verification stack{(testFrameworks.Length > 0 ? $": {string.Join(", ", testFrameworks)}" : $" represented by {Describe(testItems)}")}. Extend it according to changed-boundary risk and retain exact execution evidence.",
                "high", Cite(testItems).Concat(MarkerCitations(markerEvidence, "testing")));
        if (ai.Length > 0)
        {
            detected["TI-Q-015"] = $"AI/model capability is detected in {ai}. Define its provider, evaluation, privacy, cost, fallback, and human-oversight boundary.";
            Derive("TI-Q-015", $"Preserve the existing AI/model boundary represented by {ai}; retain provider, evaluation, versioning, privacy, cost, fallback, and human-oversight controls.",
                "medium", Cite(aiItems));
        }
        var definitions = Definitions.Select(definition => detected.TryGetValue(definition.Id, out var suggestion)
            ? definition with { SuggestedAnswer = suggestion }
            : definition).ToArray();
        return new WorkspaceAnalysis(definitions, derived, evidence.Count > 0);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TechnologyMarker>> DetectTechnologyMarkers(
        CisWorkspace workspace,
        IReadOnlyList<(CisWorkspaceRepository Repository, RepositoryClassification Classification, RepositoryComponentClassification Component)> components)
    {
        var definitions = new (string Category, string Label, string[] Markers)[]
        {
            ("database", "SQLite", ["sqlite", "microsoft.entityframeworkcore.sqlite", "better-sqlite3", "@libsql/client", "org.xerial"]),
            ("database", "PostgreSQL", ["npgsql", "postgresql", "postgres:", "postgresql:", "\"pg\""]),
            ("database", "SQL Server", ["entityframeworkcore.sqlserver", "usesqlserver", "microsoft.data.sqlclient", "\"mssql\""]),
            ("database", "MySQL/MariaDB", ["pomelo.entityframeworkcore.mysql", "mysqlconnector", "mysql2", "mariadb"]),
            ("database", "MongoDB/document database", ["mongodb", "mongoose"]),
            ("supporting-data", "Redis/distributed cache", ["stackexchange.redis", "ioredis", "redis:", "redis-server"]),
            ("supporting-data", "search index", ["elasticsearch", "opensearch", "lucene", "meilisearch"]),
            ("supporting-data", "object/blob storage", ["azure.storage.blobs", "amazons3", "@aws-sdk/client-s3", "minio"]),
            ("supporting-data", "graph database", ["neo4j", "arangodb"]),
            ("supporting-data", "vector store", ["qdrant", "pinecone", "weaviate", "pgvector"]),
            ("identity", "OIDC/OAuth identity provider", ["openidconnect", "oidc", "oauth2", "oauth 2", "auth0", "keycloak"]),
            ("identity", "ASP.NET Core Identity", ["aspnetcore.identity", "identitydbcontext"]),
            ("identity", "SuperTokens", ["supertokens"]),
            ("identity", "NextAuth/Auth.js", ["next-auth", "@auth/core"]),
            ("operations", "structured logging", ["serilog", "nlog", "winston", "pino"]),
            ("operations", "OpenTelemetry/tracing", ["opentelemetry", "addopentelemetry"]),
            ("operations", "health/readiness checks", ["healthchecks", "addhealthchecks", "readinessprobe", "livenessprobe"]),
            ("operations", "metrics", ["prometheus", "opentelemetry.exporter.prometheus"]),
            ("operations", "error monitoring", ["sentry", "applicationinsights"]),
            ("testing", "xUnit", ["xunit"]),
            ("testing", "NUnit", ["nunit"]),
            ("testing", "MSTest", ["mstest"]),
            ("testing", "Vitest", ["vitest"]),
            ("testing", "Jest", ["\"jest\"", "@jest/"]),
            ("testing", "Playwright", ["playwright"]),
            ("testing", "Cypress", ["cypress"]),
            ("testing", "Cucumber", ["cucumber"]),
            ("testing", "Testcontainers", ["testcontainers"]),
            ("testing", "Stryker mutation testing", ["stryker"]),
        };
        var sources = new List<EvidenceText>();
        sources.AddRange(components.Select(item => new EvidenceText(
            $"{item.Repository.Id}:{item.Component.Root}",
            string.Join(' ', item.Component.Languages.Concat(item.Component.Frameworks).Concat(item.Component.Roles)
                .Concat(item.Component.Capabilities).Concat(item.Component.Evidence)))));
        foreach (var repository in workspace.Repositories.Where(repository => repository.IsProductOwned))
        {
            foreach (var path in EnumerateTechnicalMetadata(repository.RepositoryPath))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > 1_048_576) continue;
                    sources.Add(new EvidenceText(
                        $"{repository.Id}:{Normalize(Path.GetRelativePath(repository.RepositoryPath, path))}",
                        File.ReadAllText(path)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Missing evidence leaves a question unresolved; it is never inferred from a read failure.
                }
            }
        }

        var result = new Dictionary<string, List<TechnologyMarker>>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var citations = sources.Where(source => definition.Markers.Any(marker =>
                    source.Text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                .Select(source => source.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
            if (citations.Length == 0) continue;
            if (!result.TryGetValue(definition.Category, out var items))
            {
                items = [];
                result[definition.Category] = items;
            }
            items.Add(new TechnologyMarker(definition.Label, citations));
        }
        return result.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<TechnologyMarker>)pair.Value
                .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray(),
            StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateTechnicalMetadata(string repositoryPath)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cis", ".git", ".next", ".nuxt", ".output", ".svelte-kit", ".vs", "artifacts", "bin", "coverage",
            "dist", "node_modules", "obj", ".stryker-tmp", ".codex-tmp",
        };
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        var visited = 0;
        while (pending.Count > 0 && visited++ < 20_000)
        {
            var directory = pending.Pop();
            string[] directories;
            string[] files;
            try
            {
                directories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var child in directories)
            {
                if (excluded.Contains(Path.GetFileName(child))) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Skip unreadable or unstable directory evidence.
                }
            }
            foreach (var file in files.Where(IsTechnicalMetadataFile)) yield return file;
        }
    }

    private static bool IsTechnicalMetadataFile(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return extension is ".csproj" or ".fsproj" or ".vbproj" or ".props" or ".targets" or ".gradle" or ".kts" or ".toml" or ".tf"
            || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Package.swift", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("requirements", StringComparison.OrdinalIgnoreCase) && extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || name.Contains("compose", StringComparison.OrdinalIgnoreCase) && extension is ".yml" or ".yaml"
            || name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] MarkerLabels(IReadOnlyDictionary<string, IReadOnlyList<TechnologyMarker>> evidence, string category)
        => evidence.TryGetValue(category, out var items)
            ? items.Select(item => item.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

    private static string[] MarkerCitations(IReadOnlyDictionary<string, IReadOnlyList<TechnologyMarker>> evidence, string category)
        => evidence.TryGetValue(category, out var items)
            ? items.SelectMany(item => item.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray()
            : [];

    private static string Render(string authorityId, string brdVersion, bool complete, IReadOnlyList<TechnicalIntentQuestion> questions)
    {
        var derived = questions.Count(item => item.Status == "Derived");
        var answered = questions.Count(item => item.Status == "Answered");
        var resolved = questions.Count(IsResolved);
        var mode = derived > 0 ? "existing-project" : "greenfield";
        var builder = new StringBuilder($$"""
---
title: "{{authorityId}} Technical Intent Questionnaire"
type: specification
status: {{(complete ? "Complete" : "Draft")}}
scope: Workspace
owner: Product owner and technical authority
last_reviewed: null
review_cadence: on approved requirement or high-level technical direction change
cis:
  stable_id: {{authorityId}}:spec:technical-intent-questionnaire
  technical_questionnaire_schema: 2
  brd_version: {{brdVersion}}
  project_mode: {{mode}}
---

# {{authorityId}} Technical Intent Questionnaire

This governed pre-intent record captures high-level technology and architecture direction. For an existing implementation, CIS resolves evidence-supported facts from deterministic repository analysis and leaves ambiguous or intentional choices for a human. For a greenfield project, every direction remains a human question. Derived answers are reviewable and may be overridden explicitly.

## Decision progress

- Resolved: {{resolved}} of {{questions.Count}}
- Derived from repository evidence: {{derived}}
- Answered by a human: {{answered}}
- State: {{(complete ? "Complete — ready to generate technical intent" : "Draft — answer every question before technical intent generation")}}

## Questions

""");
        foreach (var question in questions)
        {
            builder.AppendLine($"### {question.Id} — {question.Area}").AppendLine()
                .AppendLine(question.Question).AppendLine()
                .AppendLine($"- Why this is needed: {question.Why}")
                .AppendLine($"- Common options: {string.Join("; ", question.CommonOptions)}")
                .AppendLine($"- Advisory starting direction: {question.SuggestedAnswer}")
                .AppendLine($"- Status: {question.Status}")
                .AppendLine($"- Answered by: {question.AnsweredBy ?? "Not recorded"}")
                .AppendLine($"- Answered at: {question.AnsweredAtUtc ?? "Not recorded"}")
                .AppendLine($"- Resolution source: {question.ResolutionSource}")
                .AppendLine($"- Confidence: {question.Confidence ?? "Not assessed"}")
                .AppendLine($"- Evidence: {string.Join("; ", question.Evidence ?? []) switch { "" => "Not recorded", var value => value }}")
                .AppendLine().AppendLine("#### Recorded answer").AppendLine()
                .AppendLine(question.Answer ?? "Not answered.").AppendLine();
        }
        return builder.ToString().TrimEnd() + "\n";
    }

    private static Dictionary<string, TechnicalIntentQuestion> ParseQuestions(string content)
    {
        var result = new Dictionary<string, TechnicalIntentQuestion>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(content,
                     @"(?ms)^### (?<id>TI-Q-\d{3}) — (?<area>.+?)\r?$\n(?<body>.*?)(?=^### TI-Q-|\z)",
                     RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
        {
            var id = match.Groups["id"].Value;
            var definition = Definitions.FirstOrDefault(item => item.Id == id);
            if (definition is null) continue;
            var body = match.Groups["body"].Value;
            var status = ReadBullet(body, "Status") ?? "Unanswered";
            var suggested = ReadBullet(body, "Advisory starting direction") ?? definition.SuggestedAnswer;
            var options = (ReadBullet(body, "Common options") ?? string.Join("; ", definition.CommonOptions))
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var answer = Regex.Match(body, @"(?ms)^#### Recorded answer\s*$\r?\n(?<answer>.*)\z",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["answer"].Value.Trim();
            if (answer == "Not answered.") answer = string.Empty;
            var resolvedStatus = status is "Answered" or "Derived" && answer.Length > 0 ? status : "Unanswered";
            var resolutionSource = ReadBullet(body, "Resolution source")
                ?? (resolvedStatus == "Derived" ? "repository-evidence" : resolvedStatus == "Answered" ? "human" : "unresolved");
            var evidence = (ReadBullet(body, "Evidence") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => value != "Not recorded").ToArray();
            result[id] = new TechnicalIntentQuestion(definition.Id, definition.Area, definition.Question, definition.Why,
                options, suggested, resolvedStatus, resolvedStatus is "Answered" or "Derived" ? answer : null,
                NullIfMissing(ReadBullet(body, "Answered by")), NullIfMissing(ReadBullet(body, "Answered at")),
                resolutionSource, NullIfMissing(ReadBullet(body, "Confidence")), evidence);
        }
        return result;
    }

    private static string? ReadBullet(string body, string label)
        => Regex.Match(body, $@"(?m)^- {Regex.Escape(label)}:\s*(?<value>.+?)\s*$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } match ? match.Groups["value"].Value.Trim() : null;
    private static string? NullIfMissing(string? value) => value is null or "Not recorded" or "Not assessed" ? null : value;
    private static string? ReadNested(string content, string key)
    {
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return null;
        var match = Regex.Match(content[..end], $"(?m)^  {Regex.Escape(key)}:\\s*(?<value>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["value"].Value.Trim().Trim('"') : null;
    }

    private static TechnicalIntentQuestionnaireResult Result(string status, QuestionnaireState state,
        IReadOnlyList<TechnicalIntentQuestion> questions, bool applied, params string[] errors)
        => new(status, state.Workspace?.WorkspacePath, state.Authority?.Id,
            state.Path is null || state.Authority is null ? null : Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path)),
            state.BrdVersion, false, false, questions.Count(IsResolved),
            questions.Count(item => !IsResolved(item)), questions, [], errors.Length > 0 ? errors : state.Errors, applied);

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

    private static string Hash(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
    private static bool Equivalent(string left, string right) => left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static void Write(string path, string content) => File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));

    private sealed record QuestionDefinition(string Id, string Area, string Question, string Why,
        IReadOnlyList<string> CommonOptions, string SuggestedAnswer);
    private sealed record DerivedDirection(string Answer, string Confidence, IReadOnlyList<string> Evidence);
    private sealed record WorkspaceAnalysis(IReadOnlyList<QuestionDefinition> Definitions,
        IReadOnlyDictionary<string, DerivedDirection> Derived, bool ExistingProject);
    private sealed record TechnologyMarker(string Label, IReadOnlyList<string> Evidence);
    private sealed record EvidenceText(string Evidence, string Text);
    private sealed record QuestionnaireState(CisWorkspace? Workspace, CisWorkspaceRepository? Authority,
        CisRepositoryContext? Context, string? Path, string? BrdVersion, bool BrdReady, IReadOnlyList<string> Errors);
}

internal sealed record QuestionnaireSnapshot(string Path, string Digest, string BrdVersion, bool Current, bool Complete,
    IReadOnlyList<TechnicalIntentQuestion> Questions);
