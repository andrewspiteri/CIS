'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { openFeatureStory, renderFeatureStory, chooseStoryModel } = require('../lib/feature-story');

function fixture() {
  let selectedRoot = '/product'; let disposed; let visibility; let refreshes = 0; let reveals = 0;
  const calls = [];
  const result = { status: 'missing', slug: 'referrals', storyId: 'abc', inputHash: 'hash', featureTitle: 'Referral Platform',
    definition: 'As a customer, I want to acknowledge the current privacy document.',
    story: { title: 'Privacy evidence', phase: 'mvp', requirements: ['Retain the timestamp.', 'Retain the document version.'], owners: [],
      remainingWork: 'Record acknowledgement evidence.', existingCapability: 'unknown' }, tasks: [], warnings: [], errors: [] };
  const panel = { title: '', reveal() { reveals++; }, onDidChangeViewState: fn => { visibility = fn; }, onDidDispose: fn => { disposed = fn; }, webview: { cspSource: 'test:', onDidReceiveMessage() {} } };
  const ai = { providers: [{ name: 'ollama', isAvailable: true, isLocal: true, models: [{ name: 'small' }, { name: 'large' }] },
    { name: 'claude', isAvailable: true, isLocal: false, models: [{ name: 'opus' }] }] };
  const vscode = { ViewColumn: { Active: 1 }, ProgressLocation: { Notification: 1 }, workspace: { isTrusted: true },
    window: { createWebviewPanel: () => panel, withProgress: async (_options, fn) => fn(), showQuickPick: async choices => choices[1] } };
  const cli = { query: async args => { calls.push(args); return structuredClone(args[0] === 'ai' ? ai : result); },
    runForeground: async (_title, args) => { calls.push(args); return { exitCode: 0 }; }, clearQueryCache() {} };
  const savedDrafts = new Map();
  const storage = { get: key => savedDrafts.get(key), update: async (key, value) => savedDrafts.set(key, structuredClone(value)) };
  const page = openFeatureStory(vscode, { cli, authority: { root: () => selectedRoot }, root: '/product', slug: 'referrals', storyId: 'abc', actorIdentity: async () => 'Reviewer', refresh: () => { refreshes++; }, storage });
  return { page, result, ai, cli, calls, vscode, storage, savedDrafts, close: () => disposed(), show: () => visibility({ webviewPanel: { visible: true } }), reveals: () => reveals, changeAuthority: () => { selectedRoot = '/elsewhere'; }, refreshes: () => refreshes };
}

test('story screen loads its own definition and acceptance criteria without reopening a wizard', async () => {
  const f = fixture(); await f.page.ready;
  assert.deepEqual(f.calls[0], ['brd', 'feature', 'wizard', 'story', 'status', '--workspace', '/product', '--slug', 'referrals', '--story', 'abc']);
  const html = f.page.panel.webview.html;
  assert.match(html, /As a customer/u); assert.match(html, /Acceptance criteria/u);
  assert.match(html, /Retain the timestamp/u); assert.match(html, /Retain the document version/u);
  assert.match(html, /Generate task breakdown/u);
  assert.doesNotMatch(html, /cis.featureWizard|Product definition wizard/u);
  assert.equal(f.page.panel.title, 'Privacy evidence');
});

test('generated tasks show dependencies, repository links and completion criteria and stay on the story', async () => {
  const f = fixture(); await f.page.ready;
  f.result.status = 'current';
  f.result.tasks = [{ id: 'T1', title: '<script>task</script>', description: 'Implement acknowledgement.', repositoryIds: ['api', 'ui'], requirementNumbers: [1, 2],
    acceptanceCriteria: ['Verify the stored timestamp and version.'], dependsOn: [] }];
  await f.page.action('generate');
  assert.deepEqual(f.calls[2].slice(-2), ['--expected-input-hash', 'hash']);
  assert.ok(f.calls[2].includes('large'));
  assert.ok(!f.calls[2].includes('--allow-remote'));
  assert.equal(f.refreshes(), 1);
  const html = f.page.panel.webview.html;
  assert.match(html, /Task completion criteria/u); assert.match(html, /Verify the stored timestamp/u);
  assert.match(html, /api, ui/u); assert.match(html, /href="#criterion-2"/u);
  assert.match(html, /have not been approved or executed/u);
  assert.match(html, /&lt;script&gt;task/u); assert.doesNotMatch(html, /<script>task/u);
});

