'use strict';

const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const childProcess = require('child_process');

class CisCli {
  constructor(output) { this.output = output; }
  executable() { return vscode.workspace.getConfiguration('cis').get('executablePath', 'cis'); }
  root() { return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath; }
  query(args) {
    const root = this.root();
    if (!root) return Promise.reject(new Error('Open a repository folder first.'));
    return new Promise((resolve, reject) => {
      childProcess.execFile(this.executable(), [...args, '--repo', root, '--format', 'json'],
        { cwd: root, windowsHide: true, timeout: 30000, maxBuffer: 4 * 1024 * 1024 },
        (error, stdout, stderr) => {
          this.output.appendLine(`$ ${this.executable()} ${args.join(' ')}`);
          if (stderr.trim()) this.output.appendLine(stderr.trim());
          if (error && !stdout.trim()) return reject(new Error(stderr.trim() || error.message));
          try { resolve(JSON.parse(stdout)); } catch { reject(new Error('CIS did not return valid JSON.')); }
        });
    });
  }
  async runTask(name, args) {
    const root = this.root();
    if (!root) throw new Error('Open a repository folder first.');
    const task = new vscode.Task({ type: 'cis', command: name }, vscode.TaskScope.Workspace, name, 'cis',
      new vscode.ProcessExecution(this.executable(), [...args, '--repo', root]), []);
    task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, panel: vscode.TaskPanelKind.Shared, clear: false };
    return vscode.tasks.executeTask(task);
  }
}

class CisTreeItem extends vscode.TreeItem {
  constructor(label, collapsibleState, file) {
    super(label, collapsibleState); this.file = file;
    if (file) {
      this.contextValue = 'cis.file'; this.resourceUri = vscode.Uri.file(file);
      this.command = { command: 'cis.open', title: 'Open', arguments: [this] };
      this.iconPath = new vscode.ThemeIcon('markdown');
    } else { this.contextValue = 'cis.group'; }
  }
}

class CisWorkspaceTree {
  constructor() { this.changed = new vscode.EventEmitter(); this.onDidChangeTreeData = this.changed.event; }
  refresh() { this.changed.fire(undefined); }
  root() { return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath; }
  docsRoot() {
    const root = this.root(); if (!root) return undefined;
    const metadata = path.join(root, '.cis', 'repository.yml');
    if (fs.existsSync(metadata)) {
      const match = /^documentation_root:\s*(.+)$/m.exec(fs.readFileSync(metadata, 'utf8'));
      if (match) return path.join(root, match[1].trim().replace(/^['"]|['"]$/g, ''));
    }
    return path.join(root, vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
  }
  getTreeItem(item) { return item; }
  getChildren(item) {
    const docs = this.docsRoot(); if (!docs || !fs.existsSync(docs)) return [];
    if (!item) return [
      new CisTreeItem('Overview', vscode.TreeItemCollapsibleState.Expanded),
      new CisTreeItem('Changes', vscode.TreeItemCollapsibleState.Collapsed),
      new CisTreeItem('References', vscode.TreeItemCollapsibleState.Collapsed),
      new CisTreeItem('Command manual', vscode.TreeItemCollapsibleState.Collapsed)
    ];
    const folder = item.label === 'Overview' ? docs : item.label === 'Changes' ? path.join(docs, 'changes') :
      item.label === 'References' ? path.join(docs, 'references') : path.join(docs, 'manual');
    return markdownFiles(folder, item.label === 'Changes');
  }
}

function markdownFiles(folder, recursive) {
  if (!fs.existsSync(folder)) return [];
  const files = [];
  for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
    const full = path.join(folder, entry.name);
    if (entry.isFile() && entry.name.toLowerCase().endsWith('.md')) files.push(full);
    if (recursive && entry.isDirectory()) {
      const proposal = path.join(full, 'proposal.md'); if (fs.existsSync(proposal)) files.push(proposal);
    }
  }
  return files.sort((a, b) => a.localeCompare(b)).map(file => new CisTreeItem(path.basename(file, '.md'), vscode.TreeItemCollapsibleState.None, file));
}

function activate(context) {
  const output = vscode.window.createOutputChannel('Change Impact Studio');
  const cli = new CisCli(output); const tree = new CisWorkspaceTree();
  context.subscriptions.push(output, vscode.window.registerTreeDataProvider('cis.workspace', tree));
  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 50);
  status.command = 'cis.repoDoctor'; status.text = '$(pulse) CIS'; status.tooltip = 'Run CIS repository doctor'; status.show(); context.subscriptions.push(status);
  const command = (id, handler) => context.subscriptions.push(vscode.commands.registerCommand(id, async (...args) => {
    try { await handler(...args); } catch (error) { vscode.window.showErrorMessage(`CIS: ${error.message}`); }
  }));
  command('cis.refresh', async () => { tree.refresh(); status.text = '$(sync) CIS'; try { const result = await cli.query(['repo', 'doctor']); status.text = result.errors > 0 ? '$(error) CIS' : result.warnings > 0 ? '$(warning) CIS' : '$(check) CIS'; } catch { status.text = '$(circle-slash) CIS'; } });
  command('cis.open', async item => { if (item?.file) await vscode.commands.executeCommand('markdown.showPreview', vscode.Uri.file(item.file)); });
  command('cis.repoInit', async () => { const root = await vscode.window.showInputBox({ prompt: 'Required repository-relative CIS documentation root', value: 'docs/cis' }); if (root) await cli.runTask('CIS repository init', ['repo', 'init', '--root', root]); });
  command('cis.repoDoctor', async () => cli.runTask('CIS repository doctor', ['repo', 'doctor']));
  command('cis.graphBuild', async () => cli.runTask('CIS graph build', ['graph', 'build']));
  command('cis.contextSearch', async () => { const query = await vscode.window.showInputBox({ prompt: 'Context search terms' }); if (query) await cli.runTask('CIS context search', ['context', 'search', query]); });
  command('cis.trackerStatus', async () => showQuery(output, cli, ['tracker', 'status']));
  command('cis.aiStatus', async () => showQuery(output, cli, ['ai', 'providers']));
  const watcher = vscode.workspace.createFileSystemWatcher('**/{.cis/repository.yml,docs/**/{*.md,catalog.yml}}');
  watcher.onDidChange(() => tree.refresh()); watcher.onDidCreate(() => tree.refresh()); watcher.onDidDelete(() => tree.refresh()); context.subscriptions.push(watcher);
}

async function showQuery(output, cli, args) { const value = await cli.query(args); output.appendLine(JSON.stringify(value, null, 2)); output.show(true); }

function deactivate() {}
module.exports = { activate, deactivate, CisCli, CisWorkspaceTree, markdownFiles };
