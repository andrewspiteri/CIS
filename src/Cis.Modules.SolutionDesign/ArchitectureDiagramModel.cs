using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cis.Modules.SolutionDesign;

/// <summary>Bounded, data-only architecture views. No arbitrary SVG, script or Mermaid is executed.</summary>
public sealed record ArchitectureDiagramModel(IReadOnlyList<ArchitectureView> Views)
{
    public const string Marker = "<!-- cis:architecture-views";
    private static readonly string[] RequiredIds = ["system-context", "component-topology", "integration-trust", "deployment-operations"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ArchitectureDiagramModel ReadRequired(string content)
    {
        try
        {
            var matches = Regex.Matches(content, @"<!-- cis:architecture-views\r?\n(?<json>.*?)\r?\n-->",
                RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (matches.Count != 1) throw new InvalidDataException("Architecture inference requires exactly one hidden cis:architecture-views JSON model.");
            var json = matches[0].Groups["json"].Value;
            if (json.Length > 100_000) throw new InvalidDataException("Architecture diagram model exceeds its bounded size.");
            var model = JsonSerializer.Deserialize<ArchitectureDiagramModel>(json, JsonOptions);
            if (model?.Views is null || model.Views.Count != 4 || model.Views.Any(view => view is null)
                || !model.Views.Select(view => view.Id).Order(StringComparer.Ordinal).SequenceEqual(RequiredIds.Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Architecture diagrams must contain exactly the four required named views.");
            foreach (var view in model.Views)
            {
                if (!Label(view.Title, 100) || !Label(view.Notes, 800) || view.Nodes is null || view.Edges is null
                    || view.Nodes.Count is < 1 or > 16 || view.Edges.Count > 24)
                    throw new InvalidDataException("Architecture views require a title, evidence limitations, 1–16 nodes and at most 24 relationships.");
                if (view.Nodes.Any(node => node is null || !Id(node.Id) || !Label(node.Label, 100) || node.Layer is < 0 or > 3
                        || node.Status is not ("observed" or "unresolved" or "proposed"))
                    || view.Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != view.Nodes.Count)
                    throw new InvalidDataException("Architecture nodes need unique safe IDs, bounded labels/layers and observed, unresolved or proposed status.");
                var ids = view.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
                if (view.Edges.Any(edge => edge is null || !ids.Contains(edge.From) || !ids.Contains(edge.To)
                        || edge.From == edge.To || !Label(edge.Label, 120) || edge.Status is not ("observed" or "unresolved" or "proposed")))
                    throw new InvalidDataException("Architecture relationships must name existing distinct nodes, a bounded label and evidence status.");
            }
            return model;
        }
        catch (JsonException exception) { throw new InvalidDataException("Malformed architecture diagram JSON: " + exception.Message, exception); }
    }

    public IReadOnlyList<ArchitectureImage> Render()
        => Views.Select(view =>
        {
            var svg = RenderSvg(view);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg))).ToLowerInvariant();
            return new ArchitectureImage(view.Id, view.Title, $"architecture-diagrams/{view.Id}-{hash}.svg", svg, view.Notes);
        }).ToArray();

    private static bool Id(string? value) => value is not null && Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9_-]{0,39}$", RegexOptions.CultureInvariant);
    private static bool Label(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum
        && !value.Any(char.IsControl) && !value.Contains("-->", StringComparison.Ordinal);

    private static string RenderSvg(ArchitectureView view)
    {
        var layers = view.Nodes.Select(node => node.Layer).Distinct().Order().ToArray();
        var rows = view.Nodes.GroupBy(node => node.Layer).Max(group => group.Count());
        var longEdges = view.Edges.Select(edge => Math.Abs(Array.IndexOf(layers, view.Nodes.First(node => node.Id == edge.From).Layer)
            - Array.IndexOf(layers, view.Nodes.First(node => node.Id == edge.To).Layer)) > 1).ToArray();
        var nodeAreaHeight = 115 + rows * 145;
        var graphHeight = nodeAreaHeight + longEdges.Count(value => value) * 26;
        var noteLines = Wrap(view.Notes, 140).ToArray();
        var legendLines = view.Edges.Select((edge, i) => Wrap($"{i + 1}. {view.Nodes.First(node => node.Id == edge.From).Label} → {view.Nodes.First(node => node.Id == edge.To).Label}: {edge.Label} ({edge.Status})", 137).ToArray()).ToArray();
        var height = graphHeight + 95 + noteLines.Length * 19 + legendLines.Sum(lines => lines.Length * 19 + 10);
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1240\" height=\"{height}\" viewBox=\"0 0 1240 {height}\" role=\"img\" aria-labelledby=\"title\">\n<title id=\"title\">{Escape(view.Title)}</title>\n<rect width=\"100%\" height=\"100%\" rx=\"12\" fill=\"#f8fafc\"/>\n<g font-family=\"Segoe UI, Arial, sans-serif\" fill=\"#0f172a\">\n");
        Text(svg, 28, 34, view.Title, Math.Min(24, (int)(1120 / Math.Max(1, view.Title.Length * 0.65))));
        Text(svg, 28, 60, "Observed implementation · proposed changes and unresolved details are labelled for review", 14);
        var positions = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            var column = Array.IndexOf(layers, layer);
            var index = 0;
            foreach (var node in view.Nodes.Where(node => node.Layer == layer))
                positions[node.Id] = (28 + column * (1184 / layers.Length), 100 + index++ * 145);
        }
        var labels = new List<(int X, int Y)>();
        var bus = nodeAreaHeight;
        for (var index = 0; index < view.Edges.Count; index++)
        {
            var edge = view.Edges[index]; var from = positions[edge.From]; var to = positions[edge.To];
            var forward = to.X > from.X;
            var x1 = from.X + (forward ? 240 : 0); var x2 = to.X + (forward ? 0 : 240);
            var outgoing = view.Edges.Take(index).Count(item => item.From == edge.From);
            var incoming = view.Edges.Take(index).Count(item => item.To == edge.To);
            var y1 = from.Y + 18 + (outgoing + 1) * 70 / (view.Edges.Count(item => item.From == edge.From) + 1);
            var y2 = to.Y + 18 + (incoming + 1) * 70 / (view.Edges.Count(item => item.To == edge.To) + 1);
            var bend = Math.Abs(x2 - x1) / 2 + 20;
            if (from.X == to.X) { x1 = from.X + 240; x2 = to.X + 240; bend = 22 + index % 3 * 12; }
            var control1 = from.X == to.X ? x1 + bend : x1 + (forward ? bend : -bend);
            var control2 = from.X == to.X ? x2 + bend : x2 + (forward ? -bend : bend);
            var dash = edge.Status == "observed" ? "" : " stroke-dasharray=\"6 4\"";
            var labelX = (x1 + x2 + control1 + control2) / 4; var labelY = (y1 + y2) / 2;
            if (longEdges[index])
            {
                // Route past intermediate columns below the node area, never through an unrelated box.
                var departure = x1 + (forward ? 14 : -14); var arrival = x2 + (forward ? -14 : 14);
                svg.AppendLine($"<path d=\"M{x1} {y1} H{departure} V{bus} H{arrival} V{y2} H{x2}\" fill=\"none\" stroke=\"#64748b\" stroke-width=\"2\" stroke-linejoin=\"round\"{dash}/>");
                labelX = (departure + arrival) / 2; labelY = bus; bus += 26;
            }
            else svg.AppendLine($"<path d=\"M{x1} {y1} C{control1} {y1}, {control2} {y2}, {x2} {y2}\" fill=\"none\" stroke=\"#64748b\" stroke-width=\"2\"{dash}/>");
            var tip = forward ? -7 : 7;
            svg.AppendLine($"<path d=\"M{x2 + tip} {y2 - 5} L{x2} {y2} L{x2 + tip} {y2 + 5}\" fill=\"none\" stroke=\"#64748b\" stroke-width=\"2\"/>");
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var adjustedY = labelY + (attempt == 0 ? 0 : (attempt % 2 == 0 ? -1 : 1) * ((attempt + 1) / 2) * 26);
                if (adjustedY < 82 || adjustedY > graphHeight + 2
                    || labels.Any(label => Math.Abs(label.X - labelX) < 24 && Math.Abs(label.Y - adjustedY) < 24)
                    || positions.Values.Any(position => labelX + 12 > position.X && labelX - 12 < position.X + 240
                        && adjustedY + 12 > position.Y && adjustedY - 12 < position.Y + 108)) continue;
                if (adjustedY != labelY) svg.AppendLine($"<path d=\"M{labelX} {labelY} V{adjustedY}\" stroke=\"#94a3b8\" fill=\"none\"/>");
                svg.AppendLine($"<circle cx=\"{labelX}\" cy=\"{adjustedY}\" r=\"11\" fill=\"white\" stroke=\"#94a3b8\"/>");
                Text(svg, labelX - (index >= 9 ? 7 : 4), adjustedY + 4, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 12);
                labels.Add((labelX, adjustedY)); break;
            }
        }
        foreach (var node in view.Nodes)
        {
            var (x, y) = positions[node.Id];
            var fill = node.Status switch { "observed" => "#dbeafe", "proposed" => "#dcfce7", _ => "#fef3c7" };
            svg.AppendLine($"<rect x=\"{x}\" y=\"{y}\" width=\"240\" height=\"108\" rx=\"8\" fill=\"{fill}\" stroke=\"#94a3b8\"/>");
            var line = 0;
            foreach (var text in Wrap(node.Label, 29)) Text(svg, x + 12, y + 24 + line++ * 19, text, 15);
            Text(svg, x + 12, y + 96, node.Status, 12);
        }
        var cursor = graphHeight + 12;
        Text(svg, 28, cursor, "Relationships", 18); cursor += 28;
        foreach (var lines in legendLines)
        {
            foreach (var line in lines) { Text(svg, 28, cursor, line, 14); cursor += 19; }
            cursor += 10;
        }
        if (legendLines.Length == 0) { Text(svg, 28, cursor, "No evidenced relationships are asserted in this view.", 14); cursor += 24; }
        foreach (var line in noteLines) { Text(svg, 28, cursor, line, 13); cursor += 19; }
        return svg.AppendLine("</g></svg>").ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Wrap(string value, int maximum)
    {
        while (value.Length > maximum)
        {
            var split = value.LastIndexOf(' ', maximum);
            if (split < maximum / 2) split = maximum;
            yield return value[..split]; value = value[split..].TrimStart();
        }
        if (value.Length > 0) yield return value;
    }
    private static void Text(StringBuilder svg, int x, int y, string text, int size)
        => svg.AppendLine($"<text x=\"{x}\" y=\"{y}\" font-size=\"{size}\">{Escape(text)}</text>");
    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}

public sealed record ArchitectureView(string Id, string Title, string Notes, IReadOnlyList<ArchitectureNode> Nodes, IReadOnlyList<ArchitectureEdge> Edges);
public sealed record ArchitectureNode(string Id, string Label, int Layer, string Status);
public sealed record ArchitectureEdge(string From, string To, string Label, string Status);
public sealed record ArchitectureImage(string Id, string Title, string RelativePath, string Content, string Notes);
