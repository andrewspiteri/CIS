using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Graph;

internal static class CSharpAnalysisCache
{
    internal static CSharpCompilerAnalysis Analyze(string repository, IReadOnlyList<CSharpCompilerInput> inputs)
    {
        using var timing = CisPerformanceTrace.Start("graph.compiler");
        // A whole-compilation key invalidates cross-file bindings on any source or ownership change.
        var identity = JsonSerializer.Serialize(new { version = 1, runtime = Environment.Version.ToString(),
            module = typeof(CSharpCompilerAnalyzer).Module.ModuleVersionId,
            compiler = typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.FullName,
            inputs = inputs.OrderBy(item => item.Path, StringComparer.Ordinal).Select(item => new {
                item.Path, item.ComponentId, hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Content))) }) });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var path = Path.Combine(repository, ".cis", "local", "graph", "compiler-analysis.json");
        var safe = !CisPathSafety.ContainsReparsePoint(repository, path);
        if (safe)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length <= 64 * 1024 * 1024)
                {
                    var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
                    if (entry?.Key == key && entry.Analysis is { Symbols: not null, ReferencedSymbols: not null, Annotations: not null, Interfaces: not null, Calls: not null })
                    { timing.Mark("cache-hit"); return entry.Analysis; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        var analysis = CSharpCompilerAnalyzer.Analyze(inputs);
        timing.Mark("analyze");
        if (safe)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(new Entry(key, analysis)));
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
        }
        return analysis;
    }

    private sealed record Entry(string Key, CSharpCompilerAnalysis Analysis);
}
