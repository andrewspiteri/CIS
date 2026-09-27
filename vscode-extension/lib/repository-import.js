'use strict';

const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { escapeHtml, isValidWebviewMessage, nonce } = require('./security');
const { openGuidanceReviewProgress } = require('./repository-import-progress');

async function confirmRepositoryImport(vscode, cli, args) {
  let plan = await cli.query([...args, '--dry-run'], { repository: false, timeout: 120_000 });
  let merges = Array.isArray(plan.fileMerges) ? plan.fileMerges : [];
  if (plan.guidanceMode === 'minimal') {
    validateMinimalPlan(plan);
    const contents = await reviewRepositoryImport(vscode, plan);
    if (!contents) return undefined;
    return { args: [...args, '--merge-review', plan.mergeReviewHash, '--yes'],
      ...(merges.length ? { fileMerges: merges.map((merge, index) => ({ repositoryPath: merge.repositoryPath,
        relativePath: merge.relativePath, content: preserveLineEndings(contents[index], merge.proposedContent) })) } : {}) };
  }
  if (!merges.length) {
    const confirmed = await vscode.window.showWarningMessage(
      'Import ' + (plan.repositories?.length || 1) + ' existing repository into CIS? CIS registers repositories in place and creates the planned guidance files.',
      { modal: true }, 'Import existing repository');
    return confirmed === 'Import existing repository' ? { args: [...args, '--yes'] } : undefined;
  }
  validateMergePlan(plan);

  if (merges.some(merge => merge.guidanceReview?.status === 'unavailable') && !args.includes('--merge-model')) {
    const choices = (plan.guidanceProviders || []).filter(provider => provider.status === 'available')
      .flatMap(provider => (provider.models || []).map(model => ({ label: provider.name + ' / ' + model.name,
        description: provider.isLocal ? 'Local model' : 'Remote model — sends guidance using your account',
        detail: 'Reviews all instructions twice. Results remain an editable proposal.', provider: provider.name,
        model: model.name, remote: !provider.isLocal })));
    choices.push({ label: 'Edit manually', description: 'Only exact duplicates have been checked', manual: true });
    const selected = await vscode.window.showQuickPick(choices, { title: 'Choose a model for the guidance merge',
      placeHolder: 'Use a strong reasoning model to reconcile repeated and conflicting instructions', ignoreFocusOut: true });
    if (!selected) return undefined;
    if (!selected.manual) {
      if (selected.remote) {
        const authorized = await vscode.window.showWarningMessage('Send guidance to ' + selected.label + '?',
          { modal: true, detail: 'CIS will send these editable guidance files, the listed read-only reference evidence, and proposed CIS guidance:\n'
            + guidanceInputSummary(merges)
            + (Number.isInteger(plan.remoteReviewBatchCount) && plan.remoteReviewBatchCount > 0
              ? '\n\nReview scope: ' + merges.length + ' files in ' + plan.remoteReviewBatchCount + ' batches; up to ' + (2 * plan.remoteReviewBatchCount) + ' model calls.' : '')
            + '\n\nSmall files may share a bounded review batch. Up to ' + (plan.remoteReviewConcurrency || 3)
            + ' model requests run concurrently using your account. Each file receives two full review passes.'
            + '\n\nEach run allows ten minutes of model work. Validated passes are saved locally for unchanged inputs. If time runs out, you can continue the review or open the incomplete proposal; nothing is imported automatically.' }, 'Send guidance and review');
        if (authorized !== 'Send guidance and review') return undefined;
      }
      const reviewArgs = [...args, '--dry-run', '--merge-review', plan.mergeReviewHash,
        '--merge-provider', selected.provider, '--merge-model', selected.model];
      if (selected.remote) reviewArgs.push('--allow-remote-merge');
      while (true) {
        const progress = openGuidanceReviewProgress(vscode, plan, selected);
        try {
          plan = await cli.query(reviewArgs, { repository: false, timeout: 780_000,
            onStarted: progress.onStarted, onStderrLine: progress.onStderrLine });
          validateMergePlan(plan);
          progress.finish(plan);
        } catch (error) { progress.fail(error); throw error; }
        if (!plan.reviewPaused) break;
        const next = await vscode.window.showWarningMessage(
          'Review paused at the ten-minute limit. Previously saved passes can be reused. Unfinished files still need review.',
          { modal: true, detail: 'Continue allows another ten minutes and reuses saved passes for unchanged inputs with the same model. Open incomplete proposal lets you inspect and edit the work so far.' },
          'Continue review', 'Open incomplete proposal');
        if (next === 'Continue review') continue;
        if (next === 'Open incomplete proposal') break;
        return undefined;
      }
      merges = plan.fileMerges;
    }
  }

  const contents = await reviewRepositoryImport(vscode, plan);
  if (!contents) return undefined;
  return { args: [...args, '--merge-review', plan.mergeReviewHash, '--yes'],
    fileMerges: merges.map((merge, index) => ({ repositoryPath: merge.repositoryPath,
      relativePath: merge.relativePath, content: preserveLineEndings(contents[index], merge.proposedContent),
      ...(contents.retirements?.[index] ? { retire: true } : {}) })) };
}

