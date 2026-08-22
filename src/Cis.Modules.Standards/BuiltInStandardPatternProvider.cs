using Cis.Abstractions;

namespace Cis.Modules.Standards;

public sealed class BuiltInStandardPatternProvider : ICisStandardPatternProvider
{
    public string ProviderKey => "cis.builtin.standard-patterns";

    public IReadOnlyList<CisStandardPatternDefinition> Patterns { get; } =
    [
        Pattern(
            "csharp.testing.calls-production-symbol",
            "Tests exercise compiler-bound production symbols",
            "Recognizes repository tests that directly invoke compiler-resolved production symbols.",
            ["csharp"], ["test-automation"], [], ["compiler.symbols", "compiler.calls", "tests.symbols", "tests.calls"],
            Selector(kind: "test"),
            [Outgoing("calls", Selector(kind: "symbol", facets: ["compiler-bound"]))],
            [], 5, 0.80,
            Candidate("testing", "SHOULD", "Repository tests SHOULD exercise production behavior through compiler-bound calls rather than visibility-only assertions.", "architecture-test", "Measure test-to-production call coverage and review indirect or intentionally isolated tests."), version: 2),
        Pattern(
            "csharp.modularity.command-registration",
            "Modules register commands through the host registry",
            "Recognizes ICisModule implementations whose RegisterCommands method calls ICisCommandRegistry.Add.",
            ["csharp"], ["shared-library", "package-producer", "tooling"], ["commands"], ["compiler.symbols", "compiler.calls"],
            Selector(kind: "symbol", subtype: "method", propertyContains: [("qualifiedName", ".RegisterCommands(")]),
            [Outgoing("calls", Selector(kind: "symbol", propertyContains: [("qualifiedName", "ICisCommandRegistry.Add(")]))],
            [], 3, 0.80,
            Candidate("architecture", "MUST", "Command modules MUST register their top-level command through the host command registry.", "architecture-test", "Match module RegisterCommands implementations to compiler-bound ICisCommandRegistry.Add calls.")),
        Pattern(
            "csharp.api.thin-endpoint",
            "HTTP endpoints delegate to application boundaries",
            "Recognizes controller or endpoint methods that delegate application behavior without direct persistence calls.",
            ["csharp"], ["backend-api-producer"], ["openapi"], ["compiler.symbols", "compiler.calls", "compiler.attributes", "compiler.external-calls"],
            Selector(kind: "symbol", subtype: "method", facets: ["http-endpoint"]),
            [Outgoing("delegates", Selector(kind: "symbol", facets: ["compiler-bound"], propertyEquals: [("external", "false")]))],
            [Outgoing("persistence-call", Selector(kind: "symbol"))], 3, 0.85,
            Candidate("api", "SHOULD", "HTTP endpoint handlers SHOULD remain transport adapters and delegate business behavior and persistence access to application boundaries.", "architecture-test", "Classify endpoint attributes and inspect compiler-bound call paths for direct persistence access."), version: 2),
        Pattern(
            "csharp.security.authorization-boundary",
            "Authorization is enforced at trusted boundaries",
            "Recognizes authorization attributes and policy checks on externally reachable operations.",
            ["csharp"], ["backend-api-producer"], ["authorization"], ["compiler.symbols", "compiler.attributes", "compiler.external-calls"],
            Selector(kind: "symbol", subtype: "method", facets: ["http-endpoint"]),
            [Outgoing("authorized-by", Selector(kind: "symbol", propertyContains: [("semantics", "authorization")]))], [], 3, 0.90,
            Candidate("security", "MUST", "Externally reachable protected operations MUST enforce authorization at a trusted server boundary.", "architecture-test", "Resolve endpoint exposure and authorization attributes or policy calls through compiler evidence."), version: 2),
        Pattern(
            "csharp.application.handler-boundary",
            "Application handlers own use-case orchestration",
            "Recognizes command/query handlers that coordinate domain and persistence boundaries.",
            ["csharp"], ["backend-api-producer", "worker"], [], ["compiler.symbols", "compiler.interfaces", "compiler.calls"],
            Selector(kind: "symbol", subtype: "class", facets: ["application-handler"]),
            [Outgoing("implements", Selector(kind: "symbol", subtype: "interface", propertyContains: [("qualifiedName", "Handler")]))], [], 3, 0.80,
            Candidate("backend", "SHOULD", "Application use cases SHOULD be orchestrated by explicit handlers or equivalent application services.", "architecture-test", "Resolve handler interfaces and compare their compiler-bound dependency paths."), version: 2),
        Pattern(
            "csharp.persistence.repository-transaction",
            "Repositories participate in explicit transaction boundaries",
            "Recognizes repository calls coordinated by a unit-of-work or transaction boundary.",
            ["csharp"], ["database", "worker", "backend-api-producer"], ["persistence"], ["compiler.symbols", "compiler.calls", "compiler.interfaces", "compiler.dataflow"],
            Selector(kind: "symbol", subtype: "method", facets: ["command-handler"], propertyContains: [("callSemantics", "state-write")]),
            [Outgoing("has-dataflow", Selector(kind: "dataflow", propertyContains: [("invariants", "repository-before-commit")]))], [], 3, 0.85,
            Candidate("persistence", "SHOULD", "Material business commands SHOULD coordinate repository writes through one explicit transaction boundary.", "architecture-test", "Trace repository writes and commit calls within compiler-resolved command paths."), version: 2),
        Pattern(
            "csharp.events.transactional-outbox",
            "State changes and outbox records commit atomically",
            "Recognizes transactional write paths that add an outbox record before commit.",
            ["csharp"], ["event-producer"], ["events", "persistence"], ["compiler.symbols", "compiler.calls", "compiler.interfaces", "compiler.dataflow"],
            Selector(kind: "symbol", subtype: "method", propertyContains: [("callSemantics", "outbox-write")]),
            [Outgoing("has-dataflow", Selector(kind: "dataflow", propertyContains: [("invariants", "outbox-atomic-commit")]))], [], 3, 0.90,
            Candidate("events", "MUST", "Authoritative state and its outbox record MUST commit atomically before external event publication.", "architecture-test", "Trace state writes, outbox writes, commit, and publication order through compiler dataflow."), version: 2),
        Pattern(
            "csharp.events.idempotent-consumer",
            "Message consumers are retry-safe",
            "Recognizes message consumers with explicit deduplication or idempotency boundaries.",
            ["csharp"], ["event-consumer"], ["events"], ["compiler.symbols", "compiler.interfaces", "compiler.calls", "compiler.dataflow"],
            Selector(kind: "symbol", subtype: "method", facets: ["message-consumer"]),
            [Outgoing("has-dataflow", Selector(kind: "dataflow", propertyContains: [("invariants", "idempotency-before-effects")]))], [], 3, 0.85,
            Candidate("events", "MUST", "Material message consumers MUST be retry-safe and use an explicit idempotency mechanism.", "architecture-test", "Resolve consumer interfaces and verify deduplication or idempotency calls before material effects."), version: 2),
        Pattern(
            "csharp.configuration.options-binding",
            "Configuration is bound to typed options",
            "Recognizes configuration registration through typed options rather than scattered string lookups.",
            ["csharp"], ["backend-api-producer", "worker", "tooling"], ["configuration"], ["compiler.symbols", "compiler.external-calls", "compiler.generic-arguments"],
            Selector(kind: "symbol", subtype: "method", propertyContains: [("callSemantics", "configuration")]),
            [Outgoing("binds-options", Selector(kind: "symbol", facets: ["external"]))],
            [Outgoing("reads-configuration", Selector(kind: "symbol"))], 3, 0.80,
            Candidate("configuration", "SHOULD", "Runtime configuration SHOULD be bound to validated typed options at the composition boundary.", "architecture-test", "Inspect compiler-bound options registration and direct configuration-indexer usage."), version: 3),
        Pattern(
            "csharp.observability.structured-logging",
            "Operational telemetry uses stable structured events",
            "Recognizes structured logging calls with stable message templates or event identities.",
            ["csharp"], ["backend-api-producer", "worker", "tooling"], [], ["compiler.symbols", "compiler.external-calls", "compiler.invocation-arguments"],
            Selector(kind: "symbol", subtype: "method", propertyContains: [("callSemantics", "logging")]),
            [Outgoing("structured-log", Selector(kind: "symbol"))],
            [Outgoing("unstructured-log", Selector(kind: "symbol"))], 5, 0.80,
            Candidate("operations", "SHOULD", "Material operations SHOULD emit structured telemetry with stable event identity, outcome, and correlation context.", "manual-review", "Inspect compiler-bound logging calls, templates, event IDs, and sensitive-data handling."), version: 3),
        Pattern(
            "csharp.generated-code.exclusion",
            "Generated code is excluded from convention prevalence",
            "Recognizes compiler-generated and tool-generated declarations so they do not distort repository convention inference.",
            ["csharp"], [], [], ["compiler.symbols", "compiler.generated-markers"],
            Selector(kind: "symbol", facets: ["generated"]), [], [], 1, 1.0,
            Candidate("repository-governance", "MUST", "Generated code MUST be identified and excluded from inferred convention prevalence unless a reviewed pattern explicitly includes it.", "deterministic", "Check generated-code facets, attributes, headers, and generated source locations."), version: 2),
    ];

