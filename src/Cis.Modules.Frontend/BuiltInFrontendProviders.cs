using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Frontend;

internal abstract class FrontendProviderBase
{
    protected static Regex Pattern(string value) => new(value,
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    protected static CisFrontendObservation Observation(string provider, string framework, string kind,
        string name, CisFrontendSourceFile source, int offset, string? route = null, string? target = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var local = Slug($"{framework}-{kind}-{route ?? name}-{source.Path}");
        var line = 1 + source.Content.Take(Math.Clamp(offset, 0, source.Content.Length)).Count(ch => ch == '\n');
        return new(provider, framework, kind, local, name, source.Path, line, route, target,
            properties ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    protected static string Slug(string value)
    {
        var slug = Regex.Replace(value.ToLowerInvariant().Replace('\\', '/'), "[^a-z0-9{}]+", "-").Trim('-');
        return slug.Length <= 160 ? slug : slug[..160];
    }

    protected static string NormalizeRoute(string route)
    {
        var value = route.Trim().Trim('"', '\'');
        if (string.IsNullOrEmpty(value)) return "/";
        value = "/" + value.Trim('/');
        return Regex.Replace(value, @"\[(?:\.\.\.)?([^\]]+)\]", "{$1}");
    }
}

internal sealed class ReactNextFrontendProvider : FrontendProviderBase, ICisFrontendContextProvider
{
    private static readonly Regex Component = Pattern("""(?:export\s+default\s+)?(?:async\s+)?function\s+(?<name>[A-Z][A-Za-z0-9_]*)|(?:export\s+)?(?:const|class)\s+(?<name>[A-Z][A-Za-z0-9_]*)""");
    private static readonly Regex Navigation = Pattern("""(?:href\s*=\s*|(?:router|navigate)\.(?:push|replace)\s*\()\s*[{(]?\s*['\"](?<target>/[^'\"]*)""");
    private static readonly Regex Api = Pattern("""(?:fetch|axios\.(?:get|post|put|patch|delete))\s*\(\s*['\"](?<target>[^'\"]+)""");
    public string Name => "frontend.react-next/1";
    public bool CanInspect(CisFrontendSourceFile source) => source.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
        || source.Path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
        || (source.Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) && source.Content.Contains("next/", StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<CisFrontendObservation> Discover(CisFrontendDiscoveryContext context, CisFrontendSourceFile source)
    {
        var results = new List<CisFrontendObservation>();
        var framework = source.Content.Contains("next/", StringComparison.OrdinalIgnoreCase) || source.Path.Replace('\\', '/').Contains("/app/", StringComparison.OrdinalIgnoreCase) ? "nextjs" : "react";
        var route = RouteFromPath(source.Path);
        string? pageId = null;
        if (route is not null)
        {
            var page = route == "/" ? "Home" : string.Join(' ', route.Trim('/').Split('/').Select(segment => segment.Trim('{', '}')));
            var screen = Observation(Name, framework, "screen", page, source, 0, route, properties: new Dictionary<string, string> { ["screenType"] = "page" });
            results.Add(screen); pageId = screen.Id;
            results.Add(Observation(Name, framework, "route", route, source, 0, route, screen.Id));
        }
        foreach (Match match in Component.Matches(source.Content))
            results.Add(Observation(Name, framework, pageId is not null ? "screen-component" : "component", match.Groups["name"].Value, source, match.Index, route, pageId));
        foreach (Match match in Navigation.Matches(source.Content))
            results.Add(Observation(Name, framework, "navigation", "navigate", source, match.Index, route, NormalizeRoute(match.Groups["target"].Value)));
        foreach (Match match in Api.Matches(source.Content))
            results.Add(Observation(Name, framework, "api-call", "http call", source, match.Index, route, match.Groups["target"].Value));
        if (source.Content.Contains("useState(", StringComparison.Ordinal) || source.Content.Contains("useReducer(", StringComparison.Ordinal))
            results.Add(Observation(Name, framework, "state", "component state", source, source.Content.IndexOf("use", StringComparison.Ordinal), route, pageId));
        return results;
    }
    private static string? RouteFromPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var app = Regex.Match(normalized, @"(?:^|/)app/(?<route>.*?)/?page\.(?:t|j)sx$", RegexOptions.IgnoreCase);
        if (app.Success)
        {
            var segments = app.Groups["route"].Value.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => !(segment.StartsWith('(') && segment.EndsWith(')'))).ToArray();
            return NormalizeRoute(string.Join('/', segments));
        }
        var pages = Regex.Match(normalized, @"(?:^|/)pages/(?<route>.+)\.(?:t|j)sx$", RegexOptions.IgnoreCase);
        return pages.Success ? NormalizeRoute(pages.Groups["route"].Value.Replace("index", string.Empty, StringComparison.OrdinalIgnoreCase)) : null;
    }
}

internal sealed class AngularFrontendProvider : FrontendProviderBase, ICisFrontendContextProvider
{
    private static readonly Regex Component = Pattern("""@Component\s*\([\s\S]*?(?:selector\s*:\s*['\"](?<selector>[^'\"]+)['\"])?[\s\S]*?\)\s*(?:export\s+)?class\s+(?<name>[A-Za-z0-9_]+)""");
    private static readonly Regex Route = Pattern("""\bpath\s*:\s*['\"](?<path>[^'\"]*)['\"][^{}]{0,300}?\bcomponent\s*:\s*(?<target>[A-Za-z0-9_]+)""");
    private static readonly Regex Api = Pattern("""\bhttp\.(?:get|post|put|patch|delete)\s*\(\s*['\"](?<target>[^'\"]+)""");
    public string Name => "frontend.angular/2";
    public bool CanInspect(CisFrontendSourceFile source) => source.Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
        && !source.Path.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase)
        && !source.Path.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)
        && (source.Content.Contains("@angular/", StringComparison.OrdinalIgnoreCase) || source.Content.Contains("@Component", StringComparison.Ordinal));
    public IReadOnlyList<CisFrontendObservation> Discover(CisFrontendDiscoveryContext context, CisFrontendSourceFile source)
    {
        var results = new List<CisFrontendObservation>();
        foreach (Match match in Component.Matches(source.Content))
            results.Add(Observation(Name, "angular", "component", match.Groups["name"].Value, source, match.Index,
                properties: new Dictionary<string, string> { ["selector"] = match.Groups["selector"].Value }));
        var declaration = 0;
        foreach (Match match in Route.Matches(source.Content.Contains("@angular/router", StringComparison.Ordinal) ? source.Content : string.Empty))
        {
            var route = NormalizeRoute(match.Groups["path"].Value);
            var observation = Observation(Name, "angular", "route", route, source, match.Index, route,
                Slug("angular-component-" + match.Groups["target"].Value),
                new Dictionary<string, string> { ["routeResolution"] = "relative-declaration", ["declaredPath"] = match.Groups["path"].Value });
            results.Add(observation with { Id = observation.Id + "/declaration/" + (++declaration).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        foreach (Match match in Api.Matches(source.Content)) results.Add(Observation(Name, "angular", "api-call", "http call", source, match.Index, target: match.Groups["target"].Value));
        return results;
    }
}

internal sealed class VueFrontendProvider : FrontendProviderBase, ICisFrontendContextProvider
{
    private static readonly Regex Route = Pattern("""path\s*:\s*['\"](?<path>[^'\"]+)['\"][\s\S]{0,300}?(?:component\s*:\s*|name\s*:\s*['\"])(?<target>[A-Za-z0-9_]+)""");
    private static readonly Regex Api = Pattern("""(?:fetch|axios\.(?:get|post|put|patch|delete))\s*\(\s*['\"](?<target>[^'\"]+)""");
    public string Name => "frontend.vue/1";
    public bool CanInspect(CisFrontendSourceFile source) => source.Path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase)
        || source.Content.Contains("vue-router", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<CisFrontendObservation> Discover(CisFrontendDiscoveryContext context, CisFrontendSourceFile source)
    {
        var results = new List<CisFrontendObservation>();
        if (source.Path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase)) results.Add(Observation(Name, "vue", "component", Path.GetFileNameWithoutExtension(source.Path), source, 0));
        foreach (Match match in Route.Matches(source.Content)) { var route = NormalizeRoute(match.Groups["path"].Value); results.Add(Observation(Name, "vue", "route", route, source, match.Index, route, match.Groups["target"].Value)); }
        foreach (Match match in Api.Matches(source.Content)) results.Add(Observation(Name, "vue", "api-call", "http call", source, match.Index, target: match.Groups["target"].Value));
        return results;
    }
}

internal sealed class SwiftUiFrontendProvider : FrontendProviderBase, ICisFrontendContextProvider
{
    private static readonly Regex View = Pattern("""struct\s+(?<name>[A-Za-z0-9_]+)\s*:\s*View\b""");
    private static readonly Regex Navigation = Pattern("""NavigationLink\s*\([^\)]*destination\s*:\s*(?<target>[A-Za-z0-9_]+)""");
    private static readonly Regex Api = Pattern("""URLSession\.[A-Za-z0-9_]+\.(?:dataTask|data)\s*\(""");
    public string Name => "frontend.swiftui/1";
    public bool CanInspect(CisFrontendSourceFile source) => source.Path.EndsWith(".swift", StringComparison.OrdinalIgnoreCase) && source.Content.Contains("SwiftUI", StringComparison.Ordinal);
    public IReadOnlyList<CisFrontendObservation> Discover(CisFrontendDiscoveryContext context, CisFrontendSourceFile source)
    {
        var results = View.Matches(source.Content).Select(match => Observation(Name, "swiftui", "screen", match.Groups["name"].Value, source, match.Index)).ToList();
        results.AddRange(Navigation.Matches(source.Content).Select(match => Observation(Name, "swiftui", "navigation", "NavigationLink", source, match.Index, target: match.Groups["target"].Value)));
        results.AddRange(Api.Matches(source.Content).Select(match => Observation(Name, "swiftui", "api-call", "URLSession", source, match.Index)));
        return results;
    }
}

internal sealed class ComposeFrontendProvider : FrontendProviderBase, ICisFrontendContextProvider
{
    private static readonly Regex Composable = Pattern("""@Composable[\s\S]{0,100}?fun\s+(?<name>[A-Za-z0-9_]+)""");
    private static readonly Regex Route = Pattern("""composable\s*\(\s*['\"](?<route>[^'\"]+)['\"]""");
    private static readonly Regex Navigation = Pattern("""navigate\s*\(\s*['\"](?<target>[^'\"]+)['\"]""");
    public string Name => "frontend.compose/1";
    public bool CanInspect(CisFrontendSourceFile source) => (source.Path.EndsWith(".kt", StringComparison.OrdinalIgnoreCase) || source.Path.EndsWith(".kts", StringComparison.OrdinalIgnoreCase))
        && source.Content.Contains("androidx.compose", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<CisFrontendObservation> Discover(CisFrontendDiscoveryContext context, CisFrontendSourceFile source)
    {
        var results = Composable.Matches(source.Content).Select(match => Observation(Name, "jetpack-compose", "screen", match.Groups["name"].Value, source, match.Index)).ToList();
        results.AddRange(Route.Matches(source.Content).Select(match => { var route = NormalizeRoute(match.Groups["route"].Value); return Observation(Name, "jetpack-compose", "route", route, source, match.Index, route); }));
        results.AddRange(Navigation.Matches(source.Content).Select(match => Observation(Name, "jetpack-compose", "navigation", "navigate", source, match.Index, target: NormalizeRoute(match.Groups["target"].Value))));
        return results;
    }
}

internal sealed class GodotFrontendProvider : FrontendProviderBase, ICisFrontendContextProvider
{
    private static readonly Regex Navigation = Pattern("""change_scene_to_file\s*\(\s*['\"](?<target>[^'\"]+)['\"]""");
    public string Name => "frontend.godot/1";
    public bool CanInspect(CisFrontendSourceFile source) => source.Path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)
        || source.Path.EndsWith(".gd", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<CisFrontendObservation> Discover(CisFrontendDiscoveryContext context, CisFrontendSourceFile source)
    {
        var results = new List<CisFrontendObservation>();
        if (source.Path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            results.Add(Observation(Name, "godot", "screen", Path.GetFileNameWithoutExtension(source.Path), source, 0, target: source.Path));
        foreach (Match match in Navigation.Matches(source.Content)) results.Add(Observation(Name, "godot", "navigation", "change scene", source, match.Index, target: match.Groups["target"].Value));
        if (source.Content.Contains("HTTPRequest", StringComparison.Ordinal)) results.Add(Observation(Name, "godot", "api-call", "HTTPRequest", source, source.Content.IndexOf("HTTPRequest", StringComparison.Ordinal)));
        return results;
    }
}
