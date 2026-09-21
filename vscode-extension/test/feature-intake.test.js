'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const { openFeatureIntake, renderFeatureIntake } = require('../lib/feature-intake');
const { STEPS } = require('../lib/feature-wizard');

function fixture() {
  const root = path.resolve('feature-authority');
  const calls = []; const executed = []; const inputs = [];
  let selectedRoot = root; let refreshes = 0;
  const vscode = { workspace: { isTrusted: true }, ViewColumn: { Active: 1 }, ProgressLocation: { Notification: 1 },
    Uri: { file: fsPath => ({ fsPath }) }, commands: { executeCommand: (...args) => executed.push(args) },
    window: { createWebviewPanel: () => ({ onDidDispose() {}, webview: { cspSource: 'vscode-webview:', onDidReceiveMessage() {} } }),
      showOpenDialog: async () => undefined, showInputBox: async () => undefined,
      withProgress: async (_options, work) => work({ report() {} }) } };
  const plan = { title: 'Referrals', slug: 'referrals', repositoryMode: 'new', repositoryPath: path.resolve('referrals'), documentationRoot: 'docs/cis',
    requestPath: 'docs/cis/specs/feature-requests/referrals/request.md', sourcePath: '.cis/inputs/features/referrals/source.md', integrationRepositories: ['backend'],
    openDecisions: ['Who owns support?'], sourceHash: 'sha256:old', planHash: 'sha256:preview' };
  const wizard = { plan, revision: 'revision-1', baselineCurrent: true, reviewed: false, errors: [], pages: STEPS.map(([id, title]) => ({ id, title,
    status: id === 'foundation' ? 'Complete' : 'Needs attention', complete: id === 'foundation', attention: id === 'foundation' ? [] : ['Review and save this page.'],
    fields: id === 'foundation' ? [] : [{ id: 'summary', label: 'Describe the feature changes.', suggestedAnswer: 'Suggested from source', answer: null, required: true }], documents: [] })) };
  const savedState = new Map(); const storage = { get: key => savedState.get(key), update: async (key, value) => savedState.set(key, structuredClone(value)) };
  const cli = { query: async args => {
    calls.push(args);
    if (args[0] === 'change') return { changes: [] };
    if (args[0] === 'repo') return { workspace: { repositories: [{ id: 'authority', role: 'authority' }, { id: 'backend', role: 'participant', participation: 'owned' }] } };
    if (args[2] === 'wizard') {
      if (args[3] === 'list') return { requests: [] };
      if (args[3] === 'status') return structuredClone(wizard);
      if (args[3] === 'screens') return { status: 'missing', screens: [], errors: [] };
      if (args[3] === 'architecture') return { status: 'missing', diagrams: [], errors: [] };
      if (args[3] === 'reimport') {
        const update = { slug: 'referrals', title: 'Referrals', incomingPath: path.resolve('updated.md'),
          previousSourcePath: plan.sourcePath, sourcePath: '.cis/inputs/features/referrals/revisions/new/source.md',
          previousRequestPath: '.cis/inputs/features/referrals/history/old.md', planHash: 'sha256:reimport-preview',
          previousBytes: 100, incomingBytes: 200, addedDecisions: ['New question?'], removedDecisions: ['Removed question?'],
          retainedDecisionAnswers: 1, pagesRequiringReview: ['Business definition', 'Technical direction', 'Final review'] };
        if (args.includes('--yes')) {
          wizard.plan = { ...plan, sourcePath: update.sourcePath, sourceHash: 'sha256:updated' };
          wizard.sourceHistory = [{ title: 'Previous BRD', path: plan.sourcePath, exists: true }];
          wizard.revision = 'revision-updated';
        }
        return { status: args.includes('--yes') ? 'updated' : 'preview', plan: update, errors: [], applied: args.includes('--yes') };
      }
      const file = args[args.indexOf('--input') + 1]; const answer = JSON.parse(fs.readFileSync(file, 'utf8'));
      inputs.push({ file, draft: answer });
      const page = wizard.pages.find(page => page.id === answer.page);
      for (const field of page.fields) if (Object.hasOwn(answer.answers, field.id)) field.answer = answer.answers[field.id];
      page.complete = true; page.status = 'Reviewed'; page.attention = [];
      if (answer.repositoryWork) wizard.repositoryWork = answer.repositoryWork;
      wizard.revision = 'revision-2'; return structuredClone(wizard);
    }
    if (args[0] === 'graph') return {};
    const file = args[args.indexOf('--input') + 1]; inputs.push({ file, draft: JSON.parse(fs.readFileSync(file, 'utf8')) });
    return args.includes('--dry-run') ? { status: 'preview', plan, warnings: [], errors: [] } : { status: 'created', plan, applied: true, errors: [], warnings: [] };
  } };
  const options = { cli, root, authority: { root: () => selectedRoot }, actorIdentity: async () => 'Reviewer', refresh: async () => { refreshes++; }, storage };
  const page = openFeatureIntake(vscode, options);
  const draft = { title: 'Referrals', slug: 'referrals', sourcePath: path.resolve('prepared.md'), repositoryMode: 'new', repositoryPath: path.resolve('referrals'), documentationRoot: 'docs/cis', integrationRepositories: ['backend'] };
  return { page, vscode, cli, calls, inputs, executed, plan, draft, wizard, options, value: JSON.stringify(draft), changeAuthority: () => { selectedRoot = path.resolve('different'); }, refreshes: () => refreshes };
}

