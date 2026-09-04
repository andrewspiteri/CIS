// Self-contained CIS-0001 design source: JavaScript-generated SVG rendered with pinned Sharp.
// No network assets, machine fonts, companion stylesheets, or hand-edited PNGs are used.
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const sharpNodeModules = process.env.CIS_SHARP_NODE_MODULES;
if (!sharpNodeModules) throw new Error("Sharp runtime unavailable. Run through `cis design render`.");
const sharp = createRequire(path.join(sharpNodeModules, "..", "cis-design-runtime.cjs"))("sharp");

const outputDirectory = path.dirname(fileURLToPath(import.meta.url));
const provenance = Object.freeze({
  wireframeSha256: "sha256:69d6fd6a69eaf1753eadbd74b4da203370ca8f2ee41d3961fb6cdebdba497679",
  guidelinePath: "docs/specs/design-guidelines.md",
  guidelineSha256: "sha256:972add8e8d0bec8cb1008c02f0b672f3b838d862c8e1e3f40563a6fbed450f1a",
  shellTemplate: "shell.standard-app@1.0",
  componentTemplates: [
    "component.page-header", "component.button", "component.text-input", "component.select",
    "component.textarea", "component.checkbox", "component.tabs", "component.breadcrumbs",
    "component.dropdown-menu", "component.dialog", "component.alert", "component.table",
    "component.filter-bar", "component.status-badge", "component.card", "component.form",
    "component.empty-state", "component.timeline", "component.accordion"
  ],
  vscodeGuidance: ["UX overview", "Views", "Sidebars", "Webviews"]
});

const screens = Object.freeze([
  s("vsc-welcome", "Welcome and setup", "/welcome", "Native View", "setup-states", "desktop"),
  s("vsc-welcome", "Welcome and setup", "/welcome", "Native View", "untrusted", "compact"),
  s("vsc-authority-picker", "Authority repository picker", "/authority/select", "Quick Pick", "multi-root", "compact"),
  s("vsc-workspace", "Workspace overview", "/workspace", "Native View", "healthy", "desktop"),
  s("vsc-workspace", "Workspace overview", "/workspace", "Native View", "review-paused", "compact"),
  s("vsc-changes", "Changes inventory", "/changes", "Native View", "active", "desktop"),
  s("vsc-changes", "Changes inventory", "/changes", "Native View", "filtered-empty", "compact"),
  s("vsc-change-overview", "Change overview", "/changes/CIS-0001", "Editor webview", "planning", "desktop"),
  s("vsc-change-overview", "Change overview", "/changes/CIS-0001", "Editor webview", "design-review", "compact"),
  s("vsc-evidence", "Evidence and context", "/evidence", "Native View and Quick Pick", "matched", "desktop"),
  s("vsc-graph-detail", "Relationship detail", "/evidence/graph/vscode-extension", "Editor webview", "truncated", "desktop"),
  s("vsc-task-detail", "Planned task detail", "/changes/CIS-0001/tasks/WORK-100-BACKOFFICE", "Editor webview", "ready", "desktop"),
  s("vsc-task-detail", "Planned task detail", "/changes/CIS-0001/tasks/WORK-100-BACKOFFICE", "Editor webview", "blocked", "compact"),
  s("vsc-design-review", "Design review", "/changes/CIS-0001/design", "Editor webview", "ready", "desktop"),
  s("vsc-design-review", "Design review", "/changes/CIS-0001/design", "Editor webview", "rejected", "compact"),
  s("vsc-runs", "Runs and providers", "/runs", "Native View", "mixed-outcomes", "desktop"),
  s("vsc-run-detail", "Run evidence", "/runs/RUN-20260828141853-25277C2AAE", "Editor webview", "failed-attempt", "desktop"),
  s("vsc-agent-request", "Request agent work", "/changes/CIS-0001/tasks/WORK-100-BACKOFFICE/agent-request", "Quick Picks and confirmation", "confirmation", "desktop"),
  s("vsc-agent-run", "Foreground agent execution", "/runs/RUN-20260828141853-25277C2AAE/agent", "Progress and editor webview", "permission-request", "desktop"),
  s("vsc-agent-run", "Foreground agent execution", "/runs/RUN-20260828141853-25277C2AAE/agent", "Progress and editor webview", "recovered", "compact"),
  s("vsc-governance", "Governance inventory", "/governance", "Native View", "conflicts", "desktop"),
  s("vsc-command-progress", "Command progress and result", "/commands/OP-20260828-001", "Notification, progress, Output channel", "running", "desktop"),
  s("vsc-command-progress", "Command progress and result", "/commands/OP-20260828-001", "Notification, progress, Output channel", "invalid-evidence", "compact")
]);

function s(id, name, route, platform, state, viewport) {
  return { id, frontendType: "backoffice", name, route, platform, state, viewport,
    file: `${id}--${state}--${viewport}.png` };
}

const c = Object.freeze({
  title: "#F3F3F3", activity: "#2C2C2C", activityActive: "#FFFFFF", side: "#F7F7F7",
  sideActive: "#E8E8E8", editor: "#FFFFFF", editorSoft: "#F8FAFC", panel: "#F3F3F3",
  status: "#2563EB", text: "#1F1F1F", secondary: "#4B5563", muted: "#6B7280",
  border: "#D4D4D4", focus: "#0078D4", blue: "#2563EB", teal: "#0F766E",
  success: "#15803D", warning: "#B45309", danger: "#B91C1C", white: "#FFFFFF",
  infoBg: "#EAF2FF", successBg: "#E8F7EE", warningBg: "#FFF3DC", dangerBg: "#FEECEC",
  dark: "#0F172A", shadow: "#00000022"
});

