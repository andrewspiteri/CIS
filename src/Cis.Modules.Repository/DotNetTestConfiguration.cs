using System.Xml.Linq;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Reads literal test declarations without evaluating MSBuild or loading project code.</summary>
internal sealed record DotNetTestConfiguration(bool IsTest, IReadOnlyList<string> Frameworks,
    IReadOnlySet<string> Packages, IReadOnlyList<string> Evidence)
{
    internal static DotNetTestConfiguration Read(string repository, string projectPath, XDocument project,
        ICollection<string> warnings)
    {
        var inputs = new List<(string Path, XDocument Document)>();
        var directory = Path.GetDirectoryName(projectPath)!;
        // MSBuild automatically takes the nearest file of each kind. Parent imports and conditions
        // require evaluation, so do not silently combine every ancestor or execute an import.
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
        {
            for (var current = directory; current is not null && CisPathSafety.IsUnderRoot(repository, current, allowRoot: true);
                 current = Path.GetDirectoryName(current))
            {
                var path = Path.Combine(current, name);
                if (!File.Exists(path)) continue;
                if (CisPathSafety.ContainsReparsePoint(repository, path))
                {
                    warnings.Add($"Skipped linked .NET configuration: {Path.GetRelativePath(repository, path)}");
                    break;
                }
                try { inputs.Add((path, XDocument.Load(path))); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                { warnings.Add($"Unable to inspect {Path.GetRelativePath(repository, path)}: {exception.Message}"); }
                break;
            }
        }
        var targetsIndex = inputs.FindIndex(item => Path.GetFileName(item.Path) == "Directory.Build.targets");
        inputs.Insert(targetsIndex < 0 ? inputs.Count : targetsIndex, (projectPath, project));
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidence = new List<string>();
        var uncertainTest = false;
        bool? testProperty = null;
        foreach (var input in inputs)
        {
            var relative = Path.GetRelativePath(repository, input.Path).Replace('\\', '/');
            var declarations = input.Document.Descendants().Where(element => element.Name.LocalName is "PackageReference" or "IsTestProject").ToArray();
            foreach (var declaration in declarations)
            {
                if (declaration.AncestorsAndSelf().Any(element => element.Attribute("Condition") is not null))
                {
                    warnings.Add($"Conditional .NET test/package declaration in {relative} requires evaluated configuration review.");
                    uncertainTest |= declaration.Name.LocalName == "IsTestProject"
                        || IsTestPackage(declaration.Attribute("Include")?.Value ?? declaration.Attribute("Update")?.Value);
                    continue;
                }
                if (declaration.Name.LocalName == "IsTestProject" && bool.TryParse(declaration.Value.Trim(), out var value))
                    testProperty = value;
                if (declaration.Name.LocalName == "PackageReference")
                {
                    var remove = declaration.Attribute("Remove")?.Value;
                    if (remove is not null) packages.Remove(remove);
                    var include = declaration.Attribute("Include")?.Value;
                    if (include is not null && !include.Contains("$(", StringComparison.Ordinal)) packages.Add(include);
                    var update = declaration.Attribute("Update")?.Value;
                    if (update is not null && IsTestPackage(update)) uncertainTest = true;
                }
            }
            if (declarations.Length > 0) evidence.Add(relative);
            if (input.Document.Descendants().Any(element => element.Name.LocalName == "Import"))
                warnings.Add($"Explicit MSBuild imports in {relative} were not evaluated; verify inherited test configuration.");
        }

        var sdk = project.Root?.Attribute("Sdk")?.Value ?? "";
        var frameworks = new List<string>();
        if (packages.Contains("xunit") || packages.Contains("xunit.v3") || packages.Contains("xunit.v3.core")) frameworks.Add("xunit");
        if (packages.Contains("NUnit")) frameworks.Add("nunit");
        if (packages.Contains("MSTest.TestFramework") || packages.Contains("MSTest") || sdk.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase)) frameworks.Add("mstest");
        if (packages.Contains("TUnit")) frameworks.Add("tunit");
        if (packages.Contains("TUnit") || packages.Contains("Microsoft.Testing.Platform") || sdk.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase))
            frameworks.Add("microsoft-testing-platform");
        if (packages.Any(package => package.StartsWith("Reqnroll.", StringComparison.OrdinalIgnoreCase))) frameworks.Add("reqnroll");
        var runner = DotNetRunnerSettings.Read(repository, directory, inputs.Select(input => input.Document), warnings);
        if (uncertainTest) frameworks.Add("test-runner-unverified");
        var isTest = testProperty ?? (frameworks.Count > 0 || packages.Contains("Microsoft.NET.Test.Sdk"));
        if (isTest && runner && !frameworks.Contains("microsoft-testing-platform")) frameworks.Add("microsoft-testing-platform");
        if (isTest && (frameworks.Contains("microsoft-testing-platform") || uncertainTest))
            warnings.Add($"Native test binding for {Path.GetRelativePath(repository, projectPath)} requires runner/reporter qualification; no VSTest command was generated for this MTP or unverified configuration.");
        return new(isTest, isTest ? frameworks : [], packages, evidence.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool IsTestPackage(string? package) => package is not null &&
        (package.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
         || new[] { "xunit", "NUnit", "MSTest", "TUnit", "Microsoft.Testing.Platform", "Reqnroll" }
             .Any(prefix => package.Equals(prefix, StringComparison.OrdinalIgnoreCase) || package.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)));
}
