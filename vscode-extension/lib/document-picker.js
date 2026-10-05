'use strict';
const { escapeHtml, nonce, isValidWebviewMessage } = require('./security');
const { studioDocument } = require('./webview');

function documentRows(documents) {
  return documents.flatMap(role => (role.candidates || []).map(file => ({ role, file })));
}

function openDocumentPicker(vscode, documents, page, warnings = []) {
  return new Promise(resolve => {
    const panel = vscode.window.createWebviewPanel('cis.documentPicker', 'Load existing documents', vscode.ViewColumn.Active,
      { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] });
    const rows = documentRows(documents);
    let settled = false;
    const finish = selection => { if (settled) return; settled = true; resolve(selection); panel.dispose(); };
    panel.onDidDispose(() => { if (!settled) { settled = true; resolve(undefined); } });
    panel.webview.onDidReceiveMessage(message => {
      if (settled || !isValidWebviewMessage(message, new Set(['select-file', 'browse', 'cancel']))) return;
      if (message.command === 'cancel') return finish(undefined);
      if (message.command === 'browse') {
        const role = documents.find(item => item.role === message.value);
        if (role) finish({ role, browse: true });
      } else if (/^(0|[1-9]\d*)$/u.test(message.value || '')) {
        const row = rows[Number(message.value)];
        if (row) finish({ role: row.role, relative: row.file.path });
      }
    });
    panel.webview.html = renderDocumentPicker(panel.webview, documents, page, warnings, nonce());
  });
}

function renderDocumentPicker(webview, documents, page, warnings, scriptNonce) {
  const selectedRole = documents.some(item => item.role === page) ? page : documents[0]?.role;
  const rows = documentRows(documents);
  const body = `<style nonce="${scriptNonce}">.document-file[hidden]{display:none}.document-path{overflow-wrap:anywhere}.document-summary{white-space:pre-wrap;overflow-wrap:anywhere}.document-file .actions{justify-content:space-between}</style><header class="hero"><div><h1>Load existing documents</h1><p>Choose a Markdown document from your project or anywhere on your computer. Files outside the project are copied in; their originals stay unchanged. CIS reconciles the loaded document with available evidence. A legacy BRD receives CIS tracking sections and an original-file backup; approval still requires review.</p></div></header>
    <section class="card"><label for="document-role">Document type</label><select id="document-role">${documents.map(role => `<option value="${escapeHtml(role.role)}" ${role.role === selectedRole ? 'selected' : ''}>${escapeHtml(role.title)}</option>`).join('')}</select>
    <button type="button" data-command="browse">Load document manually…</button></section>
    ${warnings.length ? `<details class="card"><summary>Search notes</summary><ul>${warnings.map(note => `<li>${escapeHtml(note)}</li>`).join('')}</ul></details>` : ''}
    <p id="no-files" role="status" hidden>No eligible files found for this document type. Choose a file manually.</p>
    ${rows.map(({ role, file }, index) => `<article class="card document-file" data-role="${escapeHtml(role.role)}" ${role.role === selectedRole ? '' : 'hidden'}>
      <div class="actions"><h2>${escapeHtml(file.title)}</h2><button type="button" data-command="select-file" data-value="${index}">Select file</button></div>
      <p><strong>Path:</strong> <code class="document-path">${escapeHtml(file.path)}</code></p>
      <p><strong>File size:</strong> ${escapeHtml(Number.isSafeInteger(file.sizeBytes) ? `${file.sizeBytes.toLocaleString('en-US')} bytes` : 'Unavailable')} · <strong>Last modified:</strong> ${escapeHtml(file.lastModifiedUtc || 'Unavailable')}</p>
      <details><summary>Document summary</summary><p class="muted">Opening excerpt from the file</p><p class="document-summary">${escapeHtml(file.summary || 'No introductory text is available. Use the path to inspect the document.')}</p></details>
      ${role.selectedPath === file.path ? '<p class="muted">Currently loaded</p>' : ''}</article>`).join('')}
    <button type="button" class="secondary" data-command="cancel">Cancel</button>`;
  const script = `<script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();const choice=document.getElementById('document-role');
    function filter(){let count=0;document.querySelectorAll('[data-role]').forEach(row=>{row.hidden=row.dataset.role!==choice.value;if(!row.hidden)count++;});document.getElementById('no-files').hidden=count>0;}
    choice.addEventListener('change',filter);filter();document.addEventListener('click',event=>{const button=event.target.closest('button[data-command]');if(!button)return;vscode.postMessage({command:button.dataset.command,value:button.dataset.command==='browse'?choice.value:button.dataset.value||''});});</script>`;
  return studioDocument(webview, 'Load existing documents', body, scriptNonce, script);
}

module.exports = { openDocumentPicker, renderDocumentPicker };
