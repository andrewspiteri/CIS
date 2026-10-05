'use strict';

const { escapeHtml, nonce } = require('./security');
const { diffLines } = require('./repository-import');

function renderSectionProposal(proposal, scriptNonce) {
  const rows = diffLines(proposal.originalContent, proposal.proposedContent);
  const visible = new Set();
  rows.forEach((row, index) => { if (row.kind !== ' ') for (let i = Math.max(0, index - 3); i <= Math.min(rows.length - 1, index + 3); i++) visible.add(i); });
  let previous = -1;
  const diff = [...visible].sort((a, b) => a - b).map(index => {
    const gap = index > previous + 1 ? '<div class="gap">… unchanged content …</div>' : '';
    previous = index;
    const row = rows[index];
    return gap + `<div class="line ${row.kind === '+' ? 'added' : row.kind === '-' ? 'removed' : ''}"><span class="number">${row.oldLine || ''}</span><span class="number">${row.newLine || ''}</span><span>${escapeHtml(row.kind + row.text)}</span></div>`;
  }).join('');
  return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}'">
  <style nonce="${scriptNonce}">body{font-family:var(--vscode-font-family);color:var(--vscode-foreground);background:var(--vscode-editor-background);padding:24px}h1{font-size:1.5rem}.diff{font-family:var(--vscode-editor-font-family);font-size:13px;border:1px solid var(--vscode-panel-border)}.line{display:grid;grid-template-columns:4em 4em 1fr;white-space:pre-wrap;overflow-wrap:anywhere}.added{background:var(--vscode-diffEditor-insertedTextBackground,#164b2944)}.removed{background:var(--vscode-diffEditor-removedTextBackground,#6b222244)}.number{opacity:.6;text-align:right;padding-right:1em;user-select:none}.gap{padding:.5em;opacity:.65}footer{position:sticky;bottom:0;background:var(--vscode-editor-background);padding:16px 0;display:flex;gap:12px}button{padding:9px 16px;color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:0;cursor:pointer}blockquote{white-space:pre-wrap}li{margin:.5em 0}</style></head><body>
  <h1>Review suggested BRD sections</h1><p>${escapeHtml(proposal.path)}</p>
  <p>These suggestions use the existing BRD. Check that they preserve its meaning and scope. Approving applies this exact diff and rebuilds the graph. The BRD remains subject to business review and approval.</p>
  ${(proposal.warnings || []).length ? `<ul>${proposal.warnings.map(warning => `<li>${escapeHtml(warning)}</li>`).join('')}</ul>` : ''}
  <details><summary>Supporting excerpts (${proposal.sections.length} sections)</summary>${proposal.sections.map(section => `<h3>${escapeHtml(section.heading)}</h3>${section.evidenceQuotes.map(quote => `<blockquote>${escapeHtml(quote)}</blockquote>`).join('')}`).join('')}</details>
  <p>+ Added lines · − Removed lines. Unchanged content is abbreviated.</p><div class="diff" aria-label="Proposed BRD changes">${diff}</div>
  <footer><button data-action="approve">Approve changes and refresh graph</button><button data-action="cancel">Cancel</button></footer>
  <script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();document.addEventListener('click',event=>{const button=event.target.closest('button[data-action]');if(!button)return;document.querySelectorAll('button').forEach(item=>item.disabled=true);vscode.postMessage({command:button.dataset.action});});</script></body></html>`;
}

function reviewSectionProposal(vscode, proposal) {
  return new Promise(resolve => {
    const panel = vscode.window.createWebviewPanel('cis.brdSectionProposal', 'Review BRD section changes', vscode.ViewColumn.Active,
      { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] });
    let settled = false;
    const finish = approved => { if (settled) return; settled = true; resolve(approved); panel.dispose(); };
    panel.onDidDispose(() => { if (!settled) { settled = true; resolve(false); } });
    panel.webview.onDidReceiveMessage(message => { if (message?.command === 'approve') finish(true); else if (message?.command === 'cancel') finish(false); });
    panel.webview.html = renderSectionProposal(proposal, nonce());
  });
}

async function proposeBrdSections(vscode, cli, root, actorIdentity, assertAuthority, onSaved = () => {}) {
  const ai = await cli.query(['ai', 'status'], { repository: false, cache: false, interactive: true });
  const choices = (ai.providers || [])
    .sort((a, b) => Number(b.isAvailable) - Number(a.isAvailable) || Number(b.isLocal) - Number(a.isLocal))
    .flatMap(provider => (provider.models?.length ? provider.models : [{}]).map(model => ({
      label: `${provider.name}${model.name ? ' · ' + model.name : ''}`,
      description: !provider.isAvailable ? 'Unavailable — select for setup details'
        : provider.isLocal ? 'Local model — current BRD stays on this computer' : 'Remote model — requires permission to send the current BRD',
      detail: provider.detail,
      provider, model: model.name,
    })));
  if (!choices.length) throw new Error('No text-generation model is available. Start a local model or configure an AI provider.');
  const selected = await vscode.window.showQuickPick(choices, { placeHolder: 'Choose a model to suggest missing BRD sections' });
  if (!selected) return false;
  if (!selected.provider.isAvailable) {
    void Promise.resolve(vscode.window.showInformationMessage(selected.provider.detail || `${selected.provider.name} is unavailable. Check its configuration and sign-in.`)).catch(() => {});
    return false;
  }
  const args = ['brd', 'sections', 'suggest', '--workspace', root, '--provider', selected.provider.name];
  if (selected.model) args.push('--model', selected.model);
  if (!selected.provider.isLocal) {
    const allowed = await vscode.window.showWarningMessage(`Send the current BRD text to ${selected.provider.name} to suggest missing sections?`,
      { modal: true, detail: 'CIS sends the current BRD narrative and outline. For long documents, it selects relevant excerpts from across the BRD for each missing section and labels the limited coverage. Suggestions are reviewed as a diff before any changes are applied.' }, 'Send BRD and suggest');
    if (allowed !== 'Send BRD and suggest') return false;
    args.push('--allow-remote');
  }
  assertAuthority();
  const generationMinutes = selected.provider.isLocal ? 4 : 10;
  const proposal = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification,
    title: 'Suggesting BRD sections from existing content', cancellable: false }, async progress => {
    const started = Date.now();
    const report = () => progress?.report({ message: `Generating and checking suggestions · ${Math.floor((Date.now() - started) / 1000)}s · up to ${generationMinutes} minutes` });
    report();
    const timer = setInterval(report, 5000);
    try { return await cli.query(args, { repository: false, cache: false, interactive: true, timeout: (generationMinutes + 1) * 60_000 }); }
    finally { clearInterval(timer); }
  });
  if (proposal.errors?.length) throw new Error(proposal.errors.join(' '));
  if (!proposal.id) {
    void Promise.resolve(vscode.window.showInformationMessage(proposal.status === 'complete'
      ? 'All required BRD sections already have matching content.'
      : 'The model could not support any missing sections from the current BRD. No changes were made.')).catch(() => {});
    return false;
  }
  if (!/^[a-f0-9]{64}$/u.test(proposal.id) || typeof proposal.originalContent !== 'string'
      || typeof proposal.proposedContent !== 'string' || !Array.isArray(proposal.sections)) throw new Error('CIS returned an incomplete section proposal.');
  if (!await reviewSectionProposal(vscode, proposal)) return false;
  assertAuthority();
  const actor = await actorIdentity(); if (!actor) return false;
  assertAuthority();
  await cli.runForeground('Apply approved BRD section changes',
    ['brd', 'sections', 'apply', '--workspace', root, '--proposal', proposal.id, '--actor', actor], { repository: false, details: false });
  try {
    onSaved({ phase: 'saved', count: proposal.sections.length, warnings: proposal.warnings || [] });
    await cli.runForeground('Refresh graph after BRD changes', ['graph', 'build', '--workspace', root], { repository: false, details: false });
    onSaved({ phase: 'graph-refreshed', count: proposal.sections.length, warnings: proposal.warnings || [] });
  } catch (error) {
    const failure = new Error(`The approved BRD changes were saved, but the graph could not refresh: ${error.message}`);
    failure.documentApplied = true; throw failure;
  }
  return true;
}

module.exports = { proposeBrdSections, reviewSectionProposal, renderSectionProposal };