function guidanceInputSummary(merges) {
  const repositories = new Map();
  for (const merge of merges) {
    if (!repositories.has(merge.repositoryPath)) repositories.set(merge.repositoryPath, { files: new Set(), references: new Map() });
    const repository = repositories.get(merge.repositoryPath);
    repository.files.add(merge.relativePath);
    for (const source of merge.contextSources || []) repository.references.set(source.path, source.isExcerpt);
  }
  return [...repositories].map(([root, repository]) => root + '\nEditable guidance:\n'
    + [...repository.files].map(file => '  ' + file).join('\n')
    + '\nRead-only references:\n' + ([...repository.references].filter(([file]) => !repository.files.has(file))
      .map(([file, excerpt]) => '  ' + file + (excerpt ? ' (excerpt)' : '')).join('\n') || '  None')).join('\n\n');
}

function validateMergePlan(plan) {
  if (typeof plan.mergeReviewHash !== 'string' || !/^sha256:[a-f0-9]{64}$/u.test(plan.mergeReviewHash)
      || !Array.isArray(plan.fileMerges) || !plan.fileMerges.length
      || plan.fileMerges.some(merge => (!['AGENTS.md', '.github/copilot-instructions.md'].includes(merge.relativePath)
          && !/^\.github\/(?:(?:instructions\/(?!cis-)[A-Za-z0-9_-]+\.instructions\.md)|(?:skills\/(?!cis-)[A-Za-z0-9_-]+\/SKILL\.md)|(?:agents\/(?!cis-)[A-Za-z0-9_-]+\.agent\.md)|(?:prompts\/(?!cis-)[A-Za-z0-9_-]+\.prompt\.md))$/iu.test(merge.relativePath)) || typeof merge.repositoryPath !== 'string'
        || typeof merge.currentContent !== 'string' || typeof merge.proposedContent !== 'string')
      || new Set(plan.fileMerges.map(merge => JSON.stringify([merge.repositoryPath, merge.relativePath]))).size !== plan.fileMerges.length)
    throw new Error('CIS returned an incomplete guidance merge proposal. Refresh the import before continuing.');
}

function validateMinimalPlan(plan) {
  if (plan.status !== 'dry-run' || plan.applied || plan.errors?.length || plan.collisions?.length
      || !/^sha256:[a-f0-9]{64}$/u.test(plan.mergeReviewHash || '')
      || !Array.isArray(plan.assessments) || !plan.assessments.length || !Array.isArray(plan.previews)
      || !Array.isArray(plan.fileMerges) || plan.fileMerges.some(merge => !['AGENTS.md', '.github/copilot-instructions.md'].includes(merge.relativePath)))
    throw new Error('CIS returned an incomplete minimal import plan. Refresh the import before continuing.');
  if (plan.fileMerges.length) validateMergePlan(plan);
}

