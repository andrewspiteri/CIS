'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { compareLines, renderEditableDiff, readEditableDiff, confirmRepositoryImport, renderRepositoryImportReview, runRepositoryImport } = require('../lib/repository-import');

const plan = {
  status: 'dry-run', applied: false, mergeReviewHash: `sha256:${'a'.repeat(64)}`,
  repositories: [{ repositoryPath: '/repo', filesToCreate: ['docs/cis/README.md'], filesToUpdate: ['AGENTS.md'] }],
  fileMerges: [{ repositoryPath: '/repo', relativePath: 'AGENTS.md', currentContent: '# Our guide\n<script>unsafe()</script>',
    proposedContent: '# Our guide\n<script>unsafe()</script>\n<!-- cis:repository-guidance:start -->\nNew guidance' }],
  warnings: ['Unreadable directory <cache>'],
};

function fixture() {
  let handler; let disposed; let html; let calls = 0;
  const vscode = { ViewColumn: { Active: 1 }, window: {
    createWebviewPanel: (_kind, _title, _column, options) => {
      assert.deepEqual(options.localResourceRoots, []);
      return { webview: { set html(value) { html = value; }, onDidReceiveMessage: callback => { handler = callback; } },
        onDidDispose: callback => { disposed = callback; }, dispose: () => disposed() };
    },
  } };
  const cli = { query: async (args, options) => {
    calls++;
    assert.ok(args.includes('--dry-run'));
    assert.ok(!args.includes('--yes'));
    assert.equal(options.repository, false);
    return plan;
  } };
  return { vscode, cli, message: value => handler(value), close: () => disposed(), html: () => html, calls: () => calls };
}

const minimalPlan = { ...plan, guidanceMode: 'minimal',
  assessments: [{ repositoryPath: '/repo', inventoryComplete: true, inventory: [{ path: 'AGENTS.md' }],
    coverage: [{ title: 'Browser testing', action: 'preserve', detail: 'Related guidance found; enforcement unverified.', sources: ['tests<unsafe>.md'] }],
    findings: [{ path: 'AGENTS.md', detail: 'Routing precedence needs a separate decision.' }], limitations: ['Local topic matching only.'] }],
  previews: [{ repositoryPath: '/repo', relativePath: 'AGENTS.md', currentContent: 'Keep this.\n', proposedContent: 'Keep this.\nCIS addition.\n' }],
};

test('minimal import opens the gap report without selecting or invoking a model', async () => {
  const f = fixture();
  const queries = [];
  const pending = confirmRepositoryImport(f.vscode, { query: async args => { queries.push(args); return minimalPlan; } }, ['repo', 'import']);
  await new Promise(resolve => setImmediate(resolve));
  assert.match(f.html(), /Review minimal CIS import/u);
  assert.match(f.html(), /Preserve matching guidance; verify scope/u);
  assert.match(f.html(), /CIS setup/u);
  assert.match(f.html(), /Project guidance requirements/u);
  assert.match(f.html(), /tests&lt;unsafe&gt;\.md/u);
  assert.match(f.html(), /No model calls/u);
  assert.match(f.html(), /\+CIS addition/u);
  assert.doesNotMatch(f.html(), /<pre class="preview">[^<]*Keep this/u);
  f.message({ command: 'confirm', contents: [plan.fileMerges[0].proposedContent] });
  const confirmed = await pending;
  assert.equal(queries.length, 1);
  assert.ok(confirmed.args.includes(plan.mergeReviewHash));
  assert.equal(confirmed.fileMerges.length, 1);
});

test('minimal import with no existing entry points still requires review and carries a plan hash', async () => {
  const f = fixture();
  const pending = confirmRepositoryImport(f.vscode, { query: async () => ({ ...minimalPlan, fileMerges: [] }) }, ['repo', 'import']);
  await new Promise(resolve => setImmediate(resolve));
  assert.match(f.html(), /Gap report/u);
  f.message({ command: 'confirm', contents: [] });
  const confirmed = await pending;
  assert.equal(confirmed.fileMerges, undefined);
  assert.ok(confirmed.args.includes(plan.mergeReviewHash));
});

test('minimal import cancellation and invalid assessment never apply', async () => {
  const f = fixture();
  const pending = confirmRepositoryImport(f.vscode, { query: async () => minimalPlan }, []);
  await new Promise(resolve => setImmediate(resolve));
  f.close();
  assert.equal(await pending, undefined);
  await assert.rejects(confirmRepositoryImport(f.vscode, { query: async () => ({ ...minimalPlan, assessments: [] }) }, []), /incomplete minimal import plan/u);
});

