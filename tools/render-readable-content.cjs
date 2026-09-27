'use strict';

// Production renderers with deterministic, synthetic domain fixtures. No CLI/model/network.
// Optional Playwright dependency; see the readable-content implementation reference.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { execFileSync } = require('node:child_process');
const { pathToFileURL } = require('node:url');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const output = path.join(root, 'artifacts', 'readable-content', 'rendered');
const baseline = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-content-render-baseline-'));
const revision = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim();
for (const file of execFileSync('git', ['ls-tree', '-r', '--name-only', revision, 'vscode-extension/lib'], { cwd: root, encoding: 'utf8' }).trim().split('\n')) {
  const target = path.join(baseline, file); fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, execFileSync('git', ['show', `${revision}:${file}`], { cwd: root }));
}
const webview = { cspSource: 'https://fixture.invalid', asWebviewUri: value => value };
function fixtures(source) {
  const lib = name => require(path.join(source, 'vscode-extension/lib', name));
  const { studioDocument, renderDefinitionWizardHtml } = lib('webview');
  const { escapeHtml } = lib('security');
  const wrap = (title, body) => studioDocument(webview, title, `<h1>${title}</h1>${body}`, 'fixture');
  const setup = lib('getting-started').gettingStartedModel({ workspace: { workspaceFolders: [{}], isTrusted: true,
    getConfiguration: () => ({ get: () => 'docs' }) } }, { root: () => baseline });
  const decision = { id: 'TI-DEC-001', decision: 'Choose the message delivery approach', status: 'Open', requiredBefore: 'Technical approval', needsReview: true,
    rationale: 'Delivery latency remains uncertain until the prototype is measured.', suggestedResolution: 'Use queued delivery if the prototype meets the agreed latency threshold.', issues: ['Confirm the latency threshold with the product owner.'] };
  const technical = { id: 'technical', ordinal: 3, title: 'Technical direction', complete: false, current: true, status: 'Needs review',
    artifactPaths: [], issues: [], guidance: { summary: 'One technical decision needs review', nextStep: 'Refresh technical evidence, review the proposed answer and save your choice.', technicalDecisions: [decision], actions: [] } };
  const technicalModel = { pages: [
    { id: 'foundation', ordinal: 1, title: 'Project foundation', status: 'Complete', current: true, complete: true },
    { id: 'business', ordinal: 2, title: 'Business definition', status: 'Active', current: true, complete: true }, technical,
    ...['architecture', 'contracts', 'experience', 'delivery', 'review'].map((id, i) => ({ id, ordinal: i + 4, title: id, status: 'Not started' }))],
    technicalQuestions: { current: true, complete: true, questions: [{ id: 'TI-Q-001', area: 'Delivery', status: 'Answered', answer: 'Prototype queued delivery.', prompt: 'How will messages be delivered?' }] } };
  const experience = { pages: [{ id: 'technical', complete: true, current: true }, { id: 'architecture', complete: true, current: true },
    { id: 'experience', status: 'Draft', complete: false, current: false }], preview: { svgRelativePath: 'docs/ui-preview.svg', status: 'Current' },
    uiQuestions: { current: false, complete: false, questions: [{ id: 'UI-Q-001', status: 'Answered' }] } };
  const e = lib('experience-guidance'); const state = e.experienceState(experience, baseline);
  return {
    onboarding: lib('getting-started').renderGettingStartedHtml(webview, setup, 'fixture'),
    technical: renderDefinitionWizardHtml(webview, baseline, technicalModel, 'technical', 'fixture', {}),
    experience: wrap('UI direction', `<p class="badge warn">${escapeHtml(state.badge)}</p>${e.renderExperienceGuidance(state)}`),
    approval: wrap('Approve technical intent', lib('architecture-approval').renderDocumentApproval({ pages: [
      { id: 'business', complete: true, current: true, status: 'Ready for Approval', primaryPath: 'docs/brd.md' },
      { id: 'technical', complete: true, current: true, status: 'Ready for Approval', primaryPath: 'docs/technical.md' }] }, 'technical')),
    recovery: renderDefinitionWizardHtml(webview, baseline, { ...technicalModel,
      technicalDecisionSave: { id: 'TI-DEC-001', refreshed: false, error: 'The readiness command timed out.' } }, 'technical', 'fixture', {}),
  };
}
(async () => {
  const browser = await chromium.launch({ headless: true });
  const results = [];
  try {
    for (const [version, source] of [['before', baseline], ['after', root]]) {
      const directory = path.join(output, version); fs.mkdirSync(directory, { recursive: true });
      for (const [scenario, html] of Object.entries(fixtures(source))) {
        // The VS Code host normally supplies these theme variables; use a fixed fixture theme.
        const theme = '<style nonce="fixture">:root{--vscode-editor-background:#1e1e1e;--vscode-foreground:#ddd;--vscode-descriptionForeground:#aaa;--vscode-focusBorder:#007fd4;--vscode-button-background:#0e639c;--vscode-button-foreground:#fff;--vscode-button-secondaryBackground:#3a3d41;--vscode-button-secondaryForeground:#fff;--vscode-input-background:#3c3c3c;--vscode-input-foreground:#ddd;--vscode-panel-border:#555;--vscode-textLink-foreground:#75beff;--vscode-font-family:Arial,sans-serif;--vscode-font-weight:400;--vscode-font-size:14px}</style>';
        const file = path.join(directory, `${scenario}.html`); fs.writeFileSync(file, html.replace('</head>', theme + '</head>'));
        for (const [view, width, zoom] of [['desktop', 1000, 1], ['narrow', 320, 1], ['zoom200', 640, 2]]) {
          const page = await browser.newPage({ viewport: { width, height: 900 } });
          await page.addInitScript(() => { globalThis.acquireVsCodeApi = () => ({ postMessage() {}, getState() { return {}; }, setState() {} }); });
          await page.goto(pathToFileURL(file).href);
          await page.evaluate(zoom => { document.body.style.zoom = String(zoom); }, zoom);
          const state = await page.evaluate(() => {
            const visible = el => Boolean(el.getClientRects().length) && getComputedStyle(el).visibility !== 'hidden';
            return { text: document.body.innerText, overflow: document.documentElement.scrollWidth > innerWidth + 1,
              unnamed: [...document.querySelectorAll('button,input,textarea,select')].filter(visible).filter(el => !el.textContent.trim() && !el.getAttribute('aria-label') && !el.labels?.length).map(el => el.outerHTML),
              disabled: [...document.querySelectorAll('button:disabled')].filter(visible).map(el => el.textContent.trim()) };
          });
          await page.keyboard.press('Tab');
          const keyboard = await page.evaluate(() => ({ tag: document.activeElement.tagName, name: document.activeElement.textContent.trim() || document.activeElement.labels?.[0]?.textContent.trim(), outline: getComputedStyle(document.activeElement).outlineStyle }));
          const targets = await page.evaluate(() => [...document.querySelectorAll('button:not(:disabled),summary,input:not(:disabled),textarea:not(:disabled),select:not(:disabled),a[href]')]
            .filter(el => el.getClientRects().length && !el.closest('[hidden]') && ![...document.querySelectorAll('details:not([open])')].some(details => details.contains(el) && !details.querySelector('summary')?.contains(el)))
            .map((el, index) => { el.dataset.fixtureFocus = String(index); return { id: String(index), name: el.textContent.trim() || el.labels?.[0]?.textContent.trim() }; }));
          const reached = new Set();
          for (let index = 0; index <= targets.length + 1; index++) {
            reached.add(await page.evaluate(() => document.activeElement.dataset.fixtureFocus));
            await page.keyboard.press('Tab');
          }
          const keyboardMissing = targets.filter(target => !reached.has(target.id));
          await page.screenshot({ path: path.join(directory, `${scenario}-${view}.png`), fullPage: true });
          if (view === 'desktop') fs.writeFileSync(path.join(directory, `${scenario}.txt`), state.text);
          results.push({ version, scenario, view, overflow: state.overflow, unnamed: state.unnamed, disabled: state.disabled, keyboard, keyboardMissing });
          await page.close();
        }
      }
    }
    fs.writeFileSync(path.join(output, 'checks.json'), JSON.stringify({ revision, note: 'Production HTML renderers with synthetic fixtures in headless Chromium. CSS zoom approximates 200%; this is not VS Code host usability testing.', results }, null, 2));
    const failures = results.filter(r => r.version === 'after' && (r.overflow || r.unnamed.length || r.keyboardMissing.length || !r.keyboard.name || r.keyboard.outline === 'none'));
    console.log(JSON.stringify({ output, captures: results.length, failures }, null, 2));
    if (failures.length) process.exitCode = 1;
  } finally {
    await browser.close();
    if (path.dirname(baseline) === os.tmpdir() && path.basename(baseline).startsWith('cis-content-render-baseline-')) fs.rmSync(baseline, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
