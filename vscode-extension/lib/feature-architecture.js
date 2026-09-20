'use strict';
const { escapeHtml: h } = require('./security');

function architectureGallery(result, { busy = false, dirty = false, standalone = false } = {}) {
  const diagrams = result?.diagrams || [];
  return `<section class="card feature-architecture"><h2>Feature C4 diagrams</h2>
    <p>Zoom from the feature's users and external systems into the product applications, then into the proposed feature responsibilities. Existing elements retain their evidence status; proposed changes require review.</p>
    ${!standalone ? `<button type="button" data-wizard-action="generate-architecture" ${busy ? 'disabled' : ''}>Save answers and ${diagrams.length ? 'regenerate' : 'generate'} C4 diagrams</button><p class="muted">Uses the feature BRD, the architecture answers below, saved technical and integration direction, and the existing product architecture. Generation uses your local model.</p>` : ''}
    ${result?.status === 'stale' ? '<p class="notice warning">The feature direction or product architecture changed. Regenerate to update these diagrams.</p>' : ''}
    ${dirty ? '<p class="notice">There are unsaved architecture answers. Generate diagrams to save and include the displayed direction.</p>' : ''}
    ${!diagrams.length ? '<p>No feature diagrams generated yet. Review the suggested architecture answers below, then generate the C4 views.</p>' : ''}
    ${result?.summary ? `<p>${h(result.summary)}</p>` : ''}
    ${result?.warnings?.length ? `<details><summary>Basis and limitations</summary>${result.warnings.map(w => `<p>${h(w)}</p>`).join('')}</details>` : ''}
    ${diagrams.map(diagram => `<figure class="feature-diagram"><figcaption><h3>${h(diagram.title)}</h3></figcaption><div class="diagram-image ${standalone ? 'diagram-full' : ''}"><img src="data:image/svg+xml;base64,${Buffer.from(diagram.svg, 'utf8').toString('base64')}" alt="${h(diagram.title)}"></div>
      <div class="actions">${!standalone ? `<button type="button" class="secondary" data-wizard-action="open-feature-diagram" data-value="${h(diagram.id)}" ${busy ? 'disabled' : ''}>Open diagram in new tab ↗</button><button type="button" class="secondary" data-wizard-action="save-feature-diagram" data-value="${h(diagram.id)}" ${busy ? 'disabled' : ''}>Save SVG</button>` : `<button type="button" class="secondary" data-command="save-feature-diagram" data-value="${h(diagram.id)}">Save SVG</button>`}</div><p class="muted">${h(diagram.notes)}</p></figure>`).join('')}
    <p class="muted">These are feature-definition drafts. Generating them does not approve or replace the product architecture.</p></section>`;
}
const ARCHITECTURE_STYLES = '.feature-diagram{margin:24px 0 0;padding:14px;border:1px solid var(--vscode-panel-border);border-radius:8px;min-width:0}.diagram-image{max-width:100%;overflow:auto;background:#f8fafc}.diagram-image img{display:block;width:100%;height:auto}.diagram-full img{width:auto;max-width:none}.feature-diagram .actions{margin-top:12px}.feature-diagram p{overflow-wrap:anywhere}';
module.exports = { architectureGallery, ARCHITECTURE_STYLES };
