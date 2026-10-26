using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.MSTestAdapterCompat.Runtime;
using Mono.Cecil;

namespace Cis.MSTestAdapterCompat.Tool;

internal static class Program
{
    private const string OfficialSha256 = "74559c1c0fa199dc1ce53324a95b67b3084ca906df9ddac52fdb29bb2043c943";
    private const string CecilSha256 = "831dca77470d85cb6ffbea3072daa7a3df5b7c9fcfd9c3f43674a9be99d4bfcf";
    private const string Policy = "cis-mstest-4.5.1-net9-vstest-v1";

    private static int Main(string[] arguments)
    {
        try
        {
            ValidateCecil();
            if (arguments.Length == 2 && arguments[0] == "--verify-overlay")
            {
                VerifyOverlay(Path.GetFullPath(arguments[1]));
                Console.WriteLine(JsonSerializer.Serialize(new { status = "verified", policy = Policy, files = 3 }));
                return 0;
            }
            if (arguments.Length != 4 || arguments[0] != "--input" || arguments[2] != "--output-directory")
            {
                throw new ArgumentException("Usage: --input <official PlatformServices.dll> --output-directory <empty owned directory>, or --verify-overlay <directory>");
            }

            var inputPath = Path.GetFullPath(arguments[1]);
            var outputDirectory = Path.GetFullPath(arguments[3]);
            if (new FileInfo(inputPath).Length != 510264)
            {
                throw new InvalidOperationException("Unsupported input length for the exact official MSTest adapter pin.");
            }
            var inputBytes = File.ReadAllBytes(inputPath);
            if (Hash(inputBytes) != OfficialSha256)
            {
                throw new InvalidOperationException("Unsupported input: only the exact official MSTest.TestAdapter 4.5.1 net9.0 PlatformServices binary is accepted.");
            }

            ValidateOutputDirectory(inputPath, outputDirectory);
            var rewritten = AdapterRewriter.Rewrite(inputBytes);
            var runtimePath = typeof(CompatibilityRuntime).Assembly.Location;
            var runtimeBytes = File.ReadAllBytes(runtimePath);
            const string adapterName = "MSTestAdapter.PlatformServices.dll";
            const string runtimeName = "Cis.MSTestAdapterCompat.Runtime.dll";
            var manifest = new
            {
                schemaVersion = 1,
                compatibilityPolicy = Policy,
                status = "unofficial-modified-binary",
                strongNameSignature = "invalidated; original Microsoft identity retained for adapter binding",
                supportedHost = ".NET10; opt-in VSTest execution",
                source = new { package = "MSTest.TestAdapter", version = "4.5.1", targetFramework = "net9.0", file = adapterName, sha256 = OfficialSha256 },
                tool = new { file = Path.GetFileName(Assembly.GetExecutingAssembly().Location), sha256 = Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)) },
                dependency = new { package = "Mono.Cecil", version = "0.11.6", sha256 = CecilSha256 },
                outputs = new[]
                {
                    new { file = adapterName, sha256 = Hash(rewritten.Bytes), bytes = rewritten.Bytes.LongLength },
                    new { file = runtimeName, sha256 = Hash(runtimeBytes), bytes = runtimeBytes.LongLength }
                },
                rewrites = rewritten.Changes,
                cleanupResultOwnership = "First caller publishes the original result/output; other callers join without re-publishing normalized results. Infrastructure faults remain failures.",
                adoption = "Opt-in compatibility output; does not change upstream packages, native mutation outcomes or acceptance thresholds."
            };
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });

            Directory.CreateDirectory(outputDirectory);
            WriteNew(Path.Combine(outputDirectory, adapterName), rewritten.Bytes);
            WriteNew(Path.Combine(outputDirectory, runtimeName), runtimeBytes);
            WriteNew(Path.Combine(outputDirectory, "compatibility-manifest.json"), manifestBytes);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "created", policy = Policy, files = 3, bytes = rewritten.Bytes.LongLength + runtimeBytes.LongLength + manifestBytes.LongLength }));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or BadImageFormatException or JsonException or KeyNotFoundException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static void ValidateCecil()
    {
        if (Hash(File.ReadAllBytes(typeof(ModuleDefinition).Assembly.Location)) != CecilSha256)
        {
            throw new InvalidOperationException("Unsupported Mono.Cecil binary; the tool requires its pinned 0.11.6 assembly.");
        }
    }

    private static void VerifyOverlay(string directory)
    {
        var manifestPath = Path.Combine(directory, "compatibility-manifest.json");
        if (new FileInfo(manifestPath).Length > 64 * 1024)
        {
            throw new InvalidOperationException("The compatibility manifest exceeds its bounded size.");
        }
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var manifest = document.RootElement;
        var source = manifest.GetProperty("source");
        var tool = manifest.GetProperty("tool");
        var dependency = manifest.GetProperty("dependency");
        if (manifest.GetProperty("schemaVersion").GetInt32() != 1 ||
            manifest.GetProperty("compatibilityPolicy").GetString() != Policy ||
            manifest.GetProperty("status").GetString() != "unofficial-modified-binary" ||
            source.GetProperty("package").GetString() != "MSTest.TestAdapter" ||
            source.GetProperty("version").GetString() != "4.5.1" ||
            source.GetProperty("targetFramework").GetString() != "net9.0" ||
            source.GetProperty("file").GetString() != "MSTestAdapter.PlatformServices.dll" ||
            source.GetProperty("sha256").GetString() != OfficialSha256 ||
            tool.GetProperty("sha256").GetString() != Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)) ||
            dependency.GetProperty("package").GetString() != "Mono.Cecil" ||
            dependency.GetProperty("version").GetString() != "0.11.6" ||
            dependency.GetProperty("sha256").GetString() != CecilSha256)
        {
            throw new InvalidOperationException("Overlay provenance does not match the current compatibility tool and exact upstream pin.");
        }
        var outputs = manifest.GetProperty("outputs").EnumerateArray().ToArray();
        if (outputs.Length != 2)
        {
            throw new InvalidOperationException("The compatibility manifest must describe exactly two binary outputs.");
        }
        foreach (var file in new[] { "MSTestAdapter.PlatformServices.dll", "Cis.MSTestAdapterCompat.Runtime.dll" })
        {
            var entry = outputs.Single(output => output.GetProperty("file").GetString() == file);
            var path = Path.Combine(directory, file);
            var length = new FileInfo(path).Length;
            var maximumLength = file == "MSTestAdapter.PlatformServices.dll" ? 2 * 1024 * 1024 : 1024 * 1024;
            if (length > maximumLength || entry.GetProperty("bytes").GetInt64() != length)
            {
                throw new InvalidOperationException("Overlay binary length exceeds its bound or differs from its provenance.");
            }
            var bytes = File.ReadAllBytes(path);
            if (entry.GetProperty("sha256").GetString() != Hash(bytes) || entry.GetProperty("bytes").GetInt64() != bytes.LongLength ||
                (file == "Cis.MSTestAdapterCompat.Runtime.dll" && Hash(bytes) != Hash(File.ReadAllBytes(typeof(CompatibilityRuntime).Assembly.Location))))
            {
                throw new InvalidOperationException("Overlay binary bytes do not match their provenance and current runtime identity.");
            }
        }
    }

    private static void ValidateOutputDirectory(string inputPath, string outputDirectory)
    {
        for (DirectoryInfo? directory = new(outputDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Output through a filesystem link is forbidden.");
            }
        }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var outputPrefix = Path.TrimEndingDirectorySeparator(outputDirectory) + Path.DirectorySeparatorChar;
        var inputDirectoryPrefix = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(inputPath)!) + Path.DirectorySeparatorChar;
        if (inputPath.StartsWith(outputPrefix, comparison) || outputPrefix.StartsWith(inputDirectoryPrefix, comparison))
        {
            throw new InvalidOperationException("The output must be separate from the original adapter directory.");
        }
        var normalized = outputDirectory.Replace('\\', '/');
        if (normalized.Contains("/.nuget/packages/", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("/.nuget/packages", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("NuGet-cache output is forbidden.");
        }
        var configuredCache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configuredCache))
        {
            var cachePrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredCache)) + Path.DirectorySeparatorChar;
            if (outputPrefix.StartsWith(cachePrefix, comparison))
            {
                throw new InvalidOperationException("NuGet-cache output is forbidden.");
            }
        }
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new InvalidOperationException("The output directory must be empty; existing files are never overwritten.");
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }
}
