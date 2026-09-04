namespace Cis.Modules.Repository.Tests;

public sealed class RepositoryInitializerTests
{
    [Fact]
    public void RepositoryCommand_RequiresDocumentationRoot()
    {
        using var application = new Cis.Host.CisHostBuilder()
            .AddModule(new RepositoryModule())
            .Build();

        var exitCode = application.Invoke(["repo", "init"]);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void RepositoryDoctorCommand_IsRegistered()
    {
        using var application = new Cis.Host.CisHostBuilder()
            .AddModule(new RepositoryModule())
            .Build();

        var exitCode = application.Invoke(["repo", "doctor", "--help"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void RepositoryImportAndListCommands_AreRegistered()
    {
        using var application = new Cis.Host.CisHostBuilder()
            .AddModule(new RepositoryModule())
            .Build();

        Assert.Equal(0, application.Invoke(["repo", "import", "--help"]));
        Assert.Equal(0, application.Invoke(["repo", "list", "--help"]));
    }

    [Fact]
    public void WorkspaceInitCommand_IsRegistered()
    {
        using var application = new Cis.Host.CisHostBuilder()
            .AddModule(new WorkspaceModule())
            .Build();

        Assert.Equal(0, application.Invoke(["workspace", "init", "--help"]));
    }

    [Fact]
    public void WorkspaceInit_CreatesAuthorityAndIsIdempotent()
    {
        using var workspace = TemporaryRepository.Create();
        var resolver = new CisRepositoryContextResolver();
        var registry = new WorkspaceRegistry(resolver);
        var initializer = new WorkspaceInitializer(new RepositoryInitializer(), registry);

        var confirmation = initializer.Initialize(new WorkspaceInitRequest(
            workspace.Path,
            "docs",
            DryRun: false,
            Confirmed: false));
        Assert.Equal(3, confirmation.ExitCode);
        Assert.False(File.Exists(Path.Combine(workspace.Path, ".cis", "workspace.yml")));

        var initialized = initializer.Initialize(new WorkspaceInitRequest(
            workspace.Path,
            "docs",
            DryRun: false,
            Confirmed: true));
        Assert.Equal(0, initialized.ExitCode);
        Assert.Equal("initialized", initialized.Status);
        var resolved = registry.Resolve(workspace.Path);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(workspace.Path, resolved.Workspace!.AuthorityRepository!.RepositoryPath);
        Assert.Equal("authority", resolved.Workspace.AuthorityRepository.Role);

        var repeated = initializer.Initialize(new WorkspaceInitRequest(
            workspace.Path,
            "docs",
            DryRun: false,
            Confirmed: true));
        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
    }

    [Fact]
    public void WorkspaceInit_AgentOutputIncludesRepositoryClassificationAndPlannedChanges()
    {
        using var workspace = TemporaryRepository.Create();
        workspace.Write("package.json", """
        {
          "dependencies": {
            "express": "5.1.0"
          }
        }
        """);
        workspace.Write("src/server.ts", "import express from 'express';\nexpress().get('/health', (_, response) => response.send('ok'));\n");
        var registry = new WorkspaceRegistry(new CisRepositoryContextResolver());
        var initializer = new WorkspaceInitializer(new RepositoryInitializer(), registry);

        var result = initializer.Initialize(new WorkspaceInitRequest(
            workspace.Path,
            "docs",
            DryRun: true,
            Confirmed: false));
        var repository = Assert.IsType<RepositoryInitResult>(result.RepositoryInitialization);
        var classification = Assert.IsType<RepositoryClassification>(repository.Classification);

        var lines = WorkspaceModule.RenderAgentLines(result);

        Assert.Contains(lines, line => line ==
            $"classification={classification.Shape};components={classification.Components.Count}");
        Assert.Contains(lines, line => line.StartsWith("component=", StringComparison.Ordinal)
            && line.Contains("roles=backend", StringComparison.Ordinal));
        Assert.Contains(lines, line => line ==
            $"repositoryInitialization={repository.Status};applied=false;confirmationRequired=false;" +
            $"createDirectories={repository.DirectoriesToCreate.Count};createFiles={repository.FilesToCreate.Count};" +
            $"updateFiles={repository.FilesToUpdate.Count};quarantined={repository.QuarantinedPaths.Count};" +
            $"retained={repository.RetainedPaths.Count};warnings={repository.Warnings.Count};" +
            $"collisions={repository.Collisions.Count};errors={repository.Errors.Count}");
        Assert.Contains(lines, line => line.StartsWith("createFile=", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("starter=", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkspaceInit_PreservesPreviouslyImportedParticipants()
    {
        using var workspace = TemporaryRepository.Create();
        using var participant = TemporaryRepository.Create();
        var resolver = new CisRepositoryContextResolver();
        var registry = new WorkspaceRegistry(resolver);
        var importer = new RepositoryImporter(new RepositoryInitializer(), registry);
        Assert.Equal(0, importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [participant.Path],
            DryRun: false,
            Confirmed: true)).ExitCode);
        var initializer = new WorkspaceInitializer(new RepositoryInitializer(), registry);

        var result = initializer.Initialize(new WorkspaceInitRequest(
            workspace.Path,
            "docs",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        var resolved = registry.Resolve(workspace.Path).Workspace!;
        Assert.Equal(2, resolved.Repositories.Count);
        Assert.Single(resolved.Repositories, repository => repository.Role == "authority");
        Assert.Single(resolved.Repositories, repository => repository.Role == "participant");
    }

    [Fact]
    public void Import_DryRunPlansSeveralRepositoriesWithoutWriting()
    {
        using var workspace = TemporaryRepository.Create();
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        var importer = CreateImporter();

        var result = importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [first.Path, second.Path],
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("dry-run", result.Status);
        Assert.Equal(2, result.Repositories.Count);
        Assert.False(Directory.Exists(Path.Combine(first.Path, ".cis")));
        Assert.False(Directory.Exists(Path.Combine(second.Path, ".cis")));
        Assert.False(File.Exists(Path.Combine(workspace.Path, ".cis", "workspace.yml")));
    }

    [Fact]
    public void Import_RequiresConfirmationBeforeChangingSeveralRepositories()
    {
        using var workspace = TemporaryRepository.Create();
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        var importer = CreateImporter();

        var result = importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [first.Path, second.Path],
            DryRun: false,
            Confirmed: false));

        Assert.Equal(3, result.ExitCode);
        Assert.True(result.ConfirmationRequired);
        Assert.False(result.Applied);
        Assert.False(Directory.Exists(Path.Combine(first.Path, ".cis")));
        Assert.False(File.Exists(Path.Combine(workspace.Path, ".cis", "workspace.yml")));
    }

    [Fact]
    public void Import_InitializesRegistersAndIdempotentlyResolvesSeveralRepositories()
    {
        using var workspace = TemporaryRepository.Create();
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        var importer = CreateImporter();

        var imported = importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [first.Path, second.Path],
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, imported.ExitCode);
        Assert.Equal("imported", imported.Status);
        Assert.True(imported.Applied);
        Assert.True(File.Exists(Path.Combine(first.Path, ".cis", "repository.yml")));
        Assert.True(File.Exists(Path.Combine(second.Path, ".cis", "repository.yml")));
        var configurationPath = Path.Combine(workspace.Path, ".cis", "workspace.yml");
        Assert.True(File.Exists(configurationPath));
        var configuration = File.ReadAllText(configurationPath);
        Assert.Contains("schema_version: 1", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Path.Replace('\\', '/'), configuration, StringComparison.OrdinalIgnoreCase);

        var resolver = new WorkspaceRegistry(new CisRepositoryContextResolver())
            .Resolve(workspace.Path);
        Assert.True(resolver.IsSuccess);
        Assert.Equal(2, resolver.Workspace!.Repositories.Count);

        var repeated = importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [first.Path, second.Path],
            DryRun: false,
            Confirmed: true));
        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
    }

    [Fact]
    public void Import_ExistingStandaloneRepositoryBootstrapsItAsWorkspaceAuthority()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Existing.Api/Existing.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Existing.Api/Program.cs", "var app = WebApplication.CreateBuilder(args).Build(); app.MapGet(\"/health\", () => \"ok\");");
        var importer = CreateImporter();

        var imported = importer.Import(new RepositoryImportRequest(
            repository.Path,
            "docs/cis",
            [repository.Path],
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, imported.ExitCode);
        Assert.Equal("imported", imported.Status);
        Assert.True(imported.Applied);
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis", "repository.yml")));
        var configuration = File.ReadAllText(Path.Combine(repository.Path, ".cis", "workspace.yml"));
        Assert.Contains("role: authority", configuration, StringComparison.Ordinal);

        var resolved = new WorkspaceRegistry(new CisRepositoryContextResolver()).Resolve(repository.Path);
        Assert.True(resolved.IsSuccess);
        var authority = Assert.Single(resolved.Workspace!.Repositories);
        Assert.Equal("authority", authority.Role);
        Assert.Equal(repository.Path, authority.RepositoryPath);
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "permissions-dictionary.md")));
        var agentSkill = File.ReadAllText(Path.Combine(repository.Path, ".github", "skills", "cis-agent-execution", "SKILL.md"));
        Assert.StartsWith("---\nname: cis-agent-execution\ndescription:", agentSkill.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);

        var repeated = importer.Import(new RepositoryImportRequest(
            repository.Path,
            "docs/cis",
            [repository.Path],
            DryRun: false,
            Confirmed: true));
        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
    }

