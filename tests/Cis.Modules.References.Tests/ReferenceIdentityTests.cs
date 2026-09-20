using Cis.Abstractions;
using Cis.Modules.References;
using Cis.Modules.Repository;
using System.Diagnostics;

namespace Cis.Modules.References.Tests;

public sealed class ReferenceIdentityTests
{
    [Fact]
    public void Validate_RechecksSharedEvidenceAndKeepsRepositoryScopesSeparate()
    {
        using var fixture = new Fixture();
        fixture.Table("command-dictionary", "Command ID | Status | Evidence | Repository",
            "CMD-A | Draft | src/Feature.ts:1 | product-a",
            "CMD-B | Draft | src/Feature.ts:2 | product-a",
            "CMD-C | Draft | src/Feature.ts | product-b");
        var service = fixture.Service("command-dictionary");
        Assert.Equal(0, service.Validate(fixture.Authority, strict: true).ExitCode);
        var source = Path.Combine(fixture.Root, "product-a/src/Feature.ts");
        File.Delete(source);

        var missing = service.Validate(fixture.Authority, strict: true);

        Assert.Equal(5, missing.ExitCode);
        Assert.Contains(missing.Diagnostics, finding => finding.Code == "CIS-REF-EVIDENCE-001"
            && finding.Message.Contains("product-a", StringComparison.Ordinal));
        Assert.DoesNotContain(missing.Diagnostics, finding => finding.Message.Contains("product-b", StringComparison.Ordinal));
        File.WriteAllText(source, "export const restored = true;");
        Assert.Equal(0, service.Validate(fixture.Authority, strict: true).ExitCode);
    }

    public static TheoryData<string, string, string, string> CompositeRows => new()
    {
        { "data-dictionary", "Entity | Field | Type | Status | Evidence", "Account | balance | decimal \\| null | Draft | src/Feature.ts", "Account | openedAt | Date | Draft | src/Feature.ts" },
        { "workflow-state-dictionary", "Workflow ID | Workflow | State | Status", "WF-ACCOUNT | AccountStatus | Open | Draft", "WF-ACCOUNT | AccountStatus | Closed | Draft" },
        { "erd", "Entity | Relationship | Target | Status | Evidence", "Account | owner | User | Draft | src/Feature.ts", "Account | reviewer | User | Draft | src/Feature.ts" },
        { "configuration-dictionary", "Name | Path | Owner | Status | Evidence", "DB_HOST | DB_HOST | api | Draft | src/Feature.ts", "DB_HOST | DB_HOST | worker | Draft | src/Feature.ts" },
        { "package-catalogue", "Package | Component | Status | Evidence", "@azure/functions | api | Draft | src/Feature.ts", "@azure/functions | worker | Draft | src/Feature.ts" },
        { "screen-route-map", "Screen or route | Component | Platform | Status | Evidence", "/accounts | customer | web | Draft | src/Feature.ts", "/accounts | customer | native | Draft | src/Feature.ts" },
        { "permissions-dictionary", "Permission code | Status | Source location", "PERM-A-B | Draft | src/Feature.ts", "PERM-AB | Draft | src/Feature.ts" },
    };

    [Theory]
    [MemberData(nameof(CompositeRows))]
    public void Validate_UsesCompleteIdentitiesAndStillRejectsExactDuplicates(string kind, string headers, string first, string second)
    {
        using var fixture = new Fixture();
        fixture.Table(kind, headers, first, second);
        var service = fixture.Service(kind);

        Assert.Equal(0, service.Validate(fixture.Authority, strict: true).ExitCode);
        var entries = Assert.Single(service.Inventory(fixture.Authority).Families).CanonicalEntries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Select(entry => entry.CanonicalKey).Distinct().Count());