test('approval and task detail actions carry exact plan revisions and verified evidence', async () => {
  const f = fixture(); await f.page.ready;
  f.result.status = 'current'; f.result.planHash = 'plan-hash'; f.result.revision = 'none'; f.result.planState = 'Proposed';
  f.result.tasks = [{ id: 'T1', title: 'Record consent', description: 'Save evidence.', repositoryIds: [], requirementNumbers: [1], acceptanceCriteria: ['Check timestamp.'], dependsOn: [] }];
  f.result.taskProgress = [{ id: 'T1', status: 'Proposed', canStart: false, canComplete: false }];
  await f.page.action('refresh');
  assert.match(f.page.panel.webview.html, /Approve task plan/u);
  f.vscode.window.showInputBox = async () => 'Reviewed scope and dependencies';
  await f.page.action('approve');
  const approval = f.calls.find(args => args[4] === 'approve');
  assert.equal(approval[approval.indexOf('--expected-plan-hash') + 1], 'plan-hash');
  assert.equal(approval[approval.indexOf('--expected-revision') + 1], 'none');
  assert.ok(approval.includes('Reviewer'));
  f.result.planState = 'Approved'; f.result.approvedBy = 'Reviewer'; f.result.revision = 'approved-revision';
  f.result.taskProgress = [{ id: 'T1', status: 'Ready', canStart: true, canExecute: true, canComplete: false }];
  f.result.executionPlan = { hash: 'execution-hash', complexity: 'Medium', reasons: ['Several criteria.'],
    implementation: { provider: 'codex', model: 'gpt-6-sol', isLocal: false }, review: { provider: 'codex', model: 'gpt-6-astra', isLocal: false } };
  await f.page.reveal('T1');
  assert.match(f.page.panel.webview.html, /Back to story and plan/u);
  assert.match(f.page.panel.webview.html, /automatically selected models/u);
  assert.match(f.page.panel.webview.html, /data-command="start" data-value="T1" >/u);
  f.vscode.window.showWarningMessage = async (_title, options) => {
    assert.match(options.detail, /gpt-6-sol/u); assert.match(options.detail, /gpt-6-astra/u); return 'Start implementation and review';
  };
  await f.page.action('start', 'T1');
  const executed = f.calls.find(args => args[4] === 'execute');
  assert.ok(executed.includes('approved-revision')); assert.ok(executed.includes('execution-hash')); assert.ok(executed.includes('--allow-remote'));
  assert.equal(f.calls.some(args => args[4] === 'start'), false);
  f.result.taskProgress = [{ id: 'T1', status: 'InProgress', canStart: false, canComplete: true }];
  await f.page.action('refresh');
  f.vscode.window.showInputBox = async () => 'Timestamp test passed';
  f.vscode.window.showWarningMessage = async () => undefined;
  await f.page.action('complete', 'T1');
  assert.equal(f.calls.some(args => args[4] === 'complete'), false);
  f.vscode.window.showWarningMessage = async () => 'All criteria verified';
  await f.page.action('complete', 'T1');
  const complete = f.calls.find(args => args[4] === 'complete');
  assert.ok(complete.includes('--criteria-verified')); assert.ok(complete.includes('Timestamp test passed'));
});

test('blocked tasks and cancelled or stale-authority approvals do not mutate the plan', async () => {
  const f = fixture(); await f.page.ready;
  f.result.planHash = 'plan'; f.result.revision = 'none';
  await f.page.action('refresh');
  await f.page.action('start', 'T2');
  assert.equal(f.calls.some(args => args[4] === 'start'), false);
  f.vscode.window.showInputBox = async () => undefined;
  await f.page.action('approve');
  assert.equal(f.calls.some(args => args[4] === 'approve'), false);
  f.vscode.window.showInputBox = async () => { f.changeAuthority(); return 'Reviewed'; };
  await f.page.action('approve');
  assert.equal(f.calls.some(args => args[4] === 'approve'), false);
});

