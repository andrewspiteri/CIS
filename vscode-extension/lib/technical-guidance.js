'use strict';

const { pathToFileURL } = require('node:url');
const { escapeHtml: escape, resolveWithin } = require('./security');
const { renderTechnicalDecisionForm } = require('./technical-decision-form');

function renderTechnicalPage(root, page, model, action, renderQuestions) {
  const questionnaire = model.technicalQuestions;
  const questions = questionnaire?.questions || [];
  const resolved = questions.filter(question => ['answered', 'derived'].includes(String(question.status).toLowerCase())).length;
  const answersReady = questionnaire?.complete && questionnaire?.current;
  const decisions = [...(page.guidance?.technicalDecisions || [])].sort((a, b) => Number(b.needsReview) - Number(a.needsReview) || a.id.localeCompare(b.id));
  const pending = decisions.filter(decision => decision.needsReview).length;
  const saved = model.technicalDecisionSave;
  const savedDecision = saved && decisions.find(decision => decision.id === saved.id);
  const saveNotice = !saved ? '' : !saved.refreshed
    ? `<p class="notice warning" role="status">${escape(saved.id)} was saved, but readiness could not refresh. Use Refresh to check the current state. ${escape(saved.error || '')}</p>`
    : savedDecision && !savedDecision.needsReview
      ? `<p class="notice" role="status">${escape(saved.id)} saved and resolved. ${pending ? `${pending} other decision${pending === 1 ? ' still needs' : 's still need'} review.` : 'No document decisions need review.'}</p>`
      : `<p class="notice warning" role="status">${escape(saved.id)} was saved. ${savedDecision ? 'Review the remaining findings shown for this decision.' : 'The decision is no longer in the current document; review the latest technical intent.'}</p>`;
  const file = page.primaryPath && resolveWithin(root, page.primaryPath);
  const progress = `<section class="card"><span class="eyebrow">Questionnaire answers</span><h3>${resolved} of ${questions.length} answers resolved <span class="badge ${answersReady ? 'good' : 'warn'}">${answersReady ? 'Complete' : questionnaire?.complete ? 'Needs refresh' : 'Incomplete'}</span></h3><p>The questionnaire records high-level choices. The technical-intent document has its own decisions and validation checks.</p>${action('questions', 'Review questionnaire answers')}</section>`;
  const renderDecision = decision => {
    const label = `${decision.id} — ${decision.decision}`;
    const title = file ? `<a class="source-document-link" href="${escape(pathToFileURL(file).href)}${Number.isSafeInteger(decision.documentLine) ? '#L' + decision.documentLine : ''}" target="_blank" rel="noopener" data-command="open-technical-decision" data-value="${escape(decision.id)}">${escape(label)} ↗</a>` : escape(label);
    return `<article class="card"><div class="section-heading"><h4>${title}</h4><span class="badge ${decision.needsReview ? 'warn' : 'good'}">${escape(decision.needsReview ? 'Needs review' : decision.status || 'Recorded')}</span></div><p><strong>Required before:</strong> ${escape(decision.requiredBefore)} · <strong>Status:</strong> ${escape(decision.status)}</p><p>${escape(decision.rationale)}</p>${renderTechnicalDecisionForm(decision, escape)}${decision.issues?.length ? `<details><summary>Review details</summary><ul>${decision.issues.map(issue => `<li>${escape(issue)}</li>`).join('')}</ul></details>` : ''}</article>`;
  };
  const related = decisions.filter(decision => decision.needsReview && decision.questionnaireOverlap);
  const additional = decisions.filter(decision => decision.needsReview && !decision.questionnaireOverlap);
  const recorded = decisions.filter(decision => !decision.needsReview);
  const group = (title, items) => items.length ? `<section><h4>${title} (${items.length})</h4><div class="cards">${items.map(renderDecision).join('')}</div></section>` : '';
  const review = decisions.length ? `<section id="technical-decisions" tabindex="-1" aria-labelledby="technical-decisions-title"><h3 id="technical-decisions-title">Document decisions — ${pending} need attention out of ${decisions.length}</h3><p>Review the prefilled suggestions, edit any remaining choice, and save each answer here. Readiness refreshes after each save.</p>${recorded.length ? `<details ${saved?.refreshed && savedDecision && !savedDecision.needsReview ? 'open' : ''}><summary>Recorded decisions (${recorded.length})</summary><div class="cards">${recorded.map(renderDecision).join('')}</div></details>` : ''}${group('Confirm recorded directions', related)}${group('Additional product decisions', additional)}</section>` : '';
  const tools = `<section class="card"><h3>Technical-intent document</h3><div class="actions">${action('open-intent', 'Read or edit technical intent', Boolean(file))}${action('prepare-technical', 'Prepare or refresh technical evidence')}${action('infer-technical', 'Infer from existing repositories', (model.businessInference?.repositories || []).length > 0)}</div><p>Inference drafts a narrative from the owned implementation. Repeating it does not record human resolutions for the document's open decisions.</p></section>`;
  return `${saveNotice}${progress}${review}${tools}<details id="technical-questionnaire" ${answersReady ? '' : 'open'}><summary tabindex="-1">Questionnaire answers — ${resolved} of ${questions.length} resolved</summary>${renderQuestions(questions, 'technical')}</details>`;
}

module.exports = { renderTechnicalPage };