// Keep potentially long document contents out of command arguments and command logs.
async function runRepositoryImport(cli, confirmed) {
  let temporaryDirectory;
  try {
    const args = [...confirmed.args];
    if (confirmed.fileMerges) {
      temporaryDirectory = await fs.mkdtemp(path.join(os.tmpdir(), 'cis-import-review-'));
      const editsPath = path.join(temporaryDirectory, 'edits.json');
      await fs.writeFile(editsPath, JSON.stringify(confirmed.fileMerges), { encoding: 'utf8', flag: 'wx', mode: 0o600 });
      args.push('--merge-edits', editsPath);
    }
    return await cli.runForeground('Import existing CIS repository', args, { repository: false });
  } finally {
    if (temporaryDirectory) await fs.rm(temporaryDirectory, { recursive: true, force: true });
  }
}

function preserveLineEndings(content, proposal) {
  const normalized = proposal.replaceAll('\r\n', '\n');
  if (content === normalized) return proposal;
  return proposal.includes('\r\n') ? content.replaceAll('\r\n', '\n').replaceAll('\n', '\r\n') : content;
}

function reviewRepositoryImport(vscode, plan) {
  return new Promise(resolve => {
    const panel = vscode.window.createWebviewPanel('cis.repositoryImport', 'Review repository import', vscode.ViewColumn.Active,
      { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] });
    let settled = false;
    const finish = contents => {
      if (settled) return;
      settled = true;
      resolve(contents);
      panel.dispose();
    };
    panel.webview.html = renderRepositoryImportReview(plan, nonce());
    panel.onDidDispose(() => { if (!settled) { settled = true; resolve(undefined); } });
    panel.webview.onDidReceiveMessage(message => {
      if (!isValidWebviewMessage(message, new Set(['confirm', 'cancel']))) return;
      if (message.command === 'cancel') return finish(undefined);
      if (Array.isArray(message.contents) && message.contents.length === plan.fileMerges.length
          && message.contents.every(content => typeof content === 'string' && !content.includes('\0'))) {
        if (message.retirements !== undefined) {
          if (!Array.isArray(message.retirements) || message.retirements.length !== plan.fileMerges.length
              || message.retirements.some((value, index) => typeof value !== 'boolean'
                || value && ['AGENTS.md', '.github/copilot-instructions.md'].includes(plan.fileMerges[index].relativePath))) return;
          if (message.retirements.some(Boolean)) message.contents.retirements = message.retirements;
        }
        finish(message.contents);
      }
    });
  });
}

// Find unchanged lines with linear-memory LCS. Prefix/suffix trimming keeps normal
// edits cheap; unlike a set comparison, repeated Markdown lines keep their order.
function diffLines(original, edited) {
  const lines = text => { const result = text.replaceAll('\r\n', '\n').split('\n'); if (result.at(-1) === '') result.pop(); return result; };
  const before = lines(original); const after = lines(edited);
  const unchanged = new Map();
  let start = 0; let beforeEnd = before.length; let afterEnd = after.length;
  while (start < beforeEnd && start < afterEnd && before[start] === after[start]) { unchanged.set(start, start); start++; }
  while (beforeEnd > start && afterEnd > start && before[beforeEnd - 1] === after[afterEnd - 1]) {
    beforeEnd--; unchanged.set(--afterEnd, beforeEnd);
  }
  function lengths(aStart, aEnd, bStart, bEnd, reverse) {
    const row = new Uint32Array(bEnd - bStart + 1);
    for (let i = 0; i < aEnd - aStart; i++) {
      let diagonal = 0;
      for (let j = 1; j < row.length; j++) {
        const previous = row[j];
        row[j] = before[reverse ? aEnd - 1 - i : aStart + i] === after[reverse ? bEnd - j : bStart + j - 1]
          ? diagonal + 1 : Math.max(row[j], row[j - 1]);
        diagonal = previous;
      }
    }
    return row;
  }
  function match(aStart, aEnd, bStart, bEnd) {
    if (aStart === aEnd || bStart === bEnd) return;
    if (aEnd - aStart === 1) {
      for (let j = bStart; j < bEnd; j++) if (before[aStart] === after[j]) { unchanged.set(j, aStart); break; }
      return;
    }
    const middle = Math.floor((aStart + aEnd) / 2);
    const left = lengths(aStart, middle, bStart, bEnd, false);
    const right = lengths(middle, aEnd, bStart, bEnd, true);
    let split = 0; let best = -1;
    for (let j = 0; j <= bEnd - bStart; j++) {
      const score = left[j] + right[bEnd - bStart - j];
      if (score > best) { best = score; split = j; }
    }
    match(aStart, middle, bStart, bStart + split);
    match(middle, aEnd, bStart + split, bEnd);
  }
  match(start, beforeEnd, start, afterEnd);
  const rows = []; const additions = []; let oldLine = 0;
  for (let line = 0; line < after.length; line++) {
    if (!unchanged.has(line)) { additions.push({ kind: '+', text: after[line], newLine: line + 1 }); continue; }
    const match = unchanged.get(line);
    while (oldLine < match) { rows.push({ kind: '-', text: before[oldLine], oldLine: ++oldLine }); }
    rows.push(...additions.splice(0));
    rows.push({ kind: ' ', text: after[line], oldLine: ++oldLine, newLine: line + 1 });
  }
  while (oldLine < before.length) { rows.push({ kind: '-', text: before[oldLine], oldLine: ++oldLine }); }
  rows.push(...additions);
  return rows;
}

