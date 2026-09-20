'use strict';

function renderTechnicalDecisionForm(decision, escape) {
  const related = decision.relatedAnswers || [];
  const recorded = decision.recordedResolution || '';
  const answer = recorded ? [recorded, decision.recordedReason && !recorded.includes(decision.recordedReason) ? `Reason: ${decision.recordedReason}` : ''].filter(Boolean).join('\n\n') : decision.suggestedResolution || '';
  const existing = related.length ? `<details><summary>Relevant recorded answers: ${related.map(answer => escape(answer.id)).join(', ')}</summary>${related.map(answer => `<section><h5>${escape(answer.id)} — ${escape(answer.area)}</h5><p>${escape(answer.answer)}</p><p class="muted">${escape(answer.status === 'Derived' ? 'Derived from repository evidence' : `Recorded by ${answer.answeredBy || 'the reviewer'}`)}</p></section>`).join('')}</details>` : '';
  return `<section class="technical-decision-form" data-technical-decision-id="${escape(decision.id)}" data-review-token="${escape(decision.reviewToken || '')}" data-answer-recorded="${Boolean(recorded)}">
    ${existing}${decision.remainingReview ? `<p class="notice">${escape(decision.remainingReview)}</p>` : ''}
    ${decision.questionnaireOverlap && !related.length ? '<p class="notice">This decision overlaps the questionnaire, but current resolved answers are unavailable. Refresh technical evidence before reusing answers.</p>' : ''}
    ${!recorded && decision.suggestedResolution ? '<p class="muted">Suggested answer — review and edit it for this product, then save to record your choice.</p>' : ''}
    <label>Answer<textarea data-technical-resolution rows="8" maxlength="16384" placeholder="Describe the chosen direction, including any context or exceptions.">${escape(answer)}</textarea></label>
    <p data-technical-stale hidden class="notice warning">The decision or questionnaire changed while these edits were unsaved. Compare them with the current evidence, then keep or discard your edits.</p>
    <div data-technical-rebase hidden><button type="button" class="secondary" data-keep-technical-edits>Keep reviewed edits</button><button type="button" class="secondary" data-discard-technical-edits>Discard edits</button></div>
    <p data-technical-error hidden role="alert"></p><div class="actions"><button type="button" data-save-technical-decision ${decision.reviewToken ? '' : 'disabled'}>${decision.needsReview ? 'Save answer' : 'Update answer'}</button></div>
    ${decision.recordedBy ? `<p class="muted">Last recorded by ${escape(decision.recordedBy)}.</p>` : ''}
    <p class="muted">Saving updates this decision and refreshes readiness. Approval of technical intent remains a separate action.</p>
  </section>`;
}

function bindTechnicalDecisionForms(vscode) {
  const savedState = vscode.getState?.() || {};
  const drafts = { ...(savedState.technicalDecisionDrafts || {}) };
  const entries = [];
  const persist = () => vscode.setState?.({ ...(vscode.getState?.() || {}), technicalDecisionDrafts: drafts });
  for (const form of document.querySelectorAll('[data-technical-decision-id]')) {
    const id = form.dataset.technicalDecisionId, reviewToken = form.dataset.reviewToken;
    const resolution = form.querySelector('[data-technical-resolution]');
    const original = { resolution: resolution.value, reviewToken };
    let draft = drafts[id];
    // Preserve text entered in the former separate reason field when upgrading the form.
    if (draft?.reason) { draft = { ...draft, resolution: [draft.resolution, draft.resolution.includes(draft.reason) ? '' : `Reason: ${draft.reason}`].filter(Boolean).join('\n\n') }; delete draft.reason; drafts[id] = draft; }
    // A successful save is reflected in the canonical values on the next render.
    if (draft && form.dataset.answerRecorded === 'true' && draft.resolution.trim() === original.resolution.trim()) { delete drafts[id]; draft = undefined; }
    if (draft) resolution.value = draft.resolution;
    const entry = { form, id, reviewToken, resolution, original, stale: Boolean(draft && draft.reviewToken !== reviewToken) };
    const showStale = () => { form.querySelector('[data-technical-stale]').hidden = !entry.stale; form.querySelector('[data-technical-rebase]').hidden = !entry.stale; };
    const remember = () => { drafts[id] = { resolution: resolution.value, reviewToken: entry.stale ? drafts[id]?.reviewToken : reviewToken }; persist(); };
    resolution.addEventListener('input', remember);
    entries.push({ ...entry, get stale() { return entry.stale; }, rebase() { entry.stale = false; showStale(); remember(); }, remember });
    showStale();
  }
  persist();
  return { message(button) {
    if (!entries.length) return undefined;
    const form = button.closest('[data-technical-decision-id]');
    const entry = entries.find(item => item.form === form); if (!entry) return undefined;
    const error = form.querySelector('[data-technical-error]'); error.hidden = true;
    if (button.hasAttribute('data-keep-technical-edits')) { entry.rebase(); return null; }
    if (button.hasAttribute('data-discard-technical-edits')) { entry.resolution.value = entry.original.resolution; entry.rebase(); return null; }
    if (!button.hasAttribute('data-save-technical-decision')) return undefined;
    if (entry.stale || !entry.reviewToken || !entry.resolution.value.trim()) {
      error.textContent = entry.stale ? 'Review the changed evidence before saving these edits.' : 'Enter an answer before saving.';
      error.hidden = false; return null;
    }
    entry.remember();
    return { command: 'save-technical-decision', value: JSON.stringify({ id: entry.id, reviewToken: entry.reviewToken, resolution: entry.resolution.value.trim() }) };
  } };
}

module.exports = { renderTechnicalDecisionForm, technicalDecisionFormScript: () => '(' + bindTechnicalDecisionForms.toString() + ')(vscode)' };
