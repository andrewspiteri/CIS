'use strict';

const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const Module = require('node:module');
const { EventEmitter: NodeEventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');

const configuration = { executablePath: 'cis', documentationRoot: 'docs/cis', actorIdentity: '' };
class TreeItem { constructor(label, state) { this.label = label; this.collapsibleState = state; } }
class ThemeIcon { constructor(id) { this.id = id; } }
class EventEmitter { constructor() { this.event = () => {}; } fire() {} }
const vscode = {
  TreeItem,
  TreeItemCollapsibleState: { None: 0, Collapsed: 1, Expanded: 2 },
  ThemeIcon,
  Uri: { file: value => ({ fsPath: value, toString: () => `file://${value.replaceAll('\\', '/')}` }) },
  EventEmitter,
  workspace: {
    workspaceFolders: undefined,
    isTrusted: true,
    getConfiguration: () => ({ get: (key, fallback) => configuration[key] ?? fallback }),
  },
};
const originalLoad = Module._load;
Module._load = function (request, parent, isMain) {
  return request === 'vscode' ? vscode : originalLoad.call(this, request, parent, isMain);
};
const extension = require('../extension');
Module._load = originalLoad;
const { hasExistingRepositoryEvidence, repositoryMetadata } = require('../lib/views');
const { AuthoritySelector } = require('../lib/authority');
const { CisCli, foregroundFailureMessage, isCacheableQuery, safeCommandDisplay } = require('../lib/cis-cli');
const { projectChangeOverview } = require('../lib/projections');
const { documentStatus, isActiveCurrent, isReadyForApproval, linkedFeatureItems, nextStartableItem, productPaths, stateOf } = require('../lib/product-journey');
const { bound, isValidWebviewMessage, redact, resolveWithin, validateExecutable } = require('../lib/security');
const {
  openCommandProgressPanel, openDesignPanel, openEvidencePanel, renderAgentRequestHtml, renderBrdQuestionsHtml, renderTechnicalIntentQuestionsHtml, renderUiDirectionQuestionsHtml, renderDefinitionWizardHtml, renderDoctorHtml, renderChangeOverviewHtml, renderHtml, renderRecommendationReviewHtml,
  renderCommandProgressHtml, renderContextHtml, renderRunDetailHtml, renderTaskDetailHtml,
} = require('../lib/webview');

function state(initial) {
  const values = new Map(Object.entries(initial || {}));
  return { get: key => values.get(key), update: async (key, value) => value === undefined ? values.delete(key) : values.set(key, value), values };
}

test('CisCli rejects queries without an open authority repository', () => {
  vscode.workspace.workspaceFolders = undefined;
  const cli = new CisCli(vscode, { appendLine() {} }, new AuthoritySelector(vscode, state()));
  assert.throws(() => cli.query(['repo', 'doctor']), /Open a repository folder first/);
});

test('CisCli disables every process query in an untrusted workspace', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    vscode.workspace.workspaceFolders = [{ name: 'repo', uri: vscode.Uri.file(root) }];
    vscode.workspace.isTrusted = false;
    const cli = new CisCli(vscode, { appendLine() {} }, new AuthoritySelector(vscode, state()));
    assert.throws(() => cli.query(['repo', 'doctor']), error => error.kind === 'untrusted-workspace');
  } finally { vscode.workspace.isTrusted = true; fs.rmSync(root, { recursive: true, force: true }); }
});

