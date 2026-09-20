'use strict';

const path = require('node:path');
const { pathToFileURL } = require('node:url');

function renderSourceReviewForm(sources, escape) {
  const pending = sources.filter(source => source.needsReview).length;
  const toolbar = '<div class="source-review-toolbar"><span data-source-count role="status" aria-live="polite">No unsaved decisions</span><button type="button" data-save-sources disabled>Save decisions</button></div>';
  const choices = [['Adopted', 'Use its requirements'], ['Reference', 'Keep as background'], ['Rejected', 'Exclude from this BRD']];
  const pendingSummaries = sources.filter(source => source.summary?.canGenerate).length;
  return `<details id="business-source-decisions"><summary tabindex="-1">Source decisions — ${pending} need review out of ${sources.length}</summary>
    <p>Review a document, choose how it should inform the BRD, and explain why. You can complete several entries and save them together. Leave undecided entries untouched and return later.</p>
    ${pendingSummaries ? `<p><button type="button" class="secondary" data-command="summarize-business-sources">Summarize ${pendingSummaries} ${pendingSummaries === 1 ? 'document' : 'documents'} locally</button> <span class="muted">Excerpts are available now. Summaries are cached until the document changes.</span></p>` : ''}
    ${toolbar}<div class="cards">${sources.map(source => {
      const virtual = source.path.startsWith('workspace:');
      const reason = source.rationale && source.rationale !== 'TODO' ? source.rationale : '';
      return `<article class="card source-review" data-source-id="${escape(source.id)}" data-review-token="${escape(source.reviewToken || '')}" data-requires-refresh="${source.requiresReconciliation || !source.reviewToken ? 'true' : 'false'}">
        <div class="section-heading"><h3>${virtual ? 'Repository implementation evidence' : `<a class="source-document-link" href="${escape(pathToFileURL(path.resolve(source.repositoryPath, source.path)).href)}" target="_blank" rel="noopener" data-command="open-business-source" data-value="${escape(source.id)}" title="Open this document in a separate editor tab beside the wizard">${escape(source.path.split(/[\\/]/u).pop())} ↗</a>`}</h3><span class="badge ${source.needsReview ? 'warn' : 'good'}">${source.needsReview ? 'Decision needed' : 'Reviewed'}</span></div>
        <p>${escape(virtual ? source.path.slice('workspace:'.length) : source.repositoryId + ' · ' + source.path)}</p>
        <div class="source-summary"><span class="eyebrow">${source.summary?.kind === 'local-model' ? 'Local model summary' : source.summary?.kind === 'excerpt' ? 'Document excerpt' : 'About this source'}</span><p>${escape(source.summary?.text || 'Open the document title to review this source.')}</p>${source.summary?.inputTruncated ? '<small class="muted">Based on selected excerpts from a longer document.</small>' : ''}</div>
        <fieldset><legend>How should this document inform the BRD?</legend><div class="source-choices">${choices.map(([value, label]) => `<label class="source-choice"><input type="radio" name="decision-${escape(source.id)}" value="${value}" ${source.assessment === value ? 'checked' : ''}><span><strong>${label}</strong><small>${value}</small></span></label>`).join('')}</div></fieldset>
        <label for="reason-${escape(source.id)}">Why?</label><textarea id="reason-${escape(source.id)}" data-source-reason rows="3" maxlength="4096" placeholder="Explain how this document helps, or why it should be excluded.">${escape(reason)}</textarea>
        ${source.requiresReconciliation || !source.reviewToken ? '<p class="notice warning">Refresh BRD evidence before saving this source.</p>' : ''}
        <p data-source-error class="notice warning" role="alert" hidden></p>
        <button type="button" class="secondary" data-confirm-source hidden>Confirm against updated source</button>
        <button type="button" class="link" data-reset-source hidden>Discard unsaved edits</button>
        ${source.issues?.length ? `<details><summary>Review details</summary><ul>${source.issues.map(issue => `<li>${escape(issue)}</li>`).join('')}</ul></details>` : ''}
      </article>`;
    }).join('')}</div>${toolbar}</details>`;
}

