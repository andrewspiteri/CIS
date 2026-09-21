'use strict';

const { escapeHtml: h } = require('./security');

const labels = { reuse: 'Reuse existing', extend: 'Extend existing', new: 'New work', 'out-of-scope': 'Exclude from this feature', conflict: 'Scope conflict', unresolved: 'Assessment inconclusive' };
const assessmentLabels = { 'not-assessed': 'Not yet assessed', 'no-matching-evidence': 'No matching code found', inconclusive: 'Assessment inconclusive', 'ownership-conflict': 'Scope conflict' };

function decisionDraft(result, story, drafts = {}) {
  if (drafts[story.id]) return drafts[story.id];
  if (story.review) return { treatment: story.review.treatment, owners: story.review.owners, plan: story.review.plan, evidencePaths: Object.keys(story.review.evidenceHashes || {}).join('\n') };
  const treatment = ['reuse', 'extend', 'new'].includes(story.treatment) ? story.treatment : story.evidenceIds?.length ? 'extend' : 'new';
  return { treatment, owners: treatment === 'new' ? [result.featureRepositoryId].filter(Boolean) : story.owners || [],
    plan: story.treatment !== 'unresolved' && story.remainingWork ? story.remainingWork
      : `${treatment === 'new' ? 'Implement' : 'Review and extend existing support for'} ${story.title}.\n\n${(story.requirements || []).slice(0, 4).map(text => '- ' + text).join('\n')}`,
    evidencePaths: (story.evidenceIds || []).map(id => result.evidence?.find(e => e.id === id)).filter(Boolean).map(e => `${e.repositoryId}/${e.path}`).join('\n') };
}

function decisionEditor(result, story, drafts, busy) {
  const draft = decisionDraft(result, story, drafts);
  return `<details class="delivery-decision" ${drafts[story.id] ? 'open' : ''}><summary>${story.review ? 'Review or change saved planning decision' : 'Resolve the delivery decision'}</summary>
    <p>Review the prefilled suggestion. Saving records how this story will be delivered; it does not mark the implementation complete or approve the feature.</p>
    <div data-delivery-story="${h(story.id)}" ${drafts[story.id] ? 'data-delivery-edited="true"' : ''}><label>Planned treatment<select data-delivery-field="treatment">${['new', 'extend', 'reuse', 'out-of-scope'].map(t => `<option value="${t}" ${draft.treatment === t ? 'selected' : ''}>${labels[t]}</option>`).join('')}</select></label>
    <fieldset class="delivery-owners"><legend>Repositories responsible</legend>${(result.repositoryIds || []).map(id => `<label class="delivery-owner"><input type="checkbox" data-delivery-owner value="${h(id)}" ${draft.owners?.includes(id) ? 'checked' : ''}><span>${h(id)}</span></label>`).join('')}</fieldset>
    <label>Planned work and acceptance<textarea data-delivery-field="plan" rows="7" maxlength="4000">${h(draft.plan)}</textarea></label>
    <label>Supporting code paths<textarea data-delivery-field="evidencePaths" rows="3" maxlength="7200">${h(draft.evidencePaths)}</textarea></label>
    <p class="muted">For reuse or extension, keep the relevant references or enter one per line as repository-id/path/to/file. New work needs no existing-code reference. This planning choice and description are saved together; no separate reason is required.</p>
    <div class="actions"><button type="button" data-wizard-action="save-delivery-decision" data-value="${h(story.id)}" ${busy ? 'disabled' : ''}>Save story decision</button>${drafts[story.id] ? `<button type="button" class="secondary" data-wizard-action="discard-delivery-decision" data-value="${h(story.id)}" ${busy ? 'disabled' : ''}>Discard story draft</button>` : ''}</div></div></details>`;
}