test('CisCli constructs argument arrays with one authority and JSON format', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    vscode.workspace.workspaceFolders = [{ name: 'repo', uri: vscode.Uri.file(root) }];
    const cli = new CisCli(vscode, { appendLine() {} }, new AuthoritySelector(vscode, state()));
    assert.deepEqual(cli.arguments(['agent', 'providers']), ['agent', 'providers', '--repo', root, '--format', 'json']);
    assert.deepEqual(cli.arguments(['--version'], { repository: false, format: false }), ['--version']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// Trace: TC-VSC-001-001, TC-VSC-016-001, TC-VSC-017-001.
test('TC-VSC-001-001 TC-VSC-016-001 TC-VSC-017-001 CisCli classifies structured success, malformed evidence, and non-zero exits', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    vscode.workspace.workspaceFolders = [{ name: 'repo', uri: vscode.Uri.file(root) }];
    const authority = new AuthoritySelector(vscode, state());
    const output = { appendLine() {} };
    const success = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => callback(null, '{"status":"healthy"}', '') });
    assert.equal((await success.query(['repo', 'doctor'])).status, 'healthy');

    const malformed = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => callback(null, '{', '') });
    await assert.rejects(malformed.query(['repo', 'doctor']), error => error.kind === 'invalid-evidence' && error.exitCode === 0);

    const arrayResult = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => callback(null, '[{"status":"healthy"}]', '') });
    await assert.rejects(arrayResult.query(['repo', 'doctor']), error => error.kind === 'invalid-evidence' && error.exitCode === 0);

    const failure = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => {
      const error = new Error('failed'); error.code = 7; callback(error, '{"diagnostics":["ERROR: bounded failure"]}', '');
    } });
    await assert.rejects(failure.query(['repo', 'doctor']), error => error.kind === 'command-failed' && error.exitCode === 7
      && error.data.diagnostics[0] === 'ERROR: bounded failure');

    const doctorFailure = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => {
      const error = new Error('doctor findings'); error.code = 5;
      callback(error, '{"status":"errors","errorCount":1,"findings":[{"code":"CIS-X","severity":"error"}]}', '');
    } });
    const doctor = await doctorFailure.query(['repo', 'doctor'], { acceptStructuredFailure: true });
    assert.equal(doctor.status, 'errors'); assert.equal(doctor._process.exitCode, 5);
    assert.equal(doctor._process.failed, true); assert.equal(doctor.findings[0].code, 'CIS-X');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('CisCli reports supported and incompatible CLI versions', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    vscode.workspace.workspaceFolders = [{ name: 'repo', uri: vscode.Uri.file(root) }];
    const authority = new AuthoritySelector(vscode, state());
    const output = { appendLine() {} };
    const compatible = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => callback(null, '0.3.0+abc\n', '') });
    const incompatible = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => callback(null, '1.0.0\n', '') });
    assert.equal((await compatible.version()).compatible, true);
    assert.equal((await incompatible.version()).compatible, false);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('CisCli shares in-flight read projections and invalidates them explicitly or before mutations', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-cli-cache-'));
  const originalFolders = vscode.workspace.workspaceFolders;
  const originalTrusted = vscode.workspace.isTrusted;
  vscode.workspace.workspaceFolders = [{ name: 'cache', uri: vscode.Uri.file(root) }];
  vscode.workspace.isTrusted = true;
  let calls = 0;
  const processes = {
    execFile: (_exe, args, _options, callback) => {
      calls += 1;
      setImmediate(() => callback(null, JSON.stringify({ status: 'ok', command: args.slice(0, 2).join(' ') }), ''));
    },
  };
  try {
    const cli = new CisCli(vscode, { appendLine() {} }, new AuthoritySelector(vscode, state()), processes);
    const first = cli.query(['agent', 'providers']);
    const second = cli.query(['agent', 'providers']);
    assert.equal((await first).status, 'ok');
    assert.equal((await second).status, 'ok');
    assert.equal(calls, 1);

    await cli.query(['agent', 'providers']);
    assert.equal(calls, 1);
    cli.clearQueryCache();
    await cli.query(['agent', 'providers']);
    assert.equal(calls, 2);

    await cli.query(['agent', 'prepare', 'CIS-1', 'WORK-1']);
    await cli.query(['agent', 'providers']);
    assert.equal(calls, 4);
    assert.equal(isCacheableQuery(['repo', 'doctor']), true);
    assert.equal(isCacheableQuery(['solution-design', 'status']), true);
    assert.equal(isCacheableQuery(['ui-direction', 'questions', 'status', '--summary']), true);
    assert.equal(isCacheableQuery(['ui-direction', 'status']), true);
    assert.equal(isCacheableQuery(['definition', 'status']), true);
    assert.equal(isCacheableQuery(['agent', 'prepare', 'CIS-1', 'WORK-1']), false);
  } finally {
    vscode.workspace.workspaceFolders = originalFolders;
    vscode.workspace.isTrusted = originalTrusted;
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('startup status projections cache structured unavailable results for one repository generation', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-cli-startup-cache-'));
  const originalFolders = vscode.workspace.workspaceFolders;
  vscode.workspace.workspaceFolders = [{ name: 'cache', uri: vscode.Uri.file(root) }];
  let calls = 0;
  const processes = { execFile: (_exe, _args, _options, callback) => {
    calls += 1;
    const error = new Error('not ready'); error.code = 4;
    setImmediate(() => callback(error, '{"status":"missing","valid":false}', ''));
  } };
  try {
    const cli = new CisCli(vscode, { appendLine() {} }, new AuthoritySelector(vscode, state()), processes);
    const options = { acceptStructuredFailure: true };
    const first = await cli.query(['solution-design', 'status'], options);
    const second = await cli.query(['solution-design', 'status'], options);
    assert.equal(first.status, 'missing'); assert.equal(second.status, 'missing'); assert.equal(calls, 1);
    cli.clearQueryCache();
    await cli.query(['solution-design', 'status'], options);
    assert.equal(calls, 2);
  } finally {
    vscode.workspace.workspaceFolders = originalFolders;
    fs.rmSync(root, { recursive: true, force: true });
  }
});

// Trace: TC-VSC-013-001, TC-VSC-017-001, TC-VSC-022-001.
test('TC-VSC-013-001 TC-VSC-017-001 TC-VSC-022-001 foreground execution uses spawn without a shell and retains exit evidence', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    vscode.workspace.workspaceFolders = [{ name: 'repo', uri: vscode.Uri.file(root) }];
    let invocation;
    const processes = { spawn: (executable, args, options) => {
      invocation = { executable, args, options };
      const process = new NodeEventEmitter(); process.stdout = new PassThrough(); process.stderr = new PassThrough(); process.kill = () => {};
      setImmediate(() => { process.stdout.write('status=ok\n'); process.stdout.end(); process.stderr.end(); process.emit('close', 0); });
      return process;
    } };
    const localVscode = { ...vscode, ProgressLocation: { Notification: 1 }, window: {
      withProgress: (_options, callback) => callback({ report() {} }, { onCancellationRequested() {} }),
    } };
    localVscode.workspace = vscode.workspace;
    const cli = new CisCli(localVscode, { append() {}, appendLine() {} }, new AuthoritySelector(localVscode, state()), processes);
    const result = await cli.runForeground('Doctor', ['repo', 'doctor']);
    assert.equal(result.state, 'success'); assert.equal(result.exitCode, 0);
    assert.equal(invocation.options.shell, false);
    assert.deepEqual(invocation.args.slice(-4), ['--repo', root, '--format', 'agent']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('CisCli preserves version, runner-failure, cancellation, and truncated-output evidence', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-cli-failures-'));
  const originalFolders = vscode.workspace.workspaceFolders;
  vscode.workspace.workspaceFolders = [{ name: 'repo', uri: vscode.Uri.file(root) }];
  const output = { append() {}, appendLine() {} };
  const authority = new AuthoritySelector(vscode, state());
  const versionFailure = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => {
    const error = new Error('not found'); error.code = 'ENOENT'; callback(error, '', 'missing');
  } });
  const invalidVersion = new CisCli(vscode, output, authority, { execFile: (_exe, _args, _options, callback) => callback(null, 'not-semver', '') });
  try {
    await assert.rejects(versionFailure.version(), error => error.kind === 'missing-cli');
    await assert.rejects(invalidVersion.version(), error => error.kind === 'invalid-evidence');

    const panels = [];
    const localVscode = { ...vscode, ProgressLocation: { Notification: 1 }, ViewColumn: { Active: 1 }, window: {
      createWebviewPanel: () => {
        const panel = { webview: { cspSource: 'vscode-webview:', html: '', onDidReceiveMessage(handler) { this.message = handler; } } };
        panels.push(panel); return panel;
      },
      withProgress: (_options, callback) => callback({ report() {} }, { onCancellationRequested(handler) { setImmediate(handler); } }),
    } };
    localVscode.workspace = vscode.workspace;
    const cancelledProcesses = { spawn: () => {
      const process = new NodeEventEmitter(); process.stdout = new PassThrough(); process.stderr = new PassThrough();
      process.kill = () => setImmediate(() => process.emit('close', 1)); return process;
    } };
    const cancelled = new CisCli(localVscode, output, authority, cancelledProcesses);
    await assert.rejects(cancelled.runForeground('Cancel', ['graph', 'build'], { cancellable: true }), error => error.kind === 'cancelled');

    const failedProcesses = { spawn: () => {
      const process = new NodeEventEmitter(); process.stdout = new PassThrough(); process.stderr = new PassThrough(); process.kill = () => {};
      setImmediate(() => { process.stdout.write('x'.repeat((4 * 1024 * 1024) + 16)); process.stdout.end(); process.stderr.end(); process.emit('close', 4); });
      return process;
    } };
    const failed = new CisCli(localVscode, output, authority, failedProcesses);
    await assert.rejects(failed.runForeground('Fail', ['graph', 'build']), error => error.kind === 'command-failed' && error.data.truncated === true);

    const runnerProcesses = { spawn: () => {
      const process = new NodeEventEmitter(); process.stdout = new PassThrough(); process.stderr = new PassThrough(); process.kill = () => {};
      setImmediate(() => { const error = new Error('spawn failed'); error.code = 'ENOENT'; process.emit('error', error); });
      return process;
    } };
    const runner = new CisCli(localVscode, output, authority, runnerProcesses);
    await assert.rejects(runner.runForeground('Missing', ['graph', 'build']), error => error.kind === 'missing-cli');
    assert.ok(panels.length >= 3);
  } finally {
    vscode.workspace.workspaceFolders = originalFolders;
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('command display redacts human rationale, continuation values, and BRD answers', () => {
  const display = safeCommandDisplay('cis', ['agent', 'resume', 'RUN-1', '--reason', 'contains secret', '--message', 'private prompt',
    '--answer', 'stakeholder answer']);
  assert.doesNotMatch(display, /contains secret|private prompt|stakeholder answer/u);
  assert.match(display, /\[REDACTED-INPUT\]/u);
});

test('foreground failure prioritizes the CIS diagnostic over provider progress telemetry', () => {
  const message = foregroundFailureMessage(
    'status=rejected;exitCode=4;applied=false\ndiagnostic=ERROR: The isolated revision changed protected source provenance.\n',
    'agent-event=process-started;message=started\nagent-event=provider-event;message=thread/started\n', 4);
  assert.equal(message, 'ERROR: The isolated revision changed protected source provenance.');
});

// Trace: TC-VSC-003-001.
test('TC-VSC-003-001 multi-root authority requires and preserves explicit selection', async () => {
  const first = { name: 'first', uri: vscode.Uri.file('C:\\repos\\first') };
  const second = { name: 'second', uri: vscode.Uri.file('C:\\repos\\second') };
  vscode.workspace.workspaceFolders = [first, second];
  const storage = state();
  const localVscode = { ...vscode, window: { showQuickPick: async items => items[1] } };
  localVscode.workspace = vscode.workspace;
  const selector = new AuthoritySelector(localVscode, storage);
  assert.equal(selector.root(), undefined);
  assert.equal(selector.needsSelection(), true);
  await selector.choose();
  assert.equal(selector.root(), second.uri.fsPath);
  vscode.workspace.workspaceFolders = [second, first];
  assert.equal(selector.root(), second.uri.fsPath);
});

test('tree honors the initialized documentation root', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: custom-docs\n');
    const metadata = extension.repositoryMetadata(root, 'docs/cis');
    assert.equal(metadata.id, 'fixture');
    assert.equal(metadata.documentationPath, path.join(root, 'custom-docs'));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('product journey helpers preserve lifecycle authority and dependency order', () => {
  const ready = stateOf({ validation: { valid: true, current: true, effectiveStatus: 'Ready for Approval', errors: [], warnings: [] } });
  const active = stateOf({ validation: { valid: true, current: true, effectiveStatus: 'Active', errors: [], warnings: [] } });
  assert.equal(isReadyForApproval(ready), true);
  assert.equal(isActiveCurrent(active), true);
  assert.equal(nextStartableItem([
    { id: 'HLT-001', dependsOn: [], featureSpecification: 'docs/specs/features/one/feature-specification.md' },
    { id: 'HLT-002', dependsOn: ['HLT-001'], featureSpecification: 'not-created' },
    { id: 'HLT-003', dependsOn: ['HLT-002'], featureSpecification: 'not-created' },
  ]).id, 'HLT-002');
});

test('fresh initialized repository exposes product definition as the single next action', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: empty-product\ndocumentation_root: docs\n');
    const responses = {
      'repo doctor': { warningCount: 0, errorCount: 0, ollama: { isAvailable: false }, findings: [] },
      'change list': { changes: [] },
      'agent providers': { providers: [] },
    };
    const cli = { version: async () => ({ raw: '0.3.0', compatible: true }), query: async args => responses[args.join(' ')] };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const product = items.find(item => item.label === 'Product definition');
    const next = items.find(item => item.label === 'Next: Start product definition');
    assert.ok(product);
    assert.deepEqual(product.children.map(item => item.label), ['1. Workspace authority', '2. Business requirements']);
    assert.equal(next.command.command, 'cis.productStart');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('validated BRD is projected as ready for explicit approval', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Review Required\n---\n# BRD\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        if (args.join(' ') === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (args.join(' ') === 'change list') return { changes: [] };
        if (args.join(' ') === 'agent providers') return { providers: [] };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: true, current: true, effectiveStatus: 'Ready for Approval', documentStatus: 'Review Required', errors: [], warnings: [],
        } };
        throw new Error(`Unexpected query: ${args.join(' ')}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Approve business requirements');
    assert.equal(next.command.command, 'cis.brdApprove');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('active BRD routes through high-level technical choices before technical-intent generation', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Active\n---\n# BRD\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        if (args.join(' ') === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (args.join(' ') === 'change list') return { changes: [] };
        if (args.join(' ') === 'agent providers') return { providers: [] };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: { valid: true, current: true, effectiveStatus: 'Active', errors: [], warnings: [] } };
        if (args[0] === 'technical-intent' && args[1] === 'questions') return { status: 'missing', current: false, complete: false, answeredCount: 0, unansweredCount: 0, questions: [] };
        throw new Error(`Unexpected query: ${args.join(' ')}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const product = items.find(item => item.label === 'Product definition');
    const next = items.find(item => item.label === 'Next: Define high-level technical direction');
    assert.equal(next.command.command, 'cis.technicalIntentQuestions');
    assert.equal(product.children.at(-1).label, '3. Technical direction questionnaire');
    assert.equal(product.children.at(-1).command.command, 'cis.technicalIntentQuestions');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('journey map separates product, technical, experience, and feature delivery definition', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-journey-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Active\n---\n# BRD\n');
    let questionState = { status: 'status', current: true, complete: false, answeredCount: 4, unansweredCount: 12 };
    const cli = { version: async () => ({ raw: '0.3.0', compatible: true }), query: async args => {
      if (args.join(' ') === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
      if (args.join(' ') === 'change list') return { changes: [] };
      if (args.join(' ') === 'agent providers') return { providers: [] };
      if (args.join(' ') === 'change list') return { changes: [] };
      if (args[0] === 'brd' && args[1] === 'status') return { validation: { valid: true, current: true, effectiveStatus: 'Active' } };
      if (args[0] === 'technical-intent' && args[1] === 'questions') return questionState;
      if (args[0] === 'technical-intent' && args[1] === 'status') return { status: 'missing' };
      if (args[0] === 'brd' && args[1] === 'backlog') return { status: 'missing' };
      return {};
    } };
    const provider = new extension.CisViewProvider(vscode, 'journey', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    assert.deepEqual(items.map(item => item.label), ['Journey map', 'Product definition', 'Technical definition', 'Experience definition', 'Feature delivery loop']);
    assert.match(items[2].children[0].description, /4\/16 resolved/u);
    assert.equal(items[2].children[0].command.command, 'cis.technicalIntentQuestions');
    assert.equal(items[3].children.length, 4);
    assert.equal(items[4].children.length, 5);
    questionState = { status: 'status', current: true, complete: true, answeredCount: 16, unansweredCount: 0 };
    const completedItems = await provider.getChildren();
    assert.equal(completedItems[2].children[0].description, 'Complete');
    assert.equal(completedItems[2].children[0].command.command, 'cis.technicalIntentQuestions');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('journey map does not query or expose a stale backlog before solution design is active', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-gated-journey-'));
  try {
    const docs = path.join(root, 'docs');
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(docs, 'specs'), { recursive: true });
    fs.mkdirSync(path.join(docs, 'plans'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(docs, 'specs', 'business-requirements.md'), '---\nstatus: Active\n---\n# BRD\n');
    fs.writeFileSync(path.join(docs, 'specs', 'technical-intent-spec.md'),
      '---\nstatus: Active\n---\n# Technical intent\n<!-- cis:technical-intent-questionnaire-evidence:start -->\n');
    fs.writeFileSync(path.join(docs, 'plans', 'high-level-backlog.md'), '---\nstatus: Review Required\n---\n# Superseded backlog\n');
    const calls = [];
    const cli = { query: async args => {
      calls.push(args.join(' '));
      if (args.join(' ') === 'change list') return { changes: [] };
      if (args[0] === 'brd' && args[1] === 'status') return { status: 'Active', valid: true, current: true };
      if (args[0] === 'technical-intent' && args[1] === 'questions')
        return { status: 'complete', complete: true, current: true, answeredCount: 16, unansweredCount: 0 };
      if (args[0] === 'technical-intent' && args[1] === 'status') return { status: 'Active', valid: true, current: true };
      if (args[0] === 'solution-design' && args[1] === 'status')
        return { status: 'missing', valid: false, current: false, validation: { effectiveStatus: 'Missing' } };
      if (args[0] === 'brd' && args[1] === 'backlog') throw new Error('A gated backlog must not be queried.');
      throw new Error(`Unexpected journey query: ${args.join(' ')}`);
    } };

    const journey = new extension.CisViewProvider(vscode, 'journey', { root: () => root, needsSelection: () => false }, cli);
    const journeyItems = await journey.getChildren();
    const product = journeyItems.find(item => item.label === 'Product definition');
    const technical = journeyItems.find(item => item.label === 'Technical definition');
    const backlog = product.children.find(item => item.label === 'High-level backlog');
    assert.equal(backlog.description, 'Waiting For Solution Design');
    assert.equal(backlog.resourceUri, undefined);
    assert.equal(technical.children.find(item => item.label === 'Overall solution design').description, 'Missing');
    assert.equal(calls.some(call => call.startsWith('brd backlog status ')), false);

    const workspaceCli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        return cli.query(args);
      },
    };
    const workspace = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, workspaceCli);
    const workspaceItems = await workspace.getChildren();
    assert.ok(workspaceItems.some(item => item.label === 'Next: Generate overall solution design'));
    assert.equal(workspaceItems.some(item => item.label === 'Next: Validate high-level backlog'), false);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('incomplete BRD offers bounded agent drafting from reference as the next action', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Review Required\n---\n# BRD\nTODO\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        if (args.join(' ') === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (args.join(' ') === 'change list') return { changes: [] };
        if (args.join(' ') === 'agent providers') return { providers: [] };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: false, current: true, effectiveStatus: 'Review Required', errors: ['Executive summary contains TODO'], warnings: [],
        } };
        throw new Error(`Unexpected query: ${args.join(' ')}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Draft business requirements from reference');
    assert.equal(next.command.command, 'cis.brdAgentDraft');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('BRD open questions become the next guided product action', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Review Required\n---\n# BRD\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        if (args.join(' ') === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (args.join(' ') === 'change list') return { changes: [] };
        if (args.join(' ') === 'agent providers') return { providers: [] };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: false, current: true, effectiveStatus: 'Review Required', errors: ['BRD-Q-001 is unanswered'], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return { questions: [
          { id: 'BRD-Q-001', question: 'Who owns the outcome?', status: 'Unanswered' },
        ] };
        throw new Error(`Unexpected query: ${args.join(' ')}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Answer open BRD questions');
    assert.equal(next.command.command, 'cis.brdAnswerQuestions');
    assert.match(next.description, /1 stakeholder answer is required/u);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('BRD question answers remain covered by the latest independent review', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'),
      '---\nstatus: Review Required\n---\n# BRD\n\n| BRD-Q-001 | Who owns the outcome? | Andrew | Andrew | 2026-09-01T07:45:00Z |\n'
      + '| BRD-Q-002 | What is the launch phase? | Unanswered | - | - |\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          { runId: 'RUN-REVIEW', taskId: 'BRD-REVIEW', taskDigest: '0'.repeat(64), provider: 'claude', status: 'Succeeded', completedAtUtc: '2026-09-01T07:33:08Z' },
          { runId: 'RUN-DRAFT', taskId: 'BRD-DRAFT', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-09-01T07:20:00Z' },
        ] };
        if (key === 'agent show RUN-REVIEW --summary') return { run: { result: { review: { findings: [] } } } };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: false, current: true, effectiveStatus: 'Review Required', errors: ['BRD-Q-002 is unanswered'], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return { questions: [
          { id: 'BRD-Q-001', question: 'Who owns the outcome?', status: 'Answered' },
          { id: 'BRD-Q-002', question: 'What is the launch phase?', status: 'Unanswered' },
        ] };
        if (args[0] === 'brd' && args[1] === 'review' && args[2] === 'freshness') {
          return { status: 'question-answers-only', compatible: true, reviewRunId: 'RUN-REVIEW' };
        }
        throw new Error(`Unexpected query: ${key}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Answer open BRD questions');
    assert.equal(next.command.command, 'cis.brdAnswerQuestions');
    const reviewStage = items.find(item => item.label === 'Product definition').children
      .find(item => item.label === '2a. Independent BRD review');
    assert.match(reviewStage.description, /stakeholder answers recorded afterward/u);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('fully answered BRD questions route to bounded incorporation before approval', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'),
      '---\nstatus: Review Required\n---\n# BRD\n\n| BRD-Q-001 | Who owns the outcome? | Andrew | Andrew | 2026-09-01T07:45:00Z |\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          { runId: 'RUN-REVIEW', taskId: 'BRD-REVIEW', provider: 'claude', status: 'Succeeded', completedAtUtc: '2026-09-01T07:33:08Z' },
          { runId: 'RUN-DRAFT', taskId: 'BRD-DRAFT', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-09-01T07:20:00Z' },
        ] };
        if (key === 'agent show RUN-REVIEW --summary') return { run: { result: { review: { findings: [] } } } };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: true, current: true, effectiveStatus: 'Ready for Approval', documentStatus: 'Review Required', errors: [], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return {
          answerDigest: 'sha256:answered', questions: [
            { id: 'BRD-Q-001', question: 'Who owns the outcome?', status: 'Answered' },
          ],
        };
        if (args[0] === 'brd' && args[1] === 'review' && args[2] === 'freshness') {
          return { status: 'question-answers-only', compatible: true, reviewRunId: 'RUN-REVIEW' };
        }
        throw new Error(`Unexpected query: ${key}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Update BRD from answered questions');
    assert.equal(next.command.command, 'cis.brdAgentIncorporateQuestions');
    assert.equal(items.some(item => item.label === 'Next: Approve business requirements'), false);
    const incorporation = items.find(item => item.label === 'Product definition').children
      .find(item => item.label === '2c. Answer incorporation');
    assert.match(incorporation.description, /BRD content update required/u);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('a BRD updated from question answers requires a fresh independent review', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'),
      '---\nstatus: Review Required\n---\n# BRD\n\nUpdated owner outcome.\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          { runId: 'RUN-QUESTIONS', taskId: 'BRD-QUESTION-REVISION', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-09-01T08:00:00Z' },
          { runId: 'RUN-REVIEW', taskId: 'BRD-REVIEW', provider: 'claude', status: 'Succeeded', completedAtUtc: '2026-09-01T07:33:08Z' },
          { runId: 'RUN-DRAFT', taskId: 'BRD-DRAFT', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-09-01T07:20:00Z' },
        ] };
        if (key === 'agent show RUN-QUESTIONS --summary') return { run: { eventKinds: ['apply'], result: { questionRevision: {
          answerDigest: 'sha256:answered', incorporatedQuestionIds: ['BRD-Q-001'],
        } } } };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: true, current: true, effectiveStatus: 'Ready for Approval', documentStatus: 'Review Required', errors: [], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return {
          answerDigest: 'sha256:answered', questions: [
            { id: 'BRD-Q-001', question: 'Who owns the outcome?', status: 'Answered' },
          ],
        };
        if (args[0] === 'brd' && args[1] === 'review' && args[2] === 'freshness') {
          return { status: 'stale', compatible: false, reviewRunId: 'RUN-REVIEW' };
        }
        throw new Error(`Unexpected query: ${key}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Review BRD with independent agent');
    assert.equal(next.command.command, 'cis.brdAgentReview');
    assert.equal(items.some(item => item.label === 'Next: Approve business requirements'), false);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('review remediation preserves prior answered-question incorporation provenance', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'),
      '---\nstatus: Review Required\n---\n# BRD\n\nUpdated and remediated owner outcome.\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          { runId: 'RUN-CLOSURE', taskId: 'BRD-REVIEW', provider: 'claude', status: 'Succeeded', completedAtUtc: '2026-09-01T09:00:00Z' },
          { runId: 'RUN-REMEDIATION', taskId: 'BRD-REVISION', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-09-01T08:00:00Z' },
          { runId: 'RUN-QUESTIONS', taskId: 'BRD-QUESTION-REVISION', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-09-01T07:00:00Z' },
          { runId: 'RUN-DRAFT', taskId: 'BRD-DRAFT', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-09-01T06:00:00Z' },
        ] };
        if (key === 'agent show RUN-QUESTIONS --summary') return { run: { eventKinds: ['apply'], result: { questionRevision: {
          answerDigest: 'sha256:answered', incorporatedQuestionIds: ['BRD-Q-001'],
        } } } };
        if (key === 'agent show RUN-CLOSURE --summary') return { run: { result: { review: { recommendation: 'ready', findings: [] } } } };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: true, current: true, effectiveStatus: 'Ready for Approval', documentStatus: 'Review Required', errors: [], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return {
          answerDigest: 'sha256:answered', questions: [
            { id: 'BRD-Q-001', question: 'Who owns the outcome?', status: 'Answered' },
          ],
        };
        if (args[0] === 'brd' && args[1] === 'review' && args[2] === 'freshness') {
          return { status: 'current', compatible: true, reviewRunId: 'RUN-CLOSURE' };
        }
        throw new Error(`Unexpected query: ${key}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Approve business requirements');
    assert.equal(next.command.command, 'cis.brdApprove');
    assert.equal(items.some(item => item.label === 'Next: Update BRD from answered questions'), false);
    const incorporation = items.find(item => item.label === 'Product definition').children
      .find(item => item.label === '2c. Answer incorporation');
    assert.match(incorporation.description, /incorporated and independently reviewed/u);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('an agent-authored BRD is routed to a different-provider review before question resolution', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Review Required\n---\n# BRD\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          { runId: 'RUN-DRAFT', taskId: 'BRD-DRAFT', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-08-30T10:00:00Z' },
        ] };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: false, current: true, effectiveStatus: 'Review Required', errors: ['BRD-Q-001 is unanswered'], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return { questions: [
          { id: 'BRD-Q-001', question: 'Who owns the outcome?', status: 'Unanswered' },
        ] };
        throw new Error(`Unexpected query: ${key}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Review BRD with independent agent');
    assert.equal(next.command.command, 'cis.brdAgentReview');
    assert.match(next.description, /different from codex/u);
    const product = items.find(item => item.label === 'Product definition');
    assert.ok(product.children.some(item => item.label === '2a. Independent BRD review'));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('independent BRD findings route through human dispositions before agent revision', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Review Required\n---\n# BRD\n');
    let dispositionStatus = 'review-required';
    let retainedUnappliedRevision = false;
    const brdDigest = crypto.createHash('sha256').update(fs.readFileSync(
      path.join(root, 'docs', 'specs', 'business-requirements.md'))).digest('hex');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          ...(retainedUnappliedRevision ? [
            { runId: 'RUN-REVISION', taskId: 'BRD-REVISION', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-08-30T10:10:00Z' },
          ] : []),
          { runId: 'RUN-REVIEW', taskId: 'BRD-REVIEW', taskDigest: brdDigest, provider: 'claude', status: 'Succeeded', completedAtUtc: '2026-08-30T10:05:00Z' },
          { runId: 'RUN-DRAFT', taskId: 'BRD-DRAFT', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-08-30T10:00:00Z' },
        ] };
        if (key === 'agent show RUN-REVIEW --summary') return { run: { result: { review: { findings: [
          { id: 'BRD-REV-001', recommendation: 'Add a measurable outcome.' },
        ] } } } };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: false, current: true, effectiveStatus: 'Review Required', errors: ['BRD-Q-001 is unanswered'], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return { questions: [
          { id: 'BRD-Q-001', question: 'Who owns the outcome?', status: 'Unanswered' },
        ] };
        if (args[0] === 'brd' && args[1] === 'review') return {
          status: dispositionStatus, pendingCount: dispositionStatus === 'review-required' ? 1 : 0,
          acceptedCount: dispositionStatus === 'approved' ? 1 : 0, rejectedCount: 0,
          canonicalPath: path.join(root, 'docs', 'reviews', 'brd', 'RUN-REVIEW.md'),
        };
        throw new Error(`Unexpected query: ${key}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    let items = await provider.getChildren();
    let next = items.find(item => item.label === 'Next: Review recommendations');
    assert.equal(next.command.command, 'cis.brdReviewRecommendations');
    assert.deepEqual(next.command.arguments, ['RUN-REVIEW']);
    assert.ok(items.find(item => item.label === 'Product definition').children.some(item => item.label === '2b. Review recommendations'));

    dispositionStatus = 'approved';
    items = await provider.getChildren();
    next = items.find(item => item.label === 'Next: Apply approved BRD recommendations with agent');
    assert.equal(next.command.command, 'cis.brdAgentRevise');
    assert.deepEqual(next.command.arguments, ['RUN-REVIEW']);

    retainedUnappliedRevision = true;
    items = await provider.getChildren();
    next = items.find(item => item.label === 'Next: Apply approved BRD recommendations with agent');
    assert.equal(next.command.command, 'cis.brdAgentRevise');
    assert.deepEqual(next.command.arguments, ['RUN-REVIEW']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('a digest-stale BRD review is superseded and routes to a fresh independent review', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-product-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(root, 'docs', 'specs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(root, 'docs', 'specs', 'business-requirements.md'), '---\nstatus: Review Required\n---\n# BRD\ncurrent evidence\n');
    const cli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' ');
        if (key === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
        if (key === 'change list') return { changes: [] };
        if (key === 'agent providers') return { providers: [] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          { runId: 'RUN-REVIEW', taskId: 'BRD-REVIEW', taskDigest: '0'.repeat(64), provider: 'claude', status: 'Succeeded', completedAtUtc: '2026-08-30T10:05:00Z' },
          { runId: 'RUN-DRAFT', taskId: 'BRD-DRAFT', provider: 'codex', status: 'Succeeded', completedAtUtc: '2026-08-30T10:00:00Z' },
        ] };
        if (args[0] === 'brd' && args[1] === 'status') return { validation: {
          valid: false, current: true, effectiveStatus: 'Review Required', errors: ['Review required'], warnings: [],
        } };
        if (args[0] === 'brd' && args[1] === 'questions') return { questions: [] };
        throw new Error(`Unexpected query: ${key}`);
      },
    };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const items = await provider.getChildren();
    const next = items.find(item => item.label === 'Next: Review BRD with independent agent');
    assert.equal(next.command.command, 'cis.brdAgentReview');
    const reviewStage = items.find(item => item.label === 'Product definition').children
      .find(item => item.label === '2a. Independent BRD review');
    assert.match(reviewStage.description, /Superseded/u);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('journey projection routes every technical-intent, solution-design, backlog, and feature milestone', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-technical-journey-'));
  const docs = path.join(root, 'docs');
  const technical = path.join(docs, 'specs', 'technical-intent-spec.md');
  const overallDesign = path.join(docs, 'architecture', 'overall-solution-design.md');
  const componentSheet = path.join(docs, 'references', 'component-sheet.md');
  const uiDirection = path.join(docs, 'design', 'ui-direction.md');
  const backlog = path.join(docs, 'plans', 'high-level-backlog.md');
  const feature = path.join(docs, 'specs', 'features', 'HLT-FR-001.md');
  let phase = 'unmanaged-technical';
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.mkdirSync(path.join(docs, 'specs'), { recursive: true });
    fs.mkdirSync(path.dirname(backlog), { recursive: true });
    fs.mkdirSync(path.dirname(feature), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: product\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: product\n');
    fs.writeFileSync(path.join(docs, 'specs', 'business-requirements.md'), '---\nstatus: Active\n---\n# BRD\n');
    const cli = { version: async () => ({ raw: '0.3.0', compatible: true }), query: async args => {
      if (args.join(' ') === 'repo doctor') return { warningCount: 0, errorCount: 0, ollama: {}, findings: [] };
      if (args.join(' ') === 'change list') return { changes: [] };
      if (args.join(' ') === 'agent providers') return { providers: [] };
      if (args[0] === 'brd' && args[1] === 'status') return { status: 'Active', valid: true, current: true };
      if (args[0] === 'technical-intent' && args[1] === 'questions')
        return { status: 'complete', complete: true, current: true, answeredCount: 16, unansweredCount: 0 };
      if (args[0] === 'technical-intent' && args[1] === 'status')
        return phase === 'technical-review' ? { status: 'ReadyForApproval', valid: true, current: true }
          : { status: 'Active', valid: true, current: true };
      if (args[0] === 'solution-design' && args[1] === 'status')
        return phase === 'solution-review'
          ? { status: 'ReadyForApproval', valid: true, current: true, validation: { componentSheetStatus: 'Review Required' } }
          : { status: 'Active', valid: true, current: true, validation: { componentSheetStatus: 'Active' } };
      if (args[0] === 'ui-direction' && args[1] === 'questions')
        return phase === 'ui-questions'
          ? { status: 'status', complete: false, current: true, answeredCount: 4, unansweredCount: 8 }
          : { status: 'complete', complete: true, current: true, answeredCount: 12, unansweredCount: 0 };
      if (args[0] === 'ui-direction' && args[1] === 'status')
        return phase === 'ui-review' ? { status: 'ReadyForApproval', valid: true, current: true }
          : { status: 'Active', valid: true, current: true };
      if (args[0] === 'brd' && args[1] === 'backlog' && args[2] === 'status') {
        if (phase === 'backlog-review') return { status: 'ReadyForApproval', valid: true, current: true, items: [] };
        const featureSpecification = ['feature-template', 'feature-review', 'feature-active', 'all-active'].includes(phase)
          ? 'docs/specs/features/HLT-FR-001.md' : 'not-created';
        const items = [{ id: 'HLT-FR-001', outcome: 'First feature', dependsOn: [], featureSpecification }];
        if (phase === 'feature-active') items.push({ id: 'HLT-FR-002', outcome: 'Next feature', dependsOn: ['HLT-FR-001'], featureSpecification: 'not-created' });
        return { status: 'Active', valid: true, current: true, items };
      }
      if (args[0] === 'brd' && args[1] === 'feature')
        return phase === 'feature-template' ? { status: 'Draft', valid: false, current: true, errors: ['Feature specification contains TODO placeholders.'], relativePath: 'docs/specs/features/HLT-FR-001.md' }
          : phase === 'feature-review' ? { status: 'ReadyForApproval', valid: true, current: true, relativePath: 'docs/specs/features/HLT-FR-001.md' }
          : { status: 'Active', valid: true, current: true, relativePath: 'docs/specs/features/HLT-FR-001.md' };
      throw new Error(`Unexpected journey query: ${args.join(' ')}`);
    } };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const nextCommands = async () => (await provider.getChildren()).map(item => item.command?.command).filter(Boolean);

    assert.ok((await nextCommands()).includes('cis.technicalIntentInit'));
    fs.writeFileSync(technical, '<!-- cis:technical-intent-questionnaire-evidence:start -->\n---\nstatus: Review Required\n---\n');
    phase = 'technical-review';
    assert.ok((await nextCommands()).includes('cis.technicalIntentApprove'));
    phase = 'solution-missing';
    assert.ok((await nextCommands()).includes('cis.solutionDesignInit'));
    fs.mkdirSync(path.dirname(overallDesign), { recursive: true });
    fs.mkdirSync(path.dirname(componentSheet), { recursive: true });
    fs.writeFileSync(overallDesign, '---\nstatus: Review Required\n---\n# Overall solution design\n');
    fs.writeFileSync(componentSheet, '---\nstatus: Review Required\n---\n# Component sheet\n');
    const projectedPaths = productPaths(root, extension.repositoryMetadata(root, 'docs/cis'));
    assert.equal(projectedPaths.overallSolutionDesign, overallDesign);
    assert.equal(projectedPaths.componentSheet, componentSheet);
    assert.ok(fs.existsSync(projectedPaths.overallSolutionDesign));
    assert.ok(fs.existsSync(projectedPaths.componentSheet));
    phase = 'solution-review';
    const solutionReviewCommands = await nextCommands();
    assert.ok(solutionReviewCommands.includes('cis.solutionDesignApprove'), JSON.stringify(solutionReviewCommands));
    phase = 'ui-questions';
    assert.ok((await nextCommands()).includes('cis.uiDirectionQuestions'));
    phase = 'ui-missing';
    assert.ok((await nextCommands()).includes('cis.uiDirectionInit'));
    fs.mkdirSync(path.dirname(uiDirection), { recursive: true });
    fs.writeFileSync(uiDirection, '---\nstatus: Review Required\n---\n# High-level UI direction\n');
    phase = 'ui-review';
    assert.ok((await nextCommands()).includes('cis.uiDirectionApprove'));
    phase = 'backlog-missing';
    assert.ok((await nextCommands()).includes('cis.backlogBuild'));
    fs.writeFileSync(backlog, '---\nstatus: Review Required\n---\n');
    phase = 'backlog-review';
    assert.ok((await nextCommands()).includes('cis.backlogApprove'));
    fs.writeFileSync(feature, '---\nstatus: Review Required\n---\n');
    phase = 'feature-template';
    assert.ok((await nextCommands()).includes('cis.featureAgentDraft'));
    phase = 'feature-review';
    assert.ok((await nextCommands()).includes('cis.featureApprove'));
    phase = 'feature-active';
    assert.ok((await nextCommands()).includes('cis.featureStart'));
    phase = 'all-active';
    assert.ok(!(await nextCommands()).some(command => command === 'cis.featureStart' || command === 'cis.featureApprove'));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('product validation refreshes graph and approval preserves explicit human authority', async () => {
  const originalWindow = vscode.window;
  const foreground = []; const queries = []; const shown = [];
  const cli = {
    runForeground: async (title, args, options) => { foreground.push({ title, args, options }); },
    query: async (args, options) => {
      queries.push({ args, options });
      return { validation: { valid: true, current: true, effectiveStatus: 'Ready for Approval', errors: [], warnings: [] } };
    },
  };
  let refreshes = 0;
  configuration.actorIdentity = 'Andrew Spiteri';
  vscode.window = {
    showInputBox: async () => 'Requirements accepted for the product baseline',
    showWarningMessage: async () => 'Approve',
  };
  try {
    await extension.validateProductDocument(cli, async () => ++refreshes, (title, result) => shown.push({ title, result }),
      'Business requirements validation', ['brd', 'validate'], 'C:\repo');
    await extension.approveProductDocument(cli, async () => ++refreshes, () => {}, {
      label: 'business requirements', validate: ['brd', 'validate'], approve: ['brd', 'approve'],
    }, 'C:\repo');
    assert.deepEqual(foreground[0].args, ['graph', 'build', '--workspace', 'C:\repo']);
    assert.deepEqual(foreground[1].args, ['graph', 'build', '--workspace', 'C:\repo']);
    assert.deepEqual(foreground[2].args, ['brd', 'approve', '--workspace', 'C:\repo', '--reviewer', 'Andrew Spiteri',
      '--reason', 'Requirements accepted for the product baseline']);
    assert.deepEqual(foreground[3].args, ['graph', 'build', '--workspace', 'C:\repo']);
    assert.ok(foreground.every(call => call.options.repository === false));
    assert.ok(queries.every(call => call.options.repository === false));
    assert.equal(shown[0].title, 'Business requirements validation');
    assert.equal(refreshes, 2);
  } finally { configuration.actorIdentity = ''; vscode.window = originalWindow; }
});

test('validation presentation omits verbose generated collections', () => {
  const compact = extension.compactValidationResult({
    status: 'validated', items: [{ id: 'HLT-FR-001' }, { id: 'HLT-FR-002' }],
    components: [{ id: 'TI-MOD-ONE' }], validation: { current: false },
  });
  assert.equal(compact.itemCount, 2);
  assert.equal(compact.componentCount, 1);
  assert.equal(Object.hasOwn(compact, 'items'), false);
  assert.equal(Object.hasOwn(compact, 'components'), false);
  assert.equal(extension.compactValidationResult(undefined), undefined);
});

test('stale backlog actions redirect to the required solution-design stage', async () => {
  const executed = []; let refreshed = 0;
  const vscodeApi = {
    window: { showWarningMessage: async () => 'Generate overall solution design' },
    commands: { executeCommand: async command => executed.push(command) },
  };
  const cli = { query: async () => ({
    status: 'missing', validation: { valid: false, current: false, effectiveStatus: 'Missing', errors: ['Both canonical artifacts are required.'] },
  }) };

  const ready = await extension.ensureSolutionDesignReady(vscodeApi, cli, async () => { refreshed += 1; }, 'C:\\workspace');

  assert.equal(ready, false);
  assert.equal(refreshed, 1);
  assert.deepEqual(executed, ['cis.solutionDesignInit']);
  const active = await extension.ensureSolutionDesignReady(vscodeApi,
    { query: async () => ({ status: 'Active', valid: true, current: true }) }, async () => {}, 'C:\\workspace');
  assert.equal(active, true);
});

test('backlog actions redirect to high-level UI direction after solution design is active', async () => {
  const executed = []; let refreshed = 0;
  const vscodeApi = {
    window: { showWarningMessage: async () => 'Define high-level UI direction' },
    commands: { executeCommand: async command => executed.push(command) },
  };
  const cli = { query: async args => args[0] === 'solution-design'
    ? { status: 'Active', valid: true, current: true }
    : { status: 'missing', validation: { valid: false, current: false, effectiveStatus: 'Missing', errors: ['UI direction is missing.'] } } };
  const ready = await extension.ensureUiDirectionReady(vscodeApi, cli, async () => { refreshed += 1; }, 'C:\\workspace');
  assert.equal(ready, false);
  assert.equal(refreshed, 1);
  assert.deepEqual(executed, ['cis.uiDirectionQuestions']);
});

// Trace: TC-VSC-007-001, TC-VSC-017-001, TC-VSC-023-001.
test('product journey document and feature projections distinguish missing, present, and linked records', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-product-journey-'));
  try {
    const plain = path.join(root, 'plain.md');
    const governed = path.join(root, 'governed.md');
    fs.writeFileSync(plain, '# Present\n');
    fs.writeFileSync(governed, '---\nstatus: Ready for Approval\n---\n# Governed\n');
    assert.equal(documentStatus(path.join(root, 'missing.md')), 'Missing');
    assert.equal(documentStatus(plain), 'Present');
    assert.equal(documentStatus(governed), 'Ready for Approval');
    assert.deepEqual(linkedFeatureItems(undefined), []);
    assert.deepEqual(linkedFeatureItems([
      { id: 'HLT-1', featureSpecification: 'not-created' },
      { id: 'HLT-2', featureSpecification: 'docs/specs/features/HLT-2.md' },
    ]).map(item => item.id), ['HLT-2']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('TC-VSC-007-001 TC-VSC-017-001 TC-VSC-023-001 path containment rejects escape, dependency, secret, and unrestricted local state', () => {
  const root = path.resolve(os.tmpdir(), 'cis-vscode-root');
  assert.equal(resolveWithin(root, '../../outside'), undefined);
  assert.equal(resolveWithin(root, 'node_modules/pkg/index.js'), undefined);
  assert.equal(resolveWithin(root, '.env.local'), undefined);
  assert.equal(resolveWithin(root, '.cis/local/context/graph.db'), undefined);
  assert.equal(resolveWithin(root, '.cis/local/agents/runs/RUN-1/manifest.json', { allowLocalEvidence: true }),
    path.join(root, '.cis/local/agents/runs/RUN-1/manifest.json'));
});

test('markdown routing is deterministic, bounded, and only returns safe Markdown', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.writeFileSync(path.join(root, 'b.md'), '# b');
    fs.writeFileSync(path.join(root, 'a.md'), '# a');
    fs.writeFileSync(path.join(root, 'ignore.txt'), 'x');
    fs.mkdirSync(path.join(root, 'node_modules'));
    fs.writeFileSync(path.join(root, 'node_modules', 'bad.md'), '# bad');
    assert.deepEqual(extension.markdownFiles(vscode, root, true).map(item => item.label), ['a', 'b']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('Evidence view groups canonical Markdown without querying or mutating the repository', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-evidence-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: evidence\ndocumentation_root: docs\n');
    for (const folder of ['specs', 'references', 'decisions', 'manual', 'changes']) {
      fs.mkdirSync(path.join(root, 'docs', folder), { recursive: true });
      fs.writeFileSync(path.join(root, 'docs', folder, `${folder}.md`), `# ${folder}\n`);
    }
    const provider = new extension.CisViewProvider(vscode, 'evidence', { root: () => root, needsSelection: () => false }, {});
    const items = await provider.getChildren();
    assert.equal(items[0].command.command, 'cis.contextSearch');
    assert.deepEqual(items.slice(1).map(item => item.label), ['Specifications', 'References', 'Decisions', 'Manual', 'Changes']);
    assert.ok(items.slice(1).every(item => item.children.length === 1));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('diagnostics redact likely credentials and remain bounded', () => {
  const value = redact('authorization=Bearer abcdefghijklmnop token=gho_123456789abcdef password=hunter2');
  assert.doesNotMatch(value, /abcdefgh|gho_|hunter2/u);
  assert.match(value, /\[REDACTED\]/u);
  assert.ok(bound('x'.repeat(5000), 100).length < 150);
  assert.throws(() => validateExecutable('cis\n--danger'), /control characters/u);
});

// Trace: TC-VSC-010-001, TC-VSC-015-001, TC-VSC-017-001.
test('TC-VSC-010-001 TC-VSC-015-001 TC-VSC-017-001 webview content is encoded, nonce-restricted, and message commands are allowlisted', () => {
  const html = renderHtml({ cspSource: 'vscode-webview:' }, '<unsafe>', { value: '<script>bad()</script>' },
    [{ command: 'cancel', label: 'Cancel', value: 'RUN-1' }], 'fixed-nonce');
  assert.doesNotMatch(html, /<unsafe>|<script>bad/u);
  assert.match(html, /default-src 'none'/u);
  assert.match(html, /script-src 'nonce-fixed-nonce'/u);
  assert.equal(isValidWebviewMessage({ command: 'cancel', value: 'RUN-1' }, new Set(['cancel'])), true);
  assert.equal(isValidWebviewMessage({ command: 'execute-anything' }, new Set(['cancel'])), false);
});

test('BRD recommendation review presents the complete consolidated list with individual and batch controls', () => {
  const status = {
    status: 'review-required', canonicalPath: 'docs/reviews/brd/RUN-1.md', disposition: { findings: [
      {
        id: 'BRD-REV-001', severity: 'major', category: 'Traceability', location: 'Source assessment',
        observation: 'The evidence tables contradict <each other>.',
        recommendation: 'Reconcile the source assessment and traceability tables.', decision: 'pending',
      },
      {
        id: 'BRD-REV-002', severity: 'minor', category: 'Testability', location: 'Outcomes',
        observation: 'The completion measure has no deadline.',
        recommendation: 'Add the accepted delivery deadline.', decision: 'pending',
      },
    ] },
  };
  const html = renderRecommendationReviewHtml({ cspSource: 'vscode-webview:' }, 'RUN-1', status, 'review-nonce');
  assert.match(html, /Overall recommendation list/u);
  assert.match(html, /What the reviewer found/u);
  assert.match(html, /The evidence tables contradict &lt;each other&gt;\./u);
  assert.match(html, /Reconcile the source assessment and traceability tables\./u);
  assert.match(html, /The completion measure has no deadline\./u);
  assert.match(html, /Add the accepted delivery deadline\./u);
  assert.ok(html.indexOf('Reconcile the source assessment') < html.indexOf('Approve as is'));
  assert.match(html, /data-command="accept" data-value="BRD-REV-001"/u);
  assert.match(html, /data-command="accept" data-value="BRD-REV-002"/u);
  assert.match(html, /data-command="modify" data-value="BRD-REV-001"/u);
  assert.match(html, /data-command="accept-all"/u);
  assert.match(html, /Approve all pending as is/u);
  assert.match(html, /Modify and approve/u);
  assert.doesNotMatch(html, /data-command="reject"/u);
  assert.match(html, /aria-label="Review progress"/u);
  assert.match(html, /default-src 'none'/u);
  assert.match(html, /script-src 'nonce-review-nonce'/u);
});

test('BRD open-question page presents context, editable suggestions, and explicit answer controls', () => {
  const html = renderBrdQuestionsHtml({ cspSource: 'vscode-webview:' }, {
    suggestionStatus: 'current', suggestionProvider: 'ollama', suggestionModel: 'small',
    questions: [
      {
        id: 'BRD-Q-001', status: 'Unanswered', question: 'Who owns the <outcome>?',
        suggestedAnswer: 'The business sponsor owns the outcome.', suggestionConfidence: 'high',
        suggestionReason: 'The stakeholder section assigns accountability.',
        suggestionContextIds: ['BRD-Q-001-CTX-1'], context: [
          { id: 'BRD-Q-001-CTX-1', section: 'Stakeholders and actors', excerpt: 'The business sponsor is accountable.' },
        ],
      },
      {
        id: 'BRD-Q-002', status: 'Unanswered', question: 'What launch date is accepted?',
        suggestionConfidence: 'insufficient', suggestionReason: 'No accepted date is stated.', context: [],
      },
    ],
  }, 'question-nonce');

  assert.match(html, /Who owns the &lt;outcome&gt;\?/u);
  assert.match(html, /Relevant BRD context \(1\)/u);
  assert.match(html, /Stakeholders and actors/u);
  assert.match(html, /The business sponsor owns the outcome\./u);
  assert.match(html, /data-command="accept-suggestion" data-value="BRD-Q-001"/u);
  assert.match(html, /data-command="save-answer" data-value="BRD-Q-001"/u);
  assert.match(html, /Save edited answer/u);
  assert.match(html, /No supported answer is currently suggested/u);
  assert.doesNotMatch(html, /data-command="accept-suggestion" data-value="BRD-Q-002"/u);
  assert.match(html, /maxlength="16384"/u);
  assert.match(html, /Regenerate advisory suggestions/u);
  assert.match(html, /default-src 'none'/u);
  assert.match(html, /script-src 'nonce-question-nonce'/u);
});

test('technical-direction page presents all choices, editable starting directions, provenance, and progress', () => {
  const html = renderTechnicalIntentQuestionsHtml({ cspSource: 'vscode-webview:' }, {
    current: true, complete: false, answeredCount: 2, unansweredCount: 1, questions: [
      { id: 'TI-Q-001', area: 'Product surfaces', question: 'Which surfaces?', why: 'Defines entry points.',
        commonOptions: ['Public web', 'Mobile'], suggestedAnswer: 'Public web only.', status: 'Unanswered' },
      { id: 'TI-Q-004', area: 'Architecture style', question: 'Which architecture?', why: 'Defines boundaries.',
        commonOptions: ['Modular monolith', 'Microservices'], suggestedAnswer: 'Modular monolith.', status: 'Answered',
        answer: 'Modular monolith with explicit modules.', answeredBy: 'Andrew', answeredAtUtc: '2026-09-01T10:00:00Z' },
      { id: 'TI-Q-006', area: 'Primary data store', question: 'Which database?', why: 'Defines persistence.',
        commonOptions: ['SQLite', 'PostgreSQL'], suggestedAnswer: 'Reuse the detected store.', status: 'Derived',
        answer: 'Preserve the detected SQLite persistence boundary.', confidence: 'high',
        evidence: ['api:src/Product.Api/Product.Api.csproj'] },
    ],
  }, 'technical-nonce');
  assert.match(html, /High-level technical direction/u);
  assert.match(html, /Product surfaces/u);
  assert.match(html, /Modular monolith with explicit modules\./u);
  assert.match(html, /Recorded by Andrew/u);
  assert.match(html, /Derived from the existing project/u);
  assert.match(html, /api:src\/Product\.Api\/Product\.Api\.csproj/u);
  assert.doesNotMatch(html, /accept-suggestion/u);
  assert.match(html, /data-command="save-answer" data-value="TI-Q-001">Save direction/u);
  assert.match(html, /data-command="save-answer" data-value="TI-Q-004"/u);
  assert.match(html, /data-command="save-answer" data-value="TI-Q-006">Update direction/u);
  assert.equal((html.match(/data-command="save-answer"/gu) || []).length, 3);
  assert.match(html, /1 remaining/u);
  assert.match(html, /vscode\.setState\(\{scrollY:window\.scrollY,questionId:/u);
  assert.match(html, /vscode\.getState\(\)/u);
  assert.match(html, /window\.scrollTo\(0,/u);
  assert.match(html, /default-src 'none'/u);
  assert.match(html, /script-src 'nonce-technical-nonce'/u);
});

test('UI-direction page presents shared look-and-feel choices, source provenance, and editable answers', () => {
  const html = renderUiDirectionQuestionsHtml({ cspSource: 'vscode-webview:' }, {
    current: true, complete: false, answeredCount: 1, unansweredCount: 1, questions: [
      { id: 'UI-Q-003', area: 'Shell and navigation', question: 'Which shell?', why: 'Prevents per-feature shell drift.',
        commonOptions: ['Header only', 'Header and side navigation'], suggestedAnswer: 'Use the smallest shell.', status: 'Unanswered' },
      { id: 'UI-Q-007', area: 'Reusable component system', question: 'Which component system?', why: 'Enables reuse.',
        commonOptions: ['Existing component system'], suggestedAnswer: 'Preserve existing evidence.', status: 'Derived',
        answer: 'Preserve shadcn/ui and Tailwind CSS.', confidence: 'high', evidence: ['web:docs/references/ui-framework-profile.md'] },
    ],
  }, 'ui-direction-nonce');
  assert.match(html, /Define the high-level UI look and feel/u);
  assert.match(html, /Shell and navigation/u);
  assert.match(html, /Preserve shadcn\/ui and Tailwind CSS/u);
  assert.match(html, /Derived from the existing product/u);
  assert.match(html, /data-command="save-answer" data-value="UI-Q-003">Save direction/u);
  assert.match(html, /data-command="save-answer" data-value="UI-Q-007">Update direction/u);
  assert.match(html, /1 remaining/u);
  assert.match(html, /script-src 'nonce-ui-direction-nonce'/u);
});

test('high-level definition wizard renders all eight revisable pages and one consolidated activation', () => {
  const root = path.join(os.tmpdir(), 'cis-definition-wizard');
  const pages = [
    ['foundation', 'Project foundation'], ['business', 'Business definition'], ['technical', 'Technical direction'],
    ['architecture', 'Solution architecture and diagrams'], ['contracts', 'Contracts and dictionaries'],
    ['experience', 'Experience direction and UI preview'], ['delivery', 'Delivery map'], ['review', 'Review and activate'],
  ].map(([id, title], index) => ({ id, title, ordinal: index + 1, status: 'Ready for Approval', complete: true,
    current: true, primaryPath: `docs/cis/${id}.md`, artifactPaths: [`docs/cis/${id}.md`], issues: [] }));
  const model = {
    sessionId: 'DEF-1', authorityRepositoryId: 'sample', workspacePath: root, readyToActivate: true, pages,
    brdQuestions: { questions: [{ id: 'BRD-Q-1', status: 'Answered' }] },
    technicalQuestions: { questions: [{ id: 'TI-Q-1', area: 'Architecture', question: 'Which style?', why: 'Sets boundaries.',
      status: 'Answered', answer: 'Modular monolith.' }] },
    uiQuestions: { questions: [{ id: 'UI-Q-1', area: 'Typography', question: 'Which font?', why: 'Sets hierarchy.',
      status: 'Answered', answer: 'Inter.' }] },
    diagrams: [{ title: 'System context', sourceFormat: 'Mermaid', relativePath: 'docs/cis/architecture/high-level-architecture-diagrams.md' }],
    dictionaries: [{ title: 'API dictionary', applicable: true, entryCount: 2, relativePath: 'docs/cis/references/api-dictionary.md' },
      { title: 'Mobile route map', applicable: false, entryCount: 0, relativePath: 'docs/cis/references/mobile-route-map.md' }],
    preview: { svgRelativePath: 'docs/cis/design/ui-system-preview.svg', fontFamily: 'Inter', density: 'comfortable', radius: '6px' },
  };
  const webview = { cspSource: 'vscode-webview:', asWebviewUri: uri => ({ toString: () => `webview:${uri.fsPath}` }) };
  const rendered = Object.fromEntries(pages.map(page => [page.id,
    renderDefinitionWizardHtml(webview, root, model, page.id, 'wizard-nonce', vscode)]));

  for (const [id, title] of pages.map(page => [page.id, page.title])) {
    assert.match(rendered[id], new RegExp(title.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'), 'u'));
    assert.match(rendered[id], /Page \d of 8/u);
  }
  assert.match(rendered.foundation, /Repository Doctor/u);
  assert.match(rendered.business, /Draft from references/u);
  assert.match(rendered.technical, /Modular monolith\./u);
  assert.match(rendered.technical, /data-save-question="TI-Q-1"/u);
  assert.match(rendered.architecture, /System context/u);
  assert.match(rendered.contracts, /API dictionary/u);
  assert.match(rendered.experience, /ui-system-preview\.svg/u);
  assert.match(rendered.delivery, /repository routing/u);
  assert.match(rendered.review, /7\/7 pages complete/u);
  assert.match(rendered.review, /Approve and activate/u);
  assert.doesNotMatch(rendered.review, /rationale|Why are/u);
  assert.match(rendered.review, /data-command="navigate" data-value="technical"/u);
  assert.match(rendered.review, /script-src 'nonce-wizard-nonce'/u);
  assert.match(rendered.review, /const vscode=acquireVsCodeApi/u);
});

test('Repository Doctor page presents grouped findings, evidence, possible fixes, and copy-only commands', () => {
  const html = renderDoctorHtml({ cspSource: 'vscode-webview:' }, {
    status: 'errors', repositoryPath: 'C:\\repo', documentationRoot: 'docs/cis',
    errorCount: 1, warningCount: 1, informationCount: 1,
    ollama: { isAvailable: true, models: ['local-model'] },
    findings: [
      { code: 'CIS-REPO-003', severity: 'error', category: 'managed-content',
        message: 'A generated update conflicts with <canonical> content.', evidence: ['docs/cis/specs/technical-intent-spec.md'],
        suggestedFix: 'Review and reconcile the generated baseline.',
        fixCommand: 'cis repo init --root docs/cis --accept-current --yes', fixability: 'manual' },
      { code: 'CIS-INDEX-001', severity: 'warning', category: 'file-index', message: 'Routing cards are unavailable.',
        evidence: ['.cis/local/index-cards'], suggestedFix: 'Build a bounded batch.',
        fixCommand: 'cis index build --limit 100', fixability: 'review-required' },
      { code: 'CIS-OLLAMA-003', severity: 'info', category: 'local-llm', message: 'Ollama is available.',
        evidence: ['local-model'], suggestedFix: 'No action is required.', fixability: 'none' },
    ],
  }, 'doctor-nonce');

  assert.match(html, /Repository Doctor/u);
  assert.match(html, /Errors \(1\)/u);
  assert.match(html, /Warnings \(1\)/u);
  assert.match(html, /Information \(1\)/u);
  assert.match(html, /A generated update conflicts with &lt;canonical&gt; content\./u);
  assert.match(html, /Review and reconcile the generated baseline\./u);
  assert.match(html, /Fixability: Manual/u);
  assert.match(html, /cis repo init --root docs\/cis --accept-current --yes/u);
  assert.match(html, /data-command="copy-fix" data-value="CIS-REPO-003"/u);
  assert.match(html, /data-command="open-path" data-value="docs\/cis\/specs\/technical-intent-spec\.md"/u);
  assert.match(html, /data-command="refresh"/u);
  assert.match(html, /never executed automatically/u);
  assert.match(html, /script-src 'nonce-doctor-nonce'/u);
});

test('BRD recommendation approval uses exact text without rationale arguments', () => {
  const asIs = extension.brdRecommendationDecisionArgs('RUN-1', 'BRD-REV-001', 'Andrew', 'C:\\repo');
  const modified = extension.brdRecommendationDecisionArgs('RUN-1', 'BRD-REV-001', 'Andrew', 'C:\\repo',
    '  Clarify the launch scope and owner.  ');

  assert.deepEqual(asIs, ['brd', 'review', 'decide', 'RUN-1', 'BRD-REV-001', '--decision', 'accepted',
    '--actor', 'Andrew', '--workspace', 'C:\\repo']);
  assert.deepEqual(modified, ['brd', 'review', 'decide', 'RUN-1', 'BRD-REV-001', '--decision', 'accepted',
    '--actor', 'Andrew', '--approved-recommendation', 'Clarify the launch scope and owner.', '--workspace', 'C:\\repo']);
  assert.doesNotMatch(modified.join(' '), /--reason/u);
  assert.deepEqual(extension.brdRecommendationAcceptAllArgs('RUN-1', 'Andrew', 'C:\\repo'),
    ['brd', 'review', 'accept-all', 'RUN-1', '--actor', 'Andrew', '--workspace', 'C:\\repo']);
});

test('completed BRD recommendations continue without a duplicate set approval', () => {
  assert.deepEqual(extension.brdRecommendationProgress({ status: 'approved', disposition: { findings: [
    { id: 'BRD-REV-001', decision: 'accepted' }, { id: 'BRD-REV-002', decision: 'accepted' },
  ] } }), { state: 'approved', pending: 0, accepted: 2, rejected: 0 });

  const html = renderRecommendationReviewHtml({ cspSource: 'vscode-webview:' }, 'RUN-1', {
    status: 'ready-for-approval', disposition: { findings: [
      { id: 'BRD-REV-001', recommendation: 'Add the source.', decision: 'accepted' },
    ] },
  }, 'review-nonce');
  assert.match(html, /Preparing the BRD revision/u);
  assert.match(html, /No additional human approval is required/u);
  assert.doesNotMatch(html, /Approve recommendation set|data-command="approve"/u);
});

test('BRD recommendation review shows the complete locked decision summary without another approval', () => {
  const html = renderRecommendationReviewHtml({ cspSource: 'vscode-webview:' }, 'RUN-1', {
    status: 'review-required', disposition: { findings: [
      { id: 'BRD-REV-001', recommendation: 'Add the source.', approvedRecommendation: 'Add a verified business source.', decision: 'accepted' },
      { id: 'BRD-REV-002', recommendation: 'Remove the actor.', decision: 'rejected', rationale: 'Actor is confirmed.' },
    ] },
  }, 'review-nonce');
  assert.match(html, /All recommendations reviewed/u);
  assert.match(html, /1 individually approved recommendation and 1 legacy rejected recommendation/u);
  assert.match(html, /Add a verified business source\./u);
  assert.match(html, /Reviewer originally proposed/u);
  assert.match(html, /Actor is confirmed\./u);
  assert.ok(html.indexOf('Overall recommendation list') < html.indexOf('Add the source.'));
  assert.doesNotMatch(html, /data-command="approve"/u);
  assert.doesNotMatch(html, /data-command="accept"/u);
  assert.doesNotMatch(html, /data-command="accept-all"/u);
});

test('approved change overview hierarchy renders outcome, next action, tasks, decisions, and canonical evidence', () => {
  const overview = {
    identity: { id: 'CIS-1', title: 'Delivery workspace', lifecycle: 'Planning', outcome: 'Guide governed delivery.' },
    progress: { planStatus: 'Approved', completedTasks: 2, executableTasks: 5, nextRecommendedAction: 'Continue WORK-20: Frontend' },
    scope: { acceptedImpactIds: ['IMPACT-1'], roots: [{ id: 'root-1' }] },
    design: { gate: 'Approved' },
    tasks: [{ id: 'WORK-20', title: 'Frontend', category: 'frontend', complexity: 'medium', status: 'InProgress' }],
    decisions: [{ id: 'DEC-1', title: 'Use a thin client' }],
    canonicalDocuments: { proposal: 'docs/changes/CIS-1/proposal.md' },
  };
  const html = renderChangeOverviewHtml({ cspSource: 'vscode-webview:' }, overview, 'screen-nonce');
  assert.match(html, /CIS-1 · Delivery workspace/u);
  assert.match(html, /Guide governed delivery\./u);
  assert.match(html, /Continue WORK-20: Frontend/u);
  assert.match(html, /Use a thin client/u);
  assert.match(html, /data-command="open-task" data-value="WORK-20"/u);
  assert.match(html, /data-command="open-path" data-value="docs\/changes\/CIS-1\/proposal\.md"/u);
  assert.ok(html.indexOf('Next meaningful action') < html.indexOf('Delivery plan'));
});

test('approved task detail hierarchy renders completion boundary before mutation controls', () => {
  const html = renderTaskDetailHtml({ cspSource: 'vscode-webview:' }, 'CIS-1', {
    id: 'WORK-20', title: 'Frontend', category: 'frontend', complexity: 'medium', status: 'Ready',
    dependencies: ['WORK-10'], targets: ['web'], requirementIds: ['VSC-1'],
    acceptanceCriteria: 'The visible hierarchy matches the approved screen.', approvalGate: 'none',
    taskPath: 'docs/changes/CIS-1/agent-tasks/WORK-20.md',
  }, 'screen-nonce');
  assert.match(html, /Completion boundary/u);
  assert.match(html, /The visible hierarchy matches the approved screen\./u);
  assert.match(html, /data-command="transition" data-value="WORK-20"/u);
  assert.match(html, /data-command="agent" data-value="WORK-20"/u);
  assert.ok(html.indexOf('Completion boundary') < html.indexOf('Transition task'));
});

test('approved run detail keeps result, provenance, artifacts, events, and recovery actions distinct', () => {
  const html = renderRunDetailHtml({ cspSource: 'vscode-webview:' }, {
    runId: 'RUN-1', manifest: { status: 'Failed', provider: 'codex', changeId: 'CIS-1', taskId: 'WORK-20',
      mode: 'implement', permission: 'workspace-write', attempt: 1, isolation: 'detached-worktree', repositoryRevision: 'abc123' },
    result: { summary: 'A bounded failure occurred.', changedFiles: [] },
    artifacts: [{ path: 'result.json', sha256: 'sha256:abc', valid: true }],
    events: [{ timestampUtc: '2026-08-30T10:00:00Z', kind: 'state', message: 'Agent run failed.' }],
  }, [{ command: 'resume', label: 'Resume as new attempt', value: 'RUN-1' }], 'screen-nonce');
  assert.match(html, /A bounded failure occurred\./u);
  assert.match(html, /detached-worktree/u);
  assert.match(html, /result\.json/u);
  assert.match(html, /Agent run failed\./u);
  assert.match(html, /data-command="resume" data-value="RUN-1"/u);
});

test('approved agent request confirmation visibly presents the exact bounded execution before start', () => {
  const html = renderAgentRequestHtml({ cspSource: 'vscode-webview:' }, {
    changeId: 'CIS-1', taskId: 'WORK-20', target: 'web', providerId: 'codex', providerName: 'Codex',
    transport: 'app-server', mode: 'implement', permission: 'workspace-write', isolation: 'Detached worktree',
    approveRequests: false, actor: 'Andrew Spiteri',
  }, 'screen-nonce');
  assert.match(html, /CIS-1 \/ WORK-20/u);
  assert.match(html, /Codex · app-server/u);
  assert.match(html, /Detached worktree/u);
  assert.match(html, /Do not enter credentials or secrets/u);
  assert.ok(html.indexOf('Permission ceiling') < html.indexOf('Start agent work'));
  assert.match(html, /data-command="start"/u);
  assert.match(html, /data-command="cancel"/u);
});

test('approved command progress keeps running, failure, bounded output, and cancellation visibly distinct', () => {
  const running = renderCommandProgressHtml({ cspSource: 'vscode-webview:' }, 'Build graph', {
    state: 'running', command: 'cis graph build --repo C:\\repo', durationMs: 2500, cancellable: true,
  }, 'screen-nonce');
  assert.match(running, /Process active/u);
  assert.match(running, /data-command="cancel"/u);
  const failed = renderCommandProgressHtml({ cspSource: 'vscode-webview:' }, 'Build graph', {
    state: 'failure', command: 'cis graph build --repo C:\\repo', durationMs: 3000, exitCode: 4,
    failureKind: 'invalid-evidence', stderr: 'Expected result file was missing.', truncated: false,
  }, 'screen-nonce');
  assert.match(failed, /invalid-evidence/u);
  assert.match(failed, /Expected result file was missing\./u);
  assert.doesNotMatch(failed, /data-command="cancel"/u);
});

test('webview controllers dispatch only allowlisted evidence and cancellation actions', async () => {
  const panels = [];
  const localVscode = { ...vscode, ViewColumn: { Active: 1 }, window: { createWebviewPanel: (kind, title, _column, options) => {
    const panel = { kind, title, options, webview: { cspSource: 'vscode-webview:', html: '',
      onDidReceiveMessage(handler) { this.message = handler; } } };
    panels.push(panel); return panel;
  } } };
  const actions = []; const paths = [];
  const evidence = openEvidencePanel(localVscode, 'Evidence', { file: 'docs/specs/feature.md' },
    [{ command: 'refresh', label: 'Refresh' }], async (command, value) => actions.push([command, value]),
    async value => paths.push(value));
  await evidence.webview.message({ command: 'ignored', value: 'x' });
  await evidence.webview.message({ command: 'refresh', value: 'now' });
  await evidence.webview.message({ command: 'open-path', value: 'docs/specs/feature.md' });
  assert.deepEqual(actions, [['refresh', 'now']]);
  assert.deepEqual(paths, ['docs/specs/feature.md']);

  let cancelled = 0;
  const progress = openCommandProgressPanel(localVscode, 'Build graph', { state: 'running', cancellable: true }, () => ++cancelled);
  progress.update({ state: 'failure', cancellable: false, exitCode: 4, durationMs: 65_000, stderr: 'failed' });
  progress.panel.webview.message({ command: 'ignored', value: '' });
  progress.panel.webview.message({ command: 'cancel', value: '' });
  assert.equal(cancelled, 1);
  assert.match(progress.panel.webview.html, /failed/u);
  assert.match(progress.panel.webview.html, /1m 5s/u);
  assert.equal(panels.length, 2);
});

test('approved graph detail presents bounded nodes, evidence paths, relationships, freshness, and diagnostics', () => {
  const html = renderContextHtml({ cspSource: 'vscode-webview:' }, 'Context: workspace', {
    status: 'matched', freshness: 'stale', truncated: true, query: { text: 'workspace' },
    nodes: [{ key: 'repo::document::workspace', kind: 'document', subtype: 'specification', label: 'Workspace spec',
      lifecycle: 'draft', locations: [{ path: 'docs/specs/workspace.md' }] }],
    traversals: [{ from: 'workspace', type: 'references', to: 'cli' }],
    diagnostics: [{ code: 'CIS-GRAPH-001', severity: 'warning', message: 'Graph inputs changed.' }],
  }, 'screen-nonce');
  assert.match(html, /Results are truncated/u);
  assert.match(html, /Workspace spec/u);
  assert.match(html, /data-command="open-path" data-value="docs\/specs\/workspace\.md"/u);
  assert.match(html, /data-command="related" data-value="repo::document::workspace"/u);
  assert.match(html, /Graph inputs changed\./u);
});

// Trace: TC-VSC-006-001, TC-VSC-009-001.
test('TC-VSC-006-001 TC-VSC-009-001 change overview projects a single dependency-aware next action', () => {
  const overview = projectChangeOverview({ change: { id: 'CIS-1', title: 'Feature', status: 'Proposed', outcome: 'Outcome' } }, {
    planStatus: 'Approved', workItems: [
      { id: 'WORK-010', title: 'Docs', category: 'documentation', complexity: 'low', status: 'Complete', dependsOn: [] },
      { id: 'WORK-020', title: 'Frontend', category: 'frontend', complexity: 'medium', status: 'InProgress', dependsOn: ['WORK-010'] },
      { id: 'WORK-030', title: 'Verify', category: 'verification', complexity: 'medium', status: 'Draft', dependsOn: ['WORK-020'] },
    ],
  }, { gateStatus: 'Approved', approvalStatus: 'Approved', artifacts: [{ path: 'screen.png' }] });
  assert.equal(overview.progress.nextRecommendedAction, 'Continue WORK-020: Frontend');
  assert.equal(overview.progress.completedTasks, 1);
  assert.equal(overview.design.artifactCount, 1);
});

// Trace: TC-VSC-005-001, TC-VSC-007-001.
test('TC-VSC-005-001 TC-VSC-007-001 six native views are declared and the Changes projection filters without mutation', async () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8'));
  assert.deepEqual(manifest.contributes.views.cis.map(view => view.id),
    ['cis.workspace', 'cis.journey', 'cis.changes', 'cis.evidence', 'cis.runs', 'cis.governance']);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: docs\n');
    const authority = { root: () => root, needsSelection: () => false };
    const cli = { query: async args => {
      const key = args.join(' ');
      if (key === 'change list') return { changes: [
        { id: 'CIS-1', title: 'Active work', status: 'Proposed', relativePath: 'docs/changes/CIS-1' },
        { id: 'CIS-2', title: 'Previous work', status: 'Closed', relativePath: 'docs/changes/CIS-2' },
      ] };
      if (key === 'plan show CIS-1') return { planStatus: 'Approved', workItems: [{ status: 'InProgress' }] };
      if (key === 'design status CIS-1') return { gateStatus: 'Approved' };
      return {};
    } };
    const provider = new extension.CisViewProvider(vscode, 'changes', authority, cli);
    provider.setFilter('closed');
    const items = await provider.getChildren();
    assert.deepEqual(items.map(item => item.label), ['CIS-2: Previous work']);
    assert.match(items[0].description, /Closed · Closed · Complete/u);
    assert.equal(items[0].command.command, 'cis.openChange');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// Trace: TC-VSC-011-001, TC-VSC-020-001, TC-VSC-023-001.
test('TC-VSC-011-001 TC-VSC-020-001 TC-VSC-023-001 Runs projection keeps agent, workflow, tests, security, and verification distinct', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: docs\n');
    const authority = { root: () => root, needsSelection: () => false };
    const cli = { query: async args => args.join(' ') === 'agent providers'
      ? { providers: [{ id: 'codex', displayName: 'Codex', kind: 'local-cli', modes: ['implement'], directExecution: true }] }
      : { runs: [{ runId: 'RUN-1', status: 'Failed', changeId: 'CIS-1', taskId: 'WORK-1', attempt: 1 }] } };
    const provider = new extension.CisViewProvider(vscode, 'runs', authority, cli);
    const labels = (await provider.getChildren()).map(item => item.label);
    assert.deepEqual(labels, ['Providers', 'Agent runs', 'Workflow run status', 'Test and coverage status', 'Security status',
      'Verification status', 'Request agent work']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// Trace: TC-VSC-002-001, TC-VSC-016-001, TC-VSC-018-001.
test('TC-VSC-002-001 TC-VSC-016-001 TC-VSC-018-001 Workspace Welcome projection distinguishes no-folder, untrusted, and incompatible CLI states', async () => {
  const noFolder = new extension.CisViewProvider(vscode, 'workspace', { root: () => undefined, needsSelection: () => false }, {});
  assert.equal((await noFolder.getChildren())[0].label, 'Open a repository folder');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    const authority = { root: () => root, needsSelection: () => false };
    vscode.workspace.isTrusted = false;
    const untrusted = new extension.CisViewProvider(vscode, 'workspace', authority, {});
    assert.equal((await untrusted.getChildren())[0].label, 'Workspace is untrusted');
    vscode.workspace.isTrusted = true;
    const incompatible = new extension.CisViewProvider(vscode, 'workspace', authority,
      { version: async () => ({ raw: '1.0.0', compatible: false }) });
    assert.match((await incompatible.getChildren())[0].label, /incompatible/u);
  } finally { vscode.workspace.isTrusted = true; fs.rmSync(root, { recursive: true, force: true }); }
});

test('existing source repository is routed through import while an empty project is routed through create', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-existing-onboarding-'));
  try {
    assert.equal(hasExistingRepositoryEvidence(root), false);
    assert.equal(repositoryMetadata(root, 'docs/cis').onboardingMode, 'create');
    fs.mkdirSync(path.join(root, 'src'), { recursive: true });
    fs.writeFileSync(path.join(root, 'src', 'server.ts'), 'export const ready = true;\n');
    assert.equal(hasExistingRepositoryEvidence(root), true);
    assert.equal(repositoryMetadata(root, 'docs/cis').onboardingMode, 'import');

    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, {
      version: async () => ({ raw: '0.3.0', compatible: true }),
    });
    const items = await provider.getChildren();
    assert.deepEqual(items.map(item => item.label), ['Existing repository is not imported', 'Import existing repository']);
    assert.equal(items[1].command.command, 'cis.repoImport');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// Trace: TC-VSC-004-001, TC-VSC-006-001, TC-VSC-016-001.
test('TC-VSC-004-001 TC-VSC-006-001 TC-VSC-016-001 Workspace projection reports health, freshness, active change, review gate, and one next action', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true }); fs.mkdirSync(path.join(root, 'docs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, 'docs', 'README.md'), '# Docs\n');
    const responses = {
      'repo doctor': { warningCount: 2, errorCount: 0, ollama: { isAvailable: true, models: ['local-model'] }, findings: [
        { code: 'CIS-GRAPH-VALIDATE-STALE-003', severity: 'warning', category: 'context-graph', message: 'Graph stale' },
        { code: 'CIS-INDEX-002', severity: 'warning', category: 'file-index', message: 'Index stale' },
      ] },
      'change list': { changes: [{ id: 'CIS-1', title: 'Feature', status: 'Proposed', relativePath: 'docs/changes/CIS-1' }] },
      'agent providers': { providers: [{ id: 'codex' }, { id: 'claude' }] },
      'plan show CIS-1': { workItems: [{ id: 'WORK-1', title: 'Implement', status: 'InProgress', dependsOn: [] }] },
      'design status CIS-1': { gateStatus: 'Approved', approvalStatus: 'Approved', artifacts: [{ path: 'screen.png' }] },
    };
    const cli = { version: async () => ({ raw: '0.3.0', compatible: true }), query: async args => responses[args.join(' ')] };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const labels = (await provider.getChildren()).map(item => item.label);
    assert.ok(labels.includes('fixture')); assert.ok(labels.includes('CIS 0.3.0')); assert.ok(labels.includes('Doctor: warnings'));
    assert.ok(labels.includes('Local AI available')); assert.ok(labels.includes('Context graph is stale or invalid'));
    assert.ok(labels.includes('Routing index is stale or incomplete')); assert.ok(labels.includes('CIS-1'));
    assert.ok(labels.includes('Design gate: Approved')); assert.ok(labels.includes('Next: WORK-1'));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('Workspace projection does not classify informational graph and index findings as stale', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-current-context-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true }); fs.mkdirSync(path.join(root, 'docs'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: docs\n');
    const responses = {
      'repo doctor': { warningCount: 0, errorCount: 0, ollama: { isAvailable: true }, findings: [
        { code: 'CIS-GRAPH-REF-003', severity: 'information', category: 'context-graph', message: 'Placeholder skipped' },
        { code: 'CIS-INDEX-003', severity: 'information', category: 'file-index', message: 'Index fresh' },
      ] },
      'change list': { changes: [{ id: 'CIS-1', title: 'Feature', status: 'Proposed', relativePath: 'docs/changes/CIS-1' }] },
      'agent providers': { providers: [] },
      'plan show CIS-1': { workItems: [] },
      'design status CIS-1': { gateStatus: 'Not applicable', approvalStatus: 'Not applicable', artifacts: [] },
    };
    const cli = { version: async () => ({ raw: '0.3.0', compatible: true }), query: async args => responses[args.join(' ')] };
    const provider = new extension.CisViewProvider(vscode, 'workspace', { root: () => root, needsSelection: () => false }, cli);
    const labels = (await provider.getChildren()).map(item => item.label);
    assert.ok(labels.includes('Context graph is current'));
    assert.ok(labels.includes('Routing index is current'));
    assert.ok(!labels.includes('Context graph is stale or invalid'));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// Trace: TC-VSC-008-001.
test('TC-VSC-008-001 Evidence commands expose bounded search and explicit relationship traversal', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8'));
  const commands = new Set(manifest.contributes.commands.map(command => command.command));
  assert.equal(commands.has('cis.contextSearch'), true);
  assert.equal(commands.has('cis.graphRelated'), true);
  assert.equal(commands.has('cis.repoImport'), true);
  assert.equal(commands.has('cis.indexBuild'), true);
});

// Trace: TC-VSC-010-001, TC-VSC-015-001.
test('TC-VSC-010-001 TC-VSC-015-001 design review renders validated PNG metadata and hides duplicate decision actions after approval', () => {
  let panel;
  const designVscode = { ...vscode, ViewColumn: { Active: 1 }, window: { createWebviewPanel: () => {
    panel = { webview: { cspSource: 'vscode-webview:', html: '', asWebviewUri: uri => ({ toString: () => `webview://${uri.fsPath}` }),
      onDidReceiveMessage() {} } }; return panel;
  } } };
  openDesignPanel(designVscode, 'C:\\repo', 'CIS-1', { gateStatus: 'Approved', approvalStatus: 'Approved', artifacts: [
    { screenId: 'workspace', state: 'healthy', viewport: 'desktop', path: 'screen.png', width: 1600, height: 1000, sha256: 'sha256:abc' },
  ] }, value => `C:\\repo\\${value}`, async () => {});
  assert.match(panel.webview.html, /workspace/u); assert.match(panel.webview.html, /sha256:abc/u);
  assert.match(panel.webview.html, /No design decision is currently required/u); assert.doesNotMatch(panel.webview.html, /data-command="approve"/u);
  assert.match(panel.webview.html, /aria-label="Rendered screen states"/u);
});

// Trace: TC-VSC-012-001.
test('TC-VSC-012-001 Governance projection distinguishes Doctor findings, skills, standards, references, and canonical inventories', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: docs\n');
    const responses = {
      'repo doctor': { findings: [{ code: 'CIS-X', severity: 'warning', category: 'skills', message: 'Conflict candidate' }] },
      'skills inventory --summary': { status: 'valid', skillCount: 1 },
      'standards inventory --summary': { status: 'inventoried', standardCount: 1 },
      'references validate': { status: 'valid', errors: 0 },
    };
    const provider = new extension.CisViewProvider(vscode, 'governance', { root: () => root, needsSelection: () => false },
      { query: async args => responses[args.join(' ')] });
    const labels = (await provider.getChildren()).map(item => item.label);
    assert.deepEqual(labels.slice(0, 4), ['Repository Doctor findings', 'Skills inventory', 'Standards inventory', 'References validation']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// Trace: TC-VSC-016-001.
test('TC-VSC-016-001 view projection retains the last authoritative result with an explicit stale marker after refresh failure', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: docs\n');
    let fail = false;
    const provider = new extension.CisViewProvider(vscode, 'changes', { root: () => root, needsSelection: () => false }, {
      query: async () => { if (fail) throw new Error('temporary failure'); return { changes: [{ id: 'CIS-1', title: 'Feature', status: 'Proposed', relativePath: 'docs/changes/CIS-1' }] }; },
    });
    assert.equal((await provider.getChildren())[0].label, 'CIS-1: Feature');
    fail = true;
    const stale = await provider.getChildren();
    assert.equal(stale[0].label, 'Results are stale'); assert.equal(stale[1].label, 'CIS-1: Feature');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// Trace: TC-VSC-019-001.
test('TC-VSC-019-001 packaged and canonical manuals cover installation, navigation, settings, security, and agent work', () => {
  const readme = fs.readFileSync(path.join(__dirname, '..', 'README.md'), 'utf8');
  const manual = fs.readFileSync(path.join(__dirname, '..', '..', 'docs', 'manual', 'cis_vscode_extension.md'), 'utf8');
  for (const expected of ['Install', 'Workspace', 'Governed agent work', 'cis.executablePath']) assert.match(readme, new RegExp(expected, 'u'));
  for (const expected of ['First use', 'Settings', 'Troubleshooting', 'Security boundary', 'cis.actorIdentity']) assert.match(manual, new RegExp(expected, 'u'));
});

// Trace: TC-VSC-020-001, TC-VSC-021-001, TC-VSC-022-001, TC-VSC-023-001.
test('TC-VSC-020-001 TC-VSC-021-001 TC-VSC-022-001 TC-VSC-023-001 agent request uses only CIS-declared provider, target, mode, permission, and transport values', async () => {
  const originalWindow = vscode.window;
  const inputValues = ['WORK-1', 'Andrew Spiteri'];
  vscode.window = {
    showInputBox: async () => inputValues.shift(),
    showQuickPick: async items => {
      if (items[0] === 'api') return 'web';
      if (typeof items[0] === 'object' && items[0].provider) return items[0];
      if (items.includes('implement')) return 'implement';
      if (items.includes('workspace-write')) return 'workspace-write';
      if (items.includes('app-server')) return 'app-server';
      if (typeof items[0] === 'object' && Object.hasOwn(items[0], 'approve')) return items[1];
      throw new Error('Unexpected Quick Pick fixture.');
    },
    showWarningMessage: async () => 'Start governed work',
  };
  const queries = []; let execution;
  const cli = {
    query: async args => {
      queries.push(args);
      if (args.join(' ') === 'agent providers') return { providers: [{ id: 'codex', displayName: 'Codex', directExecution: true,
        modes: ['plan', 'implement'], permissions: ['read-only', 'workspace-write'], transports: ['exec-json', 'app-server'],
        supportsInteractivePermissions: true }] };
      if (args.join(' ') === 'plan show CIS-1') return { workItems: [{ id: 'WORK-1', targets: ['api', 'web'] }] };
      return { status: 'prepared' };
    },
    runForeground: async (_title, args) => { execution = args; },
  };
  let refreshes = 0;
  try {
    await extension.requestAgentWork(cli, async () => ++refreshes, 'CIS-1');
    assert.ok(queries.some(args => args.join(' ') === 'agent prepare CIS-1 WORK-1 --provider codex'));
    assert.deepEqual(execution, ['agent', 'run', 'CIS-1', 'WORK-1', '--provider', 'codex', '--mode', 'implement',
      '--permission', 'workspace-write', '--transport', 'app-server', '--actor', 'Andrew Spiteri', '--target', 'web', '--approve-requests']);
    assert.equal(refreshes, 1);
  } finally { vscode.window = originalWindow; }
});

// Trace: TC-VSC-014-001.
test('TC-VSC-014-001 debounced refresh runs only the last scheduled projection', async () => {
  let count = 0;
  let received;
  const invoke = extension.debounce(async value => { received = value; return ++count; }, 10);
  const first = invoke('first');
  const second = invoke('second');
  const third = invoke('third');
  assert.deepEqual(await Promise.all([first, second, third]), [1, 1, 1]);
  assert.equal(count, 1);
  assert.equal(received, 'third');
});

// Trace: TC-VSC-014-001.
test('TC-VSC-014-001 stale watcher notifications do not start a projection reload', () => {
  let fires = 0;
  const localVscode = {
    ...vscode,
    EventEmitter: class {
      constructor() { this.event = () => {}; }
      fire() { fires += 1; }
    },
  };
  const provider = new extension.CisViewProvider(localVscode, 'workspace', {}, {});
  provider.markStale();
  assert.equal(provider.stale, true);
  assert.equal(fires, 0);
  provider.refresh(false);
  assert.equal(provider.stale, false);
  assert.equal(fires, 1);
});

test('BRD review completion refreshes again after trailing filesystem notifications settle', async () => {
  const calls = [];
  const waits = [];
  await extension.refreshAfterReviewCompletion(async stale => calls.push(stale), async delay => waits.push(delay));
  assert.deepEqual(calls, [false, false]);
  assert.deepEqual(waits, [400]);
});

// Trace: TC-VSC-002-001, TC-VSC-003-001, TC-VSC-007-001, TC-VSC-009-001,
// TC-VSC-010-001, TC-VSC-012-001, TC-VSC-013-001, TC-VSC-014-001,
// TC-VSC-015-001, TC-VSC-016-001, TC-VSC-018-001, TC-VSC-020-001,
// TC-VSC-022-001, TC-VSC-023-001.
test('TC-VSC-002-001 TC-VSC-003-001 TC-VSC-007-001 TC-VSC-009-001 TC-VSC-010-001 TC-VSC-012-001 TC-VSC-013-001 TC-VSC-014-001 TC-VSC-015-001 TC-VSC-016-001 TC-VSC-018-001 TC-VSC-020-001 TC-VSC-022-001 TC-VSC-023-001 activation registers and drives the native workspace command surface', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-activation-'));
  const originalWindow = vscode.window;
  const originalCommands = vscode.commands;
  const originalWorkspace = { ...vscode.workspace };
  const originalRelativePattern = vscode.RelativePattern;
  const originalViewColumn = vscode.ViewColumn;
  const originalStatusBarAlignment = vscode.StatusBarAlignment;
  const originalEnv = vscode.env;
  const registered = new Map();
  const treeProviders = new Map();
  const foreground = [];
  const queries = [];
  const panels = [];
  const opened = [];
  const outputLines = [];
  const clipboardWrites = [];
  const workspaceEvents = {};
  const watchers = [];
  let brdQuestionAnswered = false;
  let reviewDecision = 'pending';
  let reviewApproved = false;
  let technicalQuestionsInitialized = false;
  let definitionSession = false;
  let aiMode = 'local';
  let openDialogOptions;
  const promptValue = prompt => {
    if (/documentation root/iu.test(prompt)) return 'docs/cis';
    if (/Search bounded/iu.test(prompt)) return 'agent provider';
    if (/graph node/iu.test(prompt)) return 'node:one';
    if (/Filter changes/iu.test(prompt)) return 'active';
    if (/Workflow run/iu.test(prompt)) return 'WF-1';
    if (/Change ID/iu.test(prompt)) return 'CIS-0001';
    if (/Task ID/iu.test(prompt)) return 'WORK-100-BACKOFFICE';
    if (/rationale|attempt required/iu.test(prompt)) return 'Reproducible test rationale';
    if (/continuation instruction/iu.test(prompt)) return 'Continue from retained evidence';
    return 'fixture';
  };
  try {
    const proposal = path.join(root, 'docs', 'changes', 'CIS-0001', 'proposal.md');
    const brd = path.join(root, 'docs', 'specs', 'business-requirements.md');
    const technicalQuestionnaire = path.join(root, 'docs', 'specs', 'technical-intent-questionnaire.md');
    const technicalIntent = path.join(root, 'docs', 'specs', 'technical-intent-spec.md');
    const overallSolutionDesign = path.join(root, 'docs', 'architecture', 'overall-solution-design.md');
    const componentSheet = path.join(root, 'docs', 'references', 'component-sheet.md');
    const uiDirectionQuestionnaire = path.join(root, 'docs', 'specs', 'ui-direction-questionnaire.md');
    const uiDirection = path.join(root, 'docs', 'design', 'ui-direction.md');
    const uiPreview = path.join(root, 'docs', 'design', 'ui-system-preview.svg');
    const backlog = path.join(root, 'docs', 'plans', 'high-level-backlog.md');
    const feature = path.join(root, 'docs', 'specs', 'features', 'HLT-FR-001.md');
    const reference = path.join(root, 'reference.md');
    const image = path.join(root, 'docs', 'changes', 'CIS-0001', 'assets', 'screen.png');
    const brdReview = path.join(root, '.cis', 'local', 'agents', 'runs', 'RUN-BRD-REVIEW', 'brd-review.md');
    const brdDisposition = path.join(root, 'docs', 'reviews', 'brd', 'RUN-BRD-REVIEW.md');
    fs.mkdirSync(path.dirname(proposal), { recursive: true });
    fs.mkdirSync(path.dirname(image), { recursive: true });
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'repository:\n  id: fixture\ndocumentation_root: docs\n');
    fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: fixture\n');
    fs.writeFileSync(proposal, '# Proposal\n');
    fs.mkdirSync(path.dirname(brd), { recursive: true });
    fs.writeFileSync(brd, '---\nstatus: Review Required\n---\n# BRD\n');
    fs.writeFileSync(technicalQuestionnaire, '# Technical direction\n');
    fs.writeFileSync(technicalIntent, '---\nstatus: Review Required\n---\n# Technical intent\n');
    fs.mkdirSync(path.dirname(overallSolutionDesign), { recursive: true });
    fs.mkdirSync(path.dirname(componentSheet), { recursive: true });
    fs.writeFileSync(overallSolutionDesign, '---\nstatus: Review Required\n---\n# Overall solution design\n');
    fs.writeFileSync(componentSheet, '---\nstatus: Review Required\n---\n# Component sheet\n');
    fs.mkdirSync(path.dirname(uiDirection), { recursive: true });
    fs.writeFileSync(uiDirectionQuestionnaire, '# High-level UI direction questionnaire\n');
    fs.writeFileSync(uiDirection, '---\nstatus: Review Required\n---\n# High-level UI direction\n');
    fs.writeFileSync(uiPreview, '<svg xmlns="http://www.w3.org/2000/svg"></svg>');
    fs.mkdirSync(path.dirname(backlog), { recursive: true });
    fs.writeFileSync(backlog, '---\nstatus: Review Required\n---\n# Backlog\n');
    fs.mkdirSync(path.dirname(feature), { recursive: true });
    fs.writeFileSync(feature, '---\nstatus: Review Required\n---\n# Feature\n');
    fs.writeFileSync(reference, '# Reference\n');
    fs.writeFileSync(image, 'png');
    fs.mkdirSync(path.dirname(brdReview), { recursive: true });
    fs.writeFileSync(brdReview, '# Independent BRD review\n');
    fs.mkdirSync(path.dirname(brdDisposition), { recursive: true });
    fs.writeFileSync(brdDisposition, '# BRD review disposition\n');

    configuration.actorIdentity = 'Andrew Spiteri';
    vscode.ViewColumn = { Active: 1 };
    vscode.StatusBarAlignment = { Left: 1 };
    vscode.env = { clipboard: { writeText: async value => clipboardWrites.push(value) } };
    vscode.RelativePattern = class { constructor(base, pattern) { this.base = base; this.pattern = pattern; } };
    vscode.workspace.workspaceFolders = [{ name: 'fixture', uri: vscode.Uri.file(root) }];
    vscode.workspace.isTrusted = true;
    vscode.workspace.createFileSystemWatcher = pattern => {
      const watcher = { pattern, onDidChange(handler) { this.change = handler; }, onDidCreate(handler) { this.create = handler; },
        onDidDelete(handler) { this.delete = handler; }, dispose() { this.disposed = true; } };
      watchers.push(watcher); return watcher;
    };
    vscode.workspace.onDidChangeWorkspaceFolders = handler => { workspaceEvents.folders = handler; return { dispose() {} }; };
    vscode.workspace.onDidGrantWorkspaceTrust = handler => { workspaceEvents.trust = handler; return { dispose() {} }; };

    vscode.commands = {
      registerCommand: (id, handler) => { registered.set(id, handler); return { dispose() {} }; },
      executeCommand: async (id, ...args) => {
        if (registered.has(id)) return registered.get(id)(...args);
        opened.push({ id, args }); return undefined;
      },
    };
    vscode.window = {
      createOutputChannel: () => ({ append() {}, appendLine: value => outputLines.push(value), show() { this.shown = true; }, dispose() {} }),
      registerTreeDataProvider: (id, provider) => { treeProviders.set(id, provider); return { dispose() {} }; },
      createStatusBarItem: () => ({ show() { this.visible = true; }, dispose() {} }),
      showInputBox: async options => promptValue(options.prompt || ''),
      showOpenDialog: async options => { openDialogOptions = options; return [vscode.Uri.file(reference)]; },
      showQuickPick: async items => Array.isArray(items) && items.includes('Complete') ? 'Complete' : items[0],
      showWarningMessage: async (_message, _options, action) => action,
      showInformationMessage: async () => undefined,
      showErrorMessage: async () => 'Show output',
      showTextDocument: async (uri, options) => opened.push({ id: 'text', uri, options }),
      createWebviewPanel: (kind, title, _column, options) => {
        const panel = { kind, title, options, webview: { cspSource: 'vscode-webview:', html: '',
          asWebviewUri: uri => ({ toString: () => `webview://${uri.fsPath}` }),
          onDidReceiveMessage(handler) { this.message = handler; } } };
        panels.push(panel); return panel;
      },
    };

    const authority = {
      root: () => root,
      needsSelection: () => false,
      choose: async () => ({ uri: vscode.Uri.file(root) }),
      clear: async () => {},
    };
    const fakeCli = {
      version: async () => ({ raw: '0.3.0', compatible: true }),
      query: async args => {
        const key = args.join(' '); queries.push(key);
        if (key.startsWith('definition init --workspace ')) {
          definitionSession = true;
          return { status: 'initialized', sessionId: 'DEF-1', pages: [] };
        }
        if (key.startsWith('definition answer --page ') || key.startsWith('definition prepare --page '))
          return { status: 'prepared', sessionId: 'DEF-1', pages: [] };
        if (key.startsWith('definition status --workspace ')) {
          const pageNames = [
            ['foundation', 'Project foundation'], ['business', 'Business definition'], ['technical', 'Technical direction'],
            ['architecture', 'Solution architecture and diagrams'], ['contracts', 'Contracts and dictionaries'],
            ['experience', 'Experience direction and UI preview'], ['delivery', 'Delivery map'], ['review', 'Review and activate'],
          ];
          return {
            status: 'status', sessionId: definitionSession ? 'DEF-1' : null, active: definitionSession,
            authorityRepositoryId: 'fixture', workspacePath: root, readyToActivate: true,
            pages: pageNames.map(([id, title], index) => ({ id, title, ordinal: index + 1, status: 'Ready for Approval',
              complete: true, current: true, primaryPath: `docs/${id}.md`, artifactPaths: [`docs/${id}.md`], issues: [] })),
            dictionaries: [{ title: 'API dictionary', applicable: true, entryCount: 1, relativePath: 'docs/references/api-dictionary.md' }],
            diagrams: [{ title: 'System context', sourceFormat: 'mermaid', relativePath: 'docs/architecture/high-level-architecture-diagrams.md' }],
            preview: { svgRelativePath: 'docs/design/ui-system-preview.svg', fontFamily: 'Inter', density: 'Balanced', radius: '6px' },
          };
        }
        if (/^(brd validate|technical-intent validate|brd backlog validate|brd feature validate --item HLT-FR-001) --workspace /u.test(key))
          return { status: 'ReadyForApproval', valid: true, current: true, errors: [], warnings: [] };
        if (key.startsWith('solution-design status --workspace '))
          return { status: 'Active', valid: true, current: true, validation: { effectiveStatus: 'Active', valid: true, current: true } };
        if (key.startsWith('ui-direction questions status --workspace ')) return {
          status: 'status', current: true, complete: true, answeredCount: 12, unansweredCount: 0,
          questions: [{ id: 'UI-Q-004', area: 'Layout and density', question: 'Which density?',
            status: 'Answered', answer: 'Compact, readable working surfaces.', answeredBy: 'Andrew Spiteri' }],
        };
        if (key.startsWith('ui-direction status --workspace '))
          return { status: 'Active', valid: true, current: true, validation: { effectiveStatus: 'Active', valid: true, current: true } };
        if (key.startsWith('brd backlog status --workspace ')) return { status: 'Active', items: [
          { id: 'HLT-FR-001', outcome: 'First governed feature', dependsOn: [], featureSpecification: 'not-created' },
        ] };
        if (key.startsWith('brd feature status --item HLT-FR-001 --workspace '))
          return { status: 'ReviewRequired', relativePath: 'docs/specs/features/HLT-FR-001.md' };
        if (key.startsWith('brd questions list --workspace ')) return {
          questions: [{ id: 'BRD-Q-001', status: brdQuestionAnswered ? 'Answered' : 'Unanswered' }],
          answerDigest: brdQuestionAnswered ? 'sha256:answered' : undefined,
        };
        if (key.startsWith('brd review status RUN-BRD-REVIEW --workspace ')) return {
          status: reviewApproved ? 'approved' : 'review-required',
          acceptedCount: reviewDecision === 'accepted' ? 1 : 0,
          rejectedCount: 0,
          canonicalPath: brdDisposition,
          disposition: { findings: [{ id: 'BRD-REV-001', severity: 'major', category: 'scope', location: 'Requirements',
            observation: 'The scope needs one clarification.', recommendation: 'Clarify the bounded scope.',
            approvedRecommendation: reviewDecision === 'accepted' ? 'Clarify the bounded scope with explicit evidence.' : undefined,
            decision: reviewDecision }] },
        };
        if (key === 'repo doctor') return { status: 'warnings', errorCount: 0, warningCount: 1, informationCount: 0,
          repositoryPath: root, documentationRoot: 'docs', ollama: { isAvailable: true, models: ['local-model'] }, findings: [
            { code: 'CIS-INDEX-001', severity: 'warning', category: 'file-index', message: 'Routing cards are unavailable.',
              evidence: ['docs/changes/CIS-0001/proposal.md'], suggestedFix: 'Build a bounded batch.', fixCommand: 'cis index build --limit 100', fixability: 'review-required' },
          ] };
        if (key === 'change list') return { changes: [{ id: 'CIS-0001', title: 'Workspace', status: 'Active', relativePath: 'docs/changes/CIS-0001' }] };
        if (key === 'change show CIS-0001') return { change: { id: 'CIS-0001', title: 'Workspace', status: 'Active', relativePath: 'docs/changes/CIS-0001' } };
        if (key === 'plan show CIS-0001') return { planStatus: 'Approved', workItems: [{ id: 'WORK-100-BACKOFFICE', title: 'Frontend', status: 'InProgress', dependsOn: [], targets: ['change-impact-studio'] }] };
        if (key === 'design status CIS-0001') return { gateStatus: 'PausedForReview', approvalStatus: 'Pending', artifacts: [
          { screenId: 'workspace', state: 'healthy', viewport: 'desktop', path: 'docs/changes/CIS-0001/assets/screen.png', width: 1600, height: 1000, sha256: 'sha256:abc' },
        ] };
        if (key === 'design validate CIS-0001') return { status: 'valid' };
        if (key === 'decision list CIS-0001') return { decisions: [] };
        if (key === 'agent providers') return { providers: [
          { id: 'codex', displayName: 'Codex', kind: 'local-cli', modes: ['implement'],
            permissions: ['workspace-write'], transports: ['app-server'], supportsInteractivePermissions: true, directExecution: true },
          { id: 'claude', displayName: 'Claude', kind: 'local-cli', modes: ['review'],
            permissions: ['read-only'], transports: ['stream-json'], supportsInteractivePermissions: false, directExecution: true },
        ] };
        if (key === 'agent runs --summary --limit 10') return { runs: [{ runId: 'RUN-1', status: 'Interrupted', changeId: 'CIS-0001', taskId: 'WORK-100-BACKOFFICE', attempt: 1 }] };
        if (key === 'agent runs --change PRODUCT --summary --latest-per-task --limit 10') return { runs: [
          { runId: 'RUN-BRD-DRAFT', status: 'Succeeded', changeId: 'PRODUCT', taskId: 'BRD-DRAFT', provider: 'codex', completedAtUtc: '2026-08-30T10:00:00Z' },
        ] };
        if (key === 'agent provider diagnose claude') return { diagnoses: [{ provider: 'claude', available: true, status: 'ready' }] };
        if (key === 'agent provider diagnose codex') return { diagnoses: [{ provider: 'codex', available: true,
          status: 'ready', capabilities: ['authentication:browser', 'authentication:device'] }] };
        if (key === 'agent runs --change PRODUCT --task BRD-REVIEW --summary --limit 10') return { runs: [
          { runId: 'RUN-BRD-REVIEW', status: 'Succeeded', changeId: 'PRODUCT', taskId: 'BRD-REVIEW', provider: 'claude', completedAtUtc: '2026-08-30T10:05:00Z' },
        ] };
        if (key === 'agent show RUN-1') return { run: { manifest: { status: 'Interrupted' }, runId: 'RUN-1' } };
        if (key === 'agent show RUN-BRD-REVIEW --summary') return { run: {
          manifest: { provider: 'claude' },
          result: { review: { findings: [{ id: 'BRD-REV-001', recommendation: 'Clarify the bounded scope.' }] } },
        } };
        if (key === 'ai status') return { providers: aiMode === 'local'
          ? [{ name: 'ollama', isLocal: true, isAvailable: true }]
          : [{ name: 'remote-ai', isLocal: false, isAvailable: true, detail: 'Configured test provider' }] };
        if (key.startsWith('context search ')) return { status: 'current', nodes: [{ key: 'node:one', label: 'Agent provider',
          kind: 'reference', locations: [{ path: 'docs/changes/CIS-0001/proposal.md' }] }], traversals: [], diagnostics: [] };
        if (key.startsWith('graph related ')) return { status: 'current', nodes: [{ key: 'node:two', label: 'Related provider',
          kind: 'component', properties: { path: 'docs/changes/CIS-0001/proposal.md' } }], traversals: [{ from: 'node:one', type: 'uses', to: 'node:two' }], diagnostics: [] };
        if (key.startsWith('brd questions guidance --workspace ')) return {
          status: brdQuestionAnswered ? 'answered' : 'unanswered', suggestionStatus: 'missing',
          unansweredCount: brdQuestionAnswered ? 0 : 1, questions: [
            { id: 'BRD-Q-001', question: 'Who owns this outcome?', status: brdQuestionAnswered ? 'Answered' : 'Unanswered', context: [
              { id: 'BRD-Q-001-CTX-1', section: 'Stakeholders and actors', excerpt: 'The sponsor owns the outcome.' },
            ], suggestionContextIds: [], answer: brdQuestionAnswered ? 'The business sponsor owns the outcome.' : undefined },
          ], errors: [], answerDigest: brdQuestionAnswered ? 'sha256:answered' : undefined,
        };
        if (key.startsWith('technical-intent questions status') && !technicalQuestionsInitialized) return {
          status: 'missing', current: false, complete: false, questions: [],
        };
        if (key.startsWith('technical-intent questions status')) return {
          status: 'status', current: true, complete: true, answeredCount: 16, unansweredCount: 0,
          questions: [{ id: 'TI-Q-004', area: 'Architecture style', question: 'Which architecture?',
            status: 'Answered', answer: 'Modular monolith.', answeredBy: 'Andrew Spiteri' }],
        };
        return { status: 'valid', errors: 0, warnings: 0, findings: [], skills: [], standards: [] };
      },
      runForeground: async (title, args, options) => {
        foreground.push({ title, args, options });
        if (args[0] === 'definition' && args[1] === 'activate') definitionSession = false;
        if (args[0] === 'workspace' && args[1] === 'init') fs.writeFileSync(path.join(root, '.cis', 'workspace.yml'), 'authority: fixture\n');
        if (args[0] === 'brd' && args[1] === 'init') fs.writeFileSync(brd, '---\nstatus: Review Required\n---\n# BRD\n');
        if (args[0] === 'brd' && args[1] === 'questions' && args[2] === 'answer') brdQuestionAnswered = true;
        if (args[0] === 'brd' && args[1] === 'review' && args[2] === 'decide') reviewDecision = 'accepted';
        if (args[0] === 'brd' && args[1] === 'review' && args[2] === 'accept-all') reviewDecision = 'accepted';
        if (args[0] === 'brd' && args[1] === 'review' && args[2] === 'approve') reviewApproved = true;
        if (args[0] === 'technical-intent' && args[1] === 'questions' && args[2] === 'init') technicalQuestionsInitialized = true;
        return { state: 'success', exitCode: 0 };
      },
    };
    const context = { workspaceState: state(), subscriptions: [] };
    extension.activate(context, { authority, cli: fakeCli });
    await new Promise(resolve => setTimeout(resolve, 300));

    assert.equal(treeProviders.size, 6);
    assert.ok(registered.size >= 25);
    await registered.get('cis.refresh')();
    await registered.get('cis.selectAuthority')();
    await registered.get('cis.clearAuthority')();
    await registered.get('cis.open')({ file: proposal });
    await registered.get('cis.preview')({ file: proposal });
    await registered.get('cis.repoInit')();
    await registered.get('cis.repoImport')();
    fs.rmSync(path.join(root, '.cis', 'workspace.yml'));
    fs.rmSync(brd);
    await registered.get('cis.productStart')();
    await registered.get('cis.brdValidate')();
    await registered.get('cis.brdApprove')();
    await registered.get('cis.brdAgentDraft')();
    await registered.get('cis.brdAgentReview')();
    await registered.get('cis.brdAnswerQuestions')();
    const questionsPanel = panels.find(panel => panel.kind === 'cis.brdQuestions');
    assert.ok(questionsPanel);
    await questionsPanel.webview.message({ command: 'generate-suggestions', value: '' });
    aiMode = 'remote';
    await questionsPanel.webview.message({ command: 'generate-suggestions', value: '' });
    await questionsPanel.webview.message({ command: 'save-answer',
      value: JSON.stringify({ id: 'BRD-Q-001', answer: 'The business sponsor owns the outcome.' }) });
    await registered.get('cis.brdReviewRecommendations')('RUN-BRD-REVIEW');
    const recommendationPanel = panels.filter(panel => panel.kind === 'cis.recommendationReview').at(-1);
    assert.ok(recommendationPanel);
    await recommendationPanel.webview.message({ command: 'open-review', value: '' });
    await recommendationPanel.webview.message({ command: 'modify', value: 'BRD-REV-001' });
    reviewDecision = 'pending'; reviewApproved = false;
    await registered.get('cis.brdReviewRecommendations')('RUN-BRD-REVIEW');
    const batchRecommendationPanel = panels.filter(panel => panel.kind === 'cis.recommendationReview').at(-1);
    await batchRecommendationPanel.webview.message({ command: 'accept-all', value: '' });
    await registered.get('cis.technicalIntentQuestions')();
    const technicalQuestionsPanel = panels.find(panel => panel.kind === 'cis.technicalIntentQuestions');
    assert.ok(technicalQuestionsPanel);
    await technicalQuestionsPanel.webview.message({ command: 'save-answer',
      value: JSON.stringify({ id: 'TI-Q-004', answer: 'Modular monolith with explicit boundaries.' }) });
    await registered.get('cis.technicalIntentInit')();
    await registered.get('cis.technicalIntentValidate')();
    await registered.get('cis.technicalIntentApprove')();
    await registered.get('cis.solutionDesignInit')();
    await registered.get('cis.solutionDesignValidate')();
    await registered.get('cis.solutionDesignApprove')();
    await registered.get('cis.uiDirectionQuestions')();
    const uiDirectionQuestionsPanel = panels.find(panel => panel.kind === 'cis.uiDirectionQuestions');
    assert.ok(uiDirectionQuestionsPanel);
    await uiDirectionQuestionsPanel.webview.message({ command: 'open-questionnaire', value: '' });
    await uiDirectionQuestionsPanel.webview.message({ command: 'save-answer',
      value: JSON.stringify({ id: 'UI-Q-004', answer: 'Compact, readable working surfaces with clear hierarchy.' }) });
    await registered.get('cis.uiDirectionInit')();
    await registered.get('cis.uiDirectionValidate')();
    await registered.get('cis.uiDirectionApprove')();
    await registered.get('cis.definitionWizard')();
    const definitionPanel = panels.find(panel => panel.kind === 'cis.definitionWizard');
    assert.ok(definitionPanel);
    await definitionPanel.webview.message({ command: 'navigate', value: 'technical' });
    await definitionPanel.webview.message({ command: 'refresh', value: '' });
    await definitionPanel.webview.message({ command: 'save-answer',
      value: JSON.stringify({ page: 'technical', id: 'TI-Q-004', answer: 'Modular monolith with explicit boundaries.' }) });
    await definitionPanel.webview.message({ command: 'prepare', value: 'experience' });
    await definitionPanel.webview.message({ command: 'open-path', value: 'docs/specs/technical-intent-spec.md' });
    await definitionPanel.webview.message({ command: 'business-action', value: 'open-brd' });
    await definitionPanel.webview.message({ command: 'business-action', value: 'doctor' });
    await definitionPanel.webview.message({ command: 'business-action', value: 'evidence' });
    await definitionPanel.webview.message({ command: 'business-action', value: 'questions' });
    await definitionPanel.webview.message({ command: 'activate', value: '' });
    await registered.get('cis.backlogBuild')();
    await registered.get('cis.backlogValidate')();
    await registered.get('cis.backlogApprove')();
    await registered.get('cis.featureStart')();
    await registered.get('cis.featureValidate')('HLT-FR-001');
    await registered.get('cis.featureApprove')('HLT-FR-001');
    await registered.get('cis.repoDoctor')();
    const doctorPanel = panels.find(panel => panel.kind === 'cis.repositoryDoctor');
    assert.ok(doctorPanel);
    assert.match(doctorPanel.webview.html, /Routing cards are unavailable\./u);
    await doctorPanel.webview.message({ command: 'copy-fix', value: 'CIS-INDEX-001' });
    await doctorPanel.webview.message({ command: 'open-path', value: 'docs/changes/CIS-0001/proposal.md' });
    await doctorPanel.webview.message({ command: 'refresh', value: '' });
    await registered.get('cis.graphBuild')();
    await registered.get('cis.indexBuild')();
    await registered.get('cis.contextSearch')();
    const contextPanel = panels.find(panel => panel.kind === 'cis.contextDetail');
    assert.ok(contextPanel);
    await contextPanel.webview.message({ command: 'open-path', value: 'docs/changes/CIS-0001/proposal.md' });
    await contextPanel.webview.message({ command: 'related', value: 'node:one' });
    await registered.get('cis.graphRelated')();
    await registered.get('cis.filterChanges')();
    await registered.get('cis.aiStatus')();
    await registered.get('cis.agentProviders')();
    await registered.get('cis.agentDiagnose')('codex');
    await registered.get('cis.agentAuthenticate')('codex');
    await registered.get('cis.agentShow')('RUN-1');
    const runPanel = panels.find(panel => panel.kind === 'cis.runDetail');
    assert.ok(runPanel);
    await runPanel.webview.message({ command: 'resume', value: 'RUN-1' });
    await registered.get('cis.workflowStatus')();
    await registered.get('cis.testStatus')();
    await registered.get('cis.securityStatus')();
    await registered.get('cis.verificationStatus')();
    await registered.get('cis.governanceInventory')('skills');
    await registered.get('cis.governanceInventory')('standards');
    await registered.get('cis.governanceInventory')('references');
    await registered.get('cis.designReview')('CIS-0001');
    const designPanel = panels.find(panel => panel.kind === 'cis.designReview');
    if (designPanel.webview.message) await designPanel.webview.message({ command: 'approve', value: 'CIS-0001' });
    await registered.get('cis.designDecision')({ decision: 'approve', changeId: 'CIS-0001' });
    await registered.get('cis.taskTransition')('CIS-0001');
    await registered.get('cis.openTask')({ changeId: 'CIS-0001', taskId: 'WORK-100-BACKOFFICE' });
    const taskPanel = panels.find(panel => panel.kind === 'cis.taskDetail');
    assert.ok(taskPanel);
    await taskPanel.webview.message({ command: 'transition', value: 'WORK-100-BACKOFFICE' });
    const taskAgentRequest = taskPanel.webview.message({ command: 'agent', value: 'WORK-100-BACKOFFICE' });
    await new Promise(resolve => setImmediate(resolve));
    await panels.filter(panel => panel.kind === 'cis.agentRequest').at(-1).webview.message({ command: 'start', value: '' });
    await taskAgentRequest;
    await registered.get('cis.openChange')('CIS-0001');
    const changePanel = panels.find(panel => panel.kind === 'cis.changeOverview');
    assert.ok(changePanel);
    await changePanel.webview.message({ command: 'open-task', value: 'WORK-100-BACKOFFICE' });
    await changePanel.webview.message({ command: 'canonical', value: 'CIS-0001' });
    await changePanel.webview.message({ command: 'design', value: 'CIS-0001' });
    await changePanel.webview.message({ command: 'transition', value: 'CIS-0001' });
    const changeAgentRequest = changePanel.webview.message({ command: 'agent', value: 'CIS-0001' });
    await new Promise(resolve => setImmediate(resolve));
    await panels.filter(panel => panel.kind === 'cis.agentRequest').at(-1).webview.message({ command: 'start', value: '' });
    await changeAgentRequest;
    await registered.get('cis.openChangeCanonical')('CIS-0001');
    await registered.get('cis.agentCancel')('RUN-1');
    await registered.get('cis.agentRecover')({ runId: 'RUN-1' });
    await registered.get('cis.agentResume')({ cis: { runId: 'RUN-1' } });
    await workspaceEvents.folders();
    await workspaceEvents.trust();
    watchers[0].change(); watchers[0].create(); watchers[0].delete();
    await registered.get('cis.open')({ file: path.join(root, '..', 'outside.md') });

    assert.ok(queries.includes('repo doctor'));
    assert.deepEqual(clipboardWrites, ['cis index build --limit 100']);
    assert.ok(queries.includes('context search --text agent provider --limit 100'));
    assert.ok(foreground.some(item => item.args.join(' ') === 'repo init --root docs/cis --yes'));
    assert.ok(foreground.some(item => item.args.join(' ') === `repo import --workspace ${root} --source ${root} --root docs/cis --yes`));
    assert.ok(foreground.some(item => item.args.join(' ') === `graph build --workspace ${root}`));
    assert.ok(foreground.some(item => item.args.join(' ') === 'index build' && item.options.cancellable === true));
    assert.ok(foreground.some(item => item.args.join(' ') === `agent author brd --provider codex --transport app-server --actor Andrew Spiteri --reference ${reference}`));
    assert.ok(foreground.some(item => item.args.join(' ') === 'agent review brd --provider claude --transport stream-json --actor Andrew Spiteri --include-authoring-evidence'));
    assert.ok(foreground.some(item => item.args[0] === 'brd' && item.args[1] === 'questions' && item.args[2] === 'answer'));
    assert.ok(foreground.some(item => item.args[0] === 'technical-intent' && item.args[1] === 'questions'
      && item.args[2] === 'answer' && item.args.includes('Modular monolith with explicit boundaries.')));
    assert.ok(foreground.some(item => item.args[0] === 'technical-intent' && item.args[1] === 'questions'
      && item.args[2] === 'answer' && item.options.details === false));
    assert.ok(foreground.some(item => item.args[0] === 'technical-intent' && item.args[1] === 'init'));
    assert.ok(foreground.some(item => item.args[0] === 'ui-direction' && item.args[1] === 'questions'
      && item.args[2] === 'answer' && item.args.includes('Compact, readable working surfaces with clear hierarchy.')));
    assert.ok(foreground.some(item => item.args[0] === 'ui-direction' && item.args[1] === 'init'));
    assert.ok(queries.some(item => item.startsWith('definition init --workspace ')));
    assert.ok(queries.some(item => item.startsWith('definition answer --page technical --id TI-Q-004')));
    assert.ok(foreground.some(item => item.args[0] === 'definition' && item.args[1] === 'activate'));
    assert.ok(foreground.some(item => item.args.join(' ') === 'agent author feature --item HLT-FR-001 --provider codex --transport app-server --actor Andrew Spiteri'));
    assert.ok(openDialogOptions.filters['Supported evidence'].includes('docx'));
    assert.deepEqual(openDialogOptions.filters['Word documents'], ['docx']);
    assert.equal(openDialogOptions.canSelectFolders, true);
    assert.ok(foreground.some(item => item.args.includes('transition')));
    assert.ok(foreground.some(item => item.args[1] === 'cancel'));
    assert.ok(foreground.some(item => item.args[1] === 'recover'));
    assert.ok(foreground.some(item => item.args[1] === 'resume'));
    assert.ok(panels.length >= 10);
    assert.ok(opened.some(item => item.id === 'markdown.showPreview'));
    assert.ok(outputLines.some(line => line.startsWith('ERROR [extension]')));
  } finally {
    configuration.actorIdentity = '';
    vscode.window = originalWindow;
    vscode.commands = originalCommands;
    Object.assign(vscode.workspace, originalWorkspace);
    vscode.RelativePattern = originalRelativePattern;
    vscode.ViewColumn = originalViewColumn;
    vscode.StatusBarAlignment = originalStatusBarAlignment;
    vscode.env = originalEnv;
    fs.rmSync(root, { recursive: true, force: true });
  }
});