        fixture.Table(kind, headers, first, second, first);
        var duplicated = service.Validate(fixture.Authority, strict: false);
        Assert.Equal(5, duplicated.ExitCode);
        Assert.Single(duplicated.Diagnostics, diagnostic => diagnostic.Code == "CIS-REF-CANON-001");
    }

    [Fact]
    public void Validate_ResolvesSameIdentitiesAndEvidenceInTheDeclaredOwnedRepository()
    {
        using var fixture = new Fixture();
        const string kind = "command-dictionary";
        fixture.Table(kind, "Command ID | Status | Evidence | Repository",
            "CMD-SAME | Draft | src/Feature.ts:1 | product-a", "CMD-SAME | Draft | src/Feature.ts#handler | product-b");
        var service = fixture.Service(kind);

        Assert.Equal(0, service.Validate(fixture.Authority, strict: true).ExitCode);
        File.Delete(Path.Combine(fixture.Root, "product-a/src/Feature.ts"));
        var missing = service.Validate(fixture.Authority, strict: true);
        Assert.Equal(5, missing.ExitCode);
        var diagnostic = Assert.Single(missing.Diagnostics);
        Assert.Equal("CIS-REF-EVIDENCE-001", diagnostic.Code);
        Assert.Contains("product-a", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-registered")]
    [InlineData("dependency")]
    public void Validate_RejectsBlankUnknownAndDependencyRepositoryScopes(string scope)
    {
        using var fixture = new Fixture();
        fixture.Table("command-dictionary", "Command ID | Status | Evidence | Repository", $"CMD-SAME | Draft | src/Feature.ts | {scope}");
        var result = fixture.Service("command-dictionary").Validate(fixture.Authority, strict: false);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-REF-SCOPE-001");
    }

    [Fact]
    public void Validate_RejectsEvidenceEscapingItsRepositoryEvenWhenTheFileExists()
    {
        using var fixture = new Fixture();
        fixture.Table("command-dictionary", "Command ID | Status | Evidence", "CMD-SAME | Draft | ../product-a/src/Feature.ts");
        var result = fixture.Service("command-dictionary").Validate(fixture.Authority, strict: true);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-REF-EVIDENCE-001");
    }

    [Fact]
    public void Discover_DoesNotMatchLocalSourceToAnotherRepositorysRow()
    {
        using var fixture = new Fixture();
        fixture.Table("command-dictionary", "Command ID | Status | Evidence | Repository", "CMD-SAME | Draft | src/Feature.ts | product-a");
        var service = fixture.Service("command-dictionary", observe: true);
        var result = service.Validate(fixture.Authority, strict: true);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-REF-DRIFT-001");
        Assert.False(Assert.Single(Assert.Single(service.Inventory(fixture.Authority).Families).Observations).CanonicalDeclared);
    }

    [Fact]
    public void Validate_DoesNotClaimForeignCurrentRowsWereSourceCorrelated()
    {
        using var fixture = new Fixture();
        fixture.Table("command-dictionary", "Command ID | Status | Evidence | Repository", "CMD-SAME | Active | src/Feature.ts | product-a");
        var result = fixture.Service("command-dictionary").Validate(fixture.Authority, strict: true);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-REF-SCOPE-002");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-REF-DRIFT-002");
    }

    [Fact]
    public void Validate_DistinguishesClassificationLabelsFromEvidenceLocators()
    {
        using var fixture = new Fixture();
        fixture.Table("module-ownership-map", "Module ID | Status | Evidence", "MOD-WORKER | Draft | src/Feature.ts; @azure/functions package dependency");
        Assert.Equal(0, fixture.Service("module-ownership-map").Validate(fixture.Authority, strict: true).ExitCode);
    }

    [Fact]
    public void Validate_ReportsMalformedRowsInsteadOfSilentlyDroppingThem()
    {
        using var fixture = new Fixture();
        fixture.Table("data-dictionary", "Entity | Field | Status", "Account | balance | Draft | extra");
        var result = fixture.Service("data-dictionary").Validate(fixture.Authority, strict: false);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-REF-CANON-003");
    }

    [Fact]
    public void Diff_ComparesCompositeRowsAndReportsRealDuplicatesWithoutThrowing()
    {
        using var fixture = new Fixture();
        const string headers = "Entity | Field | Status";
        fixture.Table("data-dictionary", headers, "Account | balance | Draft", "Account | openedAt | Draft");
        fixture.Git("init");
        fixture.Git("-c", "user.name=CIS Tests", "-c", "user.email=tests@cis.local", "add", ".");
        fixture.Git("-c", "user.name=CIS Tests", "-c", "user.email=tests@cis.local", "commit", "-m", "baseline");
        fixture.Table("data-dictionary", headers, "Account | balance | Active", "Account | openedAt | Draft");
        var service = fixture.Service("data-dictionary");
        var diff = service.Diff(fixture.Authority, "HEAD");
        Assert.Equal(0, diff.ExitCode);
        Assert.Equal("Account.balance", Assert.Single(diff.Changes).Identity);
        fixture.Table("data-dictionary", headers, "Account | balance | Draft", "Account | balance | Draft");
        Assert.Equal(5, service.Diff(fixture.Authority, "HEAD").ExitCode);
    }

    [Fact]
    public void Reconcile_KeepsRepositoryColumnWhenAddingLocalRowsToScopedTables()
    {
        using var fixture = new Fixture();
        fixture.Table("configuration-dictionary", "Name | Path | Allowed values | Refresh class | Owner | Sensitive | Description | Status | Evidence | Repository",
            "DB_HOST | DB_HOST | string | startup | worker | no | Host | Draft | src/Feature.ts | product-a");
        var service = fixture.Service("configuration-dictionary", observe: true);
        Assert.Equal(0, service.Reconcile(fixture.Authority, ["configuration-dictionary"], yes: true).ExitCode);
        var entries = Assert.Single(service.Inventory(fixture.Authority).Families).CanonicalEntries;
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.RepositoryId == "authority");
        Assert.Contains(entries, entry => entry.RepositoryId == "product-a");
        Assert.Equal(0, service.Validate(fixture.Authority, strict: true).ExitCode);
    }

    [Fact]
    public void Validate_RejectsOldCachedIdentityRulesUntilStateIsRefreshed()
    {
        using var fixture = new Fixture();
        fixture.Table("data-dictionary", "Entity | Field | Status", "Account | balance | Draft");
        var service = fixture.Service("data-dictionary");
        service.Discover(fixture.Authority);
        var path = Path.Combine(fixture.Authority, ".cis/local/references/inventory.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1", StringComparison.Ordinal));
        Assert.NotEqual(0, service.Validate(fixture.Authority, strict: false, refresh: false).ExitCode);
        Assert.Equal(0, service.Validate(fixture.Authority, strict: true).ExitCode);
    }

    private sealed class Provider(string kind, bool observe) : ICisReferenceProvider
    {
        public string Kind => kind;
        public string CanonicalFileName => kind + ".md";
        public string Description => "Fixture source declarations";
        public bool Supports(string path) => observe && path == "src/Feature.ts";
        public IReadOnlyList<CisReferenceObservation> Discover(CisReferenceDiscoveryContext context, CisReferenceSourceFile source)
            => [new(kind, "CMD-SAME", "CMD-SAME", source.RelativePath, 1, [])];
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Combine(Path.GetTempPath(), "cis-reference-scope-tests");
        public string Root { get; } = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        public string Authority => Path.Combine(Root, "authority");

        public Fixture()
        {
            foreach (var id in new[] { "authority", "product-a", "product-b", "dependency" })
            {
                Write(Path.Combine(Root, id, ".cis/repository.yml"), $"schema_version: 1\nrepository:\n  id: {id}\ndocumentation_root: docs/cis\n");
                Write(Path.Combine(Root, id, "docs/cis/catalog.yml"), $"schema_version: 1\nrepository: {id}\ndocuments: []\n");
                Write(Path.Combine(Root, id, "src/Feature.ts"), "export const feature = true;\n");
            }
            Write(Path.Combine(Authority, ".cis/workspace.yml"), "schema_version: 2\necosystem:\n  id: fixture\n  name: Fixture\nproduct:\n  id: fixture\n  name: Fixture\nrepositories:\n" +
                string.Concat(new[] { "authority", "product-a", "product-b", "dependency" }.Select(id =>
                    $"- id: {id}\n  path: ../{id}\n  documentation_root: docs/cis\n  role: {(id == "authority" ? "authority" : "participant")}\n  participation: {(id == "dependency" ? "dependency" : "owned")}\n  relationship: {(id == "dependency" ? "producer" : "none")}\n  components: []\n")));
        }

        public ReferenceGovernanceService Service(string kind, bool observe = false) => new(new CisRepositoryContextResolver(), [new Provider(kind, observe)]);
        public void Table(string kind, string headers, params string[] rows)
            => Write(Path.Combine(Authority, "docs/cis/references", kind + ".md"),
                $"# {kind}\n\n| {headers} |\n| {string.Join(" | ", Enumerable.Repeat("---", headers.Split('|').Length))} |\n" +
                string.Concat(rows.Select(row => $"| {row} |\n")));
        public void Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = Authority };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            Assert.Equal(0, CisProcessSafety.Run(start, TimeSpan.FromSeconds(30)).ExitCode);
        }
        private static void Write(string path, string content) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Parent, Root)) throw new InvalidOperationException("Unsafe fixture path.");
            foreach (var file in CisPathSafety.EnumerateFiles(Root))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(Root, recursive: true);
        }
    }
}
