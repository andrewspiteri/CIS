using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.References;

internal abstract partial class BuiltInReferenceProvider : ICisReferenceProvider
{
    private static readonly string[] SourceExtensions =
    [
        ".cs", ".fs", ".vb", ".ts", ".tsx", ".js", ".jsx", ".swift", ".kt", ".kts",
        ".json", ".yml", ".yaml", ".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".toml",
    ];
    private static readonly HashSet<string> TestPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "test", "tests", "__tests__", "fixtures",
    };

    public abstract string Kind { get; }
    public string CanonicalFileName => Kind + ".md";
    public abstract string Description { get; }

    public virtual bool Supports(string relativePath)
        => SourceExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase)
           && !relativePath.Replace('\\', '/').Split('/')
               .Any(TestPathSegments.Contains);

    public abstract IReadOnlyList<CisReferenceObservation> Discover(
        CisReferenceDiscoveryContext context,
        CisReferenceSourceFile source);

    protected IReadOnlyList<CisReferenceObservation> Match(
        CisReferenceSourceFile source,
        params (Regex Pattern, Func<Match, (string Identity, string Name, IReadOnlyList<string> Aliases)> Map)[] patterns)
    {
        var observations = new Dictionary<string, CisReferenceObservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pattern, map) in patterns)
        {
            foreach (Match match in pattern.Matches(source.Content))
            {
                var mapped = map(match);
                if (string.IsNullOrWhiteSpace(mapped.Identity)) continue;
                var line = 1 + source.Content.AsSpan(0, match.Index).Count('\n');
                var observation = new CisReferenceObservation(
                    Kind, Clean(mapped.Identity), Clean(mapped.Name), source.RelativePath, line,
                    mapped.Aliases.Select(Clean).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                observations.TryAdd(Key(observation), observation);
            }
        }

        return observations.Values.OrderBy(item => item.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    protected static string Clean(string value) => value.Trim().Trim('`', '"', '\'', ' ');
    protected static string Key(CisReferenceObservation item) => $"{item.Identity}|{item.SourcePath}|{item.Line}";
    protected static IReadOnlyList<string> Aliases(params string[] values) => values;
}

internal sealed class ConfigurationReferenceProvider : BuiltInReferenceProvider
{
    private static readonly Regex DotNetIndexer = new("(?:IConfiguration|Configuration|configuration|config)\\s*\\[\\s*\"(?<key>[^\"]+)\"\\s*\\]", RegexOptions.Compiled);
    private static readonly Regex DotNetValue = new("\\b(?:GetValue(?:<[^>]+>)?|BindConfiguration)\\s*\\(\\s*\"(?<key>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex EnvironmentValue = new("(?:GetEnvironmentVariable\\s*\\(\\s*\"|process\\.env\\.|import\\.meta\\.env\\.)(?<key>[A-Za-z_][A-Za-z0-9_:.-]*)", RegexOptions.Compiled);
    private static readonly Regex YamlRoot = new("^(?<key>[A-Za-z_][A-Za-z0-9_.-]*):", RegexOptions.Compiled | RegexOptions.Multiline);

    public override string Kind => "configuration-dictionary";
    public override string Description => "Runtime configuration keys, paths, and sources.";
    public override bool Supports(string relativePath)
        => base.Supports(relativePath) && (Path.GetExtension(relativePath) is ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".json" or ".yml" or ".yaml"
            || relativePath.EndsWith(".props", StringComparison.OrdinalIgnoreCase));

    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
    {
        var patterns = new List<(Regex, Func<Match, (string, string, IReadOnlyList<string>)>)>
        {
            (DotNetIndexer, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))),
            (DotNetValue, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))),
            (EnvironmentValue, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))),
        };
        if (source.RelativePath.EndsWith(".cis/repository.yml", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(source.RelativePath).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(source.RelativePath).Contains("config", StringComparison.OrdinalIgnoreCase))
        {
            patterns.Add((YamlRoot, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))));
            if (source.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var document = JsonDocument.Parse(source.Content);
                    return FlattenJson(source, document.RootElement, null)
                        .Concat(Match(source, patterns.ToArray())).DistinctBy(Key).ToArray();
                }
                catch (JsonException) { }
            }
        }
        var matches = Match(source, patterns.ToArray());
        if (source.RelativePath.EndsWith(".cis/repository.yml", StringComparison.OrdinalIgnoreCase))
            return matches.Where(item => item.Identity is "schema_version" or "documentation_root").ToArray();
        return matches;
    }

    private IReadOnlyList<CisReferenceObservation> FlattenJson(CisReferenceSourceFile source, JsonElement element, string? prefix)
    {
        var result = new List<CisReferenceObservation>();
        if (element.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}:{property.Name}";
            result.Add(new(Kind, key, key, source.RelativePath, null, Aliases(key, property.Name)));
            result.AddRange(FlattenJson(source, property.Value, key));
        }
        return result;
    }
}

