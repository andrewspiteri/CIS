using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

internal sealed record RepositoryTestingStarterContent(
    string Workflow,
    string SecurityWorkflow,
    string Profile,
    string SecurityProfile,
    string AcceptedSecurityFindings);

internal static class RepositoryTestingStarter
{
    public static RepositoryTestingStarterContent Create(
        string repositoryPath,
        string documentationRoot,
        RepositoryClassification classification)
    {
        var suites = new List<Suite>();
        var steps = new List<Step>();
        AddDotNet(repositoryPath, classification, suites, steps);
        AddNode(repositoryPath, classification, suites, steps);
        AddInfrastructure(repositoryPath, classification, steps);
        var security = RepositorySecurityStarter.Create(repositoryPath, classification);
        foreach (var step in security.Steps)
            steps.Add(new(step.Id, step.Command, step.WorkingDirectory, step.SuiteId, step.DependsOn, "no", step.TimeoutSeconds));

        if (suites.Count == 0)
        {
            suites.Add(new("documentation", "repository", "operational", "cis-docs", "cis docs validate --strict",
                ".", "junit", ".cis/local/testing/results/documentation.xml", "-", "-",
                "CIS CLI and a repository-owned JUnit export wrapper", "documentation changes", "pr", "retain-on-failure"));
        }

        steps.Add(new("docs", "cis docs validate --repo . --strict", ".", "-", "-", "no", 600));
        return new(
            CreateWorkflow(steps),
            CreateSecurityWorkflow(security.Steps),
            CreateProfile(suites),
            security.Profile,
            RepositorySecurityStarter.AcceptedFindings());
    }

