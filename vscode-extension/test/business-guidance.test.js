'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const vm = require('node:vm');
const { renderDefinitionWizardHtml } = require('../lib/webview');

function model() {
  return { businessInference: { canDraft: true, repositories: [{ id: 'api', repositoryPath: 'repo' }], lastPreparation: [] },
    pages: [{ id: 'business', title: 'Business definition', ordinal: 2, status: 'Review Required', complete: false, current: false,
      artifactPaths: ['docs/brd.md'], issues: ['BRD source assessment requires rationale: source-1'], guidance: {
        summary: 'The BRD exists, but its review is not complete.', nextActionId: 'refresh-evidence',
        nextStep: 'Refresh the BRD evidence, then review the source decisions.',
        reasons: ['19 source documents need a review decision.', 'Repository evidence is out of date.'],
        actions: [
          { id: 'refresh-evidence', label: 'Refresh BRD evidence', status: 'Needed', reason: 'Reconcile changed evidence.' },
          { id: 'infer-brd', label: 'Infer from existing project', status: 'Optional', reason: 'The BRD already exists; regeneration is optional.' },
          { id: 'prepare-evidence', label: 'Prepare existing-system context', status: 'Complete', reason: 'Preparation is recorded.' },
          { id: 'questions', label: 'Review business questions', status: 'Complete', reason: 'No unanswered questions were found.' },
        ],
        sourceReviews: [{ id: 'source-1', repositoryId: 'api', repositoryPath: 'repo', path: 'docs/Deposit policy.md',
          assessment: 'Unreviewed', rationale: 'TODO', needsReview: true, assessmentLine: 42, reviewToken: 'current',
          issues: ['Choose Adopted, Reference, or Rejected.', 'Explain the choice.'] }],
      } }, { id: 'technical', title: 'Technical direction', ordinal: 3 }] };
}

const render = value => renderDefinitionWizardHtml({ cspSource: 'test:' }, '/authority', value, 'business', 'nonce', {});

test('business readiness is above tools, explains blockers, and identifies optional and completed actions', () => {
  const html = render(model());
  assert.ok(html.indexOf('19 source documents need a review decision.') < html.indexOf('Dictionaries and implementation evidence'));
  assert.match(html, /Next step/u);
  assert.match(html, /Next · Needed/u);
  assert.match(html, /Optional<\/span><button[^>]+data-value="infer-brd"[^>]*>Infer from existing project/u);
  assert.match(html, /Complete<\/span><button[^>]+data-value="prepare-evidence"[^>]*>Prepare existing-system context/u);
  assert.doesNotMatch(html, /data-value="(?:infer-brd|prepare-evidence)" disabled/u);
  assert.match(html, /<details><summary>Full validation details/u);
  assert.doesNotMatch(html, /Answer 0 open questions/u);
});

test('source review offers direct choices and a reason without selecting an unanswered decision', () => {
  const html = render(model());
  assert.match(html, /Deposit policy\.md/u);
  assert.match(html, /Source decisions — 1 need review out of 1/u);
  assert.match(html, /Use its requirements/u);
  assert.match(html, /Keep as background/u);
  assert.match(html, /Exclude from this BRD/u);
  assert.match(html, /data-command="open-business-source" data-value="source-1"/u);
  assert.match(html, /<a class="source-document-link" href="file:[^"]*Deposit%20policy.md" target="_blank"/u);
  assert.doesNotMatch(html, /Open source document/u);
  assert.match(html, /data-source-reason/u);
  assert.match(html, /data-save-sources/u);
  assert.doesNotMatch(html, /value="(?:Adopted|Reference|Rejected)" checked/u);
  assert.doesNotMatch(html, /data-command="(?:approve|assess|accept-all)"/u);
});

test('document summaries are readable and escaped, with generation separate from normal status', () => {
  const value = model();
  value.pages[0].guidance.sourceReviews[0].summary = { kind: 'local-model', text: 'Describes deposit maturity instructions. <script>untrusted</script>', canGenerate: false };
  let html = render(value);
  assert.match(html, /Local model summary/u);
  assert.match(html, /Describes deposit maturity instructions\. &lt;script&gt;/u);
  assert.doesNotMatch(html, /data-command="summarize-business-sources"/u);
  value.pages[0].guidance.sourceReviews[0].summary = { kind: 'excerpt', text: 'Customers select maturity instructions.', canGenerate: true };
  html = render(value);
  assert.match(html, /Document excerpt/u);
  assert.match(html, /Summarize 1 document locally/u);
});

test('document link clicks dispatch a checked source ID instead of navigating the webview', () => {
  let click; let prevented = false; const sent = [];
  const script = /<script nonce="nonce">([\s\S]*?)<\/script>/u.exec(render(model()))[1];
  vm.runInNewContext(script, { acquireVsCodeApi: () => ({ postMessage: message => { if (message.command !== 'wizard-ready') sent.push(message); } }),
    window: { addEventListener() {} }, document: { body: { setAttribute() {} },
      querySelectorAll: () => [], getElementById: () => ({ hidden: true }), addEventListener: (_event, handler) => { click = handler; } } });
  click({ preventDefault() { prevented = true; }, target: { closest: () => ({ tagName: 'A', dataset: { command: 'open-business-source', value: 'source-1' } }) } });
  assert.equal(prevented, true); assert.equal(sent[0].command, 'open-business-source'); assert.equal(sent[0].value, 'source-1');
});

test('source decision action expands and focuses its section without starting a CLI command', () => {
  let click; let focused = false; let scrolled = false;
  const sent = []; const section = { open: false, scrollIntoView() { scrolled = true; }, querySelector: () => ({ focus() { focused = true; } }) };
  const html = render(model());
  const script = /<script nonce="nonce">([\s\S]*?)<\/script>/u.exec(html)[1];
  vm.runInNewContext(script, { acquireVsCodeApi: () => ({ postMessage: message => { if (message.command !== 'wizard-ready') sent.push(message); } }),
    window: { addEventListener() {} }, document: {
      body: { setAttribute() {} }, querySelectorAll: () => [], getElementById: () => section,
      addEventListener: (_event, handler) => { click = handler; },
    } });
  click({ target: { closest: () => ({ disabled: false, dataset: { command: 'business-action', value: 'source-decisions' } }) } });
  assert.equal(section.open, true); assert.equal(focused, true); assert.equal(scrolled, true); assert.equal(sent.length, 0);
});

test('pending sources come first and repository evidence has no nonexistent document link', () => {
  const value = model();
  value.pages[0].guidance.sourceReviews.unshift({ id: 'repo-evidence', path: 'workspace:api', repositoryId: 'docs',
    needsReview: false, assessment: 'Reference', rationale: 'Implementation context', assessmentLine: 45 });
  const html = render(value);
  assert.ok(html.indexOf('Deposit policy.md') < html.indexOf('Repository implementation evidence'));
  assert.doesNotMatch(html, /data-command="open-business-source" data-value="repo-evidence"/u);
  assert.match(html, /data-source-id="repo-evidence"/u);
});

test('untrusted strings are escaped and a ready page retains Continue', () => {
  const value = model();
  value.pages[0].guidance.reasons = ['<script>bad</script>'];
  value.pages[0].guidance.sourceReviews[0].path = '<img src=x onerror=bad>.md';
  value.pages[0].guidance.nextActionId = 'continue';
  value.pages[0].complete = true; value.pages[0].current = true;
  const html = render(value);
  assert.match(html, /&lt;script&gt;bad&lt;\/script&gt;/u);
  assert.doesNotMatch(html, /<img src=x/u);
  assert.match(html, /data-value="technical">Continue<\/button>/u);
});
