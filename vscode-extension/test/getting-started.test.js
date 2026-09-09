'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { ACTIONS, gettingStartedModel, openGettingStartedPanel, renderGettingStartedHtml } = require('../lib/getting-started');

function fixture(root, trusted = true) {
  return { workspace: { workspaceFolders: root ? [{ name: 'authority' }] : [], isTrusted: trusted,
    getConfiguration: () => ({ get: () => 'docs/cis' }) } };
}

test('Getting Started routes missing folder, authority selection, and trust before initialization', () => {
  const vscode = fixture();
  const authority = { root: () => undefined };
  assert.equal(gettingStartedModel(vscode, authority).next.action, 'open-folder');
  vscode.workspace.workspaceFolders = [{ name: 'one' }, { name: 'two' }];
  assert.equal(gettingStartedModel(vscode, authority).next.action, 'select');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-start-'));
  try {
    const untrusted = gettingStartedModel(fixture(root, false), { root: () => root });
    assert.equal(untrusted.next.action, 'trust');
    assert.equal(untrusted.canInitialize, false);
    assert.equal(untrusted.canContinue, false);
    const empty = gettingStartedModel(fixture(root), { root: () => root });
    assert.equal(empty.next.action, 'initialize');
    assert.equal(empty.canInitialize, true);
    assert.deepEqual(fs.readdirSync(root), [], 'Opening setup does not create files');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('Getting Started recognizes an authority and preserves an invalid existing registry', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-start-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'));
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: docs\ndocumentation_root: custom-docs\n');
    const registry = path.join(root, '.cis', 'workspace.yml');
    fs.writeFileSync(registry, 'schema_version: 1\nrepositories: []\n');
    const invalid = gettingStartedModel(fixture(root), { root: () => root });
    assert.equal(invalid.canInitialize, false);
    assert.equal(invalid.next.action, 'doctor');
    assert.match(fs.readFileSync(registry, 'utf8'), /schema_version: 1/u);
    fs.writeFileSync(registry, 'schema_version: 2\necosystem:\n  id: bridgelink\n  name: BridgeLink\nproduct:\n  id: fixed-term-deposits\n  name: Fixed Term Deposits\n');
    const ready = gettingStartedModel(fixture(root), { root: () => root });
    assert.equal(ready.configured, true);
    assert.equal(ready.canInitialize, false);
    assert.equal(ready.canContinue, true);
    assert.equal(ready.next.action, 'wizard');
    assert.equal(ready.product, 'Fixed Term Deposits');
    assert.equal(ready.documentationRoot, 'custom-docs');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('Getting Started renders guide, ordered setup, disabled prerequisites, and escaped product data', () => {
  const model = gettingStartedModel(fixture(), { root: () => undefined });
  const html = renderGettingStartedHtml({ cspSource: 'vscode-webview:' }, model, 'test-nonce');
  for (const label of ['How to use CIS', 'Your next step', 'Step 1', 'Step 2', 'Step 3', 'Step 4', 'What comes next?'])
    assert.ok(html.includes(label), label);
  assert.match(html, /data-command="initialize" disabled/u);
  assert.match(html, /data-command="wizard" disabled/u);
  assert.match(html, /script-src 'nonce-test-nonce'/u);
  const ready = renderGettingStartedHtml({ cspSource: 'vscode-webview:' }, {
    ...model, root: 'C:\\docs', hasFolders: true, product: '<script>unsafe</script>', configured: true, canContinue: true,
  }, 'test-nonce');
  assert.match(ready, /&lt;script&gt;unsafe&lt;\/script&gt;/u);
  assert.match(ready, /data-command="wizard" >Open high-level wizard/u);
  assert.match(ready, /data-command="initialize" disabled>Authority initialized/u);
});

test('Getting Started dispatches only its fixed action vocabulary and packages the offline guide', async () => {
  let receive;
  const actions = [];
  const vscode = { ViewColumn: { Active: 1 }, window: { createWebviewPanel: () => ({ webview: {
    cspSource: 'vscode-webview:', onDidReceiveMessage: handler => { receive = handler; },
  } }) } };
  openGettingStartedPanel(vscode, gettingStartedModel(fixture(), { root: () => undefined }), async action => actions.push(action));
  await receive({ command: 'guide' });
  await receive({ command: 'wizard', value: 'ignored' });
  await receive({ command: 'execute', value: 'cis repo init --yes' });
  assert.deepEqual(actions, ['guide', 'wizard']);
  assert.equal(ACTIONS.initialize, 'cis.authorityInit');
  assert.equal(ACTIONS.wizard, 'cis.definitionWizard');
  const guide = fs.readFileSync(path.join(__dirname, '..', 'GETTING_STARTED.md'), 'utf8');
  assert.match(guide, /## 5\. Find the next action/u);
  assert.match(guide, /\[CIS extension manual\]\(README.md\)/u);
});
