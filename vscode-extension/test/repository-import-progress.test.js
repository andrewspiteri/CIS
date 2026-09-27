'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const { PassThrough } = require('node:stream');
const { CisCli } = require('../lib/cis-cli');
const { openGuidanceReviewProgress, renderGuidanceReviewProgress } = require('../lib/repository-import-progress');

const plan = { fileMerges: [{ repositoryPath: '/repo', relativePath: 'AGENTS.md',
  guidanceSources: [{ path: 'AGENTS.md' }, { path: '.github/instructions/cis-repository.instructions.md' }] }] };
function fixture(input = plan) {
  const updates = []; let message; let dispose;
  const panel = { webview: { html: '', postMessage: value => updates.push(structuredClone(value)),
    onDidReceiveMessage: handler => { message = handler; } }, onDidDispose: handler => { dispose = handler; } };
  const vscode = { ViewColumn: { Active: 1 }, window: { createWebviewPanel: (_id, title, _column, options) => {
    assert.equal(title, 'Guidance merge progress'); assert.equal(options.retainContextWhenHidden, true); return panel;
  } } };
  const progress = openGuidanceReviewProgress(vscode, input, { label: 'codex / strong-model', remote: true });
  return { progress, panel, updates, ready: () => message({ command: 'ready' }), dispose: () => dispose() };
}

test('progress tab opens queued, streams stages, and keeps a completed coverage summary', () => {
  const f = fixture();
  assert.match(f.panel.webview.html, /What is happening now/u);
  f.ready(); assert.equal(f.updates.at(-1).model.state, 'queued');
  f.progress.onStarted();
  f.progress.onStderrLine('[guidance-review] Pass 1 of 2: Comparing every instruction.');
  f.progress.onStderrLine('[telemetry] unrelated details');
  assert.equal(f.updates.at(-1).model.current, 'Pass 1 of 2: Comparing every instruction.');
  f.progress.finish({ fileMerges: [{ ...plan.fileMerges[0], guidanceReview: { status: 'complete', completedPasses: 2,
    totalInstructions: 260, reviewedInstructions: 260, removals: [{}, {}] } }] });
  const finished = f.updates.at(-1).model;
  assert.equal(finished.state, 'complete'); assert.ok(finished.finishedAt);
  assert.ok(finished.log.some(item => item.message.includes('260 of 260 instruction blocks reviewed; 2 proposed changes')));
  assert.match(finished.current, /Review it before importing/u);
});

test('partial and failed reviews stay distinct from a completed proposal', () => {
  const f = fixture();
  f.progress.finish({ fileMerges: [{ ...plan.fileMerges[0], guidanceReview: { status: 'partial', completedPasses: 1,
    warnings: ['Audit response was incomplete.'] } }] });
  assert.equal(f.updates.at(-1).model.state, 'incomplete');
  assert.ok(f.updates.at(-1).model.log.some(item => item.message.includes('Audit response was incomplete')));
  f.progress.fail(new Error('provider failed; token=private-value'));
  assert.equal(f.updates.at(-1).model.state, 'failed');
  assert.ok(!f.updates.at(-1).model.current.includes('private-value'));
});

test('closing progress never interrupts review or writes into a disposed panel', () => {
  const f = fixture(); f.dispose();
  f.progress.onStarted(); f.progress.onStderrLine('[guidance-review] Pass 2 of 2: Checking.'); f.progress.fail(new Error('Stopped'));
  assert.equal(f.updates.length, 0);
});

test('time-limited reviews show a paused state and the supported resume action', () => {
  const f = fixture();
  f.progress.onStderrLine('[guidance-review] ' + JSON.stringify({ kind: 'review-start', concurrency: 4, message: 'Four requests.' }));
  f.progress.finish({ reviewPaused: true, fileMerges: [{ ...plan.fileMerges[0], guidanceReview: { status: 'paused', completedPasses: 1 } }] });
  const model = f.updates.at(-1).model;
  assert.equal(model.concurrency, 4);
  assert.equal(model.state, 'paused');
  assert.match(model.current, /Continue the review/u);
  assert.equal(model.repositories[0].state, 'incomplete');
});

