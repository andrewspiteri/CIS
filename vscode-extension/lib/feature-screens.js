'use strict';

const { escapeHtml: h } = require('./security');
const { mountUiControlSheets, controlSheetExport } = require('./ui-control-sheet');

function gallery(result, { busy = false, dirty = false, standalone = false, drafts = {} } = {}) {
  const screens = result?.screens || [];
  const reviews = new Map((result?.reviews || []).map(review => [review.key, review]));
  const stale = result?.status === 'stale';
  return `<section class="card feature-screens"><h2>Expected screens</h2>
    <p>Proposed screens for this feature, using the current BRD, saved experience answers and the discovered UI baseline. Review the layouts and behaviour below.</p>
    ${stale ? '<p class="notice warning">The saved feature context changed. Generate screens again before reviewing these previews.</p>' : ''}
    ${dirty ? '<p class="notice">You have unsaved experience answers. Screen feedback applies to the displayed previews using the saved direction. Save answers and regenerate screens to include your questionnaire edits.</p>' : ''}
    ${!standalone ? `<button type="button" data-wizard-action="generate-screens" ${busy ? 'disabled' : ''}>${screens.length ? 'Save answers and regenerate screens' : 'Save answers and generate screens'}</button><p class="muted">Uses your local model. This saves the experience answers shown below; screen generation does not approve a delivery design.</p>` : ''}
    ${!screens.length ? `<p>${result?.status === 'no-ui' ? 'No UI changes were proposed from the supplied direction.' : 'No screens generated yet. Review the suggested experience answers, then generate the proposed screens.'}</p>` : ''}
    ${result?.warnings?.length ? `<details><summary>Preview basis and limitations</summary>${result.warnings.map(warning => `<p class="muted">${h(warning)}</p>`).join('')}</details>` : ''}
    <div class="feature-screen-grid">${screens.map((screen, index) => { const review = reviews.get(screen.key); const rejected = review?.decision === 'not-needed'; return `<${rejected ? 'details' : 'figure'} class="feature-screen" data-control-sheet="${index}" data-source-hash="${h(result.inputHash)}">
      ${rejected ? `<summary>Not needed: ${h(screen.plan.title)}</summary>` : ''}<figcaption><h3>${h(screen.plan.title)}</h3><p>${h(screen.plan.frontendType)} · Proposed route: <code>${h(screen.plan.route)}</code></p><p>${h(screen.plan.purpose)}</p></figcaption>
      <img src="data:image/svg+xml;base64,${Buffer.from(screen.svg || '', 'utf8').toString('base64')}" alt="Proposed ${h(screen.plan.title)} screen" width="1400" height="960">
      <p class="muted" data-render-status>Rendering JPG…</p><div class="actions">${!standalone ? `<button type="button" class="secondary" data-wizard-action="open-feature-screen" data-value="${index}" ${busy ? 'disabled' : ''}>Open screen in new tab ↗</button>` : ''}<button type="button" class="secondary" data-save-feature-screen="${index}" ${busy ? 'disabled' : ''}>Save JPG</button></div>
      ${review ? `<p class="notice">${rejected ? 'Not needed' : screen.appliedReviewId === review.id ? 'Updated - ready for your review' : 'Changes requested - previous image retained until applied'} · ${h(review.actor)}</p>` : ''}
      ${standalone ? '<button type="button" class="secondary" data-return-feature>Review or amend in the feature wizard</button>' : reviewControls(result, screen, review, drafts, busy, stale)}
      <p class="muted">UI baseline: ${h(screen.baselineRepository)}</p>
      <details><summary>Actions and states to review</summary><ul>${(screen.plan.actions || []).map(action => `<li><strong>${h(action.label)}</strong>: ${h(action.destination)}</li>`).join('')}${(screen.plan.states || []).map(state => `<li>${h(state)}</li>`).join('')}</ul><p class="muted">Source passage: ${h(screen.plan.evidence)}</p></details>
    </${rejected ? 'details' : 'figure'}>`; }).join('')}${(standalone ? [] : [...reviews.values()]).filter(review => review.decision === 'not-needed' && !screens.some(screen => screen.key === review.key)).map(review => `<details class="feature-screen"><summary>Not needed: ${h(review.title)}</summary>${reviewControls(result, null, review, drafts, busy, stale)}</details>`).join('')}</div></section>`;
}

