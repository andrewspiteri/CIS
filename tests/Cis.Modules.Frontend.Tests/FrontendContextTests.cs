using Cis.Abstractions;
using Cis.Modules.Frontend;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Frontend.Tests;

public sealed class FrontendContextTests
{
    [Fact]
    public void BuiltInProviders_DiscoverWebNativeAndGodotContextIdempotently()
    {
        using var repository = Fixture.Create();
        repository.Write("web/app/lists/[listId]/page.tsx", "import Link from 'next/link'; export default function ListPage(){ const [x]=useState(1); fetch('/api/lists'); return <Link href='/app'>Home</Link>; }");
        repository.Write("admin/app.routes.ts", "import { Routes } from '@angular/router'; const routes: Routes = [{ path: 'orders', component: OrdersPage }];");
        repository.Write("ios/HomeView.swift", "import SwiftUI\nstruct HomeView: View { var body: some View { NavigationStack { Text(\"Home\") } } }");
        repository.Write("android/Home.kt", "import androidx.compose.runtime.Composable\n@Composable fun HomeScreen() { navController.navigate(\"lists\") }");
        repository.Write("game/Main.tscn", "[gd_scene format=3]\n[node name=\"Main\" type=\"Control\"]");
        repository.Write("portal/Home.vue", "<template><main>Home</main></template>");
        repository.Write(".codex-tmp/adoption-smoke/web/app/page.tsx", "export default function TemporaryPage(){ return <div>Temporary</div>; }");
        repository.Write("artifacts/vscode-smoke/web/app/page.tsx", "export default function GeneratedArtifact(){ return <div>Generated</div>; }");
        var service = CreateService();

        var first = service.Discover(repository.Path);
        var second = service.Discover(repository.Path);

        Assert.Equal(0, first.ExitCode);
        Assert.False(second.Applied);
        foreach (var framework in new[] { "nextjs", "angular", "swiftui", "jetpack-compose", "godot", "vue" })
            Assert.Contains(first.Observations, item => item.Framework == framework);
        Assert.Contains(first.Observations, item => item.Kind == "route" && item.Route == "/lists/{listId}");
        Assert.Contains(first.Observations, item => item.Kind == "api-call" && item.Target == "/api/lists");
        Assert.DoesNotContain(first.Observations, item => item.SourcePath.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(first.Observations, item => item.SourcePath.Contains(".codex-tmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(first.Observations, item => item.SourcePath.Contains("artifacts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Augmentation_ProducesGraphReadyNodesAndBoundedRelationships()
    {
        using var repository = Fixture.Create();
        repository.Write("web/app/page.tsx", "export default function HomePage(){ return <div>Home</div>; }");
        var service = CreateService();
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        Assert.Equal("frontend-context/3", service.Name);

        var augmentation = service.Augment(context, new Dictionary<string, string> { ["web/app/page.tsx"] = "sha256:test" });

        Assert.Contains(augmentation.Nodes, item => item.Kind == "screen");
        Assert.Contains(augmentation.Nodes, item => item.Kind == "route");
        Assert.Contains(augmentation.Edges, item => item.Type == "contains");
        Assert.Contains(augmentation.Edges, item => item.Type == "renders");
        Assert.All(augmentation.Nodes, item => Assert.Contains("frontend", item.Facets));
        Assert.All(augmentation.Nodes, item => Assert.Equal("active", item.Lifecycle));
        Assert.All(augmentation.Edges, item => Assert.Equal("discovered", item.State));
    }

    [Fact]
    public void DuplicateProviderNamesAndRouteCollisions_FailClosed()
    {
        using var repository = Fixture.Create();
        var providers = new ICisFrontendContextProvider[]
        {
            new FixtureProvider("fixture/1", "target-a"),
            new FixtureProvider("fixture/1", "target-b"),
        };
        repository.Write("ui/file.ts", "source");
        var service = new FrontendContextService(new CisRepositoryContextResolver(), providers,
            () => DateTimeOffset.Parse("2026-08-27T12:00:00Z"));

        var result = service.Discover(repository.Path);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-FRONTEND-PROVIDER-001");
    }

    [Fact]
    public void AngularRelativeRoutes_DoNotCollideAcrossModulesOrReadTestFixtures()
    {
        using var repository = Fixture.Create();
        repository.Write("src/app.routes.ts", "import { Routes } from '@angular/router'; const routes: Routes = [{ path: '', redirectTo: 'home' }, { path: '', component: Shell, children: [{ path: '', component: Home }] }];");
        repository.Write("src/child.routes.ts", "import { Routes } from '@angular/router'; const routes: Routes = [{ path: '', component: Child }];");
        repository.Write("src/fixture.spec.ts", "import { Routes } from '@angular/router'; const fixture = { path: '/uploads/test.csv', component: Fake };");

        var result = CreateService().Discover(repository.Path);

        Assert.Equal(0, result.ExitCode);
        var routes = result.Observations.Where(item => item.Kind == "route").ToArray();
        Assert.Equal(3, routes.Length);
        Assert.Equal(3, routes.Select(item => item.Id).Distinct().Count());
        Assert.All(routes, item => Assert.Equal("relative-declaration", item.Properties["routeResolution"]));
        Assert.DoesNotContain(result.Observations, item => item.SourcePath.EndsWith(".spec.ts", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "CIS-FRONTEND-ROUTE-001");
    }

    [Fact]
    public void ResolvedNextRoutes_StillRejectConflictingPageTargets()
    {
        using var repository = Fixture.Create();
        repository.Write("web/app/(first)/page.tsx", "export default function FirstPage() { return <main />; }");
        repository.Write("web/app/(second)/page.tsx", "export default function SecondPage() { return <main />; }");

        var result = CreateService().Discover(repository.Path);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-FRONTEND-ROUTE-001");
    }

    private static FrontendContextService CreateService()
    {
        var services = new ServiceCollection();
        new RepositoryModule().RegisterServices(services);
        new FrontendModule().RegisterServices(services);
        return services.BuildServiceProvider().GetRequiredService<FrontendContextService>();
    }

    private sealed class FixtureProvider(string name, string target) : ICisFrontendContextProvider
    {
        public string Name => name;
        public bool CanInspect(CisFrontendSourceFile source) => true;
        public IReadOnlyList<CisFrontendObservation> Discover(CisFrontendDiscoveryContext context, CisFrontendSourceFile source) =>
            [new(Name, "fixture", "route", target, "route", source.Path, 1, "/same", target, new Dictionary<string, string>())];
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; }
        private Fixture(string path) => Path = path;
        public static Fixture Create()
        {
            var fixture = new Fixture(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-frontend-tests", Guid.NewGuid().ToString("N")));
            fixture.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: frontend-fixture\ndocumentation_root: docs/cis\n");
            fixture.Write("docs/cis/catalog.yml", "schema_version: 1\nrepository: frontend-fixture\ndocuments: []\n");
            fixture.Write("docs/cis/references/screen-route-map.md", "| Screen or route | Component | Platform | Access / permission | Destination | Status | Evidence | Notes |\n|---|---|---|---|---|---|---|---|\n| Lists | web | Web | customer | /lists/{listId} | Active | source | - |\n");
            return fixture;
        }
        public void Write(string relative, string content)
        {
            var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