    private static CisStandardPatternDefinition Pattern(
        string id, string title, string description,
        IReadOnlyList<string> languages, IReadOnlyList<string> roles, IReadOnlyList<string> capabilities,
        IReadOnlyList<string> graphCapabilities, CisStandardPatternSelector subject,
        IReadOnlyList<CisStandardPatternRelation> requires, IReadOnlyList<CisStandardPatternRelation> forbids,
        int minimumOccurrences, double minimumConsistency, CisStandardPatternCandidate candidate, int version = 1)
        => new(id, version, title, description, "Active", languages, roles, capabilities, graphCapabilities, subject, requires, forbids,
            minimumOccurrences, minimumConsistency, candidate, "cis.builtin.standard-patterns", "builtin", "CIS known-standard-pattern catalogue");

    private static CisStandardPatternSelector Selector(
        string? kind = null,
        string? subtype = null,
        IReadOnlyList<string>? facets = null,
        IReadOnlyList<(string Key, string Value)>? propertyEquals = null,
        IReadOnlyList<(string Key, string Value)>? propertyContains = null,
        IReadOnlyList<string>? pathPrefixes = null,
        IReadOnlyList<string>? excludedPathPrefixes = null)
        => new(kind, subtype, facets ?? [],
            (propertyEquals ?? []).ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
            (propertyContains ?? []).ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
            pathPrefixes ?? [], excludedPathPrefixes ?? []);

    private static CisStandardPatternRelation Outgoing(string edgeType, CisStandardPatternSelector target)
        => new("outgoing", edgeType, target);

    private static CisStandardPatternCandidate Candidate(string target, string level, string wording, string enforcement, string verification)
        => new(target, level, wording, enforcement, verification);
}
