'use strict';

const { escapeHtml: h } = require('./security');
const { renderReviewText } = require('./feature-review-text');
const { gallery } = require('./feature-screens');
const { architectureGallery } = require('./feature-architecture');
const { isStoryField, renderStoryList } = require('./feature-stories');

const STEPS = [
  ['foundation', 'Feature foundation'], ['business', 'Business definition'], ['technical', 'Technical direction'],
  ['architecture', 'Solution architecture and diagrams'], ['contracts', 'Integrations and dictionaries'],
  ['experience', 'Experience direction and UI impact'], ['delivery', 'Delivery and acceptance'], ['review', 'Review and next steps'],
];

function hasUnsavedChanges(model, page) {
  const fields = page?.fields || [];
  return (page?.id === 'delivery' && model.repositoryWorkDraft !== undefined
    && JSON.stringify(model.repositoryWorkDraft) !== JSON.stringify(model.wizard?.repositoryWork || []))
    || Object.entries(model.pageDrafts?.[page?.id] || {}).some(([id, value]) => {
    const field = fields.find(field => field.id === id);
    return field && value !== (field.answer ?? field.suggestedAnswer ?? '');
  });
}

function renderRepositoryWork(model) {
  const work = model.repositoryWorkDraft || model.wizard?.repositoryWork || [];
  const repositories = (model.wizard?.repositories || model.repositories || []).filter(repo => repo.role === 'participant' && repo.participation === 'owned');
  return `<section class="card" id="repository-work"><h2>Repository feature breakdown</h2><p>Split this high-level feature into bounded work for each repository. Several entries can target the same repository. Declare dependencies where work must follow another entry; independent entries can be planned in parallel.</p><p class="muted">These entries describe proposed scope. Linked change dossiers keep their own delivery status and approval gates.</p>
    ${work.map(item => `<fieldset class="repository-work-item" data-work-id="${h(item.id)}" ${model.busy ? 'disabled' : ''}><legend>${h(item.id)}</legend>
      <label>Repository<select data-work-field="repositoryId">${repositories.map(repo => `<option value="${h(repo.id)}" ${repo.id === item.repositoryId ? 'selected' : ''}>${h(repo.id)}</option>`).join('')}</select></label>
      <label>Repository feature<input data-work-field="title" maxlength="200" value="${h(item.title)}" placeholder="For example: issue and validate referral codes"></label>
      <label>Scope and acceptance<textarea data-work-field="scope" maxlength="24000" rows="4">${h(item.scope)}</textarea></label>
      <label>Depends on<select data-work-field="dependsOn" multiple>${work.filter(other => other.id !== item.id).map(other => `<option value="${h(other.id)}" ${item.dependsOn.includes(other.id) ? 'selected' : ''}>${h(other.id)} · ${h(other.title || other.repositoryId)}</option>`).join('')}</select></label>
      <label>Linked delivery changes<select data-work-field="changeIds" multiple>${[...new Set([...(model.changes || []).map(change => change.id), ...item.changeIds])].map(id => `<option value="${h(id)}" ${item.changeIds.includes(id) ? 'selected' : ''}>${h(id)}${(model.changes || []).find(change => change.id === id)?.title ? ' · ' + h(model.changes.find(change => change.id === id).title) : ''}</option>`).join('')}</select></label>
      <button type="button" class="secondary" data-wizard-action="remove-repository-work" data-value="${h(item.id)}">Remove proposed item</button></fieldset>`).join('')}
    ${!work.length ? '<p>No repository work defined yet.</p>' : ''}<button type="button" class="secondary" data-wizard-action="add-repository-work" ${model.busy || !repositories.length ? 'disabled' : ''}>Add repository feature</button><p class="muted">Use Save reviewed answers below to save this breakdown with the delivery page.</p></section>`;
}

function navigation(model) {
  return `<nav class="wizard-steps" aria-label="Feature definition steps">${STEPS.map(([id, title], index) => {
    const page = model.wizard?.pages?.find(page => page.id === id);
    const dirty = hasUnsavedChanges(model, page);
    const current = (model.page || 'foundation') === id;
    return `<button type="button" class="step ${current ? 'current' : ''}" data-wizard-action="navigate" data-value="${id}" ${current ? 'aria-current="step"' : ''} ${model.busy ? 'disabled' : ''}><span>${index + 1}</span><span><strong>${title}</strong><small>${h(dirty ? 'Unsaved changes' : page?.status || (id === 'foundation' ? 'Setup needed' : 'After setup'))}</small></span><i class="${page?.complete && !dirty ? 'complete' : ''}" aria-hidden="true"></i></button>`;
  }).join('')}</nav>`;
}

