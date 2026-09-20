using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.UiDirection;

/// <summary>Bounded, local observations of owned web interfaces. Never records a design decision.</summary>
public sealed class UiBaselineDiscovery(ICisWorkspaceRegistry workspaces) : ICisUiBaselineDiscovery
{
    private const int Schema = 3;
    private const int FileLimit = 192;
    private const int ByteLimit = 2 * 1024 * 1024;
    private const int SingleFileLimit = 128 * 1024;
    private const string CachePath = ".cis/local/ui-baseline/baseline.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".cis", "docs", "node_modules", "dist", "build", "bin", "obj", "coverage", ".angular", ".next", ".nuxt", "vendor", "environments", "test", "tests", "__tests__", "mock-api", "mocks" };
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".ts", ".tsx", ".js", ".jsx", ".html", ".vue", ".svelte", ".css", ".scss", ".sass", ".less" };
    private static readonly Dictionary<string, string> Frameworks = new(StringComparer.Ordinal)
    { ["@angular/core"] = "Angular", ["react"] = "React", ["vue"] = "Vue", ["svelte"] = "Svelte", ["next"] = "Next.js",
      ["primeng"] = "PrimeNG", ["@angular/material"] = "Angular Material", ["@mui/material"] = "Material UI", ["antd"] = "Ant Design",
      ["vuetify"] = "Vuetify", ["tailwindcss"] = "Tailwind CSS", ["bootstrap"] = "Bootstrap" };

    public CisUiBaselineResult Discover(string workspacePath)
    {
        var resolution = workspaces.Resolve(workspacePath);
        if (resolution.Workspace?.AuthorityRepository is not { } authority || !resolution.IsSuccess)
            return new("invalid", workspacePath, "", false, [], new Dictionary<string, string>(), [], resolution.Errors.Count > 0 ? resolution.Errors : ["A product authority is required."]);
        var workspace = resolution.Workspace;
        var warnings = new List<string>();
        var inputs = new List<RepositoryInput>();
        foreach (var repository in workspace.Repositories.Where(item => item.IsProductOwned).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            try
            {
                if (!Directory.Exists(repository.RepositoryPath) || CisPathSafety.IsReparsePoint(repository.RepositoryPath))
                { warnings.Add($"{repository.Id}: repository is unavailable for UI discovery."); continue; }
                var candidates = Candidates(repository.RepositoryPath, out var enumerationLimited);
                var files = new List<Input>(); var bytes = 0; var limited = enumerationLimited;
                foreach (var file in candidates.OrderBy(Priority).ThenBy(value => value, StringComparer.Ordinal))
                {
                    if (files.Count >= FileLimit) { limited = true; break; }
                    if (CisPathSafety.ContainsReparsePoint(repository.RepositoryPath, file)) { limited = true; continue; }
                    var length = new FileInfo(file).Length;
                    if (length > SingleFileLimit || bytes + length > ByteLimit) { limited = true; continue; }
                    // Read bounded bytes too: a concurrent rewrite cannot bypass the length limit.
                    using var stream = File.OpenRead(file);
                    var buffer = new byte[SingleFileLimit + 1]; var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                    if (count > SingleFileLimit || bytes + count > ByteLimit) { limited = true; continue; }
                    bytes += count;
                    var text = Encoding.UTF8.GetString(buffer, 0, count);
                    if (text.Contains('\0')) { limited = true; continue; }
                    files.Add(new(Normalize(Path.GetRelativePath(repository.RepositoryPath, file)), text));
                }
                inputs.Add(new(repository, candidates.Count, limited, files));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { warnings.Add($"{repository.Id}: UI discovery could not read all selected source files ({error.GetType().Name})."); }
        }
        var hash = Digest($"ui-baseline-{Schema}\n" + string.Join('\n', inputs.Select(input =>
            $"{input.Repository.Id}|{input.Repository.RepositoryPath}|{input.Candidates}|{input.Limited}\n" + string.Join('\n', input.Files.Select(file => $"{file.Path}|{Digest(file.Text)}")))) + string.Join('\n', warnings));
        var cached = ReadCache(authority.RepositoryPath);
        if (cached?.SourceHash == hash && cached.WorkspacePath == workspace.WorkspacePath)
            return cached with { Cached = true };
        var repositories = inputs.Select(Analyze).Where(item => item.Facts.Count > 0).ToArray();
        foreach (var repository in repositories.Where(item => item.Limited))
            warnings.Add($"{repository.Id}: bounded sample of {repository.FilesRead} of {repository.CandidateFiles} candidate UI files; shared styles and shell files were prioritized.");
        warnings.Add("Source observations describe implementation, not runtime screenshots or an accessibility audit. Unobserved behavior remains unconfirmed.");
        var result = new CisUiBaselineResult(repositories.Length > 0 ? "discovered" : "not-found", workspace.WorkspacePath, hash, false,
            repositories, Suggestions(repositories), warnings, []);
        try
        {
            if (CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, CachePath, out var path)
                && !CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temporary, JsonSerializer.Serialize(result, Json)); File.Move(temporary, path, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return result with { Warnings = result.Warnings.Append("The disposable UI-baseline cache could not be written; observations are still available.").ToArray() }; }
        return result;
    }

    private static CisUiBaselineResult? ReadCache(string root)
    {
        try
        {
            if (!CisPathSafety.TryResolveUnderRoot(root, CachePath, out var path) || CisPathSafety.ContainsReparsePoint(root, path)
                || !File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return null;
            return JsonSerializer.Deserialize<CisUiBaselineResult>(File.ReadAllText(path), Json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static List<string> Candidates(string root, out bool limited)
    {
        var result = new List<string>(); var pending = new Queue<string>(); pending.Enqueue(root); var visited = 0; limited = false;
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            foreach (var entry in CisPathSafety.EnumerateFileSystemEntries(directory, recursive: false).Order(StringComparer.Ordinal))
            {
                if (++visited > 12_000) { limited = true; return result; }
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry)) { if (!SkippedDirectories.Contains(name) && !name.StartsWith('.')) pending.Enqueue(entry); continue; }
                if (name == "package.json" || Extensions.Contains(Path.GetExtension(name)) && !Regex.IsMatch(name,
                    @"(?:\.(?:spec|test|stories|d|min)\.|environment|secret|credential|\.config\.local)", RegexOptions.IgnoreCase)) result.Add(entry);
            }
        }
        return result;
    }

    private static int Priority(string path)
    {
        var value = Normalize(path).ToLowerInvariant(); var name = Path.GetFileName(value);
        if (name == "package.json") return 0;
        if (Regex.IsMatch(value, @"/(?:styles|theme|themes|tokens|variables)/|(?:styles|globals|theme|tailwind|app\.config)\.")) return 1;
        if (Regex.IsMatch(value, @"/layout/|/shared/components/|(?:topbar|sidebar|header|menu|routes|routing)")) return 2;
        if (Regex.IsMatch(value, @"\.(?:css|scss|sass|less)$")) return 3;
        return 4;
    }

    private static CisUiBaselineRepository Analyze(RepositoryInput input)
    {
        var facts = new List<CisUiBaselineFact>(); var tokens = new List<CisUiBaselineToken>();
        var frameworks = new List<string>(); var manifestEvidence = new List<CisUiBaselineEvidence>();
        foreach (var file in input.Files.Where(file => Path.GetFileName(file.Path) == "package.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(file.Text.TrimStart('\uFEFF'));
                foreach (var section in new[] { "dependencies", "devDependencies" })
                    if (document.RootElement.TryGetProperty(section, out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
                        foreach (var dependency in dependencies.EnumerateObject())
                            if (dependency.Value.ValueKind == JsonValueKind.String && Frameworks.TryGetValue(dependency.Name, out var name))
                            { frameworks.Add($"{name} ({dependency.Value.GetString()})"); manifestEvidence.Add(new(file.Path, Line(file.Text, file.Text.IndexOf('"' + dependency.Name + '"', StringComparison.Ordinal)))); }
            }
            catch (JsonException) { }
        }
        var source = input.Files.Where(file => Path.GetFileName(file.Path) != "package.json").ToArray();
        var templateFiles = source.Where(file => Regex.IsMatch(file.Path, @"\.(?:html|vue|svelte|tsx|jsx)$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(file.Text, @"@Component\s*\(", RegexOptions.IgnoreCase)).ToArray();
        if (frameworks.Count == 0 && templateFiles.Length == 0) return new(input.Repository.Id, input.Repository.RepositoryPath, input.Files.Count, input.Candidates, input.Limited, [], []);
        if (frameworks.Count > 0) facts.Add(new("Frameworks", string.Join(", ", frameworks.Distinct()), manifestEvidence.Distinct().Take(8).ToArray()));
        AddMatches("Shell and navigation", "Shared shell elements", @"<(?:(?:app|general)-)*(?:topbar|sidebar|header|nav|footer|router-outlet|router-view)[\w-]*\b", templateFiles);
        AddMatches("Shell and navigation", "Declared route fragments (nested prefixes are not resolved)", @"\bpath\s*:\s*['""][^'""\r\n]{0,70}['""]", source.Where(file => Regex.IsMatch(file.Path, @"(?:routes|routing)\.", RegexOptions.IgnoreCase)));
        AddMatches("Shared components", "Component selectors used by the interface", @"<(?:p-|app-|ui-|mat-|general-)[a-z][\w-]*\b", templateFiles);
        AddMatches("Theme configuration", "Configured theme", @"(?:preset\s*:\s*(?-i:[A-Z][\w]*)\b|darkModeSelector\s*:\s*['""].{1,60}?['""]|@import\s+['""].{1,100}?(?:theme|tailwind|font).{0,50}?['""])", source);
        var styles = source.Where(file => Regex.IsMatch(file.Path, @"\.(?:css|scss|sass|less)$", RegexOptions.IgnoreCase)).ToArray();
        AddMatches("Typography", "Font declarations", @"font-family\s*:\s*(?:#\{[^}\r\n]{1,80}\}|[^;{}\r\n]){1,120}", styles);
        AddMatches("Layout and spacing", "Shared spacing and layout declarations", @"(?:gap|padding|border-radius|grid-template-columns)\s*:\s*[^;{}\r\n]{1,90}", styles.Where(file => Priority(file.Path) < 3));
        AddMatches("Responsive behavior", "Media queries", @"@media[^{}\r\n]{1,120}", styles);
        AddMatches("Feedback and states", "Feedback controls and state markers", @"<(?:p-toast|p-message|p-messages|p-dialog|p-confirmdialog|p-progressspinner|p-skeleton|mat-progress-spinner|app-alert|app-spinner)\b|aria-live\s*=\s*['""][^'""]+['""]|\[loading\]", templateFiles);
        AddMatches("Accessibility markers", "Observed source markers; compliance requires review", @"aria-(?:label|labelledby|describedby)\s*=|:focus-visible|prefers-reduced-motion", source);
        foreach (var file in styles)
            foreach (Match match in Matches(file.Text, @"(?<name>--[\w-]+|\$[\w-]+|(?:background(?:-color)?|color|border-color))\s*:\s*(?<value>#[0-9a-fA-F]{3,8})\b"))
            {
                if (tokens.Count >= 24) break;
                if (tokens.Any(token => token.Name == match.Groups["name"].Value && token.Value.Equals(match.Groups["value"].Value, StringComparison.OrdinalIgnoreCase))) continue;
                tokens.Add(new(match.Groups["name"].Value, match.Groups["value"].Value, new(file.Path, Line(file.Text, match.Index))));
            }
        var controls = UiBaselineControlSheet.Discover(templateFiles.Select(file => (file.Path, file.Text)));
        var repository = new CisUiBaselineRepository(input.Repository.Id, input.Repository.RepositoryPath, input.Files.Count, input.Candidates, input.Limited, facts, tokens)
        { Controls = controls };
        return repository with { Preview = UiBaselineControlSheet.Render(repository) };

        void AddMatches(string area, string lead, string pattern, IEnumerable<Input> files)
        {
            var values = new List<string>(); var evidence = new List<CisUiBaselineEvidence>();
            foreach (var file in files)
                foreach (Match match in Matches(file.Text, pattern))
                {
                    var value = match.Value.TrimStart('<').Trim();
                    if (values.Contains(value, StringComparer.OrdinalIgnoreCase) || values.Count >= 10) continue;
                    values.Add(value); evidence.Add(new(file.Path, Line(file.Text, match.Index)));
                }
            if (values.Count > 0) facts.Add(new(area, $"{lead}: {string.Join("; ", values)}.", evidence.Distinct().ToArray()));
        }
    }

    private static Dictionary<string, string> Suggestions(IReadOnlyList<CisUiBaselineRepository> repositories)
    {
        var result = new Dictionary<string, string>();
        if (repositories.Count == 0) return result;
        var areas = new Dictionary<string, string[]> { ["UI-Q-003"] = ["Shell and navigation"], ["UI-Q-004"] = ["Layout and spacing"],
            ["UI-Q-006"] = ["Typography"], ["UI-Q-007"] = ["Frameworks", "Shared components"], ["UI-Q-008"] = ["Responsive behavior"],
            ["UI-Q-010"] = ["Feedback and states"], ["UI-Q-011"] = ["Shared components"] };
        foreach (var (id, selected) in areas)
        {
            var descriptions = repositories.Select(repository => (repository.Id, Facts: repository.Facts.Where(fact => selected.Contains(fact.Area)).ToArray())).Where(item => item.Facts.Length > 0);
            var text = string.Join("\n\n", descriptions.Select(item => $"{item.Id}: {string.Join(" ", item.Facts.Select(fact => fact.Summary))}"));
            if (text.Length > 0) result[id] = "Preserve the existing implementation as the starting baseline. " + text + " Confirm any exceptions during UI review.";
        }
        var colors = repositories.Where(repository => repository.Tokens.Count > 0).Select(repository =>
            $"{repository.Id}: {string.Join("; ", repository.Tokens.Take(8).Select(token => $"{token.Name} = {token.Value}"))}");
        if (colors.Any()) result["UI-Q-005"] = "Preserve each interface's existing theme and named tokens. " + string.Join("\n\n", colors) + ". These are observed declarations; confirm their rendered use and contrast during review.";
        result["UI-Q-012"] = "Use each existing interface as the baseline. Preserve its framework, shared shell, components and theme assets. Rebranding, framework replacement or new surfaces require an explicit change. Confirm brand ownership, localization and other constraints that source inspection cannot establish.";
        return result;
    }

    private static MatchCollection Matches(string text, string pattern) => Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static int Line(string text, int index) => 1 + text.AsSpan(0, Math.Max(index, 0)).Count('\n');
    private static string Normalize(string value) => value.Replace('\\', '/');
    private static string Digest(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed record Input(string Path, string Text);
    private sealed record RepositoryInput(CisWorkspaceRepository Repository, int Candidates, bool Limited, IReadOnlyList<Input> Files);
}
