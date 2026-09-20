using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.UiDirection;

/// <summary>Source-informed reference specimens, never screenshots or executed product components.</summary>
internal static class UiBaselineControlSheet
{
    private static readonly (string Kind, string Title, string Pattern)[] Families =
    [
        ("buttons", "Buttons", @"<(?:button|p-button|Button|app-button|ui-button)\b"),
        ("fields", "Text fields", @"<(?:input|textarea|TextField|InputText|app-input|ui-input)\b|\bpInputText\b"),
        ("selects", "Dropdowns", @"<(?:select|p-select|p-dropdown|p-multiselect|mat-select|Select|Dropdown)\b"),
        ("choices", "Checkboxes and choices", @"<(?:p-checkbox|p-radiobutton|mat-checkbox|Checkbox|Radio|p-toggleswitch)\b|\btype\s*=\s*['""](?:checkbox|radio)['""]"),
        ("tables", "Tables", @"<(?:table|p-table|mat-table|DataTable|Table)\b"),
        ("cards", "Cards", @"<(?:p-card|mat-card|Card|app-card|ui-card)\b|\bclass\s*=\s*['""][^'""\r\n]*\bcard\b"),
        ("tabs", "Tabs", @"<(?:p-tabs|p-tabview|mat-tab-group|Tabs)\b|\brole\s*=\s*['""]tablist['""]"),
        ("dialogs", "Dialogs", @"<(?:dialog|p-dialog|p-confirmdialog|mat-dialog-content|Dialog|Modal|app-modal)\b|\brole\s*=\s*['""]dialog['""]"),
        ("feedback", "Messages and progress", @"<(?:p-toast|p-message|p-messages|p-progressspinner|p-skeleton|mat-progress-spinner|app-alert|app-spinner|Alert|Progress)\b|\baria-live\s*=")
    ];

