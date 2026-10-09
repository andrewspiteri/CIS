using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Plans local reference copies; never executes them or adopts their stack.</summary>
internal sealed class RepositoryExampleInstallation(string distributionPath)
{
    internal const string Root = ".cis/local/examples";
    internal const string RecipeRoot = Root + "/dotnet-engineering";
    internal const string ManifestPath = Root + "/manifest.json";
    internal const string SettingsPath = ".cis/example-settings.json";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal ExampleInstallationPlan Plan(string repository, RepositoryClassification classification, string? requestedMode)
    {
        var changes = new List<ExampleFileChange>();
        var warnings = new List<string>();
        var retained = new List<string>();
        try
        {
            var settingsText = ReadOptional(repository, SettingsPath);
            var settings = settingsText is null ? new ExampleSettings(1, "auto") : Parse<ExampleSettings>(settingsText);
            if (settings.SchemaVersion != 1 || settings.Mode is not ("auto" or "off"))
                throw new InvalidDataException("Invalid example settings; expected schemaVersion 1 and mode auto or off.");
            var mode = requestedMode ?? settings.Mode;
            if (mode is not ("auto" or "off")) throw new InvalidDataException("Expected --examples auto or off.");
            if (requestedMode is not null)
                AddChange(SettingsPath, Serialize(new ExampleSettings(1, mode)), settingsText, changes);
            if (mode == "off") return new(changes, retained, warnings, []);
            if (classification.DeclaredStack != "csharp" && !classification.Components.Any(component => component.Languages.Contains("csharp")))
                return new(changes, retained, warnings, []);

            var bundle = BundledExampleFiles.Read(distributionPath);
            var previousText = ReadOptional(repository, ManifestPath);
            var previous = previousText is null ? null : Parse<ExampleInstallationManifest>(previousText);
            Validate(previous);
            var owned = (previous?.Files ?? []).ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var next = new List<ExampleInstalledFile>();
            foreach (var file in bundle.Files)
            {
                var relative = RecipeRoot + "/" + file.Path;
                var current = ReadOptional(repository, relative);
                var currentHash = current is null ? null : Hash(current);
                owned.TryGetValue(file.Path, out var prior);
                if (current is null || prior?.AppliedHash is not null && currentHash == prior.AppliedHash)
                {
                    AddChange(relative, file.Content, current, changes);
                    next.Add(new(file.Path, file.Hash));
                    if (currentHash == file.Hash) retained.Add(relative);
                }
                else
                {
                    retained.Add(relative);
                    warnings.Add($"Local example edit retained (edited or unowned): {relative}. Bundled version {bundle.Digest} is available through repo example --destination <new-local-directory>; compare before replacing edits.");
                    // Keep the last installed identity; never silently adopt edits as managed content.
                    next.Add(new(file.Path, prior?.AppliedHash));
                }
            }
            foreach (var prior in owned.Values.Where(file => !bundle.Files.Any(current => current.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))))
            {
                if (ReadOptional(repository, RecipeRoot + "/" + prior.Path) is null) continue;
                retained.Add(RecipeRoot + "/" + prior.Path);
                warnings.Add($"Retired example file retained for manual review: {RecipeRoot}/{prior.Path}");
                next.Add(prior with { Retired = true });
            }
            if (next.Count > 1000) throw new InvalidDataException("Local example history exceeds 1,000 files; archive retired copies and their metadata before retrying.");
            var ignore = ReadOptional(repository, Root + "/.gitignore");
            if (ignore?.TrimEnd().EndsWith("\n*", StringComparison.Ordinal) != true && ignore?.Trim() != "*")
                AddChange(Root + "/.gitignore", (ignore is null ? "" : ignore.TrimEnd() + "\n") + "# Local reference material, not project implementation.\n*\n", ignore, changes);
            AddChange(ManifestPath, Serialize(new ExampleInstallationManifest(1, bundle.Digest,
                next.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray())), previousText, changes);
            return new(changes, retained, warnings, []);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or System.ComponentModel.Win32Exception)
        {
            return new([], [], warnings, [$"Local example installation could not be planned: {exception.Message}"]);
        }
    }

    private static void Validate(ExampleInstallationManifest? manifest)
    {
        if (manifest is null) return;
        if (manifest.SchemaVersion != 1 || !ValidHash(manifest.BundleDigest) || manifest.Files is null || manifest.Files.Length > 1000)
            throw new InvalidDataException("Invalid local example manifest.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
            if (file is null || !BundledExampleFiles.ValidRelativePath(file.Path) || !paths.Add(file.Path)
                || file.AppliedHash is not null && !ValidHash(file.AppliedHash))
                throw new InvalidDataException("Invalid or duplicate file identity in local example manifest.");
    }

    private static bool ValidHash(string? hash) => hash is { Length: 71 } && hash.StartsWith("sha256:", StringComparison.Ordinal)
        && hash[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static T Parse<T>(string text) => JsonSerializer.Deserialize<T>(text, Json)
        ?? throw new InvalidDataException("Example metadata must contain an object.");

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json) + "\n";

    internal static string Hash(string text) => "sha256:" + Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(text))).ToLowerInvariant();

    private static string? ReadOptional(string repository, string relative)
    {
        if (!CisPathSafety.TryResolveUnderRoot(repository, relative, out var path)
            || CisPathSafety.ContainsReparsePoint(repository, path) || Directory.Exists(path))
            throw new InvalidDataException($"Example path must be an unlinked regular file inside the repository: {relative}");
        if (!File.Exists(path)) return null;
        return BundledExampleFiles.ReadText(path);
    }

    private static void AddChange(string path, string content, string? previous, List<ExampleFileChange> changes)
    {
        if (content != previous) changes.Add(new(path, content, previous));
    }
}

internal sealed record ExampleSettings(int SchemaVersion, string Mode);
internal sealed record ExampleInstalledFile(string Path, string? AppliedHash, bool Retired = false);
internal sealed record ExampleInstallationManifest(int SchemaVersion, string BundleDigest, ExampleInstalledFile[] Files);
internal sealed record ExampleFileChange(string Path, string Content, string? PreviousContent);
internal sealed record ExampleInstallationPlan(IReadOnlyList<ExampleFileChange> Files, IReadOnlyList<string> Retained,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
