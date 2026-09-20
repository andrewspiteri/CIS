'use strict';
const { pathToFileURL } = require('node:url');
const { escapeHtml, resolveWithin } = require('./security');

function sources(repository) {
  const values = [...(repository.facts || []).flatMap(fact => fact.evidence || []), ...(repository.tokens || []).map(token => token.evidence), ...(repository.controls || []).flatMap(control => control.evidence || [])].filter(Boolean);
  return values.filter((item, index) => values.findIndex(other => other.relativePath === item.relativePath && other.line === item.line) === index);
}

function uiBaselineSource(baseline, value) {
  if (!/^\d+:\d+$/u.test(String(value))) return undefined;
  const [repositoryIndex, sourceIndex] = value.split(':').map(Number);
  const repository = baseline?.repositories?.[repositoryIndex];
  const evidence = repository && sources(repository)[sourceIndex];
  return evidence && Number.isSafeInteger(evidence.line) && evidence.line > 0 ? { repository, evidence } : undefined;
}

function renderUiBaseline(baseline, model) {
  const embedded = model.experienceGuidance === true;
  const prerequisites = (model.pages || []).filter(page => ['technical', 'architecture'].includes(page.id) && (!page.complete || !page.current));
  const next = !embedded && prerequisites.length ? `<section class="notice warning"><strong>UI baseline review is available now</strong><p>Before recording and generating UI direction, finish the upstream decisions:</p><div class="actions">${prerequisites.map(page => `<button type="button" class="secondary" data-command="navigate" data-value="${escapeHtml(page.id)}">Review ${escapeHtml(page.id === 'technical' ? 'technical decisions' : 'solution architecture')}</button>`).join('')}</div></section>` : '';
  if (!baseline) return `<section class="notice"><strong>Discovering the current interface baseline</strong><span>CIS reads the existing interfaces when this page opens.</span></section>${next}`;
  if (baseline.errors?.length) return `<section class="notice warning"><strong>UI discovery needs attention</strong><p>${escapeHtml(baseline.errors.join(' '))}</p></section>${next}`;
  const repositories = baseline.repositories || [];
  const cards = repositories.map((repository, repositoryIndex) => {
    const sheet = repository.preview;
    const preview = hasUiBaselinePreview(repository)
      ? `<figure class="ui-preview" data-control-sheet="${repositoryIndex}" data-source-hash="${escapeHtml(baseline.sourceHash)}"><img src="data:image/svg+xml;base64,${Buffer.from(sheet.svg).toString('base64')}" alt="${escapeHtml(repository.id)}: rendered reference controls"><figcaption><strong data-render-status>Rendering JPG control sheet…</strong><p>${escapeHtml(sheet.description)}</p><div class="actions"><button type="button" class="secondary" data-save-control-sheet="${repositoryIndex}">Save JPG control sheet</button></div></figcaption></figure>`
      : '<p class="muted">No supported control families were found in the inspected templates. Review the source evidence or refresh discovery.</p>';
    const evidence = sources(repository).map((item, sourceIndex) => {
      const file = resolveWithin(repository.repositoryPath, item.relativePath);
      return file && Number.isSafeInteger(item.line) && item.line > 0 ? `<li><a class="source-document-link" href="${escapeHtml(pathToFileURL(file).href)}#L${item.line}" target="_blank" data-command="open-ui-baseline-source" data-value="${repositoryIndex}:${sourceIndex}">${escapeHtml(item.relativePath)}:${item.line}</a></li>` : '';
    }).join('');
    const tokens = (repository.tokens || []).filter(token => /^#(?:[a-f\d]{3}|[a-f\d]{4}|[a-f\d]{6}|[a-f\d]{8})$/iu.test(token.value)).map(token =>
      `<li><svg width="24" height="24" viewBox="0 0 24 24" aria-hidden="true"><rect x="1" y="1" width="22" height="22" rx="4" fill="${token.value}" stroke="currentColor"/></svg> <strong>${escapeHtml(token.name)}</strong> <code>${escapeHtml(token.value)}</code></li>`).join('');
    const facts = repository.facts || [];
    const overview = facts.filter((fact, index) => ['Frameworks', 'Typography', 'Theme configuration'].includes(fact.area)
      || fact.area === 'Shell and navigation' && facts.findIndex(item => item.area === fact.area) === index);
    const detail = facts.filter(fact => !overview.includes(fact));
    const renderFact = fact => `<section><h4>${escapeHtml(fact.area)}</h4><p>${escapeHtml(fact.summary)}</p></section>`;
    const colors = (repository.tokens || []).filter(token => /^#(?:[a-f\d]{3}|[a-f\d]{4}|[a-f\d]{6}|[a-f\d]{8})$/iu.test(token.value)).slice(0, 8);
    const palette = colors.length ? `<svg width="${colors.length * 30}" height="28" role="img" aria-label="Observed color sample"><title>${escapeHtml(colors.map(token => `${token.name}: ${token.value}`).join(', '))}</title>${colors.map((token, index) => `<rect x="${index * 30 + 1}" y="1" width="24" height="24" rx="4" fill="${token.value}" stroke="currentColor"/>`).join('')}</svg>` : '';
    return `<article class="card ui-baseline-repository"><h3>${escapeHtml(repository.id)}</h3><p class="muted">${escapeHtml(repository.filesRead)} source files inspected${repository.limited ? ' · bounded sample' : ''}</p>${preview}<details><summary>Framework, theme and typography</summary>${palette}${overview.map(renderFact).join('')}</details>${detail.length ? `<details><summary>Layout, components and behavior</summary>${detail.map(renderFact).join('')}</details>` : ''}${tokens ? `<details><summary>Observed color declarations (${(repository.tokens || []).length})</summary><ul class="rows">${tokens}</ul></details>` : ''}<details><summary>Open implementation evidence</summary><ul class="criteria">${evidence}</ul></details></article>`;
  }).join('');
  return `${next}<section><${embedded ? 'h4' : 'h3'}>Current interface baseline</${embedded ? 'h4' : 'h3'}><p>${repositories.length ? 'Automatically discovered from owned implementation repositories. Review what exists before confirming the high-level direction.' : 'No supported web interface was discovered in the owned repositories. Use the questionnaire to define a new direction or review the discovery limits.'}</p>${baseline.warnings?.length ? `<details><summary>Scope and limitations</summary><ul>${baseline.warnings.map(warning => `<li>${escapeHtml(warning)}</li>`).join('')}</ul></details>` : ''}<div class="cards">${cards}</div></section>`;
}

function uiBaselineQuestions(model) {
  return (model.uiQuestions?.questions || []).map(question => {
    const suggestion = model.uiBaseline?.suggestions?.[question.id];
    return !question.answer && suggestion ? { ...question, suggestedAnswer: suggestion } : question;
  });
}
function hasUiBaselinePreview(repository) {
  const sheet = repository?.preview;
  return typeof sheet?.svg === 'string' && sheet.svg.length > 0 && sheet.svg.length < 512 * 1024
    && Number.isSafeInteger(sheet.width) && sheet.width > 0 && sheet.width <= 4096
    && Number.isSafeInteger(sheet.height) && sheet.height > 0 && sheet.height <= 4096;
}
module.exports = { renderUiBaseline, uiBaselineQuestions, uiBaselineSource, hasUiBaselinePreview };