// Serialized into the nonce-protected webview; no canonical data is written here.
function bindSourceReviewForm(vscode) {
  const cards = Array.from(document.querySelectorAll('[data-source-id]'));
  if (!cards.length) return { message: () => undefined, refresh() {} };
  const section = document.getElementById('business-source-decisions');
  const saved = vscode.getState?.() || {};
  const drafts = saved.sourceDrafts || {};
  const entries = cards.map(card => {
    const radios = Array.from(card.querySelectorAll('input[type="radio"]'));
    const reason = card.querySelector('[data-source-reason]');
    const initial = { assessment: radios.find(radio => radio.checked)?.value || '', reason: reason.value };
    const id = card.dataset.sourceId;
    let draft = drafts[id];
    const normalized = text => text.replace(/[\r\n]/gu, ' ').trim();
    if (draft && draft.assessment === initial.assessment && normalized(draft.reason) === normalized(initial.reason)) {
      delete drafts[id]; draft = undefined;
    }
    if (draft) { radios.forEach(radio => { radio.checked = radio.value === draft.assessment; }); reason.value = draft.reason; }
    return { card, radios, reason, initial, id, token: draft?.reviewToken || card.dataset.reviewToken };
  });
  const persist = () => vscode.setState?.({ ...(vscode.getState?.() || {}), sourceDrafts: drafts, sourceReviewOpen: section.open });
  const refresh = () => {
    let count = 0;
    for (const entry of entries) {
      const assessment = entry.radios.find(radio => radio.checked)?.value || '';
      const changed = assessment !== entry.initial.assessment || entry.reason.value !== entry.initial.reason;
      const stale = changed && entry.token !== entry.card.dataset.reviewToken;
      entry.card.querySelector('[data-confirm-source]').hidden = !stale;
      entry.card.querySelector('[data-reset-source]').hidden = !changed;
      if (changed) { count++; drafts[entry.id] = { assessment, reason: entry.reason.value, reviewToken: entry.token }; }
      else delete drafts[entry.id];
      if (stale) {
        const error = entry.card.querySelector('[data-source-error]');
        error.textContent = 'This source or its saved decision changed. Your edits are retained. Review the current document before confirming them.';
        error.hidden = false;
      }
    }
    document.querySelectorAll('[data-source-count]').forEach(label => { label.textContent = count ? count + ' unsaved decision' + (count === 1 ? '' : 's') : 'No unsaved decisions'; });
    document.querySelectorAll('[data-save-sources]').forEach(button => { button.disabled = count === 0; button.textContent = count ? 'Save decisions (' + count + ')' : 'Save decisions'; });
    persist();
  };
  section.open = saved.sourceReviewOpen === true || entries.some(entry => drafts[entry.id]);
  section.addEventListener('toggle', persist);
  entries.forEach(entry => {
    const clearError = () => { entry.card.querySelector('[data-source-error]').hidden = true; refresh(); };
    entry.radios.forEach(radio => radio.addEventListener('change', clearError));
    entry.reason.addEventListener('input', clearError);
    entry.card.querySelector('[data-reset-source]').addEventListener('click', () => {
      entry.radios.forEach(radio => { radio.checked = radio.value === entry.initial.assessment; });
      entry.reason.value = entry.initial.reason; entry.token = entry.card.dataset.reviewToken; clearError();
    });
    entry.card.querySelector('[data-confirm-source]').addEventListener('click', () => { entry.token = entry.card.dataset.reviewToken; clearError(); });
  });
  refresh();
  return { refresh, message(button) {
    if (!button.hasAttribute('data-save-sources')) return undefined;
    refresh();
    let firstInvalid;
    const decisions = [];
    for (const entry of entries) {
      const draft = drafts[entry.id]; if (!draft) continue;
      let error;
      if (entry.card.dataset.requiresRefresh === 'true') error = 'Refresh BRD evidence before saving this source. Your edits will be retained.';
      else if (entry.token !== entry.card.dataset.reviewToken) error = 'Review the changed source and confirm these edits before saving.';
      else if (!draft.assessment) error = 'Choose how this document should inform the BRD.';
      else if (!draft.reason.trim()) error = 'Enter a reason for this choice.';
      if (error) {
        const label = entry.card.querySelector('[data-source-error]'); label.textContent = error; label.hidden = false;
        firstInvalid ||= entry;
      } else decisions.push({ id: entry.id, ...draft });
    }
    if (firstInvalid) { firstInvalid.card.scrollIntoView({ block: 'center' }); firstInvalid.reason.focus(); return null; }
    return decisions.length ? { command: 'save-source-decisions', value: JSON.stringify(decisions) } : null;
  } };
}

module.exports = { renderSourceReviewForm, sourceReviewScript: () => '(' + bindSourceReviewForm.toString() + ')(vscode)' };
