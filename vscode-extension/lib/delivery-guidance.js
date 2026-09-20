'use strict';
const { escapeHtml: escape } = require('./security');

function renderDeliveryPage(page, model) {
  const actions = page.guidance?.actions || [];
  const action = (id, fallback) => {
    const item = actions.find(candidate => candidate.id === id);
    return `<article class="card"><h3>${escape(item?.label || fallback)}</h3><p>${escape(item?.reason || 'Refresh to check delivery-scope readiness.')}</p>
      <div class="actions"><button type="button" data-command="delivery-action" data-value="${id}" ${!item || item.status === 'Blocked' ? 'disabled' : ''}>${escape(item?.label || fallback)}</button>
      <span class="badge ${item?.status === 'Complete' ? 'good' : ''}">${escape(item?.status || 'Unchecked')}</span></div></article>`;
  };
  const prerequisites = (model.pages || []).filter(item => ['business', 'technical', 'architecture', 'experience'].includes(item.id) && (!item.complete || !item.current));
  return `${model.deliveryNotice ? `<p class="notice" role="status">${escape(model.deliveryNotice)}</p>` : ''}
    <p>Candidate outcomes include repository routing, frontend classifications, dependencies and shared quality and operational obligations.</p>
    ${prerequisites.length ? `<div class="actions">${prerequisites.map(item => `<button type="button" class="secondary" data-command="navigate" data-value="${escape(item.id)}">Review ${escape(item.title)}</button>`).join('')}</div>` : ''}
    <section class="cards two">${action('build-backlog', 'Create candidate backlog')}${action('record-no-work', 'Record no planned work')}</section>
    ${page.status !== 'Missing' && page.primaryPath ? `<div class="actions"><button type="button" class="secondary" data-command="open-path" data-value="${escape(page.primaryPath)}">Review delivery scope</button></div>` : ''}`;
}
module.exports = { renderDeliveryPage };
