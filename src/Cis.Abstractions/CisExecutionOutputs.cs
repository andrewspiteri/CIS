using System.Diagnostics;

namespace Cis.Abstractions;

/// <summary>Recognizes disposable outputs without excluding source just because its directory has a common name.</summary>
internal sealed class CisExecutionOutputs
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    { "bin", "obj", "node_modules", ".next", ".nuxt", ".stryker-tmp", "TestResults", "coverage", "artifacts", "dist", ".vs", ".idea", "__pycache__", ".venv" };
    private readonly HashSet<string>? _gitInputDirectories;

    internal CisExecutionOutputs(string root)
    {
        if (!File.Exists(Path.Combine(root, ".git")) && !Directory.Exists(Path.Combine(root, ".git"))) return;
        var start = new ProcessStartInfo("git") { WorkingDirectory = root };
        foreach (var argument in new[] { "-c", "safe.directory=" + root, "ls-files", "--cached", "--others", "--exclude-standard", "-z" })
            start.ArgumentList.Add(argument);
        CisProcessResult result;
        try { result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(30), 16 * 1024 * 1024); }
        catch (System.ComponentModel.Win32Exception error) { throw new InvalidDataException("Git input classification is unavailable.", error); }
        if (result.ExitCode != 0 || result.TimedOut || result.OutputTruncated)
            throw new InvalidDataException("Git input classification is incomplete; execution freshness cannot be established.");
        _gitInputDirectories = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var end = path.LastIndexOf('/');
            while (end >= 0)
            {
                _gitInputDirectories.Add(path[..end]);
                end = path.LastIndexOf('/', end - 1);
            }
        }
    }

    internal bool IsOutput(string directory, string relative)
    {
        var name = Path.GetFileName(directory);
        if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Names.Contains(name)) return false;
        if (_gitInputDirectories is not null) return !_gitInputDirectories.Contains(relative);
        // Before Git adoption, only unambiguous project-adjacent native output locations are omitted.
        var parent = Path.GetDirectoryName(directory)!;
        if (name is "bin" or "obj")
            return Directory.EnumerateFiles(parent, "*.*proj", SearchOption.TopDirectoryOnly).Any();
        return name == "node_modules" && File.Exists(Path.Combine(parent, "package.json"));
    }
}
