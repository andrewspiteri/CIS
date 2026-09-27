'use strict';

const { bound, escapeHtml, isValidWebviewMessage, nonce } = require('./security');

function openGuidanceReviewProgress(vscode, plan, selected) {
  const panel = vscode.window.createWebviewPanel('cis.guidanceReviewProgress', 'Guidance merge progress', vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] });
  const model = { state: 'queued', startedAt: Date.now(), finishedAt: null, concurrency: 1, model: selected.label,
    location: selected.remote ? 'Your authorized remote model account' : 'Local model',
    repositories: plan.fileMerges.map(merge => ({ path: merge.repositoryPath, file: merge.relativePath,
      state: 'queued', startedAt: null, finishedAt: null, message: 'Waiting to start.',
      sources: (merge.guidanceSources || []).map(source => source.path),
      references: (merge.contextSources || []).map(source => source.path + (source.isExcerpt ? ' (excerpt)' : '')) })),
    current: 'Waiting for the CIS command to start.', log: [] };
  let disposed = false;
  panel.onDidDispose(() => { disposed = true; });
  const publish = () => { if (!disposed) void panel.webview.postMessage?.({ command: 'progress', model }); };
  const record = message => {
    model.current = bound(message, 4_096);
    model.log.push({ at: Date.now(), message: model.current });
    if (model.log.length > 300) model.log.shift();
    publish();
  };
  panel.webview.onDidReceiveMessage(message => {
    if (isValidWebviewMessage(message, new Set(['ready']))) publish();
  });
  panel.webview.html = renderGuidanceReviewProgress(model, nonce());
  return {
    onStarted() { model.state = 'running'; record('CIS started. Preparing the selected guidance and checking the model.'); },
    onStderrLine(line) {
      const prefix = '[guidance-review] ';
      if (!line.startsWith(prefix)) return;
      const text = line.slice(prefix.length);
      let event;
      try { event = JSON.parse(text); } catch { /* Older CLI versions send plain stage text. */ }
      if (event?.kind === 'review-start' && typeof event.message === 'string') {
        if (Number.isInteger(event.concurrency) && event.concurrency >= 1 && event.concurrency <= 4)
          model.concurrency = event.concurrency;
        record(event.message);
      } else if (['file-start', 'file-progress', 'file-finish'].includes(event?.kind)
          && Number.isInteger(event.index) && event.index >= 0 && event.index < model.repositories.length
          && typeof event.message === 'string') {
        const file = model.repositories[event.index];
        file.startedAt ??= Date.now();
        file.message = bound(event.message, 4_096);
        file.state = event.kind === 'file-finish'
          ? (['complete', 'deterministic'].includes(event.reviewStatus) ? 'complete' : 'incomplete') : 'running';
        if (event.kind === 'file-finish') file.finishedAt = Date.now();
        record('File ' + (event.index + 1) + ' of ' + model.repositories.length + ' — ' + file.file + ': ' + file.message);
      } else record(text);
    },
    finish(result) {
      const reviews = (result.fileMerges || []).map(merge => merge.guidanceReview);
      model.state = result.reviewPaused ? 'paused'
        : reviews.length > 0 && reviews.every(review => ['complete', 'deterministic'].includes(review?.status)) ? 'complete' : 'incomplete';
      model.finishedAt = Date.now();
      for (const merge of result.fileMerges || []) {
        const review = merge.guidanceReview;
        const file = model.repositories.find(item => item.path === merge.repositoryPath && item.file === merge.relativePath);
        if (file) {
          file.state = ['complete', 'deterministic'].includes(review?.status) ? 'complete' : 'incomplete';
          file.finishedAt ??= model.finishedAt;
          file.message = (review?.completedPasses || 0) + ' of 2 passes completed; '
            + (review?.reviewedInstructions || 0) + ' of ' + (review?.totalInstructions || 0) + ' instruction blocks reviewed.';
        }
        record(merge.repositoryPath + ' / ' + merge.relativePath + ': ' + (review?.completedPasses || 0) + ' of 2 passes completed; '
          + (review?.reviewedInstructions || 0) + ' of ' + (review?.totalInstructions || 0) + ' instruction blocks reviewed; '
          + (review?.removals?.length || 0) + ' proposed changes.');
        for (const warning of review?.warnings || []) record('Warning: ' + warning);
      }
      record(model.state === 'paused' ? 'The ten-minute review limit was reached. Continue the review to reuse previously saved passes, or inspect the incomplete proposal. Check warnings for any checkpoint failures.'
        : model.state === 'complete' ? 'The editable proposal is ready. Review it before importing.'
        : 'The model review is incomplete. The editable proposal includes warnings and needs manual review.');
    },
    fail(error) {
      model.state = 'failed'; model.finishedAt = Date.now();
      for (const file of model.repositories.filter(item => ['running', 'queued'].includes(item.state))) {
        file.state = 'failed'; file.finishedAt = model.finishedAt; file.message = 'Review stopped before this file finished.';
      }
      record('Guidance review failed: ' + bound(error?.message || 'CIS did not return a review.'));
    },
  };
}