function reviewControls(result, screen, review, drafts, busy, stale) {
  const rejected = review?.decision === 'not-needed', key = screen?.key || review?.key;
  const id = screen?.plan.id || key, revision = screen?.revision || review?.screenRevision;
  const disabled = busy || stale ? 'disabled' : '';
  return `<div class="screen-review" data-screen-review-key="${h(key)}" data-screen-id="${h(id)}" data-screen-revision="${h(revision)}" data-preview-hash="${h(result?.inputHash || '')}">
    <label for="feedback-${h(id)}">${rejected ? 'Review note (optional)' : 'What should change on this screen?'}</label>
    <textarea id="feedback-${h(id)}" data-screen-feedback="${h(key)}" maxlength="2000" rows="3" placeholder="For example: remove the mobile field, rename Submit to Continue, or use a table instead of a form." ${busy ? 'disabled' : ''}>${h(drafts[key] ?? review?.feedback ?? '')}</textarea>
    <div class="actions">${rejected ? `<button type="button" data-screen-decision="restore" ${busy ? 'disabled' : ''}>Include this screen again</button>` : `<button type="button" data-screen-decision="amend" ${disabled}>Apply changes to this screen</button><button type="button" class="secondary" data-screen-decision="not-needed" ${disabled}>Not needed</button>`}</div>
    ${!rejected && stale ? `<p class="notice warning">This preview is out of date. Regenerate it before applying feedback.</p><button type="button" class="secondary" data-wizard-action="generate-screens" ${busy ? 'disabled' : ''}>Save answers and regenerate screens</button>` : ''}
    <p class="muted">${rejected ? 'Excluded from the proposed screen set. This choice is saved with the feature.' : 'Feedback is saved with the feature. Applying changes regenerates this screen only.'}</p></div>`;
}

function screenScript() {
  return `const featureSheets = (${mountUiControlSheets.toString()})(document, Image, message => vscode.postMessage({ ...message, command: 'save-feature-screen' }));
    document.querySelectorAll('[data-screen-decision]').forEach(button => button.addEventListener('click', () => {
      const card = button.closest('[data-screen-review-key]');
      const feedback = card.querySelector('[data-screen-feedback]').value;
      if (button.dataset.screenDecision === 'amend' && !feedback.trim()) {
        card.querySelector('textarea').setCustomValidity('Describe the changes you want.'); card.querySelector('textarea').reportValidity(); return;
      }
      postWizard('review-feature-screen', JSON.stringify({ screenId: card.dataset.screenId, screenRevision: card.dataset.screenRevision,
        previewHash: card.dataset.previewHash, decision: button.dataset.screenDecision, feedback }));
    }));
    document.querySelectorAll('[data-screen-feedback]').forEach(input => input.addEventListener('input', () => input.setCustomValidity('')));
    document.querySelectorAll('[data-return-feature]').forEach(button => button.addEventListener('click', () => vscode.postMessage({ command: 'return-to-feature' })));
    document.querySelectorAll('[data-save-feature-screen]').forEach(button => button.addEventListener('click', () => featureSheets.save(button.dataset.saveFeatureScreen)));`;
}

function exportScreen(result, value) {
  const baseline = { sourceHash: result?.inputHash, repositories: (result?.screens || []).map(screen => ({ id: screen.plan.id, preview: true })) };
  const image = controlSheetExport(baseline, value);
  return { ...image, filename: image.filename.replace('-ui-controls.jpg', '-proposed-screen.jpg') };
}

const SCREEN_STYLES = `.feature-screen-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr));gap:20px;margin-top:20px}.feature-screen{margin:0;padding:16px;border:1px solid var(--vscode-panel-border);border-radius:8px;min-width:0}.feature-screen img{display:block;width:100%;height:auto;background:white;border:1px solid var(--vscode-panel-border)}.feature-screen code{overflow-wrap:anywhere}.feature-screen figcaption p{overflow-wrap:anywhere}.screen-review{margin:18px 0;padding-top:14px;border-top:1px solid var(--vscode-panel-border)}.screen-review label{display:block;font-weight:600}.screen-review textarea{display:block;box-sizing:border-box;width:100%;margin:8px 0;font:inherit;color:var(--vscode-input-foreground,var(--vscode-foreground));background:var(--vscode-input-background,var(--vscode-editor-background));border:1px solid var(--vscode-input-border,var(--vscode-panel-border));padding:10px;resize:vertical}.feature-screen>summary{cursor:pointer;font-weight:600}`;
module.exports = { gallery, screenScript, exportScreen, SCREEN_STYLES };
