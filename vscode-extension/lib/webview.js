'use strict';

const { escapeHtml, isValidWebviewMessage, nonce, resolveWithin } = require('./security');

function openEvidencePanel(vscode, title, model, actions = [], onAction = async () => {}, onPath = undefined) {
  const hasPaths = typeof onPath === 'function' && flatten(model).some(([, value]) => isOpenablePath(value));
  const panel = vscode.window.createWebviewPanel('cis.evidence', title, vscode.ViewColumn.Active, {
    enableScripts: actions.length > 0 || hasPaths,
    retainContextWhenHidden: false,
    localResourceRoots: [],
  });
  const scriptNonce = nonce();
  panel.webview.html = renderHtml(panel.webview, title, model, actions, scriptNonce, hasPaths);
  const allowed = new Set(actions.map(action => action.command));
  if (hasPaths) allowed.add('open-path');
  if (actions.length || hasPaths) panel.webview.onDidReceiveMessage(async message => {
    if (!isValidWebviewMessage(message, allowed)) return;
    if (message.command === 'open-path') await onPath(message.value);
    else await onAction(message.command, message.value);
  });
  return panel;
}

function openDesignPanel(vscode, root, changeId, design, resolveArtifact, onAction) {
  const artifacts = (design.artifacts || []).map(artifact => ({ artifact, file: resolveArtifact(artifact.path) })).filter(item => item.file);
  const canDecide = String(design.gateStatus).toLowerCase() === 'pausedforreview';
  const panel = vscode.window.createWebviewPanel('cis.designReview', `Design review ${changeId}`, vscode.ViewColumn.Active, {
    enableScripts: canDecide,
    retainContextWhenHidden: false,
    localResourceRoots: [vscode.Uri.file(root)],
  });
  const scriptNonce = nonce();
  const cards = artifacts.map(({ artifact, file }) => `<figure><img src="${escapeHtml(panel.webview.asWebviewUri(vscode.Uri.file(file)).toString())}" alt="${escapeHtml(`${artifact.screenId}, ${artifact.state}, ${artifact.viewport}`)}"><figcaption><strong>${escapeHtml(artifact.screenId)}</strong><br>${escapeHtml(`${artifact.state} · ${artifact.viewport} · ${artifact.width}×${artifact.height}`)}<br><code>${escapeHtml(artifact.sha256)}</code></figcaption></figure>`).join('');
  const decisionActions = canDecide ? '<button data-command="approve">Approve exact pack</button><button data-command="reject">Reject exact pack</button>' : '<span>No design decision is currently required.</span>';
  panel.webview.html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${panel.webview.cspSource}; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';">
    <title>${escapeHtml(`Design review ${changeId}`)}</title><style nonce="${scriptNonce}">
    body{color:var(--vscode-foreground);background:var(--vscode-editor-background);font:var(--vscode-font-weight) var(--vscode-font-size)/1.5 var(--vscode-font-family);padding:1rem}header{position:sticky;top:0;background:var(--vscode-editor-background);padding:.5rem 0;border-bottom:1px solid var(--vscode-panel-border);z-index:1}.meta,.actions{display:flex;gap:.7rem;flex-wrap:wrap}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(32rem,100%),1fr));gap:1rem;margin-top:1rem}figure{margin:0;border:1px solid var(--vscode-panel-border);padding:.6rem}img{display:block;width:100%;height:auto}figcaption{overflow-wrap:anywhere;padding-top:.5rem}button{color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:0;padding:.45rem .8rem}button:focus-visible{outline:2px solid var(--vscode-focusBorder);outline-offset:2px}@media(prefers-reduced-motion:reduce){*{transition:none!important}}
    </style></head><body><main><header><h1>${escapeHtml(`Design review ${changeId}`)}</h1><div class="meta"><span>Gate: <strong>${escapeHtml(design.gateStatus)}</strong></span><span>Approval: <strong>${escapeHtml(design.approvalStatus)}</strong></span><span>${artifacts.length} validated images</span></div><div class="actions">${decisionActions}</div></header><section class="grid" aria-label="Rendered screen states">${cards}</section></main>
    ${canDecide ? `<script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();document.querySelectorAll('button[data-command]').forEach(button=>button.addEventListener('click',()=>vscode.postMessage({command:button.dataset.command,value:'${escapeHtml(changeId)}'})));</script>` : ''}</body></html>`;
  const allowed = new Set(['approve', 'reject']);
  if (canDecide) panel.webview.onDidReceiveMessage(async message => { if (isValidWebviewMessage(message, allowed)) await onAction(message.command, message.value); });
  return panel;
}

function openRecommendationReviewPanel(vscode, runId, status, onAction) {
  const panel = vscode.window.createWebviewPanel('cis.recommendationReview', `BRD review ${runId}`, vscode.ViewColumn.Active, {
    enableScripts: true,
    retainContextWhenHidden: false,
    localResourceRoots: [],
  });
  const scriptNonce = nonce();
  const allowed = new Set(['accept', 'accept-all', 'modify', 'open-review']);
  const controller = {
    panel,
    update(nextStatus) {
      panel.webview.html = renderRecommendationReviewHtml(panel.webview, runId, nextStatus, scriptNonce);
    },
  };
  controller.update(status);
  panel.webview.onDidReceiveMessage(async message => {
    if (isValidWebviewMessage(message, allowed)) await onAction(message.command, message.value);
  });
  return controller;
}

function openBrdQuestionsPanel(vscode, status, onAction) {
  const panel = vscode.window.createWebviewPanel('cis.brdQuestions', 'BRD open questions', vscode.ViewColumn.Active, {
    enableScripts: true,
    retainContextWhenHidden: false,
    localResourceRoots: [],
  });
  const scriptNonce = nonce();
  const allowed = new Set(['accept-suggestion', 'generate-suggestions', 'open-brd', 'save-answer']);
  const controller = {
    panel,
    update(nextStatus) {
      panel.webview.html = renderBrdQuestionsHtml(panel.webview, nextStatus, scriptNonce);
    },
  };
  controller.update(status);
  panel.webview.onDidReceiveMessage(async message => {
    if (isValidWebviewMessage(message, allowed)) await onAction(message.command, message.value);
  });
  return controller;
}

function openTechnicalIntentQuestionsPanel(vscode, status, onAction) {
  const panel = vscode.window.createWebviewPanel('cis.technicalIntentQuestions', 'High-level technical direction', vscode.ViewColumn.Active, {
    enableScripts: true,
    retainContextWhenHidden: false,
    localResourceRoots: [],
  });
  const scriptNonce = nonce();
  const allowed = new Set(['open-questionnaire', 'save-answer']);
  const controller = {
    panel,
    update(nextStatus) { panel.webview.html = renderTechnicalIntentQuestionsHtml(panel.webview, nextStatus, scriptNonce); },
  };
  controller.update(status);
  panel.webview.onDidReceiveMessage(async message => {
    if (isValidWebviewMessage(message, allowed)) await onAction(message.command, message.value);
  });
  return controller;
}

function openUiDirectionQuestionsPanel(vscode, status, onAction) {
  const panel = vscode.window.createWebviewPanel('cis.uiDirectionQuestions', 'High-level UI direction', vscode.ViewColumn.Active, {
    enableScripts: true,
    retainContextWhenHidden: false,
    localResourceRoots: [],
  });
  const scriptNonce = nonce();
  const allowed = new Set(['open-questionnaire', 'save-answer']);
  const controller = {
    panel,
    update(nextStatus) { panel.webview.html = renderUiDirectionQuestionsHtml(panel.webview, nextStatus, scriptNonce); },
  };
  controller.update(status);
  panel.webview.onDidReceiveMessage(async message => {
    if (isValidWebviewMessage(message, allowed)) await onAction(message.command, message.value);
  });
  return controller;
}

function openDefinitionWizardPanel(vscode, root, model, onAction, onPath, initialPage) {
  const panel = vscode.window.createWebviewPanel('cis.definitionWizard', 'High-level product definition', vscode.ViewColumn.Active, {
    enableScripts: true,
    retainContextWhenHidden: true,
    localResourceRoots: [vscode.Uri.file(root)],
  });
  const scriptNonce = nonce();
  const allowed = new Set(['activate', 'business-action', 'navigate', 'open-path', 'prepare', 'refresh', 'save-answer']);
  let currentModel = model;
  let currentPage = initialPage || model?.currentPage || 'foundation';
  const controller = {
    panel,
    update(nextModel, requestedPage) {
      currentModel = nextModel || currentModel;
      currentPage = requestedPage || currentPage || nextModel?.currentPage || 'foundation';
      panel.webview.html = renderDefinitionWizardHtml(panel.webview, root, currentModel, currentPage, scriptNonce, vscode);
    },
    page() { return currentPage; },
  };
  controller.update(model, currentPage);
  panel.webview.onDidReceiveMessage(async message => {
    if (!isValidWebviewMessage(message, allowed)) return;
    if (message.command === 'navigate') {
      currentPage = String(message.value || 'foundation');
      controller.update(currentModel, currentPage);
      return;
    }
    if (message.command === 'open-path') await onPath(message.value);
    else await onAction(message.command, message.value, controller);
  });
  return controller;
}

function renderDefinitionWizardHtml(webview, root, model, currentPage, scriptNonce, vscode) {
  const pages = Array.isArray(model?.pages) ? [...model.pages].sort((a, b) => Number(a.ordinal) - Number(b.ordinal)) : [];
  const selected = pages.find(page => page.id === currentPage) || pages[0] || { id: 'foundation', ordinal: 1, title: 'Project foundation', status: 'Not started', artifactPaths: [], issues: [] };
  const statusClass = selected.complete && selected.current ? 'good' : selected.issues?.length ? 'bad' : 'warn';
  const nav = pages.map(page => `<button type="button" class="step ${page.id === selected.id ? 'current' : ''}" data-command="navigate" data-value="${escapeHtml(page.id)}" aria-current="${page.id === selected.id ? 'step' : 'false'}"><span>${escapeHtml(String(page.ordinal))}</span><span><strong>${escapeHtml(page.title)}</strong><small>${escapeHtml(page.status || 'Not started')}</small></span><i class="${page.complete && page.current ? 'complete' : ''}" aria-hidden="true"></i></button>`).join('');
  const artifacts = (selected.artifactPaths || []).map(item => `<li><button class="link" type="button" data-command="open-path" data-value="${escapeHtml(item)}">${escapeHtml(item)}</button></li>`).join('');
  const issues = (selected.issues || []).map(item => `<li>${escapeHtml(item)}</li>`).join('');
  const pageBody = renderDefinitionPageBody(webview, root, model, selected, vscode);
  const previous = pages.find(page => Number(page.ordinal) === Number(selected.ordinal) - 1);
  const next = pages.find(page => Number(page.ordinal) === Number(selected.ordinal) + 1);
  const footer = `<footer class="wizard-footer">
    <button type="button" class="secondary" data-command="navigate" data-value="${escapeHtml(previous?.id || selected.id)}" ${previous ? '' : 'disabled'}>Back</button>
    <div class="footer-primary">
      <button type="button" class="secondary" data-command="refresh">Refresh</button>
      ${selected.id !== 'review' ? `<button type="button" data-command="prepare" data-value="${escapeHtml(selected.id)}">${selected.complete && selected.current ? 'Save and continue' : 'Prepare or refresh page'}</button>` : model?.readyToActivate && model?.active !== false ? '<button type="button" data-command="activate">Approve and activate</button>' : model?.readyToActivate ? '<span class="badge good">Baseline active</span>' : '<button type="button" class="secondary" data-command="refresh">Recheck readiness</button>'}
      ${next && selected.complete && selected.current ? `<button type="button" data-command="navigate" data-value="${escapeHtml(next.id)}">Continue</button>` : ''}
    </div></footer>`;
  const body = `<header class="hero"><div><span class="eyebrow">High-level product definition · ${escapeHtml(model?.sessionId || 'new draft')}</span><h1>${escapeHtml(selected.title)}</h1><p>Build one coherent business, technical, architecture, contract, experience and delivery baseline before the feature loop.</p></div><span class="badge ${statusClass}">${escapeHtml(selected.status || 'Not started')}</span></header>
    <div class="wizard-layout"><nav class="wizard-steps" aria-label="Definition wizard pages">${nav}</nav><section class="wizard-page" aria-labelledby="page-title"><div class="section-heading"><div><span class="eyebrow">Page ${escapeHtml(String(selected.ordinal))} of ${escapeHtml(String(pages.length || 8))}</span><h2 id="page-title">${escapeHtml(selected.title)}</h2></div><span class="badge ${statusClass}">${selected.complete && selected.current ? 'Complete and current' : 'Needs attention'}</span></div>
    ${pageBody}${artifacts ? `<details><summary>Canonical artifacts (${(selected.artifactPaths || []).length})</summary><ul class="links">${artifacts}</ul></details>` : ''}${issues ? `<section class="notice warning"><strong>What needs attention</strong><ul>${issues}</ul></section>` : ''}</section></div>${footer}`;
  return studioDocument(webview, 'High-level product definition', body, scriptNonce, definitionWizardScript(scriptNonce));
}

function renderDefinitionPageBody(webview, root, model, page, vscode) {
  if (page.id === 'foundation') {
    const dictionaries = (model.dictionaries || []).filter(item => item.applicable);
    return `<section class="cards two"><article class="card"><span class="eyebrow">Workspace authority</span><h3>${escapeHtml(model.authorityRepositoryId || 'Not configured')}</h3><p>${escapeHtml(model.workspacePath || root)}</p></article><article class="card"><span class="eyebrow">Classification-selected references</span><h3>${dictionaries.length} dictionaries initialized</h3><p>Existing repositories contribute discovered facts; greenfield repositories retain governed skeletons.</p></article></section><div class="actions"><button type="button" data-command="business-action" data-value="doctor">Open Repository Doctor</button><button type="button" class="secondary" data-command="business-action" data-value="evidence">Browse evidence</button></div>`;
  }
  if (page.id === 'business') {
    const questions = model.brdQuestions?.questions || [];
    return `<section class="card"><h3>Business authority</h3><p>Define the problem, actors, outcomes, rules, constraints, exclusions and success measures. Agent drafting and independent review remain available without leaving this journey.</p><div class="actions"><button type="button" data-command="business-action" data-value="open-brd">Open BRD</button><button type="button" data-command="business-action" data-value="draft-brd">Draft from references</button><button type="button" class="secondary" data-command="business-action" data-value="questions">Answer ${questions.filter(item => String(item.status).toLowerCase() === 'unanswered').length} open questions</button><button type="button" class="secondary" data-command="business-action" data-value="review-brd">Independent review</button></div></section>`;
  }
  if (page.id === 'technical') return renderWizardQuestions(model.technicalQuestions?.questions, 'technical');
  if (page.id === 'architecture') {
    const diagrams = model.diagrams || [];
    return `<section class="cards two"><article class="card"><h3>Overall solution design</h3><p>Review module ownership, data boundaries, integrations, trust boundaries, deployment and recovery direction.</p></article><article class="card"><h3>Component sheet</h3><p>Every capability, record and cross-boundary interaction has one explicit owner.</p></article></section><section><h3>Generated diagrams</h3><div class="cards two">${diagrams.map(item => `<article class="card"><span class="eyebrow">${escapeHtml(item.sourceFormat)}</span><h4>${escapeHtml(item.title)}</h4><button type="button" class="link" data-command="open-path" data-value="${escapeHtml(item.relativePath)}">Open canonical diagram source</button></article>`).join('') || '<article class="card empty">Prepare this page after technical direction is complete.</article>'}</div></section>`;
  }
  if (page.id === 'contracts') {
    return `<p>These shared references start before feature delivery and are extended by each applicable feature.</p><div class="dictionary-grid">${(model.dictionaries || []).map(item => `<article class="card"><div class="section-heading"><h3>${escapeHtml(item.title)}</h3><span class="badge ${item.applicable ? 'good' : 'warn'}">${item.applicable ? `${item.entryCount} entries` : 'Not applicable'}</span></div>${item.applicable ? `<button type="button" class="link" data-command="open-path" data-value="${escapeHtml(item.relativePath)}">${escapeHtml(item.relativePath)}</button>` : '<span class="muted">Not selected by repository classification</span>'}</article>`).join('')}</div>`;
  }
  if (page.id === 'experience') {
    const preview = model.preview;
    let image = '<article class="card empty">Complete the UI-direction questionnaire and prepare this page to generate the one-page preview.</article>';
    if (preview?.svgRelativePath) {
      const file = resolveWithin(root, preview.svgRelativePath);
      if (file) image = `<figure class="ui-preview"><img src="${escapeHtml(webview.asWebviewUri(vscode.Uri.file(file)).toString())}" alt="One-page high-level product UI preview"><figcaption>${escapeHtml(preview.fontFamily)} · ${escapeHtml(preview.density)} · ${escapeHtml(preview.radius)} radius</figcaption></figure>`;
    }
    return `${renderWizardQuestions(model.uiQuestions?.questions, 'experience')}<section><h3>One-page visual system preview</h3>${image}</section>`;
  }
  if (page.id === 'delivery') return `<section class="card"><h3>High-level delivery map</h3><p>Review the outcome backlog, repository routing, frontend classifications, dependencies, MVP boundary and shared testing, security, infrastructure and operational obligations.</p>${page.primaryPath ? `<button type="button" class="link" data-command="open-path" data-value="${escapeHtml(page.primaryPath)}">Open high-level backlog</button>` : ''}</section>`;
  if (page.id === 'review') {
    const complete = (model.pages || []).filter(item => item.id !== 'review' && item.complete && item.current).length;
    const activationMessage = model.readyToActivate && model.active === false
      ? 'This exact baseline is active. Revisit any page to begin a new governed revision.'
      : model.readyToActivate
        ? 'The exact current revision can be activated with one human decision.'
        : 'Choose any page in the journey map to resolve its remaining issues.';
    return `<section class="review-summary"><article class="card"><span class="eyebrow">Baseline readiness</span><h3>${complete}/7 pages complete</h3><p>${activationMessage}</p></article><article class="card"><span class="eyebrow">Activation behavior</span><h3>One bounded approval</h3><p>Approval records one actor, timestamp and content baseline across the BRD, technical intent, solution design, component sheet, UI direction, diagrams, dictionary index, UI preview and backlog. A failure restores the pre-activation files.</p></article></section><section class="cards two">${(model.pages || []).filter(item => item.id !== 'review').map(item => `<button type="button" class="card review-page" data-command="navigate" data-value="${escapeHtml(item.id)}"><span>${escapeHtml(String(item.ordinal))}. ${escapeHtml(item.title)}</span><strong>${escapeHtml(item.complete && item.current ? 'Ready' : 'Revise')}</strong></button>`).join('')}</section>`;
  }
  return '';
}

function renderWizardQuestions(questions, page) {
  const items = Array.isArray(questions) ? questions : [];
  if (!items.length) return `<article class="card empty">Prepare this page to initialize its guided decisions.</article>`;
  return `<section class="wizard-questions">${items.map(question => {
    const value = question.answer || question.suggestedAnswer || '';
    const resolved = ['answered', 'derived'].includes(String(question.status || '').toLowerCase());
    return `<article class="question-card"><div class="question-heading"><div><span class="eyebrow">${escapeHtml(question.id || '')} · ${escapeHtml(question.area || '')}</span><h3>${escapeHtml(question.question || '')}</h3></div><span class="badge ${resolved ? 'good' : 'warn'}">${resolved ? 'Resolved' : 'Decision required'}</span></div><p>${escapeHtml(question.why || '')}</p><label for="wizard-${escapeHtml(question.id || '')}">Direction</label><textarea id="wizard-${escapeHtml(question.id || '')}" data-page="${escapeHtml(page)}" data-question="${escapeHtml(question.id || '')}" rows="4" maxlength="16384">${escapeHtml(value)}</textarea><div class="actions"><button type="button" data-save-question="${escapeHtml(question.id || '')}" data-page="${escapeHtml(page)}">Save direction</button></div></article>`;
  }).join('')}</section>`;
}

function definitionWizardScript(scriptNonce) {
  return `<script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();document.addEventListener('click',event=>{const button=event.target.closest('button');if(!button||button.disabled)return;if(button.dataset.saveQuestion){const area=document.getElementById('wizard-'+button.dataset.saveQuestion);vscode.postMessage({command:'save-answer',value:JSON.stringify({page:button.dataset.page,id:button.dataset.saveQuestion,answer:area?.value||''})});return;}if(button.dataset.command)vscode.postMessage({command:button.dataset.command,value:button.dataset.value||''});});</script>`;
}

function openDoctorPanel(vscode, status, onAction, onPath) {
  const allowed = new Set(['copy-fix', 'open-path', 'refresh']);
  const panel = createActionPanel(vscode, 'cis.repositoryDoctor', 'Repository Doctor', allowed, onAction, onPath);
  const scriptNonce = nonce();
  const controller = {
    panel,
    update(nextStatus) { panel.webview.html = renderDoctorHtml(panel.webview, nextStatus, scriptNonce); },
  };
  controller.update(status);
  return controller;
}

function renderDoctorHtml(webview, result, scriptNonce) {
  const findings = Array.isArray(result?.findings) ? result.findings : [];
  const severity = finding => {
    const value = String(finding?.severity || 'information').toLowerCase();
    return value === 'error' ? 'error' : value === 'warning' ? 'warning' : 'information';
  };
  const groups = {
    error: findings.filter(finding => severity(finding) === 'error'),
    warning: findings.filter(finding => severity(finding) === 'warning'),
    information: findings.filter(finding => severity(finding) === 'information'),
  };
  const count = (reported, actual) => Number.isFinite(Number(reported)) ? Number(reported) : actual;
  const errorCount = count(result?.errorCount, groups.error.length);
  const warningCount = count(result?.warningCount, groups.warning.length);
  const informationCount = count(result?.informationCount, groups.information.length);
  const status = errorCount ? 'Errors' : warningCount ? 'Warnings' : 'Healthy';
  const state = errorCount ? 'bad' : warningCount ? 'warn' : 'good';
  const renderFinding = finding => {
    const level = severity(finding);
    const evidence = (Array.isArray(finding.evidence) ? finding.evidence : []).map(item => {
      const value = String(item || '');
      return `<li>${isOpenablePath(value)
        ? `<button class="link" type="button" data-command="open-path" data-value="${escapeHtml(value)}">${escapeHtml(value)}</button>`
        : `<code>${escapeHtml(value)}</code>`}</li>`;
    }).join('');
    const command = typeof finding.fixCommand === 'string' && finding.fixCommand.trim()
      ? finding.fixCommand.trim() : '';
    const fixability = labelLabel(finding.fixability || (command ? 'review-required' : 'none'));
    return `<article class="card doctor-finding" aria-labelledby="doctor-${escapeHtml(finding.code || 'finding')}">
      <div class="section-heading"><div><span class="eyebrow">${escapeHtml(finding.category || 'repository')} · ${escapeHtml(finding.code || 'CIS finding')}</span><h3 id="doctor-${escapeHtml(finding.code || 'finding')}">${escapeHtml(finding.message || 'Repository Doctor finding')}</h3></div><span class="badge ${level === 'error' ? 'bad' : level === 'warning' ? 'warn' : 'good'}">${escapeHtml(level)}</span></div>
      ${evidence ? `<details><summary>Evidence (${(finding.evidence || []).length})</summary><ul class="criteria">${evidence}</ul></details>` : '<p class="muted">No additional evidence paths were reported.</p>'}
      <section class="notice ${level === 'warning' ? 'warning' : ''}"><strong>Possible fix</strong><span>${escapeHtml(finding.suggestedFix || 'Review the finding and choose a bounded corrective action.')}</span><span class="muted">Fixability: ${escapeHtml(fixability)}</span></section>
      ${command ? `<div class="card command"><span class="eyebrow">Suggested CIS command</span><pre>${escapeHtml(command)}</pre><div class="actions"><button type="button" data-command="copy-fix" data-value="${escapeHtml(finding.code || '')}">Copy command</button></div></div>` : ''}
    </article>`;
  };
  const renderGroup = (title, level, items) => `<section aria-labelledby="doctor-${level}-heading"><div class="section-heading"><div><span class="eyebrow">${escapeHtml(level)}</span><h2 id="doctor-${level}-heading">${escapeHtml(title)} (${items.length})</h2></div></div><div class="cards">${items.map(renderFinding).join('') || `<article class="card empty">No ${escapeHtml(title.toLowerCase())} were reported.</article>`}</div></section>`;
  const body = `<header class="hero"><div><span class="eyebrow">Repository readiness · read-only diagnosis</span><h1>Repository Doctor</h1><p>Review each evidence-backed finding and its possible fix. Commands are never executed automatically; copy one only after reviewing its scope.</p></div><span class="badge ${state}">${escapeHtml(status)}</span></header>
    <section class="card request" aria-label="Doctor summary"><div><span class="eyebrow">Repository</span><strong>${escapeHtml(result?.repositoryPath || 'Current authority repository')}</strong></div><div><span class="eyebrow">Documentation root</span><strong>${escapeHtml(result?.documentationRoot || 'Not established')}</strong></div><div><span class="eyebrow">Errors</span><strong>${errorCount}</strong></div><div><span class="eyebrow">Warnings</span><strong>${warningCount}</strong></div><div><span class="eyebrow">Information</span><strong>${informationCount}</strong></div><div><span class="eyebrow">Local AI</span><strong>${escapeHtml(result?.ollama?.isAvailable ? `Available${result.ollama.models?.length ? ` · ${result.ollama.models.length} model(s)` : ''}` : 'Unavailable')}</strong></div></section>
    <div class="actions"><button type="button" data-command="refresh">Run Doctor again</button></div>
    ${renderGroup('Errors', 'error', groups.error)}
    ${renderGroup('Warnings', 'warning', groups.warning)}
    ${renderGroup('Information', 'information', groups.information)}`;
  return studioDocument(webview, 'Repository Doctor', body, scriptNonce);
}

function renderTechnicalIntentQuestionsHtml(webview, status, scriptNonce) {
  const questions = Array.isArray(status?.questions) ? status.questions : [];
  const resolvedStatuses = new Set(['answered', 'derived']);
  const unanswered = questions.filter(item => !resolvedStatuses.has(String(item.status || '').toLowerCase()));
  const derivedCount = questions.filter(item => String(item.status || '').toLowerCase() === 'derived').length;
  const humanCount = questions.filter(item => String(item.status || '').toLowerCase() === 'answered').length;
  const cards = questions.map(question => {
    const state = String(question.status || '').toLowerCase();
    const answered = state === 'answered';
    const derived = state === 'derived';
    const resolved = answered || derived;
    const options = (question.commonOptions || []).map(option => `<li>${escapeHtml(option)}</li>`).join('');
    const startingValue = question.answer || question.suggestedAnswer || '';
    const evidence = (question.evidence || []).map(item => `<li>${escapeHtml(item)}</li>`).join('');
    const source = derived
      ? `<section class="suggestion derived"><h3>Derived from the existing project</h3><p>${escapeHtml(question.answer || '')}</p><p class="muted">Confidence: ${escapeHtml(question.confidence || 'unrated')}</p>${evidence ? `<details><summary>Repository evidence</summary><ul>${evidence}</ul></details>` : ''}</section>`
      : `<section class="suggestion"><h3>Advisory starting direction</h3><p>${escapeHtml(question.suggestedAnswer || 'No suggestion is available.')}</p></section>`;
    return `<article class="question-card" data-question-id="${escapeHtml(question.id || '')}" aria-labelledby="technical-question-${escapeHtml(question.id || '')}">
      <header class="question-heading"><div><span class="eyebrow">${escapeHtml(question.id || '')} · ${escapeHtml(question.area || '')}</span><h2 id="technical-question-${escapeHtml(question.id || '')}">${escapeHtml(question.question || '')}</h2></div><span class="badge ${resolved ? 'answered' : 'unanswered'}">${derived ? 'Derived from project' : answered ? 'Answered' : 'Decision required'}</span></header>
      <p>${escapeHtml(question.why || '')}</p><details><summary>Common choices</summary><ul>${options}</ul></details>
      ${source}
      <section class="answer"><label for="technical-answer-${escapeHtml(question.id || '')}">${derived ? 'Derived direction — edit to override' : answered ? 'Recorded direction' : 'Your direction'}</label>
        <textarea id="technical-answer-${escapeHtml(question.id || '')}" maxlength="16384" rows="5" data-answer>${escapeHtml(startingValue)}</textarea>
        ${answered ? `<p class="muted">Recorded by ${escapeHtml(question.answeredBy || 'unknown')} at ${escapeHtml(question.answeredAtUtc || 'unknown time')}.</p>` : ''}
        <div class="actions"><button type="button" data-command="save-answer" data-value="${escapeHtml(question.id || '')}">${resolved ? 'Update direction' : 'Save direction'}</button></div></section></article>`;
  }).join('');
  const completed = questions.length && !unanswered.length
    ? '<section class="completion"><strong>High-level technical direction is complete.</strong><p>CIS can now generate the technical intent, component map, interaction map, architecture guidelines, and decision baseline from these exact choices.</p></section>' : '';
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';">
  <title>High-level technical direction</title><style nonce="${scriptNonce}">
  :root{color-scheme:light dark}*{box-sizing:border-box}body{color:var(--vscode-foreground);background:var(--vscode-editor-background);font:var(--vscode-font-weight) var(--vscode-font-size)/1.55 var(--vscode-font-family);margin:0;padding:2rem;max-width:78rem}main{display:grid;gap:1.2rem}.page-heading,.question-heading{display:flex;justify-content:space-between;align-items:start;gap:1rem}.page-heading{border-bottom:1px solid var(--vscode-panel-border);padding-bottom:1rem}.page-heading h1,.question-heading h2,.suggestion h3{margin:.15rem 0}.page-heading p,.question-card p{max-width:78ch}.eyebrow{display:block;color:var(--vscode-descriptionForeground);font-size:.78rem;font-weight:600;letter-spacing:.04em;text-transform:uppercase}.progress{display:flex;gap:.55rem;flex-wrap:wrap}.badge{display:inline-block;border:1px solid var(--vscode-panel-border);border-radius:999px;padding:.12rem .6rem;font-size:.82rem}.badge.answered,.completion{border-color:var(--vscode-testing-iconPassed);color:var(--vscode-testing-iconPassed)}.badge.unanswered{color:var(--vscode-editorWarning-foreground)}.question-list{display:grid;gap:1rem}.question-card,.completion{border:1px solid var(--vscode-panel-border);border-radius:.35rem;background:var(--vscode-editorWidget-background);padding:1.1rem}.suggestion{border-left:3px solid var(--vscode-focusBorder);background:var(--vscode-textBlockQuote-background);padding:.75rem 1rem;margin:1rem 0}.answer label{display:block;font-weight:600;margin-bottom:.35rem}.muted{color:var(--vscode-descriptionForeground)}textarea{display:block;width:100%;resize:vertical;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border,var(--vscode-panel-border));padding:.65rem;font:inherit;line-height:1.45}textarea:focus{outline:2px solid var(--vscode-focusBorder);outline-offset:1px}.actions{display:flex;gap:.6rem;flex-wrap:wrap;margin-top:.7rem}button{color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:1px solid transparent;border-radius:2px;padding:.48rem .85rem;font:inherit;cursor:pointer}button.secondary{color:var(--vscode-button-secondaryForeground);background:var(--vscode-button-secondaryBackground)}button.link{color:var(--vscode-textLink-foreground);background:transparent;padding:0}button:focus-visible,summary:focus-visible{outline:2px solid var(--vscode-focusBorder);outline-offset:2px}details{margin:.8rem 0}summary{cursor:pointer;font-weight:600}
  @media(max-width:42rem){body{padding:1rem}.page-heading,.question-heading{display:block}.progress{margin-top:.65rem}.actions button{width:100%}}@media(prefers-reduced-motion:reduce){*{transition:none!important}}
  </style></head><body><main><header class="page-heading"><div><span class="eyebrow">Technical definition · governed pre-intent stage</span><h1>Choose the high-level technical direction</h1>
    <p>${derivedCount ? 'CIS has pre-filled evidence-supported directions from the existing implementation. Review or override them, then answer only the decisions the repository cannot establish safely.' : 'This is a greenfield project. Record the major technology and architecture choices before CIS creates components and interactions.'}</p><button class="link" type="button" data-command="open-questionnaire">Open canonical questionnaire</button></div>
    <div class="progress" aria-label="Technical question progress"><span>${derivedCount} derived</span><span>${humanCount} human</span><span>${unanswered.length} remaining</span></div></header>
    ${completed}<section class="question-list" aria-label="High-level technical decisions">${cards || '<p>No technical questions were found.</p>'}</section></main>
  <script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();const prior=vscode.getState()||{};const restore=()=>{const card=prior.questionId?document.querySelector('[data-question-id="'+CSS.escape(prior.questionId)+'"]'):undefined;card?.querySelector('[data-answer]')?.focus({preventScroll:true});window.scrollTo(0,Number.isFinite(prior.scrollY)?prior.scrollY:0);};requestAnimationFrame(()=>requestAnimationFrame(restore));document.querySelectorAll('button[data-command]').forEach(button=>button.addEventListener('click',()=>{const command=button.dataset.command;let value=button.dataset.value||'';if(command==='save-answer'){const card=button.closest('[data-question-id]');vscode.setState({scrollY:window.scrollY,questionId:button.dataset.value||''});value=JSON.stringify({id:button.dataset.value||'',answer:card?.querySelector('[data-answer]')?.value||''});}vscode.postMessage({command,value});}));</script></body></html>`;
}