test('architecture step loads its own diagrams, saves direction once and prevents concurrent generation', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  assert.equal(f.calls.filter(args => args[3] === 'architecture').length, 0);
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'architecture' }));
  assert.equal(f.page.model.error, undefined);
  assert.match(f.page.panel.webview.html, /Save answers and generate C4 diagrams/);
  const query = f.cli.query; let release; let preparations = 0;
  f.cli.query = async (args, options) => {
    if (args[3] === 'architecture' && args[4] === 'prepare') {
      preparations++;
      assert.equal(args[args.indexOf('--expected-revision') + 1], 'revision-2');
      return new Promise(resolve => { release = () => resolve({ status: 'current', diagrams: [
        { id: 'feature-context', title: 'C1 - Context', svg: '<svg/>', notes: 'Proposed' }], errors: [] }); });
    }
    return query(args, options);
  };
  const count = f.inputs.length;
  const payload = JSON.stringify({ page: 'architecture', answers: { summary: 'Use an independent referral application.' } });
  const first = f.page.action('generate-architecture', payload);
  for (let i = 0; !release && i < 20; i++) await new Promise(resolve => setImmediate(resolve));
  await f.page.action('generate-architecture', payload);
  assert.equal(preparations, 1); release(); await first;
  assert.equal(f.inputs.length, count + 1); assert.equal(f.page.model.busy, false);
  assert.match(f.page.panel.webview.html, /data:image\/svg\+xml;base64,/);
  assert.match(f.page.panel.webview.html, /Open diagram in new tab/);
  const prior = f.page.model.featureArchitecture;
  f.cli.query = async (args, options) => args[4] === 'prepare' ? { errors: ['Local model unavailable.'] } : query(args, options);
  await f.page.action('generate-architecture', payload);
  assert.equal(f.page.model.featureArchitecture.diagrams, prior.diagrams);
  assert.equal(f.page.model.busy, false); assert.equal(f.page.model.error, 'Local model unavailable.');
});

test('screen generation saves current experience answers once, uses the new revision and blocks concurrent clicks', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'experience' }));
  assert.equal(f.page.model.error, undefined);
  const query = f.cli.query; let release; let prepareCalls = 0;
  f.cli.query = async (args, options) => {
    if (args[3] === 'screens' && args[4] === 'prepare') {
      prepareCalls++; assert.equal(args[args.indexOf('--expected-revision') + 1], 'revision-2');
      assert.equal(options.cache, false);
      return await new Promise(resolve => { release = () => resolve({ status: 'current', inputHash: 'new', screens: [], errors: [] }); });
    }
    return query(args, options);
  };
  const saveCount = f.inputs.length, refreshes = f.refreshes();
  const message = JSON.stringify({ page: 'experience', answers: { summary: 'Show the capture form.' } });
  const first = f.page.action('generate-screens', message);
  for (let i = 0; !release && i < 20; i++) await new Promise(resolve => setImmediate(resolve));
  await f.page.action('generate-screens', message);
  assert.equal(prepareCalls, 1); release(); await first;
  assert.equal(f.page.model.busy, false); assert.equal(f.page.model.page, 'experience');
  assert.equal(f.page.model.featureScreens.inputHash, 'new');
  assert.equal(f.inputs.length, saveCount + 1);
  assert.equal(f.inputs.at(-1).draft.answers.summary, 'Show the capture form.');
  assert.equal(f.refreshes(), refreshes);
});

test('failed screen generation keeps the previous gallery and releases the wizard', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'experience' }));
  f.page.model.featureScreens = { status: 'current', inputHash: 'previous', screens: [], errors: [] };
  const query = f.cli.query;
  f.cli.query = async (args, options) => args[4] === 'prepare' ? { errors: ['Local provider unavailable.'] } : query(args, options);
  await f.page.action('generate-screens', JSON.stringify({ page: 'experience', answers: { summary: 'Direction' } }));
  assert.equal(f.page.model.error, 'Local provider unavailable.');
  assert.equal(f.page.model.featureScreens.inputHash, 'previous');
  assert.equal(f.page.model.busy, false);
});

