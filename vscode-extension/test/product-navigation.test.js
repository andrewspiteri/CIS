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

test('all saved features reopen directly and display repository work and dependencies', async () => {
  const f = fixture(); try {
    const provider = f.provider('features');
    const nodes = await provider.getChildren();
    const features = nodes.filter(node => node.contextValue === 'cis.feature');
    assert.equal(features.length, 2);
    assert.deepEqual(features[0].command.arguments, [{ root: f.root, slug: 'referrals', page: undefined }]);
    const backend = features[0].children.find(node => node.label === 'backend');
    assert.equal(backend.children[0].label, 'referrals API');
    assert.equal(backend.children[0].children[0].command.arguments[0].id, 'CIS-1');
    const ui = features[0].children.find(node => node.label === 'frontend').children[0];
    assert.equal(ui.description, 'Depends on API');
    assert.equal(ui.command.arguments[0].page, 'delivery');
    const again = await provider.getChildren();
    assert.equal(again[1].id, features[0].id);
    assert.equal(f.calls.filter(args => args.includes('status') && args.includes('wizard')).length, 0, 'no per-feature status process');
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
