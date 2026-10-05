'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { loadExistingDocuments } = require('../lib/existing-documents');
const { productPaths } = require('../lib/product-journey');

function pickerVscode(command = 'select-file', window = {}) {
  return { ViewColumn: { Active: 1 }, window: { showInformationMessage() {}, ...window,
    createWebviewPanel: () => ({ dispose() {}, onDidDispose() {}, webview: { cspSource: 'test:',
      onDidReceiveMessage: receive => { setImmediate(() => receive({ command, value: '0' })); } } }),
  } };
}

test('load existing documents uses CLI discovery and registers the chosen project path', async () => {
  const calls = [];
  const cli = { query: async args => { calls.push(args); return { documents: [{ role: 'business', title: 'Business requirements',
    candidates: [{ title: 'Existing BRD', path: 'docs/applications/parr/brd-spec.md' }] }] }; },
  runForeground: async (title, args) => { calls.push(args); return {}; }, clearQueryCache() {} };
  const vscode = pickerVscode();
  assert.equal(await loadExistingDocuments(vscode, cli, '/project'), true);
  assert.deepEqual(calls[1], ['definition', 'documents', '--workspace', '/project', '--role', 'business', '--path', 'docs/applications/parr/brd-spec.md']);
  assert.deepEqual(calls.slice(2), [['graph', 'build', '--workspace', '/project'], ['brd', 'reconcile', '--workspace', '/project'], ['graph', 'build', '--workspace', '/project']]);
});

test('cancelling document loading does not register a document', async () => {
  const cli = { query: async () => ({ documents: [] }), runForeground: () => assert.fail('must not load') };
  assert.equal(await loadExistingDocuments(pickerVscode('cancel'), cli, '/project'), false);
});