function renderUiDirectionQuestionsHtml(webview, status, scriptNonce) {
  const questions = Array.isArray(status?.questions) ? status.questions : [];
  const resolvedStatuses = new Set(['answered', 'derived']);
  const unanswered = questions.filter(item => !resolvedStatuses.has(String(item.status || '').toLowerCase()));
  const derivedCount = questions.filter(item => String(item.status || '').toLowerCase() === 'derived').length;
  const humanCount = questions.filter(item => String(item.status || '').toLowerCase() === 'answered').length;
  const cards = questions.map(question => {
    const state = String(question.status || '').toLowerCase();
    const answered = state === 'answered'; const derived = state === 'derived'; const resolved = answered || derived;
    const options = (question.commonOptions || []).map(option => `<li>${escapeHtml(option)}</li>`).join('');
    const evidence = (question.evidence || []).map(item => `<li>${escapeHtml(item)}</li>`).join('');
    const startingValue = question.answer || question.suggestedAnswer || '';
    const source = derived
      ? `<section class="suggestion derived"><h3>Derived from the existing product</h3><p>${escapeHtml(question.answer || '')}</p><p class="muted">Confidence: ${escapeHtml(question.confidence || 'unrated')}</p>${evidence ? `<details><summary>Source evidence</summary><ul>${evidence}</ul></details>` : ''}</section>`
      : `<section class="suggestion"><h3>Advisory starting direction</h3><p>${escapeHtml(question.suggestedAnswer || 'No suggestion is available.')}</p></section>`;
    return `<article class="question-card" data-question-id="${escapeHtml(question.id || '')}" aria-labelledby="ui-question-${escapeHtml(question.id || '')}">
      <header class="question-heading"><div><span class="eyebrow">${escapeHtml(question.id || '')} · ${escapeHtml(question.area || '')}</span><h2 id="ui-question-${escapeHtml(question.id || '')}">${escapeHtml(question.question || '')}</h2></div><span class="badge ${resolved ? 'answered' : 'unanswered'}">${derived ? 'Derived from project' : answered ? 'Answered' : 'Decision required'}</span></header>
      <p>${escapeHtml(question.why || '')}</p><details><summary>Common choices</summary><ul>${options}</ul></details>${source}
      <section class="answer"><label for="ui-answer-${escapeHtml(question.id || '')}">${derived ? 'Derived direction — edit to override' : answered ? 'Recorded direction' : 'Your direction'}</label>
        <textarea id="ui-answer-${escapeHtml(question.id || '')}" maxlength="16384" rows="5" data-answer>${escapeHtml(startingValue)}</textarea>
        ${answered ? `<p class="muted">Recorded by ${escapeHtml(question.answeredBy || 'unknown')} at ${escapeHtml(question.answeredAtUtc || 'unknown time')}.</p>` : ''}
        <div class="actions"><button type="button" data-command="save-answer" data-value="${escapeHtml(question.id || '')}">${resolved ? 'Update direction' : 'Save direction'}</button></div></section></article>`;
  }).join('');
  const completed = questions.length && !unanswered.length
    ? '<section class="completion"><strong>High-level UI choices are complete.</strong><p>CIS can now generate the workspace-level look, feel, shell, reusable-component, responsive, and accessibility direction.</p></section>' : '';
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';"><title>High-level UI direction</title><style nonce="${scriptNonce}">
  :root{color-scheme:light dark}*{box-sizing:border-box}body{color:var(--vscode-foreground);background:var(--vscode-editor-background);font:var(--vscode-font-weight) var(--vscode-font-size)/1.55 var(--vscode-font-family);margin:0;padding:2rem;max-width:78rem}main{display:grid;gap:1.2rem}.page-heading,.question-heading{display:flex;justify-content:space-between;align-items:start;gap:1rem}.page-heading{border-bottom:1px solid var(--vscode-panel-border);padding-bottom:1rem}.page-heading h1,.question-heading h2,.suggestion h3{margin:.15rem 0}.page-heading p,.question-card p{max-width:78ch}.eyebrow{display:block;color:var(--vscode-descriptionForeground);font-size:.78rem;font-weight:600;letter-spacing:.04em;text-transform:uppercase}.progress{display:flex;gap:.55rem;flex-wrap:wrap}.badge{display:inline-block;border:1px solid var(--vscode-panel-border);border-radius:999px;padding:.12rem .6rem;font-size:.82rem}.badge.answered,.completion{border-color:var(--vscode-testing-iconPassed);color:var(--vscode-testing-iconPassed)}.badge.unanswered{color:var(--vscode-editorWarning-foreground)}.question-list{display:grid;gap:1rem}.question-card,.completion{border:1px solid var(--vscode-panel-border);border-radius:.35rem;background:var(--vscode-editorWidget-background);padding:1.1rem}.suggestion{border-left:3px solid var(--vscode-focusBorder);background:var(--vscode-textBlockQuote-background);padding:.75rem 1rem;margin:1rem 0}.answer label{display:block;font-weight:600;margin-bottom:.35rem}.muted{color:var(--vscode-descriptionForeground)}textarea{display:block;width:100%;resize:vertical;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border,var(--vscode-panel-border));padding:.65rem;font:inherit;line-height:1.45}textarea:focus{outline:2px solid var(--vscode-focusBorder);outline-offset:1px}.actions{display:flex;gap:.6rem;flex-wrap:wrap;margin-top:.7rem}button{color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:1px solid transparent;border-radius:2px;padding:.48rem .85rem;font:inherit;cursor:pointer}button.link{color:var(--vscode-textLink-foreground);background:transparent;padding:0}button:focus-visible,summary:focus-visible{outline:2px solid var(--vscode-focusBorder);outline-offset:2px}details{margin:.8rem 0}summary{cursor:pointer;font-weight:600}
  @media(max-width:42rem){body{padding:1rem}.page-heading,.question-heading{display:block}.progress{margin-top:.65rem}.actions button{width:100%}}@media(prefers-reduced-motion:reduce){*{transition:none!important}}
  </style></head><body><main><header class="page-heading"><div><span class="eyebrow">Experience definition · governed product direction</span><h1>Define the high-level UI look and feel</h1><p>${derivedCount ? 'CIS has pre-filled objective choices from approved architecture and UI-framework evidence. Review or override them, then decide the product-specific experience choices.' : 'Choose the visual character, shell, density, components, responsive behavior, and accessibility baseline before feature wireframes are created.'}</p><button class="link" type="button" data-command="open-questionnaire">Open canonical questionnaire</button></div><div class="progress" aria-label="UI question progress"><span>${derivedCount} derived</span><span>${humanCount} human</span><span>${unanswered.length} remaining</span></div></header>
    ${completed}<section class="question-list" aria-label="High-level UI decisions">${cards || '<p>No UI-direction questions were found.</p>'}</section></main>
  <script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();const prior=vscode.getState()||{};const restore=()=>{const card=prior.questionId?document.querySelector('[data-question-id="'+CSS.escape(prior.questionId)+'"]'):undefined;card?.querySelector('[data-answer]')?.focus({preventScroll:true});window.scrollTo(0,Number.isFinite(prior.scrollY)?prior.scrollY:0);};requestAnimationFrame(()=>requestAnimationFrame(restore));document.querySelectorAll('button[data-command]').forEach(button=>button.addEventListener('click',()=>{const command=button.dataset.command;let value=button.dataset.value||'';if(command==='save-answer'){const card=button.closest('[data-question-id]');vscode.setState({scrollY:window.scrollY,questionId:button.dataset.value||''});value=JSON.stringify({id:button.dataset.value||'',answer:card?.querySelector('[data-answer]')?.value||''});}vscode.postMessage({command,value});}));</script></body></html>`;
}

function renderBrdQuestionsHtml(webview, status, scriptNonce) {
  const questions = Array.isArray(status?.questions) ? status.questions : [];
  const unanswered = questions.filter(item => String(item.status || '').toLowerCase() === 'unanswered');
  const suggested = questions.filter(item => typeof item.suggestedAnswer === 'string' && item.suggestedAnswer.trim());
  const provenance = status?.suggestionProvider
    ? `${status.suggestionProvider}${status.suggestionModel ? ` / ${status.suggestionModel}` : ''}` : 'No suggestion run retained';
  const errors = (status?.errors || []).length
    ? `<section class="error" role="alert"><strong>Guidance issue</strong><ul>${status.errors.map(error => `<li>${escapeHtml(error)}</li>`).join('')}</ul></section>` : '';
  const cards = questions.map(question => {
    const isAnswered = String(question.status || '').toLowerCase() === 'answered';
    const contexts = Array.isArray(question.context) ? question.context : [];
    const contextById = new Map(contexts.map(item => [item.id, item]));
    const cited = (question.suggestionContextIds || []).map(id => contextById.get(id)).filter(Boolean);
    const contextCards = contexts.map(item => `<article class="context-item" id="${escapeHtml(item.id || '')}"><strong>${escapeHtml(item.section || 'BRD context')}</strong><p>${escapeHtml(item.excerpt || '')}</p></article>`).join('');
    const suggestion = question.suggestedAnswer ? `<section class="suggestion" aria-labelledby="suggestion-${escapeHtml(question.id || '')}">
        <div class="section-heading"><h3 id="suggestion-${escapeHtml(question.id || '')}">Advisory suggested answer</h3><span class="badge">${escapeHtml(question.suggestionConfidence || 'unrated')}</span></div>
        <p>${escapeHtml(question.suggestedAnswer)}</p><p class="muted">${escapeHtml(question.suggestionReason || 'Review against the cited context before accepting.')}</p>
        ${cited.length ? `<p class="citations"><strong>Based on:</strong> ${cited.map(item => escapeHtml(item.section)).join(', ')}</p>` : ''}</section>`
      : `<section class="no-suggestion"><strong>No supported answer is currently suggested.</strong><p>${escapeHtml(question.suggestionReason || 'Use the BRD context below and supply the stakeholder decision, or generate advisory suggestions.')}</p></section>`;
    const startingValue = question.answer || question.suggestedAnswer || '';
    const answerMeta = isAnswered ? `<p class="answer-meta">Recorded by ${escapeHtml(question.answeredBy || 'unknown')} at ${escapeHtml(question.answeredAtUtc || 'unknown time')}.</p>` : '';
    return `<article class="question-card" data-question-id="${escapeHtml(question.id || '')}" aria-labelledby="question-${escapeHtml(question.id || '')}">
      <header class="question-heading"><div><span class="eyebrow">${escapeHtml(question.id || 'Open question')}</span><h2 id="question-${escapeHtml(question.id || '')}">${escapeHtml(question.question || '')}</h2></div><span class="badge ${isAnswered ? 'answered' : 'unanswered'}">${escapeHtml(question.status || 'Unknown')}</span></header>
      ${suggestion}<section class="answer"><label for="answer-${escapeHtml(question.id || '')}">${isAnswered ? 'Recorded answer' : 'Your answer'}</label>
        <textarea id="answer-${escapeHtml(question.id || '')}" maxlength="16384" rows="5" data-answer>${escapeHtml(startingValue)}</textarea>${answerMeta}
        <div class="actions">${!isAnswered && question.suggestedAnswer ? `<button type="button" data-command="accept-suggestion" data-value="${escapeHtml(question.id || '')}">Accept suggestion</button>` : ''}
          <button class="secondary" type="button" data-command="save-answer" data-value="${escapeHtml(question.id || '')}">${isAnswered ? 'Update answer' : question.suggestedAnswer ? 'Save edited answer' : 'Save answer'}</button></div></section>
      <details><summary>Relevant BRD context (${contexts.length})</summary><div class="context-list">${contextCards || '<p>No bounded context was found.</p>'}</div></details></article>`;
  }).join('');
  const completed = unanswered.length === 0 && questions.length
    ? '<section class="completion"><strong>All open questions have recorded human answers.</strong><p>CIS will now update the relevant BRD sections from these decisions, then require an independent review of the updated document.</p></section>' : '';
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';">
  <title>BRD open questions</title><style nonce="${scriptNonce}">
  :root{color-scheme:light dark}*{box-sizing:border-box}body{color:var(--vscode-foreground);background:var(--vscode-editor-background);font:var(--vscode-font-weight) var(--vscode-font-size)/1.55 var(--vscode-font-family);margin:0;padding:2rem;max-width:78rem}main{display:grid;gap:1.2rem}.page-heading,.question-heading,.section-heading,.toolbar{display:flex;justify-content:space-between;align-items:start;gap:1rem}.page-heading{border-bottom:1px solid var(--vscode-panel-border);padding-bottom:1rem}.page-heading h1,.question-heading h2,.suggestion h3{margin:.15rem 0}.page-heading p,.question-card p{max-width:78ch}.eyebrow{display:block;color:var(--vscode-descriptionForeground);font-size:.78rem;font-weight:600;letter-spacing:.04em;text-transform:uppercase}.toolbar{align-items:center;flex-wrap:wrap}.progress{display:flex;gap:.55rem;flex-wrap:wrap}.badge{display:inline-block;border:1px solid var(--vscode-panel-border);border-radius:999px;padding:.12rem .6rem;font-size:.82rem;text-transform:capitalize}.badge.answered,.completion{border-color:var(--vscode-testing-iconPassed);color:var(--vscode-testing-iconPassed)}.badge.unanswered{color:var(--vscode-editorWarning-foreground)}.question-list{display:grid;gap:1rem}.question-card,.completion,.error{border:1px solid var(--vscode-panel-border);border-radius:.35rem;background:var(--vscode-editorWidget-background);padding:1.1rem}.suggestion{border-left:3px solid var(--vscode-focusBorder);background:var(--vscode-textBlockQuote-background);padding:.8rem 1rem;margin:1rem 0}.suggestion h3{font-size:1rem}.no-suggestion{border-left:3px solid var(--vscode-panel-border);padding:.4rem 1rem;margin:1rem 0}.muted,.answer-meta,.citations,.no-suggestion p{color:var(--vscode-descriptionForeground)}.answer label{display:block;font-weight:600;margin-bottom:.35rem}textarea{display:block;width:100%;resize:vertical;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border,var(--vscode-panel-border));padding:.65rem;font:inherit;line-height:1.45}textarea:focus{outline:2px solid var(--vscode-focusBorder);outline-offset:1px}.actions{display:flex;gap:.6rem;flex-wrap:wrap;margin-top:.7rem}button{color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:1px solid transparent;border-radius:2px;padding:.48rem .85rem;font:inherit;cursor:pointer}button:hover{background:var(--vscode-button-hoverBackground)}button.secondary{color:var(--vscode-button-secondaryForeground);background:var(--vscode-button-secondaryBackground)}button.link{color:var(--vscode-textLink-foreground);background:transparent;padding:0}button:focus-visible,summary:focus-visible{outline:2px solid var(--vscode-focusBorder);outline-offset:2px}details{margin-top:1rem;border-top:1px solid var(--vscode-panel-border);padding-top:.75rem}summary{cursor:pointer;font-weight:600}.context-list{display:grid;gap:.6rem;margin-top:.7rem}.context-item{border-left:2px solid var(--vscode-panel-border);padding-left:.8rem}.context-item p{margin:.2rem 0}.error{border-color:var(--vscode-testing-iconFailed)}
  @media(max-width:42rem){body{padding:1rem}.page-heading,.question-heading{display:block}.progress{margin-top:.65rem}.actions button,.toolbar>button{width:100%}}@media(prefers-reduced-motion:reduce){*{transition:none!important}}
  </style></head><body><main><header class="page-heading"><div><span class="eyebrow">Business requirements</span><h1>Answer open questions</h1>
    <p>Review the relevant BRD context and any advisory suggestion. Nothing is recorded until you explicitly accept a suggestion or save your edited answer.</p><button class="link" type="button" data-command="open-brd">Open canonical BRD</button></div>
    <div class="progress" aria-label="Question progress"><span>${unanswered.length} unanswered</span><span>${questions.length - unanswered.length} answered</span><span>${suggested.length} suggested</span></div></header>
    <section class="toolbar" aria-label="Question guidance actions"><div><strong>Suggestion evidence: ${escapeHtml(provenance)}</strong><div class="muted">Status: ${escapeHtml(status?.suggestionStatus || 'missing')}</div></div><button type="button" data-command="generate-suggestions">${status?.suggestionStatus === 'current' ? 'Regenerate advisory suggestions' : 'Generate advisory suggestions'}</button></section>
    ${errors}${completed}<section class="question-list" aria-label="Open questions">${cards || '<p>No open questions were found.</p>'}</section></main>
  <script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();document.querySelectorAll('button[data-command]').forEach(button=>button.addEventListener('click',()=>{const command=button.dataset.command;let value=button.dataset.value||'';if(command==='save-answer'){const card=button.closest('[data-question-id]');const answer=card?.querySelector('[data-answer]')?.value||'';value=JSON.stringify({id:button.dataset.value||'',answer});}vscode.postMessage({command,value});}));</script></body></html>`;
}

