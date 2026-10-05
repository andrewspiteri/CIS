using System.Diagnostics;

namespace Cis.Abstractions;

/// <summary>Serializes derived-state builders across processes; followers recheck cached inputs.</summary>
public static class CisBuildLock
{
    public static IDisposable Acquire(string repository, string module, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(module) || module.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("A build module must contain only letters, digits, and hyphens.", nameof(module));
        var directory = Path.Combine(Path.GetFullPath(repository), ".cis", "local", module);
        if (CisPathSafety.ContainsReparsePoint(repository, directory))
            throw new IOException("Unsafe derived-state directory.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "build.lock");
        if (CisPathSafety.ContainsReparsePoint(repository, path)) throw new IOException("Unsafe build lock.");
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (watch.Elapsed < (timeout ?? TimeSpan.FromMinutes(5))) { Thread.Sleep(50); }
        }
    }
}
