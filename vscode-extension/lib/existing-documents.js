'use strict';

const path = require('node:path');
const { openDocumentPicker } = require('./document-picker');

// Labels for the CLI's supported document roles; validation remains in the CLI.
const DOCUMENT_ROLES = [
  ['business', 'Business requirements'], ['technical', 'Technical intent'], ['architecture', 'Solution architecture'],
  ['components', 'Component sheet'], ['diagrams', 'Architecture diagrams'], ['experience', 'UI direction'], ['delivery', 'High-level backlog'],
].map(([role, title]) => ({ role, title, candidates: [] }));

async function loadExistingDocuments(vscode, cli, root, page, options = {}) {
  let role;
  let relative;
  if (options.manual) {
    const roles = page === 'architecture' ? DOCUMENT_ROLES.filter(item => ['architecture', 'components', 'diagrams'].includes(item.role)) : DOCUMENT_ROLES;
    role = page !== 'architecture' && roles.find(item => item.role === page);
    if (!role) role = (await vscode.window.showQuickPick(roles.map(item => ({ label: item.title, documentRole: item })),
      { title: 'Load document manually', placeHolder: 'Choose the document type', ignoreFocusOut: true }))?.documentRole;
    if (!role) return false;
    relative = await browseDocument(vscode, root, role.title);
  } else {
    const selection = await discoverDocument(vscode, cli, root, page, options.documents);
    if (!selection) return false;
    ({ role, relative } = selection);
  }
  if (!relative) return false;
  const loadedMessage = path.isAbsolute(relative) ? `Imported a project copy of ${relative}` : `Loaded ${relative}`;
  // The CLI owns path validation and document-role registration.
  const result = await cli.runForeground('Load existing document', ['definition', 'documents', '--workspace', root,
    '--role', role.role, path.isAbsolute(relative) ? '--source' : '--path', relative], { repository: false });
  if (result?.errors?.length) throw new Error(result.errors.join('\n'));
  try {
    await cli.runForeground('Refresh evidence for the loaded document', ['graph', 'build', '--workspace', root], { repository: false });
    if (role.role === 'technical') {
      const questionnaire = await cli.runForeground('Prepare technical choices for the loaded document',
        ['technical-intent', 'questions', 'init', '--workspace', root], { repository: false });
      if (questionnaire?.errors?.length) throw new Error(questionnaire.errors.join('\n'));
      if (questionnaire?.complete !== true || questionnaire?.current !== true) {
        await cli.runForeground('Update context after preparing technical choices', ['graph', 'build', '--workspace', root], { repository: false });
        void vscode.window.showInformationMessage(`${loadedMessage}. Review and answer the technical choices next. Your document is preserved; evidence reconciliation will continue when those choices are complete.`);
        return true;
      }
    }
    const commands = {
      business: ['brd', 'reconcile'], technical: ['technical-intent', 'init'],
      architecture: ['solution-design', 'init'], components: ['solution-design', 'init'], diagrams: ['solution-design', 'init'],
      experience: ['ui-direction', 'init'], delivery: ['brd', 'backlog', 'build'],
    };
    const command = commands[role.role];
    if (!command) throw new Error('This document type has no reconciliation command.');
    await cli.runForeground('Reconcile loaded document evidence', [...command, '--workspace', root], { repository: false });
    await cli.runForeground('Update context after document reconciliation', ['graph', 'build', '--workspace', root], { repository: false });
  } catch (error) {
    error.documentLoaded = true;
    error.message = `${loadedMessage}, but evidence reconciliation needs attention. ${error.message}`;
    throw error;
  } finally { cli.clearQueryCache?.(); }
  void vscode.window.showInformationMessage(`${loadedMessage} and reconciled its evidence. Review the wizard findings before approval.`);
  return true;
}

async function discoverDocument(vscode, cli, root, page, existing) {
  const query = () => cli.query(['definition', 'documents', '--workspace', root], { repository: false, cache: false, timeout: 120_000 });
  const discovered = existing?.documents?.length && !existing.errors?.length ? existing
    : typeof vscode.window.withProgress === 'function'
      ? await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Finding existing project documents', cancellable: false }, query)
      : await query();
  if (discovered.errors?.length) throw new Error(discovered.errors.join('\n'));
  const selected = await openDocumentPicker(vscode, discovered.documents || [], page, discovered.warnings || []);
  if (!selected) return undefined;
  if (selected.browse) selected.relative = await browseDocument(vscode, root, selected.role.title);
  return selected;
}

async function browseDocument(vscode, root, title) {
  const files = await vscode.window.showOpenDialog({ defaultUri: vscode.Uri.file(root), canSelectMany: false,
    title: `Load ${title}`, canSelectFiles: true, canSelectFolders: false, filters: { Markdown: ['md'] }, openLabel: 'Load document' });
  if (!files?.length) return undefined;
  const relative = path.relative(root, files[0].fsPath);
  return relative === '..' || relative.startsWith('..' + path.sep) || path.isAbsolute(relative)
    ? files[0].fsPath : relative.replaceAll('\\', '/');
}

module.exports = { loadExistingDocuments };