function compareLines(original, edited) {
  const rows = diffLines(original, edited);
  return { added: rows.filter(row => row.kind !== '-').map(row => row.kind === '+'), removed: rows.filter(row => row.kind === '-').length };
}

function renderEditableDiff(original, proposed) {
  return diffLines(original, proposed).map(row => row.kind + row.text).join('\n')
    + (proposed.endsWith('\n') ? '\n' : '');
}

function readEditableDiff(value) {
  // Every initial line has a separate one-character diff prefix. Literal Markdown
  // list markers are the NEXT character and remain untouched in the saved document.
  return value.split('\n').filter(line => !line.startsWith('-'))
    .map(line => /^[ +]/u.test(line) ? line.slice(1) : line).join('\n');
}

function initializeReview(proposals) {
  const vscode = acquireVsCodeApi();
  const saved = vscode.getState();
  const editors = [...document.querySelectorAll('textarea')];
  const retirements = proposals.map((_, index) => saved?.retirements?.[index] === true);
  const drafts = saved?.drafts || [];
  const save = () => vscode.setState({ diffs: editors.map(item => item.value), retirements, drafts });
  editors.forEach((editor, index) => {
    const section = editor.closest('section');
    const gutter = section.querySelector('.gutter');
    const markers = gutter.querySelector('pre');
    const changes = section.querySelector('.changes');
    const next = section.querySelector('.next-addition');
    const retirement = section.querySelector('.retire-file');
    editor.value = typeof saved?.diffs?.[index] === 'string' ? saved.diffs[index]
      : renderEditableDiff(proposals[index].currentContent, proposals[index].proposedContent);
    let addedLines = []; let timer;
    const synchronizeScroll = () => { gutter.scrollTop = editor.scrollTop; };
    const update = () => {
      const diff = compareLines(proposals[index].currentContent, readEditableDiff(editor.value));
      let keptLine = 0;
      addedLines = [];
      const rows = editor.value.split('\n').map((text, line) => {
        const row = document.createElement('span');
        const removed = text.startsWith('-');
        const added = !removed && diff.added[keptLine++];
        row.textContent = (removed ? '−' : added ? '+' : ' ') + ' ' + (removed ? '·' : keptLine) + '\n';
        if (removed) row.className = 'removed';
        else if (added) row.className = 'added';
        if (removed || added) addedLines.push(line);
        return row;
      });
      markers.replaceChildren(...rows);
      changes.textContent = diff.added.filter(Boolean).length + ' added · ' + diff.removed + ' removed';
      next.disabled = !addedLines.length;
      synchronizeScroll();
    };
    editor.addEventListener('scroll', synchronizeScroll);
    editor.addEventListener('input', () => {
      save();
      clearTimeout(timer); timer = setTimeout(update, 120);
    });
    // Prefix newly entered/pasted Markdown so its own '+' and '-' list bullets
    // cannot accidentally turn into diff controls. execCommand preserves Undo.
    editor.addEventListener('keydown', event => {
      if (editor.readOnly) return;
      if (event.key !== 'Enter') return;
      event.preventDefault(); document.execCommand('insertText', false, '\n+');
    });
    editor.addEventListener('paste', event => {
      if (editor.readOnly) return;
      event.preventDefault();
      const text = event.clipboardData.getData('text/plain').replaceAll('\r\n', '\n');
      const atLineStart = editor.selectionStart === 0 || editor.value[editor.selectionStart - 1] === '\n';
      document.execCommand('insertText', false, (atLineStart ? '+' : '') + text.replaceAll('\n', '\n+'));
    });
    next.addEventListener('click', () => {
      clearTimeout(timer); update();
      const currentLine = editor.value.slice(0, editor.selectionStart).split('\n').length - 1;
      const line = addedLines.find(item => item > currentLine) ?? addedLines[0];
      if (line === undefined) return;
      const offset = editor.value.split('\n').slice(0, line).reduce((sum, text) => sum + text.length + 1, 0);
      editor.focus(); editor.setSelectionRange(offset, offset);
      editor.scrollTop = Math.max(0, (line - 3) * parseFloat(getComputedStyle(editor).lineHeight));
      synchronizeScroll();
    });
    if (retirement) {
      retirement.checked = retirements[index];
      editor.readOnly = retirement.checked;
      retirement.addEventListener('change', () => {
        retirements[index] = retirement.checked;
        if (retirement.checked) { drafts[index] = editor.value; editor.value = renderEditableDiff(proposals[index].currentContent, ''); }
        else editor.value = drafts[index] ?? renderEditableDiff(proposals[index].currentContent, proposals[index].proposedContent);
        editor.readOnly = retirement.checked;
        save(); update();
      });
    }
    update();
  });
  document.querySelectorAll('button[data-command]').forEach(button => button.addEventListener('click', () => {
    document.querySelectorAll('button').forEach(item => { item.disabled = true; });
    vscode.postMessage({ command: button.dataset.command, contents: editors.map(editor => readEditableDiff(editor.value)), retirements });
  }));
}