function deliveryAssessment(result, { busy = false, drafts = {}, focus } = {}) {
  const current = result?.status === 'current';
  const hasEvidence = (result?.stories || []).length > 0;
  return `<section class="card feature-delivery-assessment"><h2>Existing capability and remaining work</h2>
    <p>A requirement in the BRD may already be implemented. Compare it with code in all product-owned repositories before deciding what to build. Release categories do not establish whether work is new.</p>
    <p>${current ? 'Review the proposed ownership and implementation changes below. Conflicts require a scope decision; this comparison does not approve or complete a story.' : result?.status === 'stale' ? 'The BRD, saved direction or implementation changed. Reconcile again to review current evidence.' : 'These story outlines have not been reconciled with implementation. CIS has not established which capabilities need building.'}</p>
    <p class="muted">Reconcile saves only the ownership direction entered above, then uses a local model. Other answers and story edits stay in the form. Results are cached; use them as a draft and save after review.</p>
    <p>Uncertainty in this assessment is not a confirmed gap in the product. Open a story to see why it is inconclusive, review its requirements and code, then save a delivery decision.</p>
    <div class="actions"><button type="button" data-wizard-action="reconcile-delivery" ${busy ? 'disabled' : ''}>Reconcile with existing implementation</button>${current || result?.stories?.some(s => s.reviewCurrent) ? `<button type="button" class="secondary" data-wizard-action="use-delivery-assessment" ${busy || !Object.keys(result.suggestedAnswers || {}).length ? 'disabled' : ''}>Use reconciled stories as draft</button>` : ''}</div>
    ${(result?.warnings || []).map(w => `<p class="notice warning">${h(w)}</p>`).join('')}
    ${hasEvidence ? `<ol class="feature-story-list">${(result.stories || []).map(s => `<li ${focus === s.id ? 'data-delivery-focus' : ''}><details class="feature-story" ${drafts[s.id] || focus === s.id ? 'open' : ''}><summary>${h(s.title)} · ${h(s.reviewCurrent ? labels[s.review.treatment] + ' · Decision saved' : s.review ? 'Saved decision needs recheck' : assessmentLabels[s.assessmentState] || labels[s.treatment] || labels.unresolved)}</summary>
      ${s.review ? `<p class="notice ${s.reviewCurrent ? '' : 'warning'}">${s.reviewCurrent ? 'Planning decision saved' : 'Evidence changed; review and save this choice again'} — ${h(s.review.actor)}. ${h(s.review.plan)}</p>` : ''}
      <p><strong>Assessment basis:</strong> ${h(s.assessmentReason || 'This is a model proposal, not proof of requirement coverage.')}</p>
      <p><strong>Existing capability:</strong> ${h(s.existingCapability)}</p><p><strong>Remaining work:</strong> ${h(s.remainingWork)}</p>
      <p><strong>Repositories identified by the assessment:</strong> ${h(s.owners?.join(', ') || 'No owner established')}</p>
      <details><summary>Requirements to verify (${s.requirements?.length || 0})</summary><ul>${(s.requirements || []).map(text => `<li>${h(text)}</li>`).join('')}</ul></details>
      ${s.conflict ? `<p class="notice warning"><strong>Scope conflict:</strong> ${h(s.conflict)}</p>` : ''}
      <details><summary>Implementation evidence</summary>${(s.evidenceIds || []).map(id => result.evidence?.find(e => e.id === id)).filter(Boolean).map(e => `<p><button type="button" class="link" data-wizard-action="open-delivery-evidence" data-value="${h(e.id)}">${h(e.repositoryId)} · ${h(e.path)} ↗</button></p><pre>${h(e.excerpt)}</pre>`).join('') || '<p>No matching excerpt was found in the bounded search. This does not prove that the capability is absent.</p>'}</details>
      ${decisionEditor(result, s, drafts, busy)}
    </details></li>`).join('')}</ol>` : ''}</section>`;
}

function reconciledDrafts(model) {
  const result = model.featureDelivery;
  if (result?.status !== 'current' && !result?.stories?.some(s => s.reviewCurrent)) throw new Error('Reconcile current implementation before using these stories.');
  const fields = ['delivery-stories-foundation', 'delivery-stories-mvp', 'delivery-stories-post-mvp'];
  const values = Object.fromEntries(fields.filter(id => typeof result.suggestedAnswers?.[id] === 'string').map(id => [id, result.suggestedAnswers[id]]));
  if (!Object.keys(values).length || Object.values(values).some(v => v.length > 24000)) throw new Error('The reconciled lists are unavailable or exceed the answer limit.');
  return { ...(model.pageDrafts?.delivery || {}), ...values };
}

function deliveryScript() {
  return `document.querySelectorAll('[data-delivery-story]').forEach(row=>row.addEventListener('input',()=>{row.dataset.deliveryEdited='true';}));
  const deliveryFocus=document.querySelector('[data-delivery-focus]'); if(deliveryFocus) requestAnimationFrame(()=>deliveryFocus.scrollIntoView({block:'center'}));
  function deliveryFields() { return Object.fromEntries([...document.querySelectorAll('[data-delivery-story][data-delivery-edited]')].map(row => {
    const get = key => row.querySelector('[data-delivery-field="' + key + '"]');
    return [row.dataset.deliveryStory, { treatment:get('treatment').value, owners:[...row.querySelectorAll('[data-delivery-owner]:checked')].map(option=>option.value), plan:get('plan').value, evidencePaths:get('evidencePaths').value }];
  })); }`;
}

const DELIVERY_STYLES = '.delivery-decision label{display:grid;gap:.4rem;margin-top:.8rem}.delivery-decision .delivery-owner{display:flex;align-items:center;gap:.5rem;margin:.3rem 0;font-weight:400}.delivery-owner input{width:auto}.delivery-owner span{overflow-wrap:anywhere}.delivery-decision select,.delivery-decision textarea{width:100%;min-width:0;font:inherit;padding:.6rem;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border)}';
module.exports = { deliveryAssessment, reconciledDrafts, decisionDraft, deliveryScript, DELIVERY_STYLES };
