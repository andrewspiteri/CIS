'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const test = require('node:test');
const { CisCli } = require('../lib/cis-cli');
const { gettingStartedModel } = require('../lib/getting-started');
const { runRepositoryImport } = require('../lib/repository-import');

const repository = path.resolve(__dirname, '..', '..');
const executableName = process.platform === 'win32' ? 'cis.exe' : 'cis';
const executable = [
  path.join(repository, 'src', 'Cis.Host', 'bin', 'Release', 'net10.0', executableName),
  path.join(repository, 'src', 'Cis.Host', 'bin', 'Debug', 'net10.0', executableName),
].find(candidate => fs.existsSync(candidate));

// Trace: TC-VSC-001-001, TC-VSC-018-001, TC-VSC-020-001.
test('packaged CLI contract supports version and agent-provider discovery', { skip: executable ? false : 'Build Cis.Host before the integration test.' }, async () => {
  const vscode = {
    workspace: {
      isTrusted: true,
      getConfiguration: () => ({ get: (key, fallback) => key === 'executablePath' ? executable : fallback }),
    },
  };
  const authority = { root: () => repository, needsSelection: () => false };
  const output = { appendLine() {} };
  const cli = new CisCli(vscode, output, authority);

  const version = await cli.version();
  const providers = await cli.query(['agent', 'providers']);

  assert.equal(version.compatible, true);
  assert.equal(providers.exitCode, 0);
  assert.ok(providers.providers.some(provider => provider.id === 'codex'));
  assert.ok(providers.providers.some(provider => provider.id === 'claude'));
  assert.ok(providers.providers.every(provider => Array.isArray(provider.modes) && Array.isArray(provider.permissions)));
});

test('Getting Started authority initialization follows the CLI dry-run and confirmed workspace contract', { skip: executable ? false : 'Build Cis.Host first.' }, async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-start-cli-'));
  try {
    const vscode = { workspace: { workspaceFolders: [{ name: 'docs' }], isTrusted: true,
      getConfiguration: () => ({ get: (key, fallback) => key === 'executablePath' ? executable : fallback }) } };
    const authority = { root: () => root, needsSelection: () => false };
    const cli = new CisCli(vscode, { appendLine() {} }, authority);
    const args = ['workspace', 'init', '--repo', root, '--root', 'docs/cis',
      '--ecosystem', 'banking', '--ecosystem-name', 'Banking', '--product', 'deposits', '--product-name', 'Term Deposits'];
    const plan = await cli.query([...args, '--dry-run'], { repository: false });
    assert.equal(plan.applied, false);
    assert.deepEqual(plan.errors, []);
    assert.ok(plan.repositoryInitialization.filesToCreate.length > 0);
    assert.equal(fs.existsSync(path.join(root, '.cis', 'workspace.yml')), false);
    const result = await cli.query([...args, '--yes'], { repository: false });
    assert.equal(result.applied, true);
    const ready = gettingStartedModel(vscode, authority);
    assert.equal(ready.configured, true);
    assert.equal(ready.product, 'Term Deposits');
    assert.equal(ready.next.action, 'wizard');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('existing repository import accepts product ownership and identity without applying the dry run', { skip: executable ? false : 'Build Cis.Host first.' }, async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-import-cli-'));
  try {
    const vscode = { workspace: { isTrusted: true,
      getConfiguration: () => ({ get: (key, fallback) => key === 'executablePath' ? executable : fallback }) } };
    const authority = { root: () => root, needsSelection: () => false };
    const cli = new CisCli(vscode, { appendLine() {} }, authority);
    const plan = await cli.query(['repo', 'import', '--workspace', root, '--source', root,
      '--root', 'docs/cis', '--participation', 'owned', '--relationship', 'none',
      '--ecosystem', 'parr', '--product', 'parr', '--ecosystem-name', 'PARR', '--product-name', 'PARR',
      '--dry-run'], { repository: false });
    assert.equal(plan.status, 'dry-run');
    assert.equal(plan.applied, false);
    assert.deepEqual(plan.errors, []);
    assert.deepEqual(plan.collisions, []);
    assert.equal(plan.repositories.length, 1);
    assert.equal(plan.repositories[0].participation, 'owned');
    assert.equal(plan.repositories[0].relationship, 'none');
    assert.ok(plan.repositories[0].filesToCreate.length > 0);
    assert.equal(fs.existsSync(path.join(root, '.cis', 'workspace.yml')), false);
    assert.equal(fs.existsSync(path.join(root, 'docs', 'cis')), false);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('CLI import saves reviewed edits from the UI handoff and retains them on repeat', { skip: executable ? false : 'Build Cis.Host first.' }, async () => {
  // Exercise a noncanonical Windows spelling; the handoff must use the preview identity.
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-import-merge-cli-')).replaceAll('\\', '/');
  const original = '# Existing instructions\n\nKeep the project workflow.\n';
  fs.writeFileSync(path.join(root, 'AGENTS.md'), original);
  try {
    const vscode = { workspace: { isTrusted: true,
      getConfiguration: () => ({ get: (key, fallback) => key === 'executablePath' ? executable : fallback }) } };
    const cli = new CisCli(vscode, { appendLine() {} }, { root: () => root, needsSelection: () => false });
    const args = ['repo', 'import', '--workspace', root, '--source', root, '--root', 'docs/cis',
      '--participation', 'owned', '--relationship', 'none', '--ecosystem', 'parr', '--product', 'parr'];
    const plan = await cli.query([...args, '--dry-run'], { repository: false });
    assert.equal(plan.status, 'dry-run');
    assert.equal(plan.fileMerges[0].currentContent, original);
    assert.match(plan.mergeReviewHash, /^sha256:[a-f0-9]{64}$/u);
    assert.equal(fs.readFileSync(path.join(root, 'AGENTS.md'), 'utf8'), original);
    const edited = plan.fileMerges[0].proposedContent.replace('Keep the project workflow.\n', '')
      .replace('## Human-readable content\n', '') + '\n+ A real Markdown bullet\n';
    const result = await runRepositoryImport({ runForeground: (_title, command, options) => cli.query(command, options) }, {
      args: [...args, '--merge-review', plan.mergeReviewHash, '--yes'],
      fileMerges: [{ repositoryPath: plan.fileMerges[0].repositoryPath, relativePath: 'AGENTS.md', content: edited }],
    });
    assert.equal(result.applied, true);
    assert.equal(fs.readFileSync(path.join(root, 'AGENTS.md'), 'utf8'), edited);
    const repeated = await cli.query([...args, '--yes'], { repository: false });
    assert.equal(repeated.status, 'unchanged');
    assert.equal(fs.readFileSync(path.join(root, 'AGENTS.md'), 'utf8'), edited);
    assert.equal(gettingStartedModel(vscode, { root: () => root, needsSelection: () => false }).configured, true);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