test('all product actions resolve loaded paths and reject paths outside the project', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-existing-documents-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'));
    const config = path.join(root, '.cis/product-documents.json');
    fs.writeFileSync(config, JSON.stringify({ business: 'docs/legacy/brd.md', technical: 'docs/legacy/intent.md' }));
    const resolved = productPaths(root, { documentationPath: path.join(root, 'docs/cis') });
    assert.equal(resolved.brd, path.join(root, 'docs/legacy/brd.md'));
    assert.equal(resolved.technicalIntent, path.join(root, 'docs/legacy/intent.md'));
    assert.equal(resolved.backlog, path.join(root, 'docs/cis/plans/high-level-backlog.md'));
    fs.writeFileSync(config, JSON.stringify({ business: '../outside.md' }));
    assert.throws(() => productPaths(root, { documentationPath: path.join(root, 'docs/cis') }), /Invalid project document selection/u);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('manual loading opens the file picker without running discovery on each supported wizard page', async () => {
  for (const page of ['foundation', 'business', 'technical', 'architecture', 'contracts', 'experience', 'delivery', 'review']) {
    const calls = [];
    const root = path.resolve('project');
    const vscode = { Uri: { file: fsPath => ({ fsPath }) }, window: {
      showQuickPick: async items => items[0],
      showOpenDialog: async options => { calls.push(options); return [{ fsPath: path.join(root, 'docs', 'selected.md') }]; },
      showInformationMessage() {},
    } };
    const cli = { query: () => assert.fail('manual loading must bypass discovery'),
      runForeground: async (_title, args) => { calls.push(args); return {}; }, clearQueryCache() {} };
    assert.equal(await loadExistingDocuments(vscode, cli, root, page, { manual: true }), true);
    assert.deepEqual(calls[0].filters, { Markdown: ['md'] });
    assert.equal(calls[1].at(-1), 'docs/selected.md');
    assert.equal(calls[1][calls[1].indexOf('--role') + 1], ['business', 'technical', 'architecture', 'experience', 'delivery'].includes(page) ? page : 'business');
  }
});

test('manual picker cancellation never loads or discovers a document', async () => {
  const cli = { query: () => assert.fail('must not discover'), runForeground: () => assert.fail('must not load') };
  const vscode = { Uri: { file: fsPath => ({ fsPath }) }, window: { showOpenDialog: async () => undefined } };
  assert.equal(await loadExistingDocuments(vscode, cli, '/project', 'business', { manual: true }), false);
});

test('a supplied file outside the project is imported by its absolute path', async () => {
  const root = path.resolve('project');
  const source = path.resolve('supplied documents', 'Business requirements.md');
  const calls = [];
  const vscode = { Uri: { file: fsPath => ({ fsPath }) }, window: {
    showOpenDialog: async () => [{ fsPath: source }], showInformationMessage() {},
  } };
  const cli = { runForeground: async (_title, args) => { calls.push(args); return {}; }, clearQueryCache() {} };
  assert.equal(await loadExistingDocuments(vscode, cli, root, 'business', { manual: true }), true);
  assert.deepEqual(calls[0], ['definition', 'documents', '--workspace', root, '--role', 'business', '--source', source]);
  assert.equal(calls[2][0], 'brd');
});

test('wizard discovery reuses the displayed candidates and surfaces registration failures', async () => {
  const documents = { documents: [{ role: 'business', title: 'Business requirements', candidates: [{ title: 'BRD', path: 'docs/brd.md' }] }] };
  const cli = { query: () => assert.fail('already discovered by wizard'), runForeground: async () => { throw new Error('Selected document is missing'); } };
  const vscode = pickerVscode('select-file', { showInformationMessage: () => assert.fail('must not report success') });
  await assert.rejects(loadExistingDocuments(vscode, cli, '/project', 'business', { documents }), /Selected document is missing/u);
});

test('reconciliation failures retain the loaded-file state and never claim success', async () => {
  const documents = { documents: [{ role: 'business', title: 'BRD', candidates: [{ title: 'BRD', path: 'docs/brd.md' }] }] };
  const calls = [];
  const cli = { runForeground: async (_title, args) => {
    calls.push(args); if (args[0] === 'brd') throw new Error('Conflicting identity');
  }, clearQueryCache() {} };
  await assert.rejects(loadExistingDocuments(pickerVscode('select-file', { showInformationMessage: () => assert.fail('must not report success') }),
    cli, '/project', 'business', { documents }), error => error.documentLoaded === true && /Loaded docs\/brd.md.*needs attention/u.test(error.message));
  assert.equal(calls.length, 3);
});

test('technical imports prepare unanswered choices and finish loading without attempting generation', async () => {
  const calls = []; const messages = []; let cleared = false;
  const documents = { documents: [{ role: 'technical', title: 'Technical intent', candidates: [{ title: 'Intent', path: 'docs/intent.md' }] }] };
  const cli = { runForeground: async (_title, args) => {
    calls.push(args); return args[1] === 'questions' ? { status: 'initialized', complete: false, current: true, errors: [] } : {};
  }, clearQueryCache() { cleared = true; } };
  assert.equal(await loadExistingDocuments(pickerVscode('select-file', { showInformationMessage: message => messages.push(message) }),
    cli, '/project', 'technical', { documents }), true);
  assert.deepEqual(calls.slice(1), [
    ['graph', 'build', '--workspace', '/project'],
    ['technical-intent', 'questions', 'init', '--workspace', '/project'],
    ['graph', 'build', '--workspace', '/project'],
  ]);
  assert.equal(cleared, true);
  assert.match(messages[0], /Review and answer the technical choices next/u);
  assert.doesNotMatch(messages[0], /and reconciled its evidence/u);
});

test('technical imports reconcile only after current technical choices are complete', async () => {
  const calls = [];
  const documents = { documents: [{ role: 'technical', title: 'Technical intent', candidates: [{ title: 'Intent', path: 'docs/intent.md' }] }] };
  const cli = { runForeground: async (_title, args) => {
    calls.push(args); return args[1] === 'questions' ? { complete: true, current: true, errors: [] } : {};
  }, clearQueryCache() {} };
  assert.equal(await loadExistingDocuments(pickerVscode(), cli, '/project', 'technical', { documents }), true);
  assert.deepEqual(calls.slice(1).map(args => args.slice(0, args.indexOf('--workspace'))),
    [['graph', 'build'], ['technical-intent', 'questions', 'init'], ['technical-intent', 'init'], ['graph', 'build']]);
});

test('questionnaire preparation errors preserve the import and remain visible', async () => {
  const documents = { documents: [{ role: 'technical', title: 'Intent', candidates: [{ title: 'Intent', path: 'docs/intent.md' }] }] };
  const cli = { runForeground: async (_title, args) => args[1] === 'questions' ? { errors: ['The BRD needs approval.'] } : {}, clearQueryCache() {} };
  await assert.rejects(loadExistingDocuments(pickerVscode('select-file', { showInformationMessage: () => assert.fail('must not report success') }),
    cli, '/project', 'technical', { documents }), error => error.documentLoaded && /The BRD needs approval/u.test(error.message));
});
