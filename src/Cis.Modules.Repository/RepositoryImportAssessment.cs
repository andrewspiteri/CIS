using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed record RepositoryImportCoverage(string Id, string Title, string Status, string Action,
    IReadOnlyList<string> Sources, string Detail, string? Addition);
public sealed record RepositoryImportInventoryItem(string Path, string ContentHash);
public sealed record RepositoryImportAssessment(string RepositoryPath, string Method, bool InventoryComplete,
    IReadOnlyList<RepositoryImportCoverage> Coverage, IReadOnlyList<RepositoryImportInventoryItem> Inventory,
    IReadOnlyList<RepositoryGuidanceFinding> Findings, IReadOnlyList<string> Limitations, string InputHash)
{
    public IReadOnlyList<RepositoryImportCoverage> Setup { get; init; } = [];
}
public sealed record RepositoryImportPreview(string RepositoryPath, string RelativePath, string CurrentContent,
    string ProposedContent);

/// <summary>Local requirement evidence and exact configuration inventory; not a semantic audit.</summary>
internal static class RepositoryImportAssessmentBuilder
{
    internal const string GuidancePath = ".github/instructions/cis-import.instructions.md";
    internal const string ModePath = ".cis/import-mode";
    private const int MaximumFiles = 500;
    private const int MaximumCharacters = 4_000_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private sealed record Requirement(string Id, string Title, string[] Evidence, string Baseline);
    private static readonly Requirement[] Requirements =
    [
        new("navigation", "Read authoritative source evidence", [@"\b(read|open|inspect|consult)\b", @"\b(source|authoritative|canonical|evidence)\b"],
            "Open authoritative source documents before relying on generated summaries or changing behavior."),
        new("security", "Backend authorization", [@"\b(authoriz\w*|authent\w*)\b", @"\b(backend|server|endpoint|API|access)\b"],
            "Enforce authorization at the backend boundary; client-side visibility is not an access control."),
        new("security-secrets", "Secrets and credentials", [@"\b(secret\w*|credential\w*|tokens?|passwords?)\b", @"\b(never|avoid|protect|prevent|redact|do not|must not|don't)\b"],
            "Never commit credentials or expose secrets in logs, generated context or model requests. Use approved secret storage and redaction."),
        new("security-denied", "Denied access and tenant isolation tests", [@"\b(test\w*|verify|assert\w*)\b", @"\b(unauthenticated|wrong.role|cross.tenant|tenant.isolation|denied)\b"],
            "Test applicable unauthenticated, wrong-role and cross-tenant cases alongside allowed behavior."),
        new("security-scans", "Security scan evidence", [@"\b(scan\w*|SAST|DAST|CodeQL|semgrep|gitleaks|trivy)\b", @"\b(run|result\w*|evidence|findings|fail\w*|gate\w*)\b"],
            "Use the adopted security scanners and record actual findings and execution limits. Do not treat a configured scanner as a passed check or replace a CI gate during import."),
        new("security-exceptions", "Review security exceptions", [@"\b(security|risk|vulnerabilit\w*|finding\w*)\b", @"\b(exception\w*|waiver\w*|accept\w*)\b", @"\b(approv\w*|review\w*|expir\w*|owner)\b"],
            "Security exceptions require an identified reviewer, rationale, scope and expiry; import and model suggestions cannot accept risk."),
        new("testing", "Use real test commands", [@"\b(run|execute|command|invocation)\b", @"\b(dotnet test|npm|pnpm|yarn|pytest|cargo test|test command\w*|test suite\w*)\b"],
            "Use the repository's real build and test commands with their documented prerequisites; do not invent a runner or a passing result."),
        new("testing-results", "Report checks that ran and did not run", [@"\b(record|report|evidence|never claim)\b", @"\b(not run|did not run|skipped|could not|actually|executed|actual|exit code)\b", @"\b(test\w*|check\w*|validat\w*|result\w*)\b"],
            "Record executed commands, results and relevant checks that did not run, with the reason. Never claim a check passed without execution evidence."),
        new("browser", "Preserve the adopted browser harness", [@"\b(playwright|selenium|cypress|browser harness)\b", @"\b(use|adopt\w*|preserv\w*|runner|framework|dotnet|\.NET)\b"],
            "For browser work, preserve the adopted harness, including .NET Playwright where present. A missing harness requires a reviewed choice."),
        new("browser-state", "Verify actions and resulting state", [@"\b(browser|playwright|UI|end.to.end|e2e)\b", @"\b(assert\w*|verify|check\w*)\b", @"\b(state|behavior|behaviour|outcome|result|navigation)\b"],
            "Browser tests must verify actions and resulting state, including relevant denied and error states; a successful click alone is not evidence of correct behavior."),
        new("browser-data", "Deterministic browser data", [@"\b(browser|playwright|UI|e2e|test\w*)\b", @"\b(deterministic|seed\w*|isolated|repeatable)\b", @"\b(data|fixture\w*|database|tenant\w*)\b"],
            "Use deterministic, isolated data for browser tests and document application startup and cleanup prerequisites."),
        new("browser-auth", "Keep test authentication out of production", [@"\b(test\w*|browser|playwright)\b", @"\b(auth\w*|bypass\w*|identit\w*)\b", @"\b(production)\b", @"\b(never|isolat\w*|disabl\w*|prevent|reject|only|must not)\b"],
            "Test identities and authentication bypasses must remain isolated from production and must not weaken backend authorization."),
        new("browser-artifacts", "Retain browser failure evidence", [@"\b(browser|playwright|e2e)\b", @"\b(trace\w*|screenshot\w*|artifact\w*|video\w*)\b", @"\b(fail\w*|retain|capture|save)\b"],
            "Retain useful failure traces, screenshots or equivalent browser artifacts, with secrets and personal data excluded."),
        new("architecture", "Preserve adopted architecture and frameworks", [@"\b(architecture|framework\w*|boundar\w*|invariant\w*)\b", @"\b(approved|adopted|preserve|must|do not|never)\b"],
            "Preserve approved architecture, framework choices and module boundaries; review decisions before replacing an adopted approach."),
        new("contracts", "Check contract compatibility", [@"\b(contract\w*|API|schema\w*)\b", @"\b(compatib\w*|consumer\w*|caller\w*|breaking)\b"],
            "Identify affected callers and compatibility obligations before changing public APIs, events or schemas."),
        new("delivery", "Preserve human approval gates", [@"\b(approv\w*|review gate\w*)\b", @"\b(human|maintainer|stakeholder|release|deploy\w*|pull request)\b"],
            "Preserve required review and release gates. Generated drafts and import do not approve requirements, architecture, exceptions or deployment."),
    ];