test('per-screen review saves feedback without answering questions and only regenerates the selected screen', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'experience' }));
  const screen = { key: 'screen-key', revision: 'pixels-1', plan: { id: 'capture', title: 'Capture' }, svg: '<svg/>' };
  const gallery = { status: 'current', inputHash: 'preview', screens: [screen], reviews: [], errors: [] };
  f.page.model.featureScreens = structuredClone(gallery);
  f.page.model.pageDrafts.experience = { summary: 'Retained unsaved additional review notes.' };
  const original = f.cli.query; const saves = []; const preparations = []; let release;
  f.cli.query = async (args, options) => {
    if (args[3] === 'save') {
      const input = JSON.parse(fs.readFileSync(args[args.indexOf('--input') + 1], 'utf8')); saves.push(input);
      gallery.reviews = [{ id: 'review-1', key: screen.key, decision: input.screenReview.decision, feedback: input.screenReview.feedback }];
      f.wizard.revision = 'review-revision';
      return structuredClone(f.wizard);
    }
    if (args[3] === 'screens' && args[4] === 'status') return structuredClone(gallery);
    if (args[3] === 'screens' && args[4] === 'prepare') {
      preparations.push(args);
      assert.equal(options.cache, false);
      return new Promise(resolve => { release = () => resolve({ ...gallery, screens: [{ ...screen, revision: 'pixels-2', appliedReviewId: 'review-1' }] }); });
    }
    return original(args, options);
  };
  const payload = decision => JSON.stringify({ page: 'experience', answers: { summary: 'Retained unsaved additional review notes.' }, screenDrafts: { 'screen-key': 'Remove mobile.' },
    target: JSON.stringify({ screenId: 'capture', screenRevision: 'pixels-1', previewHash: 'preview', decision, feedback: 'Remove mobile.' }) });
  const first = f.page.action('review-feature-screen', payload('amend'));
  for (let i = 0; !release && i < 20; i++) await new Promise(resolve => setImmediate(resolve));
  await f.page.action('review-feature-screen', payload('amend'));
  assert.equal(saves.length, 1); assert.deepEqual(saves[0].answers, {});
  assert.equal(saves[0].screenReview.feedback, 'Remove mobile.');
  assert.equal(preparations[0][preparations[0].indexOf('--screen') + 1], 'screen-key');
  release(); await first;
  assert.equal(f.page.model.wizard.revision, 'review-revision'); assert.equal(f.page.model.busy, false);
  assert.equal(f.page.model.featureScreens.screens[0].revision, 'pixels-2');
  assert.equal(f.page.model.pageDrafts.experience.summary, 'Retained unsaved additional review notes.');
  await f.page.action('review-feature-screen', payload('not-needed'));
  assert.equal(preparations.length, 1);
  assert.equal(f.page.model.featureScreens.reviews[0].decision, 'not-needed');
  assert.deepEqual(saves[1].answers, {});
  assert.equal(f.page.model.pageDrafts.experience.summary, 'Retained unsaved additional review notes.');
});

test('unsaved screen feedback survives navigation and failed amendment retains the saved request', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'experience' }));
  await f.page.action('remember', JSON.stringify({ page: 'experience', screenDrafts: { 'screen-key': 'Rename the button.' } }));
  await f.page.action('navigate', JSON.stringify({ page: 'experience', target: 'technical' }));
  await f.page.action('navigate', JSON.stringify({ page: 'technical', target: 'experience' }));
  assert.equal(f.page.model.screenDrafts['screen-key'], 'Rename the button.');
  const gallery = { status: 'changes-requested', inputHash: 'preview', screens: [{ key: 'screen-key', revision: 'pixels', plan: { id: 'capture' }, svg: '<svg/>' }], reviews: [], errors: [] };
  f.page.model.featureScreens = gallery;
  f.cli.query = async args => args[3] === 'save' ? structuredClone(f.wizard)
    : args[4] === 'prepare' ? { errors: ['Local model unavailable.'] } : gallery;
  await f.page.action('review-feature-screen', JSON.stringify({ page: 'experience', target: JSON.stringify({ screenId: 'capture',
    screenRevision: 'pixels', previewHash: 'preview', decision: 'amend', feedback: 'Rename the button.' }) }));
  assert.match(f.page.model.error, /feedback was saved/);
  assert.equal(f.page.model.featureScreens.screens[0].svg, '<svg/>');
  assert.equal(f.page.model.busy, false);
});

test('feature preview is read only, captures entered details and actor, and removes its temporary request', async () => {
  const f = fixture(); await f.page.ready;
  await f.page.action('preview', f.value);
  assert.deepEqual(f.page.model.repositories.map(r => r.id), ['backend']);
  assert.equal(f.calls.length, 3);
  assert.ok(f.calls[2].includes('--dry-run')); assert.ok(!f.calls[2].includes('--yes'));
  assert.deepEqual(f.inputs[0].draft, { ...f.draft, actor: 'Reviewer' });
  assert.equal(fs.existsSync(f.inputs[0].file), false);
  assert.equal(fs.existsSync(path.dirname(f.inputs[0].file)), false);
  assert.equal(f.refreshes(), 0);
  assert.equal(f.page.model.plan.planHash, 'sha256:preview');
});

