'use strict';
const test = require('node:test'), assert = require('node:assert/strict');
const { architectureApproval, renderDocumentApproval, renderArchitectureApproval } = require('../lib/architecture-approval');
const { renderDefinitionWizardHtml } = require('../lib/webview');
const model = () => ({ pages: ['business', 'technical', 'architecture'].map(id => ({ id, status: 'Ready for Approval', complete: true, current: true, primaryPath: `docs/${id}.md`, artifactPaths: ['docs/architecture.md', 'docs/references/component-sheet.md'], issues: [] })), diagrams: [{ status: 'Review Required' }] });

test('document approval stays on its own step with prerequisites enforced in order', () => {
  const value = model();
  assert.equal(architectureApproval(value).canApproveBusiness, true);
  assert.equal(architectureApproval(value).canApproveTechnical, false);
  assert.equal(architectureApproval(value).canApproveArchitecture, false);
  const businessHtml = renderDocumentApproval(value, 'business');
  assert.match(businessHtml, /data-command="business-action" data-value="approve-business" >Approve business requirements/u);
  assert.doesNotMatch(businessHtml, /data-value="approve-(?:technical|architecture)"/u);
  const technicalHtml = renderDocumentApproval(value, 'technical');
  assert.match(technicalHtml, /data-command="technical-action" data-value="approve-technical" disabled/u);
  assert.match(technicalHtml, /data-command="navigate" data-value="business">Go to Business definition/u);
  assert.doesNotMatch(technicalHtml, /data-value="approve-(?:business|architecture)"/u);
  const blockedArchitecture = renderArchitectureApproval(value);
  assert.doesNotMatch(blockedArchitecture, /data-value="approve-(?:business|technical)"/u);
  assert.match(blockedArchitecture, /data-command="navigate" data-value="business">Go to Business definition/u);
  assert.match(blockedArchitecture, /data-command="navigate" data-value="technical">Go to Technical direction/u);
  value.pages[0].status = 'Active';
  assert.equal(architectureApproval(value).canApproveTechnical, true);
  assert.match(renderDocumentApproval(value, 'business'), /disabled>Business requirements approved/u);
  assert.match(renderDocumentApproval(value, 'technical'), /data-value="approve-technical" >Approve technical intent/u);
  value.pages[1].status = 'Active';
  assert.equal(architectureApproval(value).canApproveArchitecture, true);
  assert.match(renderDocumentApproval(value, 'technical'), /disabled>Technical intent approved/u);
  const html = renderArchitectureApproval(value);
  assert.match(html, /data-value="approve-architecture" >Approve architecture and diagrams/u);
  assert.match(html, /Review overall design/u); assert.match(html, /Review component sheet/u);
  assert.doesNotMatch(html, /Go to Business definition|Go to Technical direction/u);
});

test('stale or incomplete architecture and diagrams block approval and show the cause', () => {
  const value = model(); value.pages[0].status = value.pages[1].status = 'Active';
  value.pages[0].current = false;
  value.pages[0].issues = ['BRD evidence changed <review>.'];
  assert.match(renderDocumentApproval(value, 'business'), /BRD evidence changed &lt;review&gt;/u);
  assert.match(renderDocumentApproval(value, 'business'), /data-value="approve-business" disabled/u);
  assert.match(renderDocumentApproval(value, 'technical'), /Go to Business definition/u);
  value.pages[0].current = true;
  value.definitionApprovalNotice = { page: 'business', message: 'Check <business>.' };
  assert.match(renderDocumentApproval(value, 'business'), /Check &lt;business&gt;/u);
  assert.doesNotMatch(renderDocumentApproval(value, 'technical'), /Check &lt;business&gt;/u);
  assert.doesNotMatch(renderArchitectureApproval(value), /Check &lt;business&gt;/u);
  value.pages[2].current = false; value.pages[2].issues = ['Technical-intent baseline changed <review>.'];
  assert.equal(architectureApproval(value).canApproveArchitecture, false);
  assert.match(renderArchitectureApproval(value), /Technical-intent baseline changed &lt;review&gt;/u);
  value.pages[2].current = true; value.diagrams[0].status = 'Stale';
  assert.equal(architectureApproval(value).canApproveArchitecture, false);
  value.diagrams = []; assert.equal(architectureApproval(value).canApproveArchitecture, false);
});

test('approved architecture is shown only when the diagram set is also Active', () => {
  const value = model(); value.pages.forEach(page => { page.status = 'Active'; });
  assert.equal(architectureApproval(value).approved, false);
  value.diagrams[0].status = 'Active';
  assert.equal(architectureApproval(value).approved, true);
  assert.match(renderArchitectureApproval(value), /disabled>Architecture and diagrams approved/u);
});

test('stale inferred architecture directs the user to reconciliation instead of repeating preparation', () => {
  const value = model();
  value.pages.forEach((page, index) => { page.ordinal = index + 1; page.title = page.id; });
  const page = value.pages[2]; page.current = false; page.complete = false;
  page.guidance = { summary: 'Reconcile changed technical direction.', nextActionId: 'reconcile-architecture',
    nextStep: 'Update the draft, then review its diagrams.', reasons: ['Prepare preserves the existing narrative.'],
    actions: [{ id: 'reconcile-architecture', label: 'Reconcile architecture with technical direction', status: 'Needed' }] };
  value.diagrams = []; value.architecturePreparationNotice = 'Preparation blocked <reconcile>.';
  const html = renderDefinitionWizardHtml({ cspSource: "'self'" }, 'C:/fixture', value, 'architecture', 'nonce', {});
  assert.match(html, /data-command="architecture-action" data-value="reconcile-architecture"/u);
  assert.doesNotMatch(html, /data-command="prepare" data-value="architecture"/u);
  assert.match(html, /Preparation blocked &lt;reconcile&gt;/u);
  assert.match(html, /data-value="approve-architecture" disabled/u);
});
