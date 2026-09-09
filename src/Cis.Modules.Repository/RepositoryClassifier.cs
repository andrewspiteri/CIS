using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed partial class RepositoryClassifier
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cis",
        ".codex-tmp",
        ".git",
        ".idea",
        ".next",
        ".nuxt",
        ".output",
        ".svelte-kit",
        ".stryker-tmp",
        ".vs",
        "artifacts",
        "bin",
        "coverage",
        "dist",
        "node_modules",
        "obj",
        "skills-quarantine",
        "_old",
        "build_out",
        "nongit",
    };

    public RepositoryClassification Classify(string repositoryPath)
        => CisReadScope.Read(typeof(RepositoryClassifier), nameof(Classify), repositoryPath,
            () => ClassifyCore(repositoryPath));

    private static RepositoryClassification ClassifyCore(string repositoryPath)
    {
        var components = new List<RepositoryComponentClassification>();
        var warnings = new List<string>();
        var files = EnumerateRepositoryFiles(repositoryPath, warnings).ToArray();

        var csharpProjects = files
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var projectPath in csharpProjects.Where(path => !IsLikelyBackupProject(path, csharpProjects)))
        {
            var component = ClassifyCSharpProject(repositoryPath, projectPath, warnings);
            if (component is not null)
            {
                components.Add(component);
            }
        }

        foreach (var angularPath in files
                     .Where(path => string.Equals(
                         Path.GetFileName(path),
                         "angular.json",
                         StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            components.AddRange(ClassifyAngularWorkspace(repositoryPath, angularPath, warnings));
        }

        foreach (var packagePath in files
                     .Where(path => string.Equals(
                         Path.GetFileName(path),
                         "package.json",
                         StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var component = ClassifyWebPackage(repositoryPath, packagePath, warnings);
            if (component is not null)
            {
                components.Add(component);
            }
        }

        components.AddRange(ClassifySwiftProjects(repositoryPath, files, warnings));
        components.AddRange(ClassifyKotlinProjects(repositoryPath, files, warnings));
        MergeGodotProjects(components, ClassifyGodotProjects(repositoryPath, files, warnings));
        components.AddRange(ClassifyLooseSourceAreas(repositoryPath, files, components));

        var terraformFiles = files
            .Where(path => path.EndsWith(".tf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var composeFiles = files
            .Where(path => IsComposeFile(Path.GetFileName(path)))
            .ToArray();
        var environmentExamples = files
            .Where(path => IsEnvironmentExample(Path.GetFileName(path)))
            .ToArray();
        if (terraformFiles.Length > 0 || composeFiles.Length > 0)
        {
            var languages = terraformFiles.Length > 0 ? new List<string> { "hcl" } : [];
            var frameworks = terraformFiles.Length > 0 ? new List<string> { "terraform" } : [];
            var capabilities = new List<string> { "deployment" };
            if (composeFiles.Length > 0)
            {
                languages.Add("yaml");
                frameworks.Add("docker-compose");
            }

            if (composeFiles.Length > 0 || environmentExamples.Length > 0)
            {
                capabilities.Add("configuration");
            }

            components.Add(new RepositoryComponentClassification(
                "infrastructure",
                ".",
                languages,
                frameworks,
                ["infrastructure"],
                capabilities,
                "high",
                terraformFiles
                    .Concat(composeFiles)
                    .Concat(environmentExamples)
                    .Take(5)
                    .Select(path => ToRepositoryPath(repositoryPath, path))
                    .ToArray()));
        }

        var uniqueComponents = components
            .GroupBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Count() == 1
                ? group
                : group.Select(component => component with
                {
                    Id = CreateId($"{component.Id}-{component.Root}"),
                }))
            .OrderBy(component => component.Root, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var shape = uniqueComponents.Length > 1
            ? "monorepo"
            : uniqueComponents.Any(component => component.Roles.Contains("package-producer", StringComparer.Ordinal))
                ? "library"
                : uniqueComponents.Length == 1
                    ? "application"
                    : "unclassified";

        return new RepositoryClassification(shape, uniqueComponents, warnings);
    }

    private static bool IsComposeFile(string name)
        => name.Equals("compose.yaml", StringComparison.OrdinalIgnoreCase)
           || name.Equals("compose.yml", StringComparison.OrdinalIgnoreCase)
           || name.Equals("docker-compose.yaml", StringComparison.OrdinalIgnoreCase)
           || name.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnvironmentExample(string name)
        => name.Equals(".env.example", StringComparison.OrdinalIgnoreCase)
           || name.Equals(".env.sample", StringComparison.OrdinalIgnoreCase)
           || name.Equals(".env.template", StringComparison.OrdinalIgnoreCase)
           || name.Equals("example.env", StringComparison.OrdinalIgnoreCase);

    private static RepositoryComponentClassification? ClassifyCSharpProject(
        string repositoryPath,
        string projectPath,
        ICollection<string> warnings)
    {
        XDocument project;
        try
        {
            project = XDocument.Load(projectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            warnings.Add($"Unable to inspect {ToRepositoryPath(repositoryPath, projectPath)}: {exception.Message}");
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var projectText = project.ToString(SaveOptions.DisableFormatting);
        var source = ReadSourceText(projectDirectory, ".cs", warnings);
        var combined = projectText + Environment.NewLine + source;
        var sdk = project.Root?.Attribute("Sdk")?.Value ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(projectPath);
        var frameworks = new List<string>();
        var roles = new List<string>();
        var capabilities = new List<string> { "configuration" };
        var evidence = new List<string> { ToRepositoryPath(repositoryPath, projectPath) };

        var isTest = ContainsAny(projectText, "Microsoft.NET.Test.Sdk", "<IsTestProject>true", "xunit", "NUnit");
        var isWorker = sdk.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase)
            || projectText.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase);
        var packageReferences = project.Descendants()
            .Where(element => element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isBlazorWebAssembly = sdk.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase)
            || packageReferences.Contains("Microsoft.AspNetCore.Components.WebAssembly");
        var isWeb = sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
            || projectText.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase);
        var isApi = isWeb && ContainsAny(combined, "MapControllers", "AddControllers", "[ApiController", "MapGet(", "MapPost(");
        var isPackage = ContainsAny(projectText, "<GeneratePackageOnBuild>true", "<IsPackable>true", "<PackageId>");
        var isExecutable = projectText.Contains("<OutputType>Exe", StringComparison.OrdinalIgnoreCase);

        if (isTest)
        {
            frameworks.Add("dotnet-test");
            roles.Add("test-automation");
            evidence.Add(".NET test SDK markers");
        }
        else if (isBlazorWebAssembly)
        {
            frameworks.Add("blazor-webassembly");
            roles.Add("frontend-consumer");
            capabilities.Add("web-ui");
            evidence.Add("Blazor WebAssembly SDK or package markers");
        }
        else if (isWeb)
        {
            frameworks.Add("aspnet-core");
            roles.Add(isApi ? "backend-api-producer" : "backend-web");
            evidence.Add("ASP.NET Core Web SDK or host markers");
        }
        else if (isWorker)
        {
            frameworks.Add("dotnet-worker");
            roles.Add("worker");
            evidence.Add(".NET worker or hosted-service markers");
        }
        else
        {
            frameworks.Add("dotnet");
            roles.Add(isPackage ? "package-producer" : isExecutable ? "tooling" : "shared-library");
        }

        if (isApi && ContainsAny(combined, "AddOpenApi", "UseSwagger", "SwaggerGen", "OpenApi"))
        {
            capabilities.Add("openapi");
            evidence.Add("OpenAPI or Swagger markers");
        }

        if (isWeb && ContainsAny(source, "AddAuthorization", "[Authorize", "AuthorizationPolicy", "Permission"))
        {
            capabilities.Add("authorization");
            evidence.Add("authorization or permission markers");
        }

        var hasMessaging = ContainsAny(
            projectText,
            "MassTransit",
            "Azure.Messaging.ServiceBus",
            "Confluent.Kafka",
            "RabbitMQ.Client");
        if (hasMessaging && ContainsAny(source, "PublishAsync", "Publish(", "ProduceAsync", "SendMessage"))
        {
            roles.Add("event-producer");
            capabilities.Add("events");
            evidence.Add("messaging publisher markers");
        }

        if (hasMessaging && ContainsAny(source, "IConsumer<", "Consume(", "ProcessMessage", "MessageHandler"))
        {
            roles.Add("event-consumer");
            capabilities.Add("events");
            evidence.Add("messaging consumer markers");
        }

        if (projectText.Contains("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("database");
            capabilities.Add("persistence");
            if (ContainsAny(combined, "Migrations", "Database.Migrate"))
            {
                capabilities.Add("migrations");
            }

            evidence.Add("EF Core or migration markers");
        }

        return new RepositoryComponentClassification(
            CreateId(name),
            ToRepositoryPath(repositoryPath, projectDirectory),
            ["csharp"],
            frameworks.Distinct(StringComparer.Ordinal).Order().ToArray(),
            roles.Distinct(StringComparer.Ordinal).Order().ToArray(),
            capabilities.Distinct(StringComparer.Ordinal).Order().ToArray(),
            "high",
            evidence.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool IsLikelyBackupProject(string projectPath, IReadOnlyList<string> allProjects)
    {
        var fileName = Path.GetFileName(projectPath);
        var looksLikeBackup = fileName.Contains(" - Backup.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".backup.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".bak.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(" (Copy).", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeBackup) return false;
        var directory = Path.GetDirectoryName(projectPath);
        return allProjects.Any(other => !other.Equals(projectPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(other), directory, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<RepositoryComponentClassification> ClassifyAngularWorkspace(
        string repositoryPath,
        string angularPath,
        ICollection<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(angularPath));
            if (!document.RootElement.TryGetProperty("projects", out var projects)
                || projects.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"Angular workspace has no projects object: {ToRepositoryPath(repositoryPath, angularPath)}");
                return [];
            }

            var workspaceRoot = Path.GetDirectoryName(angularPath)!;
            var workspacePackages = ReadPackageNames(Path.Combine(workspaceRoot, "package.json"), warnings);
            var results = new List<RepositoryComponentClassification>();
            foreach (var project in projects.EnumerateObject())
            {
                var configuredRoot = project.Value.TryGetProperty("root", out var rootValue)
                    ? rootValue.GetString() ?? string.Empty
                    : string.Empty;
                var componentRoot = Path.GetFullPath(Path.Combine(workspaceRoot, configuredRoot));
                var source = ReadSourceText(componentRoot, ".ts", warnings);
                var roles = new List<string> { "frontend-consumer" };
                var capabilities = new List<string> { "configuration", "packages" };
                var evidence = new List<string>
                {
                    ToRepositoryPath(repositoryPath, angularPath),
                    $"angular project: {project.Name}",
                };
                var frameworks = new List<string> { "angular" };
                AddWebUiFrameworkEvidence(workspaceRoot, workspacePackages, source, frameworks, evidence, warnings);

                if (ContainsAny(source, "HttpClient", "provideHttpClient", "HttpClientModule"))
                {
                    roles.Add("backend-api-consumer");
                    capabilities.Add("api-consumption");
                    evidence.Add("Angular HTTP client markers");
                }

                if (ContainsAny(source, "CanActivate", "AuthGuard", "permission", "authorization"))
                {
                    capabilities.Add("authorization");
                    evidence.Add("frontend authorization markers");
                }

                if (ContainsAny(source, "RouterModule", "provideRouter", "Routes =", "Routes="))
                {
                    capabilities.Add("routes");
                    evidence.Add("Angular route markers");
                }

                results.Add(new RepositoryComponentClassification(
                    CreateId(project.Name),
                    ToRepositoryPath(repositoryPath, componentRoot),
                    ["typescript"],
                    frameworks.Distinct(StringComparer.Ordinal).Order().ToArray(),
                    roles.Order().ToArray(),
                    capabilities.Distinct(StringComparer.Ordinal).Order().ToArray(),
                    "high",
                    evidence));
            }

            return results;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Unable to inspect {ToRepositoryPath(repositoryPath, angularPath)}: {exception.Message}");
            return [];
        }
    }

    private static RepositoryComponentClassification? ClassifyWebPackage(
        string repositoryPath,
        string packagePath,
        ICollection<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
            var packages = ReadPackageNames(document.RootElement);
            if (packages.Contains("@angular/core"))
            {
                return null;
            }

            var isVsCodeExtension = document.RootElement.TryGetProperty("engines", out var engines)
                && engines.ValueKind == JsonValueKind.Object
                && engines.TryGetProperty("vscode", out _);
            var isAzureFunctions = packages.Contains("@azure/functions");
            var frontendFramework = packages.Contains("next")
                ? "nextjs"
                : packages.Contains("react")
                    ? "react"
                    : packages.Contains("vue")
                        ? "vue"
                        : null;
            var backendFramework = packages.Contains("express")
                ? "express"
                : packages.Contains("fastify")
                    ? "fastify"
                    : packages.Contains("@nestjs/core")
                        ? "nestjs"
                        : packages.Contains("koa")
                            ? "koa"
                            : packages.Contains("@hapi/hapi")
                                ? "hapi"
                                : null;
            var primaryFramework = isVsCodeExtension ? "vscode-extension"
                : frontendFramework ?? backendFramework ?? (isAzureFunctions ? "azure-functions" : null);
            if (primaryFramework is null)
            {
                return null;
            }

            var componentRoot = Path.GetDirectoryName(packagePath)!;
            var source = ReadProductionSourceText(componentRoot, warnings, ".ts", ".tsx", ".js", ".jsx");
            var hasTypeScript = File.Exists(Path.Combine(componentRoot, "tsconfig.json"))
                || EnumerateRepositoryFiles(componentRoot, warnings).Any(path =>
                    path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
            var name = document.RootElement.TryGetProperty("name", out var nameValue)
                ? nameValue.GetString()
                : null;
            var roles = new List<string>();
            if (isVsCodeExtension) roles.Add("tooling");
            if (isAzureFunctions) roles.Add("worker");
            if (frontendFramework is not null) roles.Add("frontend-consumer");
            var hasFunctionHttpTrigger = isAzureFunctions && Regex.IsMatch(source, @"\bapp\s*\.\s*http\s*\(");
            if (backendFramework is not null || hasFunctionHttpTrigger) roles.Add("backend-api-producer");
            var capabilities = new List<string> { "configuration", "packages" };
            if (frontendFramework is not null || backendFramework is not null || hasFunctionHttpTrigger)
            {
                capabilities.Add("routes");
            }
            else if (!isAzureFunctions)
            {
                capabilities.Add("commands");
            }
            var evidence = new List<string>
            {
                ToRepositoryPath(repositoryPath, packagePath),
                $"{primaryFramework} package dependency",
            };
            var frameworks = new List<string> { primaryFramework };
            if (isAzureFunctions)
            {
                frameworks.Add("azure-functions");
                evidence.Add("@azure/functions package dependency");
                if (hasFunctionHttpTrigger) evidence.Add("Azure Functions HTTP trigger registration");
                if (Regex.IsMatch(source, @"\bapp\s*\.\s*(?:serviceBusQueue|serviceBusTopic|storageQueue|eventHub|eventGrid|cosmosDB)\s*\("))
                {
                    roles.Add("event-consumer");
                    capabilities.Add("events");
                    evidence.Add("Azure Functions event trigger registration");
                }
            }
            if (frontendFramework is not null && backendFramework is not null)
            {
                frameworks.Add(backendFramework);
                evidence.Add($"{backendFramework} backend package dependency");
            }
            if (frontendFramework is not null)
            {
                AddWebUiFrameworkEvidence(componentRoot, packages, source, frameworks, evidence, warnings);
            }

            if (ContainsAny(source, "fetch(", "axios", "useSWR", "graphql-request", "ApolloClient"))
            {
                roles.Add("backend-api-consumer");
                capabilities.Add("api-consumption");
                evidence.Add("web API-client markers");
            }

            if (frontendFramework == "nextjs"
                && ContainsAny(source, "NextResponse", "NextRequest", "export async function GET", "export async function POST"))
            {
                roles.Add("backend-api-producer");
                evidence.Add("Next.js route-handler markers");
            }

            if (ContainsAny(source, "next-auth", "useSession", "AuthProvider", "permission", "authorize"))
            {
                capabilities.Add("authorization");
                evidence.Add("web authorization markers");
            }

            if (packages.Overlaps(["supertokens-node", "passport", "jsonwebtoken", "jose", "express-session"]))
            {
                capabilities.Add("authentication");
                capabilities.Add("authorization");
                evidence.Add("Node authentication package dependency");
            }

            var usesBuiltInSqlite = ContainsAny(source, "node:sqlite");
            if (usesBuiltInSqlite
                || packages.Overlaps(["pg", "better-sqlite3", "sqlite3", "@libsql/client", "@prisma/client", "typeorm", "sequelize", "mongoose", "knex", "drizzle-orm"]))
            {
                roles.Add("database");
                capabilities.Add("persistence");
                evidence.Add(usesBuiltInSqlite ? "Node built-in SQLite usage" : "Node persistence package dependency");
            }

            if (packages.Overlaps(["prisma", "node-pg-migrate", "knex", "typeorm"])
                || (usesBuiltInSqlite && ContainsAny(source, "schema_migrations", "schema migrations")))
            {
                capabilities.Add("migrations");
                evidence.Add(usesBuiltInSqlite ? "Application-owned SQLite migration markers" : "Node migration package dependency");
            }

            if (packages.Overlaps(["swagger-ui-express", "@nestjs/swagger", "@fastify/swagger", "tsoa"]))
            {
                capabilities.Add("openapi");
                evidence.Add("Node OpenAPI package dependency");
            }

            return new RepositoryComponentClassification(
                CreateId(name ?? Path.GetFileName(componentRoot)),
                ToRepositoryPath(repositoryPath, componentRoot),
                [hasTypeScript ? "typescript" : "javascript"],
                frameworks.Distinct(StringComparer.Ordinal).Order().ToArray(),
                roles.Order().ToArray(),
                capabilities.Distinct(StringComparer.Ordinal).Order().ToArray(),
                "high",
                evidence);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Unable to inspect {ToRepositoryPath(repositoryPath, packagePath)}: {exception.Message}");
            return null;
        }
    }

    private static IReadOnlyList<RepositoryComponentClassification> ClassifySwiftProjects(
        string repositoryPath,
        IReadOnlyList<string> files,
        ICollection<string> warnings)
    {
        var roots = files
            .Where(path => string.Equals(Path.GetFileName(path), "Package.swift", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), "project.pbxproj", StringComparison.OrdinalIgnoreCase))
            .Select(path => string.Equals(Path.GetFileName(path), "Package.swift", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(path)!
                : Path.GetDirectoryName(Path.GetDirectoryName(path)!)!)
            .Distinct(PathComparer)
            .Order(PathComparer)
            .ToArray();
        var results = new List<RepositoryComponentClassification>();
        foreach (var componentRoot in roots)
        {
            var source = ReadSourceText(componentRoot, warnings, ".swift");
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var projectMetadata = string.Join(
                Environment.NewLine,
                files.Where(path => path.StartsWith(componentRoot + Path.DirectorySeparatorChar, PathComparison)
                    && (string.Equals(Path.GetFileName(path), "Package.swift", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileName(path), "project.pbxproj", StringComparison.OrdinalIgnoreCase)))
                    .Select(path => ReadFileText(path, warnings)));
            var combined = projectMetadata + Environment.NewLine + source;
            var frameworks = new List<string>();
            var roles = new List<string>();
            var capabilities = new List<string> { "configuration", "packages" };
            var evidence = new List<string>();

            if (combined.Contains("SwiftUI", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("swiftui");
                evidence.Add("SwiftUI markers");
            }

            if (combined.Contains("UIKit", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("uikit");
                evidence.Add("UIKit markers");
            }

            if (combined.Contains("AppKit", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("appkit");
                evidence.Add("AppKit markers");
            }

            var isIos = ContainsAny(combined, "IPHONEOS_DEPLOYMENT_TARGET", ".iOS(", "UIKit");
            var isMacOs = ContainsAny(combined, "MACOSX_DEPLOYMENT_TARGET", ".macOS(", "AppKit");
            roles.Add(isIos ? "mobile-client" : "native-frontend");
            if (isIos)
            {
                capabilities.Add("ios");
                evidence.Add("iOS platform markers");
            }

            if (isMacOs)
            {
                capabilities.Add("macos");
                evidence.Add("macOS platform markers");
            }

            if (ContainsAny(source, "URLSession", "Alamofire", "GraphQL"))
            {
                roles.Add("backend-api-consumer");
                capabilities.Add("api-consumption");
                evidence.Add("Swift API-client markers");
            }

            if (ContainsAny(source, "NavigationStack", "NavigationView", "UINavigationController"))
            {
                capabilities.Add("navigation");
                evidence.Add("Apple navigation markers");
            }

            if (ContainsAny(source, "AuthenticationServices", "SignInWithApple", "Authorization"))
            {
                capabilities.Add("authorization");
                evidence.Add("Apple authorization markers");
            }

            evidence.Insert(0, ToRepositoryPath(repositoryPath, componentRoot));
            results.Add(new RepositoryComponentClassification(
                CreateId(Path.GetFileName(componentRoot)),
                ToRepositoryPath(repositoryPath, componentRoot),
                ["swift"],
                frameworks.Count == 0 ? ["swift-package"] : frameworks.Order().ToArray(),
                roles.Distinct(StringComparer.Ordinal).Order().ToArray(),
                capabilities.Distinct(StringComparer.Ordinal).Order().ToArray(),
                "high",
                evidence));
        }

        return results;
    }

    private static IReadOnlyList<RepositoryComponentClassification> ClassifyKotlinProjects(
        string repositoryPath,
        IReadOnlyList<string> files,
        ICollection<string> warnings)
    {
        var results = new List<RepositoryComponentClassification>();
        foreach (var buildPath in files.Where(path =>
                     string.Equals(Path.GetFileName(path), "build.gradle.kts", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Path.GetFileName(path), "build.gradle", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsGeneratedKotlinProject(repositoryPath, buildPath))
            {
                continue;
            }

            var componentRoot = Path.GetDirectoryName(buildPath)!;
            var build = ReadFileText(buildPath, warnings);
            if (!ContainsAny(build, "kotlin(", "org.jetbrains.kotlin", "kotlin-android", "kotlin.android"))
            {
                continue;
            }

            var source = ReadSourceText(componentRoot, warnings, ".kt", ".kts");
            var componentFiles = files.Where(path =>
                    path.StartsWith(componentRoot + Path.DirectorySeparatorChar, PathComparison))
                .ToArray();
            var isAndroid = ContainsAny(build, "com.android.application", "com.android.library", "kotlin.android", "kotlin-android");
            var isCompose = ContainsAny(build + source, "androidx.compose", "@Composable", "composeOptions");
            var usesAndroidViews = isAndroid
                && (ContainsAny(source, "android.view.", "android.widget.", "AppCompatActivity", "androidx.fragment")
                    || componentFiles.Any(path =>
                        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                        && path.Replace('\\', '/').Contains("/res/layout/", StringComparison.OrdinalIgnoreCase)));
            var isMultiplatform = ContainsAny(build, "kotlin.multiplatform", "org.jetbrains.kotlin.multiplatform");
            var frameworks = new List<string>();
            var roles = new List<string>();
            var capabilities = new List<string> { "configuration", "packages" };
            var evidence = new List<string> { ToRepositoryPath(repositoryPath, buildPath) };

            if (isAndroid)
            {
                frameworks.Add("android");
                roles.Add("mobile-client");
                capabilities.Add("android");
                evidence.Add("Android Gradle plugin markers");
            }
            else if (isMultiplatform)
            {
                frameworks.Add("kotlin-multiplatform");
                roles.Add("native-frontend");
                evidence.Add("Kotlin Multiplatform plugin markers");
            }
            else
            {
                frameworks.Add("kotlin");
                roles.Add("shared-library");
            }

            if (isCompose)
            {
                frameworks.Add("jetpack-compose");
                capabilities.Add("navigation");
                evidence.Add("Jetpack Compose markers");
            }

            if (usesAndroidViews)
            {
                frameworks.Add("android-views");
                evidence.Add("Android Views source or layout markers");
            }

            if (ContainsAny(source, "Retrofit", "OkHttpClient", "HttpClient(", "ktor.client"))
            {
                roles.Add("backend-api-consumer");
                capabilities.Add("api-consumption");
                evidence.Add("Kotlin API-client markers");
            }

            if (ContainsAny(source, "NavController", "composable(", "navigation("))
            {
                capabilities.Add("navigation");
                evidence.Add("Kotlin navigation markers");
            }

            if (ContainsAny(source, "BiometricPrompt", "AuthManager", "PermissionChecker", "android.permission"))
            {
                capabilities.Add("authorization");
                evidence.Add("Kotlin authorization markers");
            }

            results.Add(new RepositoryComponentClassification(
                CreateId(Path.GetFileName(componentRoot)),
                ToRepositoryPath(repositoryPath, componentRoot),
                ["kotlin"],
                frameworks.Distinct(StringComparer.Ordinal).Order().ToArray(),
                roles.Distinct(StringComparer.Ordinal).Order().ToArray(),
                capabilities.Distinct(StringComparer.Ordinal).Order().ToArray(),
                "high",
                evidence));
        }

        return results;
    }

    private static IReadOnlyList<RepositoryComponentClassification> ClassifyGodotProjects(
        string repositoryPath,
        IReadOnlyList<string> files,
        ICollection<string> warnings)
    {
        var results = new List<RepositoryComponentClassification>();
        foreach (var projectPath in files.Where(path =>
                     string.Equals(Path.GetFileName(path), "project.godot", StringComparison.OrdinalIgnoreCase)
                     && !IsGeneratedGodotProject(repositoryPath, path)))
        {
            var componentRoot = Path.GetDirectoryName(projectPath)!;
            var configuration = ReadFileText(projectPath, warnings);
            if (string.IsNullOrWhiteSpace(configuration))
            {
                continue;
            }

            var componentFiles = files.Where(path =>
                    path.StartsWith(componentRoot + Path.DirectorySeparatorChar, PathComparison))
                .ToArray();
            var languages = new List<string>();
            var roles = new List<string>();
            var capabilities = new List<string> { "configuration", "game-engine" };
            var evidence = new List<string>
            {
                ToRepositoryPath(repositoryPath, projectPath),
                "Godot project configuration",
            };
            var hasCSharp = ContainsAny(configuration, "\"C#\"", "[dotnet]")
                || componentFiles.Any(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            var hasGdScript = componentFiles.Any(path => path.EndsWith(".gd", StringComparison.OrdinalIgnoreCase));
            if (hasCSharp)
            {
                languages.Add("csharp");
                capabilities.Add("dotnet");
                evidence.Add("Godot C# or .NET markers");
            }

            if (hasGdScript)
            {
                languages.Add("gdscript");
                evidence.Add("GDScript source markers");
            }

            var mobile = ContainsAny(configuration, "\"Mobile\"", "renderer/rendering_method=\"mobile\"");
            roles.Add(mobile ? "mobile-client" : "native-frontend");
            if (mobile)
            {
                capabilities.Add("mobile");
                evidence.Add("Godot mobile feature or renderer markers");
            }

            var mainScene = ReadGodotSetting(configuration, "run/main_scene");
            if (mainScene is not null)
            {
                capabilities.Add("scenes");
                evidence.Add($"Godot main scene: {mainScene}");
            }

            if (configuration.Contains("[autoload]", StringComparison.OrdinalIgnoreCase))
            {
                capabilities.Add("autoload");
                evidence.Add("Godot autoload markers");
            }

            if (configuration.Contains("[editor_plugins]", StringComparison.OrdinalIgnoreCase))
            {
                capabilities.Add("plugins");
                evidence.Add("Godot editor-plugin markers");
            }

            if (configuration.Contains("[input", StringComparison.OrdinalIgnoreCase))
            {
                capabilities.Add("input");
            }

            var configuredName = ReadGodotSetting(configuration, "config/name");
            results.Add(new RepositoryComponentClassification(
                CreateId(configuredName ?? Path.GetFileName(componentRoot)),
                ToRepositoryPath(repositoryPath, componentRoot),
                languages.Count == 0 ? ["godot-resource"] : languages.Distinct(StringComparer.Ordinal).Order().ToArray(),
                ["godot"],
                roles,
                capabilities.Distinct(StringComparer.Ordinal).Order().ToArray(),
                "high",
                evidence.Distinct(StringComparer.Ordinal).ToArray()));
        }

        return results;
    }

    private static void MergeGodotProjects(
        IList<RepositoryComponentClassification> components,
        IReadOnlyList<RepositoryComponentClassification> godotProjects)
    {
        foreach (var godot in godotProjects)
        {
            var existingIndex = Enumerable.Range(0, components.Count).FirstOrDefault(
                index => string.Equals(components[index].Root, godot.Root, PathComparison),
                -1);
            if (existingIndex < 0)
            {
                components.Add(godot);
                continue;
            }

            var existing = components[existingIndex];
            components[existingIndex] = existing with
            {
                Languages = existing.Languages.Concat(godot.Languages).Distinct(StringComparer.Ordinal).Order().ToArray(),
                Frameworks = existing.Frameworks.Concat(godot.Frameworks).Distinct(StringComparer.Ordinal).Order().ToArray(),
                Roles = existing.Roles
                    .Where(role => role != "shared-library")
                    .Concat(godot.Roles)
                    .Distinct(StringComparer.Ordinal)
                    .Order()
                    .ToArray(),
                Capabilities = existing.Capabilities.Concat(godot.Capabilities).Distinct(StringComparer.Ordinal).Order().ToArray(),
                Evidence = existing.Evidence.Concat(godot.Evidence).Distinct(StringComparer.Ordinal).ToArray(),
            };
        }
    }

    private static string? ReadGodotSetting(string configuration, string key)
    {
        var prefix = key + "=";
        var line = configuration.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return null;
        }

        var value = line[prefix.Length..].Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
    }

    private static bool IsGeneratedGodotProject(string repositoryPath, string projectPath)
    {
        var relative = "/" + ToRepositoryPath(repositoryPath, projectPath) + "/";
        return new[] { "/.godot/", "/.gradle/", "/bin/", "/build/", "/obj/" }
            .Any(segment => relative.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGeneratedKotlinProject(string repositoryPath, string buildPath)
    {
        var relative = "/" + ToRepositoryPath(repositoryPath, Path.GetDirectoryName(buildPath)!) + "/";
        return new[] { "/.godot/", "/.gradle/", "/bin/", "/build/", "/obj/", "/build_out/" }
            .Any(segment => relative.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<RepositoryComponentClassification> ClassifyLooseSourceAreas(
        string repositoryPath,
        IReadOnlyList<string> files,
        IReadOnlyList<RepositoryComponentClassification> knownComponents)
    {
        var knownRoots = knownComponents
            .Select(component => component.Root.Replace('\\', '/').Trim('/'))
            .ToArray();
        return files
            .Select(path => (Path: path, Relative: ToRepositoryPath(repositoryPath, path)))
            .Select(item => (item.Path, item.Relative, Language: LooseSourceLanguage(item.Relative)))
            .Where(item => item.Language is not null && !IsCovered(item.Relative, knownRoots))
            .GroupBy(item => LooseSourceRoot(item.Relative), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var root = group.Key;
                var languages = group.Select(item => item.Language!)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var frameworks = languages.Select(LooseFramework)
                    .Where(value => value is not null).Cast<string>()
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var segments = root.Split('/');
                var firstSegment = segments[0];
                var role = segments.Contains("tests", StringComparer.OrdinalIgnoreCase)
                    ? "test-automation"
                    : new[] { "build", "docs", "scripts", "tools" }.Contains(firstSegment, StringComparer.OrdinalIgnoreCase)
                        ? "tooling"
                        : "shared-library";
                return new RepositoryComponentClassification(
                    CreateId(root + "-" + role),
                    root,
                    languages,
                    frameworks,
                    [role],
                    [],
                    "medium",
                    group.Select(item => item.Relative).Order(StringComparer.OrdinalIgnoreCase).Take(5).ToArray());
            })
            .OrderBy(component => component.Root, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsCovered(string relativePath, IReadOnlyList<string> roots)
        => roots.Any(root => root.Length == 0 || root == "."
            || relativePath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));

    private static string LooseSourceRoot(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
        {
            return ".";
        }

        var testsIndex = Array.FindIndex(segments, segment => segment.Equals("tests", StringComparison.OrdinalIgnoreCase));
        if (testsIndex >= 0)
        {
            // Keep loose test sources beside, rather than on top of, a nested test
            // project. For example tests/runtime/probe.cs belongs to tests/runtime,
            // while a file directly under tests still belongs to tests.
            var rootLength = segments.Length > testsIndex + 2
                ? testsIndex + 2
                : testsIndex + 1;
            return string.Join('/', segments.Take(rootLength));
        }

        return new[] { "docs", "examples", "samples", "scripts", "tools" }
            .Contains(segments[0], StringComparer.OrdinalIgnoreCase)
            && segments.Length > 2
                ? $"{segments[0]}/{segments[1]}"
                : segments[0];
    }

    private static string? LooseSourceLanguage(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" => "javascript",
            ".swift" => "swift",
            ".kt" or ".kts" => "kotlin",
            ".py" => "python",
            ".gd" => "gdscript",
            ".sql" => "sql",
            _ => null,
        };

    private static string? LooseFramework(string language)
        => language switch
        {
            "csharp" => "dotnet",
            "kotlin" => "kotlin",
            "python" => "python",
            "gdscript" => "godot",
            _ => null,
        };

    private static IEnumerable<string> EnumerateRepositoryFiles(
        string repositoryPath,
        ICollection<string> warnings)
    {
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(directory).ToArray();
                directories = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Unable to scan {ToRepositoryPath(repositoryPath, directory)}: {exception.Message}");
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var child in directories)
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(child))
                    && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string ReadSourceText(
        string directory,
        string extension,
        ICollection<string> warnings)
        => ReadSourceText(directory, warnings, extension);

    private static string ReadSourceText(
        string directory,
        ICollection<string> warnings,
        params string[] extensions)
        => ReadSourceText(directory, warnings, excludeTestSources: false, extensions);

    private static string ReadProductionSourceText(
        string directory,
        ICollection<string> warnings,
        params string[] extensions)
        => ReadSourceText(directory, warnings, excludeTestSources: true, extensions);

    private static string ReadSourceText(
        string directory,
        ICollection<string> warnings,
        bool excludeTestSources,
        params string[] extensions)
    {
        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        var content = new List<string>();
        foreach (var path in EnumerateRepositoryFiles(directory, warnings)
                     .Where(path => extensions.Any(extension =>
                         path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                     .Where(path => !excludeTestSources || !IsTestSourcePath(directory, path))
                     .Take(2_000))
        {
            try
            {
                content.Add(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Unable to read source marker file {path}: {exception.Message}");
            }
        }

        return string.Join(Environment.NewLine, content);
    }

    private static bool IsTestSourcePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.Split('/').Any(segment => segment is "test" or "tests" or "__tests__")
               || relative.Contains(".test.", StringComparison.OrdinalIgnoreCase)
               || relative.Contains(".spec.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] markers)
        => markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string ReadFileText(string path, ICollection<string> warnings)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Unable to read classifier evidence {path}: {exception.Message}");
            return string.Empty;
        }
    }

    private static string ToRepositoryPath(string repositoryPath, string absolutePath)
        => Path.GetRelativePath(repositoryPath, absolutePath).Replace('\\', '/');

    private static string CreateId(string value)
    {
        var id = InvalidIdCharacters().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(id) ? "component" : id;
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidIdCharacters();

    private static HashSet<string> ReadPackageNames(JsonElement root)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyName in new[] { "dependencies", "devDependencies", "peerDependencies" })
        {
            if (root.TryGetProperty(propertyName, out var dependencies)
                && dependencies.ValueKind == JsonValueKind.Object)
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    packages.Add(dependency.Name);
                }
            }
        }

        return packages;
    }

    private static HashSet<string> ReadPackageNames(string packagePath, ICollection<string> warnings)
    {
        if (!File.Exists(packagePath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
            return ReadPackageNames(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Unable to inspect package dependencies in {packagePath}: {exception.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void AddWebUiFrameworkEvidence(
        string componentRoot,
        IReadOnlySet<string> packages,
        string source,
        ICollection<string> frameworks,
        ICollection<string> evidence,
        ICollection<string> warnings)
    {
        var detections = new List<(string Marker, bool Present, string Evidence)>
        {
            ("angular-material", packages.Contains("@angular/material") || ContainsAny(source, "@angular/material"), "@angular/material dependency or import"),
            ("material-ui", packages.Contains("@mui/material") || ContainsAny(source, "@mui/material"), "@mui/material dependency or import"),
            ("chakra-ui", packages.Contains("@chakra-ui/react") || ContainsAny(source, "@chakra-ui/react"), "@chakra-ui/react dependency or import"),
            ("ant-design", packages.Contains("antd") || ContainsAny(source, "from 'antd'", "from \"antd\""), "Ant Design dependency or import"),
            ("mantine", packages.Contains("@mantine/core") || ContainsAny(source, "@mantine/core"), "@mantine/core dependency or import"),
            ("heroui", packages.Contains("@heroui/react") || packages.Contains("@nextui-org/react"), "HeroUI or NextUI package dependency"),
            ("radix-ui", packages.Any(package => package.StartsWith("@radix-ui/", StringComparison.OrdinalIgnoreCase)), "Radix UI package dependency"),
            ("headless-ui", packages.Contains("@headlessui/react") || packages.Contains("@headlessui/vue"), "Headless UI package dependency"),
            ("react-aria", packages.Contains("react-aria") || packages.Contains("react-aria-components"), "React Aria package dependency"),
            ("fluent-ui", packages.Contains("@fluentui/react-components") || packages.Contains("@fluentui/react"), "Fluent UI package dependency"),
            ("primereact", packages.Contains("primereact"), "PrimeReact package dependency"),
            ("primevue", packages.Contains("primevue"), "PrimeVue package dependency"),
            ("vuetify", packages.Contains("vuetify"), "Vuetify package dependency"),
            ("nuxt-ui", packages.Contains("@nuxt/ui") || ContainsAny(source, "@nuxt/ui"), "@nuxt/ui dependency or import"),
            ("quasar", packages.Contains("quasar"), "Quasar package dependency"),
            ("naive-ui", packages.Contains("naive-ui"), "Naive UI package dependency"),
            ("element-plus", packages.Contains("element-plus"), "Element Plus package dependency"),
            ("bootstrap", packages.Contains("bootstrap") || packages.Contains("react-bootstrap"), "Bootstrap package dependency"),
            ("daisyui", packages.Contains("daisyui"), "daisyUI package dependency"),
            ("flowbite", packages.Contains("flowbite") || packages.Contains("flowbite-react"), "Flowbite package dependency"),
            ("tailwindcss", packages.Contains("tailwindcss") || HasTailwindConfiguration(componentRoot), "Tailwind CSS dependency or configuration"),
            ("motion", packages.Contains("motion") || packages.Contains("framer-motion"), "Motion package dependency"),
        };

        var componentsPath = Path.Combine(componentRoot, "components.json");
        var componentsJson = File.Exists(componentsPath) ? ReadFileText(componentsPath, warnings) : string.Empty;
        var hasSera = ContainsAny(source, "seraui.com/registry", "from 'sera-ui'", "from \"sera-ui\"")
            || ContainsAny(componentsJson, "seraui.com/registry");
        var hasShadcn = File.Exists(componentsPath)
            && ContainsAny(componentsJson, "ui.shadcn.com", "https://ui.shadcn.com/schema.json", "\"aliases\"");
        detections.Insert(0, ("shadcn-ui", hasShadcn, "shadcn components.json configuration"));
        detections.Insert(0, ("sera-ui", hasSera, "Sera UI registry/source marker"));

        foreach (var detection in detections.Where(detection => detection.Present))
        {
            frameworks.Add(detection.Marker);
            evidence.Add(detection.Evidence);
        }
    }

    private static bool HasTailwindConfiguration(string componentRoot)
        => new[]
            {
                "tailwind.config.js",
                "tailwind.config.cjs",
                "tailwind.config.mjs",
                "tailwind.config.ts",
            }
            .Any(file => File.Exists(Path.Combine(componentRoot, file)));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