test('existing feature previews and applies an updated BRD without recreating repositories or refreshing the workspace', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  f.wizard.pages.find(page => page.id === 'technical').fields[0].answer = 'Keep saved technical answer.';
  const count = f.calls.length; const refreshes = f.refreshes();
  f.vscode.window.showOpenDialog = async () => [{ fsPath: path.resolve('updated.md') }];
  await f.page.action('reimport-source', JSON.stringify({ page: 'business', answers: { summary: 'Suggested from source' } }));
  assert.equal(f.page.model.error, undefined); assert.equal(f.page.model.busy, false);
  assert.equal(f.calls.length, count + 1); assert.ok(f.calls.at(-1).includes('--dry-run'));
  assert.match(f.page.panel.webview.html, /Compare BRD changes/u);
  assert.match(f.page.panel.webview.html, /New question\?/u);
  assert.match(f.page.panel.webview.html, /Removed question\?/u);
  assert.match(f.page.panel.webview.html, /Apply updated BRD/u);
  await f.page.action('apply-source-update', JSON.stringify({ page: 'business' }));
  assert.equal(f.page.model.error, undefined); assert.match(f.page.model.notice, /Updated BRD imported/u);
  assert.equal(f.page.model.sourceUpdate, undefined); assert.equal(f.page.model.page, 'business');
  assert.equal(f.refreshes(), refreshes);
  assert.equal(f.calls.slice(count).filter(args => args[0] === 'graph' || args[2] === 'intake').length, 0);
  const apply = f.calls.slice(count).find(args => args.includes('--yes'));
  assert.equal(apply[apply.indexOf('--expected-plan') + 1], 'sha256:reimport-preview');
  assert.equal(f.page.model.result.plan.sourcePath, '.cis/inputs/features/referrals/revisions/new/source.md');
  assert.equal(f.page.model.wizard.pages.find(page => page.id === 'technical').fields[0].answer, 'Keep saved technical answer.');
  assert.deepEqual(f.page.model.pageDrafts, {});
  assert.match(f.page.panel.webview.html, /Previous BRDs and saved answers/u);
  assert.deepEqual(f.executed.at(-1), ['cis.refreshFeatures']);
});

test('reimport preserves unsaved answers and repository work until explicitly saved or discarded', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  const count = f.calls.length;
  await f.page.action('reimport-source', JSON.stringify({ page: 'business', answers: { summary: 'My unsaved answer.' } }));
  assert.equal(f.calls.length, count); assert.match(f.page.model.error, /Save or discard/u);
  assert.equal(f.page.model.pageDrafts.business.summary, 'My unsaved answer.');
  await f.page.action('discard-edits', JSON.stringify({ page: 'business' }));
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'delivery' }));
  await f.page.action('reimport-source', JSON.stringify({ page: 'delivery', repositoryWork: [{ id: 'RW-001', repositoryId: 'backend', title: 'Pending', scope: 'Unsaved work', dependsOn: [], changeIds: [] }] }));
  assert.equal(f.calls.length, count); assert.match(f.page.model.error, /Delivery and acceptance/u);
  assert.equal(f.page.model.repositoryWorkDraft[0].scope, 'Unsaved work');
});

test('cancelled and identical BRD updates leave the feature review unchanged', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  const count = f.calls.length;
  await f.page.action('reimport-source', JSON.stringify({ page: 'business' }));
  assert.equal(f.calls.length, count); assert.equal(f.page.model.sourceUpdate, undefined);
  const query = f.cli.query;
  f.cli.query = async args => args[3] === 'reimport' ? { status: 'unchanged', errors: [], applied: false } : query(args);
  f.vscode.window.showOpenDialog = async () => [{ fsPath: path.resolve('identical.md') }];
  await f.page.action('reimport-source', JSON.stringify({ page: 'business' }));
  assert.match(f.page.model.notice, /identical/u); assert.equal(f.page.model.sourceUpdate, undefined);
  assert.equal(f.page.model.wizard.revision, 'revision-1');
});

test('refresh after a BRD update in another process retains old drafts separately from new questions', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('remember', JSON.stringify({ page: 'business', answers: { summary: 'Unsaved summary.', 'decision-001': 'Answer for the OLD question.' } }));
  f.wizard.plan = { ...f.plan, sourceHash: 'sha256:external-update', openDecisions: ['A different question?'] };
  await f.page.action('refresh', JSON.stringify({ page: 'business' }));
  assert.equal(f.page.model.error, undefined);
  assert.deepEqual(f.page.model.pageDrafts, {});
  assert.equal(f.page.model.recoveredSourceDraft.answers.business['decision-001'], 'Answer for the OLD question.');
  assert.deepEqual(f.page.model.recoveredSourceDraft.questions, ['Who owns support?']);
  assert.match(f.page.panel.webview.html, /Recovered unsaved edits from an earlier BRD/u);
  const reopened = openFeatureIntake(f.vscode, { ...f.options, initialSlug: 'referrals' }); await reopened.ready;
  assert.equal(reopened.model.recoveredSourceDraft.answers.business['decision-001'], 'Answer for the OLD question.');
  assert.deepEqual(reopened.model.pageDrafts, {});
});

