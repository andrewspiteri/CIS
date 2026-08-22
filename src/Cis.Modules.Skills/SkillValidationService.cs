using System.Text.RegularExpressions;
using Cis.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Cis.Modules.Skills;

public sealed partial class SkillValidationService
{
    private static readonly string[] AllowedFrontMatterKeys = ["name", "description"];
    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public SkillValidationService(ICisRepositoryContextResolver repositoryContextResolver)
    {
        _repositoryContextResolver = repositoryContextResolver;
    }

    public SkillValidationResult Validate(string repositoryPath, bool strict, bool fix = false)
    {
        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            var diagnostics = resolution.Errors.Select(error => new SkillDiagnostic(
                "CIS-SKILL-CONFIG-001", "error", ".cis/repository.yml", error, [])).ToArray();
            return CreateResult(null, ".github/skills", strict, fix, [], [], diagnostics);
        }

        var context = resolution.Context!;
        var skillsRoot = Path.Combine(context.RepositoryPath, ".github", "skills");
        var diagnosticsList = new List<SkillDiagnostic>();
        var inventory = new List<SkillInventoryItem>();
        if (!Directory.Exists(skillsRoot))
        {
            diagnosticsList.Add(new SkillDiagnostic(
                "CIS-SKILL-ROOT-001",
                "error",
                ".github/skills",
                "The repository has no implementation skill directory.",
                ["Expected .github/skills/<skill-name>/SKILL.md."]));
            return CreateResult(context.RepositoryPath, ".github/skills", strict, fix, [], inventory, diagnosticsList);
        }

        var fixedPaths = fix
            ? ApplySafeFixes(context.RepositoryPath, skillsRoot, diagnosticsList)
            : [];

        foreach (var directory in Directory.EnumerateDirectories(skillsRoot)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            InspectSkill(context.RepositoryPath, directory, inventory, diagnosticsList);
        }

        foreach (var duplicate in inventory.GroupBy(skill => skill.Name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            diagnosticsList.Add(new SkillDiagnostic(
                "CIS-SKILL-NAME-003",
                "error",
                duplicate.First().Path,
                $"Skill name '{duplicate.Key}' is declared more than once.",
                duplicate.Select(skill => skill.Path).ToArray()));
        }

        return CreateResult(context.RepositoryPath, ".github/skills", strict, fix, fixedPaths, inventory, diagnosticsList);
    }

