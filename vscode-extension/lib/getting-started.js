'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { repositoryMetadata } = require('./views');
const { escapeHtml, nonce } = require('./security');
const { createActionPanel, studioDocument } = require('./webview');

const ACTIONS = Object.freeze({
  guide: 'cis.openGuide',
  'open-folder': 'vscode.openFolder',
  select: 'cis.selectAuthority',
  initialize: 'cis.authorityInit',
  import: 'cis.repoImport',
  wizard: 'cis.definitionWizard',
  journey: 'cis.journey.focus',
  feature: 'cis.featureWizard',
  doctor: 'cis.repoDoctor',
  trust: 'workbench.trust.manage',
});

// This landing page reads setup metadata only. Lifecycle readiness stays with the CLI.
function gettingStartedModel(vscode, authority) {
  const root = authority.root();
  const hasFolders = Boolean(vscode.workspace.workspaceFolders?.length);
  const trusted = vscode.workspace.isTrusted !== false;
  const model = { root, hasFolders, trusted, configured: false,
    documentationRoot: vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis') };
  if (root) {
    try {
      const metadata = repositoryMetadata(root, model.documentationRoot);
      model.documentationRoot = metadata.documentationRoot;
      model.product = metadata.product?.name;
      model.ecosystem = metadata.ecosystem?.name;
      model.configured = Boolean(metadata.initialized && metadata.product && metadata.ecosystem);
      if (fs.existsSync(path.join(root, '.cis', 'workspace.yml')) && !model.configured)
        model.problem = 'The existing authority configuration needs attention. Open Repository Doctor to inspect it.';
    } catch (error) { model.problem = error.message; }
  }
  model.canInitialize = Boolean(root && trusted && !model.configured && !model.problem);
  model.canContinue = Boolean(model.configured && trusted);
  model.next = !hasFolders ? { action: 'open-folder', label: 'Open authority folder', detail: 'Open the folder that will hold your product documents and decisions.' }
    : !root ? { action: 'select', label: 'Select authority folder', detail: 'Choose which open folder holds the product authority.' }
      : !trusted ? { action: 'trust', label: 'Manage workspace trust', detail: 'CIS needs workspace trust before it can initialize files or run the wizard.' }
        : model.problem ? { action: 'doctor', label: 'Inspect authority setup', detail: model.problem }
          : !model.configured ? { action: 'initialize', label: 'Initialize authority', detail: 'Set the product, ecosystem, and location of your CIS documents.' }
            : { action: 'wizard', label: 'Open high-level wizard', detail: 'For an existing system, import its application repositories first. Then define or review the product in the wizard.' };
  return model;
}

function openGettingStartedPanel(vscode, model, onAction) {
  const panel = createActionPanel(vscode, 'cis.gettingStarted', 'Getting Started with CIS',
    new Set([...Object.keys(ACTIONS), 'refresh']), onAction);
  const scriptNonce = nonce();
  const controller = { panel, update(next) { panel.webview.html = renderGettingStartedHtml(panel.webview, next, scriptNonce); } };
  controller.update(model);
  return controller;
}

function renderGettingStartedHtml(webview, model, scriptNonce) {
  const button = (action, label, enabled = true, secondary = true) =>
    `<button type="button" ${secondary ? 'class="secondary"' : ''} data-command="${action}" ${enabled ? '' : 'disabled'}>${escapeHtml(label)}</button>`;
  const status = model.problem ? 'Setup needs attention' : model.configured ? 'Authority configured' : 'Setup to complete';
  const body = `<header class="hero"><div><span class="eyebrow">Change Impact Studio</span><h1>Getting started with CIS</h1><p>Turn an idea or an existing system into a defined product, then deliver one reviewed feature at a time.</p><button type="button" class="link" data-command="guide">How to use CIS →</button></div><span class="badge ${model.configured ? 'good' : 'warn'}">${status}</span></header>
    <section class="card"><span class="eyebrow">Your next step</span><h2>${escapeHtml(model.next.label)}</h2><p>${escapeHtml(model.next.detail)}</p><div class="actions">${button(model.next.action, model.next.label, true, false)}${button('refresh', 'Refresh setup')}</div></section>
    <section class="card"><span class="eyebrow">Current authority</span><h3>${escapeHtml(model.product || 'Your product')}</h3><p>${escapeHtml(model.root || 'No authority folder selected')}</p>${model.ecosystem ? `<p class="muted">Ecosystem: ${escapeHtml(model.ecosystem)} · Documents: ${escapeHtml(model.documentationRoot)}</p>` : ''}</section>
    <section class="cards two" aria-label="CIS setup steps">
      <article class="card"><span class="eyebrow">Step 1 · ${model.root ? 'Selected' : 'Start here'}</span><h2>Choose the authority folder</h2><p>The authority is the home for one product’s requirements, decisions, designs, and delivery records. It can be a dedicated docs repository beside your application repositories.</p><div class="actions">${button(model.hasFolders ? 'select' : 'open-folder', model.hasFolders ? 'Select authority folder' : 'Open authority folder')}</div></article>
      <article class="card"><span class="eyebrow">Step 2 · ${model.configured ? 'Configured' : 'Set up your product'}</span><h2>Initialize the authority</h2><p>Name your product and its ecosystem, choose where documents belong, and review the initialization plan. An ecosystem groups related products; this authority governs one product.</p><div class="actions">${button('initialize', model.configured ? 'Authority initialized' : 'Initialize authority', model.canInitialize)}</div></article>
      <article class="card"><span class="eyebrow">Step 3 · For existing systems</span><h2>Connect your repositories</h2><p>Import the application repositories owned by this product so CIS can discover the existing system. External dependencies can be added with their relationship. For a new product, you can add code repositories later.</p><div class="actions">${button('import', 'Import repositories', model.canContinue)}</div></article>
      <article class="card"><span class="eyebrow">Step 4 · Define the product</span><h2>Run the high-level wizard</h2><p>Work through business requirements, technical direction, architecture, contracts, experience, and delivery. Review the complete baseline before approving it. You can return to any page.</p><div class="actions">${button('wizard', 'Open high-level wizard', model.canContinue)}</div></article>
    </section>
    <section class="card"><h2>What comes next?</h2><p>After the product baseline is approved, add a feature from a prepared BRD. Create or select its implementation repository and choose the existing repositories it integrates with. Review the feature scope before planning and implementation.</p><div class="actions">${button('feature', 'Add feature from BRD', model.canContinue)}${button('journey', 'Open Journey Map', model.canContinue)}${button('doctor', 'Check repository health', Boolean(model.root && model.trusted))}</div></section>
    <p class="muted">The guide is available offline. If a CIS command cannot start, check the executable path in VS Code Settings. Large systems can take a few minutes to load the wizard.</p>`;
  return studioDocument(webview, 'Getting Started with CIS', body, scriptNonce);
}

module.exports = { ACTIONS, gettingStartedModel, openGettingStartedPanel, renderGettingStartedHtml };