test('stale reimport preview and authority change cannot apply an unreviewed update', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  f.vscode.window.showOpenDialog = async () => [{ fsPath: path.resolve('updated.md') }];
  await f.page.action('reimport-source', JSON.stringify({ page: 'business' }));
  const query = f.cli.query;
  f.cli.query = async args => args[3] === 'reimport' && args.includes('--yes')
    ? { status: 'stale-preview', errors: ['The source changed. Preview again.'], applied: false } : query(args);
  await f.page.action('apply-source-update', JSON.stringify({ page: 'business' }));
  assert.equal(f.page.model.sourceUpdate, undefined); assert.match(f.page.model.error, /Preview again/u);
  assert.equal(f.page.model.wizard.revision, 'revision-1'); assert.equal(f.page.model.busy, false);
  await f.page.action('reimport-source', JSON.stringify({ page: 'business' }));
  const count = f.calls.length; f.changeAuthority();
  await f.page.action('apply-source-update', JSON.stringify({ page: 'business' }));
  assert.equal(f.calls.length, count); assert.match(f.page.model.error, /authority changed/u);
});

test('concurrent apply clicks issue only one reimport and release the busy state', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  f.vscode.window.showOpenDialog = async () => [{ fsPath: path.resolve('updated.md') }];
  await f.page.action('reimport-source', JSON.stringify({ page: 'business' }));
  let release; const wait = new Promise(resolve => { release = resolve; });
  const query = f.cli.query;
  f.cli.query = async args => { if (args[3] === 'reimport' && args.includes('--yes')) await wait; return query(args); };
  const first = f.page.action('apply-source-update', JSON.stringify({ page: 'business' }));
  await f.page.action('apply-source-update', JSON.stringify({ page: 'business' }));
  release(); await first;
  assert.equal(f.calls.filter(args => args[3] === 'reimport' && args.includes('--yes')).length, 1);
  assert.equal(f.page.model.busy, false); assert.equal(f.page.model.error, undefined);
});

test('cancelled source and repository pickers preserve form input and make no intake call', async () => {
  const f = fixture(); await f.page.ready;
  await f.page.action('choose-source', f.value);
  await f.page.action('choose-repository', f.value);
  assert.equal(f.calls.length, 2); assert.equal(f.page.model.draft.title, 'Referrals');
  assert.equal(f.page.model.busy, false);
});

test('repeated create clicks apply once, bind the preview and refresh the workspace once', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value);
  let release; const wait = new Promise(resolve => { release = resolve; });
  const query = f.cli.query;
  f.cli.query = async args => { if (args.includes('--yes')) await wait; return query(args); };
  const first = f.page.action('apply');
  await f.page.action('apply'); release(); await first;
  const applies = f.calls.filter(args => args.includes('--yes'));
  assert.equal(applies.length, 1); assert.ok(applies[0].includes('sha256:preview'));
  assert.equal(f.calls.filter(args => args[0] === 'graph').length, 1);
  assert.equal(f.refreshes(), 1); assert.equal(f.page.model.result.status, 'created');
  assert.equal(f.page.model.busy, false);
  await f.page.action('apply'); assert.equal(f.refreshes(), 1);
});

test('changed authority and untrusted workspaces block creation after preview', async () => {
  for (const block of [f => f.changeAuthority(), f => { f.vscode.workspace.isTrusted = false; }]) {
    const f = fixture(); await f.page.ready; await f.page.action('preview', f.value);
    block(f); await f.page.action('apply');
    assert.equal(f.calls.filter(args => args.includes('--yes')).length, 0);
    assert.ok(f.page.model.error); assert.equal(f.page.model.busy, false);
  }
});

test('stale preview preserves entered details for correction, without a false success or graph build', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value);
  f.cli.query = async () => ({ status: 'stale-preview', errors: ['Review a fresh preview.'], _process: { failed: true } });
  await f.page.action('apply');
  assert.match(f.page.model.error, /fresh preview/u); assert.equal(f.page.model.result, undefined);
  assert.equal(f.page.model.plan, undefined); assert.equal(f.page.model.draft.title, 'Referrals');
  assert.equal(f.refreshes(), 0); assert.equal(f.page.model.busy, false);
});

test('context refresh failure retains successful setup and recovery links', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value);
  const query = f.cli.query; f.cli.query = args => { if (args[0] === 'graph') throw new Error('Graph unavailable'); return query(args); };
  await f.page.action('apply');
  assert.equal(f.page.model.result.status, 'created');
  assert.match(f.page.model.warnings.join(' '), /request was created.*Graph unavailable/u);
  assert.match(f.page.panel.webview.html, /Feature request and saved review/u);
});