test('execution confirmation cancellation and authority changes never launch an agent', async () => {
  for (const mode of ['cancel', 'authority']) {
    const f = fixture(); await f.page.ready;
    Object.assign(f.result, { planHash: 'plan', revision: 'approved', planState: 'Approved',
      tasks: [{ id: 'T1', title: 'Task', description: 'Do work', repositoryIds: [], requirementNumbers: [1], acceptanceCriteria: ['Done'], dependsOn: [] }],
      taskProgress: [{ id: 'T1', status: 'InProgress', canExecute: true, canComplete: false }],
      executionPlan: { hash: 'execution', complexity: 'Low', implementation: { provider: 'codex', model: 'gpt-6-luna' }, review: { provider: 'codex', model: 'gpt-6-astra' } } });
    await f.page.reveal('T1');
    f.vscode.window.showWarningMessage = async () => {
      if (mode === 'authority') { f.changeAuthority(); return 'Start implementation and review'; }
      return undefined;
    };
    await f.page.action('start', 'T1');
    assert.ok(f.calls.some(args => args[4] === 'execution-plan'));
    assert.ok(!f.calls.some(args => args[4] === 'execute'));
  }
});

test('failed independent review is reloaded after execution and leaves completion disabled', async () => {
  const f = fixture(); await f.page.ready;
  const selection = { hash: 'execution', complexity: 'High', implementation: { provider: 'codex', model: 'gpt-6-astra' }, review: { provider: 'codex', model: 'gpt-6-sol' } };
  Object.assign(f.result, { planHash: 'plan', revision: 'approved', planState: 'Approved', executionPlan: selection,
    tasks: [{ id: 'T1', title: 'Task', description: 'Do work', repositoryIds: [], requirementNumbers: [1], acceptanceCriteria: ['Done'], dependsOn: [] }],
    taskProgress: [{ id: 'T1', status: 'InProgress', canExecute: true, canComplete: false }] });
  await f.page.reveal('T1');
  f.vscode.window.showWarningMessage = async () => 'Start implementation and review';
  f.cli.runForeground = async () => {
    f.result.taskProgress[0].execution = { status: 'changes-requested', selection, applied: false, findings: ['Criterion not met.'], runs: [], errors: [] };
    throw new Error('Review requires changes');
  };
  await f.page.action('start', 'T1');
  assert.match(f.page.panel.webview.html, /Criterion not met/u);
  assert.match(f.page.panel.webview.html, /data-command="complete" data-value="T1" disabled/u);
  assert.match(f.page.panel.webview.html, /Retry implementation and review/u);
  assert.ok(f.refreshes() > 0);
  assert.ok(f.reveals() > 0);
});

test('running tasks refresh until an outcome arrives and refresh again when the panel becomes visible', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const f = fixture();
  await f.page.ready;
  f.result.taskProgress = [{ id: 'T1', execution: { status: 'running' } }];
  await f.page.action('refresh');
  f.result.taskProgress[0].execution.status = 'changes-requested';
  t.mock.timers.tick(5000); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.page.model.result.taskProgress[0].execution.status, 'changes-requested');
  const calls = f.calls.length;
  t.mock.timers.tick(15000); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.length, calls);
  f.show(); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.length, calls + 1);
  f.close(); f.show(); t.mock.timers.tick(15000);
  assert.equal(f.calls.length, calls + 1);
});

test('task outcome shows the latest work summary first, retains correction history, and escapes all evidence', () => {
  const task = { id: 'T1', title: 'Inventory', description: 'Investigate', repositoryIds: ['api'], dependsOn: [], acceptanceCriteria: ['Reviewed inventory'], requirementNumbers: [1] };
  const run = { repositoryId: 'api', stage: 'Implementation', round: 1, summary: 'Original inventory', validations: ['Links checked'], changedFiles: ['report.md'] };
  const execution = { status: 'reviewed', applied: true, selection: { complexity: 'High' }, findings: [], runs: [run, { ...run, round: 2, summary: 'Corrected <script>inventory</script>' }], changedFiles: ['api/report.md'] };
  const model = { taskId: 'T1', result: { featureTitle: 'Feature', story: { title: 'Story', requirements: ['Inventory'] }, tasks: [task],
    taskProgress: [{ id: 'T1', status: 'Complete', evidence: 'Checked', execution }] } };
  const html = renderFeatureStory({ cspSource: 'test:' }, model, 'test-nonce');
  assert.ok(html.indexOf('Work summary') < html.indexOf('Work to do'));
  assert.ok(html.indexOf('Corrected &lt;script&gt;') < html.indexOf('Original inventory'));
  assert.match(html, /round 2/u); assert.match(html, /Applied files/u); assert.match(html, /completion was recorded/u);
  assert.doesNotMatch(html, /<script>inventory|Verify the outcome before recording completion/u);
  execution.status = 'changes-requested'; execution.applied = false;
  const pending = renderFeatureStory({ cspSource: 'test:' }, model, 'test-nonce');
  assert.match(pending, /Candidate files — not applied/u); assert.match(pending, /Review requires changes/u);
});