function renderRepositoryImportReview(plan, scriptNonce) {
  const minimal = plan.guidanceMode === 'minimal';
  const repositories = plan.repositories || [];
  const createCount = minimal ? (plan.previews || []).filter(file => file.currentContent === '').length
    : repositories.reduce((sum, item) => sum + (item.filesToCreate?.length || 0), 0);
  const updateCount = minimal ? (plan.previews || []).filter(file => file.currentContent !== '').length
    : repositories.reduce((sum, item) => sum + (item.filesToUpdate?.length || 0), 0);
  const merges = plan.fileMerges.map((merge, index) => '<section class="merge"><h2>' + escapeHtml(merge.repositoryPath) + ' / ' + escapeHtml(merge.relativePath) + '</h2>'
    + renderGuidanceReview(merge.guidanceReview)
    + (!['AGENTS.md', '.github/copilot-instructions.md'].includes(merge.relativePath)
      ? '<p><label><input type="checkbox" class="retire-file"> Retire this guidance file</label></p><p class="review-note">Retirement moves the original to <code>.cis/retired-guidance/</code>, outside automatic discovery. Preserve any unique obligations in retained guidance and remove its incoming links first. The move happens only when you save and import.</p>' : '')
    + (merge.contextSources?.length ? '<details><summary>Read-only reference evidence</summary><p>These excerpts provide context. Files with their own editable proposal are reviewed separately; other references are not edited. Excerpts do not cover every linked instruction or dependency.</p><ul>'
      + merge.contextSources.map(source => '<li>' + escapeHtml(source.path) + (source.isExcerpt ? ' — excerpt' : ' — full text') + '</li>').join('') + '</ul></details>' : '')
    + (minimal ? '<details><summary>Edit the entry-point addition in context</summary>' : '')
    + '<div class="editor-toolbar"><label for="guidance-' + index + '">Edit merged guidance</label><span class="changes" role="status" aria-live="polite"></span><button class="secondary next-addition">Next change</button></div>'
    + '<div class="editor"><div class="gutter" aria-hidden="true"><pre></pre></div><textarea id="guidance-' + index + '" aria-describedby="editor-help" spellcheck="false" wrap="off"></textarea></div>'
    + (minimal ? '</details>' : '') + '</section>').join('');
  const files = repositories.map(repository => '<details><summary>' + escapeHtml(repository.repositoryPath) + ' — planned files</summary>'
    + '<h3>Create</h3><ul>' + (repository.filesToCreate || []).map(file => '<li>' + escapeHtml(file) + '</li>').join('') + '</ul>'
    + '<h3>Update</h3><ul>' + (repository.filesToUpdate || []).map(file => '<li>' + escapeHtml(file) + '</li>').join('') + '</ul></details>').join('');
  const warnings = plan.warnings?.length ? '<details><summary>' + plan.warnings.length + ' import warnings</summary><ul>' + plan.warnings.map(warning => '<li>' + escapeHtml(warning) + '</li>').join('') + '</ul></details>' : '';
  const proposals = JSON.stringify(plan.fileMerges.map(({ currentContent, proposedContent }) => ({ currentContent, proposedContent })))
    .replaceAll('<', '\\u003c').replaceAll('\u2028', '\\u2028').replaceAll('\u2029', '\\u2029');
  return '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
    + '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src \'nonce-' + scriptNonce + '\'; script-src \'nonce-' + scriptNonce + '\';">'
    + '<title>Review repository import</title><style nonce="' + scriptNonce + '">'
    + 'body{font:var(--vscode-font-size,14px)/1.5 var(--vscode-font-family,system-ui);color:var(--vscode-foreground,#ddd);background:var(--vscode-editor-background,#1e1e1e);padding:1.5rem;margin:0}'
    + 'main{max-width:100rem;margin:auto}header{max-width:65rem}h1{font-size:1.65rem;margin:0 0 .6rem}h2{font-size:1.1rem;overflow-wrap:anywhere}h3{font-size:1rem}'
    + '.summary,.changes{color:var(--vscode-descriptionForeground,#aaa)}.editor-toolbar{display:flex;align-items:center;gap:1rem;flex-wrap:wrap;margin-bottom:.5rem}.editor-toolbar label{font-weight:600}.editor-toolbar .changes{margin-left:auto}.editor-toolbar button{padding:.3rem .7rem}'
    + '.editor{display:grid;grid-template-columns:5.5rem minmax(0,1fr);border:1px solid var(--vscode-panel-border,#555);height:60vh;min-height:18rem}'
    + 'textarea,.gutter pre{font:13px/21px var(--vscode-editor-font-family,monospace);font-variant-ligatures:none;margin:0;padding:12px 8px;box-sizing:border-box;tab-size:4}'
    + 'textarea{color:var(--vscode-editor-foreground,#ddd);background:var(--vscode-editor-background,#1e1e1e);border:0;resize:none;width:100%;height:100%;white-space:pre;overflow:auto}'
    + '.gutter{overflow:hidden;background:var(--vscode-editorGutter-background,#252526);user-select:none}.gutter pre{text-align:right;color:var(--vscode-editorLineNumber-foreground,#aaa);padding-left:0;padding-right:0;padding-bottom:30px}.gutter span{display:block;height:21px;padding-right:12px;box-sizing:border-box}.gutter .added{color:var(--vscode-gitDecoration-addedResourceForeground,#81b88b);background:var(--vscode-diffEditor-insertedLineBackground,rgba(46,160,67,.2));font-weight:700}'
    + '.gutter .removed{color:var(--vscode-gitDecoration-deletedResourceForeground,#f48771);background:var(--vscode-diffEditor-removedLineBackground,rgba(248,81,73,.2));font-weight:700}.review-note{color:var(--vscode-descriptionForeground,#aaa)}blockquote{white-space:pre-wrap;border-left:3px solid var(--vscode-panel-border,#555);padding-left:1rem;margin-left:0}'
    + 'details{margin:1rem 0}summary{cursor:pointer}li{overflow-wrap:anywhere}.merge{margin:1.5rem 0}.actions{position:sticky;bottom:0;background:var(--vscode-editor-background,#1e1e1e);padding:1rem 0;display:flex;gap:.7rem;border-top:1px solid var(--vscode-panel-border,#555)}'
    + 'table{border-collapse:collapse;width:100%}td,th{text-align:left;vertical-align:top;padding:.6rem;border-bottom:1px solid var(--vscode-panel-border,#555)}.preview{white-space:pre-wrap;overflow-wrap:anywhere}code{overflow-wrap:anywhere}'
    + 'button{border:0;padding:.6rem 1rem;cursor:pointer;font:inherit;color:var(--vscode-button-foreground,#fff);background:var(--vscode-button-background,#0078d4)}button.secondary{color:var(--vscode-button-secondaryForeground,#fff);background:var(--vscode-button-secondaryBackground,#444)}button:disabled{opacity:.5;cursor:default}'
    + ':focus-visible{outline:2px solid var(--vscode-focusBorder,#0078d4);outline-offset:2px}@media(max-width:750px){body{padding:1rem}.actions{flex-wrap:wrap}}'
    + '</style></head><body><main><header><h1>' + (minimal ? 'Review minimal CIS import' : 'Review and edit repository guidance') + '</h1>'
    + (minimal ? '<p>Import adds missing CIS runtime configuration and proposes baselines for individual requirements. Existing directives remain authoritative. Evidence matches remain unverified; unresolved capability setup and conflicts appear in the report.</p>' : '')
    + '<p id="editor-help">Review and edit one combined diff. <strong>+</strong> marks additions; <strong>−</strong> marks proposed deletions. Deleted lines and the first diff-prefix character are not saved. To keep a deleted line, replace its leading <strong>−</strong> with a space.</p>'
    + '<p class="summary">Repositories: ' + repositories.length + ' · Files to create: ' + createCount + ' · Files to update: ' + updateCount + '</p>'
    + (minimal ? '<p class="summary">Counts exclude disposable local reports and scan state. Workspace registration is included.</p>' : '')
    + '<p><strong>Save guidance and import</strong> saves your edited text and applies the import plan. No import changes have been applied yet.</p></header>'
    + (minimal ? renderImportAssessment(plan) : '')
    + merges + files + warnings + '<div class="actions"><button data-command="confirm">Save guidance and import</button><button class="secondary" data-command="cancel">Cancel</button></div></main>'
    + '<script nonce="' + scriptNonce + '">' + [diffLines, compareLines, renderEditableDiff, readEditableDiff, initializeReview].map(fn => fn.toString()).join('\n') + '\ninitializeReview(' + proposals + ');</script></body></html>';
}

