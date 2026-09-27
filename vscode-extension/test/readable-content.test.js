'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { failureMessage, sourceOverviewNote } = require('../lib/content-messages');
const { CisCli } = require('../lib/cis-cli');
const { renderSourceReviewForm } = require('../lib/business-source-form');
const { renderTechnicalDecisionForm } = require('../lib/technical-decision-form');
const { renderReviewPage } = require('../lib/feature-wizard');
const { renderDocumentApproval } = require('../lib/architecture-approval');
const { gallery } = require('../lib/feature-screens');
const { architectureGallery } = require('../lib/feature-architecture');
const { escapeHtml } = require('../lib/security');
const { experienceState } = require('../lib/experience-guidance');
const { renderDefinitionWizardHtml } = require('../lib/webview');

test('content review examples retain conditional judgments; parsing is not a semantic-equivalence test', () => {
  const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, '../../docs/references/human-readable-content-fixtures.json'), 'utf8'));
  const cases = new Map(fixture.cases.map(item => [item.id, item]));
  assert.equal(cases.size, fixture.cases.length);
  assert.equal(cases.get('HC-FIX-04').expected, 'context-dependent');
  for (const id of ['HC-FIX-05', 'HC-FIX-09']) assert.equal(cases.get(id).expected, 'accept-subject-to-domain-check');
  for (const item of cases.values()) assert.ok(item.source && item.candidate && item.reason);
});

test('routine failure guidance uses checked transport state and keeps unknown causes and secrets out', () => {
  assert.match(failureMessage({ kind: 'timeout', message: 'request interrupted' }), /changes may already have been made/u);
  assert.match(failureMessage({ kind: 'stale-evidence', message: 'source changed' }), /refreshing does not approve/u);
  const unknown = failureMessage({ kind: 'future-code', message: 'token=fixturesecret123' });
  assert.match(unknown, /could not explain.*future-code/u);
  assert.match(unknown, /REDACTED/u);
  assert.doesNotMatch(unknown, /fixturesecret123|network is down|saved|approved/u);
  assert.match(failureMessage({ kind: 'invalid-evidence', message: 'no result' }), /Readiness is unverified/u);
});

test('workspace permission denial retains the execution gate and offers trust review without running a process', () => {
  let calls = 0;
  const cli = new CisCli({ workspace: { isTrusted: false } }, {}, { root: () => '/fixture' }, { execFile() { calls++; } });
  assert.throws(() => cli.query(['definition', 'status']), error => {
    assert.equal(error.kind, 'untrusted-workspace');
    assert.match(failureMessage(error), /commands are disabled.*Review Workspace Trust/u);
    return true;
  });
  assert.equal(calls, 0);
});

test('technical save without review evidence explains its disabled action and preserves the approval boundary', () => {
  const html = renderTechnicalDecisionForm({ id: 'TI-DEC-001', needsReview: true, suggestedResolution: '<draft>' }, escapeHtml);
  assert.match(html, /data-save-technical-decision disabled>Save answer/u);
  assert.match(html, /current review evidence is missing/u);
  assert.match(html, /Approval of technical intent remains a separate action/u);
  assert.match(html, /&lt;draft&gt;/u);
});

test('preview readiness and document approval are separate state-to-action projections', () => {
  const model = { pages: [{ id: 'experience', complete: true, current: true, status: 'Ready for Approval' }] };
  const state = experienceState(model, '/fixture');
  assert.equal(state.action.command, 'navigate'); assert.equal(state.action.value, 'delivery');
  assert.match(state.next, /not approved/u);
  const html = renderDocumentApproval({ pages: [{ id: 'technical', complete: true, current: true, status: 'Ready for Approval', issues: [] }] }, 'technical');
  assert.match(html, /data-command="technical-action" data-value="approve-technical" disabled/u);
  assert.match(html, /Approve the business requirements.*first/u);
  assert.doesNotMatch(html, /<h3[^>]*>Technical intent approved/u);
});

test('an empty findings list never claims an unsaved feature page is reviewed', () => {
  const page = { id: 'technical', title: 'Technical direction', status: 'Needs attention', complete: false, fields: [], documents: [], attention: [] };
  const html = renderReviewPage({ page: 'technical', wizard: { pages: [page], baselineCurrent: true } });
  assert.match(html, /empty findings list does not mean this step is complete/u);
  assert.doesNotMatch(html, /This page is reviewed against/u);
  assert.match(html, /form="feature-review-form"[^>]*>Save and continue/u);
  const review = { ...page, id: 'review', title: 'Review' };
  const final = renderReviewPage({ page: 'review', wizard: { pages: [page, review], baselineCurrent: false } });
  assert.match(final, /form="feature-review-form" disabled>Record definition review/u);
  assert.match(final, /product baseline is not current/u);
  assert.match(final, /does not authorize implementation/u);
});

test('overview limits, source action identity, stale review findings and local-only fallback stay visible', () => {
  const html = renderSourceReviewForm([{ id: 'BRD-SRC-001', path: 'docs/source.md', repositoryPath: '/fixture', repositoryId: 'fixture', needsReview: true,
    requiresReconciliation: true, issues: ['Source changed; review it again.'], summary: { kind: 'local-model', text: '<proposed overview>', inputTruncated: true, canGenerate: true } }], escapeHtml);
  assert.match(html, /data-command="open-business-source" data-value="BRD-SRC-001"/u);
  assert.match(html, /Selective overview.*Review the full source/u);
  assert.match(html, /supporting quotations do not verify every claim/u);
  assert.match(html, /selected excerpts from a longer document/u);
  assert.match(html, /No remote fallback/u);
  assert.match(html, /<section class="notice warning"><strong>Source review findings/u);
  assert.match(html, /<details id="business-source-decisions" open><summary>/u);
  assert.match(html, /&lt;proposed overview&gt;/u);
  assert.match(sourceOverviewNote({ kind: 'future-kind' }), /not recognized/u);
});

test('failed draft generation remains visible without a model and does not masquerade as no UI scope', () => {
  for (const render of [gallery, architectureGallery]) {
    const html = render({ status: 'invalid', errors: ['No local model is available.'], warnings: ['Selected evidence only.'] });
    assert.match(html, /Generation did not complete/u);
    assert.match(html, /No local model is available/u);
    assert.match(html, /<details open><summary>/u);
    assert.doesNotMatch(html, /No UI changes were proposed/u);
  }
});

test('partial save gives refresh priority and disabled technical actions never claim availability', () => {
  const html = renderDefinitionWizardHtml({ cspSource: 'fixture' }, '/fixture', {
    pages: [{ id: 'technical', ordinal: 3, title: 'Technical direction', status: 'Needs review', complete: false, current: false,
      issues: [], artifactPaths: [], guidance: { summary: 'Old next step', nextStep: 'Save the choice again', actions: [] } }],
    technicalDecisionSave: { id: 'TI-DEC-001', refreshed: false, error: 'Readiness timed out.' },
  }, 'technical', 'fixture', {});
  assert.match(html, /Answer saved · readiness unverified/u);
  assert.match(html, /data-command="refresh">Refresh/u);
  assert.doesNotMatch(html, /Old next step|Save the choice again/u);
  assert.match(html, /Unavailable<\/span><button[^>]*data-value="open-intent" disabled/u);
  assert.match(html, /document location is unavailable/u);
  assert.match(html, /product-owned repositories have been imported and discovered/u);
  assert.match(html, /\.wizard-footer\{position:static;/u);
});
