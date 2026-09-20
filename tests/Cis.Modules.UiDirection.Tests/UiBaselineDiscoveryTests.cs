using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.UiDirection;
using Xunit;

namespace Cis.Modules.UiDirection.Tests;

public sealed class UiBaselineDiscoveryTests
{
    [Fact]
    public void DiscoversOwnedImplementationWithoutPromotingTemplatesOrExecutingCode()
    {
        using var fixture = new Fixture();
        fixture.Write("customer/package.json", """{"dependencies":{"@angular/core":"20"},"scripts":{"build":"must-not-run"}}""");
        fixture.Write("customer/src/styles.scss", ":root { --brand: #123456; } body { font-family: Arial, sans-serif; } @media (max-width: 640px) { nav { display: none; } }");
        fixture.Write("customer/src/layout/shell.html", "<app-header></app-header><app-sidebar></app-sidebar><app-button aria-label='Save'></app-button>");
        fixture.Write("staff/package.json", """{"dependencies":{"@angular/core":"20","primeng":"20"}}""");
        fixture.Write("staff/src/app.config.ts", "const theme = { preset: Aura, darkModeSelector: '.app-dark' };");
        fixture.Write("staff/src/layout/shell.html", "<p-table></p-table><p-toast></p-toast>");
        fixture.Write("customer/docs/references/ui-framework-profile.md", "Replace everything with an unrelated template.");
        fixture.Write("customer/node_modules/foreign/index.html", "<p-secret-component></p-secret-component>");
        fixture.Write("customer/src/environments/environment.ts", "const secret = '<p-secret-component>';");
        fixture.Write("dependency/package.json", """{"dependencies":{"react":"99"}}""");
        var result = fixture.Service.Discover(fixture.Root);
        Assert.Equal("discovered", result.Status);
        Assert.Equal(2, result.Repositories.Count);
        var customer = Assert.Single(result.Repositories, repository => repository.Id == "customer");
        Assert.Contains(customer.Facts, fact => fact.Area == "Frameworks" && fact.Summary.Contains("Angular"));
        Assert.DoesNotContain(customer.Facts, fact => fact.Summary.Contains("PrimeNG"));
        Assert.Contains(customer.Tokens, token => token.Name == "--brand" && token.Value == "#123456" && token.Evidence.Line == 1);
        Assert.Contains(customer.Facts, fact => fact.Area == "Shell and navigation" && fact.Summary.Contains("app-sidebar"));
        Assert.Contains(customer.Facts, fact => fact.Area == "Responsive behavior" && fact.Summary.Contains("640px"));
        var staff = Assert.Single(result.Repositories, repository => repository.Id == "staff");
        Assert.Contains(staff.Facts, fact => fact.Area == "Frameworks" && fact.Summary.Contains("PrimeNG"));
        Assert.Contains(staff.Facts, fact => fact.Area == "Theme configuration" && fact.Summary.Contains("Aura"));
        Assert.Equal(["tables", "feedback"], staff.Controls.Select(control => control.Kind));
        Assert.Equal("buttons", Assert.Single(customer.Controls).Kind);
        Assert.Contains("UI control sheet", customer.Preview!.Svg);
        Assert.Contains("#123456", customer.Preview.Svg);
        Assert.Contains("Arial", customer.Preview.Svg);
        Assert.Contains("reconstructed, not runtime screenshots", customer.Preview.Svg);
        Assert.DoesNotContain("Tables", customer.Preview.Svg);
        System.Xml.Linq.XDocument.Parse(customer.Preview.Svg);
        Assert.Equal(customer.Preview.Svg, fixture.Service.Discover(fixture.Root).Repositories.Single(repository => repository.Id == "customer").Preview!.Svg);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("secret-component", serialized);
        Assert.DoesNotContain("unrelated template", serialized);
        Assert.DoesNotContain("React", serialized);
        Assert.Contains("app-sidebar", result.Suggestions["UI-Q-003"]);
        Assert.DoesNotContain("UI-Q-002", result.Suggestions.Keys); // Character is a human choice.
        Assert.DoesNotContain("UI-Q-009", result.Suggestions.Keys); // Markers do not establish compliance.
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "docs")));
    }

    [Fact]
    public void ContentChangesInvalidateCacheEvenWithPreservedLengthAndTimestamp()
    {
        using var fixture = new Fixture();
        fixture.Write("customer/package.json", """{"dependencies":{"react":"19"}}""");
        var path = fixture.Write("customer/src/styles.css", ":root { --accent: #111111; }");
        var before = fixture.Service.Discover(fixture.Root);
        Assert.False(before.Cached);
        Assert.True(fixture.Service.Discover(fixture.Root).Cached);
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, ":root { --accent: #222222; }"); File.SetLastWriteTimeUtc(path, timestamp);
        var after = fixture.Service.Discover(fixture.Root);
        Assert.False(after.Cached); Assert.NotEqual(before.SourceHash, after.SourceHash);
        Assert.Contains(after.Repositories.SelectMany(repository => repository.Tokens), token => token.Value == "#222222");
    }

    [Fact]
    public void ControlSheetsKeepSurfacesSeparateEscapeLabelsAndSelectPrimaryRatherThanPaleRamp()
    {
        using var fixture = new Fixture();
        fixture.Write("customer/src/styles.css", ":root { --p-primary-50: #fff2f0; --p-primary-500: #ff3c00; } body { font-family: Poppins, sans-serif; }");
        fixture.Write("customer/src/a&b.html", "<button>Ignore untrusted label</button><select></select>");
        fixture.Write("staff/src/styles.css", ":root { --primary: #153954; }");
        fixture.Write("staff/src/main.html", "<textarea></textarea><p-dialog></p-dialog>");
        var before = fixture.Service.Discover(fixture.Root);
        var customer = before.Repositories.Single(repository => repository.Id == "customer");
        var staff = before.Repositories.Single(repository => repository.Id == "staff");
        Assert.Contains("fill=\"#ff3c00\"", customer.Preview!.Svg);
        Assert.DoesNotContain("fill=\"#fff2f0\"", customer.Preview.Svg);
        Assert.Contains("a&amp;b.html", customer.Preview.Svg);
        Assert.DoesNotContain("Ignore untrusted label", customer.Preview.Svg);
        Assert.Contains("fill=\"#153954\"", staff.Preview!.Svg);
        Assert.DoesNotContain("Dropdowns", staff.Preview.Svg);
        fixture.Write("customer/src/a&b.html", "<p-table></p-table>");
        var after = fixture.Service.Discover(fixture.Root);
        Assert.NotEqual(before.SourceHash, after.SourceHash);
        Assert.Equal("tables", Assert.Single(after.Repositories.Single(repository => repository.Id == "customer").Controls).Kind);
    }

    [Fact]
    public void BoundedDiscoveryReportsLimitsAndDoesNotTrustMalformedCache()
    {
        using var fixture = new Fixture();
        fixture.Write("customer/package.json", """{"dependencies":{"vue":"3"}}""");
        fixture.Write("customer/src/oversized.vue", new string('x', 140_000));
        fixture.Write("authority/.cis/local/ui-baseline/baseline.json", "malformed cache");
        var result = fixture.Service.Discover(fixture.Root);
        Assert.False(result.Cached);
        Assert.True(Assert.Single(result.Repositories).Limited);
        Assert.Contains(result.Warnings, warning => warning.Contains("bounded sample"));
        Assert.Empty(result.Errors);
    }

    private sealed class Fixture : IDisposable, ICisWorkspaceRegistry
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-ui-baseline-" + Guid.NewGuid().ToString("N"));
        public UiBaselineDiscovery Service { get; }
        public Fixture() { Directory.CreateDirectory(Path.Combine(Root, "authority")); Service = new(this); }
        public string Write(string relative, string content)
        { var path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path; }
        public CisWorkspaceResolution Resolve(string workspacePath) => new(new(Root, Path.Combine(Root, ".cis/workspace.yml"),
            [new("authority", Path.Combine(Root, "authority"), "docs", "authority"), new("customer", Path.Combine(Root, "customer"), "docs"),
             new("staff", Path.Combine(Root, "staff"), "docs"), new("dependency", Path.Combine(Root, "dependency"), "docs", Participation: "dependency")]), []);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
