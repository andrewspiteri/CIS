using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Brd;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Cis.Modules.SolutionDesign;
using Cis.Modules.TechnicalIntent;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

[CollectionDefinition("SolutionDesignConsole", DisableParallelization = true)]
public sealed class SolutionDesignConsoleCollection;

[Collection("SolutionDesignConsole")]
public sealed class SolutionDesignWorkflowTests
{
    [Fact]
    public void Commands_AreRegistered()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new WorkspaceModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new BrdModule())
            .AddModule(new TechnicalIntentModule())
            .AddModule(new SolutionDesignModule())
            .Build();

        Assert.Equal(0, application.Invoke(["solution-design", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["solution-design", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["solution-design", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["solution-design", "approve", "--help"]));
    }

    [Fact]
    public void InitializeAndApprove_ProduceOneCurrentAtomicBundleIdempotently()
    {
        using var fixture = Fixture.Create();

        var initialized = fixture.Service.Initialize(fixture.Root);

        Assert.Equal(0, initialized.ExitCode);
        Assert.True(initialized.Applied);
        Assert.Equal("Ready for Approval", initialized.Validation!.EffectiveStatus);
        Assert.Equal(2, initialized.Components.Count);
        Assert.Contains(initialized.Components, component => component.Id == "TI-MOD-IDENTITY");
        Assert.Contains(initialized.Components, component => component.Id == "TI-MOD-WORKSPACE");
        var design = File.ReadAllText(fixture.DesignPath);
        var sheet = File.ReadAllText(fixture.SheetPath);
        Assert.Contains("## System context and boundaries", design, StringComparison.Ordinal);
        Assert.Contains("## UI design handoff", design, StringComparison.Ordinal);
        Assert.Contains("| `TI-MOD-IDENTITY` | Identity |", sheet, StringComparison.Ordinal);
        Assert.Contains("## Component responsibility profiles", sheet, StringComparison.Ordinal);
        Assert.Contains("## Component interaction catalogue", sheet, StringComparison.Ordinal);
        Assert.False(fixture.Service.Evaluate(fixture.Root).Ready);

        var approved = fixture.Service.Approve(fixture.Root, "Andrew Spiteri", "The complete solution architecture is accepted.");

        Assert.Equal("Active", approved.Validation!.EffectiveStatus);
        Assert.True(fixture.Service.Evaluate(fixture.Root).Ready);
        var approvedDesign = File.ReadAllText(fixture.DesignPath);
        var approvedSheet = File.ReadAllText(fixture.SheetPath);
        Assert.Contains("approved_bundle_hash: \"sha256:", approvedDesign, StringComparison.Ordinal);
        Assert.Contains("approved_bundle_hash: \"sha256:", approvedSheet, StringComparison.Ordinal);
        Assert.Contains("approved_by: \"Andrew Spiteri\"", approvedDesign, StringComparison.Ordinal);

        File.WriteAllText(fixture.TechnicalIntentPath, File.ReadAllText(fixture.TechnicalIntentPath)
            .Replace("last_reviewed: 2026-09-02", "last_reviewed: 2026-09-03", StringComparison.Ordinal));
        Assert.Equal("Active", fixture.Service.Status(fixture.Root).Validation!.EffectiveStatus);

        var repeated = fixture.Service.Initialize(fixture.Root);

        Assert.False(repeated.Applied);
        Assert.Equal("Active", repeated.Validation!.EffectiveStatus);
        Assert.Equal(approvedDesign, File.ReadAllText(fixture.DesignPath));
        Assert.Equal(approvedSheet, File.ReadAllText(fixture.SheetPath));
    }

    [Fact]
    public void SourceOrBundleChange_RequiresRenewedWholeBundleApproval()
    {
        using var fixture = Fixture.Create();
        fixture.Service.Initialize(fixture.Root);
        fixture.Service.Approve(fixture.Root, "Architecture owner", "Reviewed as one bundle.");

        File.AppendAllText(fixture.DesignPath, "\nA material unapproved design direction.\n");
        var changed = fixture.Service.Status(fixture.Root);

        Assert.Equal("Stale", changed.Validation!.EffectiveStatus);
        Assert.False(changed.Validation.Current);
        Assert.False(fixture.Service.Evaluate(fixture.Root).Ready);

        var reconciledEdit = fixture.Service.Initialize(fixture.Root);
        Assert.Equal("Ready for Approval", reconciledEdit.Validation!.EffectiveStatus);
        Assert.Contains("A material unapproved design direction.", File.ReadAllText(fixture.DesignPath), StringComparison.Ordinal);
        Assert.Contains("approved_by: null", File.ReadAllText(fixture.DesignPath), StringComparison.Ordinal);

        File.WriteAllText(fixture.DesignPath, File.ReadAllText(fixture.DesignPath).Replace(
            "\nA material unapproved design direction.\n", string.Empty, StringComparison.Ordinal));
        File.AppendAllText(fixture.TechnicalIntentPath, "\nApproved source evolution.\n");
        fixture.Source.Result = fixture.Source.Result with { Validation = fixture.ActiveValidation };
        Assert.False(fixture.Service.Status(fixture.Root).Validation!.Current);

        var refreshed = fixture.Service.Initialize(fixture.Root);

        Assert.True(refreshed.Applied);
        Assert.Equal("Ready for Approval", refreshed.Validation!.EffectiveStatus);
        Assert.DoesNotContain("approved_by: \"Architecture owner\"", File.ReadAllText(fixture.DesignPath), StringComparison.Ordinal);
    }

    [Fact]
    public void InactiveTechnicalIntent_BlocksGeneration()
    {
        using var fixture = Fixture.Create();
        fixture.Source.Result = fixture.Source.Result with
        {
            Validation = new TechnicalIntentValidation(true, true, "Ready for Approval", "Review Required", [], [])
        };

        var result = fixture.Service.Initialize(fixture.Root);

        Assert.Equal("blocked", result.Status);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("Active, current technical intent", StringComparison.Ordinal));
        Assert.False(File.Exists(fixture.DesignPath));
    }

    [Fact]
    public void Commands_RenderHumanAgentJsonAndRejectUnknownFormats()
    {
        using var fixture = Fixture.Create();
        using var application = new CisHostBuilder()
            .AddModule(new FixtureDependenciesModule(fixture))
            .AddModule(new SolutionDesignModule())
            .Build();

        var human = Invoke(application, ["solution-design", "init", "--workspace", fixture.Root, "--format", "human"]);
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Overall solution design: initialized", human.Output, StringComparison.Ordinal);
        Assert.Contains("Components: 2", human.Output, StringComparison.Ordinal);

        var agent = Invoke(application, ["solution-design", "status", "--workspace", fixture.Root, "--format", "agent"]);
        Assert.Equal(0, agent.ExitCode);
        Assert.Contains("status=status;exitCode=0", agent.Output, StringComparison.Ordinal);
        Assert.Contains("component=TI-MOD-IDENTITY", agent.Output, StringComparison.Ordinal);

        var json = Invoke(application, ["solution-design", "validate", "--workspace", fixture.Root, "--format", "json"]);
        Assert.Equal(0, json.ExitCode);
        Assert.Contains("\"status\": \"validated\"", json.Output, StringComparison.Ordinal);
        Assert.Contains("\"componentSheetPath\"", json.Output, StringComparison.Ordinal);

        var approved = Invoke(application,
        [
            "solution-design", "approve", "--workspace", fixture.Root,
            "--reviewer", "Andrew; Spiteri", "--reason", "Architecture\napproved", "--format", "agent"
        ]);
        Assert.Equal(0, approved.ExitCode);
        Assert.Contains("status=approved", approved.Output, StringComparison.Ordinal);

        var unsupported = Invoke(application, ["solution-design", "status", "--workspace", fixture.Root, "--format", "yaml"]);
        Assert.Equal(2, unsupported.ExitCode);
        Assert.Contains("Unsupported format 'yaml'", unsupported.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDocumentsAndInvalidApprovalFailClosed()
    {
        using var fixture = Fixture.Create();

        var missing = fixture.Service.Status(fixture.Root);
        Assert.Equal("missing", missing.Status);
        Assert.Equal(4, missing.ExitCode);
        Assert.Equal("Missing", missing.Validation!.DesignStatus);
        Assert.Equal("Missing", missing.Validation.ComponentSheetStatus);

        var invalid = fixture.Service.Approve(fixture.Root, " ", " ");
        Assert.Equal("invalid", invalid.Status);
        Assert.Equal(2, invalid.ExitCode);
        Assert.Contains("Reviewer and approval reason are required.", invalid.Errors);

        fixture.Service.Initialize(fixture.Root);
        File.WriteAllText(fixture.DesignPath, File.ReadAllText(fixture.DesignPath)
            .Replace("product:architecture:overall-solution-design", "foreign:architecture:design", StringComparison.Ordinal));
        var collision = fixture.Service.Initialize(fixture.Root);
        Assert.Equal("collision", collision.Status);
        Assert.Equal(2, collision.ExitCode);

        var nonWorkspace = Path.Combine(Path.GetTempPath(), "cis-no-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nonWorkspace);
        try
        {
            var notApplicable = fixture.Service.Evaluate(nonWorkspace);
            Assert.False(notApplicable.Applicable);
            Assert.True(notApplicable.Ready);
        }
        finally
        {
            Directory.Delete(nonWorkspace, true);
        }
    }

    private static (int ExitCode, string Output, string Error) Invoke(CisApplication application, string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = application.Invoke(args);
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, SolutionDesignService service, StubTechnicalIntentSource source)
        {
            Root = root;
            Service = service;
            Source = source;
        }

        public string Root { get; }
        public string TechnicalIntentPath => Path.Combine(Root, "docs", "specs", "technical-intent-spec.md");
        public string DesignPath => Path.Combine(Root, "docs", "architecture", "overall-solution-design.md");
        public string SheetPath => Path.Combine(Root, "docs", "references", "component-sheet.md");
        public SolutionDesignService Service { get; }
        public StubTechnicalIntentSource Source { get; }
        public TechnicalIntentValidation ActiveValidation => new(true, true, "Active", "Active", [], []);

        public static Fixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "cis-solution-design-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, ".cis"));
            Directory.CreateDirectory(Path.Combine(root, "docs", "specs"));
            File.WriteAllText(Path.Combine(root, ".cis", "workspace.yml"), "schema_version: 2\necosystem:\n  id: product\n  name: Product\nproduct:\n  id: product\n  name: Product\nrepositories:\n- id: product\n  path: .\n  documentation_root: docs\n  role: authority\n  participation: owned\n  relationship: none\n  components: []\n");
            File.WriteAllText(Path.Combine(root, ".cis", "repository.yml"), "repository:\n  id: product\ndocumentation_root: docs\n");
            File.WriteAllText(Path.Combine(root, "docs", "catalog.yml"), "schema_version: 1\nrepository: product\ndocuments: []\n");
            File.WriteAllText(Path.Combine(root, "docs", "specs", "technical-intent-spec.md"), TechnicalIntent());
            var workspace = new CisWorkspace(root, Path.Combine(root, ".cis", "workspace.yml"),
                [new CisWorkspaceRepository("product", root, "docs", "authority")]);
            var source = new StubTechnicalIntentSource(new TechnicalIntentResult("status", root, "product",
                "docs/specs/technical-intent-spec.md", new TechnicalIntentValidation(true, true, "Active", "Active", [], []), [], [], [], false));
            var service = new SolutionDesignService(new StubWorkspaceRegistry(workspace),
                new StubRepositoryResolver(new CisRepositoryContext(root, "product", "docs", Path.Combine(root, "docs"), Path.Combine(root, "docs", "catalog.yml"))),
                new DocumentationCatalogMerger(), source, () => new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero));
            return new Fixture(root, service, source);
        }

        public void Dispose() => Directory.Delete(Root, true);

        private static string TechnicalIntent() => """
            ---
            title: "Product Technical Intent"
            type: technical-intent
            status: Active
            last_reviewed: 2026-09-02
            ---

            # Product Technical Intent

            ## Design goals and principles

            Keep product behavior simple and component boundaries explicit.

            ## Runtime architecture and trust boundaries

            The browser calls one trusted application boundary over HTTPS.

            ## Product module architecture

            | Module | Classification | Purpose | Owns | Must not own | BRD authority |
            | --- | --- | --- | --- | --- | --- |
            | `TI-MOD-IDENTITY` — Identity | backend | Establish actors | Actor sessions | Product lists | `BRD-FR-001` |
            | `TI-MOD-WORKSPACE` — Workspace | frontend | Present work | View state | Credentials | `BRD-FR-002` |

            ## Application model and invariants

            Identity owns sessions; workspace owns no durable business state.

            ## Integration point catalog

            | Integration | From | To | Contract | Direction |
            | --- | --- | --- | --- | --- |
            | `TI-INT-WORKSPACE-IDENTITY` | Workspace | Identity | Session API | forward-transitive |

            ## Security and privacy intent

            Deny access without a verified actor.

            ## Operations, migration, and recovery intent

            Health, backup, restore, and migration behavior is explicit.

            ## Quality attributes and verification direction

            Test boundaries independently and end to end.

            ## Component and interaction map

            Browser → application boundary → owned state.
            """;
    }

    private sealed class StubWorkspaceRegistry(CisWorkspace workspace) : ICisWorkspaceRegistry
    {
        public CisWorkspaceResolution Resolve(string workspacePath) => new(workspace, []);
    }

    private sealed class StubRepositoryResolver(CisRepositoryContext context) : ICisRepositoryContextResolver
    {
        public CisRepositoryContextResolution Resolve(string repositoryPath) => new(context, []);
    }

    private sealed class StubTechnicalIntentSource(TechnicalIntentResult result) : ISolutionDesignTechnicalIntentSource
    {
        public TechnicalIntentResult Result { get; set; } = result;
        public TechnicalIntentResult Status(string workspacePath) => Result;
    }

    private sealed class FixtureDependenciesModule(Fixture fixture) : ICisModule
    {
        public string Name => "solution-design-test-dependencies";
        public string Description => "Registers deterministic solution-design test dependencies.";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<ICisWorkspaceRegistry>(new StubWorkspaceRegistry(new CisWorkspace(
                fixture.Root,
                Path.Combine(fixture.Root, ".cis", "workspace.yml"),
                [new CisWorkspaceRepository("product", fixture.Root, "docs", "authority")])));
            services.AddSingleton<ICisRepositoryContextResolver>(new StubRepositoryResolver(new CisRepositoryContext(
                fixture.Root,
                "product",
                "docs",
                Path.Combine(fixture.Root, "docs"),
                Path.Combine(fixture.Root, "docs", "catalog.yml"))));
            services.AddSingleton(new DocumentationCatalogMerger());
            services.AddSingleton<ISolutionDesignTechnicalIntentSource>(fixture.Source);
        }

        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
        {
        }
    }
}