function esc(value) { return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;"); }
function rect(x, y, w, h, fill = c.white, stroke = "none", radius = 0, sw = 1) { return `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${radius}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}"/>`; }
function line(x1, y1, x2, y2, stroke = c.border, sw = 1) { return `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" stroke="${stroke}" stroke-width="${sw}"/>`; }
function text(value, x, y, size = 13, fill = c.text, weight = 400, extra = "") { return `<text x="${x}" y="${y}" font-family="Inter, Segoe UI, Arial, sans-serif" font-size="${size}" font-weight="${weight}" fill="${fill}" ${extra}>${esc(value)}</text>`; }
function multi(lines, x, y, size = 13, fill = c.secondary, weight = 400, gap = 20) { return lines.map((v, i) => text(v, x, y + i * gap, size, fill, weight)).join(""); }
function divider(x, y, w) { return line(x, y, x + w, y, c.border); }
function icon(glyph, x, y, active = false, size = 19) { return text(glyph, x, y, size, active ? c.activityActive : "#B7B7B7", active ? 700 : 500, 'text-anchor="middle"'); }
function badge(label, x, y, tone = "neutral") {
  const p = { neutral: ["#ECECEC", c.secondary], accent: [c.infoBg, c.blue], success: [c.successBg, c.success], warning: [c.warningBg, c.warning], danger: [c.dangerBg, c.danger] }[tone];
  const w = Math.max(68, label.length * 7 + 22); return rect(x, y, w, 24, p[0], "none", 12) + text(label, x + 11, y + 17, 11, p[1], 650);
}
function button(label, x, y, kind = "secondary", disabled = false, width) {
  const w = width ?? Math.max(92, label.length * 7.4 + 28); const palette = kind === "primary" ? [c.focus, c.white, c.focus] : kind === "danger" ? [c.danger, c.white, c.danger] : [c.white, c.text, c.border];
  const p = disabled ? ["#EEEEEE", "#999999", c.border] : palette;
  return rect(x, y, w, 30, p[0], p[2], 2) + text(label, x + w / 2, y + 20, 12, p[1], 600, 'text-anchor="middle"');
}
function card(x, y, w, h, title, body = "") { return rect(x, y, w, h, c.white, c.border, 6) + text(title, x + 18, y + 28, 14, c.text, 650) + (body ? text(body, x + 18, y + 51, 12, c.muted) : ""); }
function alert(x, y, w, title, body, tone = "info") {
  const p = { info: [c.infoBg, c.blue], success: [c.successBg, c.success], warning: [c.warningBg, c.warning], danger: [c.dangerBg, c.danger] }[tone];
  return rect(x, y, w, 68, p[0], "none", 4) + rect(x, y, 4, 68, p[1]) + text(title, x + 18, y + 25, 13, p[1], 700) + text(body, x + 18, y + 48, 12, c.secondary);
}
function input(x, y, w, placeholder, focused = false) { return rect(x, y, w, 30, c.white, focused ? c.focus : c.border, 2, focused ? 2 : 1) + text(placeholder, x + 10, y + 20, 12, focused ? c.text : c.muted); }
function check(x, y, label, checked = false) { return rect(x, y, 16, 16, checked ? c.focus : c.white, checked ? c.focus : c.border, 2) + (checked ? text("✓", x + 8, y + 13, 12, c.white, 700, 'text-anchor="middle"') : "") + text(label, x + 25, y + 13, 12, c.text); }
function row(x, y, w, title, detail, status, tone = "neutral", selected = false) {
  let out = selected ? rect(x, y, w, 56, c.sideActive) : "";
  out += text(title, x + 12, y + 21, 12, c.text, selected ? 650 : 500) + text(detail, x + 12, y + 41, 11, c.muted);
  if (status) out += badge(status, x + w - Math.max(82, status.length * 7 + 28), y + 15, tone);
  return out + divider(x, y + 56, w);
}

function layout(screen) {
  const compact = screen.viewport === "compact"; const W = compact ? 1100 : 1600; const H = compact ? 760 : 1000;
  const titleH = 34, statusH = 23, activityW = 48, sideW = compact ? 248 : 292, tabsH = 36;
  const editorX = activityW + sideW, editorY = titleH + tabsH, editorW = W - editorX, editorH = H - editorY - statusH;
  return { compact, W, H, titleH, statusH, activityW, sideW, tabsH, editorX, editorY, editorW, editorH };
}

function applicationShell(screen, content) {
  const d = layout(screen); let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${d.W}" height="${d.H}" viewBox="0 0 ${d.W} ${d.H}">`;
  svg += rect(0, 0, d.W, d.H, c.editor) + rect(0, 0, d.W, d.titleH, c.title) + divider(0, d.titleH, d.W);
  svg += text("☰", 16, 22, 15, c.secondary) + text("File", 50, 22, 12) + text("Edit", 82, 22, 12) + text("View", 116, 22, 12);
  svg += text("Change Impact Studio — change-impact-studio", d.W / 2, 22, 12, c.secondary, 500, 'text-anchor="middle"');
  svg += rect(0, d.titleH, d.activityW, d.H - d.titleH - d.statusH, c.activity);
  const active = viewFor(screen.id); const icons = [["◇", "workspace"], ["⇄", "changes"], ["⌕", "evidence"], ["▶", "runs"], ["⚙", "governance"]];
  icons.forEach(([g, id], i) => { const y = d.titleH + 38 + i * 52; if (id === active) svg += rect(0, y - 25, 2, 40, c.white); svg += icon(g, 24, y, id === active); });
  svg += icon("◉", 24, d.H - d.statusH - 62, false) + icon("⚙", 24, d.H - d.statusH - 22, false, 17);
  svg += rect(d.activityW, d.titleH, d.sideW, d.H - d.titleH - d.statusH, c.side) + line(d.activityW + d.sideW, d.titleH, d.activityW + d.sideW, d.H - d.statusH);
  svg += text("CHANGE IMPACT STUDIO", d.activityW + 14, d.titleH + 25, 11, c.secondary, 650) + text("…", d.activityW + d.sideW - 20, d.titleH + 25, 18, c.secondary, 650, 'text-anchor="middle"');
  svg += sidebar(screen, d);
  svg += rect(d.editorX, d.titleH, d.editorW, d.tabsH, c.title) + divider(d.editorX, d.titleH + d.tabsH, d.editorW);
  svg += rect(d.editorX, d.titleH, Math.min(250, d.editorW), d.tabsH, c.editor) + text(tabName(screen), d.editorX + 15, d.titleH + 23, 12, c.text, 500) + text("×", d.editorX + Math.min(230, d.editorW - 15), d.titleH + 23, 14, c.muted);
  svg += rect(d.editorX, d.editorY, d.editorW, d.editorH, c.editor) + content;
  svg += rect(0, d.H - d.statusH, d.W, d.statusH, c.status) + text("✓ CIS: " + statusText(screen), 10, d.H - 7, 11, c.white, 500) + text("main*   UTF-8   Ln 1, Col 1", d.W - 180, d.H - 7, 11, c.white, 500);
  return svg + "</svg>";
}

function renderScreen(screen) {
  const d = layout(screen);
  const content = contentForScreen(screen, d);
  return applicationShell(screen, content);
}

function viewFor(id) { if (id.includes("change") || id.includes("task") || id.includes("design")) return "changes"; if (id.includes("evidence") || id.includes("graph")) return "evidence"; if (id.includes("run") || id.includes("agent") || id.includes("command")) return "runs"; if (id.includes("governance")) return "governance"; return "workspace"; }
function tabName(screen) { return screen.platform.includes("Native") ? "Welcome" : `${screen.name}  ·  ${screen.state}`; }
function statusText(screen) { if (screen.state === "review-paused" || screen.state === "design-review") return "Design review required"; if (["blocked", "rejected", "failed-attempt", "invalid-evidence"].includes(screen.state)) return "Action required"; return "Workspace ready"; }

function sidebar(screen, d) {
  const x = d.activityW, y = d.titleH + 44, w = d.sideW; const active = viewFor(screen.id); let out = "";
  if (active === "workspace") {
    out += text("WORKSPACE", x + 14, y + 18, 11, c.secondary, 700);
    out += row(x, y + 30, w, "change-impact-studio", "Authority repository", "Selected", "success", true);
    out += row(x, y + 86, w, "Repository Doctor", screen.state === "setup-states" ? "2 setup issues" : "No blocking findings", screen.state === "setup-states" ? "Warning" : "Healthy", screen.state === "setup-states" ? "warning" : "success");
    out += row(x, y + 142, w, "Context graph", "Built 2 minutes ago", "Current", "success");
    out += row(x, y + 198, w, "Local AI", "Ollama detected", "Ready", "accent");
    out += row(x, y + 254, w, "CIS-0001", "VS Code Delivery Workspace", screen.state === "review-paused" ? "Review" : "Active", screen.state === "review-paused" ? "warning" : "accent");
  } else if (active === "changes") {
    out += input(x + 12, y, w - 24, "Filter changes…", screen.id === "vsc-changes");
    out += text("CURRENT", x + 14, y + 56, 10, c.secondary, 700);
    out += row(x, y + 68, w, "CIS-0001", "VS Code Delivery Workspace", screen.state === "design-review" || screen.id === "vsc-design-review" ? "Review" : "Planning", screen.state === "design-review" || screen.id === "vsc-design-review" ? "warning" : "accent", true);
    out += row(x, y + 124, w, "CIS-0002", "Agent execution coordination", "Complete", "success");
    out += text("CLOSED", x + 14, y + 205, 10, c.secondary, 700);
    out += row(x, y + 216, w, "CIS-0000", "Repository foundation", "Closed", "neutral");
  } else if (active === "evidence") {
    out += input(x + 12, y, w - 24, "Search bounded context…", true);
    out += text("CANONICAL", x + 14, y + 56, 10, c.secondary, 700);
    ["Feature specification", "Accepted impacts", "Delivery plan", "Textual wireframes", "Design approval"].forEach((v, i) => out += row(x, y + 68 + i * 48, w, v, `docs/${v.toLowerCase().replaceAll(" ", "-")}.md`, "", "neutral", i === 2));
  } else if (active === "runs") {
    out += text("AGENT PROVIDERS", x + 14, y + 18, 10, c.secondary, 700);
    out += row(x, y + 30, w, "Codex", "app-server · 0.150.0", "Ready", "success");
    out += row(x, y + 86, w, "Claude", "Executable not found", "Missing", "warning");
    out += text("RECENT RUNS", x + 14, y + 166, 10, c.secondary, 700);
    out += row(x, y + 178, w, "RUN-…25277C2AAE", "Agent · attempt 4", screen.state.includes("failed") ? "Failed" : "Stopped", screen.state.includes("failed") ? "danger" : "warning", screen.id === "vsc-run-detail" || screen.id === "vsc-agent-run");
    out += row(x, y + 234, w, "TEST-20260828-01", "Focused tests · 22/22", "Passed", "success");
  } else {
    out += input(x + 12, y, w - 24, "Filter governance…");
    out += text("INVENTORY", x + 14, y + 56, 10, c.secondary, 700);
    out += row(x, y + 68, w, "Skills", "31 active · 1 conflict", "Review", "warning", true);
    out += row(x, y + 124, w, "Standards", "18 active", "Healthy", "success");
    out += row(x, y + 180, w, "References", "27 current · 2 stale", "Warning", "warning");
    out += row(x, y + 236, w, "Repository Doctor", "3 suggested fixes", "Review", "warning");
  }
  return out;
}

function heading(d, eyebrow, title, description, status, tone = "accent") {
  const x = d.editorX + (d.compact ? 28 : 48), y = d.editorY + (d.compact ? 38 : 52), w = d.editorW - (d.compact ? 56 : 96);
  return text(eyebrow.toUpperCase(), x, y, 10, c.muted, 700) + text(title, x, y + 36, d.compact ? 23 : 28, c.text, 650) + text(description, x, y + 63, 13, c.secondary) + badge(status, x + w - Math.max(90, status.length * 7 + 28), y + 10, tone);
}

function contentForScreen(screen, d) {
  const x = d.editorX + (d.compact ? 28 : 48), top = d.editorY + (d.compact ? 120 : 145), w = d.editorW - (d.compact ? 56 : 96);
  let out = heading(d, screen.platform, screen.name, description(screen), stateLabel(screen), stateTone(screen));
  switch (screen.id) {
    case "vsc-welcome": return out + welcome(screen, d, x, top, w);
    case "vsc-authority-picker": return out + authority(screen, d, x, top, w);
    case "vsc-workspace": return out + workspace(screen, d, x, top, w);
    case "vsc-changes": return out + changes(screen, d, x, top, w);
    case "vsc-change-overview": return out + changeOverview(screen, d, x, top, w);
    case "vsc-evidence": return out + evidence(screen, d, x, top, w);
    case "vsc-graph-detail": return out + graph(screen, d, x, top, w);
    case "vsc-task-detail": return out + task(screen, d, x, top, w);
    case "vsc-design-review": return out + design(screen, d, x, top, w);
    case "vsc-runs": return out + runs(screen, d, x, top, w);
    case "vsc-run-detail": return out + runDetail(screen, d, x, top, w);
    case "vsc-agent-request": return out + agentRequest(screen, d, x, top, w);
    case "vsc-agent-run": return out + agentRun(screen, d, x, top, w);
    case "vsc-governance": return out + governance(screen, d, x, top, w);
    case "vsc-command-progress": return out + commandProgress(screen, d, x, top, w);
    default: return out;
  }
}

function description(screen) {
  const map = {
    "vsc-welcome": "One truthful setup action, with recovery that preserves the thin-client boundary.",
    "vsc-authority-picker": "Explicit workspace authority; folder changes never switch it silently.",
    "vsc-workspace": "Repository health, freshness, active delivery state, and the next meaningful action.",
    "vsc-changes": "Current and closed dossiers remain shallow, filterable, and read-only on selection.",
    "vsc-change-overview": "Scope, decisions, plan, review points, and one recommendation derived from CIS.",
    "vsc-evidence": "Canonical Markdown and bounded source evidence remain directly inspectable.",
    "vsc-graph-detail": "A bounded relationship view with equivalent accessible list and freshness evidence.",
    "vsc-task-detail": "The approved task contract, dependencies, gates, and exact execution evidence.",
    "vsc-design-review": "Compare behavior and rendered evidence before releasing the global design barrier.",
    "vsc-runs": "Provider readiness and distinct workflow, testing, security, diagnostic, and assurance results.",
    "vsc-run-detail": "Attempts and evidence remain durable; a later result never erases the first failure.",
    "vsc-agent-request": "Confirm the exact provider, repository, isolation, mode, and permission ceiling.",
    "vsc-agent-run": "Observe governed foreground work without becoming a chat client or transcript viewer.",
    "vsc-governance": "Compact governance inventories distinguish health, conflicts, quarantine, and fixes.",
    "vsc-command-progress": "Bounded output preserves cancellation, exit code, classification, and evidence."
  }; return map[screen.id];
}
function stateLabel(screen) { return screen.state.replaceAll("-", " ").replace(/\b\w/g, m => m.toUpperCase()); }
function stateTone(screen) { if (["healthy", "ready", "recovered", "matched", "active"].includes(screen.state)) return "success"; if (["rejected", "failed-attempt", "invalid-evidence", "blocked"].includes(screen.state)) return "danger"; if (["review-paused", "design-review", "permission-request", "conflicts", "setup-states"].includes(screen.state)) return "warning"; return "accent"; }

function welcome(screen, d, x, y, w) {
  if (screen.state === "untrusted") return alert(x, y, w, "Workspace trust is required", "You can inspect canonical Markdown, but CIS commands remain disabled.", "warning") + card(x, y + 92, w, 150, "Read-only navigation remains available", "Trust this folder only when you know and control its contents.") + button("Open first-use guide", x + 18, y + 174, "secondary");
  const gap = 14, cw = (w - gap) / 2; let out = "";
  out += card(x, y, cw, 142, "No folder", "Open a folder before CIS can select repository authority.") + button("Open Folder", x + 18, y + 88, "primary");
  out += card(x + cw + gap, y, cw, 142, "CIS executable missing", "Configure a compatible CIS CLI path; credentials stay external.") + button("Configure CIS", x + cw + gap + 18, y + 88, "secondary");
  out += card(x, y + 158, cw, 142, "Repository not initialized", "Choose the documentation root, then run the idempotent init command.") + button("Initialize repository", x + 18, y + 246, "primary");
  out += card(x + cw + gap, y + 158, cw, 142, "Initialization collision", "Existing paths are preserved. Repository Doctor explains safe fixes.") + button("Run Repository Doctor", x + cw + gap + 18, y + 246, "secondary");
  out += alert(x, y + 316, w, "Compatible CLI detected", "CIS 0.3.0 · repository authority remains explicit and workspace-scoped.", "success"); return out;
}

function authority(screen, d, x, y, w) {
  const pw = Math.min(620, w - 60), px = x + (w - pw) / 2, py = y - 26;
  let out = rect(px + 8, py + 8, pw, 330, c.shadow, "none", 6) + rect(px, py, pw, 330, c.white, c.focus, 6, 2);
  out += input(px + 18, py + 18, pw - 36, "Select the CIS authority repository", true);
  out += text("Eligible workspace folders", px + 18, py + 72, 11, c.muted, 700);
  out += row(px + 8, py + 84, pw - 16, "change-impact-studio", "C:\\miscwork\\portfolio\\…  ·  docs/", "Selected", "success", true);
  out += row(px + 8, py + 140, pw - 16, "friends-todo-docs", "C:\\miscwork\\sample-repos\\…  ·  cisdocs/", "Healthy", "success");
  out += row(px + 8, py + 196, pw - 16, "legacy-service", "C:\\miscwork\\legacy-service  ·  docs/", "Warning", "warning");
  out += text("Enter to select  ·  Esc to keep current authority", px + 18, py + 284, 11, c.muted); return out;
}

function workspace(screen, d, x, y, w) {
  if (screen.state === "review-paused") return alert(x, y, w, "Design review required", "CIS-0001 is paused. Only wireframe/design review work is eligible.", "warning") + card(x, y + 92, w, 154, "Next meaningful action", "Review the 23-image design pack against its exact renderer and wireframe digest.") + button("Open design review", x + 18, y + 178, "primary") + button("Open canonical wireframes", x + 174, y + 178, "secondary");
  const cols = d.compact ? 1 : 3, gap = 14, cw = cols === 1 ? w : (w - gap * 2) / 3; let out = "";
  const cards = [["Repository Doctor", "No blocking findings", "Healthy", "success"], ["Context graph", "Build a6f8978a · current", "Current", "success"], ["Local AI", "Ollama available", "Ready", "accent"]];
  cards.forEach((v, i) => { const cx = x + (i % cols) * (cw + gap), cy = y + Math.floor(i / cols) * 112; out += card(cx, cy, cw, 98, v[0], v[1]) + badge(v[2], cx + 18, cy + 61, v[3]); });
  const lower = y + (cols === 1 ? 350 : 118); out += card(x, lower, w, 166, "CIS-0001 · Visual Studio Code Delivery Workspace", "Planning approved · 2 of 10 tasks active · no stale source evidence") + badge("In progress", x + 18, lower + 66, "accent") + text("Next: complete visual design and enter human review", x + 18, lower + 112, 13, c.secondary, 600) + button("Open active change", x + w - 164, lower + 112, "primary", false, 146); return out;
}

function changes(screen, d, x, y, w) {
  if (screen.state === "filtered-empty") return input(x, y, w, "phase:closed blocked:true", true) + card(x, y + 50, w, 180, "No changes match this filter", "The canonical inventory is unchanged. Clear the local filter to restore all dossiers.") + button("Clear filter", x + 18, y + 150, "primary");
  let out = input(x, y, Math.min(420, w - 130), "Filter changes…") + button("Filters", x + Math.min(434, w - 116), y, "secondary");
  out += card(x, y + 50, w, 284, "Current changes", "Selecting a row opens detail; it never transitions lifecycle state.");
  out += row(x + 8, y + 104, w - 16, "CIS-0001 · VS Code Delivery Workspace", "Planning · design work active · source current", "In progress", "accent", true);
  out += row(x + 8, y + 160, w - 16, "CIS-0002 · Agent execution coordination", "Closed · exact verification evidence retained", "Complete", "success");
  out += row(x + 8, y + 216, w - 16, "CIS-0000 · Repository foundation", "Closed · no residual risk", "Closed", "neutral");
  return out;
}

function changeOverview(screen, d, x, y, w) {
  const review = screen.state === "design-review"; let out = review ? alert(x, y, w, "Global design barrier", "23 rendered states are ready for one combined human decision.", "warning") : alert(x, y, w, "Plan approved", "23 requirements and 24 accepted impacts are the canonical delivery boundary.", "success");
  const gap = 14, cw = d.compact ? w : (w - gap) / 2; const cy = y + 84;
  out += card(x, cy, cw, 172, "Progress", review ? "Wireframe validated · visual pack rendered · downstream paused" : "Impact accepted · plan approved · wireframe in progress");
  out += badge(review ? "Paused for review" : "2 / 10 tasks active", x + 18, cy + 72, review ? "warning" : "accent") + text("Next meaningful action", x + 18, cy + 120, 11, c.muted, 700) + text(review ? "Review exact design evidence" : "Complete the visual design pack", x + 18, cy + 143, 13, c.text, 650);
  if (!d.compact) out += card(x + cw + gap, cy, cw, 172, "Accepted scope", "Thin client · backoffice UX · governed Codex and Claude work") + badge("24 impacts", x + cw + gap + 18, cy + 72, "success") + text("Canonical feature", x + cw + gap + 18, cy + 120, 11, c.muted, 700) + text("vscode-delivery-workspace-feature.md", x + cw + gap + 18, cy + 143, 13, c.blue, 600);
  const by = cy + 188; out += card(x, by, w, d.compact ? 146 : 176, "Delivery tasks", "Dependencies and gates come from the approved plan.");
  out += text("WORK-010-BACKOFFICE", x + 18, by + 78, 12, c.text, 650) + badge(review ? "Completed" : "In progress", x + 180, by + 62, review ? "success" : "accent") + text("WORK-020-BACKOFFICE", x + 18, by + 118, 12, c.text, 650) + badge(review ? "Review" : "Ready", x + 180, by + 102, review ? "warning" : "neutral");
  if (!d.compact) out += text("WORK-100-BACKOFFICE", x + 410, by + 78, 12, c.text, 650) + badge("Blocked", x + 576, by + 62, "warning") + button(review ? "Open design review" : "Open next task", x + w - 170, by + 120, "primary", false, 152);
  return out;
}

function evidence(screen, d, x, y, w) {
  let out = input(x, y, Math.min(620, w), "agent execution permission ceiling", true) + text("12 results · current graph · limited to 50", x, y + 54, 11, c.muted, 600);
  const results = [["Feature specification", "docs/specs/features/agent-execution-coordination-feature.md", "Requirement and boundary evidence"], ["Agent provider profile", "docs/references/agent-provider-profile.md", "Declared transports and permission capabilities"], ["Codex provider adapter", "src/Cis.Providers.Agent.Codex/CodexAgentProvider.cs", "Source evidence · 7 relationships"]];
  results.forEach((r, i) => { const ry = y + 72 + i * 88; out += card(x, ry, w, 76, r[0], r[2]) + text(r[1], x + 18, ry + 62, 11, c.blue, 500); });
  out += alert(x, y + 348, w, "Results are bounded", "2 additional matches were truncated. Refine the query or explicitly open relationships.", "info"); return out;
}

function graph(screen, d, x, y, w) {
  const centerX = x + w / 2, cy = y + 160; let out = alert(x, y, w, "Graph build a6f8978a · current", "Showing 8 of 13 neighbors. An equivalent accessible relationship list is available.", "info");
  out += card(centerX - 145, cy - 48, 290, 96, "CodexAgentProvider", "source symbol · selected");
  const nodes = [[x, cy - 110, "AgentService", "calls"], [x, cy + 80, "ICisAgentProvider", "implements"], [x + w - 220, cy - 110, "AgentExecutionTests", "verified by"], [x + w - 220, cy + 80, "agent-provider-profile.md", "documented by"]];
  nodes.forEach(([nx, ny, title, relation]) => { out += line(centerX, cy, nx + 110, ny + 40, c.blue, 2) + card(nx, ny, 220, 80, title, relation); });
  out += button("Load 5 more relationships", centerX - 98, y + 340, "secondary", false, 196); return out;
}

function task(screen, d, x, y, w) {
  const blocked = screen.state === "blocked"; let out = blocked ? alert(x, y, w, "Blocked by global design review", "Implementation cannot begin until the exact wireframe, renderer, and PNG manifest are approved.", "warning") : alert(x, y, w, "Eligible for governed execution", "Dependencies are complete and provider capability evidence is current.", "success");
  out += card(x, y + 84, w, 206, "WORK-100-BACKOFFICE · Implement approved frontend", "Frontend · medium complexity · change-impact-studio repository");
  out += text("Depends on", x + 18, y + 150, 11, c.muted, 700) + text("WORK-020 · WORK-040 · WORK-080", x + 18, y + 172, 12, c.text, 600);
  out += text("Acceptance", x + 18, y + 208, 11, c.muted, 700) + multi(["Production UI matches approved behavior and visual direction.", "Component, accessibility, type, build, and browser checks pass."], x + 18, y + 230, 12);
  out += button("Open canonical task", x + 18, y + 258, "secondary", false, 150);
  if (!blocked) out += button("Request agent work", x + w - 166, y + 258, "primary", false, 148); return out;
}

function design(screen, d, x, y, w) {
  const rejected = screen.state === "rejected"; let out = rejected ? alert(x, y, w, "Revision rejected", "Navigation density obscures the next action. Revise and resubmit; downstream work remains paused.", "danger") : alert(x, y, w, "Manifest validation passed", "15 screens · 23 PNG states · renderer and source digests current.", "success");
  const gap = 12, pw = d.compact ? w : (w - gap) / 2, ph = d.compact ? 126 : 162, py = y + 84;
  out += rect(x, py, pw, ph, "#F3F3F3", c.border, 5) + rect(x + 10, py + 10, pw * 0.28, ph - 20, c.activity, "none", 2) + rect(x + pw * 0.32, py + 10, pw * 0.63, ph - 20, c.white, c.border, 2) + text("Workspace · desktop", x + 18, py + ph - 18, 11, c.secondary, 600);
  if (!d.compact) out += rect(x + pw + gap, py, pw, ph, "#F3F3F3", c.border, 5) + rect(x + pw + gap + 10, py + 10, pw * 0.38, ph - 20, c.activity, "none", 2) + rect(x + pw + gap + pw * 0.42, py + 10, pw * 0.53, ph - 20, c.white, c.border, 2) + text("Agent run · compact", x + pw + gap + 18, py + ph - 18, 11, c.secondary, 600);
  const by = py + ph + 20; out += text("Provenance", x, by, 11, c.muted, 700) + text("Sharp/SVG · shell.standard-app@1.0 · VS Code UX guidance · 23 valid hashes", x, by + 24, 12, c.text, 600);
  out += button("Open wireframes", x, by + 52, "secondary", false, 132) + button(rejected ? "Resubmit after revision" : "Reject", x + 144, by + 52, rejected ? "primary" : "danger", false, rejected ? 164 : 92);
  if (!rejected) out += button("Approve design pack", x + w - 166, by + 52, "primary", false, 166); return out;
}

function runs(screen, d, x, y, w) {
  let out = alert(x, y, w, "Codex ready · Claude unavailable", "Availability and capabilities come from CIS provider diagnosis; credentials remain external.", "info");
  const list = [["Codex", "app-server · workspace-write · resume", "Ready", "success"], ["Claude", "Executable not found", "Unavailable", "warning"], ["Agent RUN-…25277C2AAE", "4 attempts · idle timeout retained", "Failed", "danger"], ["Focused agent tests", "22 passed · 0 failed", "Passed", "success"], ["Security scan", "No critical findings · 1 unavailable provider smoke", "Partial", "warning"]];
  out += card(x, y + 84, w, 330, "Distinct outcomes", "First attempts, classifications, unavailable suites, revisions, and hashes remain visible.");
  list.forEach((r, i) => out += row(x + 8, y + 136 + i * 52, w - 16, r[0], r[1], r[2], r[3], i === 2)); return out;
}

function runDetail(screen, d, x, y, w) {
  let out = alert(x, y, w, "Attempt 4 timed out", "The first App Server schema failures and later timeout are preserved as separate evidence.", "danger");
  out += card(x, y + 84, w, 282, "RUN-20260828141853-25277C2AAE", "Codex · isolated workspace-write · revision 7bf8f8c2 · result not imported");
  const events = [["14:18:53", "Attempt 1 · invalid approval-policy value"], ["14:20:14", "Attempt 2 · invalid sandbox enum"], ["14:24:02", "Attempt 3 · oversized event retained and classified"], ["14:31:17", "Attempt 4 · idle timeout · no file changes"]];
  events.forEach((e, i) => { const yy = y + 162 + i * 48; out += i < events.length - 1 ? line(x + 30, yy + 9, x + 30, yy + 54, c.border, 2) : ""; out += rect(x + 24, yy + 3, 12, 12, i === 3 ? c.danger : c.blue, "none", 6) + text(e[0], x + 52, yy + 13, 11, c.muted, 600) + text(e[1], x + 126, yy + 13, 12, c.text, 500); });
  out += button("Open bounded diagnostics", x + 18, y + 326, "secondary", false, 174) + button("Recover", x + w - 104, y + 326, "primary", false, 86); return out;
}

function agentRequest(screen, d, x, y, w) {
  const pw = Math.min(720, w), px = x + (w - pw) / 2; let out = card(px, y, pw, 368, "Confirm governed agent work", "No chat history is created. The exact task contract and ceiling are passed through CIS.");
  const fields = [["Task", "WORK-100-BACKOFFICE"], ["Repository", "change-impact-studio"], ["Provider / transport", "Codex · app-server"], ["Isolation", "Isolated worktree"], ["Run mode", "Foreground"], ["Permission ceiling", "Workspace write · no approval requests"]];
  fields.forEach((f, i) => { const fx = px + 20 + (i % 2) * (pw / 2), fy = y + 78 + Math.floor(i / 2) * 62; out += text(f[0].toUpperCase(), fx, fy, 10, c.muted, 700) + text(f[1], fx, fy + 22, 12, c.text, 600); });
  out += alert(px + 20, y + 264, pw - 40, "Do not enter credentials or secrets", "Provider authentication remains outside this extension.", "warning");
  out += button("Cancel", px + pw - 250, y + 326, "secondary", false, 94) + button("Start agent work", px + pw - 144, y + 326, "primary", false, 124); return out;
}

function agentRun(screen, d, x, y, w) {
  const permission = screen.state === "permission-request"; let out = permission ? alert(x, y, w, "Permission request requires a decision", "Write docs/changes/CIS-0001/wireframes.md · within declared workspace ceiling.", "warning") : alert(x, y, w, "Recovered as a new attempt", "Attempt 1 remains interrupted; attempt 2 resumed with the complete digest-bound task contract.", "success");
  out += card(x, y + 84, w, 242, "Foreground run · attempt " + (permission ? "1" : "2"), "Codex · isolated worktree · workspace-write · elapsed 03:42");
  const steps = permission ? [["Prepared", "Task digest verified"], ["Running", "Read bounded context"], ["Permission", "Awaiting controller"], ["Completion", "Not started"]] : [["Recovered", "Prior process classified"], ["Resumed", "Complete task contract sent"], ["Validated", "Focused checks passed"], ["Completed", "Evidence ready to inspect"]];
  steps.forEach((s, i) => { const yy = y + 150 + i * 40; out += rect(x + 20, yy, 12, 12, i <= (permission ? 2 : 3) ? (permission && i === 2 ? c.warning : c.success) : c.border, "none", 6) + text(s[0], x + 48, yy + 11, 12, c.text, 650) + text(s[1], x + 150, yy + 11, 12, c.muted); });
  if (permission) out += button("Deny", x + w - 250, y + 278, "secondary", false, 92) + button("Allow once", x + w - 146, y + 278, "primary", false, 126); else out += button("Open result evidence", x + w - 176, y + 278, "primary", false, 158); return out;
}

function governance(screen, d, x, y, w) {
  let out = alert(x, y, w, "One skill conflict needs review", "No file was overwritten. The duplicate is isolated until an explicit audit decision.", "warning");
  out += card(x, y + 84, w, 320, "Governance inventory", "Search and audit canonical assets without hiding quarantine or stale profiles.");
  const items = [["cis-agent-execution", "Active · exact provider contract", "Healthy", "success"], ["agent-runner", "Duplicate command authority", "Conflict", "danger"], ["legacy-vscode-flow", "Moved to .cis/quarantine/skills", "Quarantined", "warning"], ["testing-standard", "Current · source digest verified", "Healthy", "success"], ["repository-profile", "Provider profile changed", "Stale", "warning"]];
  items.forEach((r, i) => out += row(x + 8, y + 136 + i * 50, w - 16, r[0], r[1], r[2], r[3], i === 1));
  out += button("Run skills audit", x + 18, y + 368, "secondary", false, 134) + button("Review suggested fix", x + w - 176, y + 368, "primary", false, 158); return out;
}

function commandProgress(screen, d, x, y, w) {
  const invalid = screen.state === "invalid-evidence"; let out = card(x, y, w, d.compact ? 260 : 326, "CIS Output · OP-20260828-001", "Bounded and redacted output; original process result remains authoritative.");
  out += rect(x + 18, y + 64, w - 36, d.compact ? 126 : 190, "#1E1E1E", "none", 3) + multi(["> cis design render CIS-0001 --format json", "Validating wireframe digest…", "Resolving pinned Sharp runtime…", invalid ? "ERROR expected result manifest is missing" : "Rendering 23 review artifacts… 18/23"], x + 34, y + 92, 12, invalid ? "#FCA5A5" : "#D1D5DB", 500, 26);
  if (!invalid) out += rect(x + 18, y + (d.compact ? 212 : 276), w - 220, 5, "#E5E7EB", "none", 3) + rect(x + 18, y + (d.compact ? 212 : 276), (w - 220) * 0.78, 5, c.focus, "none", 3) + button("Cancel", x + w - 120, y + (d.compact ? 198 : 262), "secondary", false, 92);
  const nw = Math.min(430, w - 40), nx = x + w - nw - 20, ny = invalid ? y + (d.compact ? 278 : 350) : y + (d.compact ? 278 : 350);
  out += rect(nx + 6, ny + 6, nw, 92, c.shadow, "none", 5) + rect(nx, ny, nw, 92, c.white, invalid ? c.danger : c.border, 5) + text(invalid ? "Invalid evidence" : "Rendering design pack", nx + 16, ny + 28, 13, invalid ? c.danger : c.text, 700) + text(invalid ? "Process exited 0, but expected manifest was unreadable." : "18 of 23 artifacts complete", nx + 16, ny + 52, 12, c.secondary) + text(invalid ? "Open diagnostics" : "Show Output", nx + 16, ny + 76, 11, c.blue, 650); return out;
}

mkdirSync(outputDirectory, { recursive: true });
for (const screen of screens) {
  const d = layout(screen); const output = path.join(outputDirectory, screen.file);
  await sharp(Buffer.from(renderScreen(screen))).png().toFile(output);
  const metadata = await sharp(output).metadata(); const stats = await sharp(output).stats();
  if (metadata.width !== d.W || metadata.height !== d.H || stats.channels.every(channel => channel.stdev === 0)) throw new Error(`Invalid or blank render: ${screen.file}`);
  console.log(`Rendered ${screen.file} (${metadata.width}x${metadata.height})`);
}
console.log(`Runtime node=${process.version} sharp=${sharp.versions.sharp} libvips=${sharp.versions.vips}`);