internal sealed class PermissionReferenceProvider : BuiltInReferenceProvider
{
    private static readonly Regex Stable = new("\\b(?<key>PERM-[A-Z0-9][A-Z0-9_-]*)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Policy = new(
        "(?:\\bPolicy\\s*=\\s*|\\b(?:RequireAuthorization|HasPermission|hasPermission|can)\\s*\\(\\s*)\"(?<key>[A-Za-z][A-Za-z0-9:._-]{1,127})\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public override string Kind => "permissions-dictionary";
    public override string Description => "Authorization policies and permission codes used by source.";
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
        => Match(source,
            (Stable, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))),
            (Policy, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))));
}

internal abstract class TypeReferenceProvider : BuiltInReferenceProvider
{
    private readonly Regex _type;
    private readonly Regex _stable;
    protected TypeReferenceProvider(string suffix, string stablePrefix)
    {
        _type = new Regex($"\\b(?:class|record|interface|type|data\\s+class|sealed\\s+class)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*{suffix})\\b", RegexOptions.Compiled);
        _stable = new Regex($"\\b(?<key>{stablePrefix}-[A-Z0-9][A-Z0-9_-]*)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
        => Match(source,
            (_type, m => (m.Groups["name"].Value, m.Groups["name"].Value, Aliases(m.Groups["name"].Value))),
            (_stable, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))));
}

internal sealed class CommandReferenceProvider : TypeReferenceProvider
{
    public CommandReferenceProvider() : base("Command", "CMD") { }
    public override string Kind => "command-dictionary";
    public override string Description => "Requested-action command types and stable command identities.";
}

internal sealed class EventReferenceProvider : TypeReferenceProvider
{
    public EventReferenceProvider() : base("Event", "EVT") { }
    public override string Kind => "event-dictionary";
    public override string Description => "Published and consumed event types and stable event identities.";
}

internal sealed class ProjectionReferenceProvider : TypeReferenceProvider
{
    public ProjectionReferenceProvider() : base("(?:Projection|ReadModel)", "PROJ") { }
    public override string Kind => "projection-dictionary";
    public override string Description => "Projection and read-model types.";
}

internal sealed class WorkflowStateReferenceProvider : BuiltInReferenceProvider
{
    private static readonly Regex Stable = new("\\b(?<key>(?:WF|STATE)-[A-Z0-9][A-Z0-9_-]*)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Enum = new("\\benum\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:State|Status))\\b", RegexOptions.Compiled);
    public override string Kind => "workflow-state-dictionary";
    public override string Description => "Workflow and lifecycle state identities.";
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
        => Match(source,
            (Stable, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))),
            (Enum, m => (m.Groups["name"].Value, m.Groups["name"].Value, Aliases(m.Groups["name"].Value))));
}