    public static IReadOnlyList<CisUiBaselineControl> Discover(IEnumerable<(string Path, string Text)> files)
    {
        var sources = files.ToArray();
        return Families.Select(family => new CisUiBaselineControl(family.Kind, family.Title,
            sources.SelectMany(file => Regex.Matches(file.Text, family.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Take(1).Select(match => new CisUiBaselineEvidence(file.Path, 1 + file.Text.AsSpan(0, match.Index).Count('\n'))))
                .Take(3).ToArray())).Where(control => control.Evidence.Count > 0).ToArray();
    }

    public static CisUiBaselinePreview? Render(CisUiBaselineRepository repository)
    {
        if (repository.Controls.Count == 0) return null;
        var theme = Theme(repository);
        const int width = 1200;
        var height = 242 + ((repository.Controls.Count + 1) / 2) * 252;
        var svg = new StringBuilder($"""
<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
<title>{Xml(repository.Id)} — UI control sheet</title>
<desc>Source-based reference renderings of observed control families. Examples are reconstructed, not runtime screenshots.</desc>
<rect width="{width}" height="{height}" fill="#f4f5f7"/>
<g font-family="{Xml(theme.Font)}, Segoe UI, sans-serif" fill="#182230">
<text x="40" y="42" font-size="13" letter-spacing="2">EXISTING INTERFACE / CONTROL SHEET</text>
<text x="40" y="84" font-size="30" font-weight="700">{Xml(Short(repository.Id, 62))}</text>
<text x="40" y="118" font-size="15">Source-based reconstruction · {repository.Controls.Count} observed control families · human review required</text>
<text x="40" y="148" font-size="14" fill="#526070">{Xml(Short(theme.Description, 145))}</text>
<text x="40" y="175" font-size="13" fill="#526070">Example content and states; geometry uses reference defaults. Local font availability may affect typography.</text>
""");
        for (var index = 0; index < repository.Controls.Count; index++)
        {
            var control = repository.Controls[index];
            var x = 40 + (index % 2) * 576;
            var y = 202 + (index / 2) * 252;
            svg.Append($"""
<g transform="translate({x} {y})">
<rect width="544" height="228" rx="12" fill="#ffffff" stroke="#d8dee7"/>
<text x="24" y="34" font-size="17" font-weight="700">{index + 1:00} / {Xml(control.Title)}</text>
<text x="24" y="58" font-size="12" fill="#64748b">{Xml(Short(control.Evidence[0].RelativePath, 64))}:{control.Evidence[0].Line}</text>
<g transform="translate(24 78)">{Specimen(control.Kind, theme)}</g>
</g>
""");
        }
        svg.Append($"<text x=\"40\" y=\"{height - 18}\" font-size=\"12\" fill=\"#526070\">Baseline review aid · Compare with the running interface before approving exact control appearance.</text></g></svg>");
        return new(svg.ToString(), width, height,
            "Rendered reference controls using observed theme values. Reconstructed examples; exact component styling, states and font availability require comparison with the running interface.");
    }

    private static string Specimen(string kind, SheetTheme t)
    {
        string Box(int x, int y, int w, int h, string fill = "#ffffff", string stroke = "#cbd5e1") =>
            $"<rect x=\"{x}\" y=\"{y}\" width=\"{w}\" height=\"{h}\" rx=\"{t.Radius}\" fill=\"{fill}\" stroke=\"{stroke}\"/>";
        static string Text(int x, int y, string value, string fill = "#182230", int size = 14) =>
            $"<text x=\"{x}\" y=\"{y}\" fill=\"{fill}\" font-size=\"{size}\">{Xml(value)}</text>";
        string Button(int x, string label, bool secondary = false) => Box(x, 8, 144, 42, secondary ? "#ffffff" : t.Primary, t.Primary)
            + Text(x + 16, 35, label, secondary ? t.Primary : Ink(t.Primary));
        return kind switch
        {
            "buttons" => Button(0, "Primary action") + Button(158, "Secondary", true) + Box(316, 8, 150, 42, "#e9edf2", "#e9edf2")
                + Text(336, 35, "Disabled", "#758195") + Text(0, 91, "Reference variants: primary · secondary · disabled", "#64748b", 12),
            "fields" => Text(0, 15, "Field label") + Box(0, 26, 228, 42) + Text(12, 53, "Enter a value", "#64748b")
                + Text(250, 15, "Validation example") + Box(250, 26, 228, 42, "#fff", "#b42318")
                + Text(250, 91, "A value is required", "#b42318", 12),
            "selects" => Text(0, 15, "Select an option") + Box(0, 26, 330, 42) + Text(14, 53, "Selected option")
                + Text(303, 53, "⌄") + Text(0, 100, "Closed dropdown · example selection", "#64748b", 12),
            "choices" => Box(0, 15, 22, 22, t.Primary, t.Primary) + Text(4, 32, "✓", Ink(t.Primary)) + Text(36, 32, "Selected")
                + Box(190, 15, 22, 22) + Text(226, 32, "Unselected") + Text(0, 91, "Selection states shown as reference examples", "#64748b", 12),
            "tables" => Box(0, 0, 496, 128) + Box(0, 0, 496, 34, "#f2f4f7", "#f2f4f7") + Text(14, 23, "Name") + Text(310, 23, "Status")
                + Text(14, 64, "Example item") + Text(310, 64, "Ready", "#067647")
                + "<path d=\"M0 80H496\" stroke=\"#e2e8f0\"/>" + Text(14, 109, "Another item") + Text(310, 109, "Needs review", "#985d00"),
            "cards" => Box(0, 0, 496, 130) + Text(20, 32, "Information card", "#182230", 18) + Text(20, 62, "A short summary of related information.", "#64748b")
                + Text(20, 107, "View details →", t.Primary),
            "tabs" => Box(0, 4, 496, 42, "#f8fafc", "#e2e8f0") + Text(18, 31, "Overview", t.Primary) + Text(164, 31, "Details") + Text(305, 31, "History")
                + $"<path d=\"M12 46H112\" stroke=\"{t.Primary}\" stroke-width=\"3\"/>" + Text(12, 91, "Selected tab and sibling navigation", "#64748b", 12),
            "dialogs" => Box(0, 0, 496, 130) + Text(18, 30, "Review an action", "#182230", 18) + Text(462, 28, "×")
                + Text(18, 60, "Explain the outcome before confirming.", "#64748b")
                + Box(316, 79, 160, 34, t.Primary, t.Primary) + Text(347, 101, "Confirm", Ink(t.Primary)) + Text(236, 101, "Cancel"),
            "feedback" => Box(0, 0, 496, 50, "#ecfdf3", "#a6f4c5") + Text(16, 31, "✓  The action completed", "#067647")
                + Box(0, 64, 496, 50, "#fff8e6", "#f5dfa7") + Text(16, 95, "!  Review the highlighted information", "#985d00"),
            _ => ""
        };
    }

    private static SheetTheme Theme(CisUiBaselineRepository repository)
    {
        var tokens = repository.Tokens.Where(token => Regex.IsMatch(token.Value, "^#[a-fA-F0-9]{6}$")).ToArray();
        var primary = tokens.FirstOrDefault(token => Regex.IsMatch(token.Name, @"^(?:\$|--)(?:color-)?(?:primary|brand|accent)$|^--p-primary-500$", RegexOptions.IgnoreCase));
        var font = repository.Facts.Where(fact => fact.Area == "Typography").Select(fact =>
            Regex.Match(fact.Summary, "font-family\\s*:\\s*['\"]?(?<font>[A-Za-z][A-Za-z0-9 -]{1,40})", RegexOptions.IgnoreCase))
            .FirstOrDefault(match => match.Success)?.Groups["font"].Value.Trim() ?? "Segoe UI";
        var radius = repository.Facts.Where(fact => fact.Area == "Layout and spacing")
            .Select(fact => Regex.Match(fact.Summary, @"border-radius\s*:\s*(?<n>\d{1,2})px", RegexOptions.IgnoreCase))
            .FirstOrDefault(match => match.Success)?.Groups["n"].Value;
        var rounding = int.TryParse(radius, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 0, 16) : 6;
        return new(primary?.Value ?? "#475569", font, rounding,
            $"{(primary is null ? "Neutral reference palette (primary not resolved)" : $"Observed {primary.Name}: {primary.Value}")} · Font: {font}");
    }

    private static string Ink(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16); var g = Convert.ToInt32(hex.Substring(3, 2), 16); var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return (r * 299 + g * 587 + b * 114) / 1000 > 150 ? "#182230" : "#ffffff";
    }
    private static string Xml(string value) => SecurityElement.Escape(value) ?? "";
    private static string Short(string value, int length) => value.Length > length ? value[..(length - 1)] + "…" : value;
    private sealed record SheetTheme(string Primary, string Font, int Radius, string Description);
}
