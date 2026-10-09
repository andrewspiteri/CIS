using System.Globalization;
using System.Xml.Linq;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

/// <summary>Projects native Cobertura line hits onto a reviewed Git change scope. It does not run tests.</summary>
internal static class ChangedCoverageProjection
{
    public static TestCoverageSummary Read(TestResultAdapterContext context, XDocument report)
    {
        var scope = ChangedProductionScope.Read(context.RepositoryPath);
        var reported = ReadLines(context.RepositoryPath, report);
        var total = 0;
        var covered = 0;
        var uninstrumented = 0;
        foreach (var file in scope.Lines)
        {
            if (!reported.TryGetValue(file.Key, out var hits))
            {
                // An uninstrumented new production file must not disappear from the denominator.
                total += file.Value.Count;
                uninstrumented++;
                continue;
            }
            foreach (var line in hits.Where(line => file.Value.Contains(line.Key)))
            {
                total++;
                if (line.Value > 0) covered++;
            }
        }
        return new(total == 0 ? 0 : covered * 100d / total, 0, 0, 0, "changed-production",
            TestResultEvidence.Relative(context.RepositoryPath, context.CoveragePath!), total, covered, scope.BaseRevision,
            scope.Lines.Values.Sum(lines => lines.Count), uninstrumented);
    }

    private static Dictionary<string, Dictionary<int, long>> ReadLines(string repository, XDocument report)
    {
        if (report.Root?.Name.LocalName != "coverage") throw new InvalidDataException("Changed coverage currently requires native Cobertura XML.");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, Dictionary<int, long>>(comparer);
        var sources = report.Descendants("source").Select(source => source.Value).Append(repository).Distinct(comparer).ToArray();
        foreach (var type in report.Descendants("class"))
        {
            var name = type.Attribute("filename")?.Value;
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Cobertura class is missing its source path.");
            var candidates = sources.Select(root => Path.GetFullPath(Path.Combine(root, name)))
                .Where(path => CisPathSafety.IsUnderRoot(repository, path) && File.Exists(path))
                .Distinct(comparer).ToArray();
            // Roslyn may emit virtual generated-source paths beneath obj without writing files.
            // Those are not production inputs; missing actual changed files still count as uncovered.
            if (candidates.Length == 0 && name.Replace('\\', '/').Split('/').Contains("obj", StringComparer.OrdinalIgnoreCase)) continue;
            if (candidates.Length != 1) throw new InvalidDataException("Cobertura source path cannot be resolved uniquely inside the repository.");
            var path = candidates[0];
            if (CisPathSafety.ContainsReparsePoint(repository, path)) throw new InvalidDataException("Cobertura source traverses a linked path.");
            if (!result.TryGetValue(path, out var lines)) result[path] = lines = [];
            foreach (var line in type.Descendants("line"))
            {
                if (!int.TryParse(line.Attribute("number")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1
                    || !long.TryParse(line.Attribute("hits")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hits) || hits < 0)
                    throw new InvalidDataException("Cobertura line numbers and hits must be nonnegative integers.");
                lines[number] = Math.Max(lines.GetValueOrDefault(number), hits);
            }
        }
        return result;
    }
}
