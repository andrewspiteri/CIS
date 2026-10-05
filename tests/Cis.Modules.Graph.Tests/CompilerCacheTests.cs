using System.Diagnostics;
using System.Text.Json;

namespace Cis.Modules.Graph.Tests;

public sealed class CompilerCacheTests(ITestOutputHelper output)
{
    [Fact]
    public void CacheReusesAnalysisAndInvalidatesSourceAndOwnershipChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "cis-compiler-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var inputs = Enumerable.Range(0, 100).Select(i => new CSharpCompilerInput($"Class{i}.cs",
                $"public class Class{i} {{ public int Value() => {i}; }}", "component")).ToArray();
            var clock = Stopwatch.StartNew();
            var first = CSharpAnalysisCache.Analyze(root, inputs);
            var cold = clock.Elapsed.TotalMilliseconds;
            var path = Path.Combine(root, ".cis/local/graph/compiler-analysis.json");
            var original = File.ReadAllBytes(path);
            clock.Restart();
            var second = CSharpAnalysisCache.Analyze(root, inputs);
            var warm = clock.Elapsed.TotalMilliseconds;
            output.WriteLine($"Compiler cache fixture (100 source files): cold={cold:F1}ms, cached={warm:F1}ms");
            Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
            Assert.Equal(original, File.ReadAllBytes(path));
            inputs[0] = inputs[0] with { Content = "public class Changed { }", ComponentId = "new-owner" };
            var changed = CSharpAnalysisCache.Analyze(root, inputs);
            Assert.Contains(changed.Symbols, item => item.Name == "Changed" && item.ComponentId == "new-owner");
            Assert.DoesNotContain(changed.Symbols, item => item.Name == "Class0");
            File.WriteAllText(path, "invalid derived cache");
            Assert.Equal(JsonSerializer.Serialize(changed), JsonSerializer.Serialize(CSharpAnalysisCache.Analyze(root, inputs)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