async function feedbackFixture() {
  const f = fixture(); await f.page.ready;
  Object.assign(f.result, { status: 'current', planState: 'Approved', planHash: 'plan', revision: 'review-revision', approvedBy: 'Reviewer',
    tasks: [{ id: 'T1', title: 'Inventory', description: 'Investigate', repositoryIds: [], dependsOn: [], acceptanceCriteria: ['Reviewed inventory'], requirementNumbers: [1] }],
    taskProgress: [{ id: 'T1', status: 'InProgress', canExecute: true, execution: { status: 'changes-requested', applied: false, findings: ['Choose the owner.'], runs: [] },
      reviewFeedback: [{ id: 'finding-1', finding: 'Choose the owner.', answer: null, suggestedAnswer: 'Use the existing backend.', suggestionReason: 'The backend owns existing maintenance.', suggestionModel: 'local / strong' }] }] });
  await f.page.reveal('T1'); return f;
}

test('review suggestions require explicit use and save; answers bind the exact review and reach the saved view', async () => {
  const f = await feedbackFixture();
  const html = f.page.panel.webview.html;
  assert.match(html, /checks whether retained implementation can be reused/u);
  assert.doesNotMatch(html, /Start this task to perform the work/u);
  assert.match(html, /Supporting source excerpt/u); assert.match(html, /Use suggested answer/u);
  assert.match(html, /data-finding="finding-1"[^>]*><\/textarea>/u);
  const script = [...html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/gu)].at(-1)[1];
  assert.doesNotThrow(() => new Function(script));
  await f.page.action('use-feedback', JSON.stringify({ taskId: 'T1', id: 'finding-1' }));
  assert.equal(f.page.model.feedbackDrafts.T1['finding-1'], 'Use the existing backend.');
  assert.equal(f.calls.some(args => args[4] === 'feedback-save'), false);
  await f.page.action('start', 'T1');
  assert.match(f.page.model.error, /Save your review answers/u);
  assert.equal(f.calls.some(args => args[4] === 'execution-plan'), false);
  const query = f.cli.query; let temporary;
  f.cli.query = async args => {
    if (args[4] !== 'feedback-save') return query(args);
    temporary = args[args.indexOf('--input') + 1];
    const request = JSON.parse(fs.readFileSync(temporary, 'utf8'));
    assert.equal(request.expectedRevision, 'review-revision'); assert.equal(request.expectedPlanHash, 'plan');
    assert.equal(request.actor, 'Reviewer'); assert.equal(request.answers['finding-1'], 'Use the existing backend.');
    f.result.taskProgress[0].reviewFeedback[0].answer = request.answers['finding-1'];
    f.result.taskProgress[0].reviewFeedback[0].actor = 'Reviewer'; f.result.revision = 'saved-revision';
    return structuredClone(f.result);
  };
  await f.page.action('save-feedback', 'T1');
  assert.equal(f.page.model.error, undefined); assert.equal(f.page.model.feedbackDrafts.T1, undefined);
  assert.equal(fs.existsSync(temporary), false);
  assert.match(f.page.panel.webview.html, /Saved by Reviewer/u);
  assert.match(f.page.panel.webview.html, /findings remain open/u);
});

test('failed feedback saves preserve drafts through refresh and reopening', async () => {
  const f = await feedbackFixture();
  await f.page.action('remember-feedback', JSON.stringify({ taskId: 'T1', answers: { 'finding-1': 'My edited answer\nWith details.' } }));
  const query = f.cli.query;
  f.cli.query = args => args[4] === 'feedback-save' ? Promise.resolve({ errors: ['The review changed.'] }) : query(args);
  await f.page.action('save-feedback', 'T1');
  assert.match(f.page.model.error, /review changed/u);
  await f.page.action('refresh');
  assert.match(f.page.panel.webview.html, /My edited answer/u);
  f.close();
  const reopened = openFeatureStory(f.vscode, { cli: f.cli, authority: { root: () => '/product' }, root: '/product', slug: 'referrals', storyId: 'abc', taskId: 'T1', storage: f.storage });
  await reopened.ready;
  assert.match(reopened.panel.webview.html, /My edited answer/u);
});