function renderRecommendationReviewHtml(webview, runId, status, scriptNonce) {
  const disposition = status?.disposition || {};
  const findings = Array.isArray(disposition.findings) ? disposition.findings : [];
  const pending = findings.filter(item => String(item.decision || '').toLowerCase() === 'pending');
  const accepted = findings.filter(item => String(item.decision || '').toLowerCase() === 'accepted');
  const rejected = findings.filter(item => String(item.decision || '').toLowerCase() === 'rejected');
  const state = String(status?.status || 'review-required').toLowerCase();
  const approved = state === 'approved' || state === 'applied';
  const reviewedCount = findings.length - pending.length;
  const progress = findings.length ? `${reviewedCount} of ${findings.length} reviewed` : 'No findings';
  const canonical = status?.canonicalPath
    ? '<button class="link" type="button" data-command="open-review">Open canonical review record</button>' : '';
  const recommendationCards = findings.map(item => {
    const decision = String(item.decision || 'pending').toLowerCase();
    const approvedRecommendation = item.approvedRecommendation || item.recommendation || 'No recommendation was supplied.';
    const controls = decision === 'pending' ? `<footer class="actions" aria-label="Decision for ${escapeHtml(item.id || 'finding')}">
        <button type="button" data-command="accept" data-value="${escapeHtml(item.id || '')}">Approve as is</button>
        <button class="secondary" type="button" data-command="modify" data-value="${escapeHtml(item.id || '')}">Modify and approve</button></footer>` : '';
    return `<article class="finding" aria-labelledby="finding-${escapeHtml(item.id || 'unknown')}">
      <header class="finding-heading"><div><span class="eyebrow">${escapeHtml(item.category || 'Review finding')}</span><h3 id="finding-${escapeHtml(item.id || 'unknown')}">${escapeHtml(item.id || 'Recommendation')}</h3></div>
        <div class="badges"><span class="badge severity">${escapeHtml(item.severity || 'unclassified')}</span><span class="badge ${escapeHtml(decision)}">${escapeHtml(decision)}</span></div></header>
      <dl class="meta"><div><dt>Location</dt><dd>${escapeHtml(item.location || 'Whole document')}</dd></div></dl>
      <section><h4>What the reviewer found</h4><p>${escapeHtml(item.observation || 'No observation was supplied.')}</p></section>
      <section class="recommendation"><h4>${decision === 'accepted' ? 'Approved recommendation' : 'Recommendation'}</h4><p>${escapeHtml(approvedRecommendation)}</p></section>
      ${item.approvedRecommendation && item.approvedRecommendation !== item.recommendation
        ? `<p class="source-recommendation"><strong>Reviewer originally proposed:</strong> ${escapeHtml(item.recommendation || '')}</p>` : ''}${item.rationale
        ? `<p class="rationale"><strong>Legacy rejection rationale:</strong> ${escapeHtml(item.rationale)}</p>` : ''}${controls}</article>`;
  }).join('');
  const completion = approved ? `<section class="completion approved" aria-labelledby="completion-title"><span class="eyebrow">Disposition set approved</span>
        <h2 id="completion-title">The bounded remediation scope is locked</h2>
        <p>${accepted.length} approved and ${rejected.length} legacy rejected. ${state === 'applied'
          ? 'The approved recommendations have been applied.' : 'Approved recommendations are ready for the revision agent.'}</p></section>`
    : pending.length === 0 ? `<section class="completion" aria-labelledby="completion-title"><span class="eyebrow">All recommendations reviewed</span>
          <h2 id="completion-title">Preparing the BRD revision</h2>
          <p>CIS is mechanically locking ${accepted.length} individually approved recommendation${accepted.length === 1 ? '' : 's'}${rejected.length ? ` and ${rejected.length} legacy rejected recommendation${rejected.length === 1 ? '' : 's'}` : ''}, then it will open agent selection. No additional human approval is required.</p></section>`
      : '';
  const batch = pending.length ? `<section class="batch-bar" aria-label="Batch decision"><div><strong>${pending.length} pending recommendation${pending.length === 1 ? '' : 's'}</strong><p>Approve every unchanged pending recommendation atomically, or edit individual items below first.</p></div>
      <button type="button" data-command="accept-all">Approve all pending as is</button></section>` : '';
  const content = `${completion}${batch}<section class="recommendation-list" aria-labelledby="recommendations-title"><h2 id="recommendations-title">Overall recommendation list</h2><div class="finding-grid">${recommendationCards}</div></section>`;
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';">
  <title>${escapeHtml(`BRD review ${runId}`)}</title><style nonce="${scriptNonce}">
  :root{color-scheme:light dark}*{box-sizing:border-box}body{color:var(--vscode-foreground);background:var(--vscode-editor-background);font:var(--vscode-font-weight) var(--vscode-font-size)/1.55 var(--vscode-font-family);margin:0;padding:2rem;max-width:78rem}main{display:grid;gap:1.25rem}.page-heading,.batch-bar{display:flex;justify-content:space-between;align-items:start;gap:1rem}.page-heading{border-bottom:1px solid var(--vscode-panel-border);padding-bottom:1rem}.page-heading h1,.finding h3,.completion h2,.recommendation-list h2{margin:.15rem 0}.page-heading p,.finding p,.completion p,.batch-bar p{max-width:76ch}.batch-bar{align-items:center;border:1px solid var(--vscode-focusBorder);border-radius:.35rem;padding:1rem}.batch-bar p{margin:.2rem 0 0;color:var(--vscode-descriptionForeground)}.eyebrow,dt{display:block;color:var(--vscode-descriptionForeground);font-size:.78rem;font-weight:600;letter-spacing:.04em;text-transform:uppercase}.progress,.badges{display:flex;gap:.45rem;flex-wrap:wrap;align-items:center}.badge{display:inline-block;border:1px solid var(--vscode-panel-border);border-radius:999px;padding:.12rem .6rem;font-size:.82rem;text-transform:capitalize}.badge.accepted,.approved{border-color:var(--vscode-testing-iconPassed);color:var(--vscode-testing-iconPassed)}.badge.rejected{border-color:var(--vscode-testing-iconFailed);color:var(--vscode-testing-iconFailed)}.badge.pending,.badge.severity{color:var(--vscode-editorWarning-foreground)}.finding,.completion{border:1px solid var(--vscode-panel-border);border-radius:.35rem;background:var(--vscode-editorWidget-background);padding:1.1rem}.finding-grid{display:grid;gap:1rem}.finding-heading{display:flex;justify-content:space-between;gap:1rem}.meta{margin:1rem 0}.meta div{border-top:1px solid var(--vscode-panel-border);padding-top:.65rem}.meta dd{margin:.2rem 0 0;overflow-wrap:anywhere}.recommendation{border-left:3px solid var(--vscode-focusBorder);background:var(--vscode-textBlockQuote-background);padding:.75rem 1rem;margin-top:1rem}.recommendation h4{margin-top:0}.actions{display:flex;flex-wrap:wrap;gap:.6rem;margin-top:1.2rem}button{color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:1px solid transparent;border-radius:2px;padding:.48rem .85rem;font:inherit;cursor:pointer}button:hover{background:var(--vscode-button-hoverBackground)}button.secondary{color:var(--vscode-button-secondaryForeground);background:var(--vscode-button-secondaryBackground)}button.secondary:hover{background:var(--vscode-button-secondaryHoverBackground)}button.link{color:var(--vscode-textLink-foreground);background:transparent;padding:0}button.link:hover{color:var(--vscode-textLink-activeForeground);background:transparent;text-decoration:underline}button:focus-visible{outline:2px solid var(--vscode-focusBorder);outline-offset:2px}.rationale,.source-recommendation{color:var(--vscode-descriptionForeground)}
  @media(max-width:42rem){body{padding:1rem}.page-heading,.finding-heading{display:block}.progress{margin-top:.75rem}.meta{grid-template-columns:1fr}.actions button{width:100%}}@media(prefers-reduced-motion:reduce){*{scroll-behavior:auto!important;transition:none!important}}
  </style></head><body><main><header class="page-heading"><div><span class="eyebrow">Independent BRD review</span><h1>Review recommendations</h1>
    <p>Review the complete consolidated list, approve unchanged recommendations individually or as one batch, and edit only the items that need different remediation text. These decisions define revision scope; they do not approve or edit the BRD itself.</p>${canonical}</div>
    <div class="progress" aria-label="Review progress"><span class="badge pending">${escapeHtml(progress)}</span><span>${pending.length} pending</span><span>${accepted.length} accepted</span><span>${rejected.length} rejected</span></div></header>${content}</main>
  <script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();document.querySelectorAll('button[data-command]').forEach(button=>button.addEventListener('click',()=>vscode.postMessage({command:button.dataset.command,value:button.dataset.value||''})));</script></body></html>`;
}

function openChangeOverviewPanel(vscode, overview, onAction, onPath) {
  const changeId = overview?.identity?.id || 'Change';
  const panel = createActionPanel(vscode, 'cis.changeOverview', `${changeId} · Change overview`,
    new Set(['canonical', 'design', 'transition', 'agent', 'open-task', 'open-path']), onAction, onPath);
  const scriptNonce = nonce();
  panel.webview.html = renderChangeOverviewHtml(panel.webview, overview, scriptNonce);
  return panel;
}

function renderChangeOverviewHtml(webview, overview, scriptNonce) {
  const identity = overview?.identity || {};
  const progress = overview?.progress || {};
  const scope = overview?.scope || {};
  const design = overview?.design || {};
  const tasks = Array.isArray(overview?.tasks) ? overview.tasks : [];
  const decisions = Array.isArray(overview?.decisions) ? overview.decisions : [];
  const documents = Object.entries(overview?.canonicalDocuments || {});
  const taskRows = tasks.map(task => `<li class="row"><button class="row-main" type="button" data-command="open-task" data-value="${escapeHtml(task.id || '')}">
      <span><strong>${escapeHtml(task.id || 'Task')}</strong> · ${escapeHtml(task.title || 'Untitled')}</span>
      <span class="muted">${escapeHtml(task.category || 'uncategorized')} · ${escapeHtml(task.complexity || 'unclassified')}</span></button>
      <span class="badge ${stateClass(task.status)}">${escapeHtml(task.status || 'Unknown')}</span></li>`).join('');
  const decisionRows = decisions.length ? decisions.map(item => `<li class="row static"><span><strong>${escapeHtml(item.id || item.decisionId || 'Decision')}</strong><br>
      <span class="muted">${escapeHtml(item.title || item.summary || item.status || 'Recorded decision')}</span></span></li>`).join('')
    : '<li class="empty">No decisions were reported for this change.</li>';
  const documentRows = documents.map(([label, value]) => `<li><button class="link" type="button" data-command="open-path" data-value="${escapeHtml(value)}">${escapeHtml(labelLabel(label))}</button></li>`).join('');
  const body = `<header class="hero"><div><span class="eyebrow">Change overview</span><h1>${escapeHtml(`${identity.id || 'Change'} · ${identity.title || 'Untitled'}`)}</h1>
      <p>${escapeHtml(identity.outcome || 'No outcome was reported.')}</p></div><span class="badge ${stateClass(identity.lifecycle)}">${escapeHtml(identity.lifecycle || 'Unknown')}</span></header>
    <section class="notice"><strong>${escapeHtml(progress.planStatus || 'Plan status unavailable')}</strong><span>${escapeHtml(progress.nextRecommendedAction || 'No recommended action is currently available.')}</span></section>
    <section class="cards two"><article class="card"><span class="eyebrow">Progress</span><h2>${escapeHtml(`${progress.completedTasks ?? 0} / ${progress.executableTasks ?? 0} tasks complete`)}</h2>
      <p>Next meaningful action</p><strong>${escapeHtml(progress.nextRecommendedAction || 'Review final verification state.')}</strong></article>
      <article class="card"><span class="eyebrow">Accepted scope</span><h2>${escapeHtml(`${(scope.acceptedImpactIds || []).length} impacts`)}</h2>
      <p>${escapeHtml((scope.roots || []).map(root => root.id || root).join(', ') || 'No bounded roots were reported.')}</p></article></section>
    <section class="card"><div class="section-heading"><div><span class="eyebrow">Delivery plan</span><h2>Tasks</h2></div><span class="badge ${stateClass(design.gate)}">Design: ${escapeHtml(design.gate || 'Not applicable')}</span></div>
      <ul class="rows">${taskRows || '<li class="empty">No planned tasks were reported.</li>'}</ul></section>
    <section class="cards two"><article class="card"><span class="eyebrow">Governed decisions</span><h2>Decision record</h2><ul class="rows">${decisionRows}</ul></article>
      <article class="card"><span class="eyebrow">Canonical evidence</span><h2>Open source records</h2><ul class="links">${documentRows || '<li class="empty">No canonical files were reported.</li>'}</ul></article></section>
    <footer class="actions" aria-label="Change actions"><button type="button" data-command="canonical" data-value="${escapeHtml(identity.id || '')}">Open canonical dossier</button>
      <button class="secondary" type="button" data-command="design" data-value="${escapeHtml(identity.id || '')}">Review design</button>
      <button class="secondary" type="button" data-command="transition" data-value="${escapeHtml(identity.id || '')}">Transition task</button>
      <button class="secondary" type="button" data-command="agent" data-value="${escapeHtml(identity.id || '')}">Request agent work</button></footer>`;
  return studioDocument(webview, `${identity.id || 'Change'} overview`, body, scriptNonce);
}

function openTaskDetailPanel(vscode, changeId, task, onAction, onPath) {
  const panel = createActionPanel(vscode, 'cis.taskDetail', `${task.id} · Task detail`,
    new Set(['transition', 'agent', 'open-path']), onAction, onPath);
  const scriptNonce = nonce();
  panel.webview.html = renderTaskDetailHtml(panel.webview, changeId, task, scriptNonce);
  return panel;
}

function renderTaskDetailHtml(webview, changeId, task, scriptNonce) {
  const list = value => Array.isArray(value) ? value : value ? [value] : [];
  const requirements = list(task.requirementIds || task.requirements);
  const dependencies = list(task.dependencies || task.dependsOn);
  const targets = list(task.targets);
  const criteria = list(task.acceptanceCriteria);
  const body = `<header class="hero"><div><span class="eyebrow">Planned task · ${escapeHtml(changeId)}</span><h1>${escapeHtml(`${task.id} · ${task.title || 'Untitled'}`)}</h1>
      <p>${escapeHtml(task.category || 'uncategorized')} · ${escapeHtml(task.complexity || 'unclassified')} complexity</p></div><span class="badge ${stateClass(task.status)}">${escapeHtml(task.status || 'Unknown')}</span></header>
    <section class="cards two"><article class="card"><span class="eyebrow">Dependencies</span><h2>${escapeHtml(dependencies.length ? dependencies.join(', ') : 'None')}</h2>
      <p>Approval gate: <strong>${escapeHtml(task.approvalGate || 'none')}</strong></p></article>
      <article class="card"><span class="eyebrow">Repository routing</span><h2>${escapeHtml(targets.length ? targets.join(', ') : 'Authority repository')}</h2>
      <p>${escapeHtml(requirements.length ? `${requirements.length} requirement references` : 'No requirement references reported.')}</p></article></section>
    <section class="card"><span class="eyebrow">Acceptance criteria</span><h2>Completion boundary</h2><ul class="criteria">${criteria.length
      ? criteria.map(item => `<li>${escapeHtml(item)}</li>`).join('') : '<li>No acceptance criteria were reported.</li>'}</ul></section>
    <section class="card"><span class="eyebrow">Traceability</span><h2>Requirements</h2><p>${escapeHtml(requirements.join(', ') || 'No linked requirements were reported.')}</p></section>
    <footer class="actions" aria-label="Task actions">${task.taskPath
      ? `<button class="secondary" type="button" data-command="open-path" data-value="${escapeHtml(task.taskPath)}">Open canonical task</button>` : ''}
      <button type="button" data-command="transition" data-value="${escapeHtml(task.id || '')}">Transition task</button>
      <button class="secondary" type="button" data-command="agent" data-value="${escapeHtml(task.id || '')}">Request agent work</button></footer>`;
  return studioDocument(webview, `${task.id || 'Task'} detail`, body, scriptNonce);
}

function openRunDetailPanel(vscode, run, actions, onAction, onPath) {
  const runId = run?.runId || run?.manifest?.runId || 'Run';
  const allowed = new Set([...actions.map(action => action.command), 'open-path']);
  const panel = createActionPanel(vscode, 'cis.runDetail', `${runId} · Run detail`, allowed, onAction, onPath);
  const scriptNonce = nonce();
  panel.webview.html = renderRunDetailHtml(panel.webview, run, actions, scriptNonce);
  return panel;
}

function renderRunDetailHtml(webview, run, actions, scriptNonce) {
  const manifest = run?.manifest || {};
  const result = run?.result || {};
  const runId = run?.runId || manifest.runId || 'Run';
  const status = manifest.status || result.status || run?.status || 'Unknown';
  const artifacts = Array.isArray(run?.artifacts) ? run.artifacts : [];
  const events = Array.isArray(run?.events) ? run.events.slice(-20) : [];
  const artifactRows = artifacts.length ? artifacts.map(item => `<li class="row static"><span><strong>${escapeHtml(item.path || 'Artifact')}</strong><br>
      <span class="muted">${escapeHtml(item.sha256 || 'No digest')} · ${item.valid === false ? 'invalid' : 'valid'}</span></span></li>`).join('')
    : '<li class="empty">No retained artifacts were reported.</li>';
  const eventRows = events.length ? events.map(item => `<li class="timeline"><span>${escapeHtml(item.timestampUtc || '')}</span><strong>${escapeHtml(item.kind || 'event')}</strong><p>${escapeHtml(item.message || item.providerEventType || '')}</p></li>`).join('')
    : '<li class="empty">No bounded events were reported.</li>';
  const buttons = actions.map(action => `<button class="${action.command === 'cancel' ? '' : 'secondary'}" type="button" data-command="${escapeHtml(action.command)}" data-value="${escapeHtml(action.value || runId)}">${escapeHtml(action.label)}</button>`).join('');
  const body = `<header class="hero"><div><span class="eyebrow">Agent run evidence</span><h1>${escapeHtml(runId)}</h1>
      <p>${escapeHtml(`${manifest.changeId || run.changeId || 'Unbound change'} / ${manifest.taskId || run.taskId || 'Unbound task'}`)}</p></div><span class="badge ${stateClass(status)}">${escapeHtml(status)}</span></header>
    <section class="cards two"><article class="card"><span class="eyebrow">Execution</span><h2>${escapeHtml(manifest.provider || run.provider || 'Provider unavailable')}</h2>
      <p>${escapeHtml(`${manifest.mode || 'mode unavailable'} · ${manifest.permission || manifest.permissionCeiling || 'ceiling unavailable'} · attempt ${manifest.attempt || run.attempt || 1}`)}</p></article>
      <article class="card"><span class="eyebrow">Isolation and provenance</span><h2>${escapeHtml(manifest.isolation || manifest.workspaceMode || 'Not reported')}</h2>
      <p>${escapeHtml(manifest.repositoryRevision || manifest.baseline || 'Repository revision unavailable')}</p></article></section>
    <section class="card"><span class="eyebrow">Result</span><h2>${escapeHtml(result.summary || 'No bounded result summary was reported.')}</h2>
      <p>${escapeHtml((result.changedFiles || []).length ? `${result.changedFiles.length} changed files` : 'No changed files')}</p></section>
    <section class="cards two"><article class="card"><span class="eyebrow">Artifacts</span><h2>Retained evidence</h2><ul class="rows">${artifactRows}</ul></article>
      <article class="card"><span class="eyebrow">Events</span><h2>Latest bounded events</h2><ol class="timeline-list">${eventRows}</ol></article></section>
    ${buttons ? `<footer class="actions" aria-label="Run actions">${buttons}</footer>` : ''}`;
  return studioDocument(webview, `${runId} detail`, body, scriptNonce);
}

function confirmAgentRequest(vscode, request) {
  if (typeof vscode.window.createWebviewPanel !== 'function') return vscode.window.showWarningMessage(
    agentRequestConfirmation(request), { modal: true }, 'Start governed work');
  const panel = vscode.window.createWebviewPanel('cis.agentRequest', `Request agent work · ${request.taskId}`, vscode.ViewColumn.Active, {
    enableScripts: true, retainContextWhenHidden: false, localResourceRoots: [],
  });
  const scriptNonce = nonce();
  panel.webview.html = renderAgentRequestHtml(panel.webview, request, scriptNonce);
  return new Promise(resolve => {
    let completed = false;
    const finish = value => {
      if (completed) return;
      completed = true;
      if (typeof panel.dispose === 'function') panel.dispose();
      resolve(value);
    };
    panel.webview.onDidReceiveMessage(message => {
      if (!isValidWebviewMessage(message, new Set(['start', 'cancel']))) return;
      finish(message.command === 'start' ? 'Start governed work' : undefined);
    });
    if (typeof panel.onDidDispose === 'function') panel.onDidDispose(() => finish(undefined));
  });
}

function renderAgentRequestHtml(webview, request, scriptNonce) {
  const body = `<header class="hero"><div><span class="eyebrow">Governed agent request</span><h1>Request agent work</h1>
      <p>Confirm the exact provider, repository, isolation, run mode, and permission ceiling before process creation.</p></div><span class="badge warn">Confirmation</span></header>
    <section class="card request"><div><span class="eyebrow">Task</span><strong>${escapeHtml(`${request.changeId} / ${request.taskId}`)}</strong></div>
      <div><span class="eyebrow">Repository</span><strong>${escapeHtml(request.target || 'Authority repository')}</strong></div>
      <div><span class="eyebrow">Provider / transport</span><strong>${escapeHtml(`${request.providerName || request.providerId} · ${request.transport}`)}</strong></div>
      <div><span class="eyebrow">Isolation</span><strong>${escapeHtml(request.isolation)}</strong></div>
      <div><span class="eyebrow">Run mode</span><strong>${escapeHtml(request.mode)}</strong></div>
      <div><span class="eyebrow">Permission ceiling</span><strong>${escapeHtml(request.permission)}</strong></div>
      <div><span class="eyebrow">Approval requests</span><strong>${escapeHtml(request.approveRequests ? 'Bounded inside declared ceiling' : 'Denied')}</strong></div>
      <div><span class="eyebrow">Human actor</span><strong>${escapeHtml(request.actor)}</strong></div></section>
    <section class="notice warning"><strong>Do not enter credentials or secrets</strong><span>Provider authentication remains provider-native. CIS records the exact bounded request and cannot silently expand it.</span></section>
    <footer class="actions" aria-label="Agent request confirmation"><button class="secondary" type="button" data-command="cancel">Cancel</button>
      <button type="button" data-command="start">Start agent work</button></footer>`;
  return studioDocument(webview, `Request agent work · ${request.taskId}`, body, scriptNonce);
}

function agentRequestConfirmation(request) {
  return `Start ${request.providerName || request.providerId} for ${request.changeId}/${request.taskId}? Target: ${request.target || 'authority repository'}; mode: ${request.mode}; permission ceiling: ${request.permission}; isolation: ${request.isolation}; transport: ${request.transport}; approval requests: ${request.approveRequests ? 'bounded ceiling' : 'denied'}.`;
}

function openCommandProgressPanel(vscode, title, model, onCancel) {
  const panel = vscode.window.createWebviewPanel('cis.commandProgress', `${title} · CIS command`, vscode.ViewColumn.Active, {
    enableScripts: model.cancellable === true, retainContextWhenHidden: false, localResourceRoots: [],
  });
  const scriptNonce = nonce();
  const controller = {
    panel,
    update(next) { panel.webview.html = renderCommandProgressHtml(panel.webview, title, next, scriptNonce); },
  };
  controller.update(model);
  if (model.cancellable === true) panel.webview.onDidReceiveMessage(message => {
    if (isValidWebviewMessage(message, new Set(['cancel']))) onCancel();
  });
  return controller;
}

function renderCommandProgressHtml(webview, title, model, scriptNonce) {
  const state = model.state || 'running';
  const terminal = ['success', 'failure', 'cancelled'].includes(String(state).toLowerCase());
  const output = [model.stdout, model.stderr].filter(Boolean).join('\n').trim();
  const body = `<header class="hero"><div><span class="eyebrow">CIS command</span><h1>${escapeHtml(title)}</h1>
      <p>${escapeHtml(model.command || 'A bounded CIS process is running.')}</p></div><span class="badge ${stateClass(state)}">${escapeHtml(state)}</span></header>
    <section class="cards two"><article class="card"><span class="eyebrow">Elapsed time</span><h2>${escapeHtml(formatDuration(model.durationMs || 0))}</h2>
      <p>Exit code: <strong>${escapeHtml(model.exitCode === undefined || model.exitCode === null ? 'Pending' : model.exitCode)}</strong></p></article>
      <article class="card"><span class="eyebrow">Evidence state</span><h2>${escapeHtml(model.truncated ? 'Output truncated' : terminal ? 'Terminal evidence retained' : 'Process active')}</h2>
      <p>${escapeHtml(model.failureKind || 'No failure classification')}</p></article></section>
    <section class="card"><span class="eyebrow">Bounded output</span><h2>${output ? 'Process output' : 'No output yet'}</h2>
      <pre>${escapeHtml(output || 'CIS has not emitted bounded output.')}</pre></section>
    ${!terminal && model.cancellable ? '<footer class="actions"><button type="button" data-command="cancel">Cancel command</button></footer>' : ''}`;
  return studioDocument(webview, `${title} · ${state}`, body, scriptNonce);
}

function openContextPanel(vscode, title, result, onAction, onPath) {
  const panel = createActionPanel(vscode, 'cis.contextDetail', title,
    new Set(['related', 'open-path']), onAction, onPath);
  const scriptNonce = nonce();
  panel.webview.html = renderContextHtml(panel.webview, title, result, scriptNonce);
  return panel;
}

function renderContextHtml(webview, title, result, scriptNonce) {
  const nodes = Array.isArray(result?.nodes) ? result.nodes : [];
  const traversals = Array.isArray(result?.traversals) ? result.traversals : [];
  const diagnostics = Array.isArray(result?.diagnostics) ? result.diagnostics : [];
  const nodeCards = nodes.map(node => {
    const location = node.locations?.find(item => item.path)?.path || node.properties?.path;
    return `<article class="card"><div class="section-heading"><div><span class="eyebrow">${escapeHtml(`${node.kind || 'node'} · ${node.subtype || 'unclassified'}`)}</span>
        <h2>${escapeHtml(node.label || node.key || 'Graph node')}</h2></div><span class="badge ${stateClass(node.lifecycle || node.authority)}">${escapeHtml(node.lifecycle || node.authority || 'Unknown')}</span></div>
      <p>${escapeHtml(node.key || '')}</p><div class="actions">${location
        ? `<button class="secondary" type="button" data-command="open-path" data-value="${escapeHtml(location)}">Open evidence</button>` : ''}
        <button type="button" data-command="related" data-value="${escapeHtml(node.key || '')}">Explore relationships</button></div></article>`;
  }).join('');
  const diagnosticRows = diagnostics.map(item => `<li class="row static"><span><strong>${escapeHtml(item.code || item.severity || 'Diagnostic')}</strong><br>
      <span class="muted">${escapeHtml(item.message || '')}</span></span><span class="badge ${stateClass(item.severity)}">${escapeHtml(item.severity || 'info')}</span></li>`).join('');
  const traversalRows = traversals.map(item => `<li class="row static"><span>${escapeHtml(item.from || item.source || '')}</span><strong>${escapeHtml(item.type || item.relationship || 'related')}</strong><span>${escapeHtml(item.to || item.target || '')}</span></li>`).join('');
  const body = `<header class="hero"><div><span class="eyebrow">Bounded context graph</span><h1>${escapeHtml(title)}</h1>
      <p>${escapeHtml(result?.query?.text || result?.query?.id || 'Queryable canonical and derived evidence.')}</p></div><span class="badge ${stateClass(result?.freshness)}">${escapeHtml(result?.freshness || result?.status || 'Unknown')}</span></header>
    ${result?.truncated ? '<section class="notice warning"><strong>Results are truncated</strong><span>Refine the query or traverse a specific node to remain within the bounded result limit.</span></section>' : ''}
    <section><div class="section-heading"><div><span class="eyebrow">Matched evidence</span><h2>${nodes.length} nodes</h2></div></div>
      <div class="cards two">${nodeCards || '<article class="card empty">No graph nodes matched this bounded query.</article>'}</div></section>
    <section class="cards two"><article class="card"><span class="eyebrow">Relationships</span><h2>${traversals.length} traversals</h2><ul class="rows">${traversalRows || '<li class="empty">No traversals were reported.</li>'}</ul></article>
      <article class="card"><span class="eyebrow">Diagnostics</span><h2>${diagnostics.length} findings</h2><ul class="rows">${diagnosticRows || '<li class="empty">No graph diagnostics were reported.</li>'}</ul></article></section>`;
  return studioDocument(webview, title, body, scriptNonce);
}

function formatDuration(milliseconds) {
  const seconds = Math.max(0, Math.floor(Number(milliseconds || 0) / 1000));
  if (seconds < 60) return `${seconds}s`;
  return `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

function createActionPanel(vscode, kind, title, allowed, onAction, onPath) {
  const panel = vscode.window.createWebviewPanel(kind, title, vscode.ViewColumn.Active, {
    enableScripts: allowed.size > 0, retainContextWhenHidden: false, localResourceRoots: [],
  });
  if (allowed.size) panel.webview.onDidReceiveMessage(async message => {
    if (!isValidWebviewMessage(message, allowed)) return;
    if (message.command === 'open-path') await onPath(message.value);
    else await onAction(message.command, message.value);
  });
  return panel;
}

function studioDocument(webview, title, body, scriptNonce, customScript = '') {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';"><title>${escapeHtml(title)}</title>
  <style nonce="${scriptNonce}">:root{color-scheme:light dark}*{box-sizing:border-box}body{color:var(--vscode-foreground);background:var(--vscode-editor-background);font:var(--vscode-font-weight) var(--vscode-font-size)/1.5 var(--vscode-font-family);margin:0;padding:2rem;max-width:80rem}main{display:grid;gap:1rem}.hero,.section-heading{display:flex;justify-content:space-between;align-items:start;gap:1rem}.hero{border-bottom:1px solid var(--vscode-panel-border);padding-bottom:1rem}.hero h1,.card h2,.notice strong{margin:.15rem 0}.hero p{max-width:78ch}.eyebrow{display:block;color:var(--vscode-descriptionForeground);font-size:.78rem;font-weight:600;letter-spacing:.04em;text-transform:uppercase}.badge{display:inline-block;border:1px solid var(--vscode-panel-border);border-radius:999px;padding:.15rem .62rem;font-size:.82rem;white-space:nowrap}.badge.good{color:var(--vscode-testing-iconPassed);border-color:var(--vscode-testing-iconPassed)}.badge.bad{color:var(--vscode-testing-iconFailed);border-color:var(--vscode-testing-iconFailed)}.badge.warn{color:var(--vscode-editorWarning-foreground)}.notice{display:flex;flex-direction:column;gap:.2rem;border-left:3px solid var(--vscode-focusBorder);background:var(--vscode-textBlockQuote-background);padding:.8rem 1rem}.notice.warning{border-left-color:var(--vscode-editorWarning-foreground)}.cards{display:grid;gap:1rem}.cards.two{grid-template-columns:repeat(2,minmax(0,1fr))}.card{border:1px solid var(--vscode-panel-border);border-radius:.35rem;background:var(--vscode-editorWidget-background);padding:1rem;overflow-wrap:anywhere}.card.request{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1.25rem}.card.request>div{border-top:1px solid var(--vscode-panel-border);padding-top:.65rem}.rows,.links,.criteria,.timeline-list{list-style:none;margin:.7rem 0 0;padding:0}.row{display:flex;align-items:center;justify-content:space-between;gap:1rem;border-top:1px solid var(--vscode-panel-border);padding:.7rem 0}.row-main{display:flex;flex-direction:column;align-items:start;gap:.15rem;flex:1;color:var(--vscode-foreground);background:transparent;border:0;padding:0;text-align:left}.row-main:hover strong{color:var(--vscode-textLink-activeForeground)}.muted,.timeline span{color:var(--vscode-descriptionForeground)}.empty{color:var(--vscode-descriptionForeground);padding:.5rem 0}.links{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.55rem}.criteria{list-style:disc;padding-left:1.2rem}.timeline{border-left:2px solid var(--vscode-panel-border);padding:0 0 .75rem .8rem}.timeline span,.timeline strong{display:block}.timeline p{margin:.2rem 0}.card pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:30rem;overflow:auto;color:var(--vscode-textPreformat-foreground);background:var(--vscode-textCodeBlock-background);padding:.75rem}.actions{display:flex;flex-wrap:wrap;gap:.6rem;margin-top:.2rem}button{font:inherit;cursor:pointer}button:not(.row-main):not(.link){color:var(--vscode-button-foreground);background:var(--vscode-button-background);border:1px solid transparent;border-radius:2px;padding:.48rem .85rem}button.secondary{color:var(--vscode-button-secondaryForeground)!important;background:var(--vscode-button-secondaryBackground)!important}.link{color:var(--vscode-textLink-foreground);background:transparent;border:0;padding:0;text-align:left}.link:hover{color:var(--vscode-textLink-activeForeground);text-decoration:underline}button:focus-visible{outline:2px solid var(--vscode-focusBorder);outline-offset:2px}button:disabled{cursor:default;opacity:.55}.wizard-layout{display:grid;grid-template-columns:minmax(13rem,18rem) minmax(0,1fr);gap:1.25rem}.wizard-steps{display:flex;flex-direction:column;gap:.3rem}.wizard-steps .step{display:grid;grid-template-columns:1.7rem 1fr .65rem;align-items:center;gap:.6rem;color:var(--vscode-foreground);background:transparent;border-color:transparent;text-align:left}.wizard-steps .step.current{background:var(--vscode-list-activeSelectionBackground);color:var(--vscode-list-activeSelectionForeground)}.wizard-steps .step span:first-child{display:grid;place-items:center;border:1px solid currentColor;border-radius:50%;width:1.55rem;height:1.55rem}.wizard-steps small,.wizard-steps strong{display:block}.wizard-steps i{width:.55rem;height:.55rem;border:1px solid var(--vscode-panel-border);border-radius:50%}.wizard-steps i.complete{background:var(--vscode-testing-iconPassed);border-color:var(--vscode-testing-iconPassed)}.wizard-page{display:grid;align-content:start;gap:1rem;min-width:0}.wizard-footer{position:sticky;bottom:0;display:flex;justify-content:space-between;gap:1rem;background:var(--vscode-editor-background);border-top:1px solid var(--vscode-panel-border);padding:1rem 0}.footer-primary{display:flex;justify-content:flex-end;gap:.6rem;flex-wrap:wrap}.dictionary-grid,.wizard-questions,.review-summary{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.question-card{border:1px solid var(--vscode-panel-border);border-radius:.35rem;padding:1rem}.question-heading{display:flex;justify-content:space-between;gap:.75rem}.question-card label{display:block;margin:.7rem 0 .25rem;font-weight:600}.question-card textarea{width:100%;resize:vertical;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border);padding:.6rem;font:inherit}.ui-preview{margin:0;border:1px solid var(--vscode-panel-border);padding:.6rem}.ui-preview img{display:block;width:100%;height:auto}.ui-preview figcaption{color:var(--vscode-descriptionForeground);padding-top:.5rem}.review-page{display:flex;justify-content:space-between;align-items:center;color:var(--vscode-foreground);text-align:left}.review-page strong{color:var(--vscode-textLink-foreground)}details{padding:.4rem 0}summary{cursor:pointer}@media(max-width:48rem){body{padding:1rem}.hero,.section-heading,.question-heading{display:block}.hero>.badge,.section-heading>.badge,.question-heading>.badge{margin-top:.6rem}.cards.two,.card.request,.dictionary-grid,.wizard-questions,.review-summary{grid-template-columns:1fr}.links{grid-template-columns:1fr}.actions button{width:100%}.wizard-layout{grid-template-columns:1fr}.wizard-steps{display:grid;grid-template-columns:repeat(2,minmax(0,1fr))}.wizard-footer{position:static;display:block}.wizard-footer>button,.footer-primary button{width:100%;margin-top:.5rem}}@media(prefers-reduced-motion:reduce){*{scroll-behavior:auto!important;transition:none!important}}</style></head>
  <body><main>${body}</main>${customScript || `<script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();document.querySelectorAll('button[data-command]').forEach(button=>button.addEventListener('click',()=>vscode.postMessage({command:button.dataset.command,value:button.dataset.value||''})));</script>`}</body></html>`;
}

function stateClass(value) {
  const state = String(value || '').toLowerCase().replaceAll('-', '');
  if (['approved', 'active', 'complete', 'completed', 'passed', 'succeeded', 'healthy', 'current', 'ready'].includes(state)) return 'good';
  if (['failed', 'error', 'blocked', 'invalid', 'invalidevidence', 'timedout', 'rejected'].includes(state)) return 'bad';
  return 'warn';
}

function labelLabel(value) { return String(value || '').replace(/([a-z])([A-Z])/gu, '$1 $2').replace(/^./u, match => match.toUpperCase()); }

function renderHtml(webview, title, model, actions, scriptNonce, linkPaths = false) {
  const rows = flatten(model).map(([label, value]) => `<tr><th scope="row">${escapeHtml(label)}</th><td>${linkPaths && isOpenablePath(value)
    ? `<button class="link" type="button" data-command="open-path" data-value="${escapeHtml(value)}">${escapeHtml(value)}</button>`
    : `<code>${escapeHtml(value)}</code>`}</td></tr>`).join('');
  const buttons = actions.map(action => `<button type="button" data-command="${escapeHtml(action.command)}" data-value="${escapeHtml(action.value || '')}">${escapeHtml(action.label)}</button>`).join('');
  const script = actions.length || linkPaths ? `<script nonce="${scriptNonce}">
    const vscode = acquireVsCodeApi();
    document.querySelectorAll('button[data-command]').forEach(button => button.addEventListener('click', () => {
      vscode.postMessage({ command: button.dataset.command, value: button.dataset.value });
    }));
  </script>` : '';
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src 'nonce-${scriptNonce}'; script-src 'nonce-${scriptNonce}';">
<title>${escapeHtml(title)}</title>
<style nonce="${scriptNonce}">
  :root { color-scheme: light dark; } body { color: var(--vscode-foreground); background: var(--vscode-editor-background); font: var(--vscode-font-weight) var(--vscode-font-size)/1.5 var(--vscode-font-family); padding: 1.25rem; max-width: 78rem; }
  h1 { font-size: 1.4rem; } table { width: 100%; border-collapse: collapse; } th, td { text-align: left; vertical-align: top; padding: .5rem; border-bottom: 1px solid var(--vscode-panel-border); overflow-wrap: anywhere; } th { width: 13rem; }
  code { color: var(--vscode-textPreformat-foreground); white-space: pre-wrap; } .actions { display: flex; flex-wrap: wrap; gap: .5rem; margin: 1rem 0; } button { color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: 0; padding: .45rem .8rem; } button.link { color: var(--vscode-textLink-foreground); background: transparent; padding: 0; text-align: left; overflow-wrap: anywhere; } button:hover { background: var(--vscode-button-hoverBackground); } button.link:hover { color: var(--vscode-textLink-activeForeground); background: transparent; text-decoration: underline; } button:focus-visible { outline: 2px solid var(--vscode-focusBorder); outline-offset: 2px; }
  @media (max-width: 42rem) { body { padding: .75rem; } table, tbody, tr, th, td { display: block; } th { width: auto; padding-bottom: 0; } td { padding-top: .2rem; } }
  @media (prefers-reduced-motion: reduce) { * { scroll-behavior: auto !important; transition: none !important; } }
</style></head><body><main><h1>${escapeHtml(title)}</h1><div class="actions" aria-label="Available actions">${buttons}</div><table><tbody>${rows}</tbody></table></main>${script}</body></html>`;
}

