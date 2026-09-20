using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd;

/// <summary>Introduces proposed feature scope without claiming backlog or implementation approval.</summary>
public sealed partial class FeatureIntakeService(
    ICisWorkspaceRegistry workspaces,
    RepositoryImporter importer,
    DocumentationCatalogMerger catalog,
    IEnumerable<ICisProductDefinitionAuthority> definitions,
    IEnumerable<ICisFeatureScreenGenerator>? screenGenerators = null,
    IEnumerable<ICisFeatureArchitectureGenerator>? architectureGenerators = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string RecordMarker = "<!-- cis:feature-intake\n";

    public CisFeatureIntakeResult Import(string workspacePath, CisFeatureIntakeRequest request,
        bool dryRun, bool confirmed, string? expectedPlan = null)
    {
        try { return ImportCore(workspacePath, request, dryRun, confirmed, expectedPlan); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException or System.ComponentModel.Win32Exception)
        { return new("invalid", null, [exception.Message], [], false); }
    }

    private CisFeatureIntakeResult ImportCore(string workspacePath, CisFeatureIntakeRequest request,
        bool dryRun, bool confirmed, string? expectedPlan)
    {
        var resolved = workspaces.Resolve(workspacePath);
        if (!resolved.IsSuccess || resolved.Workspace?.AuthorityRepository is null)
            return Invalid(resolved.Errors.Count > 0 ? resolved.Errors : ["Select an initialized product authority."]);
        var workspace = resolved.Workspace;
        var authority = workspace.AuthorityRepository!;
        if (request is null || !SingleLine(request.Title, 200) || !SingleLine(request.Actor, 200)
            || string.IsNullOrWhiteSpace(request.Slug)
            || !Regex.IsMatch(request.Slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)
            || request.Slug.Length > 80 || request.RepositoryMode is not ("new" or "existing")
            || request.IntegrationRepositories is null || request.IntegrationRepositories.Count > 20)
            return Invalid(["Provide a title, actor, lowercase feature slug, repository choice and up to 20 integration repositories."]);
        var repo = Path.GetFullPath(request.RepositoryPath);
        var parent = Path.GetDirectoryName(repo);
        if (parent is null || !Directory.Exists(parent) || !SafeAbsolutePath(repo)
            || !CisPathSafety.TryResolveUnderRoot(repo, request.DocumentationRoot, out _))
            return Invalid(["Choose a safe repository folder with an existing parent and a contained documentation root."]);
        if (workspace.Repositories.Any(item =>
            CisPathSafety.IsUnderRoot(item.RepositoryPath, repo) || CisPathSafety.IsUnderRoot(repo, item.RepositoryPath)))
            return Invalid(["The feature repository cannot contain or be nested inside a registered repository."]);
        var registered = workspace.Repositories.FirstOrDefault(item => SamePath(item.RepositoryPath, repo));
        if (registered is not null && (!registered.IsProductOwned || registered.Role != "participant"
            || registered.DocumentationRoot != request.DocumentationRoot))
            return Invalid(["Choose a product-owned participant with its existing documentation root. A feature cannot reclassify an authority or external dependency."]);
        var integrations = request.IntegrationRepositories.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (integrations.Any(id => !workspace.Repositories.Any(item => item.Id == id && item.Role == "participant"
                && !SamePath(item.RepositoryPath, repo))))
            return Invalid(["An integration target is unknown, is the authority, or is the feature repository itself."]);
        var source = Path.GetFullPath(request.SourcePath);
        if (!File.Exists(source) || !string.Equals(Path.GetExtension(source), ".md", StringComparison.OrdinalIgnoreCase)
            || !SafeAbsolutePath(source) || new FileInfo(source).Length is 0 or > 2_097_152)
            return Invalid(["Select a local Markdown BRD of up to 2 MiB. Links and empty files are not accepted."]);
        var sourceBytes = File.ReadAllBytes(source);
        var content = new UTF8Encoding(false, true).GetString(sourceBytes);
        if (content.Contains('\0')) return Invalid(["The requirements document must be UTF-8 text without null characters."]);
        var sourceHash = Hash(sourceBytes);
        var requestRelative = $"{authority.DocumentationRoot}/specs/feature-requests/{request.Slug}/request.md";
        var sourceRelative = $".cis/inputs/features/{request.Slug}/source.md";
        if (!CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, requestRelative, out var requestPath)
            || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, sourceRelative, out var sourcePath)
            || !SafeAbsolutePath(requestPath) || !SafeAbsolutePath(sourcePath))
            return Invalid(["The feature request or retained source uses an unsafe path."]);
        var binding = definitions.Select(item => item.Evaluate(authority.RepositoryPath)).FirstOrDefault(item => item.Applicable);
        var warnings = new List<string>();
        if (binding is { Active: false }) warnings.Add("The product baseline needs attention. Intake may be recorded as Draft; feature approval and planning remain blocked until the baseline is current.");
        var decisions = OpenDecisions(content);
        var fingerprint = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { title = request.Title.Trim(), request.Slug, sourceHash, repository = repo, request.DocumentationRoot, integrations, actor = request.Actor.Trim() }, Json)));
        var registry = File.ReadAllBytes(workspace.ConfigurationPath);
        var planHash = Hash(Encoding.UTF8.GetBytes(fingerprint + "|" + Hash(registry) + "|" + request.RepositoryMode
            + "|" + binding?.BaselineHash + "|" + Directory.Exists(repo)));
        var plan = new CisFeatureIntakePlan(request.Title.Trim(), request.Slug, repo, request.RepositoryMode,
            request.DocumentationRoot, requestRelative, sourceRelative, sourceHash, integrations, decisions, binding?.BaselineHash, planHash);
        var catalogPath = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot, "catalog.yml");
        if (!SafeAbsolutePath(catalogPath)) return Invalid(["The authority catalog uses an unsafe path."]);
        var originalCatalog = File.ReadAllText(catalogPath);
        CatalogArtifactEntry[] catalogEntries = [new($"{authority.Id}:feature-intake:{request.Slug}", requestRelative, "feature-intake", "draft", "canonical")];
        var merged = catalog.Merge(authority.Id, originalCatalog, catalogEntries);
        if (merged.Collisions.Count > 0) return new("collision", plan, merged.Collisions, warnings, false);
        if (File.Exists(requestPath))
        {
            var record = ReadRecord(requestPath);
            if (record?.Fingerprint == fingerprint && registered is not null && File.Exists(sourcePath)
                && Hash(File.ReadAllBytes(sourcePath)) == sourceHash && !merged.Changed)
                return new("unchanged", record.Plan, [], warnings, false);
            return new("collision", plan, ["This feature slug already has an intake. Open the existing request or choose a different slug; its source and review will not be overwritten."], warnings, false);
        }
        if (File.Exists(sourcePath)) return new("collision", plan, ["The retained source path already exists. Review the earlier intake before retrying."], warnings, false);
        if (request.RepositoryMode == "new" && (Directory.Exists(repo) || File.Exists(repo)))
            return new("collision", plan, ["The new repository folder already exists. Select Use existing repository or choose another folder."], warnings, false);
        if (request.RepositoryMode == "existing" && !Directory.Exists(repo)) return Invalid(["The selected existing repository does not exist."]);
        if (request.RepositoryMode == "existing" && registered is null)
        {
            var preview = importer.Import(new(workspace.WorkspacePath, request.DocumentationRoot, [repo], true, false, "owned", "none"));
            if (preview.ExitCode != 0) return new("blocked", plan, preview.Errors.Concat(preview.Collisions).ToArray(), preview.Warnings, false);
            warnings.AddRange(preview.Warnings);
        }
        if (integrations.Any(id => workspace.Repositories.Single(item => item.Id == id).IsDependency))
            warnings.Add("Selected external dependencies supply integration context only. Their implementation remains under their own product authority.");
        if (dryRun) return new("preview", plan, [], warnings, false);
        if (!confirmed) return new("confirmation-required", plan, [], warnings, false, true);
        if (string.IsNullOrWhiteSpace(expectedPlan) || expectedPlan != planHash)
            return new("stale-preview", plan, ["The request or workspace changed. Review a fresh preview before creating the feature."], warnings, false);

        // Serialize intake applies across processes. A cancelled preview has written nothing.
        var lockPath = Path.Combine(authority.RepositoryPath, ".cis/local/feature-intake.lock");
        if (!SafeAbsolutePath(lockPath)) return Invalid(["The feature intake lock uses an unsafe path."]);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        using var intakeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(requestPath) || !File.ReadAllBytes(workspace.ConfigurationPath).SequenceEqual(registry)
            || File.ReadAllText(catalogPath) != originalCatalog || Hash(File.ReadAllBytes(source)) != sourceHash)
            return new("stale-preview", plan, ["The input changed while preparing intake. Review a fresh preview."], warnings, false);
        var repositoryCreated = false;
        try
        {
            if (request.RepositoryMode == "new")
            {
                if (Directory.Exists(repo)) throw new IOException("The repository folder appeared after preview; no files were overwritten.");
                Directory.CreateDirectory(repo);
                repositoryCreated = true;
                var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var argument in new[] { "init", "--initial-branch=main", "--", repo }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Git could not start.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000)) { process.Kill(true); throw new IOException("Git initialization timed out."); }
                Task.WaitAll(stdout, stderr);
                if (process.ExitCode != 0) throw new IOException("Git initialization failed: " + stderr.Result.Trim());
            }
            if (registered is null)
            {
                var imported = importer.Import(new(workspace.WorkspacePath, request.DocumentationRoot, [repo], false, true, "owned", "none"));
                if (imported.ExitCode != 0) throw new IOException("Repository setup needs attention: " + string.Join("; ", imported.Errors.Concat(imported.Collisions)));
                warnings.AddRange(imported.Warnings);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
            using (var stream = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(sourceBytes);
            var record = new IntakeRecord(fingerprint, request.Actor.Trim(), DateTimeOffset.UtcNow.ToString("O"), plan);
            using (var stream = new StreamWriter(new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(false)))
                stream.Write(Render(authority.Id, requestPath, sourcePath, record, workspace));
            // Import can take time. Merge against the latest catalog so unrelated additions are retained.
            var currentCatalog = File.ReadAllText(catalogPath);
            var latest = catalog.Merge(authority.Id, currentCatalog, catalogEntries);
            if (latest.Collisions.Count > 0) throw new IOException(string.Join("; ", latest.Collisions));
            var temporaryCatalog = catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryCatalog, latest.Content, new UTF8Encoding(false));
                if (File.ReadAllText(catalogPath) != currentCatalog) throw new IOException("The catalog changed during setup. Review the preserved request before retrying.");
                File.Move(temporaryCatalog, catalogPath, overwrite: true);
            }
            finally { if (File.Exists(temporaryCatalog)) File.Delete(temporaryCatalog); }
            return new("created", plan, [], warnings.Distinct().ToArray(), true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Preserve any generated repository or source so recovery cannot delete user work.
            return new("setup-incomplete", plan,
                [$"Feature setup stopped: {exception.Message}", "Any created files were preserved. Inspect the repository and intake paths before retrying."],
                warnings, repositoryCreated || File.Exists(sourcePath));
        }
    }

    public static IReadOnlyList<string> OpenDecisions(string content)
    {
        var section = Regex.Match(content, @"(?im)^#{1,3}\s+(?:\d+[.)]?\s+)?(?:Open (?:business )?(?:decisions|questions)|Unresolved (?:decisions|questions))\s*\r?$\n(?<body>.*?)(?=^#{1,3}\s|\z)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!section.Success) return [];
        return section.Groups["body"].Value.Split('\n').Select(line => line.Trim())
            .Where(line => Regex.IsMatch(line, @"^(?:\d+[.)]|[-*])\s+", RegexOptions.CultureInvariant))
            .Select(line => Regex.Replace(line, @"^(?:\d+[.)]|[-*])\s+", "")).Take(200).ToArray();
    }

    private static string Render(string authorityId, string requestPath, string sourcePath, IntakeRecord record, CisWorkspace workspace)
    {
        var p = record.Plan;
        var link = Path.GetRelativePath(Path.GetDirectoryName(requestPath)!, sourcePath).Replace('\\', '/');
        var integrations = p.IntegrationRepositories.Select(id => workspace.Repositories.Single(item => item.Id == id))
            .Select(item => $"- `{item.Id}` — {(item.IsProductOwned ? "product-owned integration target" : "external integration context; owned elsewhere")}");
        return $"""
---
title: {JsonSerializer.Serialize(p.Title)}
type: feature-intake
status: Draft
scope: Workspace
owner: {JsonSerializer.Serialize(record.Actor)}
last_reviewed: null
review_cadence: on feature scope change
cis:
  stable_id: {authorityId}:feature-intake:{p.Slug}
  source_hash: {p.SourceHash}
  product_definition_hash: {JsonSerializer.Serialize(p.ProductDefinitionHash)}
---

# {p.Title}

This proposed feature was introduced by {record.Actor} on {record.CreatedAt}. It requires scope review before backlog approval, feature planning or implementation.

## Prepared business requirements

[Open the original BRD]({link})

The source is retained byte-for-byte. Its instructions, assumptions and examples are draft requirements, not recorded human decisions. Existing product approvals are retained; no requirements have been adopted automatically.

## Implementation repository

`{p.RepositoryPath}` — product-owned participant; CIS documentation: `{p.DocumentationRoot}`.

## Integration scope

{(p.IntegrationRepositories.Count == 0 ? "No integration targets selected." : string.Join('\n', integrations))}

Selection records intended integration scope. API, event and ownership contracts still need validation against the existing implementation.

## Open decisions from the BRD

{(p.OpenDecisions.Count == 0 ? "No numbered open-decision section was detected. Review the source for remaining assumptions." : string.Join('\n', p.OpenDecisions.Select((decision, i) => $"{i + 1}. {decision}")))}

## Next review

Review the source and resolve its open decisions. Identify the product-requirement and architecture changes needed for this feature. Those reviewed changes must enter the governed backlog before Start Feature Specification and implementation planning are available. This intake is not an approved backlog item.

{RecordMarker}{JsonSerializer.Serialize(record, Json)}
-->
""";
    }

    private static IntakeRecord? ReadRecord(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = text.IndexOf(RecordMarker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf("\n-->", start, StringComparison.Ordinal);
        return end < 0 ? null : JsonSerializer.Deserialize<IntakeRecord>(text[(start + RecordMarker.Length)..end], Json);
    }
    private static bool SingleLine(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && !value.Any(char.IsControl);
    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool SamePath(string a, string b) => Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar)
        .Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), CisPathSafety.PlatformComparison);
    private static bool SafeAbsolutePath(string path) => !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(path)!, path);
    private static CisFeatureIntakeResult Invalid(IReadOnlyList<string> errors) => new("invalid", null, errors, [], false);
    private sealed record IntakeRecord(string Fingerprint, string Actor, string CreatedAt, CisFeatureIntakePlan Plan)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<SourceRevision>? SourceRevisions { get; init; }
    }
    private sealed record SourceRevision(string SourcePath, string SourceHash, string RequestPath, string Actor, string UpdatedAt);
}
