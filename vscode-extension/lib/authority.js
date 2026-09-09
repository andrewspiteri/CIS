'use strict';

const AUTHORITY_KEY = 'cis.authorityFolderUri';

class AuthoritySelector {
  constructor(vscode, workspaceState) {
    this.vscode = vscode;
    this.workspaceState = workspaceState;
  }

  folders() { return this.vscode.workspace.workspaceFolders || []; }

  selectedFolder() {
    const folders = this.folders();
    if (folders.length === 0) return undefined;
    const selected = this.workspaceState.get(AUTHORITY_KEY);
    const match = selected && folders.find(folder => folder.uri.toString() === selected);
    if (match) return match;
    return folders.length === 1 ? folders[0] : undefined;
  }

  root() { return this.selectedFolder()?.uri.fsPath; }

  async choose() {
    const folders = this.folders();
    if (folders.length === 0) throw new Error('Open a repository folder first.');
    const items = folders.map(folder => ({ label: folder.name, description: folder.uri.fsPath, folder }));
    const selected = await this.vscode.window.showQuickPick(items, {
      placeHolder: 'Select the CIS authority repository',
      matchOnDescription: true,
    });
    if (!selected) return undefined;
    await this.workspaceState.update(AUTHORITY_KEY, selected.folder.uri.toString());
    return selected.folder;
  }

  async clear() { await this.workspaceState.update(AUTHORITY_KEY, undefined); }

  needsSelection() { return this.folders().length > 1 && !this.selectedFolder(); }
}

module.exports = { AUTHORITY_KEY, AuthoritySelector };
