'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { CisViewProvider } = require('../lib/views');

function fixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-navigation-'));
  fs.mkdirSync(path.join(root, '.cis')); fs.writeFileSync(path.join(root, '.cis/repository.yml'), 'repository:\n  id: authority\ndocumentation_root: docs\n');
  const repos = ['authority', 'backend', 'frontend', 'referrals'].map(id => ({ id, repositoryPath: path.join(root, id), documentationRoot: 'docs', role: id === 'authority' ? 'authority' : 'participant', participation: 'owned' }));
  const features = ['referrals', 'renewals'].map(slug => ({ plan: { slug, title: slug, repositoryPath: repos[3].repositoryPath, integrationRepositories: ['backend', 'frontend'], requestPath: `docs/specs/feature-requests/${slug}/request.md` }, status: 'Definition in progress', reviewedPages: 3, totalPages: 8, errors: [], repositoryWork: [
    { id: 'API', title: `${slug} API`, repositoryId: 'backend', scope: 'Backend work', dependsOn: [], changeIds: ['CIS-1'] },
    { id: 'UI', title: `${slug} UI`, repositoryId: 'frontend', scope: 'Customer experience', dependsOn: ['API'], changeIds: [] },
  ], stories: [
    { id: 'policy', title: 'Agree policy', phase: 'Foundation', repositoryIds: [], status: 'Proposed' },
    { id: 'api', title: 'Record referral', phase: 'MVP', repositoryIds: ['backend'], status: 'Proposed' },
    { id: 'journey', title: 'Submit referral', phase: 'MVP', repositoryIds: ['backend', 'frontend'], status: 'Decision saved' },
  ] }));
  const nav = { workspace: { product: { id: 'deposits', name: 'Fixed Term Deposits' }, ecosystem: { name: 'BridgeLink' }, repositories: repos }, features };
  const changes = [{ id: 'CIS-1', title: 'Shared API change', status: 'Proposed' }, { id: 'CIS-2', title: 'Other', status: 'Closed' }];
  const calls = [];
  const cli = { version: async () => ({ compatible: true, raw: '0.3.0' }), query: async args => {
    calls.push(args);
    if (args.includes('navigation')) return nav;
    if (args[0] === 'definition') return { pages: Array.from({ length: 8 }, () => ({ complete: true, current: true })) };
    if (args[0] === 'change') return { changes };
    if (args[0] === 'repo') return { errorCount: 0, warningCount: 0 };
    throw new Error(`Unexpected command ${args.join(' ')}`);
  } };
  const vscode = { TreeItem: class { constructor(label, state) { this.label = label; this.collapsibleState = state; } },
    ThemeIcon: class { constructor(id) { this.id = id; } }, TreeItemCollapsibleState: { None: 0, Collapsed: 1, Expanded: 2 },
    Uri: { file: fsPath => ({ fsPath }) }, EventEmitter: class { event() {} fire() {} }, workspace: { isTrusted: true, getConfiguration: () => ({ get: () => 'docs' }) } };
  return { root, nav, calls, provider: id => new CisViewProvider(vscode, id, { root: () => root }, cli), dispose: () => fs.rmSync(root, { recursive: true, force: true }) };
}

test('product stays visible with repositories and wizard even when several changes exist', async () => {
  const f = fixture(); try {
    const nodes = await f.provider('workspace').getChildren();
    const product = nodes.find(node => node.id === 'product');
    assert.equal(product.label, 'Fixed Term Deposits'); assert.equal(product.description, 'BridgeLink');
    assert.equal(product.children.find(node => node.id === 'product-definition').description, '8/8 steps current');
    assert.equal(product.children.find(node => node.id === 'repositories').children.length, 4);
    assert.ok(!f.calls.some(args => args[0] === 'plan' || args[0] === 'design' || args[0] === 'change'));
  } finally { f.dispose(); }
});

