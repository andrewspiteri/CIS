using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Design;

internal static class FeatureScreenRenderer
{
    public static string Render(string title, IReadOnlyList<(CisFeatureScreenPlan Plan, CisUiBaselineRepository? Baseline)> screens)
    {
        var data = screens.Select(item => new { plan = item.Plan, primary = Primary(item.Baseline), font = Font(item.Baseline) });
        return "import {writeFileSync} from 'node:fs';\nimport {fileURLToPath} from 'node:url';\n"
            + "const feature = " + JsonSerializer.Serialize(title) + ";\nconst screens = "
            + JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + ";\n"
            + "let tokens;\n" + DesignRendererScaffolder.ControlPrimitives + Renderer;
    }

    private static string Primary(CisUiBaselineRepository? baseline) => baseline?.Tokens.FirstOrDefault(token =>
        Regex.IsMatch(token.Name, @"^(?:\$|--)(?:color-)?(?:primary|brand|accent)$|^--p-primary-500$", RegexOptions.IgnoreCase)
        && Regex.IsMatch(token.Value, "^#[0-9a-fA-F]{6}$"))?.Value ?? "#2563EB";
    private static string Font(CisUiBaselineRepository? baseline)
    {
        var typography = baseline?.Facts.FirstOrDefault(fact => fact.Area.Equals("Typography", StringComparison.OrdinalIgnoreCase))?.Summary ?? "";
        return Regex.IsMatch(typography, "\\bLato\\b", RegexOptions.IgnoreCase) ? "Lato, Segoe UI, Arial, sans-serif" : "Segoe UI, Arial, sans-serif";
    }

    private const string Renderer = """
function wrap(value, x, y, limit = 85, size = 15, fill = tokens.muted) {
  const lines = [''];
  for (const word of String(value).split(/\s+/)) {
    if (lines.at(-1).length + word.length > limit && lines.at(-1)) lines.push('');
    lines[lines.length - 1] += (lines.at(-1) ? ' ' : '') + word;
  }
  return lines.slice(0, 4).map((value, i) => text(value, x, y + i * 24, size, fill)).join('');
}
for (let index = 0; index < screens.length; index++) {
  const {plan: s, primary, font} = screens[index];
  tokens = {bg:'#FFFFFF', soft:'#F8FAFC', dark:'#17212F', slate:'#334155', muted:'#64748B', border:'#DCE2E9', blue:primary, teal:'#0F766E', success:'#15803D', warning:'#B45309', critical:'#B91C1C', radiusSm:6, radiusMd:10, radiusLg:14};
  const publicUi = s.frontendType === 'public';
  const x = publicUi ? 120 : 292, w = publicUi ? 1160 : 1036;
  let svg = rect(0, 0, 1400, 960, tokens.soft, tokens.soft, 0);
  svg += rect(0, 0, 1400, 84, tokens.bg, tokens.border, 0) + text(feature, 36, 51, 22, tokens.dark, 700);
  if (!publicUi) {
    svg += rect(0, 84, 244, 876, tokens.bg, tokens.border, 0);
    svg += text(s.frontendType === 'backoffice' ? 'Administration' : 'My account', 28, 132, 12, tokens.muted, 650);
    screens.filter(item => item.plan.frontendType === s.frontendType).forEach((item, i) => {
      const y = 158 + i * 66;
      if (item.plan.id === s.id) svg += rect(16, y, 212, 54, tokens.soft, tokens.soft, 6) + rect(16, y, 4, 54, primary, primary, 0);
      svg += wrap(item.plan.title, 32, y + 23, 23, 13, item.plan.id === s.id ? primary : tokens.slate);
    });
  }
  svg += text(s.title, x, 150, 30, tokens.dark, 700);
  svg += wrap(s.purpose, x, 188, Math.floor(w / 9));
  const y = 274;
  if (s.layout === 'table') {
    svg += dataTable(s.fields.map(field => field.label), [s.fields.map(field => field.value)], x, y, w);
  } else {
    svg += rect(x, y, w, 438, tokens.bg, tokens.border, 10);
    s.fields.forEach((field, i) => {
      const fx = x + 32 + (i % 2) * ((w - 64) / 2 + 12), fy = y + 50 + Math.floor(i / 2) * 120;
      const fw = (w - 96) / 2;
      if (field.kind === 'toggle') svg += toggleControl(field.label, fx, fy + 16, /yes|true|enabled|on/i.test(field.value));
      else if (field.kind === 'checkbox') svg += checkboxControl(field.label, fx, fy + 16, 'unchecked');
      else if (s.layout === 'detail') svg += text(field.label, fx, fy, 13, tokens.muted, 650) + wrap(field.value, fx, fy + 36, 45, 20, tokens.dark);
      else if (field.kind === 'select') svg += selectInput(field.label, field.value, fx, fy, fw);
      else svg += textInput(field.label, field.value, fx, fy, fw);
    });
  }
  s.actions.forEach((action, i) => { svg += standardButton(action.label, x + i * 290, s.layout === 'table' ? 484 : 746, 270, i === 0 ? 'primary' : 'secondary'); });
  svg += line(x, 886, x + w, 886) + text(feature, x, 918, 12, tokens.muted);
  const image = `<svg xmlns="http://www.w3.org/2000/svg" width="1400" height="960" viewBox="0 0 1400 960"><title>${esc(s.title)}</title>${svg}</svg>`
    .replaceAll('Inter, Segoe UI, Arial, sans-serif', esc(font));
  writeFileSync(fileURLToPath(new URL(index + '.svg', import.meta.url)), image, 'utf8');
}
""";
}
