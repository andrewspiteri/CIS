using System.IO.Compression;
using System.Diagnostics;
using Cis.Abstractions;

namespace Cis.Abstractions.Tests;

public sealed class PathAndArchiveSafetyTests
{
    [Theory]
    [InlineData(null, "file.txt")]
    [InlineData("", "file.txt")]
    [InlineData("   ", "file.txt")]
    [InlineData("root", null)]
    [InlineData("root", "")]
    [InlineData("root", "   ")]
    public void ResolveUnderRoot_RejectsMissingInputs(string? root, string? relative)
    {
        Assert.False(CisPathSafety.TryResolveUnderRoot(root!, relative!, out var absolute));
        Assert.Empty(absolute);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("safe/../../outside.txt")]
    [InlineData("safe//file.txt")]
    [InlineData("./file.txt")]
    public void ResolveUnderRoot_RejectsAmbiguousOrEscapingPaths(string relative)
    {
        var root = TemporaryRoot("cis-path-tests");
        Assert.False(CisPathSafety.TryResolveUnderRoot(root, relative, out _));
    }

    [Fact]
    public void PathContainment_UsesThePlatformCaseSemantics()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.False(CisPathSafety.IsUnderRoot("/tmp/cis-root", "/tmp/CIS-ROOT/file.txt"));
    }

    [Fact]
    public void ResolveUnderRoot_AllowsTheRootOnlyWhenExplicitlyRequested()
    {
        var root = TemporaryRoot("cis-path-tests");
        Assert.False(CisPathSafety.TryResolveUnderRoot(root, ".", out _));
        Assert.True(CisPathSafety.TryResolveUnderRoot(root, ".", out var resolved, allowRoot: true));
        Assert.Equal(Path.GetFullPath(root), resolved);
    }

    [Fact]
    public void ResolveUnderRoot_ResolvesAValidNestedPath()
    {
        var root = TemporaryRoot("cis-path-tests");
        Assert.True(CisPathSafety.TryResolveUnderRoot(root, "safe/file.txt", out var resolved));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "safe", "file.txt"), resolved);
        Assert.True(CisPathSafety.IsUnderRoot(root, resolved));
        Assert.False(CisPathSafety.IsUnderRoot(root, root));
        Assert.True(CisPathSafety.IsUnderRoot(root, root, allowRoot: true));
        Assert.False(CisPathSafety.IsUnderRoot(root, root + "-sibling/file.txt"));
    }

    [Fact]
    public void ResolveUnderRoot_RejectsRootedAndInvalidPaths()
    {
        var root = TemporaryRoot("cis-path-tests");
        Assert.False(CisPathSafety.TryResolveUnderRoot(root, Path.GetFullPath(Path.Combine(root, "file.txt")), out _));
        Assert.False(CisPathSafety.TryResolveUnderRoot("\0", "file.txt", out _));
        Assert.False(CisPathSafety.IsUnderRoot("\0", root));
        if (OperatingSystem.IsWindows())
            Assert.False(CisPathSafety.TryResolveUnderRoot(root, "safe:file.txt", out _));
    }

    [Fact]
    public void ReparseChecks_DistinguishNormalOutsideAndLinkedPaths()
    {
        var basis = TemporaryRoot("cis-path-tests");
        var root = Path.Combine(basis, "root");
        var child = Path.Combine(root, "child");
        var outside = Path.Combine(basis, "outside");
        Directory.CreateDirectory(child);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(child, "normal.txt"), "normal");
        Assert.False(CisPathSafety.ContainsReparsePoint(root, root));
        Assert.False(CisPathSafety.ContainsReparsePoint(root, Path.Combine(child, "normal.txt")));
        Assert.True(CisPathSafety.ContainsReparsePoint(root, Path.Combine(outside, "file.txt")));
        Assert.False(CisPathSafety.IsReparsePoint(Path.Combine(child, "normal.txt")));
        Assert.False(CisPathSafety.ContainsReparsePointInTree(root));

        var link = Path.Combine(root, "linked");
        try
        {
            var linked = TryCreateDirectoryLink(link, outside);
            if (linked)
            {
                Assert.True(CisPathSafety.IsReparsePoint(link));
                Assert.True(CisPathSafety.ContainsReparsePoint(root, Path.Combine(link, "file.txt")));
                Assert.True(CisPathSafety.ContainsReparsePointInTree(root));
            }
        }
        finally
        {
            if (Directory.Exists(link) && CisPathSafety.IsReparsePoint(link)) Directory.Delete(link);
            Directory.Delete(basis, true);
        }
    }

    [Fact]
    public void ReparseChecks_DetectAFileSymbolicLinkWhenSupported()
    {
        var basis = TemporaryRoot("cis-path-tests");
        var root = Directory.CreateDirectory(Path.Combine(basis, "root")).FullName;
        var target = Path.Combine(basis, "target.txt");
        var link = Path.Combine(root, "linked.txt");
        File.WriteAllText(target, "target");
        try
        {
            try { File.CreateSymbolicLink(link, target); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { return; }
            Assert.True(CisPathSafety.IsReparsePoint(link));
            Assert.True(CisPathSafety.ContainsReparsePoint(root, link));
            Assert.True(CisPathSafety.ContainsReparsePointInTree(root));
        }
        finally
        {
            Directory.Delete(basis, true);
        }
    }

    [Fact]
    public void Enumerators_RespectRecursionPatternsAndEntryKinds()
    {
        var root = TemporaryRoot("cis-path-tests");
        var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        File.WriteAllText(Path.Combine(root, "root.txt"), "root");
        File.WriteAllText(Path.Combine(child, "child.txt"), "child");
        File.WriteAllText(Path.Combine(child, "child.bin"), "child");
        try
        {
            Assert.Single(CisPathSafety.EnumerateFiles(root, "*.txt", recursive: false));
            Assert.Equal(2, CisPathSafety.EnumerateFiles(root, "*.txt").Count());
            Assert.Single(CisPathSafety.EnumerateDirectories(root, recursive: false));
            Assert.Equal(4, CisPathSafety.EnumerateFileSystemEntries(root).Count());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ProcessRunner_BoundsOutputAndTerminatesTimeouts()
    {
        var output = Command("for /L %i in (1,1,500) do @echo 1234567890", "for i in $(seq 1 500); do echo 1234567890; done");
        var bounded = CisProcessSafety.Run(output, TimeSpan.FromSeconds(10), maximumCharactersPerStream: 128);
        Assert.Equal(0, bounded.ExitCode);
        Assert.True(bounded.OutputTruncated);
        Assert.True(bounded.StandardOutput.Length <= 128);

        var slow = Command("ping 127.0.0.1 -n 20 >nul", "sleep 20");
        var stopwatch = Stopwatch.StartNew();
        var timed = CisProcessSafety.Run(slow, TimeSpan.FromMilliseconds(100));
        stopwatch.Stop();
        Assert.True(timed.TimedOut);
        Assert.Null(timed.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Timed-out process took {stopwatch.Elapsed} to terminate.");
    }

    [Fact]
    public void ProcessRunner_ValidatesArgumentsAndCapturesBothStreamsAndExitCode()
    {
        Assert.Throws<ArgumentNullException>(() => CisProcessSafety.Run(null!, TimeSpan.FromSeconds(1)));
        var start = Command("exit /b 0", "exit 0");
        Assert.Throws<ArgumentOutOfRangeException>(() => CisProcessSafety.Run(start, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => CisProcessSafety.Run(start, TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CisProcessSafety.Run(start, TimeSpan.FromMilliseconds((double)int.MaxValue + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CisProcessSafety.Run(start, TimeSpan.FromSeconds(1), 0));

        var exactUpperTimeout = CisProcessSafety.Run(Command("exit /b 0", "exit 0"), TimeSpan.FromMilliseconds(int.MaxValue), 1);
        Assert.Equal(0, exactUpperTimeout.ExitCode);
        Assert.False(exactUpperTimeout.TimedOut);
        Assert.False(exactUpperTimeout.OutputTruncated);

        var command = Command("(echo out&echo err 1>&2&exit /b 7)", "printf out; printf err >&2; exit 7");
        var result = CisProcessSafety.Run(command, TimeSpan.FromSeconds(10), 32);
        Assert.Equal(7, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("out", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("err", result.StandardError, StringComparison.Ordinal);
        Assert.False(result.OutputTruncated);
        Assert.True(command.RedirectStandardOutput);
        Assert.True(command.RedirectStandardError);
        Assert.False(command.UseShellExecute);
        Assert.True(command.CreateNoWindow);
    }

    [Fact]
    public void ProcessRunner_HandlesExactAndOversizedOutputOnBothStreams()
    {
        var exact = CisProcessSafety.Run(Command("<nul set /p =12345678", "printf 12345678"), TimeSpan.FromSeconds(10), 8);
        Assert.Equal("12345678", exact.StandardOutput);
        Assert.False(exact.OutputTruncated);

        var oversized = CisProcessSafety.Run(
            Command("(for /L %i in (1,1,9000) do @<nul set /p =x)&(for /L %i in (1,1,9000) do @<nul set /p =e 1>&2)",
                "i=0; while [ $i -lt 9000 ]; do printf x; printf e >&2; i=$((i+1)); done"),
            TimeSpan.FromSeconds(20), 8_193);
        Assert.Equal(8_193, oversized.StandardOutput.Length);
        Assert.Equal(8_193, oversized.StandardError.Length);
        Assert.True(oversized.OutputTruncated);
    }

    [Fact]
    public void RecursiveEnumeration_DoesNotFollowSymbolicLinks()
    {
        var basis = TemporaryRoot("cis-path-tests");
        var root = Path.Combine(basis, "root"); var outside = Path.Combine(basis, "outside");
        Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(root, "inside.txt"), "inside");
        File.WriteAllText(Path.Combine(outside, "outside.txt"), "outside");
        var link = Path.Combine(root, "linked");
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { Directory.Delete(basis, true); return; }
        try
        {
            var files = CisPathSafety.EnumerateFiles(root).Select(Path.GetFileName).ToArray();
            Assert.Contains("inside.txt", files);
            Assert.DoesNotContain("outside.txt", files);
            Assert.True(CisPathSafety.ContainsReparsePoint(root, Path.Combine(link, "outside.txt")));
        }
        finally { Directory.Delete(basis, true); }
    }

    [Fact]
    public void ArchiveExtraction_RejectsDuplicateAndEscapingPaths()
    {
        var basis = TemporaryRoot("cis-archive-tests");
        Directory.CreateDirectory(basis);
        var archivePath = Path.Combine(basis, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            Write(archive.CreateEntry("safe/file.txt"), "one");
            Write(archive.CreateEntry("safe/file.txt"), "two");
            Write(archive.CreateEntry("../outside.txt"), "escape");
        }
        try
        {
            Assert.Throws<InvalidDataException>(() => CisArchiveSafety.ExtractZip(
                archivePath, Path.Combine(basis, "output"), 10, 1024));
            Assert.False(File.Exists(Path.Combine(basis, "outside.txt")));
        }
        finally { Directory.Delete(basis, true); }
    }

    [Fact]
    public void ArchiveExtraction_ValidatesLimitsAndExtractsFilesAndDirectories()
    {
        var basis = TemporaryRoot("cis-archive-tests");
        Directory.CreateDirectory(basis);
        var archivePath = Path.Combine(basis, "valid.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("empty/");
            Write(archive.CreateEntry("nested/one.txt"), "one");
            Write(archive.CreateEntry("nested/two.txt"), "two");
        }
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CisArchiveSafety.ExtractZip(archivePath, Path.Combine(basis, "zero-entry"), 0, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => CisArchiveSafety.ExtractZip(archivePath, Path.Combine(basis, "zero-byte"), 3, 0));
            Assert.Throws<InvalidDataException>(() => CisArchiveSafety.ExtractZip(archivePath, Path.Combine(basis, "too-many"), 2, 6));
            Assert.Throws<InvalidDataException>(() => CisArchiveSafety.ExtractZip(archivePath, Path.Combine(basis, "too-large"), 3, 5));

            var output = Path.Combine(basis, "output");
            CisArchiveSafety.ExtractZip(archivePath, output, 3, 6);
            Assert.True(Directory.Exists(Path.Combine(output, "empty")));
            Assert.Equal("one", File.ReadAllText(Path.Combine(output, "nested", "one.txt")));
            Assert.Equal("two", File.ReadAllText(Path.Combine(output, "nested", "two.txt")));
        }
        finally
        {
            Directory.Delete(basis, true);
        }
    }

    [Fact]
    public void ArchiveExtraction_RejectsCaseVariantsSymbolicLinksAndExistingTargets()
    {
        var basis = TemporaryRoot("cis-archive-tests");
        Directory.CreateDirectory(basis);
        try
        {
            var duplicate = Path.Combine(basis, "duplicate.zip");
            using (var archive = ZipFile.Open(duplicate, ZipArchiveMode.Create))
            {
                Write(archive.CreateEntry("safe/File.txt"), "one");
                Write(archive.CreateEntry("safe/file.txt"), "two");
            }
            var duplicateException = Assert.Throws<InvalidDataException>(() =>
                CisArchiveSafety.ExtractZip(duplicate, Path.Combine(basis, "duplicate-output"), 2, 16));
            Assert.Contains("duplicate", duplicateException.Message, StringComparison.OrdinalIgnoreCase);

            var symbolic = Path.Combine(basis, "symbolic.zip");
            using (var archive = ZipFile.Open(symbolic, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("linked");
                entry.ExternalAttributes = 0xA000 << 16;
                Write(entry, "target");
            }
            var symbolicException = Assert.Throws<InvalidDataException>(() =>
                CisArchiveSafety.ExtractZip(symbolic, Path.Combine(basis, "symbolic-output"), 1, 16));
            Assert.Contains("symbolic-link", symbolicException.Message, StringComparison.Ordinal);

            var existingArchive = Path.Combine(basis, "existing.zip");
            using (var archive = ZipFile.Open(existingArchive, ZipArchiveMode.Create))
                Write(archive.CreateEntry("file.txt"), "new");
            var existingOutput = Path.Combine(basis, "existing-output");
            Directory.CreateDirectory(existingOutput);
            File.WriteAllText(Path.Combine(existingOutput, "file.txt"), "old");
            Assert.Throws<IOException>(() => CisArchiveSafety.ExtractZip(existingArchive, existingOutput, 1, 3));
            Assert.Equal("old", File.ReadAllText(Path.Combine(existingOutput, "file.txt")));
        }
        finally
        {
            Directory.Delete(basis, true);
        }
    }

    [Fact]
    public void ArchiveExtraction_CreatesDestinationForEmptyArchiveAndSupportsEmptyFiles()
    {
        var basis = TemporaryRoot("cis-archive-tests");
        Directory.CreateDirectory(basis);
        try
        {
            var emptyArchive = Path.Combine(basis, "empty.zip");
            using (ZipFile.Open(emptyArchive, ZipArchiveMode.Create)) { }
            var emptyOutput = Path.Combine(basis, "empty-output");
            CisArchiveSafety.ExtractZip(emptyArchive, emptyOutput, 1, 1);
            Assert.True(Directory.Exists(emptyOutput));

            var zeroArchive = Path.Combine(basis, "zero.zip");
            using (var archive = ZipFile.Open(zeroArchive, ZipArchiveMode.Create)) archive.CreateEntry("zero.txt");
            var zeroOutput = Path.Combine(basis, "zero-output");
            CisArchiveSafety.ExtractZip(zeroArchive, zeroOutput, 1, 1);
            Assert.True(File.Exists(Path.Combine(zeroOutput, "zero.txt")));
            Assert.Equal(0, new FileInfo(Path.Combine(zeroOutput, "zero.txt")).Length);
        }
        finally
        {
            Directory.Delete(basis, true);
        }
    }

    [Fact]
    public void ArchiveExtraction_RejectsEscapesBeforeCreatingAFileAndCumulativeOverflowBeforeSecondFile()
    {
        var basis = TemporaryRoot("cis-archive-tests");
        Directory.CreateDirectory(basis);
        try
        {
            var escapeArchive = Path.Combine(basis, "escape.zip");
            using (var archive = ZipFile.Open(escapeArchive, ZipArchiveMode.Create))
                Write(archive.CreateEntry("../outside.txt"), "escape");
            var escape = Assert.Throws<InvalidDataException>(() =>
                CisArchiveSafety.ExtractZip(escapeArchive, Path.Combine(basis, "escape-output"), 1, 6));
            Assert.Equal("Archive contains an escaping or invalid path: ../outside.txt", escape.Message);
            Assert.False(File.Exists(Path.Combine(basis, "outside.txt")));

            var overflowArchive = Path.Combine(basis, "overflow.zip");
            using (var archive = ZipFile.Open(overflowArchive, ZipArchiveMode.Create))
            {
                Write(archive.CreateEntry("one.txt"), "one");
                Write(archive.CreateEntry("two.txt"), "two");
            }
            var overflowOutput = Path.Combine(basis, "overflow-output");
            var overflow = Assert.Throws<InvalidDataException>(() =>
                CisArchiveSafety.ExtractZip(overflowArchive, overflowOutput, 2, 5));
            Assert.Equal("Archive exceeds the 5 byte expanded-size limit.", overflow.Message);
            Assert.Equal("one", File.ReadAllText(Path.Combine(overflowOutput, "one.txt")));
            Assert.False(File.Exists(Path.Combine(overflowOutput, "two.txt")));
        }
        finally
        {
            Directory.Delete(basis, true);
        }
    }

    private static void Write(ZipArchiveEntry entry, string value)
    {
        using var writer = new StreamWriter(entry.Open()); writer.Write(value);
    }

    private static string TemporaryRoot(string prefix)
        => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static ProcessStartInfo Command(string windows, string unix)
    {
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe") : new ProcessStartInfo("/bin/sh");
        start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        start.ArgumentList.Add(OperatingSystem.IsWindows() ? windows : unix);
        return start;
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(link);
        }
    }
}
