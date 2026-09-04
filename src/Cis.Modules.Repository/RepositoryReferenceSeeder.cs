using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cis.Modules.Repository;

internal static class RepositoryReferenceSeeder
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cis",
        ".codex-tmp",
        ".git",
        ".idea",
        ".next",
        ".nuxt",
        ".output",
        ".svelte-kit",
        ".vs",
        "bin",
        "coverage",
        "dist",
        "node_modules",
        "obj",
        "skills-quarantine",
    };

    private static readonly Regex MinimalApiPattern = new(
        "\\bMap(?<method>Get|Post|Put|Patch|Delete)\\s*\\(\\s*\"(?<route>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControllerRoutePattern = new(
        "\\[Route\\s*\\(\\s*\"(?<route>[^\"]+)\"\\s*\\)\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControllerActionPattern = new(
        "\\[(?:Http)?(?<method>Get|Post|Put|Patch|Delete)(?:Attribute)?(?:\\s*\\(\\s*\"(?<route>[^\"]*)\"[^)]*\\))?\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControllerClassPattern = new(
        "\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*Controller)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RouteHandlerPattern = new(
        "\\bexport\\s+(?:async\\s+)?function\\s+(?<method>GET|POST|PUT|PATCH|DELETE)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExpressRoutePattern = new(
        "\\b(?:app|router)\\s*\\.\\s*(?<method>get|post|put|patch|delete|options|head)\\s*\\(\\s*['\"`](?<route>[^'\"`]+)['\"`]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AngularRoutePattern = new(
        "\\bpath\\s*:\\s*['\"](?<route>[^'\"]*)['\"]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GradlePackagePattern = new(
        "(?<scope>api|implementation|compileOnly|runtimeOnly|testImplementation)\\s*\\(\\s*\"(?<package>[^:\"]+:[^:\"]+)(?::(?<version>[^\"]+))?\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SwiftPackagePattern = new(
        "\\.package\\s*\\(\\s*url:\\s*\"(?<url>[^\"]+)\"\\s*,\\s*(?:from|exact):\\s*\"(?<version>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommandTypePattern = CreateTypePattern("Command");

    private static readonly Regex EventTypePattern = CreateTypePattern("Event");

    private static readonly Regex ProjectionTypePattern = CreateTypePattern("Projection|Projector|ReadModel");

    private static readonly Regex ExceptionTypePattern = CreateTypePattern("Exception");

    private static readonly Regex WorkflowEnumPattern = new(
        "\\benum\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:State|Status))\\s*\\{(?<body>[^}]+)\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex AuthorizationPattern = new(
        "\\bRequireAuthorization\\s*\\(\\s*\"(?<permission>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PermissionConstantPattern = new(
        "\\bconst\\s+string\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*\"(?<permission>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DbSetPattern = new(
        "\\bDbSet<(?<entity>[A-Za-z_][A-Za-z0-9_.]*)>\\s+(?<property>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>> Seed(
        string repositoryPath,
        RepositoryClassification classification)
    {
        var files = EnumerateFiles(repositoryPath).ToArray();
        return new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>(StringComparer.Ordinal)
        {
            ["api-dictionary"] = SeedApis(repositoryPath, files, classification),
            ["command-dictionary"] = SeedNamedTypes(repositoryPath, files, classification, CommandTypePattern, "command"),
            ["event-dictionary"] = SeedEvents(repositoryPath, files, classification),
            ["workflow-state-dictionary"] = SeedWorkflowStates(repositoryPath, files, classification),
            ["business-invariant-catalogue"] = [],
            ["projection-dictionary"] = SeedProjections(repositoryPath, files, classification),
            ["permissions-dictionary"] = SeedPermissions(repositoryPath, files, classification),
            ["configuration-dictionary"] = SeedConfiguration(repositoryPath, files, classification),
            ["package-catalogue"] = SeedPackages(repositoryPath, files, classification),
            ["screen-route-map"] = SeedRoutes(repositoryPath, files, classification),
            ["problem-details-catalogue"] = SeedProblems(repositoryPath, files, classification),
            ["module-ownership-map"] = SeedModules(classification),
            ["data-dictionary"] = SeedData(repositoryPath, files, classification),
            ["erd"] = [],
            ["traceability-matrix"] = SeedTraceability(classification),
        };
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedApis(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var content = TryReadText(path);
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in MinimalApiPattern.Matches(content))
            {
                var method = match.Groups["method"].Value.ToUpperInvariant();
                var route = match.Groups["route"].Value;
                rows.Add([
                    CreateApiId(component.Id, method, route),
                    "unversioned",
                    method,
                    route,
                    component.Id,
                    component.Id,
                    "unclassified",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                    "unknown",
                    "Deterministically discovered; governance fields require review.",
                ]);
            }

            var controller = ControllerClassPattern.Match(content);
            if (controller.Success)
            {
                var prefix = ControllerRoutePattern.Match(content) is { Success: true } routeMatch
                    ? routeMatch.Groups["route"].Value
                    : string.Empty;
                prefix = prefix.Replace(
                    "[controller]",
                    controller.Groups["name"].Value[..^"Controller".Length],
                    StringComparison.OrdinalIgnoreCase);
                foreach (Match action in ControllerActionPattern.Matches(content))
                {
                    var method = action.Groups["method"].Value.ToUpperInvariant();
                    var route = CombineRoute(prefix, action.Groups["route"].Value);
                    rows.Add([
                        CreateApiId(component.Id, method, route),
                        "unversioned",
                        method,
                        route,
                        component.Id,
                        component.Id,
                        "unclassified",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "Draft",
                        ToRepositoryPath(repositoryPath, path),
                        "unknown",
                        "Deterministically discovered from an MVC controller; governance fields require review.",
                    ]);
                }
            }
        }

        foreach (var path in files.Where(IsNextRouteHandler))
        {
            var content = TryReadText(path);
            var component = FindComponent(repositoryPath, path, classification);
            var route = CreateNextRoute(repositoryPath, path, component);
            foreach (Match match in RouteHandlerPattern.Matches(content))
            {
                var method = match.Groups["method"].Value.ToUpperInvariant();
                rows.Add([
                    CreateApiId(component.Id, method, route),
                    "unversioned",
                    method,
                    route,
                    component.Id,
                    component.Id,
                    "unclassified",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                    "unknown",
                    "Deterministically discovered; governance fields require review.",
                ]);
            }
        }

        foreach (var path in files.Where(IsExpressSource))
        {
            var content = TryReadText(path);
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in ExpressRoutePattern.Matches(content))
            {
                var method = match.Groups["method"].Value.ToUpperInvariant();
                var route = match.Groups["route"].Value;
                rows.Add([
                    CreateApiId(component.Id, method, route),
                    "unversioned",
                    method,
                    route,
                    component.Id,
                    component.Id,
                    "unclassified",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                    "unknown",
                    "Deterministically discovered from an Express route; governance fields require review.",
                ]);
            }
        }

        return ConsolidateApiRows(rows);
    }

    private static bool IsExpressSource(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        if (fileName.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".spec.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return !segments.Any(segment => segment.Equals("test", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("tests", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("__tests__", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ConsolidateApiRows(
        IReadOnlyList<IReadOnlyList<string>> rows)
        => rows
            .GroupBy(row => row[0], StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First().ToArray();
                first[20] = string.Join("; ", group.Select(row => row[20])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase));
                return (IReadOnlyList<string>)first;
            })
            .OrderBy(row => row[0], StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<string>> ConsolidateConfigurationRows(
        IReadOnlyList<IReadOnlyList<string>> rows)
        => rows
            .GroupBy(row => row[4] + '\u001f' + row[1], StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var allowedValues = string.Join("<br>", group.Select(row => row[2]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                var evidence = string.Join("<br>", group.Select(row => row[8]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
                return (IReadOnlyList<string>)
                [
                    first[0],
                    first[1],
                    allowedValues,
                    first[3],
                    first[4],
                    group.Any(row => row[5].Equals("yes", StringComparison.OrdinalIgnoreCase)) ? "yes" : "no",
                    first[6],
                    first[7],
                    evidence,
                ];
            })
            .OrderBy(row => row[4], StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row[1], StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<string>> SeedNamedTypes(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification,
        Regex pattern,
        string kind)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in pattern.Matches(TryReadText(path)))
            {
                var name = match.Groups["name"].Value;
                rows.Add([
                    CreateCatalogueId(kind == "command" ? "CMD" : "ITEM", $"{component.Id}-{name}"),
                    name,
                    component.Id,
                    "unknown",
                    ToRepositoryPath(repositoryPath, path),
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedEvents(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in EventTypePattern.Matches(TryReadText(path)))
            {
                var name = match.Groups["name"].Value;
                rows.Add([
                    CreateCatalogueId("EVT", $"{component.Id}-{name}"),
                    name,
                    "unknown",
                    component.Id,
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedWorkflowStates(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in WorkflowEnumPattern.Matches(TryReadText(path)))
            {
                var workflow = match.Groups["name"].Value;
                foreach (var state in ParseEnumMembers(match.Groups["body"].Value))
                {
                    rows.Add([
                        CreateCatalogueId("WF", $"{component.Id}-{workflow}"),
                        workflow,
                        state,
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "unknown",
                        "Draft",
                    ]);
                }
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedProjections(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in ProjectionTypePattern.Matches(TryReadText(path)))
            {
                var name = match.Groups["name"].Value;
                rows.Add([
                    CreateCatalogueId("PROJ", $"{component.Id}-{name}"),
                    name,
                    "unknown",
                    component.Id,
                    "unknown",
                    "unknown",
                    "unknown",
                    "unknown",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedPermissions(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var discoveries = new List<(string Permission, string AppliesTo, string Evidence)>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var content = TryReadText(path);
            foreach (Match match in AuthorizationPattern.Matches(content))
            {
                discoveries.Add((
                    match.Groups["permission"].Value,
                    "authorization gate",
                    ToRepositoryPath(repositoryPath, path)));
            }

            if (!Path.GetFileName(path).Contains("permission", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match match in PermissionConstantPattern.Matches(content))
            {
                discoveries.Add((
                    match.Groups["permission"].Value,
                    match.Groups["name"].Value,
                    ToRepositoryPath(repositoryPath, path)));
            }
        }

        return DistinctRows(discoveries.Select(discovery =>
        {
            var component = FindComponentByRelativePath(discovery.Evidence, classification);
            var action = discovery.Permission.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "unknown";
            return (IReadOnlyList<string>)[
                discovery.Permission,
                Humanize(discovery.Permission),
                component.Id,
                component.Id,
                action,
                "unknown",
                discovery.AppliesTo,
                "unknown",
                "unknown",
                "unknown",
                "unknown",
                discovery.Evidence,
                "Draft",
                "Deterministically discovered; role mapping and least-privilege review required.",
            ];
        }));
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedProblems(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in ExceptionTypePattern.Matches(TryReadText(path)))
            {
                var name = match.Groups["name"].Value;
                rows.Add([
                    CreateCatalogueId("PROB", $"{component.Id}-{name}"),
                    name,
                    "unknown",
                    component.Id,
                    Humanize(name),
                    "unknown",
                    "unknown",
                    "unknown",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedData(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in DbSetPattern.Matches(TryReadText(path)))
            {
                rows.Add([
                    match.Groups["entity"].Value,
                    "*",
                    "entity",
                    "unknown",
                    "unknown",
                    "unknown",
                    component.Id,
                    "mutable or frozen: review required",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedConfiguration(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path =>
                     Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                     && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var component = FindComponent(repositoryPath, path, classification);
                AddConfigurationRows(
                    rows,
                    document.RootElement,
                    prefix: null,
                    component.Id,
                    ToRepositoryPath(repositoryPath, path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // The classifier remains authoritative for scan warnings. A reference seed is best-effort.
            }
        }

        foreach (var path in files.Where(IsEnvironmentExample))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (var line in TryReadText(path).Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = trimmed[..separator].Trim();
                var value = trimmed[(separator + 1)..].Trim();
                var sensitive = IsSensitive(key);
                rows.Add([
                    key,
                    key,
                    sensitive ? "<redacted>" : Limit(value),
                    "startup-stable",
                    component.Id,
                    sensitive ? "yes" : "no",
                    "Environment example setting; review required and runtime consumption not yet verified.",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        return ConsolidateConfigurationRows(rows);
    }

    private static void AddConfigurationRows(
        ICollection<IReadOnlyList<string>> rows,
        JsonElement element,
        string? prefix,
        string component,
        string source)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}:{property.Name}";
                AddConfigurationRows(rows, property.Value, key, component, source);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var key = prefix ?? "configuration";
            rows.Add([
                key,
                key,
                "array",
                "startup-stable",
                component,
                "no",
                "Discovered JSON configuration array.",
                "Draft",
                source,
            ]);
            return;
        }

        var name = prefix ?? "configuration";
        var sensitive = IsSensitive(name);
        rows.Add([
            name,
            name,
            sensitive ? "<redacted>" : Limit(element.ToString()),
            "startup-stable",
            component,
            sensitive ? "yes" : "no",
            "Discovered JSON configuration value; allowed values and runtime consumption require review.",
            "Draft",
            source,
        ]);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedPackages(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var project = XDocument.Load(path);
                var component = FindComponent(repositoryPath, path, classification);
                foreach (var package in project.Descendants().Where(element =>
                             string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal)))
                {
                    var name = package.Attribute("Include")?.Value ?? package.Attribute("Update")?.Value;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var version = package.Attribute("Version")?.Value
                        ?? package.Elements().FirstOrDefault(element =>
                            string.Equals(element.Name.LocalName, "Version", StringComparison.Ordinal))?.Value
                        ?? "centrally managed";
                    rows.Add([
                        name,
                        component.Id,
                        "NuGet dependency",
                        version,
                        "package-reference",
                        "Draft",
                        ToRepositoryPath(repositoryPath, path),
                    ]);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Best-effort seed; malformed manifests are left for deterministic package tooling.
            }
        }

        foreach (var path in files.Where(path =>
                     string.Equals(Path.GetFileName(path), "package.json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var component = FindComponent(repositoryPath, path, classification);
                foreach (var group in new[] { "dependencies", "devDependencies", "peerDependencies" })
                {
                    if (!document.RootElement.TryGetProperty(group, out var dependencies)
                        || dependencies.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var package in dependencies.EnumerateObject())
                    {
                        rows.Add([
                            package.Name,
                            component.Id,
                            "JavaScript package dependency",
                            package.Value.GetString() ?? "unspecified",
                            group,
                            "Draft",
                            ToRepositoryPath(repositoryPath, path),
                        ]);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Best-effort seed.
            }
        }

        foreach (var path in files.Where(path =>
                     string.Equals(Path.GetFileName(path), "build.gradle", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Path.GetFileName(path), "build.gradle.kts", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in GradlePackagePattern.Matches(TryReadText(path)))
            {
                rows.Add([
                    match.Groups["package"].Value,
                    component.Id,
                    "Gradle dependency",
                    match.Groups["version"].Success ? match.Groups["version"].Value : "managed",
                    match.Groups["scope"].Value,
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        foreach (var path in files.Where(path =>
                     string.Equals(Path.GetFileName(path), "Package.swift", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            foreach (Match match in SwiftPackagePattern.Matches(TryReadText(path)))
            {
                var package = GetSwiftPackageName(match.Groups["url"].Value);
                rows.Add([
                    package,
                    component.Id,
                    "Swift package dependency",
                    match.Groups["version"].Value,
                    "package",
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                ]);
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedRoutes(
        string repositoryPath,
        IReadOnlyList<string> files,
        RepositoryClassification classification)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var path in files.Where(IsNextPage))
        {
            var component = FindComponent(repositoryPath, path, classification);
            rows.Add([
                CreateNextRoute(repositoryPath, path, component),
                component.Id,
                "Next.js",
                "unknown",
                ToRepositoryPath(repositoryPath, path),
                "Draft",
                ToRepositoryPath(repositoryPath, path),
                "File-system route; access and navigation behavior require review.",
            ]);
        }

        foreach (var path in files.Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)))
        {
            var component = FindComponent(repositoryPath, path, classification);
            if (!component.Frameworks.Contains("angular", StringComparer.Ordinal))
            {
                continue;
            }

            foreach (Match match in AngularRoutePattern.Matches(TryReadText(path)))
            {
                var route = match.Groups["route"].Value;
                rows.Add([
                    route.Length == 0 ? "/" : "/" + route.TrimStart('/'),
                    component.Id,
                    "Angular",
                    "unknown",
                    ToRepositoryPath(repositoryPath, path),
                    "Draft",
                    ToRepositoryPath(repositoryPath, path),
                    "Route declaration; guards and destination behavior require review.",
                ]);
            }
        }

        return DistinctRows(rows);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedModules(RepositoryClassification classification)
        => classification.Components.Select(component => (IReadOnlyList<string>)[
            CreateCatalogueId("MOD", component.Id),
            component.Id,
            component.Root,
            string.Join(", ", component.Roles.Concat(component.Capabilities).Distinct(StringComparer.Ordinal)),
            component.Capabilities.Contains("events", StringComparer.Ordinal) ? "event surface detected" : "unknown",
            component.Capabilities.Contains("persistence", StringComparer.Ordinal) ? "persistence surface detected" : "unknown",
            component.Roles.Contains("backend-api-producer", StringComparer.Ordinal) ? "API producer" : "none detected",
            "TODO: define negative ownership and dependency rules",
            "repository profile; system context; technical intent",
            "Draft",
            string.Join("; ", component.Evidence),
        ]).ToArray();

    private static IReadOnlyList<IReadOnlyList<string>> SeedTraceability(RepositoryClassification classification)
        => classification.Components.Select(component => (IReadOnlyList<string>)[
            CreateCatalogueId("TRACE", component.Id),
            "product intent and detected repository role",
            "product-intent-spec; technical-intent-spec; system-context-spec",
            component.Id,
            component.Root,
            "TODO: link component tests and verification evidence",
            "Draft",
            string.Join("; ", component.Evidence),
        ]).ToArray();

    private static RepositoryComponentClassification FindComponent(
        string repositoryPath,
        string path,
        RepositoryClassification classification)
    {
        var relative = ToRepositoryPath(repositoryPath, path);
        return classification.Components
                   .Where(component => component.Root == "."
                       || relative.Equals(component.Root, StringComparison.OrdinalIgnoreCase)
                       || relative.StartsWith(component.Root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(component => component.Root.Length)
                   .FirstOrDefault()
               ?? new RepositoryComponentClassification(
                   "repository",
                   ".",
                   [],
                   [],
                   [],
                   [],
                   "unknown",
                   []);
    }

    private static bool IsNextRouteHandler(string path)
        => IsJavaScriptOrTypeScript(path)
            && string.Equals(Path.GetFileNameWithoutExtension(path), "route", StringComparison.OrdinalIgnoreCase);

    private static bool IsNextPage(string path)
        => IsJavaScriptOrTypeScript(path)
            && (string.Equals(Path.GetFileNameWithoutExtension(path), "page", StringComparison.OrdinalIgnoreCase)
                || path.Replace('\\', '/').Contains("/pages/", StringComparison.OrdinalIgnoreCase));

    private static bool IsJavaScriptOrTypeScript(string path)
        => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

    private static string CreateNextRoute(
        string repositoryPath,
        string path,
        RepositoryComponentClassification component)
    {
        var relative = component.Root == "."
            ? ToRepositoryPath(repositoryPath, path)
            : Path.GetRelativePath(
                Path.Combine(repositoryPath, component.Root.Replace('/', Path.DirectorySeparatorChar)),
                path).Replace('\\', '/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        var appIndex = parts.FindIndex(part => string.Equals(part, "app", StringComparison.OrdinalIgnoreCase));
        var pagesIndex = parts.FindIndex(part => string.Equals(part, "pages", StringComparison.OrdinalIgnoreCase));
        var start = appIndex >= 0 ? appIndex + 1 : pagesIndex >= 0 ? pagesIndex + 1 : 0;
        var routeParts = parts.Skip(start).ToList();
        if (routeParts.Count > 0)
        {
            routeParts.RemoveAt(routeParts.Count - 1);
        }

        routeParts = routeParts
            .Where(part => !(part.StartsWith('(') && part.EndsWith(')')) && !part.StartsWith('@'))
            .Select(ConvertRouteSegment)
            .ToList();
        return routeParts.Count == 0 ? "/" : "/" + string.Join('/', routeParts);
    }

    private static string ConvertRouteSegment(string segment)
    {
        if (segment.StartsWith("[[...", StringComparison.Ordinal) && segment.EndsWith("]]", StringComparison.Ordinal))
        {
            return "{*" + segment[5..^2] + "}";
        }

        if (segment.StartsWith("[...", StringComparison.Ordinal) && segment.EndsWith(']'))
        {
            return "{*" + segment[4..^1] + "}";
        }

        return segment.StartsWith('[') && segment.EndsWith(']')
            ? "{" + segment[1..^1] + "}"
            : segment;
    }

    private static string CreateApiId(string component, string method, string route)
    {
        var routeId = Regex.Replace(route.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return $"{component}:{method.ToLowerInvariant()}:{(routeId.Length == 0 ? "root" : routeId)}";
    }

    private static string CombineRoute(string prefix, string suffix)
    {
        var combined = prefix.Trim('/') + "/" + suffix.Trim('/');
        while (combined.Contains("//", StringComparison.Ordinal))
        {
            combined = combined.Replace("//", "/", StringComparison.Ordinal);
        }
        return "/" + combined.Trim('/');
    }

    private static Regex CreateTypePattern(string suffixes)
        => new(
            $"\\b(?:record(?:\\s+class)?|class|struct)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:{suffixes}))\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IReadOnlyList<string> ParseEnumMembers(string body)
        => body.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('=')[0].Trim())
            .Select(value => Regex.Replace(value, "//.*$|/\\*.*?\\*/", string.Empty).Trim())
            .Where(value => Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static RepositoryComponentClassification FindComponentByRelativePath(
        string relativePath,
        RepositoryClassification classification)
        => classification.Components
               .Where(component => component.Root == "."
                   || relativePath.Equals(component.Root, StringComparison.OrdinalIgnoreCase)
                   || relativePath.StartsWith(
                       component.Root.TrimEnd('/') + "/",
                       StringComparison.OrdinalIgnoreCase))
               .OrderByDescending(component => component.Root.Length)
               .FirstOrDefault()
           ?? new RepositoryComponentClassification(
               "repository",
               ".",
               [],
               [],
               [],
               [],
               "unknown",
               []);

    private static string Humanize(string value)
    {
        var separated = Regex.Replace(value.Replace('.', ' ').Replace('-', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ");
        return string.Join(' ', separated.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string CreateCatalogueId(string prefix, string value)
    {
        var normalized = Regex.Replace(Humanize(value).ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
        return $"{prefix}-{(normalized.Length == 0 ? "ITEM" : normalized)}";
    }

    private static string GetSwiftPackageName(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\'));
    }

    private static IReadOnlyList<IReadOnlyList<string>> DistinctRows(IEnumerable<IReadOnlyList<string>> rows)
        => rows.GroupBy(row => string.Join('\u001f', row), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => row[0], StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => string.Join('\u001f', row), StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsEnvironmentExample(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(".env.example", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".env.sample", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".env.template", StringComparison.OrdinalIgnoreCase)
            || name.Equals("example.env", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSensitive(string key)
        => new[] { "password", "secret", "token", "apikey", "api_key", "credential", "connectionstring" }
            .Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string Limit(string value)
        => value.Length <= 80 ? value : value[..77] + "...";

    private static string TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> EnumerateFiles(string repositoryPath)
    {
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var child in directories)
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(child))
                    && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string ToRepositoryPath(string repositoryPath, string path)
        => Path.GetRelativePath(repositoryPath, path).Replace('\\', '/');
}
