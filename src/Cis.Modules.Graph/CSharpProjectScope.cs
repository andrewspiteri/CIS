using System.Xml.Linq;

namespace Cis.Modules.Graph;

internal static class CSharpProjectScope
{
    internal static CSharpCompilerInput CreateInput(string repository, string path, string content,
        string componentId, bool owned, IEnumerable<string> paths)
    {
        var projects = paths.Where(item => item.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidates = projects.Where(project => path.StartsWith(DirectoryPrefix(project), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(project => DirectoryPrefix(project).Length).ToArray();
        // Ambiguous colocated projects and detached snapshots get file-scoped identities.
        var project = candidates.FirstOrDefault();
        if (project is null || candidates.Count(candidate => DirectoryPrefix(candidate) == DirectoryPrefix(project)) > 1)
            return new(path, content, componentId, owned) { ProjectPath = "source:" + path };
        var references = new List<string>();
        try
        {
            var document = XDocument.Load(Path.Combine(repository, project));
            foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"
                && !element.AncestorsAndSelf().Any(parent => parent.Attribute("Condition") is not null)))
            {
                var include = reference.Attribute("Include")?.Value;
                if (include is null || include.Contains("$(", StringComparison.Ordinal)) continue;
                var absolute = Path.GetFullPath(Path.Combine(repository, DirectoryPrefix(project), include.Replace('\\', '/')));
                var relative = Path.GetRelativePath(repository, absolute).Replace('\\', '/');
                var canonical = projects.FirstOrDefault(candidate => candidate.Equals(relative, StringComparison.OrdinalIgnoreCase));
                if (canonical is not null) references.Add(canonical);
            }
        }
        catch (System.Xml.XmlException) { /* Unsupported manifests cannot establish dependencies. */ }
        return new(path, content, componentId, owned) { ProjectPath = project, ProjectReferences = references };
    }

    private static string DirectoryPrefix(string path) => path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : "";
}