    internal static RepositoryImportAssessment Analyze(string root, string documentationRoot, IReadOnlyList<RepositoryStarterArtifact> coreArtifacts)
    {
        var limits = new List<string>();
        var contents = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var candidates = new SortedSet<string>(StringComparer.Ordinal) { "AGENTS.md", "CLAUDE.md", ".github/copilot-instructions.md" };
        var generatedPaths = coreArtifacts.Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var complete = true;
        var total = 0;
        foreach (var relative in new[] { ".github/instructions", ".github/skills", ".github/agents", ".github/prompts" })
        {
            var directory = Path.Combine(root, relative);
            if (Directory.Exists(directory)) Visit(directory);
        }
        var inspected = new HashSet<string>(StringComparer.Ordinal);
        while (candidates.Count > 0)
        {
            var relative = candidates.Min!;
            candidates.Remove(relative);
            if (relative == GuidancePath || generatedPaths.Contains(relative) || RepositoryScanExclusions.IsPath(relative) || !inspected.Add(relative)) continue;
            if (!CisPathSafety.TryResolveUnderRoot(root, relative, out var absolute)
                || CisPathSafety.ContainsReparsePoint(root, absolute))
            { Exclude(relative, "unsafe or symbolic path"); continue; }
            if (RepositoryScanExclusions.IsPath(Path.GetRelativePath(root, absolute))) continue;
            if (!File.Exists(absolute))
            {
                fingerprints[relative] = "missing";
                if (relative is not ("AGENTS.md" or "CLAUDE.md" or ".github/copilot-instructions.md"))
                    Exclude(relative, "referenced file is missing");
                continue;
            }
            if (contents.Count >= MaximumFiles || new FileInfo(absolute).Length > 256_000 || total >= MaximumCharacters)
            { Exclude(relative, "inventory limit"); continue; }
            try
            {
                var text = File.ReadAllText(absolute);
                // Exclude our own entry-point block from topic detection so repeated imports stay stable.
                var evidence = RepositoryAgentsMerge.IsEntryPoint(relative)
                    ? Regex.Replace(text, @"<!-- cis:repository-guidance:start -->.*?<!-- cis:repository-guidance:end -->",
                        "", RegexOptions.Singleline | RegexOptions.CultureInvariant, Timeout).TrimEnd() : text;
                fingerprints[relative] = RepositoryAgentsMerge.IsEntryPoint(relative) && evidence.Length == 0
                    ? "missing" : RepositoryAgentsMerge.Hash(evidence);
                if (evidence.Length > 0) contents[relative] = evidence;
                total += text.Length;
                foreach (var link in RepositoryGuidanceDiscovery.References(root, relative, evidence))
                    if (!inspected.Contains(link)) candidates.Add(link);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { Exclude(relative, "unreadable file"); }
        }
        var findings = new List<RepositoryGuidanceFinding>();
        // Match all required evidence within one paragraph/list, never filenames or distant topic mentions.
        var blocks = contents.ToDictionary(item => item.Key, item => Regex.Split(
            Regex.Replace(item.Value, @"\A---\r?\n.*?\r?\n---(?:\r?\n|$)", "", RegexOptions.Singleline, Timeout),
            @"\r?\n\s*\r?\n", RegexOptions.CultureInvariant, Timeout));
        var coverage = Requirements.Select(requirement =>
        {
            var matches = blocks.Where(item => item.Value.Any(block => requirement.Evidence.All(pattern =>
                Regex.IsMatch(block, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout))))
                .Select(item => item.Key).ToArray();
            return new RepositoryImportCoverage(requirement.Id, requirement.Title,
                matches.Length > 0 ? "evidence-found-unverified" : complete ? "gap-in-inspected-guidance" : "unverified",
                matches.Length > 0 ? "preserve" : complete ? "add" : "defer", matches,
                matches.Length > 0 ? "Requirement-specific text found. Preserve it; scope, correctness and enforcement still require verification."
                    : complete ? "The inspected guidance did not contain all evidence for this requirement. Propose a scoped baseline; retain existing project policy."
                    : "Unreadable or omitted evidence prevents a reliable gap decision. Review those inputs first.",
                matches.Length == 0 && complete ? requirement.Baseline : null);
        }).ToArray();
        foreach (var item in contents)
        {
            if (Regex.IsMatch(item.Value, @"(?im)^.*\b(first|required|must|before)\b.*\b(parrctx|repo-index|routing|graph)\b|^.*\b(parrctx|repo-index)\b.*\b(first|required|must)\b",
                RegexOptions.CultureInvariant, Timeout))
                findings.Add(new("routing-review", item.Key,
                    "Existing routing precedence was found and retained. Reconcile it before expanding CIS to own repository-wide navigation; no conflict is resolved by this import."));
        }
        foreach (var topic in coverage.Where(item => item.Sources.Count > 1))
            findings.Add(new("overlap-candidate", topic.Id,
                $"{topic.Sources.Count} files contain evidence for {topic.Title.ToLowerInvariant()}. They may have different scopes; similarity does not authorize consolidation."));
        limits.Add("Local requirement evidence matching only. A match does not prove equivalent scope, correctness or enforcement. A missing match is a proposed gap, not proof that project guidance is absent. This is not a semantic merge.");
        limits.Add("Inspects root entry points, conventional GitHub guidance and discovered local references (500 files, 256 KB/file, 4 million characters). Other locations and implicit references may remain undiscovered.");
        limits.Add("Temporary, cache, build, dependency and generated runtime folders are intentionally excluded, including .github/tmp and .github/copilot-runtime. Links into these folders are not evidence.");
        limits.Add("No commands, tests, scanners or CI gates were executed. Framework choices, policy equivalence and automation dependencies require a focused follow-up review.");
        var setup = AssessSetup(root, documentationRoot, coreArtifacts, fingerprints, findings);
        var inventory = fingerprints.Select(item => new RepositoryImportInventoryItem(item.Key, item.Value)).ToArray();
        return new(root, "local-requirement-evidence", complete, coverage, inventory, findings, limits,
            RepositoryAgentsMerge.Hash(JsonSerializer.Serialize(new { version = 2, inventory, complete, coverage, setup }))) { Setup = setup };

        void Exclude(string path, string reason)
        {
            complete = false;
            fingerprints[path] = "excluded:" + reason;
            limits.Add($"Not inspected: {path} ({reason}).");
        }
        void Visit(string directory)
        {
            if (CisPathSafety.ContainsReparsePoint(root, directory))
            { Exclude(Path.GetRelativePath(root, directory), "symbolic directory"); return; }
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
                {
                    if (RepositoryScanExclusions.IsPath(Path.GetRelativePath(root, path))) continue;
                    if (candidates.Count >= MaximumFiles) { Exclude("additional guidance", "inventory limit"); return; }
                    if (CisPathSafety.ContainsReparsePoint(root, path)) { Exclude(Path.GetRelativePath(root, path), "symbolic path"); continue; }
                    if (Directory.Exists(path)) Visit(path);
                    else if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        candidates.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { Exclude(Path.GetRelativePath(root, directory), "unreadable directory"); }
        }
    }

    private static IReadOnlyList<RepositoryImportCoverage> AssessSetup(string root, string documentationRoot,
        IReadOnlyList<RepositoryStarterArtifact> artifacts, IDictionary<string, string> fingerprints,
        ICollection<RepositoryGuidanceFinding> findings)
    {
        var result = new List<RepositoryImportCoverage>();
        Check("repository-registration", "CIS repository registration", ".cis/repository.yml", true,
            "Register the selected documentation root for CIS commands.");
        foreach (var artifact in artifacts)
            Check(artifact.Id, artifact.Id.Replace("reference.", "", StringComparison.Ordinal).Replace('-', ' '),
                artifact.RelativePath, true, artifact.Id == "reference.test-suite-profile"
                    ? "Bind detected test projects and declared scripts to CIS. Review commands, prerequisites and report paths before running; this is not execution evidence."
                    : "Add the missing CIS runtime profile. These scoped defaults do not replace project directives or approve a model, source document or agent run.");
        if (artifacts.All(item => item.Id != "reference.test-suite-profile"))
            Check("reference.test-suite-profile", "Test command bindings", $"{documentationRoot}/references/test-suite-profile.md", false,
                "No supported test command could be inferred. Select and verify the adopted harness before configuring CIS testing; no substitute runner is installed.");
        Check("reference.security-suite-profile", "Security scanner bindings", $"{documentationRoot}/references/security-suite-profile.md", false,
            "Map the adopted scanners, commands, output formats and CI gates in a focused review. Security prose alone is not an executable binding; import does not select replacement scanners.");
        return result;

        void Check(string id, string title, string path, bool add, string detail)
        {
            var absolute = Path.Combine(root, path);
            var exists = File.Exists(absolute);
            var unsafePath = CisPathSafety.ContainsReparsePoint(root, absolute) || Directory.Exists(absolute);
            var unreadable = false;
            if (unsafePath) fingerprints[path] = "excluded:unsafe configuration path";
            else if (!exists) fingerprints[path] = "missing";
            else
            {
                try
                {
                    if (new FileInfo(absolute).Length > 256_000) unreadable = true;
                    else fingerprints[path] = RepositoryAgentsMerge.Hash(File.ReadAllText(absolute));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { unreadable = true; }
                if (unreadable) fingerprints[path] = "excluded:unreadable configuration";
            }
            var action = unsafePath || unreadable ? "defer" : exists ? "preserve" : add ? "add" : "defer";
            var explanation = unsafePath || unreadable ? "Configuration could not be safely inspected. Resolve the path or read failure before using this capability."
                : exists ? "Existing configuration will be preserved byte for byte. Run the capability's validation before use; file presence does not establish validity or approval."
                : detail;
            result.Add(new(id, title, exists ? "existing-unverified" : "missing-configuration", action, [path], explanation, null));
            if (action == "defer") findings.Add(new("capability-setup", path, explanation));
        }
    }

    internal static string Guidance(string documentationRoot, RepositoryImportAssessment assessment) =>
        "---\napplyTo: \"**\"\n---\n\n# CIS import essentials\n\n"
        + "Scope: CIS commands and newly created CIS artifacts. Existing project directives, authoritative documents, frameworks and approval gates remain in force. This file does not replace repository-wide routing or resolve conflicts.\n\n"
        + $"- CIS configuration is `.cis/repository.yml`; the selected CIS documentation root is `{documentationRoot}`. Existing documents elsewhere remain authoritative until explicitly reconciled.\n"
        + "- Use `cis repo doctor` to check registration and `cis graph build --workspace <workspace>` to refresh derived workspace context. Use `cis --help` and command help to discover capabilities; use the listed CIS runtime profiles and review inferred test bindings before execution. Security scanner bindings and other capability setup remain explicit follow-up work.\n"
        + "- Read source documents before relying on generated context. Imported evidence and generated drafts do not establish currency or approval. Preserve project-specific requirements and report contradictions for a focused decision.\n"
        + "- Keep automatic model selection local. Remote model use requires explicit authorization for the selected content; never send secrets or credentials.\n"
        + "- Review planned file changes before applying them. Record actual validation results and unresolved decisions; CIS does not grant deployment or policy approval.\n\n"
        + "## Proposed requirement baselines\n\nExisting project policy takes precedence. These additions apply only where that policy is silent; text matching does not prove full coverage.\n\n"
        + string.Join("", assessment.Coverage.Where(item => item.Addition is not null).Select(item => $"- **{item.Title}:** {item.Addition}\n"))
        + "\nThe local import assessment is available in `.cis/local/import/report.json`. Overlap, routing and unverified items remain follow-up work.\n";

    internal static string EntryPoint => "## CIS integration\n\nFor CIS commands and artifacts, read `.github/instructions/cis-import.instructions.md`. Existing project directives remain authoritative; unresolved overlaps are recorded in `.cis/local/import/report.json`.\n";
}
