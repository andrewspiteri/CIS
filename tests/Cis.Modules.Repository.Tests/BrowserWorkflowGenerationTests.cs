using Cis.Abstractions;
using Cis.Modules.Workflow;

namespace Cis.Modules.Repository.Tests;

public sealed class BrowserWorkflowGenerationTests
{
    [Theory]
    [InlineData(".", "", "npm", true)]
    [InlineData("apps/portal with spaces", "", "npm", true)]
    [InlineData("apps/portal", "pnpm-lock.yaml", "corepack", false)]
    [InlineData("apps/portal", "yarn.lock", "corepack", false)]
    public void GeneratedBrowserWorkflowBindsReporterToTheDeclaredRunAndAttemptPath(
        string componentRoot, string lockFile, string executable, bool npmSeparator)
    {
        var repository = Path.Combine(Path.GetTempPath(), "cis-browser-generation", Guid.NewGuid().ToString("N"));
        try
        {
            void Write(string relative, string content)
            {
                var path = Path.Combine(repository, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
            Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: browser-fixture\ndocumentation_root: docs/cis\n");
            Write("docs/cis/catalog.yml", "schema_version: 1\nrepository: browser-fixture\ndocuments: []\n");
            Write(Path.Combine(componentRoot, "package.json"), """
                {"name":"portal","scripts":{"test:browser":"playwright test"},"dependencies":{"next":"16.0.0","react":"19.0.0"},"devDependencies":{"@playwright/test":"1.55.0"}}
                """);
            Write(Path.Combine(componentRoot, "browser.spec.js"), "import { test } from '@playwright/test';\n");
            if (lockFile.Length > 0) Write(lockFile, "");
            var classification = new RepositoryClassifier().Classify(repository);
            var generated = RepositoryTestingStarter.Create(repository, "docs/cis", classification);
            Write("docs/cis/workflows/standard-delivery.md", generated.Workflow);

            var described = new WorkflowService(new CisRepositoryContextResolver()).Describe(repository, "standard-delivery");

            Assert.Equal(0, described.ExitCode);
            var browser = Assert.Single(described.Workflow!.Steps,
                step => step.Suites.Any(suite => suite.EndsWith("-browser", StringComparison.Ordinal)));
            Assert.Equal(executable, browser.Executable);
            Assert.Contains("--reporter=line,json", browser.Arguments);
            Assert.Equal(npmSeparator, browser.Arguments.Contains("--", StringComparer.Ordinal));
            var output = browser.EnvironmentVariables!["PLAYWRIGHT_JSON_OUTPUT_FILE"];
            var declared = $".cis/local/testing/results/{browser.Suites[0]}/{{run-id}}/attempt-{{attempt}}/playwright.json";
            Assert.Contains($"| playwright-json | {declared} |", generated.Profile, StringComparison.Ordinal);
            Assert.Equal(Path.GetFullPath(Path.Combine(repository, declared)),
                Path.GetFullPath(Path.Combine(repository, browser.WorkingDirectory, output)));
            var artifactOutput = Assert.Single(browser.Arguments, argument => argument.StartsWith("--output=", StringComparison.Ordinal))["--output=".Length..];
            Assert.Equal(Path.GetFullPath(Path.Combine(repository, Path.GetDirectoryName(declared)!, "artifacts")),
                Path.GetFullPath(Path.Combine(repository, browser.WorkingDirectory, artifactOutput)));
            Assert.Contains(browser.DependsOn, id => id.EndsWith("-browser-prerequisite", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repository)) Directory.Delete(repository, true);
        }
    }
}