    private void InspectSkill(
        string repositoryPath,
        string directory,
        ICollection<SkillInventoryItem> inventory,
        ICollection<SkillDiagnostic> diagnostics)
    {
        var folderName = Path.GetFileName(directory);
        var relativeDirectory = Normalize(Path.GetRelativePath(repositoryPath, directory));
        if (!ValidNameRegex().IsMatch(folderName) || folderName.Length > 64)
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-NAME-001",
                "error",
                relativeDirectory,
                "Skill directory names must contain only lowercase letters, digits, and hyphens and be at most 64 characters.",
                [folderName]));
        }

        var path = Path.Combine(directory, "SKILL.md");
        var relativePath = Normalize(Path.GetRelativePath(repositoryPath, path));
        if (!File.Exists(path))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-FILE-001",
                "error",
                relativeDirectory,
                "A skill directory does not contain SKILL.md.",
                [relativePath]));
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-FILE-002", "error", relativePath,
                $"The skill file could not be read: {exception.Message}", []));
            return;
        }

        if (!TryExtractFrontMatter(content, out var yaml, out var body))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-YAML-001", "error", relativePath,
                "SKILL.md must begin with terminated YAML front matter.", []));
            return;
        }

        Dictionary<string, object>? metadata;
        try
        {
            metadata = _deserializer.Deserialize<Dictionary<string, object>>(yaml);
        }
        catch (YamlException exception)
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-YAML-002", "error", relativePath,
                $"SKILL.md has malformed YAML front matter: {exception.Message}", []));
            return;
        }

        metadata ??= new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var unknown in metadata.Keys.Except(AllowedFrontMatterKeys, StringComparer.Ordinal))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-YAML-003", "warning", relativePath,
                $"Front matter key '{unknown}' is not part of the portable CIS skill contract.",
                ["Portable keys: name, description."]));
        }

        var name = Scalar(metadata, "name");
        var description = Scalar(metadata, "description");
        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-NAME-002", "error", relativePath,
                "Skill front matter requires a non-empty name.", []));
            name = folderName;
        }
        else if (!string.Equals(name, folderName, StringComparison.Ordinal))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-NAME-004", "error", relativePath,
                "Skill front matter name must exactly match its directory name.",
                [$"directory={folderName}", $"name={name}"]));
        }

        if (!ValidNameRegex().IsMatch(name) || name.Length > 64)
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-NAME-005", "error", relativePath,
                "Skill name must contain only lowercase letters, digits, and hyphens and be at most 64 characters.",
                [name]));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-DESCRIPTION-001", "error", relativePath,
                "Skill front matter requires a non-empty description.", []));
        }
        else
        {
            if (description.Length > 1024)
            {
                diagnostics.Add(new SkillDiagnostic(
                    "CIS-SKILL-DESCRIPTION-002", "error", relativePath,
                    "Skill description exceeds the 1024-character portable limit.",
                    [$"length={description.Length}"]));
            }

        }

        if (string.IsNullOrWhiteSpace(body))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-BODY-001", "error", relativePath,
                "SKILL.md requires an instruction body after front matter.", []));
        }

        if (!HeadingRegex().IsMatch(body))
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-HEADING-001", "error", relativePath,
                "SKILL.md requires a level-one Markdown heading in its instruction body.", []));
        }

        var lineCount = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length;
        if (lineCount > 500)
        {
            diagnostics.Add(new SkillDiagnostic(
                "CIS-SKILL-BODY-002", "warning", relativePath,
                "SKILL.md exceeds 500 lines and should move detailed material into referenced resources.",
                [$"lines={lineCount}"]));
        }

        ValidateLinks(repositoryPath, path, body, relativePath, diagnostics);
        inventory.Add(new SkillInventoryItem(name, relativePath, description, lineCount));
    }

    private static void ValidateLinks(
        string repositoryPath,
        string skillPath,
        string body,
        string relativePath,
        ICollection<SkillDiagnostic> diagnostics)
    {
        foreach (Match match in MarkdownLinkRegex().Matches(body))
        {
            var target = match.Groups[1].Value.Trim().Split('#', 2)[0];
            if (string.IsNullOrWhiteSpace(target)
                || target.StartsWith('#')
                || Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                continue;
            }

            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(skillPath)!, target.Replace('/', Path.DirectorySeparatorChar)));
            if (!resolved.StartsWith(repositoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(resolved) && !Directory.Exists(resolved))
            {
                diagnostics.Add(new SkillDiagnostic(
                    "CIS-SKILL-LINK-001", "error", relativePath,
                    $"Referenced local skill resource does not exist or escapes the repository: {target}",
                    [target]));
            }
        }
    }

    private static SkillValidationResult CreateResult(
        string? repositoryPath,
        string skillsRoot,
        bool strict,
        bool fixRequested,
        IEnumerable<string> fixedPaths,
        IEnumerable<SkillInventoryItem> inventory,
        IEnumerable<SkillDiagnostic> diagnostics)
    {
        var orderedFixedPaths = fixedPaths.Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var orderedSkills = inventory.OrderBy(skill => skill.Name, StringComparer.Ordinal).ToArray();
        var orderedDiagnostics = diagnostics
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();
        var errors = orderedDiagnostics.Count(diagnostic => diagnostic.Severity == "error");
        var warnings = orderedDiagnostics.Count(diagnostic => diagnostic.Severity == "warning");
        var failed = errors > 0 || strict && warnings > 0;
        return new SkillValidationResult(
            failed ? "invalid" : warnings > 0 ? "valid-with-warnings" : "valid",
            failed ? 2 : 0,
            repositoryPath,
            skillsRoot,
            orderedSkills.Length,
            errors,
            warnings,
            strict,
            fixRequested,
            orderedFixedPaths.Length > 0,
            orderedFixedPaths,
            orderedSkills,
            orderedDiagnostics);
    }

    private IReadOnlyList<string> ApplySafeFixes(
        string repositoryPath,
        string skillsRoot,
        ICollection<SkillDiagnostic> diagnostics)
    {
        var fixedPaths = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(skillsRoot)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(path))
            {
                continue;
            }

            var relativePath = Normalize(Path.GetRelativePath(repositoryPath, path));
            try
            {
                var content = File.ReadAllText(path);
                var repaired = RepairContent(Path.GetFileName(directory), content);
                if (string.Equals(content, repaired, StringComparison.Ordinal))
                {
                    continue;
                }

                var temporaryPath = path + ".cis-fix-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporaryPath, repaired);
                    File.Move(temporaryPath, path, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                fixedPaths.Add(relativePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new SkillDiagnostic(
                    "CIS-SKILL-FIX-001", "error", relativePath,
                    $"The safe skill repair could not be applied: {exception.Message}", []));
            }
        }

        return fixedPaths;
    }

    private Dictionary<string, object>? TryReadMetadata(string yaml)
    {
        try
        {
            return _deserializer.Deserialize<Dictionary<string, object>>(yaml);
        }
        catch (YamlException)
        {
            return null;
        }
    }

    private string RepairContent(string folderName, string content)
    {
        var usesCrLf = content.Contains("\r\n", StringComparison.Ordinal);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        string yaml;
        string body;
        var changed = false;

        if (TryExtractFrontMatter(normalized, out yaml, out body))
        {
            var metadata = TryReadMetadata(yaml);
            if (metadata is null)
            {
                return content;
            }

            var additions = new List<string>();
            if (string.IsNullOrWhiteSpace(Scalar(metadata, "name")))
            {
                additions.Add($"name: {folderName}");
            }

            if (string.IsNullOrWhiteSpace(Scalar(metadata, "description")))
            {
                additions.Add($"description: \"{EscapeYaml(CreateDescription(folderName, body))}\"");
            }

            if (additions.Count > 0)
            {
                yaml = yaml.TrimEnd() + "\n" + string.Join("\n", additions);
                changed = true;
            }
        }
        else
        {
            if (normalized.StartsWith("---\n", StringComparison.Ordinal)
                || string.Equals(normalized, "---", StringComparison.Ordinal))
            {
                return content;
            }

            body = normalized;
            yaml = $"name: {folderName}\ndescription: \"{EscapeYaml(CreateDescription(folderName, body))}\"";
            changed = true;
        }

        body = body.TrimStart('\n');
        if (!HeadingRegex().IsMatch(body))
        {
            body = $"# {ToTitle(folderName)}\n\n" + body;
            changed = true;
        }

        if (!changed)
        {
            return content;
        }

        var repaired = $"---\n{yaml.TrimEnd()}\n---\n\n{body.TrimEnd()}\n";
        return usesCrLf ? repaired.Replace("\n", "\r\n", StringComparison.Ordinal) : repaired;
    }

    private static string CreateDescription(string folderName, string body)
    {
        var heading = HeadingRegex().Match(body);
        var title = heading.Success ? heading.Groups[1].Value.Trim() : ToTitle(folderName);
        return $"Follow the {title} workflow. Use when this repository skill applies.";
    }

    private static string EscapeYaml(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string ToTitle(string value)
        => string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static string Scalar(IReadOnlyDictionary<string, object> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value?.ToString()?.Trim() ?? string.Empty : string.Empty;

    private static bool TryExtractFrontMatter(string content, out string yaml, out string body)
    {
        yaml = string.Empty;
        body = content;
        using var reader = new StringReader(content);
        if (!string.Equals(reader.ReadLine()?.TrimStart('\uFEFF'), "---", StringComparison.Ordinal))
        {
            return false;
        }

        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                yaml = string.Join("\n", lines);
                body = reader.ReadToEnd();
                return true;
            }

            lines.Add(line);
        }

        return false;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidNameRegex();

    [GeneratedRegex(@"\[[^\]]+\]\(([^)\s]+)(?:\s+[^)]*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"^#\s+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();
}
