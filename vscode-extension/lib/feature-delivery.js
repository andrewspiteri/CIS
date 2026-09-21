'use strict';

const { escapeHtml: h } = require('./security');

const labels = { reuse: 'Reuse existing', extend: 'Extend existing', new: 'New implementation proposed', conflict: 'Scope conflict', unresolved: 'Needs investigation' };

function deliveryAssessment(result, { busy = false } = {}) {
  const current = result?.status === 'current';
  const hasEvidence = (result?.stories || []).length > 0;
  return `<section class="card feature-delivery-assessment"><h2>Existing capability and remaining work</h2>
    <p>A requirement in the BRD may already be implemented. Compare it with code in all product-owned repositories before deciding what to build. Release categories do not establish whether work is new.</p>
    <p>${current ? 'Review the proposed ownership and implementation changes below. Conflicts require a scope decision; this comparison does not approve or complete a story.' : result?.status === 'stale' ? 'The BRD, saved direction or implementation changed. Reconcile again to review current evidence.' : 'These story outlines have not been reconciled with implementation. CIS has not established which capabilities need building.'}</p>
    <p class="muted">Reconcile saves only the ownership direction entered above, then uses a local model. Other answers and story edits stay in the form. Results are cached; use them as a draft and save after review.</p>
    <div class="actions"><button type="button" data-wizard-action="reconcile-delivery" ${busy ? 'disabled' : ''}>Reconcile with existing implementation</button>${current ? `<button type="button" class="secondary" data-wizard-action="use-delivery-assessment" ${busy || !Object.keys(result.suggestedAnswers || {}).length ? 'disabled' : ''}>Use reconciled stories as draft</button>` : ''}</div>
    ${(result?.warnings || []).map(w => `<p class="notice warning">${h(w)}</p>`).join('')}
    ${hasEvidence ? `<ol class="feature-story-list">${(result.stories || []).map(s => `<li><details class="feature-story"><summary>${h(s.title)} · ${h(labels[s.treatment] || labels.unresolved)}</summary>
      <p><strong>Existing capability:</strong> ${h(s.existingCapability)}</p><p><strong>Remaining work:</strong> ${h(s.remainingWork)}</p>
      <p><strong>Owning repositories:</strong> ${h(s.owners?.join(', ') || 'To determine')}</p>
      ${s.conflict ? `<p class="notice warning"><strong>Scope conflict:</strong> ${h(s.conflict)}</p>` : ''}
      <details><summary>Implementation evidence</summary>${(s.evidenceIds || []).map(id => result.evidence?.find(e => e.id === id)).filter(Boolean).map(e => `<p>${h(e.repositoryId)} · ${h(e.path)}</p><pre>${h(e.excerpt)}</pre>`).join('') || '<p>No supporting code excerpt was established.</p>'}</details>
    </details></li>`).join('')}</ol>` : ''}</section>`;
}

function reconciledDrafts(model) {
  const result = model.featureDelivery;
  if (result?.status !== 'current') throw new Error('Reconcile current implementation before using these stories.');
  const fields = ['delivery-stories-foundation', 'delivery-stories-mvp', 'delivery-stories-post-mvp'];
  const values = Object.fromEntries(fields.filter(id => typeof result.suggestedAnswers?.[id] === 'string').map(id => [id, result.suggestedAnswers[id]]));
  if (!Object.keys(values).length || Object.values(values).some(v => v.length > 24000)) throw new Error('The reconciled lists are unavailable or exceed the answer limit.');
  return { ...(model.pageDrafts?.delivery || {}), ...values };
}

module.exports = { deliveryAssessment, reconciledDrafts };