function initializeGuidanceProgress(initial) {
  const vscode = acquireVsCodeApi();
  let model = initial;
  const labels = { queued: 'Queued', running: 'Review in progress', complete: 'Proposal ready', incomplete: 'Review incomplete', paused: 'Review paused', failed: 'Review failed' };
  const elapsed = milliseconds => { const seconds = Math.max(0, Math.floor(milliseconds / 1000)); return Math.floor(seconds / 60) + 'm ' + seconds % 60 + 's'; };
  const tick = () => {
    document.getElementById('elapsed').textContent = elapsed((model.finishedAt || Date.now()) - model.startedAt);
    for (const [index, file] of model.repositories.entries()) {
      document.getElementById('file-time-' + index).textContent = file.startedAt
        ? 'File elapsed: ' + elapsed((file.finishedAt || model.finishedAt || Date.now()) - file.startedAt) : '';
    }
  };
  const render = () => {
    document.getElementById('status').textContent = labels[model.state] || model.state;
    document.getElementById('current').textContent = model.current;
    document.getElementById('activity').textContent = model.log.map(item => elapsed(item.at - model.startedAt) + '  ' + item.message).join('\n')
      || 'Stage updates will appear here when CIS starts.';
    document.getElementById('status').dataset.state = model.state;
    const finished = model.repositories.filter(file => ['complete', 'incomplete'].includes(file.state)).length;
    const active = model.repositories.filter(file => file.state === 'running').length;
    const incomplete = model.repositories.filter(file => file.state === 'incomplete').length;
    document.getElementById('file-count').textContent = finished + ' of ' + model.repositories.length + ' files finished'
      + (active ? ' · ' + active + ' active' : '') + (incomplete ? ' · ' + incomplete + ' need manual review' : '');
    document.getElementById('review-schedule').textContent = model.concurrency > 1
      ? 'Up to ' + model.concurrency + ' model requests run concurrently. Small files may share a bounded batch; each file keeps both full review passes.'
      : 'Files run one at a time. Each file keeps both full review passes.';
    for (const [index, file] of model.repositories.entries()) {
      document.getElementById('file-status-' + index).textContent = labels[file.state] || 'Queued';
      document.getElementById('file-message-' + index).textContent = file.message || 'Waiting to start.';
    }
    tick();
  };
  window.addEventListener('message', event => { if (event.data?.command === 'progress') { model = event.data.model; render(); } });
  render();
  setInterval(tick, 1000);
  vscode.postMessage({ command: 'ready' });
}

function renderGuidanceReviewProgress(model, scriptNonce) {
  const data = JSON.stringify(model).replaceAll('<', '\\u003c');
  return `<!doctype html><html lang="en"><head><meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0"><title>Guidance merge progress</title>
<style nonce="${scriptNonce}">
body{font-family:var(--vscode-font-family);color:var(--vscode-foreground);background:var(--vscode-editor-background);padding:24px;max-width:1000px;margin:auto;line-height:1.5}
h1{font-size:26px;margin:0 0 8px}h2{font-size:17px;margin:0 0 8px}p{margin:8px 0}.muted{color:var(--vscode-descriptionForeground)}
.metrics{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:16px;margin:24px 0}.card{padding:16px;border:1px solid var(--vscode-panel-border);border-radius:6px}
.value{font-size:18px;overflow-wrap:anywhere}.label{color:var(--vscode-descriptionForeground);font-size:12px}li{margin:8px 0;overflow-wrap:anywhere}.section-title{margin-top:24px}
pre{white-space:pre-wrap;overflow-wrap:anywhere;font-family:var(--vscode-editor-font-family);font-size:12px;line-height:1.7}
#status[data-state=failed],#status[data-state=incomplete],#status[data-state=paused]{color:var(--vscode-errorForeground)}#status[data-state=complete]{color:var(--vscode-testing-iconPassed)}
</style></head><body>
<h1>Guidance merge progress</h1><p>Preparing editable repository guidance proposals. This review does not apply the import.</p>
<div class="metrics"><div class="card"><div class="label">Selected model</div><div class="value">${escapeHtml(model.model)}</div><p class="muted">${escapeHtml(model.location)}</p></div>
<div class="card"><div class="label">Status</div><div id="status" class="value" role="status"></div><p>Total elapsed: <span id="elapsed">0m 0s</span></p><p id="file-count"></p></div></div>
<section class="card"><h2>What is happening now</h2><p id="current" aria-live="polite">${escapeHtml(model.current)}</p>
<p id="review-schedule" class="muted"></p><p class="muted">Each run allows ten minutes of model work, with up to five minutes per pass. Validated passes are saved locally and reused for unchanged inputs. Total elapsed includes preparation; you can continue a paused review.</p></section>
<h2 class="section-title">File progress</h2><ol>${model.repositories.map((repo, index) => '<li><strong>' + escapeHtml(repo.file)
    + '</strong><br><span class="muted">' + escapeHtml(repo.path) + '</span><br><span id="file-status-' + index + '">Queued</span>'
    + ' <span id="file-time-' + index + '" class="muted"></span><p id="file-message-' + index + '">Waiting to start.</p></li>').join('')}</ol>
<h2 class="section-title">Review stages</h2><ol><li>Prepare the existing guidance and proposed CIS instructions.</li>
<li>Compare every instruction with CIS guidance and propose removals or replacements.</li>
<li>Check the whole proposal again for missed repetitions, conflicts and lost project details.</li>
<li>Validate coverage and open the editable review for your decision.</li></ol>
<h2>Selected guidance</h2><ul>${model.repositories.map(repo => '<li><strong>' + escapeHtml(repo.path) + ' / ' + escapeHtml(repo.file)
    + '</strong><br><span class="muted">Proposed CIS sources: ' + repo.sources.map(escapeHtml).join(', ')
    + (repo.references?.length ? '<br>Read-only reference evidence: ' + repo.references.map(escapeHtml).join(', ') : '') + '</span></li>').join('')}</ul>
<section class="card"><h2>Live activity</h2><pre id="activity" aria-label="Guidance review activity"></pre></section>
<p class="muted">Closing this tab leaves the review running. When it finishes, review and edit the proposal before choosing Save guidance and import.</p>
<script nonce="${scriptNonce}">(${initializeGuidanceProgress.toString()})(${data});</script></body></html>`;
}

module.exports = { openGuidanceReviewProgress, renderGuidanceReviewProgress };
