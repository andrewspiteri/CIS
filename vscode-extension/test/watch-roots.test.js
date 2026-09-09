'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { workspaceWatchRoots, ignoredWatchEvent } = require('../lib/watch-roots');

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
    '.cis/local/status/graph-validation.json', '.cis/local/feedback/usage.json'])
    assert.equal(ignoredWatchEvent({ fsPath: path.join(root, relative) }, root), true);
  for (const relative of ['src/Program.cs', 'src/appsettings.json', 'docs/cis/catalog.yml',
    'docs/cis/specs/business-requirements.md', '.cis/local/graph/context.db', '.cis/local/agents/runs/run.json'])
    assert.equal(ignoredWatchEvent({ fsPath: path.join(root, relative) }, root), false);
});