    [Fact]
    public void Import_InvalidSourcePreventsMutationOfEveryRepository()
    {
        using var workspace = TemporaryRepository.Create();
        using var valid = TemporaryRepository.Create();
        var missing = Path.Combine(workspace.Path, "missing");
        var importer = CreateImporter();

        var result = importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [valid.Path, missing],
            DryRun: false,
            Confirmed: true));

        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(valid.Path, ".cis")));
        Assert.False(File.Exists(Path.Combine(workspace.Path, ".cis", "workspace.yml")));
    }

    [Fact]
    public void Doctor_WarnsWithoutFailingWhenOllamaIsUnavailable()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));
        var doctor = CreateDoctor(
            initializer,
            new OllamaProbeResult("unavailable", "http://127.0.0.1:11434", [], "connection refused"));

        var result = doctor.Inspect(repository.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("warnings", result.Status);
        Assert.Contains(result.Findings, finding => finding.Code == "CIS-OLLAMA-001");
        Assert.DoesNotContain(result.Findings, finding => finding.Severity == "error");
    }

    [Fact]
    public void Doctor_IsHealthyWhenRepositoryAndOllamaAreReady()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));
        var doctor = CreateDoctor(
            initializer,
            new OllamaProbeResult("available", "http://127.0.0.1:11434", ["qwen3:8b"], null));

        var result = doctor.Inspect(repository.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("healthy", result.Status);
        Assert.Equal("CIS-OLLAMA-003", Assert.Single(result.Findings).Code);
        Assert.Equal(1, result.InformationCount);
    }

    [Fact]
    public void Doctor_CountsBothInformationSeveritySpellings()
    {
        var findings = new[]
        {
            new Cis.Abstractions.CisRepositoryDoctorFinding("INFO-001", "info", "test", "First", [], "None", null, "none"),
            new Cis.Abstractions.CisRepositoryDoctorFinding("INFO-002", "information", "test", "Second", [], "None", null, "none"),
        };

        var result = new RepositoryDoctorResult(
            "healthy",
            "C:/repo",
            "docs",
            new OllamaProbeResult("available", "http://127.0.0.1:11434", [], null),
            findings,
            RepositoryConfigurationValid: true);

        Assert.Equal(2, result.InformationCount);
    }

    [Fact]
    public void JavaScriptToolingDoctor_WarnsWhenEslintNineLintScriptHasNoFlatConfig()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("package.json", """
            {
              "scripts": { "lint": "eslint ." },
              "devDependencies": { "eslint": "^9.39.0" }
            }
            """);
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        var check = new JavaScriptToolingDoctorCheck();

        var finding = Assert.Single(check.Inspect(context));

        Assert.Equal("CIS-JS-DOCTOR-001", finding.Code);
        Assert.Equal("warning", finding.Severity);

        repository.Write("eslint.config.js", "export default [];\n");
        Assert.Empty(check.Inspect(context));
    }

    [Fact]
    public void Doctor_ReportsUnappliedInitializationDelta()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));
        File.Delete(Path.Combine(repository.Path, ".github", "skills", "cis-verification", "SKILL.md"));
        var doctor = CreateDoctor(
            initializer,
            new OllamaProbeResult("available", "http://127.0.0.1:11434", ["qwen3:8b"], null));

        var result = doctor.Inspect(repository.Path);

        Assert.Equal(0, result.ExitCode);
        var finding = Assert.Single(result.Findings, finding => finding.Code == "CIS-REPO-005");
        Assert.Contains(
            finding.Evidence,
            evidence => evidence.Contains("cis-verification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Doctor_AdoptsValidLegacySourceEvidenceRegistryWithoutCollision()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        Assert.Equal(0, initializer.Initialize(new RepositoryInitRequest(
            repository.Path, "docs/cis", DryRun: false, Confirmed: true)).ExitCode);

        var manifestPath = Path.Combine(repository.Path, ".cis", "starter-manifest.yml");
        var store = new StarterManifestStore();
        var manifest = Assert.IsType<StarterManifest>(store.Read(manifestPath).Manifest);
        File.WriteAllText(manifestPath, store.Write(manifest with
        {
            ManagedArtifacts = manifest.ManagedArtifacts
                .Where(item => item.Definition != "reference.source-evidence").ToArray(),
        }));
        File.AppendAllText(Path.Combine(repository.Path, "docs", "cis", "references", "source-evidence.md"),
            "\n| BRD-SRC-abcdef123456 | docs/ref/source.md | md | sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef | Reference | Andrew | 2026-08-30T10:00:00Z | Selected evidence. |\n");
        var doctor = CreateDoctor(initializer,
            new OllamaProbeResult("available", "http://127.0.0.1:11434", ["qwen3:8b"], null));

        var before = doctor.Inspect(repository.Path);

        Assert.True(before.ExitCode == 0, string.Join("\n", before.Findings.Select(item =>
            $"{item.Code}/{item.Severity}: {item.Message}")));
        Assert.DoesNotContain(before.Findings, finding => finding.Code == "CIS-REPO-003");
        Assert.Contains(before.Findings, finding => finding.Code == "CIS-REPO-005"
            && finding.Evidence.Any(item => item.Contains("starter-manifest.yml", StringComparison.Ordinal)));

        Assert.Equal(0, initializer.Initialize(new RepositoryInitRequest(
            repository.Path, "docs/cis", DryRun: false, Confirmed: true)).ExitCode);
        var after = doctor.Inspect(repository.Path);
        Assert.DoesNotContain(after.Findings, finding => finding.Code is "CIS-REPO-003" or "CIS-REPO-005");
    }

    [Fact]
    public void Initialize_SeedsVerificationGuidanceThatPreventsMaskedGateFailures()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        var skill = File.ReadAllText(Path.Combine(repository.Path, ".github", "skills", "cis-verification", "SKILL.md"));
        Assert.Contains("independently observed command", skill, StringComparison.Ordinal);
        Assert.Contains("never let a later successful command mask an earlier failure", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_UsesRootHintToDiagnoseFailedPreInitializationState()
    {
        using var repository = TemporaryRepository.Create();
        var doctor = CreateDoctor(
            new RepositoryInitializer(),
            new OllamaProbeResult("available", "http://127.0.0.1:11434", ["qwen3:8b"], null));

        var result = doctor.Inspect(repository.Path, "docs/cis");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("docs/cis", result.DocumentationRoot);
        Assert.Contains(result.Findings, finding => finding.Code == "CIS-REPO-001");
        Assert.Contains(result.Findings, finding => finding.Code == "CIS-REPO-005");
    }

    [Fact]
    public void Initialize_RejectsAbsoluteDocumentationRoot()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            Path.GetFullPath(Path.Combine(repository.Path, "external")),
            DryRun: false,
            Confirmed: false));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("relative", StringComparison.OrdinalIgnoreCase));
    }

    private static RepositoryDoctor CreateDoctor(
        RepositoryInitializer initializer,
        OllamaProbeResult ollamaResult)
        => new(
            new CisRepositoryContextResolver(),
            initializer,
            [],
            new FixedOllamaProbe(ollamaResult));

    private static RepositoryImporter CreateImporter()
    {
        var resolver = new CisRepositoryContextResolver();
        return new RepositoryImporter(
            new RepositoryInitializer(),
            new WorkspaceRegistry(resolver));
    }

    [Fact]
    public void RepositoryContextResolver_ResolvesGeneratedConfiguration()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));

        var resolution = new CisRepositoryContextResolver().Resolve(repository.Path);

        Assert.True(resolution.IsSuccess);
        Assert.Equal("docs/cis", resolution.Context!.DocumentationRoot);
        Assert.Equal(
            Path.Combine(repository.Path, "docs", "cis", "catalog.yml"),
            resolution.Context.CatalogPath);
    }

    [Fact]
    public void RepositoryContextResolver_RejectsConfiguredRootEscape()
    {
        using var repository = TemporaryRepository.Create();
        Directory.CreateDirectory(Path.Combine(repository.Path, ".cis"));
        File.WriteAllText(
            Path.Combine(repository.Path, ".cis", "repository.yml"),
            "schema_version: 1\nrepository:\n  id: example\ndocumentation_root: ../outside\n");

        var resolution = new CisRepositoryContextResolver().Resolve(repository.Path);

        Assert.False(resolution.IsSuccess);
        Assert.Contains(resolution.Errors, error => error.Contains("inside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Initialize_RejectsDocumentationRootThatEscapesRepository()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            Path.Combine("..", "external"),
            DryRun: false,
            Confirmed: false));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("escape", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Initialize_DryRunPlansWithoutWriting()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("dry-run", result.Status);
        Assert.False(result.Applied);
        Assert.Contains("docs/cis/catalog.yml", result.FilesToCreate);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, "docs")));
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis")));
    }

    [Fact]
    public void Initialize_CreatesMinimumRepositoryStructure()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "README.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "catalog.yml")));
        Assert.True(Directory.Exists(Path.Combine(repository.Path, "docs", "cis", "architecture", "decisions")));
        Assert.True(Directory.Exists(Path.Combine(repository.Path, "docs", "cis", "specs")));
        Assert.True(Directory.Exists(Path.Combine(repository.Path, "docs", "cis", "standards")));
        Assert.True(Directory.Exists(Path.Combine(repository.Path, "docs", "cis", "references")));
        Assert.True(Directory.Exists(Path.Combine(repository.Path, "docs", "cis", "changes")));

        var configuration = File.ReadAllText(Path.Combine(repository.Path, ".cis", "repository.yml"));
        Assert.Contains("documentation_root: docs/cis", configuration, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_IsIdempotentForGeneratedState()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        var request = new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false);

        var firstResult = initializer.Initialize(request);
        var secondResult = initializer.Initialize(request);

        Assert.True(firstResult.Applied);
        Assert.Equal(0, secondResult.ExitCode);
        Assert.Equal("unchanged", secondResult.Status);
        Assert.False(secondResult.Applied);
        Assert.Empty(secondResult.DirectoriesToCreate);
        Assert.Empty(secondResult.FilesToCreate);
    }

    [Fact]
    public void Initialize_RequiresConfirmationForExistingNonEmptyRoot()
    {
        using var repository = TemporaryRepository.Create();
        var documentationRoot = Path.Combine(repository.Path, "docs");
        Directory.CreateDirectory(documentationRoot);
        File.WriteAllText(Path.Combine(documentationRoot, "existing.md"), "Existing content");
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs",
            DryRun: false,
            Confirmed: false));

        Assert.Equal(3, result.ExitCode);
        Assert.True(result.ConfirmationRequired);
        Assert.False(result.Applied);
        Assert.False(File.Exists(Path.Combine(repository.Path, ".cis", "repository.yml")));
    }

    [Fact]
    public void Initialize_ConfirmedExistingRootRetainsContentAndAddsMissingStructure()
    {
        using var repository = TemporaryRepository.Create();
        var documentationRoot = Path.Combine(repository.Path, "docs");
        Directory.CreateDirectory(documentationRoot);
        var existingPath = Path.Combine(documentationRoot, "README.md");
        File.WriteAllText(existingPath, "Maintainer documentation");
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Equal("Maintainer documentation", File.ReadAllText(existingPath));
        Assert.Contains("docs/README.md", result.RetainedPaths);
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "catalog.yml")));
    }

    [Fact]
    public void Initialize_ReportsRequiredPathCollision()
    {
        using var repository = TemporaryRepository.Create();
        var documentationRoot = Path.Combine(repository.Path, "docs");
        Directory.CreateDirectory(documentationRoot);
        File.WriteAllText(Path.Combine(documentationRoot, "specs"), "not a directory");
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Collisions, collision => collision.Contains("docs/specs", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(repository.Path, ".cis", "repository.yml")));
    }

    [Fact]
    public void Initialize_ReportsDocumentationRootAncestorCollision()
    {
        using var repository = TemporaryRepository.Create();
        File.WriteAllText(Path.Combine(repository.Path, "docs"), "not a directory");
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Collisions, collision => collision.Contains("docs", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis")));
    }

    [Fact]
    public void Classifier_DetectsAspNetApiRolesAndCapabilities()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "src/Example.Api/Program.cs",
            "var builder = WebApplication.CreateBuilder(args); builder.Services.AddControllers(); builder.Services.AddAuthorization(); builder.Services.AddOpenApi(); app.MapControllers();");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Equal("application", classification.Shape);
        Assert.Contains("csharp", component.Languages);
        Assert.Contains("aspnet-core", component.Frameworks);
        Assert.Contains("backend-api-producer", component.Roles);
        Assert.Contains("openapi", component.Capabilities);
        Assert.Contains("authorization", component.Capabilities);
    }

    [Fact]
    public void Classifier_DetectsBlazorWebAssemblyAsFrontendConsumer()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Portal/Portal.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"><ItemGroup>" +
            "<PackageReference Include=\"Microsoft.AspNetCore.Components.WebAssembly\" Version=\"10.0.0\" />" +
            "</ItemGroup></Project>");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Contains("blazor-webassembly", component.Frameworks);
        Assert.Contains("frontend-consumer", component.Roles);
        Assert.Contains("web-ui", component.Capabilities);
        Assert.DoesNotContain("shared-library", component.Roles);
    }

    [Fact]
    public void Classifier_DoesNotTreatBlazorWebAssemblyServerHostAsBrowserClient()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Portal.Server/Portal.Server.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup>" +
            "<PackageReference Include=\"Microsoft.AspNetCore.Components.WebAssembly.Server\" Version=\"10.0.0\" />" +
            "</ItemGroup></Project>");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Contains("aspnet-core", component.Frameworks);
        Assert.Contains("backend-web", component.Roles);
        Assert.DoesNotContain("frontend-consumer", component.Roles);
    }

    [Fact]
    public void Classifier_ExcludesLikelyBackupProjectBesideCanonicalProject()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Settings/Settings.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        repository.Write("src/Settings/Settings - Backup.Application.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Equal("settings-api", component.Id);
        Assert.DoesNotContain("Backup", string.Join(' ', component.Evidence), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classifier_DetectsAngularFrontendAndApiConsumption()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "angular.json",
            "{\"projects\":{\"portal\":{\"root\":\"projects/portal\",\"sourceRoot\":\"projects/portal/src\"}}}");
        repository.Write(
            "projects/portal/src/app/app.ts",
            "import { HttpClient } from '@angular/common/http'; const routes: Routes = []; class AuthGuard implements CanActivate {}");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Contains("typescript", component.Languages);
        Assert.Contains("angular", component.Frameworks);
        Assert.Contains("frontend-consumer", component.Roles);
        Assert.Contains("backend-api-consumer", component.Roles);
        Assert.Contains("authorization", component.Capabilities);
        Assert.Contains("routes", component.Capabilities);
    }

    [Fact]
    public void Classifier_PreservesComponentsWithDuplicateProjectNames()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "services/one/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "services/two/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        Assert.Equal(2, classification.Components.Count);
        Assert.Equal(2, classification.Components.Select(component => component.Id).Distinct().Count());
    }

    [Fact]
    public void Classifier_ExcludesLegacyAndGeneratedProjectsAndOwnsLooseToolingSources()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/App/App.cs", "namespace App; public sealed class Service { }");
        repository.Write(
            "_old/Legacy.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "game/android/build/build.gradle.kts",
            "plugins { id(\"com.android.application\"); id(\"org.jetbrains.kotlin.android\") }");
        repository.Write("docs/art/scripts/render.py", "def render():\n    return True\n");
        repository.Write(
            "games/Demo/tests/Demo.Tests/Demo.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        repository.Write("games/Demo/tests/runtime/coverage_driver.gd", "extends Node\n");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        Assert.DoesNotContain(classification.Components, component => component.Root.StartsWith("_old", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(classification.Components, component => component.Root.Contains("/build", StringComparison.OrdinalIgnoreCase));
        var tooling = Assert.Single(classification.Components, component => component.Root == "docs/art");
        Assert.Contains("python", tooling.Languages);
        Assert.Contains("tooling", tooling.Roles);
        var looseTests = Assert.Single(classification.Components, component => component.Root == "games/Demo/tests/runtime");
        Assert.Contains("test-automation", looseTests.Roles);
        Assert.Contains(classification.Components, component => component.Root == "games/Demo/tests/Demo.Tests");
        Assert.DoesNotContain(classification.Components, component => component.Root == "games/Demo/tests");
    }

    [Fact]
    public void Classifier_DetectsNextJsFrontendWithoutAngular()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "apps/portal/package.json",
            "{\"name\":\"customer-portal\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\",\"next-auth\":\"5.0.0\"}}");
        repository.Write("apps/portal/tsconfig.json", "{\"compilerOptions\":{}}");
        repository.Write(
            "apps/portal/app/page.tsx",
            "export default async function Page() { const response = await fetch('/api/account'); return <div />; }");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Contains("typescript", component.Languages);
        Assert.Contains("nextjs", component.Frameworks);
        Assert.Contains("frontend-consumer", component.Roles);
        Assert.Contains("backend-api-consumer", component.Roles);
        Assert.Contains("routes", component.Capabilities);
    }

    [Fact]
    public void Classifier_DoesNotInferProductionPersistenceFromFrontendArchitectureAssertions()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"customer-portal\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\"}}");
        repository.Write("tsconfig.json", "{\"compilerOptions\":{}}");
        repository.Write("src/app/page.tsx", "export default function Page() { return <main />; }");
        repository.Write("test/architecture/frontend-boundaries.test.ts", "expect(imports).not.toContain('node:sqlite');");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.DoesNotContain("database", component.Roles);
        Assert.DoesNotContain("persistence", component.Capabilities);
    }

    [Fact]
    public void ClassifierAndReferences_IgnoreGeneratedNextStandaloneOutput()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"customer-portal\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\"}}");
        repository.Write("tsconfig.json", "{\"compilerOptions\":{}}");
        repository.Write("src/app/page.tsx", "export default function Page() { return <main />; }");
        repository.Write(
            ".next/standalone/package.json",
            "{\"name\":\"generated-output\",\"dependencies\":{\"next\":\"16.0.0\",\"@mui/material\":\"7.0.0\"}}");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Equal(".", component.Root);
        Assert.DoesNotContain("material-ui", component.Frameworks);

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        Assert.True(result.Applied);
        var packages = File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "references", "package-catalogue.md"));
        Assert.DoesNotContain("generated-output", packages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@mui/material", packages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classifier_IgnoresStrykerMutationSandboxes()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("package.json", "{\"name\":\"api\",\"dependencies\":{\"express\":\"5.2.1\"}}");
        repository.Write("src/index.ts", "import express from 'express'; export const app = express();");
        repository.Write(".stryker-tmp/sandbox/package.json", "{\"name\":\"mutation-copy\",\"dependencies\":{\"next\":\"16.0.0\"}}");
        repository.Write(".stryker-tmp/sandbox/src/page.tsx", "export default function Page() { return <main />; }");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Equal("api", component.Id);
        Assert.Contains("express", component.Frameworks);
    }

    [Fact]
    public void Classifier_DetectsExpressApiAuthenticationAndPersistenceFromPackageEvidence()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"friends-todo-api\",\"dependencies\":{\"express\":\"5.2.1\",\"supertokens-node\":\"24.0.3\",\"pg\":\"8.16.3\"},\"devDependencies\":{\"typescript\":\"5.9.3\"}}");
        repository.Write("tsconfig.json", "{\"compilerOptions\":{\"strict\":true}}");
        repository.Write("src/index.ts", "import express from 'express'; const app = express(); app.get('/health', (_, res) => res.json({ ok: true }));");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Equal(".", component.Root);
        Assert.Contains("typescript", component.Languages);
        Assert.Contains("express", component.Frameworks);
        Assert.Contains("backend-api-producer", component.Roles);
        Assert.Contains("database", component.Roles);
        Assert.DoesNotContain("shared-library", component.Roles);
        Assert.Contains("authentication", component.Capabilities);
        Assert.Contains("authorization", component.Capabilities);
        Assert.Contains("persistence", component.Capabilities);
        Assert.Contains("routes", component.Capabilities);
    }

    [Fact]
    public void Classifier_DetectsNodeBuiltInSqlitePersistenceAndMigrations()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"friends-todo-api\",\"dependencies\":{\"express\":\"5.2.1\"},\"devDependencies\":{\"typescript\":\"5.9.3\"}}");
        repository.Write("tsconfig.json", "{\"compilerOptions\":{\"strict\":true}}");
        repository.Write(
            "src/database.ts",
            "import { DatabaseSync } from 'node:sqlite'; const db = new DatabaseSync('app.db'); db.exec('CREATE TABLE schema_migrations(id TEXT)');");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Contains("database", component.Roles);
        Assert.Contains("persistence", component.Capabilities);
        Assert.Contains("migrations", component.Capabilities);
        Assert.Contains(component.Evidence, item => item.Contains("built-in SQLite", StringComparison.Ordinal));
    }

    [Fact]
    public void Classifier_DetectsSeraTailwindAndMotionEvidence()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "apps/portal/package.json",
            "{\"name\":\"portal\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\",\"tailwindcss\":\"4.0.0\",\"framer-motion\":\"12.0.0\"}}");
        repository.Write(
            "apps/portal/components.json",
            "{\"$schema\":\"https://ui.shadcn.com/schema.json\",\"aliases\":{\"components\":\"@/components\"}}");
        repository.Write(
            "apps/portal/src/install.ts",
            "export const registry = 'https://seraui.com/registry/button.json';");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Contains("sera-ui", component.Frameworks);
        Assert.Contains("shadcn-ui", component.Frameworks);
        Assert.Contains("tailwindcss", component.Frameworks);
        Assert.Contains("motion", component.Frameworks);
        var resolution = Assert.Single(UiFrameworkResolver.Resolve(
            new RepositoryClassification("application", [component], [])));
        Assert.Equal("Existing", resolution.Resolution);
        Assert.Equal("Sera UI", resolution.UiFramework);
    }

    [Fact]
    public void Initialize_DefaultsUnconfiguredNextJsToShadcnWithoutInstallingPackages()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "apps/portal/package.json",
            "{\"name\":\"portal\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\"}}");
        repository.Write("apps/portal/app/page.tsx", "export default function Page() { return <main />; }");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.True(result.Applied);
        var profile = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "ui-framework-profile.md"));
        Assert.Contains("| portal | Next.js | Default | shadcn/ui |", profile, StringComparison.Ordinal);
        Assert.Contains("Initialization records policy and guidance but does not install dependencies", profile, StringComparison.Ordinal);
        var instruction = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "portal-ui-framework.instructions.md"));
        Assert.Contains("Existing framework takes precedence", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tailwindcss", File.ReadAllText(Path.Combine(repository.Path, "apps", "portal", "package.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void UiFrameworkResolver_PreservesDetectedMaterialUi()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"portal\",\"dependencies\":{\"react\":\"19.0.0\",\"@mui/material\":\"7.0.0\"}}");
        repository.Write("src/App.tsx", "export const App = () => <main />;");

        var classification = new RepositoryClassifier().Classify(repository.Path);
        var resolution = Assert.Single(UiFrameworkResolver.Resolve(classification));

        Assert.Equal("Existing", resolution.Resolution);
        Assert.Equal("Material UI", resolution.UiFramework);
        Assert.DoesNotContain("shadcn", resolution.UiFramework, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classifier_DetectsSwiftNativeClient()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "apps/ios/Package.swift",
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Mobile\", platforms: [.iOS(.v17)])");
        repository.Write(
            "apps/ios/Sources/Mobile/App.swift",
            "import SwiftUI\nstruct AppView: View { var body: some View { NavigationStack { Text(\"Hi\") } } }\nlet task = URLSession.shared.dataTask(with: URL(string: \"https://example.test\")!)");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Contains("swift", component.Languages);
        Assert.Contains("swiftui", component.Frameworks);
        Assert.Contains("mobile-client", component.Roles);
        Assert.Contains("backend-api-consumer", component.Roles);
        Assert.Contains("ios", component.Capabilities);
        Assert.Contains("navigation", component.Capabilities);
    }

    [Fact]
    public void Classifier_DetectsKotlinAndroidComposeClient()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "apps/android/build.gradle.kts",
            "plugins { id(\"com.android.application\"); id(\"org.jetbrains.kotlin.android\") }\ndependencies { implementation(\"androidx.compose.ui:ui:1.0\") }");
        repository.Write(
            "apps/android/src/main/kotlin/App.kt",
            "import androidx.compose.runtime.Composable\n@Composable fun App() { val client = OkHttpClient(); NavController() }");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Contains("kotlin", component.Languages);
        Assert.Contains("android", component.Frameworks);
        Assert.Contains("jetpack-compose", component.Frameworks);
        Assert.Contains("mobile-client", component.Roles);
        Assert.Contains("backend-api-consumer", component.Roles);
        Assert.Contains("navigation", component.Capabilities);
    }

    [Fact]
    public void UiFrameworkResolver_PreservesAndroidViewsInsteadOfApplyingComposeDefault()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "apps/android/build.gradle.kts",
            "plugins { id(\"com.android.application\"); id(\"org.jetbrains.kotlin.android\") }");
        repository.Write(
            "apps/android/src/main/kotlin/MainActivity.kt",
            "import androidx.appcompat.app.AppCompatActivity\nclass MainActivity : AppCompatActivity()");
        repository.Write(
            "apps/android/src/main/res/layout/activity_main.xml",
            "<LinearLayout xmlns:android=\"http://schemas.android.com/apk/res/android\" />");

        var classification = new RepositoryClassifier().Classify(repository.Path);
        var component = Assert.Single(classification.Components);
        var resolution = Assert.Single(UiFrameworkResolver.Resolve(classification));

        Assert.Contains("android-views", component.Frameworks);
        Assert.Equal("Existing", resolution.Resolution);
        Assert.Equal("Android Views", resolution.UiFramework);
    }

    [Fact]
    public void Classifier_DetectsGodotProjectAndMergesCoLocatedCSharpProject()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "games/Runner/Runner.csproj",
            "<Project Sdk=\"Godot.NET.Sdk/4.7.0\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "games/Runner/project.godot",
            "config_version=5\n[application]\nconfig/name=\"Runner\"\nrun/main_scene=\"res://Main.tscn\"\n" +
            "config/features=PackedStringArray(\"4.7\", \"C#\", \"Mobile\")\n[autoload]\nNav=\"*res://Nav.cs\"\n" +
            "[dotnet]\nproject/assembly_name=\"Runner\"\n[editor_plugins]\nenabled=PackedStringArray(\"res://addons/tool/plugin.cfg\")\n");
        repository.Write("games/Runner/tools/export_plugin.gd", "@tool\nextends EditorExportPlugin\n");
        repository.Write(
            "games/Runner/android/build/assets/project.godot",
            "config_version=5\n[application]\nconfig/name=\"Generated Export\"\nrun/main_scene=\"res://main.tscn\"\n");

        var classification = new RepositoryClassifier().Classify(repository.Path);

        var component = Assert.Single(classification.Components);
        Assert.Equal("games/Runner", component.Root);
        Assert.Contains("csharp", component.Languages);
        Assert.Contains("gdscript", component.Languages);
        Assert.Contains("godot", component.Frameworks);
        Assert.Contains("mobile-client", component.Roles);
        Assert.DoesNotContain("shared-library", component.Roles);
        Assert.Contains("scenes", component.Capabilities);
        Assert.Contains("autoload", component.Capabilities);
        Assert.Contains("plugins", component.Capabilities);
        Assert.Contains("dotnet", component.Capabilities);
        Assert.Contains(component.Evidence, value => value == "games/Runner/project.godot");
    }

    [Fact]
    public void Initialize_GodotProjectBindsGodotInstructionAndScreenReference()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "game/project.godot",
            "config_version=5\n[application]\nconfig/name=\"Game\"\nrun/main_scene=\"res://Main.tscn\"\n");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.instruction.godot");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "reference.screen-route-map");
        Assert.Contains(".github/instructions/game-godot.instructions.md", result.FilesToCreate);
    }

    [Fact]
    public void Initialize_ClassifiedRepositoryRequiresReviewAndBindsStarters()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "src/Example.Api/Program.cs",
            "builder.Services.AddControllers(); builder.Services.AddAuthorization(); app.MapControllers();");
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));

        Assert.Equal(3, result.ExitCode);
        Assert.True(result.ConfirmationRequired);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "reference.api-dictionary");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "reference.permissions-dictionary");
        Assert.Contains("docs/cis/specs/api-dictionary-spec.md", result.FilesToCreate);
        Assert.Contains(".github/instructions/example-api-csharp.instructions.md", result.FilesToCreate);
        Assert.Contains(".github/instructions/cis-api-governance.instructions.md", result.FilesToCreate);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.instruction.api-governance");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.dotnet-quality");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.core-testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.business-assurance");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.api-integration");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.secure-delivery");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.agent-documentation-compliance");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.secure-feature-implementation");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.api-controller");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.observability");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.edge-security-header");
        Assert.DoesNotContain(result.StarterSelections, selection => selection.Definition == "standard.default.frontend-interaction");
        Assert.Contains("docs/cis/standards/api-controller-standard.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-dotnet-test/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-unit-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-architecture-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-mutation-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-business-acceptance-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-api-integration-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-secure-feature/SKILL.md", result.FilesToCreate);
        Assert.Contains("docs/cis/references/implementation-skill-packs.md", result.FilesToCreate);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis")));
    }

    [Fact]
    public void Initialize_WebFrontendBindsSharedReferencesAndFrameworkInstruction()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "apps/portal/package.json",
            "{\"name\":\"portal\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\"}}");
        repository.Write("apps/portal/tsconfig.json", "{\"compilerOptions\":{}}");
        repository.Write("apps/portal/app/page.tsx", "export async function Page() { return fetch('/api/items'); }");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "reference.api-dictionary");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "reference.screen-route-map");
        Assert.Contains(".github/instructions/portal-nextjs.instructions.md", result.FilesToCreate);
        Assert.Contains("docs/cis/references/screen-route-map.md", result.FilesToCreate);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.frontend-delivery");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.browser-assurance");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.core-testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.business-assurance");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.frontend-interaction");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.accessibility-responsive-regression");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.edge-security-header");
        Assert.DoesNotContain(result.StarterSelections, selection => selection.Definition == "standard.default.api-controller");
        Assert.Contains("docs/cis/standards/frontend-interaction-standard.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-frontend-implementation/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-unit-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-frontend-component-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-business-acceptance-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-browser-regression-tests/SKILL.md", result.FilesToCreate);
    }

    [Fact]
    public void Initialize_PlaywrightWorkflowInstallsChromiumBeforeBrowserSuite()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"web\",\"scripts\":{\"test:browser\":\"playwright test\"},\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\"},\"devDependencies\":{\"@playwright/test\":\"1.55.0\"}}");
        repository.Write("tsconfig.json", "{\"compilerOptions\":{}}");
        repository.Write("app/page.tsx", "export default function Page() { return null; }");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        var workflow = File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "workflows", "standard-delivery.md"));
        Assert.Contains("playwright install chromium", workflow, StringComparison.Ordinal);
        Assert.Contains("web-browser-prerequisite", workflow, StringComparison.Ordinal);
        Assert.Contains("| web-browser |", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_TypeScriptApiSelectsPortableTestingAndRealDependencySkills()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"friends-todo-api\",\"dependencies\":{\"express\":\"5.1.0\",\"supertokens-node\":\"23.0.0\",\"better-sqlite3\":\"12.0.0\"},\"devDependencies\":{\"typescript\":\"5.9.0\"}}");
        repository.Write("tsconfig.json", "{\"compilerOptions\":{}}");
        repository.Write(
            "src/server.ts",
            "import express from 'express'; export const app = express(); app.get('/health/live', (_request, response) => response.sendStatus(200));");
        repository.Write(
            "src/server.test.ts",
            "import express from 'express'; const app = express(); app.post('/test-only', (_request, response) => response.sendStatus(201));");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.core-testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.business-assurance");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.api-integration");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.data-persistence");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.real-dependency-testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "reference.problem-details-catalogue");
        Assert.DoesNotContain(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.dotnet-quality");
        Assert.Contains(".github/skills/cis-add-unit-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-architecture-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-mutation-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-business-acceptance-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-add-api-integration-tests/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-testcontainers-integration-test/SKILL.md", result.FilesToCreate);
        var testingStandard = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "standards",
            "testing-standard.md"));
        Assert.Contains("## Layered test model", testingStandard, StringComparison.Ordinal);
        Assert.Contains("## Change-to-test mapping", testingStandard, StringComparison.Ordinal);
        Assert.Contains("provisioning mechanism, not a separate test layer", testingStandard, StringComparison.Ordinal);
        Assert.Contains("**TEST-012**", testingStandard, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "problem-details-catalogue.md")));
        var apiDictionary = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "api-dictionary.md"));
        Assert.Contains(
            "| friends-todo-api:get:health-live | unversioned | GET | /health/live | friends-todo-api | friends-todo-api | unclassified",
            apiDictionary,
            StringComparison.Ordinal);
        Assert.Contains(
            "Deterministically discovered from an Express route; governance fields require review.",
            apiDictionary,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/test-only", apiDictionary, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_SelectsDataInfrastructureOperationsAndReleaseSkillPacks()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Jobs/Jobs.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Worker\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore\" Version=\"10.0.0\" /></ItemGroup></Project>");
        repository.Write(
            "src/Jobs/Worker.cs",
            "sealed class Worker : BackgroundService { DbSet<Order> Orders { get; set; } }");
        repository.Write(
            "infra/main.tf",
            "terraform { required_version = \">= 1.9\" } resource \"null_resource\" \"example\" {}");
        repository.Write(
            "tools/package.json",
            "{\"name\":\"delivery-tools\",\"version\":\"1.0.0\",\"dependencies\":{\"typescript\":\"5.9.0\"}}");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.data-persistence");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.core-testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.real-dependency-testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.infrastructure-delivery");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.operational-readiness");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.dependency-and-release");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.repository-transaction");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.observability");
        Assert.Contains(".github/skills/cis-database-migration/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-testcontainers-integration-test/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-infrastructure-delivery/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-observability-implementation/SKILL.md", result.FilesToCreate);
        Assert.Contains(".github/skills/cis-release-rollout/SKILL.md", result.FilesToCreate);
    }

    [Fact]
    public void Initialize_TerraformRepositoryReceivesTestingStandardWithoutApplicationTestSkills()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "terraform/main.tf",
            "terraform { required_version = \">= 1.9\" } resource \"null_resource\" \"example\" {}");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.testing");
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.infrastructure-delivery");
        Assert.DoesNotContain(result.StarterSelections, selection => selection.Definition == "guidance.skill-pack.core-testing");
        Assert.Contains("docs/cis/standards/testing-standard.md", result.FilesToCreate);
        Assert.DoesNotContain(".github/skills/cis-add-unit-tests/SKILL.md", result.FilesToCreate);
    }

    [Fact]
    public void Initialize_TerraformComposeRepositoryClassifiesAndSeedsConfiguration()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "terraform/main.tf",
            "terraform { required_version = \">= 1.9\" } resource \"null_resource\" \"example\" {}");
        repository.Write(
            "compose.yaml",
            "services:\n  api:\n    image: example/api:latest\n");
        repository.Write(
            ".env.example",
            "AUTH_MODE=anonymous\nANONYMOUS_SESSION_SECRET=\n");

        var classification = new RepositoryClassifier().Classify(repository.Path);
        var infrastructure = Assert.Single(classification.Components);
        Assert.Contains("hcl", infrastructure.Languages);
        Assert.Contains("yaml", infrastructure.Languages);
        Assert.Contains("terraform", infrastructure.Frameworks);
        Assert.Contains("docker-compose", infrastructure.Frameworks);
        Assert.Contains("configuration", infrastructure.Capabilities);

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            result.StarterSelections,
            selection => selection.Definition == "reference.configuration-dictionary");
        var configuration = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "configuration-dictionary.md"));
        Assert.Contains("| AUTH_MODE | AUTH_MODE | anonymous |", configuration, StringComparison.Ordinal);
        Assert.Contains(
            "| ANONYMOUS_SESSION_SECRET | ANONYMOUS_SESSION_SECRET | <redacted> |",
            configuration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_EventProducerSelectsOutboxProjectionStandard()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Messaging/Messaging.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Worker\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"MassTransit\" Version=\"9.0.0\" /></ItemGroup></Project>");
        repository.Write(
            "src/Messaging/Publisher.cs",
            "sealed class Publisher { Task PublishAsync(object message) => Task.CompletedTask; }");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StarterSelections, selection => selection.Definition == "standard.default.outbox-projection");
        Assert.Contains("docs/cis/standards/outbox-projection-standard.md", result.FilesToCreate);
    }

    [Fact]
    public void Initialize_ClassifiedRepositoryIsIdempotentAfterConfirmation()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "src/Example.Api/Program.cs",
            "builder.Services.AddControllers(); app.MapControllers();");
        var initializer = new RepositoryInitializer();
        var request = new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true);

        var first = initializer.Initialize(request);
        var second = initializer.Initialize(request);

        Assert.True(first.Applied);
        Assert.Equal("unchanged", second.Status);
        Assert.False(second.Applied);
        Assert.Empty(second.FilesToCreate);
        Assert.Empty(second.FilesToUpdate);
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis", "starter-manifest.yml")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "repository-profile.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, ".github", "skills", "cis-change-impact", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, ".github", "skills", "cis-delivery-execution", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, ".github", "skills", "cis-agent-execution", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, ".github", "skills", "cis-security-testing", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, ".github", "instructions", "cis-delivery-execution.instructions.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, ".github", "instructions", "cis-agent-execution.instructions.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, ".github", "instructions", "cis-security-testing.instructions.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "agent-provider-profile.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "ai-routing-profile.md")));
        Assert.Contains("| brd-question-suggestion | ollama | repository-smallest-local | no | no |",
            File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "references", "ai-routing-profile.md")),
            StringComparison.Ordinal);
        Assert.Contains("| brd-question-suggestions | .cis/local/brd/questions | 0 | 0 | preserve |",
            File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "references", "local-artifact-retention.md")),
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "diagnostics-profile.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "learning-history.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "workflows", "standard-delivery.md")));
        Assert.Contains(
            "cis docs validate --repo . --strict",
            File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "workflows", "standard-delivery.md")),
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "security-suite-profile.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "accepted-security-findings.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "standards", "api-controller-standard.md")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "standards", "security-testing-standard.md")));
        Assert.Contains(
            "API-BOUNDARY-001",
            File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "references", "standards-conformance-matrix.md")));
        Assert.Contains(
            "SEC-TEST-010",
            File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "references", "standards-conformance-matrix.md")));
    }

    [Fact]
    public void Initialize_UnclassifiedRepositorySeedsCoreGuidanceAndSpecifications()
    {
        using var repository = TemporaryRepository.Create();

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.documentation");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.standards-governance");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.instruction.standards-governance");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "standard.documentation-governance");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "standard.default.agent-documentation-compliance");
        Assert.DoesNotContain(result.StarterSelections, selection =>
            selection.Definition == "standard.default.testing");
        Assert.DoesNotContain(result.StarterSelections, selection =>
            selection.Definition == "standard.default.api-controller");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "reference.standards-conformance-matrix");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "template.standard");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "template.standard-inference-pattern");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.verification");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "specification.system-context");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "specification.product-intent");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "specification.technical-intent");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "specification.repository-delivery-policy");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "specification.api-design-and-governance");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "reference.task-type-capability-selections");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "reference.api-governance-profile");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "specification.external-tracker-synchronization");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "reference.external-tracker-profile");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.external-tracker-sync");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.instruction.external-tracker");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "template.feature-spec");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.maintain-contracts");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.api-contract-governance");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.repository-bootstrap");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.skill-governance");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.import-repositories");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.govern-business-requirements");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.govern-solution-design");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.instruction.solution-design");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.govern-ui-direction");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.instruction.ui-direction");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.graph-context");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.file-index");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.change-dossier");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.impact-review");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.decision-review");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.skill.bounded-planning");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.instruction.change-delivery");
        Assert.Contains(result.StarterSelections, selection =>
            selection.Definition == "guidance.instruction.business-requirements");
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "cis-repository.instructions.md")));
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-documentation",
            "SKILL.md")));
        var deliveryPolicy = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "specs",
            "repository-delivery-policy-spec.md"));
        Assert.Contains("local-only", deliveryPolicy, StringComparison.Ordinal);
        Assert.Contains("must not infer authority", deliveryPolicy, StringComparison.Ordinal);
        var capabilitySelections = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "task-type-capability-selections.md"));
        Assert.Contains("cis:task-type-capability-selections:start", capabilitySelections, StringComparison.Ordinal);
        Assert.Contains("cis plan capability select", capabilitySelections, StringComparison.Ordinal);
        Assert.Contains("cis plan task migrate-type", capabilitySelections, StringComparison.Ordinal);
        var bootstrapSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-repository-bootstrap",
            "SKILL.md"));
        Assert.Contains("cis repo init", bootstrapSkill, StringComparison.Ordinal);
        Assert.Contains("cis repo doctor", bootstrapSkill, StringComparison.Ordinal);
        var skillGovernanceSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-skill-governance",
            "SKILL.md"));
        Assert.Contains("cis skills validate --repo", skillGovernanceSkill, StringComparison.Ordinal);
        Assert.Contains("cis skills import --source", skillGovernanceSkill, StringComparison.Ordinal);
        Assert.Contains("cis skills audit --repo", skillGovernanceSkill, StringComparison.Ordinal);
        Assert.Contains("advisory candidates", skillGovernanceSkill, StringComparison.Ordinal);
        Assert.Contains("configured remote provider", skillGovernanceSkill, StringComparison.Ordinal);
        Assert.Contains("cis skills audit --fix", skillGovernanceSkill, StringComparison.Ordinal);
        var importSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-import-repositories",
            "SKILL.md"));
        Assert.Contains("cis repo import", importSkill, StringComparison.Ordinal);
        Assert.Contains("cis graph build --workspace", importSkill, StringComparison.Ordinal);
        Assert.Contains("not source copying", importSkill, StringComparison.Ordinal);
        var brdSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-govern-business-requirements",
            "SKILL.md"));
        Assert.Contains("cis brd discover", brdSkill, StringComparison.Ordinal);
        Assert.Contains("cis brd reconcile", brdSkill, StringComparison.Ordinal);
        Assert.Contains("Adopted feature specification", brdSkill, StringComparison.Ordinal);
        Assert.Contains("cis brd approve", brdSkill, StringComparison.Ordinal);
        Assert.Contains("explicitly authorizes", brdSkill, StringComparison.Ordinal);
        var solutionDesignSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-govern-solution-design",
            "SKILL.md"));
        Assert.Contains("cis solution-design init", solutionDesignSkill, StringComparison.Ordinal);
        Assert.Contains("one review point", solutionDesignSkill, StringComparison.Ordinal);
        var solutionDesignInstruction = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "cis-solution-design.instructions.md"));
        Assert.Contains("one atomic review", solutionDesignInstruction, StringComparison.Ordinal);
        Assert.Contains("TI-MOD-*", solutionDesignInstruction, StringComparison.Ordinal);
        var uiDirectionSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-govern-ui-direction",
            "SKILL.md"));
        Assert.Contains("cis ui-direction questions init", uiDirectionSkill, StringComparison.Ordinal);
        Assert.Contains("Feature textual wireframes", uiDirectionSkill, StringComparison.Ordinal);
        var uiDirectionInstruction = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "cis-ui-direction.instructions.md"));
        Assert.Contains("UI-framework-profile provenance", uiDirectionInstruction, StringComparison.Ordinal);
        Assert.Contains("Sharp/SVG", uiDirectionInstruction, StringComparison.Ordinal);
        var repositoryInstruction = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "cis-repository.instructions.md"));
        Assert.Contains("same `--repo` and `--root`", repositoryInstruction, StringComparison.Ordinal);
        Assert.Contains("explicit human-authority commands", repositoryInstruction, StringComparison.Ordinal);
        Assert.Contains("cis-import-repositories", repositoryInstruction, StringComparison.Ordinal);
        Assert.Contains("cis-skill-governance", repositoryInstruction, StringComparison.Ordinal);
        Assert.Contains("cis-standards-governance", repositoryInstruction, StringComparison.Ordinal);
        var standardsSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-standards-governance",
            "SKILL.md"));
        Assert.Contains("cis standards applicable", standardsSkill, StringComparison.Ordinal);
        Assert.Contains("cis standards patterns", standardsSkill, StringComparison.Ordinal);
        Assert.Contains("cis standards infer", standardsSkill, StringComparison.Ordinal);
        Assert.Contains("advisory-model", standardsSkill, StringComparison.Ordinal);
        var standardPatternTemplate = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "templates",
            "standard-pattern-template.md"));
        Assert.Contains("type: standard-inference-pattern", standardPatternTemplate, StringComparison.Ordinal);
        Assert.Contains("required_graph_capabilities:", standardPatternTemplate, StringComparison.Ordinal);
        Assert.Contains("minimum_consistency:", standardPatternTemplate, StringComparison.Ordinal);
        var standardsMatrix = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "standards-conformance-matrix.md"));
        Assert.Contains("CIS-STD-DOC-008", standardsMatrix, StringComparison.Ordinal);
        var graphContextSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-graph-context",
            "SKILL.md"));
        Assert.Contains("cis graph validate", graphContextSkill, StringComparison.Ordinal);
        Assert.Contains("cis context pack", graphContextSkill, StringComparison.Ordinal);
        var fileIndexSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-file-index",
            "SKILL.md"));
        Assert.Contains("cis index status", fileIndexSkill, StringComparison.Ordinal);
        Assert.Contains("cis index find", fileIndexSkill, StringComparison.Ordinal);
        Assert.Contains("--allow-remote", fileIndexSkill, StringComparison.Ordinal);
        var impactReviewSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-impact-review",
            "SKILL.md"));
        Assert.Contains("cis impact completeness", impactReviewSkill, StringComparison.Ordinal);
        Assert.Contains("Never disposition findings autonomously", impactReviewSkill, StringComparison.Ordinal);
        var decisionReviewSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-decision-review",
            "SKILL.md"));
        Assert.Contains("cis decision promote", decisionReviewSkill, StringComparison.Ordinal);
        Assert.Contains("explicit user authorization", decisionReviewSkill, StringComparison.Ordinal);
        var planningSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-bounded-planning",
            "SKILL.md"));
        Assert.Contains("cis plan validate", planningSkill, StringComparison.Ordinal);
        Assert.Contains("cis plan import-spec", planningSkill, StringComparison.Ordinal);
        Assert.Contains("cis plan derive", planningSkill, StringComparison.Ordinal);
        Assert.Contains("do not request separate impact or plan approvals", planningSkill, StringComparison.Ordinal);
        Assert.Contains("wireframe", planningSkill, StringComparison.Ordinal);
        Assert.Contains("decomposed parent", planningSkill, StringComparison.Ordinal);
        Assert.Contains("agent-tasks/WORK-NNN.md", planningSkill, StringComparison.Ordinal);
        Assert.Contains("design.md", planningSkill, StringComparison.Ordinal);
        Assert.Contains("verification.md", planningSkill, StringComparison.Ordinal);
        Assert.Contains("Never forge or infer feature authority", planningSkill, StringComparison.Ordinal);
        var designReviewSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-design-review",
            "SKILL.md"));
        Assert.Contains("wireframe-approve` is optional", designReviewSkill, StringComparison.Ordinal);
        Assert.Contains("wireframe digest, renderer, and PNG manifest together", designReviewSkill, StringComparison.Ordinal);
        var trackerSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-external-tracker-sync",
            "SKILL.md"));
        Assert.Contains("cis tracker plan", trackerSkill, StringComparison.Ordinal);
        Assert.Contains("never approve CIS plans", trackerSkill, StringComparison.OrdinalIgnoreCase);
        var trackerInstruction = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "cis-external-tracker.instructions.md"));
        Assert.Contains("cis tracker resolve", trackerInstruction, StringComparison.Ordinal);
        Assert.Contains("Credentials", trackerInstruction, StringComparison.Ordinal);
        var deliveryInstruction = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "cis-change-delivery.instructions.md"));
        Assert.Contains("applyTo: \"docs/cis/changes/**\"", deliveryInstruction, StringComparison.Ordinal);
        Assert.Contains("may not invent human disposition", deliveryInstruction, StringComparison.Ordinal);
        Assert.Contains("cis plan derive", deliveryInstruction, StringComparison.Ordinal);
        Assert.Contains("cis plan import-spec", deliveryInstruction, StringComparison.Ordinal);
        var brdInstruction = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "instructions",
            "cis-business-requirements.instructions.md"));
        Assert.Contains("docs/cis/specs/business-requirements.md", brdInstruction, StringComparison.Ordinal);
        Assert.Contains("feature specifications", brdInstruction, StringComparison.Ordinal);
        Assert.Contains("cis brd reconcile", brdInstruction, StringComparison.Ordinal);
        Assert.Contains("may never restore `Active`", brdInstruction, StringComparison.Ordinal);
        var featureTemplate = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "templates",
            "feature-spec-template.md"));
        Assert.Contains("type` to `feature-specification`", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("| ID | Surface | Frontend type | Requirement | Acceptance criteria |", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("public`, `customer`, or `backoffice", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("## Non-goals and explicit exclusions", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("## Search, projection, and retrieval boundaries", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("## Testing and regression requirements", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("Architecture and structural tests", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("Business acceptance tests", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("Testcontainers-provisioned dependency", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("Mutation testing and test-quality assurance", featureTemplate, StringComparison.Ordinal);
        Assert.Contains("unauthenticated/public endpoint", featureTemplate, StringComparison.Ordinal);
        var publicEndpointPolicy = Path.Combine(repository.Path, "docs", "cis", "specs", "public-endpoint-caching-policy-spec.md");
        Assert.True(File.Exists(publicEndpointPolicy));
        Assert.Contains("including on cache miss", File.ReadAllText(publicEndpointPolicy), StringComparison.OrdinalIgnoreCase);
        var apiGovernance = Path.Combine(repository.Path, "docs", "cis", "specs", "api-design-and-governance-spec.md");
        Assert.True(File.Exists(apiGovernance));
        var apiGovernanceContent = File.ReadAllText(apiGovernance);
        Assert.Contains("API-SCOPE-01", apiGovernanceContent, StringComparison.Ordinal);
        Assert.Contains("PARR", apiGovernanceContent, StringComparison.Ordinal);
        Assert.Contains("`public`", apiGovernanceContent, StringComparison.Ordinal);
        Assert.Contains("`customer`, `backoffice`, `internal/service`", apiGovernanceContent, StringComparison.Ordinal);
        Assert.Contains("OpenAPI does not replace", apiGovernanceContent, StringComparison.Ordinal);
        var apiGovernanceSkill = File.ReadAllText(Path.Combine(
            repository.Path,
            ".github",
            "skills",
            "cis-api-contract-governance",
            "SKILL.md"));
        Assert.Contains("PUBLIC-ENDPOINT-CACHE", apiGovernanceSkill, StringComparison.Ordinal);
        Assert.Contains("OpenAPI", apiGovernanceSkill, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "specs",
            "system-context-spec.md")));
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "specs",
            "delivery-and-assurance-spec.md")));
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "specs",
            "product-intent-spec.md")));
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "specs",
            "technical-intent-spec.md")));
        var technicalIntent = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "specs",
            "technical-intent-spec.md"));
        Assert.Contains("technical_intent_schema: 2", technicalIntent, StringComparison.Ordinal);
        Assert.Contains("approved_content_hash: null", technicalIntent, StringComparison.Ordinal);
        var apiProfile = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "api-governance-profile.md"));
        Assert.Contains("compatibility-mode", apiProfile, StringComparison.Ordinal);
        Assert.Contains("forward-transitive", apiProfile, StringComparison.Ordinal);
        Assert.Contains("## Supported versions", apiProfile, StringComparison.Ordinal);
        var trackerProfile = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "external-tracker-profile.md"));
        Assert.Contains("| github | github | no |", trackerProfile, StringComparison.Ordinal);
        Assert.Contains("| jira | jira | no |", trackerProfile, StringComparison.Ordinal);
        var trackerSpecification = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "specs",
            "external-tracker-synchronization-spec.md"));
        Assert.Contains("remote-deleted", trackerSpecification, StringComparison.Ordinal);
        Assert.Contains("ICisTrackerProvider", trackerSpecification, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "templates",
            "feature-spec-template.md")));
        Assert.True(File.Exists(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "templates",
            "adr-template.md")));
    }

    [Fact]
    public void Initialize_SeedsReferencesFromDeterministicRepositoryEvidence()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup>" +
            "<PackageReference Include=\"FluentValidation\" Version=\"12.0.0\" />" +
            "</ItemGroup></Project>");
        repository.Write(
            "src/Example.Api/Program.cs",
            "var app = builder.Build(); app.MapGet(\"/orders/{id}\", () => Results.Ok());");
        repository.Write(
            "src/Example.Api/OrdersController.cs",
            "[Route(\"api/[controller]\")] public sealed class OrdersController { [HttpPost(\"{id}\")] public void Update(string id) { } }");
        repository.Write(
            "src/Example.Api/appsettings.json",
            "{\"Feature\":{\"Enabled\":true},\"Auth\":{\"ClientSecret\":\"do-not-copy\"}}");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        var api = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "api-dictionary.md"));
        var packages = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "package-catalogue.md"));
        var configuration = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "configuration-dictionary.md"));

        Assert.Contains(
            "| example-api:get:orders-id | unversioned | GET | /orders/{id} | example-api | example-api | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | src/Example.Api/Program.cs | unknown | Deterministically discovered; governance fields require review. |",
            api);
        Assert.Contains(
            "| example-api:post:api-orders-id | unversioned | POST | /api/Orders/{id} | example-api | example-api | unclassified",
            api);
        Assert.Contains("Deterministically discovered from an MVC controller; governance fields require review.", api);
        Assert.Contains(
            "| FluentValidation | example-api | NuGet dependency | 12.0.0 | package-reference | Draft | src/Example.Api/Example.Api.csproj |",
            packages);
        Assert.Contains(
            "| Feature:Enabled | Feature:Enabled | True | startup-stable | example-api | no | Discovered JSON configuration value; allowed values and runtime consumption require review. | Draft | src/Example.Api/appsettings.json |",
            configuration);
        Assert.Contains(
            "| Auth:ClientSecret | Auth:ClientSecret | <redacted> | startup-stable | example-api | yes | Discovered JSON configuration value; allowed values and runtime consumption require review. | Draft | src/Example.Api/appsettings.json |",
            configuration);
        Assert.DoesNotContain("do-not-copy", configuration);
    }

    [Fact]
    public void Initialize_ConsolidatesEnvironmentVariantsOfAnOwnedConfigurationKey()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Example.Api/Example.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        repository.Write("src/Example.Api/appsettings.json", "{\"Feature\":{\"Enabled\":true}}");
        repository.Write("src/Example.Api/appsettings.Development.json", "{\"Feature\":{\"Enabled\":false}}");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        var configuration = File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "configuration-dictionary.md"));
        Assert.Equal(2, configuration.Split("| Feature:Enabled |", StringSplitOptions.None).Length);
        Assert.Contains("False<br>True", configuration);
        Assert.Contains("appsettings.Development.json<br>src/Example.Api/appsettings.json", configuration);
    }

    [Fact]
    public void Initialize_BindsAndSeedsDomainBehaviorFamilies()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Orders.Api/Orders.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup>" +
            "<PackageReference Include=\"MassTransit\" Version=\"9.0.0\" />" +
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore\" Version=\"10.0.0\" />" +
            "</ItemGroup></Project>");
        repository.Write(
            "src/Orders.Api/Orders.cs",
            "public sealed record CreateOrderCommand; " +
            "public sealed record OrderCreatedEvent; " +
            "public enum OrderStatus { Draft, Submitted, Completed } " +
            "public sealed class OrderSummaryProjection { } " +
            "public sealed class OrderNotFoundException : Exception { } " +
            "public sealed class OrdersContext { public DbSet<Order> Orders { get; set; } } " +
            "builder.Services.AddAuthorization(); bus.Publish(new OrderCreatedEvent()); " +
            "app.MapPost(\"/orders\", () => Results.Ok()).RequireAuthorization(\"orders.create\");");

        var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        foreach (var definition in new[]
                 {
                     "reference.command-dictionary",
                     "reference.event-dictionary",
                     "reference.workflow-state-dictionary",
                     "reference.business-invariant-catalogue",
                     "reference.projection-dictionary",
                     "reference.permissions-dictionary",
                     "reference.problem-details-catalogue",
                     "reference.data-dictionary",
                     "reference.traceability-matrix",
                 })
        {
            Assert.Contains(result.StarterSelections, selection => selection.Definition == definition);
        }

        string ReadReference(string name) => File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            name));

        Assert.Contains("CMD-ORDERS-API-CREATE-ORDER-COMMAND", ReadReference("command-dictionary.md"));
        Assert.Contains("EVT-ORDERS-API-ORDER-CREATED-EVENT", ReadReference("event-dictionary.md"));
        Assert.Contains("WF-ORDERS-API-ORDER-STATUS", ReadReference("workflow-state-dictionary.md"));
        Assert.Contains("PROJ-ORDERS-API-ORDER-SUMMARY-PROJECTION", ReadReference("projection-dictionary.md"));
        Assert.Contains("| orders.create | orders create |", ReadReference("permissions-dictionary.md"));
        Assert.Contains("PROB-ORDERS-API-ORDER-NOT-FOUND-EXCEPTION", ReadReference("problem-details-catalogue.md"));
        Assert.Contains("| Order | * | entity |", ReadReference("data-dictionary.md"));
        Assert.Contains("TRACE-ORDERS-API", ReadReference("traceability-matrix.md"));
    }

    [Fact]
    public void Initialize_NewProjectProducesOnlyIncrementalCreatesAndManagedUpdates()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Api/Program.cs", "builder.Services.AddControllers(); app.MapControllers();");
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        repository.Write(
            "src/Example.Worker/Example.Worker.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Worker\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Worker/Worker.cs", "public sealed class Worker : BackgroundService { }");

        var plan = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, plan.ExitCode);
        Assert.Equal(2, plan.Classification!.Components.Count);
        Assert.Contains(".github/instructions/example-worker-csharp.instructions.md", plan.FilesToCreate);
        Assert.Contains("docs/cis/specs/module-ownership-map-spec.md", plan.FilesToCreate);
        Assert.Contains("docs/cis/references/repository-profile.md", plan.FilesToUpdate);
        Assert.Contains("docs/cis/catalog.yml", plan.FilesToUpdate);
        Assert.Contains(".cis/starter-manifest.yml", plan.FilesToUpdate);
    }

    [Fact]
    public void Initialize_QuarantinesOnlyUnchangedObsoleteManagedArtifactsAndIsIdempotent()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Api/Program.cs", "builder.Services.AddControllers(); app.MapControllers();");
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        var instruction = Path.Combine(repository.Path, ".github", "instructions", "example-api-csharp.instructions.md");
        Directory.Delete(Path.Combine(repository.Path, "src"), recursive: true);

        var preview = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false,
            QuarantineObsolete: true));

        Assert.Contains(
            preview.QuarantinedPaths,
            path => path == ".github/instructions/example-api-csharp.instructions.md -> .cis/quarantine/repository-init/.github/instructions/example-api-csharp.instructions.md");
        Assert.True(File.Exists(instruction));

        var applied = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true,
            QuarantineObsolete: true));
        var quarantined = Path.Combine(
            repository.Path,
            ".cis",
            "quarantine",
            "repository-init",
            ".github",
            "instructions",
            "example-api-csharp.instructions.md");

        Assert.True(applied.Applied);
        Assert.False(File.Exists(instruction));
        Assert.True(File.Exists(quarantined));
        Assert.DoesNotContain(
            ".github/instructions/example-api-csharp.instructions.md",
            File.ReadAllText(Path.Combine(repository.Path, ".cis", "starter-manifest.yml")),
            StringComparison.Ordinal);

        var repeated = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false,
            QuarantineObsolete: true));
        Assert.Empty(repeated.QuarantinedPaths);
    }

    [Fact]
    public void Initialize_QuarantineRetainsEditedObsoleteManagedArtifacts()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Api/Program.cs", "builder.Services.AddControllers(); app.MapControllers();");
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        var instruction = Path.Combine(repository.Path, ".github", "instructions", "example-api-csharp.instructions.md");
        File.AppendAllText(instruction, "\nMaintainer exception.\n");
        Directory.Delete(Path.Combine(repository.Path, "src"), recursive: true);

        var applied = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true,
            QuarantineObsolete: true));

        Assert.True(File.Exists(instruction));
        Assert.Contains("Maintainer exception", File.ReadAllText(instruction), StringComparison.Ordinal);
        Assert.DoesNotContain(
            applied.QuarantinedPaths,
            path => path.StartsWith(".github/instructions/example-api-csharp.instructions.md", StringComparison.Ordinal));
        Assert.Contains(
            applied.Warnings,
            warning => warning.Contains("human-owned or edited", StringComparison.Ordinal)
                && warning.Contains("example-api-csharp.instructions.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Initialize_RetainsHumanEditsWhenTemplateHasNotChanged()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Api/Program.cs", "builder.Services.AddControllers(); app.MapControllers();");
        var initializer = new RepositoryInitializer();
        var request = new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true);
        initializer.Initialize(request);
        var referencePath = Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md");
        File.AppendAllText(referencePath, "\nMaintainer verified entry.\n");

        var result = initializer.Initialize(request);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("unchanged", result.Status);
        Assert.Contains("docs/cis/references/api-dictionary.md", result.RetainedPaths);
        Assert.Contains("Maintainer verified entry", File.ReadAllText(referencePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_ReportsMergeCollisionWhenEditedManagedFileAlsoNeedsUpdate()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Api/Program.cs", "builder.Services.AddControllers(); app.MapControllers();");
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        var profilePath = Path.Combine(repository.Path, "docs", "cis", "references", "repository-profile.md");
        File.AppendAllText(profilePath, "\nMaintainer note.\n");
        repository.Write(
            "src/Example.Worker/Example.Worker.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Worker\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Worker/Worker.cs", "public sealed class Worker : BackgroundService { }");

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(
            result.Collisions,
            collision => collision.Contains("repository-profile.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Maintainer note", File.ReadAllText(profilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_AcceptCurrentMarksConflictingArtifactAsHumanOwned()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("src/Example.Api/Program.cs", "builder.Services.AddControllers(); app.MapControllers();");
        var initializer = new RepositoryInitializer();
        initializer.Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        var profilePath = Path.Combine(repository.Path, "docs", "cis", "references", "repository-profile.md");
        File.AppendAllText(profilePath, "\nMaintainer-owned profile.\n");
        repository.Write(
            "src/Example.Worker/Example.Worker.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Worker\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var accepted = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true,
            AcceptCurrent: true));
        var repeated = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: true,
            Confirmed: false));

        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal(0, repeated.ExitCode);
        Assert.Contains("Maintainer-owned profile", File.ReadAllText(profilePath), StringComparison.Ordinal);
        Assert.Contains("ownership: human", File.ReadAllText(Path.Combine(repository.Path, ".cis", "starter-manifest.yml")), StringComparison.Ordinal);
        Assert.DoesNotContain(repeated.Collisions, collision => collision.Contains("repository-profile.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Initialize_AcceptCurrentAdoptsPreExistingStarterWithoutOverwritingIt()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("docs/cis/references/ai-routing-profile.md", "# Repository-specific AI routing\n");
        var initializer = new RepositoryInitializer();

        var result = initializer.Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true,
            AcceptCurrent: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("# Repository-specific AI routing\n", File.ReadAllText(Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "ai-routing-profile.md")));
        Assert.Contains("ownership: human", File.ReadAllText(Path.Combine(repository.Path, ".cis", "starter-manifest.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_AdoptsGovernedTechnicalIntentEvolutionWithoutFalseCollision()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        var request = new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true);
        Assert.Equal(0, initializer.Initialize(request).ExitCode);

        var technicalIntentPath = Path.Combine(repository.Path, "docs", "cis", "specs", "technical-intent-spec.md");
        File.WriteAllText(technicalIntentPath, """
            ---
            title: Governed Technical Intent
            type: specification
            status: Draft
            cis:
              stable_id: example:spec:technical-intent
              technical_intent_schema: 3
            ---

            # Governed Technical Intent

            <!-- cis:technical-intent-baseline:start -->
            | Kind | ID | Version | Evidence |
            | --- | --- | --- | --- |
            | brd | example:spec:business-requirements | semantic-v1:sha256:example | Active canonical BRD |
            <!-- cis:technical-intent-baseline:end -->

            <!-- cis:technical-intent-business-evidence:start -->
            Governed business evidence.
            <!-- cis:technical-intent-business-evidence:end -->
            """);
        repository.Write(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var preview = initializer.Initialize(new RepositoryInitRequest(
            repository.Path, "docs/cis", DryRun: true, Confirmed: false));

        Assert.DoesNotContain(preview.Collisions, collision =>
            collision.Contains("technical-intent-spec.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("docs/cis/specs/technical-intent-spec.md", preview.RetainedPaths);

        Assert.Equal(0, initializer.Initialize(request).ExitCode);
        var repeated = initializer.Initialize(new RepositoryInitRequest(
            repository.Path, "docs/cis", DryRun: true, Confirmed: false));
        Assert.DoesNotContain(repeated.Collisions, collision =>
            collision.Contains("technical-intent-spec.md", StringComparison.OrdinalIgnoreCase));

        var manifest = File.ReadAllText(Path.Combine(repository.Path, ".cis", "starter-manifest.yml"));
        Assert.Contains("definition: specification.technical-intent", manifest, StringComparison.Ordinal);
        Assert.Contains("ownership: human", manifest, StringComparison.Ordinal);
        Assert.Contains("Governed business evidence.", File.ReadAllText(technicalIntentPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Classifier_RecognizesVsCodeExtensionAsToolingComponent()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "vscode-extension/package.json",
            "{\"name\":\"cis-vscode\",\"engines\":{\"vscode\":\"^1.95.0\"},\"main\":\"./extension.js\"}");
        repository.Write("vscode-extension/extension.js", "function activate(context) { } module.exports = { activate };");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Equal("vscode-extension", component.Root);
        Assert.Contains("vscode-extension", component.Frameworks);
        Assert.Contains("tooling", component.Roles);
        Assert.Contains("commands", component.Capabilities);
    }

    [Fact]
    public void Classifier_ExcludesReleaseArtifactsFromComponentDiscovery()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Actual/Actual.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "artifacts/copied/FalseComponent.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            ".codex-tmp/adoption-smoke/FalseTemporaryComponent.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var component = Assert.Single(new RepositoryClassifier().Classify(repository.Path).Components);

        Assert.Equal("src/Actual", component.Root);
    }

    [Fact]
    public void SecurityDoctor_RejectsMutableWorkflowActionsAndScannerImages()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/app.ts", "export const ready = true;");
        new RepositoryInitializer().Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        repository.Write(".github/workflows/ci.yml", "steps:\n  - uses: actions/checkout@v4\n");
        repository.Write(".github/workflows/security.yml", "steps:\n  - run: docker run --rm ghcr.io/gitleaks/gitleaks:v8.28.0 detect\n");
        repository.Write("tools/run-security-scan.mjs", "const scanner = 'aquasec/trivy:0.72.0';\n");
        repository.Write("Dockerfile", "FROM node:24-alpine\n");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;

        var findings = new SecuritySuiteProfileDoctorCheck().Inspect(context);

        Assert.Contains(findings, finding => finding.Code == "CIS-SEC-DOCTOR-008" && finding.Severity == "error");
        Assert.Contains(findings, finding => finding.Code == "CIS-SEC-DOCTOR-009" && finding.Severity == "error");
    }

    [Fact]
    public void SecurityDoctor_AcceptsImmutableWorkflowActionsAndScannerImages()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/app.ts", "export const ready = true;");
        new RepositoryInitializer().Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        repository.Write(".github/workflows/ci.yml", "steps:\n  - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4\n");
        repository.Write("scripts/run-security-scan.mjs", "const scanner = 'aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f';\n");
        repository.Write("Dockerfile", "FROM node:24-alpine@sha256:595398b0081eacda8e1c4c5b97b76cd1020e4d58a8ebcb4843b9bca1e79e7436\n");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;

        var findings = new SecuritySuiteProfileDoctorCheck().Inspect(context);

        Assert.DoesNotContain(findings, finding => finding.Code is "CIS-SEC-DOCTOR-008" or "CIS-SEC-DOCTOR-009");
    }

    [Fact]
    public void SecurityDoctor_PrunesGeneratedArtifactTreesBeforeInspectingDockerfiles()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/app.ts", "export const ready = true;");
        new RepositoryInitializer().Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        repository.Write("artifacts/clean-profile/agent-host/local-endpoint/Dockerfile", "FROM node:latest\n");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;

        var findings = new SecuritySuiteProfileDoctorCheck().Inspect(context);

        Assert.DoesNotContain(findings, finding => finding.Code == "CIS-SEC-DOCTOR-009");
    }

    [Fact]
    public void SecurityDoctor_AcceptsGovernedScannerWrapperModes()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/app.ts", "export const ready = true;");
        new RepositoryInitializer().Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        repository.Write(
            "docs/cis/references/security-suite-profile.md",
            "| Suite ID | Tool | Command | Working directory | Result format | Result path |\n" +
            "|---|---|---|---|---|---|\n" +
            "| sast | semgrep | node tools/run-security-scan.mjs sast | . | semgrep-json | .cis/local/security/results/semgrep.json |\n" +
            "| secrets | gitleaks | node tools/run-security-scan.mjs secrets | . | gitleaks-json | .cis/local/security/results/gitleaks.json |\n" +
            "| filesystem | trivy | node tools/run-security-scan.mjs filesystem | . | trivy-json | .cis/local/security/results/trivy.json |\n");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;

        var findings = new SecuritySuiteProfileDoctorCheck().Inspect(context);

        Assert.DoesNotContain(findings, finding => finding.Code == "CIS-SEC-DOCTOR-007");
    }

    [Fact]
    public void Initializer_PrunesGeneratedArtifactTreesWhenDetectingContainerSecuritySuites()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "package.json",
            "{\"name\":\"sample\",\"scripts\":{\"security:image\":\"trivy image sample\"},\"dependencies\":{\"next\":\"16.0.0\"}}");
        repository.Write("src/app.ts", "export const ready = true;");
        repository.Write("artifacts/clean-profile/agent-host/local-endpoint/Dockerfile", "FROM scratch\n");

        var result = new RepositoryInitializer().Initialize(
            new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        var profile = File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "security-suite-profile.md"));
        Assert.DoesNotContain("repository-image", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void Initializer_PreservesConfiguredRepositoryIdentityWhenCheckoutFolderNameChanges()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            ".cis/repository.yml",
            "schema_version: 1\n\nrepository:\n  id: portable-repository\n\ndocumentation_root: docs/cis\n");
        repository.Write("docs/cis/.keep", string.Empty);
        repository.Write("src/app.ts", "export const ready = true;");

        var result = new RepositoryInitializer().Initialize(
            new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Contains("id: portable-repository", File.ReadAllText(System.IO.Path.Combine(repository.Path, ".cis", "repository.yml")), StringComparison.Ordinal);
        Assert.Contains("portable-repository:docs:root", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "catalog.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Reinitialize_AddsNewDefaultStandardMappingsWithoutReplacingHumanMatrixRows()
    {
        using var repository = TemporaryRepository.Create();
        var initializer = new RepositoryInitializer();
        var request = new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true);
        Assert.Equal(0, initializer.Initialize(request).ExitCode);
        var matrixPath = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "standards-conformance-matrix.md");
        File.AppendAllText(
            matrixPath,
            "| repository:standard:custom | CUSTOM-001 | custom | manual-review | maintainer review | Active | Human-owned row. |\n");
        repository.Write("package.json", "{\"name\":\"portal\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\"}}");
        repository.Write("app/page.tsx", "export default function Page() { return null; }\n");

        var result = initializer.Initialize(request);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        var matrix = File.ReadAllText(matrixPath);
        Assert.Contains("CUSTOM-001", matrix, StringComparison.Ordinal);
        Assert.Contains("SEC-FEAT-006", matrix, StringComparison.Ordinal);
        Assert.Equal(1, matrix.Split("SEC-FEAT-006", StringSplitOptions.None).Length - 1);
        Assert.Equal("unchanged", initializer.Initialize(request).Status);
    }

    [Fact]
    public void TestingDoctor_AcceptsGovernedDotnetTestWrapper()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        new RepositoryInitializer().Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        repository.Write(
            "docs/cis/references/test-suite-profile.md",
            "| Suite ID | Component | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path |\n" +
            "|---|---|---|---|---|---|---|---|---|\n" +
            "| dotnet-tests | repository | dotnet-test | pwsh tools/run-dotnet-tests.ps1 | . | trx | .cis/local/testing/results/dotnet-tests.trx | .cis/local/testing/coverage/summary.json | - |\n");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;

        var findings = new TestSuiteProfileDoctorCheck().Inspect(context);

        Assert.DoesNotContain(findings, finding => finding.Code == "CIS-TEST-DOCTOR-005");
    }

    [Fact]
    public void TestingDoctor_AcceptsClassificationComponentRootAsSuiteComponent()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "vscode-extension/package.json",
            "{\"name\":\"sample-extension\",\"engines\":{\"vscode\":\"^1.109.0\"},\"scripts\":{\"test\":\"node --test\"}}");
        repository.Write("vscode-extension/extension.js", "exports.activate = () => {};\n");
        new RepositoryInitializer().Initialize(new RepositoryInitRequest(repository.Path, "docs/cis", DryRun: false, Confirmed: true));
        repository.Write(
            "docs/cis/references/test-suite-profile.md",
            "| Suite ID | Component | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path |\n" +
            "|---|---|---|---|---|---|---|---|---|\n" +
            "| vscode-tests | vscode-extension | vitest | npm test | vscode-extension | vitest-json | .cis/local/testing/results/vscode.json | - | - |\n");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;

        var findings = new TestSuiteProfileDoctorCheck().Inspect(context);

        Assert.DoesNotContain(findings, finding => finding.Code == "CIS-TEST-DOCTOR-007");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cis-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRepository(path);
        }

        public void Write(string relativePath, string content)
        {
            var absolutePath = System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, content);
        }

        public void Dispose()
        {
            var fullPath = System.IO.Path.GetFullPath(Path);
            var safeParent = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-tests")) +
                System.IO.Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }

    private sealed class FixedOllamaProbe(OllamaProbeResult result) : IOllamaProbe
    {
        public OllamaProbeResult Probe() => result;
    }
}
