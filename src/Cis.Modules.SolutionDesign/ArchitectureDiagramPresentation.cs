using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.SolutionDesign;

internal static class ArchitectureDiagramPresentation
{
    private const string Start = "<!-- cis:solution-design-diagrams:start -->";
    private const string End = "<!-- cis:solution-design-diagrams:end -->";
    private static readonly Regex Block = new(Regex.Escape(Start) + @"\r?\n<!-- content-sha256:(?<hash>[a-f0-9]{64}) -->\r?\n(?<body>.*?)" + Regex.Escape(End),
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string WithoutPreservedBlock(string original, string proposed)
    {
        var prior = ReadBlock(original); var next = ReadBlock(proposed);
        if (next.Length > 0 && next != prior) throw new InvalidDataException("Preserve the existing CIS diagram display block verbatim; CIS updates it from the model.");
        return next.Length == 0 ? proposed : proposed.Replace(next, "", StringComparison.Ordinal);
    }

    private static string ReadBlock(string content)
    {
        var matches = Block.Matches(content);
        if (!content.Contains("<!-- cis:solution-design-diagrams:", StringComparison.Ordinal)) return "";
        if (matches.Count != 1 || content.Split(Start, StringSplitOptions.None).Length != 2 || content.Split(End, StringSplitOptions.None).Length != 2
            || Hash(matches[0].Groups["body"].Value) != matches[0].Groups["hash"].Value)
            throw new InvalidDataException("Preserved modified or malformed CIS diagram display block; move human additions outside it before regeneration.");
        return matches[0].Value;
    }

    public static string Embed(string content, IReadOnlyList<ArchitectureImage> images)
    {
        var prior = ReadBlock(content);
        var body = new StringBuilder("\n## C4 solution diagrams\n\nThese diagrams zoom from the product's users and external systems into its applications and data stores, then into selected application components. Containers describe software boundaries; they do not assert deployed infrastructure.\n");
        foreach (var image in images)
            body.Append($"\n### {WebUtility.HtmlEncode(image.Title)}\n\n![{WebUtility.HtmlEncode(image.Title)}]({image.RelativePath})\n\n[Open full-size diagram]({image.RelativePath})\n\n{WebUtility.HtmlEncode(image.Notes)}\n");
        var text = body.ToString(); var block = $"{Start}\n<!-- content-sha256:{Hash(text)} -->\n{text}{End}";
        if (prior.Length > 0) return content.Replace(prior, block, StringComparison.Ordinal);
        const string managed = "<!-- cis:solution-design-managed:start -->";
        if (content.Split(managed, StringSplitOptions.None).Length != 2) throw new InvalidDataException("A unique solution-design managed block is required for the C4 display.");
        return content.Replace(managed, managed + "\n\n" + block, StringComparison.Ordinal);
    }

    public static void WriteAssets(string authority, string designPath, IReadOnlyList<ArchitectureImage> images)
    {
        var directory = Path.GetDirectoryName(designPath)!;
        foreach (var image in images)
        {
            var path = Path.Combine(directory, image.RelativePath);
            if (CisPathSafety.ContainsReparsePoint(authority, path)) throw new InvalidDataException("Unsafe architecture diagram asset path.");
            if (File.Exists(path) && File.ReadAllText(path) != image.Content) throw new InvalidDataException("Preserved modified architecture SVG: " + image.RelativePath);
        }
        foreach (var image in images)
        {
            var path = Path.Combine(directory, image.RelativePath);
            if (File.Exists(path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, image.Content, new UTF8Encoding(false)); File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
}