function renderImportAssessment(plan) {
  const reports = (plan.assessments || []).map(report => '<section><h2>Gap report: ' + escapeHtml(report.repositoryPath) + '</h2>'
    + '<p>' + (report.inventory || []).length + ' input paths · Local requirement evidence · '
    + (report.inventoryComplete ? 'No inventory exclusions' : 'Inventory incomplete: some inputs could not be inspected')
    + '. No model calls. Tests and CI gates have not been run.</p>'
    + '<h3>CIS setup</h3><p>Configuration checks are independent of guidance matches. Missing scanner bindings remain explicit follow-up work.</p>'
    + renderAssessmentRows(report.setup || [])
    + '<h3>Project guidance requirements</h3><p>Each row looks for specific evidence within a paragraph or list. Matches do not prove equivalent scope or working enforcement.</p>'
    + '<table><thead><tr><th scope="col">Responsibility</th><th scope="col">Finding and proposed action</th></tr></thead><tbody>'
    + (report.coverage || []).map(item => '<tr><th scope="row">' + escapeHtml(item.title) + '</th><td><strong>'
      + escapeHtml({ preserve: 'Preserve matching guidance; verify scope', add: 'Add a scoped baseline', defer: 'Defer: insufficient evidence' }[item.action] || item.action)
      + '</strong><p>' + escapeHtml(item.detail) + '</p>'
      + (item.addition ? '<p>' + escapeHtml(item.addition) + '</p>' : '')
      + (item.sources?.length ? '<details><summary>' + item.sources.length + ' source files</summary><ul>'
        + item.sources.map(source => '<li><code>' + escapeHtml(source) + '</code></li>').join('') + '</ul></details>' : '') + '</td></tr>').join('')
    + '</tbody></table><details><summary>' + (report.findings || []).length + ' follow-up candidates — no changes proposed</summary><ul>'
    + (report.findings || []).map(item => '<li><strong>' + escapeHtml(item.path) + '</strong>: ' + escapeHtml(item.detail) + '</li>').join('') + '</ul></details>'
    + '<details><summary>Scope and verification limits</summary><ul>' + (report.limitations || []).map(item => '<li>' + escapeHtml(item) + '</li>').join('')
    + '</ul></details><p>The report is saved locally to <code>.cis/local/import/report.json</code> when you import.</p></section>').join('');
  const previews = (plan.previews || []).map(file => {
    const changes = diffLines(file.currentContent, file.proposedContent).filter(row => row.kind !== ' ');
    return '<details><summary>' + escapeHtml(file.relativePath) + ' — ' + changes.length + ' changed lines</summary><pre class="preview">'
      + escapeHtml(changes.map(row => row.kind + row.text).join('\n')) + '</pre></details>';
  }).join('');
  return reports + '<section><h2>Proposed changes</h2><p>Only additions and removals are shown here. Entry-point editors below provide the surrounding text.</p>' + previews + '</section>';
}

