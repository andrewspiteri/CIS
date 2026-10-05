'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { proposeBrdSections, renderSectionProposal } = require('../lib/brd-sections');
const { CisCli } = require('../lib/cis-cli');

const proposal = { id: 'a'.repeat(64), path: 'docs/brd.md', sections: [{ heading: 'Scope', content: 'Existing scope', evidenceQuotes: ['Existing scope evidence.'] }],
  warnings: ['Success measures still need review.'], originalContent: 'status: Active\n# BRD\nExisting scope\n',
  proposedContent: 'status: Review Required\n# BRD\nExisting scope\n## Scope\nExisting scope\n<script>untrusted</script>\n' };

function fixture({ remote = false, failGraph = false, failApply = false, consent = true, providers, pick = 0 } = {}) {
  const calls = []; const panels = []; const saved = []; let authorityChecks = 0;
  let choices = []; const messages = [];
  const vscode = { ViewColumn: { Active: 1 }, ProgressLocation: { Notification: 1 }, window: {
    showQuickPick: async items => { choices = items; return items[pick]; }, showWarningMessage: async () => consent ? 'Send BRD and suggest' : undefined,
    showInformationMessage: message => { messages.push(message); return new Promise(() => {}); }, withProgress: (_options, operation) => operation(),
    createWebviewPanel: () => {
      const panel = { dispose() { this.onDispose(); }, onDidDispose(handler) { this.onDispose = handler; },
        webview: { onDidReceiveMessage(handler) { this.receive = handler; }, html: '' } };
      panels.push(panel); return panel;
    },
  } };
  const cli = { query: async (args, options) => {
    calls.push(args);
    if (args[0] === 'brd') assert.equal(options.timeout, args.includes('--allow-remote') ? 660_000 : 300_000,
      'The UI must allow the entire backend generation budget plus process overhead');
    if (args[0] === 'ai') assert.deepEqual(
      CisCli.prototype.arguments.call({ root: () => '/workspace' }, args, options),
      ['ai', 'status', '--format', 'json'], 'AI provider discovery must not receive repository arguments');
    return args[0] === 'ai' ? { providers: providers || [{ name: remote ? 'remote' : 'ollama', isAvailable: true, isLocal: !remote, models: [{ name: 'fixture' }] }] } : proposal;
  }, runForeground: async (_title, args) => {
    calls.push(args);
    if (failApply && args[2] === 'apply') throw new Error('The BRD changed after review.');
    if (failGraph && args[0] === 'graph') throw new Error('Graph unavailable');
    return { exitCode: 0 };
  } };
  return { calls, panels, saved, run: () => proposeBrdSections(vscode, cli, '/workspace', async () => 'Human reviewer', () => { authorityChecks++; }, state => saved.push({ ...state, calls: calls.length })),
    checks: () => authorityChecks, choices: () => choices, messages };
}

async function waitForReview(f) {
  for (let i = 0; i < 20 && !f.panels.length; i++) await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.panels.length, 1);
  return f.panels[0];
}

test('BRD proposal renders escaped plus/minus diff with approval and supporting excerpts', () => {
  const html = renderSectionProposal(proposal, 'nonce');
  assert.match(html, /-status: Active/u); assert.match(html, /\+status: Review Required/u);
  assert.match(html, /\+## Scope/u); assert.match(html, /&lt;script&gt;untrusted/u);
  assert.doesNotMatch(html, /<script>untrusted/u);
  assert.match(html, /Approve changes and refresh graph/u);
  assert.match(html, /Existing scope evidence\./u);
});

test('BRD remains unchanged until diff approval, then applies once before graph refresh', async () => {
  const f = fixture(); const result = f.run(); const panel = await waitForReview(f);
  assert.equal(f.calls.length, 2, 'Only provider status and proposal generation occur before review');
  panel.webview.receive({ command: 'approve' }); panel.webview.receive({ command: 'approve' });
  assert.equal(await result, true);
  assert.deepEqual(f.calls.slice(2).map(args => args.slice(0, 3)), [['brd', 'sections', 'apply'], ['graph', 'build', '--workspace']]);
  assert.ok(f.calls[2].includes(proposal.id)); assert.ok(f.calls[2].includes('Human reviewer'));
  assert.equal(f.checks(), 3);
  assert.deepEqual(f.saved.map(state => [state.phase, state.calls]), [['saved', 3], ['graph-refreshed', 4]]);
  assert.deepEqual(f.saved[0].warnings, proposal.warnings);
});

for (const close of [false, true]) test(`cancelling BRD diff ${close ? 'by closing it' : 'with Cancel'} never applies or builds`, async () => {
  const f = fixture(); const result = f.run(); const panel = await waitForReview(f);
  if (close) panel.dispose(); else panel.webview.receive({ command: 'cancel' });
  assert.equal(await result, false); assert.equal(f.calls.length, 2);
});

test('BRD proposal requires explicit remote disclosure before generation', async () => {
  const refused = fixture({ remote: true, consent: false });
  assert.equal(await refused.run(), false); assert.equal(refused.calls.length, 1);
  const approved = fixture({ remote: true }); const result = approved.run();
  (await waitForReview(approved)).webview.receive({ command: 'cancel' }); await result;
  assert.ok(approved.calls[1].includes('--allow-remote'));
});

test('stale proposals do not rebuild, while failed graph refresh reports saved changes', async () => {
  for (const failApply of [true, false]) {
    const f = fixture({ failApply, failGraph: !failApply }); const result = f.run();
    const failure = assert.rejects(result, error => failApply ? /BRD changed/u.test(error.message)
      : error.documentApplied === true && /changes were saved/u.test(error.message));
    (await waitForReview(f)).webview.receive({ command: 'approve' });
    await failure;
    assert.equal(f.calls.some(args => args[0] === 'graph'), !failApply);
    assert.deepEqual(f.saved.map(state => state.phase), failApply ? [] : ['saved']);
  }
});

for (const provider of ['codex', 'claude']) test(`account model picker routes ${provider} through remote consent and diff review`, async () => {
  const providers = [
    { name: 'ollama', isAvailable: true, isLocal: true, models: [{ name: 'local' }] },
    { name: provider, isAvailable: true, isLocal: false, models: [{ name: 'account-model' }], detail: 'Signed-in account' },
  ];
  const refused = fixture({ providers, pick: 1, consent: false });
  assert.equal(await refused.run(), false); assert.equal(refused.calls.length, 1);
  const f = fixture({ providers, pick: 1 }); const result = f.run();
  const panel = await waitForReview(f);
  assert.match(f.choices()[1].label, new RegExp(provider));
  assert.ok(f.calls[1].includes('--allow-remote'));
  assert.equal(f.calls[1][f.calls[1].indexOf('--provider') + 1], provider);
  assert.equal(f.calls[1][f.calls[1].indexOf('--model') + 1], 'account-model');
  panel.webview.receive({ command: 'cancel' });
  assert.equal(await result, false); assert.equal(f.calls.length, 2);
});

test('unavailable account providers remain visible and explain setup without starting generation or waiting on notification', async () => {
  const f = fixture({ providers: [{ name: 'claude', isAvailable: false, isLocal: false, models: [], detail: 'Sign in to Claude Code.' }] });
  assert.equal(await f.run(), false);
  assert.match(f.choices()[0].label, /claude/u);
  assert.match(f.choices()[0].description, /Unavailable/u);
  assert.deepEqual(f.messages, ['Sign in to Claude Code.']); assert.equal(f.calls.length, 1);
});