test('review answer generation selects a model without saving or executing anything', async () => {
  const f = await feedbackFixture();
  await f.page.action('suggest-feedback', 'T1');
  const call = f.calls.find(args => args[4] === 'feedback-suggest');
  assert.ok(call.includes('large')); assert.ok(call.includes('review-revision'));
  assert.equal(f.calls.some(args => ['feedback-save', 'execute', 'complete'].includes(args[4])), false);
  assert.equal(f.page.model.result.taskProgress[0].reviewFeedback[0].answer, null);
  assert.match(f.page.panel.webview.html, /Suggestions are ready/u);
});

test('an older executable cannot silently hide the response fields for recorded findings', async () => {
  const f = await feedbackFixture();
  delete f.result.taskProgress[0].reviewFeedback;
  await f.page.action('refresh');
  assert.match(f.page.panel.webview.html, /Review answers are unavailable/u);
  assert.match(f.page.panel.webview.html, /workspace setting can override the user setting/u);
  assert.match(f.page.panel.webview.html, /Choose the owner/u);
  const commands = [];
  f.vscode.commands = { executeCommand: async (...args) => commands.push(args) };
  await f.page.action('executable-settings');
  assert.deepEqual(commands, [['workbench.action.openSettings', '@id:cis.executablePath']]);
  assert.equal(f.calls.some(args => args[4] === 'execute'), false);
});

test('failed generation retains the definition and duplicate generation is prevented', async () => {
  const f = fixture(); await f.page.ready;
  let finish; let calls = 0;
  f.cli.query = async args => { if (args[0] === 'ai') return f.ai; calls++; return new Promise(resolve => { finish = resolve; }); };
  const running = f.page.action('generate');
  await f.page.action('generate'); await new Promise(resolve => setImmediate(resolve)); assert.equal(calls, 1);
  finish({ errors: ['Local model unavailable'] }); await running;
  assert.match(f.page.panel.webview.html, /Local model unavailable/u);
  assert.match(f.page.panel.webview.html, /Retain the timestamp/u);
  assert.equal(f.page.model.busy, false);
});

test('stronger remote models require explicit context permission; cancellation never generates', async () => {
  for (const approved of [false, true]) {
    const f = fixture(); await f.page.ready;
    f.vscode.window.showQuickPick = async choices => choices.find(item => item.model === 'opus');
    f.vscode.window.showWarningMessage = async (_title, options) => {
      assert.match(options.detail, /selected code excerpts/u);
      return approved ? 'Send context and generate' : undefined;
    };
    await f.page.action('generate');
    const prepare = f.calls.find(args => args[4] === 'prepare');
    if (approved) { assert.ok(prepare.includes('--allow-remote')); assert.ok(prepare.includes('opus')); }
    else assert.equal(prepare, undefined);
  }
});

test('cancelling model selection preserves existing tasks and changing authority cancels remote selection', async () => {
  const f = fixture(); await f.page.ready;
  f.vscode.window.showQuickPick = async () => undefined;
  await f.page.action('generate'); assert.equal(f.calls.length, 2); assert.equal(f.page.model.error, undefined);
  f.vscode.window.showQuickPick = async choices => { f.changeAuthority(); return choices[2]; };
  f.vscode.window.showWarningMessage = () => assert.fail('changed authority must not send or request permission for story context');
  await f.page.action('generate'); assert.match(f.page.model.error, /product changed/u);
});

test('an unavailable model never generates tasks', async () => {
  const f = fixture(); await f.page.ready;
  f.ai.providers[0].isAvailable = false;
  await f.page.action('generate');
  assert.match(f.page.model.error, /unavailable/u);
  assert.equal(f.calls.filter(args => args[4] === 'prepare').length, 0);
});

test('changed authority, trust and disposed panels cannot generate tasks', async () => {
  for (const mode of ['authority', 'trust', 'disposed']) {
    const f = fixture(); await f.page.ready;
    if (mode === 'authority') f.changeAuthority();
    if (mode === 'trust') f.vscode.workspace.isTrusted = false;
    if (mode === 'disposed') f.close();
    await f.page.action('generate'); assert.equal(f.calls.length, 1);
  }
});

test('stale plans require regeneration and excluded stories cannot generate delivery tasks', () => {
  const model = { result: { status: 'stale', story: { title: 'Story', requirements: [], reviewCurrent: true,
    review: { owners: [], plan: 'Excluded', treatment: 'out-of-scope' } }, tasks: [] } };
  const html = renderFeatureStory({ cspSource: 'test:' }, model, 'test-nonce');
  assert.match(html, /supporting evidence changed/u);
  assert.match(html, /data-command="generate" disabled/u);
});