test('interleaved file stages retain independent status and timers with incomplete results distinct', () => {
  const input = { fileMerges: ['AGENTS.md', '.github/copilot-instructions.md', '.github/instructions/routing.instructions.md']
    .map(relativePath => ({ ...plan.fileMerges[0], relativePath })) };
  const f = fixture(input);
  const event = (kind, index, message, reviewStatus) => f.progress.onStderrLine('[guidance-review] '
    + JSON.stringify({ kind, index, total: 3, concurrency: 3, message, reviewStatus }));
  f.progress.onStarted();
  event('review-start', -1, 'Reviewing three files concurrently.');
  event('file-start', 0, 'Preparing.'); event('file-start', 1, 'Preparing.');
  event('file-progress', 0, 'Pass 2 of 2: Checking.');
  event('file-progress', 1, 'Pass 1 of 2: Comparing.');
  let model = f.updates.at(-1).model;
  assert.equal(model.concurrency, 3);
  assert.equal(model.repositories[0].message, 'Pass 2 of 2: Checking.');
  assert.equal(model.repositories[1].message, 'Pass 1 of 2: Comparing.');
  assert.equal(model.repositories[2].state, 'queued');
  assert.ok(model.repositories[0].startedAt);
  assert.match(model.current, /File 2 of 3 — \.github\/copilot-instructions.md/u);
  event('file-finish', 1, 'Only one pass validated.', 'partial');
  event('file-finish', 0, 'Both passes validated.', 'complete');
  model = f.updates.at(-1).model;
  assert.equal(model.repositories[0].state, 'complete');
  assert.equal(model.repositories[1].state, 'incomplete');
  assert.ok(model.repositories[1].finishedAt);
  const finishedAt = model.repositories[0].finishedAt;
  event('file-progress', 2, 'Pass 1 of 2: Comparing.');
  f.progress.fail(new Error('Command stopped'));
  model = f.updates.at(-1).model;
  assert.equal(model.repositories[0].finishedAt, finishedAt);
  assert.equal(model.repositories[0].state, 'complete');
  assert.equal(model.repositories[1].state, 'incomplete');
  assert.equal(model.repositories[2].state, 'failed');
});

test('malformed structured progress cannot change a file or inject markup', () => {
  const f = fixture(); f.progress.onStarted();
  for (const event of [{ kind: 'file-finish', index: -1, message: 'fake', reviewStatus: 'complete' },
    { kind: 'file-progress', index: 0, message: { html: '<script>' } },
    { kind: 'review-start', concurrency: 300, message: 'Invalid count.' }])
    assert.doesNotThrow(() => f.progress.onStderrLine('[guidance-review] ' + JSON.stringify(event)));
  assert.equal(f.updates.at(-1).model.repositories[0].state, 'queued');
  assert.equal(f.updates.at(-1).model.concurrency, 1);
});