test('webview script is nonce protected, executable, and posts form values and document actions', async () => {
  const f = fixture(); await f.page.ready;
  f.page.model.draft.title = '<script>alert(1)</script>';
  const html = renderFeatureIntake({ cspSource: 'vscode-webview:' }, f.page.model, 'test-nonce');
  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/u);
  const scripts = [...html.matchAll(/<script nonce="test-nonce">([\s\S]*?)<\/script>/gu)]; assert.equal(scripts.length, 1);
  const sent = []; const handlers = {};
  const button = { dataset: { featureAction: 'choose-source' }, addEventListener: (event, fn) => { handlers[event] = fn; } };
  const link = { dataset: { command: 'open-request' }, addEventListener: (event, fn) => { handlers['link-' + event] = fn; } };
  const form = { addEventListener: (event, fn) => { handlers[event] = fn; } };
  vm.runInNewContext(scripts[0][1], { window: { addEventListener() {} }, acquireVsCodeApi: () => ({ postMessage: message => sent.push(message) }),
    document: { getElementById: id => id === 'feature-form' ? form : undefined,
      querySelector: () => undefined,
      querySelectorAll: selector => selector === '[data-feature-action]' ? [button] : selector === 'button[data-command]' ? [link] : selector === 'button' ? [button, link] : [] },
    FormData: class { entries() { return Object.entries(f.draft).filter(([key]) => key !== 'integrationRepositories'); } getAll() { return ['backend']; } } });
  handlers.click(); assert.equal(sent[0].command, 'choose-source'); assert.deepEqual(JSON.parse(sent[0].value), f.draft);
  handlers.submit({ preventDefault() {} }); assert.equal(sent[1].command, 'preview');
  handlers['link-click'](); assert.equal(sent[2].command, 'open-request');
});

test('manifest and Getting Started expose the add feature flow', () => {
  const manifest = require('../package.json');
  assert.ok(manifest.contributes.commands.some(c => c.command === 'cis.featureAdd'));
  const entry = manifest.contributes.menus['view/title'].find(c => c.command === 'cis.featureAdd');
  assert.match(entry.when, /cis.features/u);
  assert.equal(require('../lib/getting-started').ACTIONS.feature, 'cis.featureWizard');
});

test('moving stories updates only local drafts, survives reopening, and saves both lists together', async () => {
  const { parseStories } = require('../lib/feature-stories');
  const f = fixture();
  const mvp = 'delivery-stories-mvp', later = 'delivery-stories-post-mvp', foundation = 'delivery-stories-foundation';
  const story = '### Customer handoff\n\nAs a customer, I want an immediate redirect.\n\n- Use the current tab.\n<!-- Source: DIR-001 -->';
  f.wizard.pages.find(p => p.id === 'delivery').fields.push(
    { id: mvp, label: 'MVP', suggestedAnswer: story, required: true },
    { id: later, label: 'Post-MVP', suggestedAnswer: 'No Post-MVP stories are currently selected.', required: true },
    { id: foundation, label: 'Foundation', suggestedAnswer: '### Security\n\nProtect tenant data.', required: true });
  await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'delivery' }));
  const calls = f.calls.length, saves = f.inputs.length;
  await f.page.action('move-story', JSON.stringify({ page: 'delivery', answers: { summary: 'Retain my release notes.' },
    target: JSON.stringify({ fieldId: mvp, index: 0, key: parseStories(story).stories[0].key }) }));
  assert.equal(f.page.model.error, undefined);
  assert.equal(f.calls.length, calls); assert.equal(f.inputs.length, saves);
  assert.equal(f.page.model.pageDrafts.delivery[later], story);
  assert.equal(f.page.model.pageDrafts.delivery[mvp], 'No MVP stories are currently selected.');
  assert.equal(f.page.model.pageDrafts.delivery[foundation], '### Security\n\nProtect tenant data.');
  assert.match(f.page.model.notice, /Save this page to keep the change/u);
  assert.equal(f.page.model.wizard.pages.find(p => p.id === 'delivery').fields.find(f => f.id === later).answer, undefined);
  const reopened = openFeatureIntake(f.vscode, f.options); await reopened.ready;
  assert.equal(reopened.model.pageDrafts.delivery[later], story);
  await reopened.action('save-page', JSON.stringify({ page: 'delivery', answers: reopened.model.pageDrafts.delivery }));
  assert.equal(f.inputs.at(-1).draft.answers[later], story);
  assert.equal(f.inputs.at(-1).draft.answers.summary, 'Retain my release notes.');
  assert.equal(reopened.model.wizard.pages.find(p => p.id === 'delivery').fields.find(f => f.id === later).answer, story);
  assert.equal(reopened.model.pageDrafts.delivery, undefined);
});