function flatten(value, prefix = '', output = []) {
  if (value === null || value === undefined || typeof value !== 'object') {
    output.push([prefix || 'Value', String(value ?? '')]); return output;
  }
  if (Array.isArray(value)) {
    if (!value.length) output.push([prefix || 'Items', 'None']);
    else value.forEach((item, index) => flatten(item, `${prefix || 'Items'} ${index + 1}`, output));
    return output;
  }
  for (const [key, item] of Object.entries(value)) flatten(item, prefix ? `${prefix} / ${key}` : key, output);
  return output;
}

function isOpenablePath(value) {
  return typeof value === 'string' && !value.includes('::') && /^[^\0<>:"|?*]+[\\/][^\0<>:"|?*]+\.(?:md|cs|js|mjs|ts|tsx|json|ya?ml|tf)$/iu.test(value);
}

module.exports = {
  confirmAgentRequest, flatten, isOpenablePath, openBrdQuestionsPanel, openTechnicalIntentQuestionsPanel, openUiDirectionQuestionsPanel, openDefinitionWizardPanel, openDoctorPanel, openChangeOverviewPanel, openDesignPanel, openEvidencePanel,
  openCommandProgressPanel, openContextPanel, openRecommendationReviewPanel, openRunDetailPanel, openTaskDetailPanel,
  renderAgentRequestHtml, renderChangeOverviewHtml, renderCommandProgressHtml, renderContextHtml,
  renderBrdQuestionsHtml, renderTechnicalIntentQuestionsHtml, renderUiDirectionQuestionsHtml, renderDefinitionWizardHtml, renderDoctorHtml, renderHtml, renderRecommendationReviewHtml, renderRunDetailHtml, renderTaskDetailHtml,
};