test('webview updates text without replacing the page and escapes supplied content', () => {
  const model = { state: 'queued', startedAt: Date.now() - 2000, finishedAt: null, model: '<model>', location: 'Local',
    repositories: [{ path: '<path>', file: 'AGENTS.md', sources: ['<source>'] }], current: '</script><unsafe>', log: [] };
  const html = renderGuidanceReviewProgress(model, 'test');
  assert.match(html, /&lt;model&gt;/u); assert.match(html, /&lt;path&gt;/u);
  assert.ok(!html.includes('</script><unsafe>'));
  const elements = Object.fromEntries(['elapsed', 'status', 'current', 'activity', 'file-count', 'review-schedule',
    'file-time-0', 'file-status-0', 'file-message-0'].map(id => [id, { textContent: '', dataset: {} }]));
  let receive; let tick; let ready;
  vm.runInNewContext(html.match(/<script nonce="test">([\s\S]*)<\/script>/u)[1], {
    document: { getElementById: id => elements[id] },
    window: { addEventListener: (_event, callback) => { receive = callback; } },
    acquireVsCodeApi: () => ({ postMessage: value => { ready = value; } }), setInterval: callback => { tick = callback; },
  });
  assert.equal(ready.command, 'ready'); assert.equal(elements.status.textContent, 'Queued');
  receive({ data: { command: 'progress', model: { ...model, state: 'complete', finishedAt: model.startedAt + 65000,
    repositories: [{ ...model.repositories[0], state: 'complete', startedAt: model.startedAt + 5000, finishedAt: model.startedAt + 60000,
      message: 'Both passes validated.' }],
    current: '<safe plain text>', log: [{ at: model.startedAt + 65000, message: 'Both passes validated.' }] } } });
  tick();
  assert.equal(elements.elapsed.textContent, '1m 5s');
  assert.equal(elements.current.textContent, '<safe plain text>');
  assert.equal(elements.status.textContent, 'Proposal ready');
  assert.equal(elements['file-count'].textContent, '1 of 1 files finished');
  assert.equal(elements['file-time-0'].textContent, 'File elapsed: 0m 55s');
  assert.equal(elements['file-message-0'].textContent, 'Both passes validated.');
  assert.match(elements.activity.textContent, /Both passes validated/u);
});

test('CLI query streams complete UTF-8 stderr lines before the JSON result, including a final partial line', async () => {
  const stderr = new PassThrough(); let complete; let started = false; const lines = [];
  const vscode = { workspace: { isTrusted: true, getConfiguration: () => ({ get: () => 'cis' }) } };
  const cli = new CisCli(vscode, { appendLine() {} }, { root: () => '/repo' }, {
    execFile: (_exe, _args, _options, callback) => { complete = callback; return { stderr }; },
  });
  const result = cli.query(['repo', 'import', '--dry-run'], { repository: false,
    onStarted: () => { started = true; }, onStderrLine: line => lines.push(line) });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(started, true);
  const first = Buffer.from('[guidance-review] Révision\n');
  const cut = first.indexOf(0xc3) + 1;
  stderr.write(first.subarray(0, cut)); stderr.write(first.subarray(cut));
  stderr.write('token=private'); stderr.write('-value\n[guidance-review] Final update');
  assert.equal(lines[0], '[guidance-review] Révision');
  assert.equal(lines[1], 'token=[REDACTED]');
  complete(null, '{"status":"dry-run"}', '');
  assert.equal((await result).status, 'dry-run');
  assert.equal(lines[2], '[guidance-review] Final update');
});

test('streamed progress retains the end of long reviews in the output channel without duplication', async () => {
  const stderr = new PassThrough(); let complete; const output = [];
  const vscode = { workspace: { isTrusted: true, getConfiguration: () => ({ get: () => 'cis' }) } };
  const cli = new CisCli(vscode, { appendLine: line => output.push(line) }, { root: () => '/repo' }, {
    execFile: (_exe, _args, _options, callback) => { complete = callback; return { stderr }; },
  });
  const result = cli.query(['repo', 'import', '--dry-run'], { repository: false, onStderrLine() {} });
  await new Promise(resolve => setImmediate(resolve));
  const events = Array.from({ length: 500 }, (_, index) => '[guidance-review] ' + JSON.stringify({ elapsedMs: index * 1000, message: 'Event ' + index })).join('\n') + '\n';
  assert.ok(events.length > 16_384);
  stderr.write(events);
  complete(null, '{"status":"dry-run"}', events);
  await result;
  assert.equal(output.filter(line => line.startsWith('[guidance-review]')).length, 500);
  assert.ok(output.some(line => line.includes('Event 499')));
  assert.ok(output.every(line => !line.includes('output truncated')));
});
