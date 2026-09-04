using System.Diagnostics;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Design;

public sealed record DesignProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IDesignProcessRunner
{
    DesignProcessResult Run(string repositoryPath, string rendererPath);
}

public sealed class NodeDesignProcessRunner : IDesignProcessRunner
{
    public static readonly TimeSpan RendererTimeout = TimeSpan.FromMinutes(5);

    public DesignProcessResult Run(string repositoryPath, string rendererPath)
    {
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(rendererPath);
        var sharpNodeModules = FindSharpNodeModules(repositoryPath, rendererPath);
        if (sharpNodeModules is not null)
            start.Environment["CIS_SHARP_NODE_MODULES"] = sharpNodeModules;
        var result = CisProcessSafety.Run(start, RendererTimeout, maximumCharactersPerStream: 2_097_152);
        var error = result.StandardError;
        if (result.TimedOut) error = $"Renderer timed out after {RendererTimeout.TotalMinutes:0} minutes.\n{error}";
        if (result.OutputTruncated) error = $"{error}\nRenderer output exceeded the retained-output limit.".TrimStart();
        return new(result.ExitCode ?? -1, result.StandardOutput, error);
    }

    public static string? FindSharpNodeModules(string repositoryPath, string rendererPath)
    {
        static string? Existing(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            var full = Path.GetFullPath(candidate);
            return File.Exists(Path.Combine(full, "sharp", "package.json")) ? full : null;
        }

        var configured = Existing(Environment.GetEnvironmentVariable("CIS_SHARP_NODE_MODULES"));
        if (configured is not null) return configured;

        var repository = Path.GetFullPath(repositoryPath);
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(rendererPath)); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var local = Existing(Path.Combine(directory, "node_modules"));
            if (local is not null) return local;
            if (directory.Equals(repository, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
        }

        var workspacePath = Path.Combine(repository, ".cis", "workspace.yml");
        if (File.Exists(workspacePath))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(workspacePath),
                         @"(?m)^\s+path:\s*(?<path>[^#\r\n]+?)\s*$",
                         RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                var participant = match.Groups["path"].Value.Trim().Trim('"', '\'');
                var root = Path.GetFullPath(Path.IsPathRooted(participant)
                    ? participant
                    : Path.Combine(repository, participant));
                var modules = Existing(Path.Combine(root, "node_modules"));
                if (modules is not null) return modules;
            }
        }

        foreach (var candidate in (Environment.GetEnvironmentVariable("NODE_PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var modules = Existing(candidate);
            if (modules is not null) return modules;
        }
        return null;
    }
}
