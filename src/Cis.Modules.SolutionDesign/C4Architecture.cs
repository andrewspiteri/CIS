using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.SolutionDesign;

/// <summary>C4 scope and identity rules shared by inference, validation and rendering.</summary>
internal static class C4Architecture
{
    public static void Validate(ArchitectureDiagramModel model)
    {
        static bool Id(string? value) => value is not null && Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_-]{0,39}$", RegexOptions.CultureInvariant);
        static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max
            && !value.Any(char.IsControl) && !value.Contains("-->", StringComparison.Ordinal);
        static bool Status(string? value) => value is "observed" or "proposed" or "unresolved";
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException("C4: " + message); }
        Require(model.Views is { Count: >= 3 and <= 10 } && model.Views.All(v => v is not null), "provide context, container and at least one scoped component view (3–10 views).");
        var views = model.Views!;
        Require(views.All(v => Id(v.Id) && Id(v.ScopeId) && Text(v.Title, 100) && Text(v.Notes, 800)
            && v.Level is "context" or "container" or "component"), "views require safe IDs, a C4 level, scope, title and evidence limitations.");
        Require(views.Select(v => v.Id).Distinct(StringComparer.Ordinal).Count() == views.Count, "view IDs must be unique.");
        Require(views.Count(v => v.Level == "context") == 1 && views.Count(v => v.Level == "container") == 1, "provide exactly one context and one container view.");
        Require(views.Where(v => v.Level == "component").Select(v => v.ScopeId).Distinct().Count() == views.Count(v => v.Level == "component"), "use one component view per selected container.");
        foreach (var v in views)
        {
            Require(v.Nodes is { Count: >= 2 and <= 16 } && v.Edges is { Count: >= 1 and <= 24 }, "each view needs 2–16 nodes and 1–24 directed relationships.");
            Require(v.Nodes!.All(n => n is not null && Id(n.Id) && Text(n.Label, 70) && Text(n.Description, 160)
                && n.Layer is >= 0 and <= 3 && Status(n.Status) && n.Kind is "person" or "software-system" or "container" or "component"
                && (n.ParentId is null || Id(n.ParentId))
                && (n.Kind is "container" or "component" ? Text(n.Technology, 70) : n.Technology is null)), "nodes need identity, type, responsibility and status; applications/data stores/components also need technology (or explicitly unresolved).");
            var ids = v.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            Require(ids.Count == v.Nodes.Count, "node IDs must be unique within a view.");
            Require(v.Edges!.All(e => e is not null && ids.Contains(e.From) && ids.Contains(e.To) && e.From != e.To
                && Text(e.Label, 100) && Status(e.Status) && (v.Level == "context" ? e.Technology is null : Text(e.Technology, 70))), "relationships need existing endpoints, direction, action, status and technology below context level.");
            Require(ids.All(id => v.Edges.Any(e => e.From == id || e.To == id)), "every displayed node must participate in a relationship.");
        }
        var all = views.SelectMany(v => v.Nodes).GroupBy(n => n.Id, StringComparer.Ordinal).ToArray();
        Require(all.All(g => g.Select(n => n with { Layer = 0 }).Distinct().Count() == 1), "reuse the same identity, type and description across views; only layout may change.");
        var context = views.Single(v => v.Level == "context"); var containers = views.Single(v => v.Level == "container");
        Require(context.ScopeId == containers.ScopeId && context.Nodes.Any(n => n.Id == context.ScopeId && n.Kind == "software-system"), "context and container views must zoom into the same software system.");
        Require(context.Nodes.All(n => n.Kind is "person" or "software-system" && n.ParentId is null), "context shows only people and software systems, without applications or data stores.");
        Require(context.Nodes.All(n => n.Id == context.ScopeId ? n.Layer is 1 or 2 : n.Layer is 0 or 3), "place the scoped system in the centre and external context in layers 0/3.");
        Require(containers.Nodes.All(n => n.Kind is "person" or "software-system" ? n.ParentId is null && n.Id != context.ScopeId : n.Kind == "container" && n.ParentId == context.ScopeId), "container view shows applications/data stores owned by the scoped system and external actors/systems.");
        Require(containers.Nodes.Any(n => n.Kind == "container"), "container view must identify the product's applications or data stores.");
        foreach (var v in views.Where(v => v.Level == "component"))
        {
            Require(containers.Nodes.Any(n => n.Id == v.ScopeId && n.Kind == "container"), "component scope must be an application/container from the container view.");
            Require(v.Nodes.Any(n => n.Kind == "component"), "component view must expose components inside its scoped container.");
            Require(v.Nodes.All(n => n.Kind == "component" ? n.ParentId == v.ScopeId
                : containers.Nodes.Any(c => c.Id == n.Id) && n.Id != v.ScopeId), "component views may only contain components of one container and its direct context.");
        }
        foreach (var v in views.Where(v => v.Level != "context"))
            Require(v.Nodes.All(n => n.ParentId == v.ScopeId ? n.Layer is 1 or 2 : n.Layer is 0 or 3), "place elements inside the scope in layers 1/2 and supporting context in layers 0/3 so boundaries remain unambiguous.");
    }

    public static string RenderSvg(ArchitectureDiagramModel model, ArchitectureView view)
    {
        const int width = 1510, cardWidth = 270;
        var cardHeight = view.Nodes.Max(n => 66 + Wrap(n.Label, 27).Count() * 22 + Wrap(n.Description!, 35).Count() * 17
            + (n.Technology is null ? 0 : Wrap(n.Technology, 35).Count() * 16));
        var rowHeight = cardHeight + 40;
        var nodes = view.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var positions = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal);
        foreach (var group in view.Nodes.GroupBy(n => n.Layer))
        {
            var index = 0;
            foreach (var n in group) positions[n.Id] = (35 + n.Layer * 390, 154 + index++ * rowHeight);
        }
        var rows = view.Nodes.GroupBy(n => n.Layer).Max(g => g.Count());
        if (view.Level == "context")
            positions[view.ScopeId!] = (620, 154 + (rows - 1) * rowHeight / 2);
        var longEdges = view.Edges.Select(e => view.Level != "context" && Math.Abs(positions[e.From].X - positions[e.To].X) > 390).ToArray();
        var graphEnd = 154 + rows * rowHeight;
        var ledgerTop = graphEnd + longEdges.Count(e => e) * 24 + 64;
        var notes = Wrap(view.Notes, 165).ToArray();
        var height = ledgerTop + ((view.Edges.Count + 1) / 2) * 108 + 68 + notes.Length * 21;
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" role=\"img\" aria-labelledby=\"title description\">\n<title id=\"title\">{E(view.Title)}</title><desc id=\"description\">{E(view.Notes)}</desc>\n<rect width=\"100%\" height=\"100%\" fill=\"#f8fafc\"/><g font-family=\"Segoe UI, Arial, sans-serif\">\n");
        Text(svg, 35, 40, view.Title, 26, "#0f172a", true);
        var scope = model.Views.SelectMany(v => v.Nodes).First(n => n.Id == view.ScopeId);
        var level = view.Level == "context" ? "C1 · System context" : view.Level == "container" ? "C2 · Containers" : "C3 · Components";
        Text(svg, 35, 71, $"{level}   |   Scope: {scope.Label}", 16, "#334155");
        Text(svg, 35, 99, "Solid = observed · Dashed = proposed / unresolved · Numbered arrows are explained below", 14, "#475569");
        if (view.Level != "context")
        {
            svg.AppendLine($"<rect x=\"397\" y=\"122\" width=\"716\" height=\"{rows * rowHeight + 16}\" rx=\"8\" fill=\"#eff6ff\" stroke=\"#2563eb\" stroke-dasharray=\"9 5\"/>");
            Text(svg, 414, 144, $"{scope.Label} [{(view.Level == "container" ? "Software system" : "Container")} boundary]", 14, "#1e40af");
        }
        var labels = new List<(int X, int Y)>();
        var bus = graphEnd;
        for (var i = 0; i < view.Edges.Count; i++)
        {
            var edge = view.Edges[i]; var a = positions[edge.From]; var b = positions[edge.To];
            var forward = b.X > a.X; var same = a.X == b.X;
            var x1 = a.X + (forward || same ? cardWidth : 0); var x2 = b.X + (forward ? 0 : cardWidth);
            int Port(string id, int y) => y + 25 + (cardHeight - 50) * (view.Edges.Take(i).Count(e => e.From == id || e.To == id) + 1)
                / (view.Edges.Count(e => e.From == id || e.To == id) + 1);
            var y1 = Port(edge.From, a.Y); var y2 = Port(edge.To, b.Y);
            // Only relationships spanning intermediate columns need a track below the boxes.
            var departure = x1 + (forward || same ? 12 : -12) + (forward || same ? 1 : -1) * (i % 8) * 3;
            var arrival = x2 + (forward ? -12 : 12) + (forward ? -1 : 1) * (i % 8) * 3;
            var dash = edge.Status == "observed" ? "" : " stroke-dasharray=\"6 4\"";
            var color = edge.Status == "observed" ? "#64748b" : edge.Status == "proposed" ? "#15803d" : "#a16207";
            var labelX = (departure + arrival) / 2; var labelY = bus;
            if (longEdges[i])
            {
                svg.AppendLine($"<path d=\"M{x1} {y1} H{departure} V{bus} H{arrival} V{y2} H{x2}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.7\" stroke-linejoin=\"round\"{dash}/>");
                bus += 24;
            }
            else
            {
                var bend = same ? 32 + i % 3 * 8 : Math.Abs(x2 - x1) / 2;
                var c1 = x1 + (forward || same ? bend : -bend); var c2 = x2 + (forward ? -bend : bend);
                svg.AppendLine($"<path d=\"M{x1} {y1} C{c1} {y1}, {c2} {y2}, {x2} {y2}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.7\"{dash}/>");
                labelX = (x1 + 3 * c1 + 3 * c2 + x2) / 8; labelY = (y1 + y2) / 2;
            }
            var tip = forward ? -7 : 7;
            svg.AppendLine($"<path d=\"M{x2 + tip} {y2 - 5} L{x2} {y2} L{x2 + tip} {y2 + 5}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"/>");
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var adjustedY = labelY + (attempt == 0 ? 0 : (attempt % 2 == 0 ? -1 : 1) * ((attempt + 1) / 2) * 25);
                if (adjustedY < 154 || adjustedY > ledgerTop - 40
                    || labels.Any(p => Math.Abs(p.X - labelX) < 28 && Math.Abs(p.Y - adjustedY) < 24)
                    || positions.Values.Any(p => labelX + 14 > p.X && labelX - 14 < p.X + cardWidth && adjustedY + 12 > p.Y && adjustedY - 12 < p.Y + cardHeight)) continue;
                labelY = adjustedY; break;
            }
            labels.Add((labelX, labelY));
            svg.AppendLine($"<rect x=\"{labelX - 13}\" y=\"{labelY - 11}\" width=\"26\" height=\"22\" rx=\"5\" fill=\"white\" stroke=\"{color}\"/>");
            Text(svg, labelX - (i < 9 ? 4 : 8), labelY + 5, (i + 1).ToString(), 13, "#0f172a", true);
        }
        foreach (var n in view.Nodes)
        {
            var p = positions[n.Id]; var owned = n.Id == view.ScopeId || n.ParentId == view.ScopeId;
            var color = n.Status == "unresolved" ? "#854d0e" : n.Status == "proposed" ? "#166534" : owned ? "#1d4ed8" : "#475569";
            var dash = n.Status == "observed" ? "" : " stroke-dasharray=\"6 4\"";
            svg.AppendLine($"<rect x=\"{p.X}\" y=\"{p.Y}\" width=\"{cardWidth}\" height=\"{cardHeight}\" rx=\"{(n.Kind == "person" ? 32 : 6)}\" fill=\"{color}\" stroke=\"#0f172a\"{dash}/>");
            var y = p.Y + 27;
            foreach (var line in Wrap(n.Label, 27)) { Text(svg, p.X + 16, y, line, 17, "white", true); y += 22; }
            Text(svg, p.X + 16, y + 1, $"[{n.Kind}] · {n.Status}", 12, "#e2e8f0"); y += 23;
            foreach (var line in Wrap(n.Description!, 35)) { Text(svg, p.X + 16, y, line, 13, "white"); y += 17; }
            if (n.Technology is not null)
                foreach (var line in Wrap(n.Technology, 35)) { Text(svg, p.X + 16, y + 3, line, 12, "#dbeafe"); y += 16; }
        }
        Text(svg, 35, ledgerTop - 21, "Directed relationships", 21, "#0f172a", true);
        for (var i = 0; i < view.Edges.Count; i++)
        {
            var e = view.Edges[i]; var x = 35 + i % 2 * 745; var y = ledgerTop + i / 2 * 108;
            foreach (var line in Wrap($"{i + 1}. {nodes[e.From].Label} → {nodes[e.To].Label}", 77)) { Text(svg, x, y, line, 14, "#0f172a", true); y += 18; }
            foreach (var line in Wrap(e.Label, 83)) { Text(svg, x, y, line, 14, "#334155"); y += 18; }
            foreach (var line in Wrap($"{e.Technology ?? "Business interaction"} · {e.Status}", 88)) { Text(svg, x, y, line, 13, "#475569"); y += 17; }
        }
        var noteY = ledgerTop + ((view.Edges.Count + 1) / 2) * 108 + 12;
        Text(svg, 35, noteY, "Evidence and limitations", 16, "#0f172a", true);
        foreach (var line in notes) { noteY += 21; Text(svg, 35, noteY, line, 14, "#475569"); }
        return svg.AppendLine("</g></svg>").ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        while (text.Length > width)
        {
            var cut = text.LastIndexOf(' ', width); if (cut < width / 2) cut = width;
            yield return text[..cut]; text = text[cut..].TrimStart();
        }
        if (text.Length > 0) yield return text;
    }
    private static string E(string text) => WebUtility.HtmlEncode(text);
    private static void Text(StringBuilder svg, int x, int y, string text, int size, string color, bool bold = false)
        => svg.AppendLine($"<text x=\"{x}\" y=\"{y}\" font-size=\"{size}\" fill=\"{color}\"{(bold ? " font-weight=\"600\"" : "")}>{E(text)}</text>");
}
