using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cis.Modules.Graph;

internal static partial class ImplementationGraphExtractor
{
    private static readonly IReadOnlyDictionary<string, string> SourceLanguages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".js"] = "javascript",
            [".jsx"] = "javascript",
            [".swift"] = "swift",
            [".kt"] = "kotlin",
            [".kts"] = "kotlin",
            [".py"] = "python",
            [".sql"] = "sql",
            [".tf"] = "terraform",
        };

    private static readonly HashSet<string> ManifestNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json",
        "build.gradle",
        "build.gradle.kts",
        "Package.swift",
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".artifacts",
        "artifacts",
        "bin",
        "obj",
        "node_modules",
        ".stryker-tmp",
        ".vs",
        ".idea",
        ".next",
        "dist",
        "build",
        "coverage",
        "DerivedData",
        ".gradle",
        "skills-quarantine",
        "_old",
        "build_out",
        "nongit",
    };

    public static IReadOnlyList<string> EnumerateInputPaths(string repositoryPath)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        while (pending.TryPop(out var directory))
        {
            foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(repositoryPath, child).Replace('\\', '/');
                if (!IsExcludedDirectory(relative)
                    && !new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(child);
                }
            }

            files.AddRange(Directory.EnumerateFiles(directory)
                .Select(path => Path.GetRelativePath(repositoryPath, path).Replace('\\', '/'))
                .Where(IsGraphInput));
        }

        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsSourceFile(string path)
        => SourceLanguages.ContainsKey(Path.GetExtension(path));

    public static string LanguageFor(string path)
        => SourceLanguages.GetValueOrDefault(Path.GetExtension(path), "unknown");

    public static IReadOnlyList<DiscoveredDeclaration> ExtractDeclarations(
        string path,
        string content,
        string componentId)
    {
        var language = LanguageFor(path);
        var declarations = new List<DiscoveredDeclaration>();
        var namespaceName = language == "csharp"
            ? CSharpNamespacePattern().Match(content).Groups["name"].Value
            : string.Empty;

        foreach (Match match in DeclarationPattern(language).Matches(content))
        {
            var name = match.Groups["name"].Value;
            var kind = match.Groups["kind"].Value.ToLowerInvariant();
            var qualifiedName = string.IsNullOrWhiteSpace(namespaceName)
                ? name
                : namespaceName + "." + name;
            var localId = string.Join('/',
                Encode(componentId),
                language,
                Encode(kind),
                Encode(qualifiedName));
            declarations.Add(new DiscoveredDeclaration(
                localId,
                qualifiedName,
                kind,
                language,
                LineNumber(content, match.Index)));
        }

        return declarations;
    }

    public static IReadOnlyList<DiscoveredTest> ExtractTests(
        string path,
        string content,
        string componentId)
    {
        var language = LanguageFor(path);
        var tests = new List<DiscoveredTest>();
        foreach (Match match in TestPattern(language).Matches(content))
        {
            var name = match.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var framework = TestFramework(language, match.Value);
            var identity = $"{path}#{name}";
            tests.Add(new DiscoveredTest(
                $"{Encode(componentId)}/{framework}/{Encode(identity)}",
                name,
                framework,
                language,
                LineNumber(content, match.Index)));
        }

        return tests;
    }

    public static IReadOnlyList<DiscoveredDependency> ExtractDependencies(string path, string content)
    {
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractDotNetDependencies(path, content);
        }

        if (path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractNpmDependencies(content);
        }

        if (path.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase))
        {
            return GradleDependencyPattern().Matches(content)
                .Select(match => ExternalDependency(
                    "gradle",
                    match.Groups["name"].Value,
                    match.Groups["version"].Value))
                .Distinct()
                .ToArray();
        }

        if (path.EndsWith("Package.swift", StringComparison.OrdinalIgnoreCase))
        {
            return SwiftPackagePattern().Matches(content)
                .Select(match => ExternalDependency(
                    "swift-package",
                    NormalizeSwiftPackageName(match.Groups["url"].Value),
                    match.Groups["version"].Value))
                .Distinct()
                .ToArray();
        }

        return [];
    }

    public static IReadOnlyList<DiscoveredWorkflow> ExtractWorkflows(string path, string content)
    {
        if (!IsWorkflow(path))
        {
            return [];
        }

        var workflows = new List<DiscoveredWorkflow>();
        var inJobs = false;
        var jobsIndent = -1;
        var offset = 0;
        foreach (var line in SplitLines(content))
        {
            var trimmed = line.Trim();
            var indent = line.Length - line.TrimStart().Length;
            if (!inJobs && string.Equals(trimmed, "jobs:", StringComparison.Ordinal))
            {
                inJobs = true;
                jobsIndent = indent;
            }
            else if (inJobs && trimmed.Length > 0 && indent <= jobsIndent && !trimmed.StartsWith('#'))
            {
                break;
            }
            else if (inJobs && indent > jobsIndent && indent <= jobsIndent + 2)
            {
                var match = WorkflowJobPattern().Match(trimmed);
                if (match.Success)
                {
                    var job = match.Groups["name"].Value;
                    workflows.Add(new DiscoveredWorkflow(
                        $"{Encode(path)}#{Encode(job)}",
                        job,
                        LineNumber(content, offset)));
                }
            }

            offset += line.Length + 1;
        }

        return workflows;
    }

    public static string? EvidencePath(string value, IReadOnlySet<string> sourcePaths)
    {
        var candidates = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(candidate => candidate.Trim().Trim('`').Replace('\\', '/'));
        foreach (var candidate in candidates)
        {
            var matched = sourcePaths
                .OrderByDescending(path => path.Length)
                .FirstOrDefault(path => candidate.Equals(path, StringComparison.OrdinalIgnoreCase)
                    || candidate.StartsWith(path + ":", StringComparison.OrdinalIgnoreCase)
                    || candidate.StartsWith(path + "#", StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                return matched;
            }
        }

        return null;
    }

    private static IReadOnlyList<DiscoveredDependency> ExtractDotNetDependencies(string path, string content)
    {
        try
        {
            var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            var dependencies = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => ExternalDependency(
                    "nuget",
                    AttributeOrChild(element, "Include"),
                    AttributeOrChild(element, "Version")))
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency.Name))
                .ToList();
            dependencies.AddRange(document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => AttributeOrChild(element, "Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => new DiscoveredDependency(
                    "project/" + Encode(ResolveRelativePath(path, include)),
                    Path.GetFileNameWithoutExtension(include),
                    "project",
                    string.Empty,
                    ResolveRelativePath(path, include))));
            return dependencies.Distinct().ToArray();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    private static IReadOnlyList<DiscoveredDependency> ExtractNpmDependencies(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var result = new List<DiscoveredDependency>();
            foreach (var sectionName in new[] { "dependencies", "devDependencies", "peerDependencies" })
            {
                if (!document.RootElement.TryGetProperty(sectionName, out var section)
                    || section.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result.AddRange(section.EnumerateObject().Select(property => ExternalDependency(
                    "npm",
                    property.Name,
                    property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString())));
            }

            return result.Distinct().ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DiscoveredDependency ExternalDependency(string ecosystem, string name, string version)
        => new(
            $"{ecosystem}/{Encode(name.ToLowerInvariant())}",
            name,
            ecosystem,
            version,
            null);

    private static Regex DeclarationPattern(string language) => language switch
    {
        "csharp" => CSharpDeclarationPattern(),
        "typescript" or "javascript" => TypeScriptDeclarationPattern(),
        "swift" => SwiftDeclarationPattern(),
        "kotlin" => KotlinDeclarationPattern(),
        "python" => PythonDeclarationPattern(),
        _ => NeverPattern(),
    };

    private static Regex TestPattern(string language) => language switch
    {
        "csharp" => CSharpTestPattern(),
        "typescript" or "javascript" => JavaScriptTestPattern(),
        "swift" => SwiftTestPattern(),
        "kotlin" => KotlinTestPattern(),
        "python" => PythonTestPattern(),
        _ => NeverPattern(),
    };

    private static string TestFramework(string language, string matchedText) => language switch
    {
        "csharp" when matchedText.Contains("TestMethod", StringComparison.Ordinal) => "mstest",
        "csharp" when matchedText.Contains("TestCase", StringComparison.Ordinal)
            || matchedText.Contains("Test]", StringComparison.Ordinal) => "nunit",
        "csharp" => "xunit",
        "typescript" or "javascript" => "javascript-test",
        "swift" => "xctest",
        "kotlin" => "junit",
        "python" => "pytest",
        _ => "test",
    };

    private static string AttributeOrChild(XElement element, string name)
        => element.Attribute(name)?.Value
            ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value
            ?? string.Empty;

    private static string ResolveRelativePath(string containingPath, string referencedPath)
    {
        var directory = Path.GetDirectoryName(containingPath.Replace('/', Path.DirectorySeparatorChar))
            ?? string.Empty;
        var segments = Path.Combine(directory, referencedPath)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (normalized.Count > 0)
                {
                    normalized.RemoveAt(normalized.Count - 1);
                }

                continue;
            }

            normalized.Add(segment);
        }

        return string.Join('/', normalized);
    }

    private static string NormalizeSwiftPackageName(string url)
        => Path.GetFileNameWithoutExtension(url.TrimEnd('/'));

    private static bool IsGraphInput(string path)
        => IsSourceFile(path)
            || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || ManifestNames.Contains(Path.GetFileName(path))
            || IsWorkflow(path);

    private static bool IsWorkflow(string path)
        => path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)
            && (path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private static bool IsExcludedDirectory(string path)
    {
        var segments = path.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            if (ExcludedDirectories.Contains(segments[index]))
            {
                return true;
            }

            if (index > 0
                && string.Equals(segments[index - 1], ".cis", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index], "local", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int LineNumber(string content, int index)
        => content.AsSpan(0, Math.Min(index, content.Length)).Count('\n') + 1;

    private static string[] SplitLines(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string Encode(string value) => Uri.EscapeDataString(value);

    [GeneratedRegex(@"^\s*namespace\s+(?<name>[A-Za-z_][\w.]*)", RegexOptions.Multiline)]
    private static partial Regex CSharpNamespacePattern();

    [GeneratedRegex(@"^\s*(?:(?:public|internal|protected|private|static|abstract|sealed|partial|readonly|ref)\s+)*(?<kind>class|record|struct|interface|enum)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Multiline)]
    private static partial Regex CSharpDeclarationPattern();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?:default\s+)?(?<kind>class|interface|type|enum|function)\s+(?<name>[A-Za-z_$][\w$]*)", RegexOptions.Multiline)]
    private static partial Regex TypeScriptDeclarationPattern();

    [GeneratedRegex(@"^\s*(?:(?:public|internal|private|open|final)\s+)*(?<kind>class|struct|enum|protocol|actor|func)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Multiline)]
    private static partial Regex SwiftDeclarationPattern();

    [GeneratedRegex(@"^\s*(?:(?:public|internal|private|protected|open|data|sealed|enum|annotation|value)\s+)*(?<kind>class|interface|object|fun)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Multiline)]
    private static partial Regex KotlinDeclarationPattern();

    [GeneratedRegex(@"^\s*(?<kind>class|def|async\s+def)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Multiline)]
    private static partial Regex PythonDeclarationPattern();

    [GeneratedRegex(@"\[(?:Fact|Theory|Test|TestCase|TestMethod)(?:\([^\]]*\))?\]\s*(?:(?:public|internal|protected|private|static|async|virtual|sealed|override)\s+)*(?:[\w<>,?.\[\]]+\s+)+(?<name>[A-Za-z_]\w*)\s*\(", RegexOptions.Multiline)]
    private static partial Regex CSharpTestPattern();

    [GeneratedRegex(@"\b(?:it|test)\s*\(\s*['""`](?<name>[^'""`]+)['""`]", RegexOptions.Multiline)]
    private static partial Regex JavaScriptTestPattern();

    [GeneratedRegex(@"^\s*func\s+(?<name>test[A-Za-z_]\w*)\s*\(", RegexOptions.Multiline)]
    private static partial Regex SwiftTestPattern();

    [GeneratedRegex(@"@Test(?:\([^)]*\))?\s*(?:public\s+|internal\s+|private\s+)?fun\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Multiline)]
    private static partial Regex KotlinTestPattern();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>test_[A-Za-z_]\w*)\s*\(", RegexOptions.Multiline)]
    private static partial Regex PythonTestPattern();

    [GeneratedRegex(@"^(?<name>[A-Za-z_][\w.-]*):(?:\s*(?:#.*)?)$")]
    private static partial Regex WorkflowJobPattern();

    [GeneratedRegex(@"\b(?:implementation|api|compileOnly|runtimeOnly|testImplementation|androidTestImplementation)\s*\(?\s*['""](?<name>[^:'""]+:[^:'""]+):(?<version>[^'""]+)['""]")]
    private static partial Regex GradleDependencyPattern();

    [GeneratedRegex(@"\.package\s*\(\s*url:\s*""(?<url>[^""]+)""\s*,\s*(?:from:\s*)?""(?<version>[^""]+)""")]
    private static partial Regex SwiftPackagePattern();

    [GeneratedRegex("a^")]
    private static partial Regex NeverPattern();
}

internal sealed record DiscoveredDeclaration(
    string LocalId,
    string Name,
    string Kind,
    string Language,
    int Line);

internal sealed record DiscoveredTest(
    string LocalId,
    string Name,
    string Framework,
    string Language,
    int Line);

internal sealed record DiscoveredDependency(
    string LocalId,
    string Name,
    string Ecosystem,
    string Version,
    string? ProjectPath);

internal sealed record DiscoveredWorkflow(
    string LocalId,
    string Name,
    int Line);
