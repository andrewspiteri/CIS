'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { workspaceWatchRoots, ignoredWatchEvent, createConfigurationWatchFilter } = require('../lib/watch-roots');

test('cache invalidation watches registered repositories even with only the authority open', () => {
  const fixture = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-watch-'));
  try {
    const authority = path.join(fixture, 'authority');
    const participant = path.join(fixture, 'api service');
    for (const root of [authority, participant]) {
      fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
      fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'schema_version: 1\n');
    }
    const registry = path.join(authority, '.cis', 'workspace.yml');
    fs.writeFileSync(registry, "repositories:\n- id: docs\n  path: .\n- id: api\n  path: '../api service'\n- id: missing\n  path: ../absent\n");
    assert.deepEqual(workspaceWatchRoots([authority]), [authority, participant]);
    fs.writeFileSync(registry, 'repositories:\n- id: bad\n  path: "unterminated\n- id: api\n  path: "../api service" # registered participant\n');
    assert.deepEqual(workspaceWatchRoots([authority]), [authority, participant]);
    fs.writeFileSync(registry, 'repositories:\n- id: docs\n  path: .\n');
    assert.deepEqual(workspaceWatchRoots([authority]), [authority]);
  } finally { fs.rmSync(fixture, { recursive: true, force: true }); }
});

test('generated file churn preserves UI caches while source and graph edits invalidate them', () => {
  const root = path.resolve('authority');
  for (const relative of ['node_modules/package/index.js', 'obj/Debug/Generated.cs', 'dist/bundle.js',
    '.cis/local/status/graph-validation.json', '.cis/local/feedback/usage.json',
    'tmp/snapshot.json', '.github/tmp/cache.json', '.github/copilot-runtime/instructions.md',
    '.cache/index.json', 'src/__pycache__/generated.py', 'TestResults/result.xml', '.TMP/SNAPSHOT.JSON'])
    assert.equal(ignoredWatchEvent({ fsPath: path.join(root, relative) }, root), true);
  for (const relative of ['src/Program.cs', 'src/appsettings.json', 'docs/cis/catalog.yml',
    'docs/cis/specs/business-requirements.md', 'docs/applications/parr/specs/brd-spec.md',
    '.github/instructions/project.instructions.md', '.cis/product-documents.json',
    '.cis/local/graph/context.db', '.cis/local/graph/context.db-wal', '.cis/local/agents/runs/run.json'])
    assert.equal(ignoredWatchEvent({ fsPath: path.join(root, relative) }, root), false);
});

test('unchanged MCP notifications preserve snapshots but edits, creation and deletion invalidate them', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-config-watch-'));
  try {
    fs.mkdirSync(path.join(root, '.github'));
    const uri = { fsPath: path.join(root, '.github', 'mcp.json') };
    fs.writeFileSync(uri.fsPath, '{"servers":{}}');
    const unchanged = createConfigurationWatchFilter(root);
    assert.equal(unchanged(uri), true);
    fs.writeFileSync(uri.fsPath, '{"servers":{}}');
    assert.equal(unchanged(uri), true);
    fs.writeFileSync(uri.fsPath, '{"servers":{"local":{}}}');
    assert.equal(unchanged(uri), false);
    assert.equal(unchanged(uri), true);
    fs.unlinkSync(uri.fsPath);
    assert.equal(unchanged(uri), false);
    fs.writeFileSync(uri.fsPath, '{}');
    assert.equal(unchanged(uri), false);
    assert.equal(unchanged({ fsPath: path.join(root, 'docs', 'requirements.md') }), false);
    fs.writeFileSync(uri.fsPath, 'x'.repeat(1024 * 1024 + 1));
    assert.equal(unchanged(uri), false);
    assert.equal(unchanged(uri), false, 'unreadable or oversized inputs must not silently suppress invalidation');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