    private static void AddDotNet(string repositoryPath, RepositoryClassification classification,
        ICollection<Suite> suites, ICollection<Step> steps)
    {
        var solution = Directory.EnumerateFiles(repositoryPath, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(repositoryPath, "*.sln", SearchOption.TopDirectoryOnly)).FirstOrDefault();
        if (solution is not null && classification.Components.Any(item => item.Frameworks.Contains("dotnet-test", StringComparer.Ordinal)))
        {
            const string id = "dotnet-tests";
            const string result = ".cis/local/testing/results/dotnet-tests.trx";
            var command = $"dotnet test {Quote(Path.GetFileName(solution))} --logger trx;LogFileName={id}.trx --results-directory .cis/local/testing/results";
            suites.Add(new(id, "repository", "unit", "dotnet-test", command, ".", "trx", result,
                "-", "-", ".NET SDK", "C# production changes", "pr", "retain-on-failure"));
            steps.Add(new(id, command, ".", id, "build", "no", 3600));
            steps.Add(new("build", $"dotnet build {Quote(Path.GetFileName(solution))} --no-restore", ".", "-", "-", "no", 1200));
            return;
        }
        foreach (var component in classification.Components.Where(item => item.Frameworks.Contains("dotnet-test", StringComparer.Ordinal)))
        {
            var project = component.Evidence.FirstOrDefault(item => item.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (project is null) continue;
            var id = SafeId(component.Id + "-unit");
            var result = $".cis/local/testing/results/{id}.trx";
            var command = $"dotnet test {Quote(project)} --logger trx;LogFileName={id}.trx --results-directory .cis/local/testing/results --collect XPlat Code Coverage";
            suites.Add(new(id, component.Id, "unit", "dotnet-test", command, ".", "trx", result,
                $".cis/local/testing/coverage/{id}/coverage.cobertura.xml", "-", ".NET SDK", "C# production changes", "pr", "retain-on-failure"));
            steps.Add(new(id, command, ".", id, "-", "no", 1800));
        }

        if (classification.Components.Any(item => item.Languages.Contains("csharp", StringComparer.Ordinal)) && suites.All(item => item.Framework != "dotnet-test"))
        {
            var target = solution is null ? string.Empty : " " + Quote(Path.GetFileName(solution));
            steps.Add(new("build", $"dotnet build{target} --no-restore", ".", "-", "-", "no", 1200));
        }
    }

    private static void AddNode(string repositoryPath, RepositoryClassification classification,
        ICollection<Suite> suites, ICollection<Step> steps)
    {
        foreach (var component in classification.Components.Where(item => item.Languages.Any(language => language is "typescript" or "javascript")))
        {
            var root = Path.GetFullPath(Path.Combine(repositoryPath, component.Root.Replace('/', Path.DirectorySeparatorChar)));
            var package = Path.Combine(root, "package.json");
            if (!File.Exists(package)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(package));
            var scripts = document.RootElement.TryGetProperty("scripts", out var value) && value.ValueKind == JsonValueKind.Object
                ? value.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var packages = PackageNames(document.RootElement);
            var prefix = PackageRunner(repositoryPath, root);

            var testScript = FirstScript(scripts, "test:unit", "test", "unit");
            if (testScript is not null && packages.Contains("vitest"))
            {
                var id = SafeId(component.Id + "-unit");
                var result = $".cis/local/testing/results/{id}.json";
                var output = Relative(root, Path.Combine(repositoryPath, result.Replace('/', Path.DirectorySeparatorChar)));
                var coverage = $".cis/local/testing/coverage/{id}/coverage-summary.json";
                var command = $"{prefix} run {testScript} -- --reporter=json --outputFile={output}";
                suites.Add(new(id, component.Id, "unit", "vitest", command, component.Root, "vitest-json", result,
                    coverage, "-", "Node.js and installed dependencies", "TypeScript or JavaScript production changes", "pr", "retain-on-failure"));
                steps.Add(new(id, command, component.Root, id, "-", "no", 1200));
            }

            var cucumber = FirstScript(scripts, "test:business", "test:cucumber", "cucumber");
            if (cucumber is not null && packages.Contains("@cucumber/cucumber"))
            {
                var id = SafeId(component.Id + "-business");
                var result = $".cis/local/testing/results/{id}.xml";
                var output = Relative(root, Path.Combine(repositoryPath, result.Replace('/', Path.DirectorySeparatorChar)));
                var command = $"{prefix} run {cucumber} -- --format junit:{output}";
                suites.Add(new(id, component.Id, "business", "cucumber", command, component.Root, "cucumber-junit", result,
                    "-", "-", "Node.js and installed dependencies", "business behavior changes", "pr", "retain-on-failure"));
                steps.Add(new(id, command, component.Root, id, "-", "no", 1800));
            }

            var browser = FirstScript(scripts, "test:e2e", "test:browser", "e2e");
            if (browser is not null && packages.Contains("@playwright/test"))
            {
                var id = SafeId(component.Id + "-browser");
                var prerequisiteId = SafeId(component.Id + "-browser-prerequisite");
                var result = $".cis/local/testing/results/{id}.json";
                var command = $"{prefix} run {browser}";
                suites.Add(new(id, component.Id, "browser", "playwright", command, component.Root, "playwright-json", result,
                    "-", "-", "Node.js, installed browsers, application runtime, and JSON reporter output", "critical frontend journeys", "release", "retain-on-failure"));
                steps.Add(new(prerequisiteId, $"{prefix} exec playwright install chromium", component.Root, "-", "-", "no", 1200));
                steps.Add(new(id, command, component.Root, id, prerequisiteId, "no", 2400));
            }

            if (scripts.ContainsKey("build")) steps.Add(new(SafeId(component.Id + "-build"), $"{prefix} run build", component.Root, "-", "-", "no", 1200));
        }
    }

    private static void AddInfrastructure(string repositoryPath, RepositoryClassification classification, ICollection<Step> steps)
    {
        if (classification.Components.Any(item => item.Frameworks.Contains("docker-compose", StringComparer.Ordinal)))
            steps.Add(new("compose-validate", "docker compose config --quiet", ".", "-", "-", "no", 300));
        if (classification.Components.Any(item => item.Frameworks.Contains("terraform", StringComparer.Ordinal)))
        {
            var root = CisPathSafety.EnumerateFiles(repositoryPath, "*.tf")
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.terraform{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetDirectoryName).FirstOrDefault() ?? repositoryPath;
            steps.Add(new("terraform-fmt", "terraform fmt -check", Relative(repositoryPath, root), "-", "-", "no", 300));
            steps.Add(new("terraform-validate", "terraform validate", Relative(repositoryPath, root), "-", "terraform-fmt", "no", 600));
        }
    }

    private static string CreateWorkflow(IEnumerable<Step> steps)
    {
        var body = string.Join(Environment.NewLine, steps.DistinctBy(item => item.Id).Select(item =>
            $"| {item.Id} | {item.Command} | {item.WorkingDirectory} | {item.Suites} | {item.DependsOn} | {item.ContinueOnFailure} | {item.Timeout} |"));
        return $$"""
        ---
        title: "Standard Delivery Verification Workflow"
        type: workflow-definition
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-26"
        review_cadence: "on build, test, runtime, or classification change"
        ---

        # Standard delivery verification workflow

        Commands are selected from repository classification and declared package scripts. Reinitialization preserves reviewed commands.

        | Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
        |---|---|---|---|---|---|---:|
        {{body}}
        """;
    }

    private static string CreateSecurityWorkflow(IEnumerable<RepositorySecurityStep> steps)
    {
        var body = string.Join(Environment.NewLine, steps.DistinctBy(item => item.Id).Select(item =>
            $"| {item.Id} | {item.Command} | {item.WorkingDirectory} | {item.SuiteId} | {item.DependsOn} | no | {item.TimeoutSeconds} |"));
        return $$"""
        ---
        title: "Security Verification Workflow"
        type: workflow-definition
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-27"
        review_cadence: "on scanner, target, policy, or evidence change"
        ---

        # Security verification workflow

        This focused workflow executes applicable governed scanners without rerunning unrelated delivery suites.

        | Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
        |---|---|---|---|---|---|---:|
        {{body}}
        """;
    }

    private static string CreateProfile(IEnumerable<Suite> suites)
    {
        var body = string.Join(Environment.NewLine, suites.Select(item =>
            $"| {item.Id} | {item.Component} | {item.Layer} | {item.Framework} | {item.Command} | {item.WorkingDirectory} | {item.ResultFormat} | {item.ResultPath} | {item.CoveragePath} | {item.MutationPath} | {item.Prerequisites} | {item.AppliesWhen} | {item.CiTier} | {item.Artifacts} |"));
        return $$"""
        ---
        title: "Test Suite Profile"
        type: test-suite-profile
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-26"
        review_cadence: "on test framework, command, classification, or CI-tier change"
        ---

        # Test suite profile

        Result, coverage, mutation, and retained artifact paths are derived state under `.cis/local/`.
        Suite-specific sanitized runtime evidence belongs under `.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/` and is hash-correlated during `cis test reconcile`.

        | Suite ID | Component | Layer | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path | Prerequisites | Applies when | CI tier | Artifacts |
        |---|---|---|---|---|---|---|---|---|---|---|---|---|---|
        {{body}}
        """;
    }

    private static HashSet<string> PackageNames(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in new[] { "dependencies", "devDependencies" })
            if (root.TryGetProperty(group, out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
                foreach (var item in dependencies.EnumerateObject()) names.Add(item.Name);
        return names;
    }

    private static string PackageRunner(string repository, string root)
        => File.Exists(Path.Combine(root, "pnpm-lock.yaml")) || File.Exists(Path.Combine(repository, "pnpm-lock.yaml")) ? "corepack pnpm"
            : File.Exists(Path.Combine(root, "yarn.lock")) || File.Exists(Path.Combine(repository, "yarn.lock")) ? "corepack yarn"
            : "npm";

    private static string? FirstScript(IReadOnlyDictionary<string, string> scripts, params string[] names)
        => names.FirstOrDefault(scripts.ContainsKey);
    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
    private static string SafeId(string value) => new string(value.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private sealed record Suite(string Id, string Component, string Layer, string Framework, string Command,
        string WorkingDirectory, string ResultFormat, string ResultPath, string CoveragePath, string MutationPath,
        string Prerequisites, string AppliesWhen, string CiTier, string Artifacts);
    private sealed record Step(string Id, string Command, string WorkingDirectory, string Suites, string DependsOn,
        string ContinueOnFailure, int Timeout);
}