test('import waits for valid edited contents and binds them to the reviewed proposal', async () => {
  const f = fixture();
  const args = ['repo', 'import', '--source', '/repo'];
  let settled = false;
  const result = confirmRepositoryImport(f.vscode, f.cli, args).then(value => { settled = true; return value; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(settled, false);
  f.message({ command: 'arbitrary-write' });
  assert.equal(settled, false);
  assert.match(f.html(), /Save guidance and import/u);
  f.message({ command: 'confirm' });
  f.message({ command: 'confirm', contents: [] });
  f.message({ command: 'confirm', contents: [null] });
  f.message({ command: 'confirm', contents: ['bad\0content'] });
  assert.equal(settled, false);
  const edited = '# Shorter guidance\n+ A literal Markdown list item\n';
  f.message({ command: 'confirm', contents: [edited] });
  assert.deepEqual(await result, { args: [...args, '--merge-review', plan.mergeReviewHash, '--yes'],
    fileMerges: [{ repositoryPath: '/repo', relativePath: 'AGENTS.md', content: edited }] });
  assert.equal(f.calls(), 1);
  assert.deepEqual(args, ['repo', 'import', '--source', '/repo']);
});

for (const action of ['cancel', 'close']) test(`${action} leaves the import unapplied`, async () => {
  const f = fixture();
  const result = confirmRepositoryImport(f.vscode, f.cli, ['repo', 'import']);
  await new Promise(resolve => setImmediate(resolve));
  if (action === 'close') f.close(); else f.message({ command: 'cancel' });
  f.message({ command: 'confirm', contents: ['late edit'] });
  assert.equal(await result, undefined);
  assert.equal(f.calls(), 1);
});

test('review renders one editable field and safely embeds document text with file effects and warnings', () => {
  const html = renderRepositoryImportReview(plan, 'test-nonce');
  assert.ok(!html.includes('<script>unsafe()'));
  assert.match(html, /\\u003cscript>unsafe\(\)\\u003c\/script>/u);
  assert.equal((html.match(/<textarea /gu) || []).length, 1);
  assert.ok(!html.includes('readonly'));
  assert.match(html, /Edit merged guidance/u);
  assert.match(html, /marks proposed deletions/u);
  assert.match(html, /replace its leading/u);
  assert.match(html, /Files to create: 1 · Files to update: 1/u);
  assert.match(html, /No import changes have been applied yet/u);
  assert.match(html, /Unreadable directory &lt;cache&gt;/u);
  assert.match(html, /default-src 'none'/u);
});

test('incomplete proposals cannot be confirmed', async () => {
  const f = fixture();
  await assert.rejects(confirmRepositoryImport(f.vscode, { query: async () => ({ ...plan, mergeReviewHash: undefined }) }, []), /incomplete guidance merge proposal/u);
  assert.equal(f.html(), undefined);
});

test('connected guidance accepts reviewed skills agents and prompts with explicit retirement', async () => {
  const f = fixture();
  const connected = { ...plan, fileMerges: [plan.fileMerges[0], ...[
    '.github/skills/legacy/SKILL.md', '.github/agents/docs.agent.md', '.github/prompts/register.prompt.md',
  ].map(relativePath => ({ ...plan.fileMerges[0], relativePath }))] };
  let settled = false;
  const pending = confirmRepositoryImport(f.vscode, { query: async () => connected }, ['repo', 'import'])
    .then(value => { settled = true; return value; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal((f.html().match(/class="retire-file"/gu) || []).length, 3);
  f.message({ command: 'confirm', contents: ['root', '', 'agent', 'prompt'], retirements: [true, false, false, false] });
  assert.equal(settled, false, 'entry points cannot be retired');
  f.message({ command: 'confirm', contents: ['root', '', 'agent', 'prompt'], retirements: [false, true, false, false] });
  const accepted = await pending;
  assert.equal(accepted.fileMerges[1].retire, true);
  assert.equal(accepted.fileMerges[1].content, '');
  assert.equal(accepted.fileMerges[0].retire, undefined);
  assert.match(f.html(), /original to <code>\.cis\/retired-guidance\//u);
});

test('unresolved dependency findings remain visible and escaped after model passes complete', () => {
  const html = renderRepositoryImportReview({ ...plan, fileMerges: [{ ...plan.fileMerges[0], guidanceReview: {
    status: 'complete', provider: 'codex', model: 'selected', reviewedInstructions: 10, totalInstructions: 10, completedPasses: 2,
    findings: [{ kind: 'enforcement', path: '.github/workflows/check.yml', detail: 'Inspect <old gate>; import did not migrate it.' }],
  } }] }, 'nonce');
  assert.match(html, /Needs attention before completing the migration/u);
  assert.match(html, /Inspect &lt;old gate&gt;/u);
  assert.doesNotMatch(html, /Inspect <old gate>/u);
});

test('imports without a merge still require import confirmation', async () => {
  let prompt;
  const vscode = { window: { showWarningMessage: async message => { prompt = message; return 'Import existing repository'; } } };
  const args = await confirmRepositoryImport(vscode, { query: async () => ({ ...plan, fileMerges: [] }) }, ['repo', 'import']);
  assert.deepEqual(args, { args: ['repo', 'import', '--yes'] });
  assert.match(prompt, /creates the planned guidance files/u);
});

test('line markers distinguish additions, edits, and deletions with repeated Markdown lines', () => {
  assert.deepEqual(compareLines('same\n', 'same\nnew\n'), { added: [false, true], removed: 0 });
  assert.deepEqual(compareLines('heading\nold\nfooter\n', 'heading\nnew\nfooter\n'), { added: [false, true, false], removed: 1 });
  assert.deepEqual(compareLines('same\nsame\n', 'same\n'), { added: [false], removed: 1 });
  assert.deepEqual(compareLines('a\nb\nc\na\nd\n', 'c\na\nz\nd\n'), { added: [false, false, true, false], removed: 2 });
  assert.deepEqual(compareLines('a\r\n\r\nb\r\n', 'a\n\nb\n'), { added: [false, false, false], removed: 0 });
  assert.deepEqual(compareLines('', '+ actual text\n'), { added: [true], removed: 0 });
  assert.deepEqual(compareLines('remove all\n', ''), { added: [], removed: 1 });
});

test('review preserves CRLF when the editable field supplies LF', async () => {
  const f = fixture();
  const result = confirmRepositoryImport(f.vscode, { query: async () => ({ ...plan,
    fileMerges: [{ ...plan.fileMerges[0], proposedContent: 'one\r\ntwo\r\n' }] }) }, []);
  await new Promise(resolve => setImmediate(resolve));
  f.message({ command: 'confirm', contents: ['one\n+ edited\n'] });
  assert.equal((await result).fileMerges[0].content, 'one\r\n+ edited\r\n');
});

for (const fail of [false, true]) test(`edited content is handed to the CLI and cleaned up after ${fail ? 'failure' : 'success'}`, async () => {
  let editsPath;
  const fileMerges = [{ repositoryPath: '/repo', relativePath: 'AGENTS.md', content: '# Edited\n+ literal bullet\n' }];
  const running = runRepositoryImport({ runForeground: async (_title, args, options) => {
    editsPath = args[args.indexOf('--merge-edits') + 1];
    assert.deepEqual(JSON.parse(fs.readFileSync(editsPath, 'utf8')), fileMerges);
    assert.ok(!args.includes(fileMerges[0].content));
    assert.equal(options.repository, false);
    if (fail) throw new Error('CLI rejected stale review');
    return { applied: true };
  } }, { args: ['repo', 'import', '--yes'], fileMerges });
  if (fail) await assert.rejects(running, /stale review/u);
  else assert.deepEqual(await running, { applied: true });
  assert.equal(fs.existsSync(path.dirname(editsPath)), false);
});

test('webview script submits only kept and added text from the editable diff', () => {
  const listeners = {}; const buttons = {}; const rows = [];
  const markers = { replaceChildren: (...items) => rows.splice(0, rows.length, ...items) };
  const gutter = { scrollTop: 0, querySelector: () => markers };
  const changes = {}; const next = { addEventListener: (event, callback) => { buttons.next = callback; } };
  const section = { querySelector: selector => ({ '.gutter': gutter, '.changes': changes, '.next-addition': next })[selector] };
  const editor = { value: '', scrollTop: 42, closest: () => section,
    addEventListener: (event, callback) => { listeners[event] = callback; } };
  const confirm = { dataset: { command: 'confirm' }, addEventListener: (_event, callback) => { buttons.confirm = callback; } };
  let saved; let message;
  const html = renderRepositoryImportReview(plan, 'test');
  const script = html.match(/<script nonce="test">([\s\S]*)<\/script>/u)[1];
  vm.runInNewContext(script, {
    document: { createElement: () => ({}), querySelectorAll: selector => selector === 'textarea' ? [editor] : [confirm] },
    acquireVsCodeApi: () => ({ getState: () => undefined, setState: state => { saved = state; }, postMessage: value => { message = value; } }),
    clearTimeout() {}, setTimeout() {},
  });
  assert.equal(readEditableDiff(editor.value), plan.fileMerges[0].proposedContent);
  assert.equal(rows.filter(row => row.className === 'added').length, 2);
  assert.equal(gutter.scrollTop, editor.scrollTop);
  editor.value = ' # Manually deduplicated\n-Remove this superseded instruction\n++ Keep this real plus\n';
  listeners.input(); buttons.confirm();
  assert.equal(saved.diffs[0], editor.value);
  assert.equal(message.contents[0], '# Manually deduplicated\n+ Keep this real plus\n');
});

test('unified review keeps deletion text visible but excludes it from the saved file', () => {
  const before = '# Team\nUse old tool first.\nKeep domain rules.\n';
  const after = '# Team\nUse CIS first.\nKeep domain rules.\n';
  const review = renderEditableDiff(before, after);
  assert.match(review, /-Use old tool first\./u);
  assert.match(review, /\+Use CIS first\./u);
  assert.equal(readEditableDiff(review), after);
  assert.ok(readEditableDiff(review.replace('-Use old tool', ' Use old tool')).includes('Use old tool first.'));
});

test('diff round trips Markdown prefixes, indentation, empty files and final newlines', () => {
  for (const before of ['', 'old', 'old\n', '- old\n\n+ original\n']) {
    for (const after of ['', '\n', 'new', 'new\n', '  indented\n- bullet\n+ bullet\n---\n', '+\n-\n \n'])
      assert.equal(readEditableDiff(renderEditableDiff(before, after)), after, JSON.stringify({ before, after }));
  }
});

test('review exposes escaped removal reasons, replacement evidence, and incomplete analysis', () => {
  const html = renderRepositoryImportReview({ ...plan, fileMerges: [{ ...plan.fileMerges[0], guidanceReview: {
    provider: 'local', model: '<model>', status: 'partial', warnings: ['Unreviewed <instructions> remain.'],
    removals: [{ startLine: 3, endLine: 4, kind: 'conflict', reason: 'Different <first> tool.',
      replacementPath: '.github/instructions/cis-repository.instructions.md', replacementText: 'Use <CIS> first.' }],
  } }] }, 'test');
  assert.match(html, /1 proposed instruction changes/u);
  assert.match(html, /Original lines 3–4: conflict/u);
  assert.match(html, /Different &lt;first&gt; tool/u);
  assert.match(html, /Use &lt;CIS&gt; first/u);
  assert.match(html, /Unreviewed &lt;instructions&gt; remain/u);
});

test('model choice sends only explicitly authorized guidance and binds the original content hash', async () => {
  const f = fixture(); const calls = [];
  const pendingPlan = { ...plan, fileMerges: [{ ...plan.fileMerges[0], guidanceReview: { status: 'unavailable' } }],
    guidanceProviders: [{ name: 'codex', status: 'available', isLocal: false, models: [{ name: 'strong-model' }] }] };
  f.vscode.window.showQuickPick = async choices => choices.find(choice => choice.provider === 'codex');
  f.vscode.window.showWarningMessage = async (_message, options, action) => {
    assert.match(options.detail, /AGENTS.md/u);
    assert.match(options.detail, /\/repo/u);
    return action;
  };
  const cli = { query: async args => {
    calls.push(args);
    return args.includes('--merge-model') ? plan : pendingPlan;
  } };
  const result = confirmRepositoryImport(f.vscode, cli, ['repo', 'import']);
  await new Promise(resolve => setImmediate(resolve));
  assert.ok(calls[1].includes('--allow-remote-merge'));
  assert.equal(calls[1][calls[1].indexOf('--merge-review') + 1], plan.mergeReviewHash);
  assert.equal(calls[1][calls[1].indexOf('--merge-model') + 1], 'strong-model');
  f.message({ command: 'confirm', contents: ['# Reviewed\n'] });
  const confirmed = await result;
  assert.ok(!confirmed.args.includes('--allow-remote-merge'), 'apply never reruns a model');
});

test('declining remote guidance review never invokes the model', async () => {
  const f = fixture(); let calls = 0;
  f.vscode.window.showQuickPick = async choices => choices[0];
  f.vscode.window.showWarningMessage = async () => undefined;
  const cli = { query: async args => {
    calls++;
    return { ...plan, fileMerges: [{ ...plan.fileMerges[0], guidanceReview: { status: 'unavailable' } }],
      guidanceProviders: [{ name: 'codex', status: 'available', isLocal: false, models: [{ name: 'strong-model' }] }] };
  } };
  assert.equal(await confirmRepositoryImport(f.vscode, cli, []), undefined);
  assert.equal(calls, 1);
});

for (const action of ['Continue review', 'Open incomplete proposal', undefined])
  test(`time-limited review waits for the user's decision: ${action || 'cancel'}`, async () => {
    const f = fixture(); const calls = []; let decide;
    const initial = { ...plan, remoteReviewConcurrency: 4,
      fileMerges: [{ ...plan.fileMerges[0], guidanceReview: { status: 'unavailable' } }],
      guidanceProviders: [{ name: 'codex', status: 'available', isLocal: false, models: [{ name: 'strong-model' }] }] };
    f.vscode.window.showQuickPick = async choices => choices[0];
    f.vscode.window.showWarningMessage = async (message, options, first) => {
      if (message.startsWith('Send guidance')) { assert.match(options.detail, /Up to 4 model requests/u); return first; }
      assert.match(message, /ten-minute limit/u);
      return new Promise(resolve => { decide = resolve; });
    };
    const result = confirmRepositoryImport(f.vscode, { query: async (args, options) => {
      calls.push({ args, options });
      return calls.length === 1 ? initial : { ...plan, reviewPaused: calls.length === 2 };
    } }, ['repo', 'import']);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(calls.length, 2, 'No automatic continuation after the time limit');
    assert.equal(calls[1].options.timeout, 780_000, 'Timeout must not grow with file count');
    decide(action);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(calls.length, action === 'Continue review' ? 3 : 2);
    if (action === 'Continue review') assert.deepEqual(calls[2].args, calls[1].args, 'Same authorized model and input hash');
    if (!action) assert.equal(await result, undefined);
    else { f.message({ command: 'cancel' }); assert.equal(await result, undefined); }
  });

test('remote approval lists entry points, linked instructions and reference excerpts, and confirmation saves every file', async () => {
  const f = fixture(); let detail; const calls = [];
  const multi = { ...plan, fileMerges: [plan.fileMerges[0], { ...plan.fileMerges[0], relativePath: '.github/copilot-instructions.md' },
    { ...plan.fileMerges[0], relativePath: '.github/instructions/routing.instructions.md' }]
    .map(merge => ({ ...merge, guidanceReview: { status: 'unavailable' },
      contextSources: [{ path: '.github/scripts/preflight.py', isExcerpt: true, content: 'quoted evidence' }] })),
    guidanceProviders: [{ name: 'codex', status: 'available', isLocal: false, models: [{ name: 'strong-model' }] }] };
  f.vscode.window.showQuickPick = async choices => choices[0];
  f.vscode.window.showWarningMessage = async (_message, options, action) => { detail = options.detail; return action; };
  const result = confirmRepositoryImport(f.vscode, { query: async args => { calls.push(args); return multi; } }, ['repo', 'import']);
  await new Promise(resolve => setImmediate(resolve));
  assert.match(detail, /AGENTS.md/u);
  assert.match(detail, /\.github\/copilot-instructions.md/u);
  assert.match(detail, /\.github\/scripts\/preflight.py \(excerpt\)/u);
  assert.match(detail, /\.github\/instructions\/routing.instructions.md/u);
  assert.equal((f.html().match(/<textarea /gu) || []).length, 3);
  assert.match(f.html(), /Read-only reference evidence/u);
  f.message({ command: 'confirm', contents: ['# AGENTS edit\n', '# Copilot edit\n', '# Scoped instruction edit\n'] });
  const confirmed = await result;
  assert.equal(confirmed.fileMerges.length, 3);
  assert.equal(confirmed.fileMerges[1].relativePath, '.github/copilot-instructions.md');
  assert.equal(confirmed.fileMerges[1].content, '# Copilot edit\n');
  assert.equal(calls.length, 2);
});

test('review rejects duplicate editable files and arbitrary reference edits', async () => {
  for (const fileMerges of [[plan.fileMerges[0], plan.fileMerges[0]],
    ...['.github/scripts/preflight.py', '.github/instructions/../../escape.instructions.md', '.github/instructions/cis-repository.instructions.md', '.github/instructions/CIS-repository.instructions.md']
      .map(relativePath => [{ ...plan.fileMerges[0], relativePath }])]) {
    const f = fixture();
    await assert.rejects(confirmRepositoryImport(f.vscode, { query: async () => ({ ...plan, fileMerges }) }, []), /incomplete guidance merge proposal/u);
    assert.equal(f.html(), undefined);
  }
});
