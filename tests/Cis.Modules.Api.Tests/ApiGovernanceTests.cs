using Cis.Modules.Api;
using Cis.Modules.Repository;
using Cis.Host;

namespace Cis.Modules.Api.Tests;

public sealed class ApiGovernanceTests
{
    [Fact]
    public void ApiCommands_AreRegisteredByTheExplicitModule()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new ApiModule())
            .Build();

        Assert.Equal(0, application.Invoke(["api", "discover", "--help"]));
        Assert.Equal(0, application.Invoke(["api", "inventory", "--help"]));
        Assert.Equal(0, application.Invoke(["api", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["api", "diff", "--help"]));
    }

    [Fact]
    public void ApiCli_CompletesDiscoverInventoryValidateAndCompatibleDiffEndToEnd()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new ApiModule())
            .Build();

        Assert.Equal(0, application.Invoke(["api", "discover", "--repo", repository.Path, "--format", "agent"]));
        Assert.Equal(0, application.Invoke(["api", "inventory", "--repo", repository.Path, "--exposure", "public", "--format", "agent"]));
        Assert.Equal(0, application.Invoke(["api", "validate", "--repo", repository.Path, "--strict", "--format", "agent"]));
        Assert.Equal(0, application.Invoke(["api", "diff", "--repo", repository.Path, "--format", "agent"]));
    }

    [Fact]
    public void Discover_CorrelatesDictionarySourceAndOpenApiIdempotently()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var first = service.Discover(repository.Path);
        var second = service.Discover(repository.Path);

        Assert.Equal(3, first.Operations);
        Assert.Equal(3, first.SourceOperations);
        Assert.Equal(3, first.DictionaryOperations);
        Assert.Equal(3, first.OpenApiOperations);
        Assert.True(first.Applied);
        Assert.False(second.Applied);
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "api", "operations.json")));
        Assert.Contains(first.Items, item => item.ApiId == "API-CATALOG-GET" && item.Exposure == "public" && item.OpenApiOperation == "getCatalog");
    }

    [Fact]
    public void Discover_PreservesExplicitAuthenticationDecisionFromControllerAttributes()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        repository.Write("src/ProfileController.cs", """
            [Authorize]
            public sealed class ProfileController
            {
                [HttpGet("/profile")]
                public void Get() { }

                [Authorize("RequireInteractiveUser")]
                [HttpPost("/profile")]
                public void Update() { }
            }
            """);
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Discover(repository.Path);

        Assert.Contains(result.Items, item => item.Method == "GET" && item.Path == "/profile"
            && item.Exposure == "customer" && item.Permission == "authenticated");
        Assert.Contains(result.Items, item => item.Method == "POST" && item.Path == "/profile"
            && item.Exposure == "customer" && item.Permission == "RequireInteractiveUser");
    }

    [Fact]
    public void Discover_FindsExpressRoutesAndExcludesTestOnlyRoutes()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        repository.Write("src/app.ts", "const app = express(); app.get('/health/live', (_request, response) => response.sendStatus(200));");
        repository.Write("test/app.test.ts", "const app = express(); app.post('/test-only', (_request, response) => response.sendStatus(201));");
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Discover(repository.Path);

        Assert.Contains(result.Items, item => item.Method == "GET" && item.Path == "/health/live" && item.SourceDiscovered);
        Assert.DoesNotContain(result.Items, item => item.Path == "/test-only");
    }

    [Fact]
    public void Discover_NormalizesFrameworkRouteParametersToCanonicalOpenApiSyntax()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        repository.Write("src/ListRoutes.ts", "app.get('/api/v1/lists/:listId', (_request, response) => response.sendStatus(200));");
        var dictionary = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md");
        File.AppendAllText(dictionary, "\n| API-LIST-GET | v1 | GET | /api/v1/lists/{listId} | list | api | customer | external:web | none | ListResponse | owner | getList | session | bounded | safe GET | one | unavailable | private, no-store | application service | Planned | src/ListRoutes.ts | tests/ListRoutesTests.ts | canonical parameter |\n");
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Discover(repository.Path);

        var operation = Assert.Single(result.Items, item => item.ApiId == "API-LIST-GET");
        Assert.Equal("/api/v1/lists/{listId}", operation.Path);
        Assert.True(operation.SourceDiscovered);
    }

    [Fact]
    public void Discover_DoesNotLoadPlannedOpenApiDocuments()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var profilePath = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-governance-profile.md");
        File.WriteAllText(profilePath, File.ReadAllText(profilePath).Replace(
            "| current | docs/openapi/current.json | api | Active | fixture |",
            "| current | docs/openapi/future.json | api | Planned | generated by later work |",
            StringComparison.Ordinal));
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Discover(repository.Path);

        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "CIS-API-OAS-002");
    }

    [Fact]
    public void Validate_RejectsPublicEndpointWithDirectPersistence()
    {
        using var repository = ApiRepository.Create(validPublic: false);
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Validate(repository.Path, strict: true);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-API-PUBLIC-003" && item.Rule == "API-PUB-01");
        Assert.All(result.Diagnostics, item => Assert.False(string.IsNullOrWhiteSpace(item.Remediation)));
    }

    [Fact]
    public void Validate_NormalizesMultiPathDictionaryEvidenceInDiagnostics()
    {
        using var repository = ApiRepository.Create(validPublic: false);
        var dictionary = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md");
        File.WriteAllText(dictionary, File.ReadAllText(dictionary).Replace(
            "| src/PublicEndpoints.cs | tests/ApiContractTests.cs | cache-only route |",
            "| src/PublicEndpoints.cs; src/CustomerEndpoints.cs | tests/ApiContractTests.cs | cache-only route |",
            StringComparison.Ordinal));
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Validate(repository.Path, strict: true);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "CIS-API-PUBLIC-003");
        Assert.Contains("src/PublicEndpoints.cs", diagnostic.Evidence);
        Assert.Contains("src/CustomerEndpoints.cs", diagnostic.Evidence);
        Assert.DoesNotContain(diagnostic.Evidence, item => item.Contains(';', StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsAlignedCustomerBackofficeAndCachedPublicContracts()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Validate(repository.Path, strict: true);

        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Warnings);
    }

    [Fact]
    public void Validate_AcceptsNoStoreIdentityProtocolAndNamedExternalConsumer()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var dictionary = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md");
        File.WriteAllText(dictionary, File.ReadAllText(dictionary)
            .Replace("| public | anonymous |", "| identity-protocol | external:customer-web |", StringComparison.Ordinal)
            .Replace("| public-catalog: key=path; ttl=60; vary=accept; background refresh |", "| no-store |", StringComparison.Ordinal));
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Validate(repository.Path, strict: true);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code.StartsWith("CIS-API-IDP-", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "CIS-API-CONS-001");
    }

    [Fact]
    public void Validate_AcceptsVersionPinnedSdkOwnedDynamicSurfaceWithImplementationEvidence()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        repository.Write("src/SdkAuth.ts", "app.use(sdkAuthenticationMiddleware());\n");
        var dictionary = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md");
        File.AppendAllText(dictionary, "\n| API-SDK-AUTH | sdk-pinned | ANY | /auth/* | authentication | api | identity-protocol | external:customer-web | pinned recipe | pinned recipe | verified session | SDK-owned | verified session state | sdk-protocol | SDK-governed | single response | PROB-ORDERS-FORBIDDEN | no-store | SDK middleware to identity service | Active | src/SdkAuth.ts | tests/ApiContractTests.cs | dynamic SDK route family |\n");
        var profile = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-governance-profile.md");
        File.AppendAllText(profile, "\n| authentication | sdk-pinned | Active | none | external:customer-web | fixture |\n");
        var current = System.IO.Path.Combine(repository.Path, "docs", "openapi", "current.json");
        File.WriteAllText(current, File.ReadAllText(current).Replace(
            "\"paths\": {",
            "\"x-sdk-owned-surfaces\": [{\"pathPrefix\":\"/auth\",\"owner\":\"identity-sdk@1.2.3\",\"recipes\":[\"session\"],\"implementationEvidence\":[\"src/SdkAuth.ts\"]}], \"paths\": {",
            StringComparison.Ordinal));
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Validate(repository.Path, strict: true);

        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        Assert.DoesNotContain(result.Diagnostics, item => item.Code is "CIS-API-OAS-001" or "CIS-API-SOURCE-001" or "CIS-API-OAS-004");
    }

    [Fact]
    public void Validate_RejectsUnknownConsumersAndMissingVerifiedTests()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var dictionary = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md");
        File.WriteAllText(dictionary, File.ReadAllText(dictionary).Replace("customer-web", "ghost-portal", StringComparison.Ordinal));
        var profile = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "repository-profile.md");
        File.WriteAllText(profile, "# Repository Profile\n\n### backoffice-web\n\n- Root: `apps/backoffice`\n");
        File.Delete(System.IO.Path.Combine(repository.Path, "tests", "ApiContractTests.cs"));
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Validate(repository.Path, strict: true);

        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-API-CONS-001" && item.Message.Contains("ghost-portal", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-API-TEST-001" && item.Message.Contains("/catalog/v1/items", StringComparison.Ordinal));
    }

    [Fact]
    public void Diff_DetectsBreakingRequestResponseSecurityAndEnumChanges()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var service = new OpenApiCompatibilityService(new CisRepositoryContextResolver());

        var result = service.Diff(repository.Path, "docs/openapi/baseline.json", "docs/openapi/breaking.json");

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Findings, item => item.Classification == "operation-removed");
        Assert.Contains(result.Findings, item => item.Classification == "request-required-added");
        Assert.Contains(result.Findings, item => item.Classification == "response-property-removed");
        Assert.Contains(result.Findings, item => item.Classification == "security-changed");
        Assert.Contains(result.Findings, item => item.Classification == "enum-value-removed");
    }

    [Fact]
    public void Diff_UsesEveryConfiguredBaselineForForwardTransitiveCompatibility()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        repository.Write("docs/openapi/legacy.json", """
            {
              "openapi": "3.0.1",
              "paths": {
                "/legacy/v1/items": {
                  "get": {
                    "responses": { "200": { "description": "ok" } }
                  }
                }
              }
            }
            """);
        var profilePath = System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "api-governance-profile.md");
        var profile = File.ReadAllText(profilePath).Replace(
            "| baseline | docs/openapi/baseline.json | api | Active | fixture |",
            "| baseline | docs/openapi/baseline.json | api | Active | fixture |\n| baseline | docs/openapi/legacy.json | api | Active | supported legacy v1 |",
            StringComparison.Ordinal);
        File.WriteAllText(profilePath, profile);
        var service = new OpenApiCompatibilityService(new CisRepositoryContextResolver());

        var result = service.Diff(repository.Path, baselinePath: null, currentPath: null);

        Assert.Equal(5, result.ExitCode);
        Assert.Equal(2, result.Comparisons.Count);
        Assert.Contains(result.Comparisons, item => item.Baseline == "docs/openapi/baseline.json" && item.Status == "compatible");
        Assert.Contains(result.Comparisons, item => item.Baseline == "docs/openapi/legacy.json" && item.Status == "breaking");
        Assert.Contains(result.Findings, item => item.Classification == "operation-removed"
            && item.Evidence.Contains("baseline:docs/openapi/legacy.json"));
    }

    [Fact]
    public void Inventory_FiltersDerivedStateWithoutRescanning()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());
        service.Discover(repository.Path);

        var result = service.Inventory(repository.Path, exposure: "backoffice", module: "orders");

        var operation = Assert.Single(result.Items);
        Assert.Equal("API-ADMIN-GET", operation.ApiId);
    }

    [Fact]
    public void ValidateWithoutRefresh_RejectsMissingNormalizedState()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        var service = new ApiGovernanceService(new CisRepositoryContextResolver());

        var result = service.Validate(repository.Path, strict: true, refresh: false);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-API-STATE-001");
    }

    private sealed class ApiRepository : IDisposable
    {
        public string Path { get; }
        private ApiRepository(string path) => Path = path;

        public static ApiRepository Create(bool validPublic)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-api-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Write(root, ".cis/repository.yml", "schema_version: 1\nrepository:\n  id: api-fixture\ndocumentation_root: docs/cis\n");
            Directory.CreateDirectory(System.IO.Path.Combine(root, "docs", "cis", "references"));
            Write(root, "docs/cis/references/api-governance-profile.md", Profile());
            Write(root, "docs/cis/references/api-dictionary.md", Dictionary());
            Write(root, "docs/cis/references/permissions-dictionary.md", Permissions());
            Write(root, "docs/cis/references/problem-details-catalogue.md", Problems());
            Write(root, "src/PublicEndpoints.cs", PublicSource(validPublic));
            Write(root, "src/CustomerEndpoints.cs", "app.MapGet(\"/orders/v1/orders/{id}\", [Authorize(Policy = \"PERM-ORDERS-READ\")] [EnableRateLimiting(\"customer-read\")] () => Results.Ok());\n");
            Write(root, "src/AdminEndpoints.cs", "app.MapGet(\"/orders/v1/admin/orders\", [Authorize(Policy = \"PERM-ORDERS-ADMIN\")] [EnableRateLimiting(\"admin-read\")] () => Results.Ok());\n");
            Write(root, "tests/ApiContractTests.cs", "// API-CATALOG-GET API-ORDER-GET API-ADMIN-GET positive negative authorization cache contracts\n");
            Write(root, "docs/openapi/current.json", CurrentOpenApi());
            Write(root, "docs/openapi/baseline.json", BaselineOpenApi());
            Write(root, "docs/openapi/breaking.json", BreakingOpenApi());
            return new ApiRepository(root);
        }

        public void Write(string relative, string content) => Write(Path, relative, content);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }

        private static void Write(string root, string relative, string content)
        {
            var path = System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static string Profile() => """
            # API Governance Profile
            | Setting | Value | Status | Rationale |
            |---|---|---|---|
            | route-template | module/version/resource | Active | fixture |
            | compatibility-mode | forward-transitive | Active | fixture |
            | deprecation-window-days | 180 | Active | fixture |
            | production-openapi-exposure | restricted | Active | fixture |
            | correlation-header | x-correlation-id | Active | fixture |
            | client-header | x-client-id | Active | fixture |

            | Document role | Path | Host / deployment | Status | Evidence |
            |---|---|---|---|---|
            | current | docs/openapi/current.json | api | Active | fixture |
            | baseline | docs/openapi/baseline.json | api | Active | fixture |

            | Module | Major version | Status | Sunset | Consumers | Evidence |
            |---|---|---|---|---|---|
            | orders | v1 | Active | none | customer-web; backoffice-web; anonymous | fixture |
            """;

        private static string Dictionary() => """
            # API Dictionary
            | API ID | Version | Method | Path | Module | Host | Exposure | Consumer | Request contract | Response contract | Permission / auth | OpenAPI operation | Trusted scope source | Rate-limit policy | Idempotency / concurrency | Collection semantics | Error contract | Cache policy | Data access path | Status | Evidence | Known tests | Notes |
            |---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
            | API-CATALOG-GET | v1 | GET | /catalog/v1/items | orders | api | public | anonymous | no-body | CatalogResponse | anonymous-approved | getCatalog | anonymous | public-read | GET idempotent | stable cursor; max 100; filters do not widen scope | PROB-ORDERS-NOT-FOUND | public-catalog: key=path; ttl=60; vary=accept; background refresh | background cache populator from materialized catalogue | Verified | src/PublicEndpoints.cs | tests/ApiContractTests.cs | cache-only route |
            | API-ORDER-GET | v1 | GET | /orders/v1/orders/{id} | orders | api | customer | customer-web | no-body | OrderResponse | PERM-ORDERS-READ | getOrder | identity.customer_id | customer-read | GET idempotent | single resource | PROB-ORDERS-NOT-FOUND | private no-store | application service to repository | Implemented | src/CustomerEndpoints.cs | tests/ApiContractTests.cs | trusted identity scope |
            | API-ADMIN-GET | v1 | GET | /orders/v1/admin/orders | orders | api | backoffice | backoffice-web | no-body | OrderListResponse | PERM-ORDERS-ADMIN | getAdminOrders | identity.staff_id | admin-read | GET idempotent | stable cursor; max 100 | PROB-ORDERS-FORBIDDEN | private no-store | application service to repository | Implemented | src/AdminEndpoints.cs | tests/ApiContractTests.cs | admin policy |
            """;

        private static string Permissions() => """
            | Permission code | Permission name |
            |---|---|
            | PERM-ORDERS-READ | Read orders |
            | PERM-ORDERS-ADMIN | Administer orders |
            """;

        private static string Problems() => """
            | Problem ID | Problem type / reason |
            |---|---|
            | PROB-ORDERS-NOT-FOUND | order-not-found |
            | PROB-ORDERS-FORBIDDEN | order-forbidden |
            """;

        private static string PublicSource(bool valid) => valid
            ? "app.MapGet(\"/catalog/v1/items\", [AllowAnonymous] [EnableRateLimiting(\"public-read\")] () => Results.Ok()).CacheOutput(\"public-catalog\");\n"
            : "app.MapGet(\"/catalog/v1/items\", [AllowAnonymous] [EnableRateLimiting(\"public-read\")] (OrdersDbContext db) => db.Items).CacheOutput(\"public-catalog\");\n";

        private static string CurrentOpenApi() => BaselineOpenApi();
        private static string BaselineOpenApi() => """
            {
              "openapi": "3.0.3",
              "security": [{"bearer": []}],
              "paths": {
                "/catalog/v1/items": {
                  "get": {
                    "operationId": "getCatalog",
                    "security": [],
                    "responses": {"200": {"description": "ok", "content": {"application/json": {"schema": {"type": "object", "properties": {"items": {"type": "array"}, "state": {"type": "string", "enum": ["active", "archived"]}}}}}}}
                  }
                },
                "/orders/v1/orders/{id}": {
                  "get": {
                    "operationId": "getOrder",
                    "parameters": [{"name": "id", "in": "path", "required": true}],
                    "requestBody": {"content": {"application/json": {"schema": {"type": "object", "required": ["id"], "properties": {"id": {"type": "string"}, "filter": {"type": "string"}}}}}},
                    "responses": {
                      "200": {"description": "ok", "content": {"application/json": {"schema": {"type": "object", "properties": {"id": {"type": "string"}, "name": {"type": ["string", "null"]}}}}}},
                      "404": {"description": "missing"}
                    }
                  }
                },
                "/orders/v1/admin/orders": {
                  "get": {
                    "operationId": "getAdminOrders",
                    "responses": {"200": {"description": "ok", "content": {"application/json": {"schema": {"type": "object", "properties": {"items": {"type": "array"}}}}}}}
                  }
                }
              }
            }
            """;

        private static string BreakingOpenApi() => """
            {
              "openapi": "3.0.3",
              "paths": {
                "/catalog/v1/items": {
                  "get": {
                    "operationId": "getCatalog",
                    "security": [],
                    "responses": {"200": {"description": "ok", "content": {"application/json": {"schema": {"type": "object", "properties": {"state": {"type": "string", "enum": ["active"]}}}}}}}
                  }
                },
                "/orders/v1/orders/{id}": {
                  "get": {
                    "security": [],
                    "parameters": [{"name": "id", "in": "path", "required": true}, {"name": "x-client-id", "in": "header", "required": true}],
                    "requestBody": {"content": {"application/json": {"schema": {"type": "object", "required": ["id", "filter"], "properties": {"id": {"type": "string"}, "filter": {"type": "string"}}}}}},
                    "responses": {"200": {"description": "ok", "content": {"application/json": {"schema": {"type": "object", "properties": {"id": {"type": "string"}}}}}}}
                  }
                }
              }
            }
            """;
    }
}