function renderAssessmentRows(items) {
  return '<table><thead><tr><th scope="col">Requirement</th><th scope="col">Finding and proposed action</th></tr></thead><tbody>'
    + items.map(item => '<tr><th scope="row">' + escapeHtml(item.title) + '</th><td><strong>'
      + escapeHtml({ preserve: 'Keep existing configuration; validation pending', add: 'Add missing CIS configuration', defer: 'Setup needs review' }[item.action] || item.action)
      + '</strong><p>' + escapeHtml(item.detail) + '</p><ul>'
      + (item.sources || []).map(source => '<li><code>' + escapeHtml(source) + '</code></li>').join('') + '</ul></td></tr>').join('')
    + '</tbody></table>';
}

function renderGuidanceReview(review) {
  if (!review) return '';
  const findings = review.removals || [];
  const unresolved = review.findings || [];
  const followUps = unresolved.length ? '<div role="note"><h3>Needs attention before completing the migration</h3><ul>'
    + unresolved.map(item => '<li><strong>' + escapeHtml(item.path) + '</strong>: ' + escapeHtml(item.detail) + '</li>').join('') + '</ul></div>' : '';
  const notes = (review.warnings || []).map(warning => '<p class="review-note">' + escapeHtml(warning) + '</p>').join('');
  const scope = '<p class="review-note">' + (review.model
    ? 'Whole-document review: ' + escapeHtml(review.reviewedInstructions || 0) + ' of ' + escapeHtml(review.totalInstructions || 0)
      + ' instructions; ' + escapeHtml(review.completedPasses || 0) + ' of 2 passes completed ('
      + escapeHtml(review.provider) + ' / ' + escapeHtml(review.model) + '). '
    : 'Only exact duplicates have been checked. ')
    + (review.status === 'complete' ? 'Review the proposed removals and replacements before importing.'
      : 'The semantic merge is incomplete. Review remaining conflicts manually or retry with a stronger model.') + '</p>';
  if (!findings.length) return scope + notes + followUps;
  return scope + notes + followUps + '<details><summary>' + findings.length + ' proposed instruction changes — reasons and replacements</summary>'
    + findings.map(item => '<article><h3>Original lines ' + escapeHtml(item.startLine) + '–' + escapeHtml(item.endLine) + ': ' + escapeHtml(item.kind) + '</h3><p>'
      + escapeHtml(item.reason) + '</p><p>Replacement: ' + escapeHtml(item.replacementPath) + '</p><blockquote>'
      + escapeHtml(item.replacementText) + '</blockquote></article>').join('') + '</details>';
}

module.exports = { compareLines, diffLines, renderEditableDiff, readEditableDiff, confirmRepositoryImport, renderRepositoryImportReview, reviewRepositoryImport, runRepositoryImport };