function renderReviewPage(model) {
  const current = model.wizard?.pages?.find(page => page.id === model.page);
  if (!current) return '<section class="card"><h2>Feature definition</h2><p>Complete feature foundation to begin reviewing this feature.</p></section>';
  const pageDraft = model.pageDrafts?.[current.id] || {};
  const stories = current.id === 'delivery' ? current.fields.filter(isStoryField) : [];
  const questions = current.fields.filter(field => field.id !== 'summary' && !stories.includes(field));
  const summary = current.fields.find(field => field.id === 'summary');
  const structured = questions.some(field => !field.id.startsWith('decision-'));
  const field = (f, index) => {
    const storyGroup = stories.includes(f);
    const value = pageDraft[f.id] ?? f.answer ?? f.suggestedAnswer ?? '';
    const edited = pageDraft[f.id] !== undefined && pageDraft[f.id] !== (f.answer ?? f.suggestedAnswer ?? '');
    const label = edited ? 'Unsaved edit' : f.answer != null ? 'Saved answer' : f.suggestedAnswer ? 'Suggested from available context' : f.required === false ? 'Optional follow-up' : 'Your answer is needed';
    const contextDiffers = f.answer != null && f.suggestedAnswer && f.answer !== f.suggestedAnswer;
    return `<article class="question-card feature-question ${storyGroup ? 'feature-story-group' : ''}" id="question-${h(f.id)}"><span class="eyebrow">${index ? `Question ${index} of ${questions.length} · ` : ''}${h(label)}</span><h3 id="label-${h(f.id)}">${h(f.label)}</h3>
      ${value ? storyGroup ? renderStoryList(value, { fieldId: f.id, busy: model.busy, focusIndex: model.storyMoveFocus?.fieldId === f.id ? model.storyMoveFocus.index : undefined }) : `<div class="feature-review-text">${renderReviewText(value)}</div>` : `<p class="muted">${storyGroup ? 'The BRD does not establish this story list. Add the required stories, or explicitly record why none are needed.' : f.required === false ? 'Add any further direction you want to record.' : 'The available documents do not establish this answer. Record the proposed direction below.'}</p>`}
      ${contextDiffers ? `<details class="feature-source-context"><summary>Compare with the current BRD and product context</summary><div class="feature-review-text">${renderReviewText(f.suggestedAnswer)}</div><button type="button" class="secondary" data-wizard-action="use-suggestion" data-value="${h(f.id)}" ${model.busy ? 'disabled' : ''}>Use this text as my draft</button></details>` : ''}
      <details class="feature-answer-editor" ${!value || edited && !storyGroup ? 'open' : ''}><summary>${storyGroup ? 'Edit user stories' : value ? 'Edit answer' : 'Enter answer'}</summary><label class="muted" for="answer-${h(f.id)}">${storyGroup ? 'Edit the prefilled stories and acceptance outlines. Use the card buttons to move stories between MVP and Post-MVP, then save this page to keep the changes.' : value ? 'Review and edit the prefilled text. Saving records your reviewed answer.' : 'Proposed answer'}</label><textarea id="answer-${h(f.id)}" name="${h(f.id)}" aria-labelledby="label-${h(f.id)}" rows="6" maxlength="24000">${h(value)}</textarea></details></article>`;
  };
  const documents = current.documents.filter(document => document.exists);
  const unsaved = model.wizard.pages.filter(page => page.id !== 'review' && hasUnsavedChanges(model, page));
  const cannotFinish = current.id === 'review' && (!model.wizard.baselineCurrent || model.wizard.pages.some(page => page.id !== 'review' && !page.complete) || unsaved.length > 0);
  const previous = STEPS[Math.max(0, STEPS.findIndex(([id]) => id === current.id) - 1)];
  const next = STEPS[STEPS.findIndex(([id]) => id === current.id) + 1];
  const completed = model.wizard.pages.filter(page => page.complete && !hasUnsavedChanges(model, page)).length;
  const primary = current.id === 'review' ? (model.wizard.reviewed ? 'Update definition review' : 'Record definition review') : 'Save and continue';
  return `<section class="card readiness"><span class="eyebrow">Step ${STEPS.findIndex(([id]) => id === current.id) + 1} of ${STEPS.length} · ${h(current.status)}</span><h2>${h(current.title)}</h2>
    <div class="feature-review-progress"><span>${completed} of ${STEPS.length} steps reviewed</span><span>·</span><span>${questions.length ? `${questions.length} ${structured ? 'review questions' : 'business decisions'}` : 'Narrative review'}</span></div>
    ${current.id === 'business' && !questions.length ? '<p>No open business decisions were found in the imported BRD. Review the business narrative, then save and continue to technical direction.</p>' : ''}
    ${structured ? '<p>Review the suggested text for each topic, edit where needed, then save and continue. Suggestions are recorded only when you save.</p>' : ''}
    ${current.attention.length ? `<h3>What needs attention</h3><ul>${current.attention.map(item => `<li>${h(item)}</li>`).join('')}</ul>` : '<p>This page is reviewed against the current product baseline. Continue, or revise the saved answers.</p>'}</section>
    <details class="card"><summary>BRD and product reference documents</summary><div class="actions"><button type="button" class="link" data-wizard-action="open-source">Current feature BRD ↗</button><button type="button" class="link" data-wizard-action="open-request">Feature request and saved review ↗</button>${documents.map(document => `<button type="button" class="link" data-wizard-action="open-document" data-value="${h(document.path)}">${h(document.title)} ↗</button>`).join('')}</div></details>
    ${questions.length ? `<section class="card"><h3>Questions in this step</h3><div class="feature-question-index">${questions.map((question, index) => `<button type="button" class="link" data-question-target="${h(question.id)}">${index + 1}. ${h(question.label)}</button>`).join('')}</div></section>` : ''}
    ${current.id === 'review' ? `<section class="card"><h3>Feature review summary</h3><div class="review-summary">${model.wizard.pages.filter(page => page.id !== 'review').map(page => `<button type="button" class="secondary review-page" data-wizard-action="navigate" data-value="${page.id}"><strong>${h(page.title)}</strong><span>${h(page.status)}</span></button>`).join('')}</div><p>Recording this review concludes the proposed feature definition. The high-level backlog and feature specification retain their own approval gates.</p></section>` : ''}
    ${current.id === 'review' && unsaved.length ? `<p class="notice warning">Save or discard the unsaved changes in ${h(unsaved.map(page => page.title).join(', '))} before recording the review.</p>` : ''}
    ${current.id === 'architecture' ? architectureGallery(model.featureArchitecture, { busy: model.busy, dirty: hasUnsavedChanges(model, current) }) : ''}
    ${current.id === 'experience' ? gallery(model.featureScreens, { busy: model.busy, dirty: hasUnsavedChanges(model, current), drafts: model.screenDrafts }) : ''}
    <form id="feature-review-form" class="source-review feature-review-form"><fieldset ${model.busy ? 'disabled' : ''}><legend>${current.id === 'review' ? 'Review conclusion' : 'Proposed feature direction'}</legend><div class="wizard-questions feature-questions">
      ${stories.length ? `<section class="feature-delivery-stories"><h2>Required user stories</h2><p>Foundation is required regardless of release scope. MVP completes the first release. Post-MVP captures later delivery. Review the suggested categories and acceptance outlines; future candidates remain uncommitted until you decide to include them.</p><p class="muted">The BRD contains the full requirements behind each outline. These lists describe feature scope; delivery and backlog approvals remain separate.</p>${stories.map(story => field(story, 0)).join('')}</section>` : ''}
      ${!structured && summary ? field(summary, 0) : ''}${questions.map((question, index) => field(question, index + 1)).join('')}
      ${structured && summary ? `<details><summary>${h(summary.label)}</summary>${field(summary, 0)}</details>` : ''}</div></fieldset></form>
    ${current.id === 'delivery' ? renderRepositoryWork(model) : ''}
    ${current.id === 'review' || current.id === 'delivery' ? `<section class="card"><h3>Next delivery actions</h3><p>Reconcile this feature's proposed changes into the product baseline and approve its backlog outcome. Then continue through the governed feature specification.</p><div class="actions"><button type="button" class="secondary" data-wizard-action="product-wizard">Open product wizard</button><button type="button" class="secondary" data-wizard-action="start-approved-feature" ${model.wizard.reviewed ? '' : 'disabled'}>Continue with an approved backlog outcome</button></div></section>` : ''}
    <footer class="feature-review-actions"><span class="muted">${next ? `Next: ${h(next[1])}. Incomplete answers are saved and remain on this step for review.` : 'Record the final review when all preceding steps are complete.'}</span><button type="submit" form="feature-review-form" ${model.busy || cannotFinish ? 'disabled' : ''}>${primary}</button>${next ? `<button type="button" class="secondary" data-wizard-action="save-page" ${model.busy ? 'disabled' : ''}>Save without leaving</button>` : ''}<button type="button" class="secondary" data-wizard-action="navigate" data-value="${previous[0]}" ${model.busy ? 'disabled' : ''}>Back</button>${next ? `<button type="button" class="secondary" data-wizard-action="navigate" data-value="${next[0]}" ${model.busy ? 'disabled' : ''}>View next step →</button>` : ''}<button type="button" class="secondary" data-wizard-action="refresh" ${model.busy ? 'disabled' : ''}>Refresh status</button>${hasUnsavedChanges(model, current) ? '<button type="button" class="secondary" data-wizard-action="discard-edits">Discard unsaved edits</button>' : ''}</footer>`;
}

function wizardScript(page) {
  return `
    const reviewForm = document.getElementById('feature-review-form');
    function reviewFields() { return reviewForm ? Object.fromEntries(new FormData(reviewForm).entries()) : undefined; }
    function repositoryWork() { return document.getElementById('repository-work') ? [...document.querySelectorAll('[data-work-id]')].map(row => {
      const value = field => row.querySelector('[data-work-field="' + field + '"]').value;
      const selected = field => [...row.querySelector('[data-work-field="' + field + '"]').selectedOptions].map(option => option.value);
      return { id: row.dataset.workId, repositoryId: value('repositoryId'), title: value('title'), scope: value('scope'), dependsOn: selected('dependsOn'), changeIds: selected('changeIds') };
    }) : undefined; }
    function postWizard(command, value) { clearTimeout(draftTimer); vscode.postMessage({ command, value: JSON.stringify({ page: ${JSON.stringify(page)}, target: value, answers: reviewFields(), screenDrafts: Object.fromEntries([...document.querySelectorAll('[data-screen-feedback]')].map(input => [input.dataset.screenFeedback, input.value])), repositoryWork: repositoryWork(), draft: typeof fields === 'function' && fields() ? JSON.parse(fields()) : undefined }) }); }
    document.querySelectorAll('[data-wizard-action]').forEach(button => button.addEventListener('click', () => {
      if (button.dataset.wizardAction === 'move-story') button.disabled = true;
      postWizard(button.dataset.wizardAction, button.dataset.value);
    }));
    const movedStory = document.querySelector('[data-story-focus]');
    if (movedStory) requestAnimationFrame(() => { movedStory.scrollIntoView({ block: 'center' }); movedStory.querySelector('button')?.focus({ preventScroll: true }); });
    reviewForm?.addEventListener('submit', event => { event.preventDefault(); postWizard('save-continue'); });
    document.querySelectorAll('[data-question-target]').forEach(button => button.addEventListener('click', () => {
      const card = document.getElementById('question-' + button.dataset.questionTarget);
      card?.scrollIntoView({ block: 'start' });
      card?.querySelector('summary')?.focus({ preventScroll: true });
    }));
    let draftTimer;
    window.addEventListener('message', event => { if (event.data?.type === 'feature-navigate') postWizard('navigate', event.data.page); });
    document.querySelectorAll('#feature-form, #feature-review-form, #repository-work, .feature-screens').forEach(form => form.addEventListener('input', () => {
      clearTimeout(draftTimer); draftTimer = setTimeout(() => postWizard('remember'), 250);
    }));`;
}

module.exports = { STEPS, navigation, renderReviewPage, wizardScript, hasUnsavedChanges };