internal sealed class StableLiteralReferenceProvider : BuiltInReferenceProvider
{
    private readonly string _kind;
    private readonly string _description;
    private readonly Regex _pattern;
    public StableLiteralReferenceProvider(string kind, string description, string prefix)
    {
        _kind = kind; _description = description;
        _pattern = new($"\\b(?<key>{prefix}-[A-Z0-9][A-Z0-9_-]*)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
    public override string Kind => _kind;
    public override string Description => _description;
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
        => Match(source, (_pattern, m => (m.Groups["key"].Value, m.Groups["key"].Value, Aliases(m.Groups["key"].Value))));
}

internal sealed class PackageReferenceProvider : BuiltInReferenceProvider
{
    private static readonly Regex XmlPackage = new("<PackageReference\\s+Include=\"(?<name>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public override string Kind => "package-catalogue";
    public override string Description => "Declared build and runtime package dependencies.";
    public override bool Supports(string relativePath)
        => relativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
    {
        var component = Component(source.RelativePath, source.Content);
        var result = Match(source, (XmlPackage, m =>
        {
            var package = m.Groups["name"].Value;
            return ($"{package}@{component}", package, Aliases($"{package}@{component}"));
        })).ToList();
        if (!source.RelativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)) return result;
        try
        {
            using var document = JsonDocument.Parse(source.Content);
            foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
            {
                if (!document.RootElement.TryGetProperty(section, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object) continue;
                result.AddRange(dependencies.EnumerateObject().Select(item => new CisReferenceObservation(
                    Kind, $"{item.Name}@{component}", item.Name, source.RelativePath, null, Aliases($"{item.Name}@{component}"),
                    new Dictionary<string, string> { ["dependencyClass"] = section, ["version"] = item.Value.ToString() })));
            }
        }
        catch (JsonException) { }
        return result.DistinctBy(Key).OrderBy(item => item.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Component(string relativePath, string content)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        if (name.Equals("package", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileName(Path.GetDirectoryName(relativePath));
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("name", out var packageName)
                    && packageName.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(packageName.GetString()))
                    name = packageName.GetString();
            }
            catch (JsonException) { }
        }
        return Regex.Replace(name ?? "root", "([a-z0-9])([A-Z])", "$1-$2").Replace('.', '-').Replace('_', '-').ToLowerInvariant();
    }
}

internal sealed class ScreenRouteReferenceProvider : BuiltInReferenceProvider
{
    private static readonly Regex Route = new("(?:path\\s*:\\s*|href\\s*=\\s*|router\\.(?:push|replace)\\s*\\()\"?(?<route>/[A-Za-z0-9_./:{}\\[\\]-]*)", RegexOptions.Compiled);
    public override string Kind => "screen-route-map";
    public override string Description => "Frontend and native navigation routes.";
    public override bool Supports(string relativePath)
        => Path.GetExtension(relativePath) is ".ts" or ".tsx" or ".js" or ".jsx" or ".swift" or ".kt" or ".kts";
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
    {
        var result = Match(source, (Route, m => (m.Groups["route"].Value, m.Groups["route"].Value, Aliases(m.Groups["route"].Value)))).ToList();
        var normalized = source.RelativePath.Replace('\\', '/');
        var marker = normalized.IndexOf("/app/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0 && normalized.EndsWith("/page.tsx", StringComparison.OrdinalIgnoreCase))
        {
            var route = normalized[(marker + 4)..^9];
            route = Regex.Replace(route, @"/\([^/]+\)", string.Empty);
            route = Regex.Replace(route, @"\[([^]]+)\]", "{$1}");
            route = "/" + route.Trim('/');
            result.Add(new(Kind, route, route, source.RelativePath, 1, Aliases(route)));
        }
        return result.DistinctBy(Key).ToArray();
    }
}

internal sealed class ModuleOwnershipReferenceProvider : BuiltInReferenceProvider
{
    public override string Kind => "module-ownership-map";
    public override string Description => "Project and package module boundaries.";
    public override bool Supports(string relativePath)
        => relativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
    {
        var name = Path.GetFileNameWithoutExtension(source.RelativePath);
        if (source.RelativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(source.Content);
                if (document.RootElement.TryGetProperty("name", out var property) && property.ValueKind == JsonValueKind.String)
                    name = property.GetString() ?? name;
            }
            catch (JsonException) { }
        }
        var slug = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1-$2").Replace('.', '-').Replace('_', '-').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(name) ? [] : [new(Kind, name, name, source.RelativePath, 1, Aliases(name, slug, Path.GetDirectoryName(source.RelativePath)?.Replace('\\', '/') ?? string.Empty))];
    }
}

internal sealed class DataReferenceProvider : BuiltInReferenceProvider
{
    private static readonly Regex DbSet = new("\\bDbSet<(?<name>[A-Za-z_][A-Za-z0-9_.]*)>", RegexOptions.Compiled);
    private static readonly Regex Entity = new("\\b(?:class|record|interface|data\\s+class)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:Entity|Record))\\b", RegexOptions.Compiled);
    public override string Kind => "data-dictionary";
    public override string Description => "Persistent entity types discovered from persistence source.";
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
        => Match(source,
            (DbSet, m => (m.Groups["name"].Value, m.Groups["name"].Value, Aliases(m.Groups["name"].Value))),
            (Entity, m => (m.Groups["name"].Value, m.Groups["name"].Value, Aliases(m.Groups["name"].Value))));
}

internal sealed class ErdReferenceProvider : BuiltInReferenceProvider
{
    private static readonly Regex Relation = new("\\b(?:HasOne|HasMany)<(?<target>[A-Za-z_][A-Za-z0-9_.]*)>|\\bDbSet<(?<target2>[A-Za-z_][A-Za-z0-9_.]*)>", RegexOptions.Compiled);
    public override string Kind => "erd";
    public override string Description => "Persistent relationship and aggregate evidence.";
    public override IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
        => Match(source, (Relation, m =>
        {
            var target = m.Groups["target"].Success ? m.Groups["target"].Value : m.Groups["target2"].Value;
            return (target, target, Aliases(target));
        }));
}
