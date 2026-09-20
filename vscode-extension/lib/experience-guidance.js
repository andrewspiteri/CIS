'use strict';
const { escapeHtml: escape, resolveWithin } = require('./security');
const { hasUiBaselinePreview } = require('./ui-baseline');

// Presentation of checked CLI state; preview availability never satisfies approval gates.
function experienceState(model, root) {
  const page = model.pages?.find(item => item.id === 'experience') || {};
  const prerequisites = (model.pages || []).filter(item => ['technical', 'architecture'].includes(item.id) && (!item.complete || !item.current));
  const baseline = model.uiBaseline;
  const repositories = baseline?.repositories || [];
  const sheets = baseline?.errors?.length ? 0 : repositories.filter(hasUiBaselinePreview).length;
  const canonicalFile = model.preview?.svgRelativePath && resolveWithin(root, model.preview.svgRelativePath);
  const canonicalStale = Boolean(canonicalFile && model.preview.status === 'Stale');
  const available = sheets > 0 || Boolean(canonicalFile);
  const questions = model.uiQuestions?.questions || [];
  const pending = questions.filter(item => !['answered', 'derived'].includes(String(item.status).toLowerCase())).length;
  const ready = page.complete === true && page.current === true;
  const badge = ready ? page.status || 'Ready for Approval' : available ? 'Preview available · Direction pending' : page.status || 'Not started';
  const visual = sheets ? `Available — ${sheets} of ${repositories.length} interface control sheets`
    : canonicalFile ? canonicalStale ? 'Previous preview available — refresh required' : 'Available — recorded UI direction'
      : baseline?.errors?.length ? 'Discovery failed — refresh to retry'
        : !baseline ? 'Discovering the existing interfaces'
          : repositories.length ? 'Source evidence available — no control sheets found' : 'No existing interface preview found';
  let direction, next, action;
  if (ready) {
    direction = 'Complete and current'; next = 'Review the preview and recorded direction, then continue to the delivery map.';
    action = { command: 'navigate', value: 'delivery', label: 'Continue to delivery map' };
  } else if (prerequisites.length) {
    const technical = prerequisites.find(item => item.id === 'technical');
    const decisions = technical?.guidance?.technicalDecisions?.filter(item => item.needsReview).length
      || new Set((technical?.issues || []).flatMap(issue => String(issue).match(/TI-DEC-\d+/gu) || [])).size;
    direction = `Waiting for ${prerequisites.map(item => item.id === 'technical' ? 'technical direction' : 'solution architecture').join(' and ')}`;
    next = `${decisions ? `${decisions} technical document decisions still need review. ` : ''}Finish the linked upstream review before recording UI direction. The existing interface previews can be reviewed now.`;
    const first = prerequisites[0];
    action = { command: 'navigate', value: first.id, label: first.id === 'technical' ? 'Review technical decisions' : 'Review solution architecture' };
  } else if (!questions.length) {
    direction = 'UI questions have not been prepared'; next = 'Prepare the UI questions to review the discovered suggestions and record your direction.';
    action = { command: 'prepare', value: 'experience', label: 'Prepare UI questions' };
  } else if (pending) {
    direction = `${pending} UI questions need answers out of ${questions.length}`; next = 'Review the suggested directions below and save the remaining answers.';
    action = { command: 'focus-ui-questions', value: '', label: 'Review UI questions' };
  } else if (!model.uiQuestions?.current || !model.uiQuestions?.complete) {
    direction = 'Answers recorded — questionnaire needs refresh'; next = 'Refresh UI direction to reconcile the recorded answers with current evidence. Existing human answers are preserved.';
    action = { command: 'prepare', value: 'experience', label: 'Refresh UI direction' };
  } else {
    direction = 'Questionnaire complete — UI direction needs preparation'; next = 'Prepare UI direction from the recorded answers and review any remaining validation findings.';
    action = { command: 'prepare', value: 'experience', label: 'Prepare UI direction' };
  }
  return { badge, visual, direction, next, action, prerequisites, ready, sheets, canonicalFile, canonicalStale, questions };
}

function experienceAction(action) {
  return `<button type="button" data-command="${escape(action.command)}" data-value="${escape(action.value)}">${escape(action.label)}</button>`;
}

function renderExperienceGuidance(state) {
  return `<section class="card readiness"><span class="eyebrow">Experience readiness</span><h3>Visual preview and UI direction</h3><p><strong>Visual preview:</strong> ${escape(state.visual)}</p><p><strong>UI direction:</strong> ${escape(state.direction)}</p><h4>Next step</h4><p>${escape(state.next)}</p><div class="actions">${experienceAction(state.action)}${state.prerequisites.filter(item => item.id !== state.action.value).map(item => experienceAction({ command: 'navigate', value: item.id, label: item.id === 'technical' ? 'Review technical decisions' : 'Review solution architecture' })).join('')}</div></section>`;
}
module.exports = { experienceState, experienceAction, renderExperienceGuidance };
