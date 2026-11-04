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
        // MSBuild automatically takes the nearest file of each kind. Follow only
        // bounded literal local imports; never run MSBuild or target code.
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
        var uncertainRunner = false;
        bool? testProperty = null;
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var activeImports = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var visitedInputs = new HashSet<string>(activeImports.Comparer);
        var inputCount = 0;
        void Unverified(string message)
        {
            warnings.Add(message);
            uncertainTest = uncertainRunner = true;
        }
        bool TryLocalPath(string value, string source, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['$', '@', '%', '*', '?', ';', '\0']) >= 0) return false;
            try
            {
                var absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, value.Replace('\\', Path.DirectorySeparatorChar)));
                if (!CisPathSafety.IsUnderRoot(repository, absolute) || CisPathSafety.ContainsReparsePoint(repository, absolute)) return false;
                path = absolute;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return false; }
        }
        bool? Condition(string? condition, string source)
        {
            var literal = LiteralCondition(condition, properties);
            if (literal is not null) return literal;
            var exists = System.Text.RegularExpressions.Regex.Match(condition!, @"^\s*Exists\(\s*['""](?<path>[^'""]+)['""]\s*\)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return exists.Success && TryLocalPath(exists.Groups["path"].Value, source, out var path) ? File.Exists(path) || Directory.Exists(path) : null;
        }
        void ReadInput(string path, XDocument document, int depth)
        {
            var relative = Path.GetRelativePath(repository, path).Replace('\\', '/');
            if (activeImports.Contains(path))
            { Unverified($"Cyclic MSBuild import in {relative}; test configuration remains unverified."); return; }
            if (visitedInputs.Any(previous => previous.Equals(path, StringComparison.OrdinalIgnoreCase) && !visitedInputs.Comparer.Equals(previous, path)))
            { Unverified($"MSBuild import paths differ only by casing at {relative}; test configuration remains unverified on this filesystem."); return; }
            // MSBuild ignores a file already imported in this evaluation. In
            // particular a repeated targets import cannot undo a later setting.
            if (!visitedInputs.Add(path)) return;
            if (depth > 16 || ++inputCount > 32)
            { Unverified($"Static MSBuild import inspection reached its 16-level or 32-file bound at {relative}; test configuration remains unverified."); return; }
            activeImports.Add(path);
            var groupConditions = new Dictionary<XElement, bool?>();
            var declared = false;
            foreach (var declaration in document.Descendants())
            {
                if (declaration.Ancestors().Any(element => element.Name.LocalName == "Choose")) continue;
                if (declaration.Name.LocalName == "Choose")
                {
                    declared = true;
                    var relevant = false;
                    foreach (var choice in declaration.Descendants())
                    {
                        if (choice.Parent?.Name.LocalName == "PropertyGroup")
                        {
                            properties.Remove(choice.Name.LocalName);
                            if (choice.Name.LocalName == "IsTestProject") testProperty = null;
                        }
                        var packageName = choice.Attribute("Include")?.Value ?? choice.Attribute("Update")?.Value ?? choice.Attribute("Remove")?.Value;
                        relevant |= choice.Name.LocalName is "Import" or "IsTestProject" || IsRunnerProperty(choice.Name.LocalName)
                            || choice.Name.LocalName == "PackageReference" && (IsTestPackage(packageName) || IsUnresolvedPackageIdentity(packageName));
                    }
                    if (relevant) Unverified($"Unsupported MSBuild Choose control flow in {relative}; test configuration remains unverified.");
                    continue;
                }
                // Target bodies are runtime build actions, not initial project
                // metadata. Never interpret them as evaluated test settings.
                if (declaration.Ancestors().Any(element => element.Name.LocalName == "Target"))
                {
                    if (declaration.Name.LocalName is "Import" or "IsTestProject" || IsRunnerProperty(declaration.Name.LocalName)
                        || declaration.Name.LocalName == "PackageReference" && IsTestPackage(declaration.Attribute("Include")?.Value))
                        Unverified($"Build-time test declarations in {relative} were not evaluated; test configuration remains unverified.");
                    continue;
                }
                var import = declaration.Name.LocalName == "Import";
                if (!import && declaration.Name.LocalName != "PackageReference" && declaration.Parent?.Name.LocalName != "PropertyGroup") continue;
                declared = true;
                // MSBuild evaluates a group's condition once, before its children
                // can assign a property used by that condition.
                var conditions = declaration.AncestorsAndSelf().Reverse().Select(element =>
                {
                    if (element == declaration) return Condition(element.Attribute("Condition")?.Value, path);
                    if (!groupConditions.TryGetValue(element, out var result))
                        groupConditions[element] = result = Condition(element.Attribute("Condition")?.Value, path);
                    return result;
                }).ToArray();
                if (conditions.Contains(false)) continue;
                if (import)
                {
                    var importValue = declaration.Attribute("Project")?.Value ?? "";
                    if (conditions.Contains(null) || declaration.Attribute("Sdk") is not null || !TryLocalPath(importValue, path, out var imported))
                    { Unverified($"MSBuild import in {relative} is conditional, dynamic, linked or outside the repository; test configuration remains unverified."); continue; }
                    try
                    {
                        if (!File.Exists(imported) || new FileInfo(imported).Length > 1_048_576)
                        { Unverified($"MSBuild import from {relative} is missing or exceeds the 1 MiB static metadata bound; test configuration remains unverified."); continue; }
                        var metadata = XDocument.Load(imported);
                        if (metadata.Root?.Name.LocalName != "Project" || metadata.Root?.Attribute("Sdk") is not null
                            || metadata.Descendants().Any(element => element.Name.LocalName is "Sdk" or "UsingTask"))
                        { Unverified($"MSBuild import from {relative} contains unsupported SDK, control-flow or executable build configuration; test configuration remains unverified."); continue; }
                        ReadInput(imported, metadata, depth + 1);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                    { Unverified($"Unable to inspect MSBuild import from {relative}: {exception.Message}"); }
                    continue;
                }
                if (conditions.Contains(null))
                {
                    if (declaration.Parent?.Name.LocalName == "PropertyGroup") properties.Remove(declaration.Name.LocalName);
                    uncertainRunner |= IsRunnerProperty(declaration.Name.LocalName);
                    var packageName = declaration.Attribute("Include")?.Value ?? declaration.Attribute("Update")?.Value ?? declaration.Attribute("Remove")?.Value;
                    var testDeclaration = declaration.Name.LocalName == "IsTestProject"
                        || declaration.Name.LocalName == "PackageReference" && (IsTestPackage(packageName) || IsUnresolvedPackageIdentity(packageName));
                    if (testDeclaration || IsRunnerProperty(declaration.Name.LocalName))
                        warnings.Add($"Conditional .NET test/package declaration in {relative} requires evaluated configuration review.");
                    uncertainTest |= testDeclaration;
                    continue;
                }
                if (declaration.Parent?.Name.LocalName == "PropertyGroup")
                {
                    if (!declaration.Value.Contains("$(", StringComparison.Ordinal)
                        && !declaration.Value.Contains("@(", StringComparison.Ordinal))
                        properties[declaration.Name.LocalName] = declaration.Value.Trim();
                    else properties.Remove(declaration.Name.LocalName);
                    if (IsRunnerProperty(declaration.Name.LocalName) && !bool.TryParse(declaration.Value.Trim(), out _))
                        uncertainRunner = true;
                }
                if (declaration.Name.LocalName == "IsTestProject" && bool.TryParse(declaration.Value.Trim(), out var value))
                    testProperty = value;
                if (declaration.Name.LocalName == "PackageReference")
                {
                    if (new[] { "Include", "Update", "Remove" }.Any(name => IsUnresolvedPackageIdentity(declaration.Attribute(name)?.Value)))
                    {
                        Unverified($"Nonliteral .NET package identity in {relative} requires evaluated configuration review; test configuration remains unverified.");
                        continue;
                    }
                    var remove = declaration.Attribute("Remove")?.Value;
                    if (remove is not null) packages.Remove(remove);
                    var include = declaration.Attribute("Include")?.Value;
                    if (include is not null && !include.Contains("$(", StringComparison.Ordinal)) packages.Add(include);
                    var update = declaration.Attribute("Update")?.Value;
                    if (update is not null && IsTestPackage(update)) uncertainTest = true;
                }
            }
            if (declared || depth > 0) evidence.Add(relative);
            activeImports.Remove(path);
        }
        foreach (var input in inputs) ReadInput(input.Path, input.Document, 0);

        var sdk = project.Root?.Attribute("Sdk")?.Value ?? "";
        var frameworks = new List<string>();
        if (packages.Contains("xunit") || packages.Contains("xunit.v3") || packages.Contains("xunit.v3.core")) frameworks.Add("xunit");
        if (packages.Contains("NUnit")) frameworks.Add("nunit");
        if (packages.Contains("MSTest.TestFramework") || packages.Contains("MSTest") || sdk.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase)) frameworks.Add("mstest");
        if (packages.Contains("TUnit")) frameworks.Add("tunit");
        if (packages.Contains("TUnit") || packages.Contains("Microsoft.Testing.Platform") || sdk.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase))
            frameworks.Add("microsoft-testing-platform");
        if (packages.Any(package => package.StartsWith("Reqnroll.", StringComparison.OrdinalIgnoreCase))) frameworks.Add("reqnroll");
        var resolvedProperties = new XDocument(new XElement("Project", new XElement("PropertyGroup",
            properties.Select(property => new XElement(property.Key, property.Value)))));
        var dualXunitRunner = (packages.Contains("xunit.v3") || packages.Contains("xunit.v3.core"))
            && packages.Contains("Microsoft.NET.Test.Sdk") && packages.Contains("xunit.runner.visualstudio");
        var runner = DotNetRunnerSettings.Read(repository, directory, [resolvedProperties], warnings, dualXunitRunner);
        runner |= uncertainRunner;
        if (uncertainTest) frameworks.Add("test-runner-unverified");
        var isTest = testProperty ?? (frameworks.Count > 0 || packages.Contains("Microsoft.NET.Test.Sdk"));
        if (isTest && runner && !frameworks.Contains("microsoft-testing-platform")) frameworks.Add("microsoft-testing-platform");
        if (isTest && (frameworks.Contains("microsoft-testing-platform") || uncertainTest))
            warnings.Add($"Native test binding for {Path.GetRelativePath(repository, projectPath)} requires runner/reporter qualification; no VSTest command was generated for this MTP or unverified configuration.");
        return new(isTest, isTest ? frameworks : [], packages, evidence.Distinct(StringComparer.Ordinal).ToArray());
    }

    // Deliberately bounded literal evaluation: never execute MSBuild, imports, tasks,
    // property functions, or target-repository assemblies during discovery.
    private static bool? LiteralCondition(string? condition, IReadOnlyDictionary<string, string> properties)
    {
        if (condition is null) return true;
        var match = System.Text.RegularExpressions.Regex.Match(condition,
            "^\\s*['\"]\\$\\((?<property>[A-Za-z_][A-Za-z0-9_]*)\\)['\"]\\s*(?<op>==|!=)\\s*['\"](?<value>[^'\"$@]*)['\"]\\s*$");
        if (!match.Success || !properties.TryGetValue(match.Groups["property"].Value, out var value)) return null;
        var equal = value.Equals(match.Groups["value"].Value, StringComparison.OrdinalIgnoreCase);
        return match.Groups["op"].Value == "==" ? equal : !equal;
    }

    private static bool IsTestPackage(string? package) => package is not null &&
        (package.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
         || new[] { "xunit", "NUnit", "MSTest", "TUnit", "Microsoft.Testing.Platform", "Reqnroll" }
             .Any(prefix => package.Equals(prefix, StringComparison.OrdinalIgnoreCase) || package.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)));

    private static bool IsUnresolvedPackageIdentity(string? value)
        => value?.IndexOfAny(['$', '@', '%', '*', '?', ';']) >= 0;

    private static bool IsRunnerProperty(string name) => name is "TestingPlatformDotnetTestSupport"
        or "UseMicrosoftTestingPlatformRunner" or "EnableNUnitRunner" or "EnableMSTestRunner";
}
