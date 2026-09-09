using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed partial class RepositoryInitializer
{
    private static readonly string[] ObservedFamilies =
    ["api-dictionary", "data-dictionary", "erd", "workflow-state-dictionary", "permissions-dictionary", "screen-route-map",
        "command-dictionary", "event-dictionary", "projection-dictionary", "problem-details-catalogue", "module-ownership-map",
        "configuration-dictionary", "package-catalogue", "business-invariant-catalogue", "traceability-matrix"];

    /// <summary>Explicit preparation of draft inventories before existing-system BRD inference.</summary>
    public CisReferencePreparationResult PrepareObservedReferences(string repositoryPath, bool apply = true)
        => PrepareReferenceArtifacts(repositoryPath, apply, null);

    private CisReferencePreparationResult PrepareReferenceArtifacts(string repositoryPath, bool apply,
        IReadOnlyList<RepositoryStarterArtifact>? suppliedArtifacts)
    {
        var inventories = new List<CisReferencePreparationItem>(); var warnings = new List<string>();
        var resolution = new CisRepositoryContextResolver().Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new(repositoryPath, [], [], resolution.Errors, false);
        var context = resolution.Context; var root = context.RepositoryPath;
        var manifestPath = Path.Combine(root, ".cis", "starter-manifest.yml");
        var read = _manifestStore.Read(manifestPath);
        if (read.Errors.Count > 0 || read.Manifest is null) return new(root, [], [], read.Errors, false);
        var managed = read.Manifest.ManagedArtifacts.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var artifacts = suppliedArtifacts ?? _starterBinder.Bind(root, context.RepositoryId, context.DocumentationRoot, _classifier.Classify(root)).Artifacts;
        var changes = new List<(string Path, string? Before, string After)>();
        foreach (var artifact in artifacts.Where(item => item.Definition.StartsWith("reference.", StringComparison.Ordinal)
                     && ObservedFamilies.Contains(Path.GetFileNameWithoutExtension(item.RelativePath), StringComparer.Ordinal)))
        {
            var kind = Path.GetFileNameWithoutExtension(artifact.RelativePath);
            if (!CisPathSafety.TryResolveUnderRoot(root, artifact.RelativePath, out var path)
                || CisPathSafety.ContainsReparsePoint(root, path))
                return new(root, inventories, warnings, [$"Unsafe dictionary path: {artifact.RelativePath}"], false);
            var generated = ReferenceTable(artifact.Content);
            if (generated is null) continue;
            var newRows = generated.Value.Rows.Where(row => !PlaceholderRow(row)).ToArray();
            var scoped = generated.Value.Header.EndsWith(" Repository |", StringComparison.Ordinal);
            var duplicate = newRows.GroupBy(row => ReferenceIdentity(kind, row, scoped), StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
                return new(root, inventories, warnings, [$"Ambiguous discovered identity in {artifact.RelativePath}: {duplicate.Key}. Resolve the source declarations before preparation."], false);
            var before = File.Exists(path) ? File.ReadAllText(path) : null;
            var after = artifact.Content; var action = before is null ? "created" : "refreshed"; var added = newRows.Length;
            managed.TryGetValue(artifact.RelativePath, out var prior);
            var pristine = before is not null && prior?.Ownership == "managed" && prior.AppliedHash == ComputeHash(before)
                && Regex.IsMatch(before, @"(?m)^status: Draft\s*$");
            if (before is not null && !pristine)
            {
                // Changed or reviewed dictionaries are human-owned. Append only missing draft identities;
                // never replace their rows, status, prose, or approval metadata.
                var current = ReferenceTable(before);
                var mergeBefore = before;
                if (scoped && current is not null && generated.Value.Header == current.Value.Header + " Repository |"
                    && Regex.IsMatch(before, @"(?m)^status: Draft\s*$"))
                {
                    // Add scope without changing any existing cell or claiming its repository.
                    mergeBefore = AddRepositoryColumn(before, current.Value);
                    current = ReferenceTable(mergeBefore);
                }
                if (!Regex.IsMatch(before, @"(?m)^status: Draft\s*$") || current is null || current.Value.Header != generated.Value.Header)
                {
                    warnings.Add($"Preserved {artifact.RelativePath}; its reviewed status or table schema requires manual reconciliation ({newRows.Length} source rows discovered).");
                    inventories.Add(new(kind, artifact.RelativePath, newRows.Length, 0, "preserved"));
                    continue;
                }
                var identities = current.Value.Rows.Select(row => ReferenceIdentity(kind, row, scoped)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = newRows.Where(row => !identities.Contains(ReferenceIdentity(kind, row, scoped))).ToArray();
                added = missing.Length; action = "extended";
                var lines = mergeBefore.Replace("\r\n", "\n").Split('\n').ToList();
                lines.InsertRange(current.Value.End, missing);
                if (newRows.Length > 0) lines.RemoveAll(line => line.StartsWith('|') && PlaceholderRow(line));
                after = string.Join('\n', lines);
                if (newRows.Length > 0) after = after.Replace("No deterministic facts were extracted. Replace the placeholder row with verified repository facts.",
                    "CIS discovered implementation facts. Existing human rows are retained; review all inferred rows before approval.", StringComparison.Ordinal);
                if (before.Contains("\r\n", StringComparison.Ordinal)) after = after.Replace("\n", "\r\n");
                warnings.Add($"Preserved existing rows in {artifact.RelativePath}; discovered changes to existing identities require review.");
            }
            var renderedAssets = false;
            if (kind == "erd")
            {
                ErdDiagramRenderer.Result diagrams;
                try { diagrams = ErdDiagramRenderer.Render(after, context.RepositoryId); }
                catch (InvalidDataException exception) { return new(root, inventories, warnings, [exception.Message], false); }
                after = diagrams.Markdown;
                foreach (var asset in diagrams.Assets)
                {
                    var relative = Path.GetDirectoryName(artifact.RelativePath)!.Replace('\\', '/') + "/" + asset.RelativePath;
                    if (!CisPathSafety.TryResolveUnderRoot(root, relative, out var assetPath) || CisPathSafety.ContainsReparsePoint(root, assetPath))
                        return new(root, inventories, warnings, [$"Unsafe ERD diagram path: {relative}"], false);
                    if (File.Exists(assetPath))
                    {
                        if (File.ReadAllText(assetPath) != asset.Content)
                            return new(root, inventories, warnings, [$"Preserved modified ERD diagram: {relative}. Restore or move it before regenerating this content-addressed asset."], false);
                    }
                    else { changes.Add((assetPath, null, asset.Content)); renderedAssets = true; }
                }
            }
            if (before == after) { action = renderedAssets ? "rendered" : "unchanged"; added = 0; }
            else
            {
                changes.Add((path, before, after));
                if (pristine || before is null) managed[artifact.RelativePath] = prior is null
                    ? new(artifact.Id, artifact.RelativePath, artifact.Definition, 1, ComputeHash(after))
                    : prior with { AppliedHash = ComputeHash(after) };
            }
            inventories.Add(new(kind, artifact.RelativePath, newRows.Length, added, action));
        }
        var selected = artifacts.Where(item => item.Definition.StartsWith("reference.", StringComparison.Ordinal)).ToArray();
        foreach (var artifact in selected.Where(item => item.RelativePath.EndsWith("-spec.md", StringComparison.Ordinal)))
        {
            if (!CisPathSafety.TryResolveUnderRoot(root, artifact.RelativePath, out var path) || CisPathSafety.ContainsReparsePoint(root, path))
                return new(root, inventories, warnings, [$"Unsafe reference specification path: {artifact.RelativePath}"], false);
            if (!File.Exists(path))
            {
                changes.Add((path, null, artifact.Content));
                managed[artifact.RelativePath] = new(artifact.Id, artifact.RelativePath, artifact.Definition, 1, ComputeHash(artifact.Content));
            }
        }
        var catalogBefore = File.ReadAllText(context.CatalogPath);
        var catalog = _catalogMerger.Merge(context.RepositoryId, catalogBefore,
            selected.Where(item => item.CatalogEntry is not null).Select(item => item.CatalogEntry!).ToArray());
        if (catalog.Collisions.Count > 0) return new(root, inventories, warnings, catalog.Collisions, false);
        if (catalog.Changed) changes.Add((context.CatalogPath, catalogBefore, catalog.Content));
        if (changes.Count > 0 && apply)
        {
            var manifestBefore = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
            var manifestAfter = _manifestStore.Write(read.Manifest with { ManagedArtifacts = managed.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray() });
            if (manifestBefore != manifestAfter) changes.Add((manifestPath, manifestBefore, manifestAfter));
            // Validate every target before the first write. Retain a content-addressed recovery copy.
            var backupDirectory = Path.Combine(root, ".cis", "local", "reference-preparation", "backups");
            if (CisPathSafety.ContainsReparsePoint(root, backupDirectory)) return new(root, inventories, warnings, ["Unsafe recovery directory."], false);
            foreach (var change in changes)
                if (CisPathSafety.ContainsReparsePoint(root, change.Path)
                    || (File.Exists(change.Path) ? File.ReadAllText(change.Path) : null) != change.Before)
                    return new(root, inventories, warnings, [$"File changed during inventory preparation: {change.Path}"], false);
            foreach (var change in changes)
            {
                if (change.Before is not null)
                {
                    var backup = Path.Combine(backupDirectory, ComputeHash(change.Before)[7..] + ".txt");
                    if (CisPathSafety.ContainsReparsePoint(root, backup)) return new(root, inventories, warnings, ["Unsafe recovery directory."], false);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    if (!File.Exists(backup)) File.WriteAllText(backup, change.Before, new UTF8Encoding(false));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(change.Path)!);
                var temporary = change.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, change.After, new UTF8Encoding(false));
                File.Move(temporary, change.Path, true);
            }
        }
        return new(root, inventories, warnings, [], changes.Count > 0 && apply);
    }

    private static (string Header, string[] Rows, int End)? ReferenceTable(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!lines[index].StartsWith('|') || !Regex.IsMatch(lines[index + 1], @"^\|[\s|:-]+\|$")) continue;
            var end = index + 2; while (end < lines.Length && lines[end].StartsWith('|')) end++;
            return (lines[index], lines[(index + 2)..end], end);
        }
        return null;
    }

    private static bool PlaceholderRow(string row) => ReferenceIdentity("", row) is "TODO" or "";
    private static string AddRepositoryColumn(string before, (string Header, string[] Rows, int End) table)
    {
        var lines = before.Replace("\r\n", "\n").Split('\n');
        var start = Array.IndexOf(lines, table.Header);
        lines[start] += " Repository |";
        lines[start + 1] += "---|";
        for (var index = start + 2; index < table.End; index++) lines[index] += "  |";
        var result = string.Join('\n', lines);
        return before.Contains("\r\n", StringComparison.Ordinal) ? result.Replace("\n", "\r\n") : result;
    }

    private static string ReferenceIdentity(string kind, string row, bool scoped = false)
    {
        var cells = Regex.Split(row.Trim().Trim('|'), @"(?<!\\)\|").Select(item => item.Trim().Trim('`')).ToArray();
        var indexes = kind switch { "data-dictionary" => new[] { 0, 1 }, "erd" => [0, 1, 2],
            "workflow-state-dictionary" => [0, 2], "screen-route-map" => [0, 1, 2],
            "configuration-dictionary" => [4, 1], "package-catalogue" => [1, 0], _ => [0] };
        return string.Join('\u001f', indexes.Select(index => index < cells.Length ? cells[index] : "")
            .Concat(scoped ? cells.TakeLast(1) : []));
    }
}