test('approved backlog features are visible, actionable and do not duplicate saved definitions', async () => {
  const f = fixture(); try {
    f.nav.backlogFeatures = [
      { id: 'HLT-MD-01', title: 'Market capture', status: 'Ready for feature definition', canStart: true, issues: [] },
      { id: 'HLT-MD-02', title: 'Capture recovery', status: 'Blocked', canStart: false, issues: ['Start HLT-MD-01 first.'] },
    ];
    const nodes = await f.provider('features').getChildren();
    const planned = nodes.filter(node => node.contextValue === 'cis.backlogFeature');
    assert.equal(planned.length, 2);
    assert.equal(planned[0].command.command, 'cis.backlogFeature');
    assert.deepEqual(planned[0].command.arguments, [{ root: f.root, itemId: 'HLT-MD-01' }]);
    assert.equal(planned[0].children[0].label, 'Start feature definition');
    assert.equal(planned[1].children[1].description, 'Start HLT-MD-01 first.');
    f.nav.features[0].plan.backlogItemId = 'HLT-MD-01';
    const refreshed = await f.provider('features').getChildren();
    assert.equal(refreshed.filter(node => node.contextValue === 'cis.backlogFeature').length, 1);
  } finally { f.dispose(); }
});

test('all saved features reopen directly and display stories with zero, one or several repository links', async () => {
  const f = fixture(); try {
    const provider = f.provider('features');
    const nodes = await provider.getChildren();
    const features = nodes.filter(node => node.contextValue === 'cis.feature');
    assert.equal(features.length, 2);
    assert.deepEqual(features[0].command.arguments, [{ root: f.root, slug: 'referrals', page: undefined }]);
    const breakdown = features[0].children.find(node => node.label === 'Story breakdown');
    assert.equal(breakdown.description, '3 stories');
    assert.deepEqual(breakdown.children.map(node => node.label), ['Agree policy', 'Record referral', 'Submit referral']);
    assert.deepEqual(breakdown.children.map(node => node.children.length), [0, 1, 2]);
    assert.deepEqual(breakdown.children[2].children.map(node => node.command.arguments[0].id), ['backend', 'frontend']);
    assert.equal(breakdown.children[0].command.command, 'cis.featureStory');
    assert.deepEqual(breakdown.children[0].command.arguments[0], { root: f.root, slug: 'referrals', storyId: 'policy' });
    assert.ok(!features[0].children.some(node => node.label === 'backend' || node.label === 'Repository work breakdown'));
    const again = await provider.getChildren();
    assert.equal(again[1].id, features[0].id);
    assert.equal(f.calls.filter(args => args.includes('status') && args.includes('wizard')).length, 0, 'no per-feature status process');
  } finally { f.dispose(); }
});

test('story tasks appear under their story and open the selected task with stable identities', async () => {
  const f = fixture(); try {
    for (const feature of f.nav.features) feature.stories[0].tasks = [
      { id: 'T1', title: 'Review policy', status: 'Ready', repositoryIds: [] },
      { id: 'T2', title: 'Implement policy', status: 'Blocked', repositoryIds: ['backend', 'frontend'] },
    ];
    const nodes = await f.provider('features').getChildren();
    const features = nodes.filter(node => node.contextValue === 'cis.feature');
    const taskNodes = feature => feature.children.find(node => node.label === 'Story breakdown').children[0].children;
    const tasks = taskNodes(features[0]);
    assert.deepEqual(tasks.map(task => task.label), ['T1 · Review policy', 'T2 · Implement policy']);
    assert.deepEqual(tasks.map(task => task.description), ['Ready', 'Blocked']);
    assert.deepEqual(tasks[1].command.arguments[0], { root: f.root, slug: 'referrals', storyId: 'policy', taskId: 'T2' });
    assert.deepEqual(tasks[1].children.map(node => node.command.arguments[0].id), ['backend', 'frontend']);
    assert.notEqual(tasks[0].id, taskNodes(features[1])[0].id);
  } finally { f.dispose(); }
});

test('change grouping preserves shared changes, stable unique identities, filters and unassigned work', async () => {
  const f = fixture(); try {
    const provider = f.provider('changes'); const nodes = await provider.getChildren();
    assert.deepEqual(nodes.map(node => node.label), ['referrals', 'renewals', 'Other product changes']);
    assert.notEqual(nodes[0].children[0].id, nodes[1].children[0].id);
    assert.equal(nodes[2].children[0].command.arguments[0].id, 'CIS-2');
    provider.setFilter('renewals');
    assert.deepEqual((await provider.getChildren()).map(node => node.label), ['renewals']);
    assert.ok(!f.calls.some(args => args[0] === 'plan' || args[0] === 'design'));
  } finally { f.dispose(); }
});
