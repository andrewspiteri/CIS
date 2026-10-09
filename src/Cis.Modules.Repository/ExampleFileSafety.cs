using System.Diagnostics;

namespace Cis.Modules.Repository;

/// <summary>Rejects special objects before opening reference files; Unix metadata probes are bounded.</summary>
internal static class ExampleFileSafety
{
    internal static void EnsureRegular(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new InvalidDataException("Example input must be an unlinked regular file.");
        if (OperatingSystem.IsWindows()) return;

        // Unix FileAttributes does not distinguish FIFOs from regular files. Use the system's
        // metadata-only predicate, never a shell or a program from the target repository.
        var executable = new[] { "/usr/bin/test", "/bin/test" }.FirstOrDefault(File.Exists)
            ?? throw new InvalidDataException("Regular-file validation requires the system test utility on this platform.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(Path.GetFullPath(path));
        using var process = Process.Start(start) ?? throw new IOException("Cannot start regular-file validation.");
        if (!process.WaitForExit(3000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidDataException("Regular-file validation timed out.");
        }
        if (process.ExitCode != 0) throw new InvalidDataException("Example input is not a regular file.");
    }
}
