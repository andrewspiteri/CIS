'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const Module = require('node:module');

const originalLoad = Module._load;
const configuration = { executablePath: 'cis', documentationRoot: 'docs/cis' };
class TreeItem { constructor(label, state) { this.label = label; this.collapsibleState = state; } }
const vscode = {
  TreeItem,
  TreeItemCollapsibleState: { None: 0, Collapsed: 1, Expanded: 2 },
  ThemeIcon: class ThemeIcon {},
  Uri: { file: value => ({ fsPath: value }) },
  EventEmitter: class EventEmitter { constructor() { this.event = () => {}; } fire() {} },
  workspace: { workspaceFolders: undefined, getConfiguration: () => ({ get: (key, fallback) => configuration[key] ?? fallback }) },
};
Module._load = function (request, parent, isMain) {
  return request === 'vscode' ? vscode : originalLoad.call(this, request, parent, isMain);
};
const { CisCli, CisWorkspaceTree, markdownFiles } = require('../extension');
Module._load = originalLoad;

test('CisCli rejects queries without an open repository', async () => {
  const cli = new CisCli({ appendLine() {} });
  await assert.rejects(cli.query(['repo', 'doctor']), /Open a repository folder first/);
});

test('tree honors the initialized documentation root', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.mkdirSync(path.join(root, '.cis'), { recursive: true });
    fs.writeFileSync(path.join(root, '.cis', 'repository.yml'), 'documentation_root: custom-docs\n');
    vscode.workspace.workspaceFolders = [{ uri: { fsPath: root } }];
    assert.equal(new CisWorkspaceTree().docsRoot(), path.join(root, 'custom-docs'));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('markdown routing is deterministic and only returns markdown', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-vscode-'));
  try {
    fs.writeFileSync(path.join(root, 'b.md'), '# b');
    fs.writeFileSync(path.join(root, 'a.md'), '# a');
    fs.writeFileSync(path.join(root, 'ignore.txt'), 'x');
    assert.deepEqual(markdownFiles(root, false).map(item => item.label), ['a', 'b']);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