test('an edited story is retained when its old card is clicked and can be moved from the refreshed card', async () => {
  const { parseStories } = require('../lib/feature-stories');
  const f = fixture(); const mvp = 'delivery-stories-mvp', later = 'delivery-stories-post-mvp';
  const original = '### Capture\n\nOriginal acceptance.';
  const edited = '### Capture\n\nMy unsaved acceptance changes.\n<!-- Source: FRM-001 -->';
  f.wizard.pages.find(p => p.id === 'delivery').fields.push(
    { id: mvp, label: 'MVP', suggestedAnswer: original, required: true },
    { id: later, label: 'Post-MVP', suggestedAnswer: '', required: true });
  await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'delivery' }));
  const move = text => JSON.stringify({ page: 'delivery', answers: { [mvp]: edited, [later]: '' },
    target: JSON.stringify({ fieldId: mvp, index: 0, key: parseStories(text).stories[0].key }) });
  await f.page.action('move-story', move(original));
  assert.match(f.page.model.error, /Your edits are retained/u);
  assert.equal(f.page.model.pageDrafts.delivery[mvp], edited);
  assert.equal(f.page.model.pageDrafts.delivery[later], '');
  await f.page.action('move-story', move(edited));
  assert.equal(f.page.model.error, undefined);
  assert.equal(f.page.model.pageDrafts.delivery[later], edited);
});

test('wizard presents all eight stages, saves one page in place and resumes after reopening', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  assert.equal(f.page.model.page, 'business'); assert.equal(f.page.model.error, undefined);
  for (const [, title] of STEPS) assert.ok(f.page.panel.webview.html.includes(title), title);
  const callsBefore = f.calls.length; const refreshesBefore = f.refreshes();
  await f.page.action('save-page', JSON.stringify({ answers: { summary: 'Reviewed business scope.' } }));
  assert.equal(f.calls.length, callsBefore + 1, 'saving must issue one CLI command');
  assert.equal(f.page.model.page, 'business'); assert.equal(f.refreshes(), refreshesBefore);
  assert.equal(f.page.model.wizard.pages.find(page => page.id === 'business').status, 'Reviewed');
  assert.deepEqual(f.executed, [['cis.refreshFeatures']], 'save updates only feature navigation and does not open an editor');
  assert.equal(fs.existsSync(f.inputs.at(-1).file), false);
  await f.page.action('navigate', JSON.stringify({ target: 'technical' }));
  await f.page.action('remember', JSON.stringify({ answers: { summary: 'Unsaved technical draft.' } }));
  const reopened = openFeatureIntake(f.vscode, f.options); await reopened.ready;
  assert.equal(reopened.model.page, 'technical');
  assert.equal(reopened.model.pageDrafts.technical.summary, 'Unsaved technical draft.');
  assert.equal(reopened.model.wizard.pages.find(page => page.id === 'business').fields[0].answer, 'Reviewed business scope.');
  assert.match(reopened.panel.webview.html, /Unsaved technical draft/u);
});

test('wizard stale-save errors preserve edits and do not mark a page reviewed', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  f.cli.query = async () => ({ errors: ['The product baseline changed. Refresh before saving.'], _process: { failed: true } });
  await f.page.action('save-page', JSON.stringify({ answers: { summary: 'Keep these edits.' } }));
  assert.match(f.page.model.error, /baseline changed/u); assert.equal(f.page.model.busy, false);
  assert.equal(f.page.model.pageDrafts.business.summary, 'Keep these edits.');
  assert.equal(f.page.model.wizard.pages.find(page => page.id === 'business').complete, false);
  assert.match(f.page.panel.webview.html, /Keep these edits/u);
});

test('save and continue advances once after a completed save and preserves the next page draft', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  f.page.model.pageDrafts.technical = { summary: 'Next page work in progress.' };
  const before = f.calls.length; const refreshes = f.refreshes();
  await f.page.action('save-continue', JSON.stringify({ page: 'business', answers: { summary: 'Reviewed business scope.' } }));
  assert.equal(f.calls.length, before + 1); assert.equal(f.page.model.page, 'technical');
  assert.equal(f.page.model.pageDrafts.technical.summary, 'Next page work in progress.');
  assert.equal(f.page.model.pageDrafts.business, undefined); assert.equal(f.refreshes(), refreshes);
  assert.match(f.page.model.notice, /Business definition saved and reviewed/u);
  const reopened = openFeatureIntake(f.vscode, f.options); await reopened.ready;
  assert.equal(reopened.model.page, 'technical');
});

test('incomplete saves and failed saves do not advance the feature wizard', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  const query = f.cli.query;
  f.cli.query = async args => {
    const result = await query(args);
    if (args[3] === 'save') { result.pages.find(page => page.id === 'business').complete = false; }
    return result;
  };
  await f.page.action('save-continue', JSON.stringify({ page: 'business', answers: { summary: 'Partial scope.' } }));
  assert.equal(f.page.model.page, 'business'); assert.match(f.page.model.notice, /remaining questions/u);
  f.cli.query = async () => ({ errors: ['Concurrent edit; refresh before saving.'] });
  await f.page.action('save-continue', JSON.stringify({ page: 'business', answers: { summary: 'Keep unsaved changes.' } }));
  assert.equal(f.page.model.page, 'business'); assert.equal(f.page.model.pageDrafts.business.summary, 'Keep unsaved changes.');
  assert.equal(f.page.model.busy, false);
});

