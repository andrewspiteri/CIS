'use strict';
const { escapeHtml: escape } = require('./security');

function architectureApproval(model) {
  const page = id => model.pages?.find(item => item.id === id);
  const business = page('business'), technical = page('technical'), architecture = page('architecture');
  const active = item => item?.status === 'Active' && item.complete && item.current;
  const ready = item => item?.complete && item.current;
  const diagramsReady = model.diagrams?.length > 0 && model.diagrams.every(item => item.status !== 'Stale');
  const approved = active(architecture) && model.diagrams?.length > 0 && model.diagrams.every(item => item.status === 'Active');
  const blockers = [...(architecture?.issues || [])];
  if (!active(business)) blockers.unshift('Approve the business requirements before approving technical intent and architecture.');
  if (!active(technical)) blockers.push('Approve the technical intent before approving architecture.');
  if (!ready(architecture) && !blockers.some(item => /architecture|solution|design/iu.test(item))) blockers.push('Prepare or reconcile the architecture until its evidence is complete and current.');
  if (!diagramsReady) blockers.push('Prepare or refresh the architecture diagrams.');
  return { business, technical, architecture, approved, businessApproved: active(business), technicalApproved: active(technical), blockers: [...new Set(blockers)],
    canApproveBusiness: ready(business) && !active(business),
    canApproveTechnical: active(business) && ready(technical) && !active(technical),
    canApproveArchitecture: active(business) && active(technical) && ready(architecture) && diagramsReady && !approved };
}

const review = (page, label) => page?.primaryPath ? `<button type="button" class="secondary" data-command="open-path" data-value="${escape(page.primaryPath)}">${label}</button>` : '';
const navigate = (page, label) => `<button type="button" class="secondary" data-command="navigate" data-value="${page}">Go to ${label}</button>`;
const approve = (page, label, enabled) => `<button type="button" data-command="${page}-action" data-value="approve-${page}" ${enabled ? '' : 'disabled'}>${label}</button>`;
const notice = (model, page) => model.definitionApprovalNotice?.page === page ? `<p class="notice" role="status">${escape(model.definitionApprovalNotice.message)}</p>` : '';

function renderDocumentApproval(model, pageId) {
  if (!['business', 'technical'].includes(pageId)) return '';
  const state = architectureApproval(model);
  const page = state[pageId];
  const business = pageId === 'business';
  const label = business ? 'business requirements' : 'technical intent';
  const title = business ? 'Business requirements' : 'Technical intent';
  const approved = business ? state.businessApproved : state.technicalApproved;
  const enabled = business ? state.canApproveBusiness : state.canApproveTechnical;
  const blockers = [...(page?.issues || [])];
  if (!page?.complete || !page?.current) {
    if (!blockers.length) blockers.push(`Complete the ${business ? 'business review' : 'technical review'} and refresh its evidence before approval.`);
  }
  if (!business && !state.businessApproved) blockers.unshift('Approve the business requirements in Business definition first.');
  return `<section class="card" aria-labelledby="${pageId}-approval-title"><h3 id="${pageId}-approval-title">${approved ? `${title} approved` : `Review and approve ${label}`}</h3>
    <p>${business ? 'Review the BRD and approve its current business requirements before approving technical intent.' : 'Review the technical-intent document and approve its current direction before approving solution architecture.'}</p>
    ${!approved && blockers.length ? `<ul>${[...new Set(blockers)].map(item => `<li>${escape(item)}</li>`).join('')}</ul>` : ''}
    ${!business && !state.businessApproved ? `<div class="actions">${navigate('business', 'Business definition')}</div>` : ''}
    <div class="actions">${review(page, `Review ${label}`)}${approve(pageId, approved ? `${title} approved` : `Approve ${label}`, enabled)}</div>
    ${notice(model, pageId)}
  </section>`;
}

function renderArchitectureApproval(model) {
  const state = architectureApproval(model);
  const files = (state.architecture?.artifactPaths || []).filter(file => file !== state.architecture.primaryPath);
  return `<section class="card" aria-labelledby="architecture-approval-title"><h3 id="architecture-approval-title">${state.approved ? 'Architecture approved' : 'Review and approve architecture'}</h3>
    <p>The overall design, component sheet and C4 diagrams are approved together. Approve the BRD in Business definition and technical intent in Technical direction first. Final product activation remains on Review and activate after the remaining pages are complete.</p>
    ${!state.approved && state.blockers.length ? `<ul>${state.blockers.map(item => `<li>${escape(item)}</li>`).join('')}</ul>` : ''}
    <div class="actions">${review(state.architecture, 'Review overall design')}${files.map(file => `<button type="button" class="secondary" data-command="open-path" data-value="${escape(file)}">${file.endsWith('component-sheet.md') ? 'Review component sheet' : 'Review diagrams'}</button>`).join('')}</div>
    ${!state.businessApproved || !state.technicalApproved ? `<div class="actions">${!state.businessApproved ? navigate('business', 'Business definition') : ''}${!state.technicalApproved ? navigate('technical', 'Technical direction') : ''}</div>` : ''}
    <div class="actions">${approve('architecture', state.approved ? 'Architecture and diagrams approved' : 'Approve architecture and diagrams', state.canApproveArchitecture)}</div>
    ${notice(model, 'architecture')}
    ${model.architecturePreparationNotice ? `<p class="notice" role="status">${escape(model.architecturePreparationNotice)}</p>` : ''}
  </section>`;
}
module.exports = { architectureApproval, renderDocumentApproval, renderArchitectureApproval };