test('using updated source context changes only a draft until explicitly saved', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  const current = f.page.model.wizard.pages.find(page => page.id === 'business').fields[0];
  current.answer = 'Previous human review.'; current.suggestedAnswer = 'Updated source context.';
  const count = f.calls.length;
  await f.page.action('use-suggestion', JSON.stringify({ page: 'business', target: 'summary', answers: { summary: current.answer } }));
  assert.equal(f.calls.length, count); assert.equal(current.answer, 'Previous human review.');
  assert.equal(f.page.model.pageDrafts.business.summary, 'Updated source context.');
});

test('wizard navigation preserves drafts and checks only diagram status on entering architecture', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  const count = f.calls.length;
  await f.page.action('navigate', JSON.stringify({ target: 'architecture', answers: { summary: 'Work in progress.' } }));
  await f.page.action('navigate', JSON.stringify({ target: 'business' }));
  assert.equal(f.calls.length, count + 1);
  assert.deepEqual(f.calls.at(-1).slice(0, 5), ['brd', 'feature', 'wizard', 'architecture', 'status']);
  assert.equal(f.page.model.pageDrafts.business.summary, 'Work in progress.');
  assert.equal(f.page.model.wizard.pages.find(page => page.id === 'business').complete, false);
});

test('unsaved page changes block final review and late autosave events cannot cross pages', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ page: 'business', target: 'review', answers: { summary: 'Unsaved scope change.' } }));
  const count = f.calls.length;
  await f.page.action('remember', JSON.stringify({ page: 'business', answers: { summary: 'Late event.' } }));
  assert.equal(f.page.model.pageDrafts.review, undefined);
  await f.page.action('save-page', JSON.stringify({ page: 'review', answers: { summary: 'Reviewed.' } }));
  assert.equal(f.calls.length, count); assert.match(f.page.model.error, /unsaved/u);
  assert.match(f.page.panel.webview.html, /Save or discard/u);
});

test('starting another feature retains the previous feature page and unsaved answers for resume', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ target: 'technical' }));
  await f.page.action('new-feature', JSON.stringify({ answers: { summary: 'Keep this feature-specific draft.' } }));
  assert.deepEqual(f.executed.at(-1), ['cis.featureAdd']);
  assert.equal(f.page.model.selectedSlug, 'referrals'); assert.equal(f.page.model.page, 'technical');
  assert.equal(f.page.model.page, 'technical');
  assert.equal(f.page.model.pageDrafts.technical.summary, 'Keep this feature-specific draft.');
});

test('direct feature links restore the requested feature and retain a separate repository work draft', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  await f.page.action('navigate', JSON.stringify({ target: 'delivery' }));
  const work = [{ id: 'RW-001', repositoryId: 'backend', title: 'Referral handoff', scope: 'Redirect after creating the referral.', dependsOn: [], changeIds: [] }];
  await f.page.action('remember', JSON.stringify({ page: 'delivery', answers: { summary: 'Plan the repository changes.' }, repositoryWork: work }));
  const reopened = openFeatureIntake(f.vscode, { ...f.options, initialSlug: 'referrals', initialPage: 'delivery' });
  await reopened.ready;
  assert.equal(reopened.model.selectedSlug, 'referrals'); assert.equal(reopened.model.page, 'delivery');
  assert.deepEqual(reopened.model.repositoryWorkDraft, work);
  await reopened.action('save-page', JSON.stringify({ page: 'delivery', answers: { summary: 'Plan the repository changes.' }, repositoryWork: work }));
  assert.deepEqual(f.inputs.at(-1).draft.repositoryWork, work);
  assert.equal(reopened.model.repositoryWorkDraft, undefined);
  assert.deepEqual(reopened.model.wizard.repositoryWork, work);
  const fresh = openFeatureIntake(f.vscode, { ...f.options, createNew: true }); await fresh.ready;
  assert.equal(fresh.model.selectedSlug, undefined); assert.equal(fresh.model.page, 'foundation');
  assert.equal(reopened.model.selectedSlug, 'referrals', 'adding another feature must not replace the open feature');
});

test('saved feature navigation remains available while reviewing a feature', async () => {
  const f = fixture(); await f.page.ready; await f.page.action('preview', f.value); await f.page.action('apply');
  f.page.model.requests.push({ ...f.plan, slug: 'renewals', title: 'Renewals' });
  const html = renderFeatureIntake({ cspSource: 'vscode-webview:' }, f.page.model, 'test-nonce');
  assert.match(html, /All high-level features/u); assert.match(html, /data-wizard-action="open-feature" data-value="renewals"/u);
  await f.page.action('open-feature', JSON.stringify({ page: 'business', target: 'renewals', answers: { summary: 'Keep my referral draft.' } }));
  assert.deepEqual(f.executed.at(-1), ['cis.featureWizard', { root: f.options.root, slug: 'renewals' }]);
  assert.equal(f.page.model.selectedSlug, 'referrals');
  assert.equal(f.page.model.pageDrafts.business.summary, 'Keep my referral draft.');
});
