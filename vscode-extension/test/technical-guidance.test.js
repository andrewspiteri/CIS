'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const vm = require('node:vm');
const { renderDefinitionWizardHtml } = require('../lib/webview');

function model() {
  return { businessInference: { repositories: [{ id: 'api' }] },
    technicalQuestions: { complete: true, current: true, questions: [
      { id: 'TI-Q-001', question: 'Which framework?', status: 'Derived', answer: 'Existing framework' },
      { id: 'TI-Q-002', question: 'Which hosting?', status: 'Answered', answer: 'Existing platform' },
    ] }, pages: [{ id: 'technical', ordinal: 3, title: 'Technical direction', status: 'Review Required', complete: false, current: true,
      primaryPath: 'docs/technical.md', artifactPaths: ['docs/technical.md'], issues: ['Technical decision remains unresolved: TI-DEC-001'],
      guidance: { summary: 'Questionnaire complete; 1 technical decision still needs review.', nextActionId: 'decisions', nextStep: 'Review the linked decision and rationale.',
        reasons: ['A technical document decision is open.'], actions: [
          { id: 'decisions', label: 'Review 1 technical decision', status: 'Needed' },
          { id: 'questions', label: 'Review questionnaire answers', status: 'Complete' },
          { id: 'infer-technical', label: 'Infer from existing repositories', status: 'Optional' },
          { id: 'prepare-technical', label: 'Prepare or refresh technical evidence', status: 'Optional' },
        ], technicalDecisions: [{ id: 'TI-DEC-001', decision: 'Confirm recovery ownership', status: 'Open', requiredBefore: 'Change dossier creation', rationale: 'Needs operator review.', needsReview: true, documentLine: 42, issues: [] }],
      } }, { id: 'architecture', ordinal: 4, title: 'Solution architecture' }] };
}
const render = value => renderDefinitionWizardHtml({ cspSource: 'test:' }, '/authority', value, 'technical', 'nonce', {});

test('completed answers keep open document decisions and their next step visible', () => {
  const html = render(model());
  assert.match(html, /Questionnaire complete; 1 technical decision still needs review/u);
  assert.match(html, /2 of 2 answers resolved/u);
  assert.match(html, /Document decisions — 1 need attention out of 1/u);
  assert.match(html, /Confirm recovery ownership/u);
  assert.match(html, /href="file:[^"]*technical.md#L42" target="_blank"/u);
  assert.match(html, /data-command="open-technical-decision" data-value="TI-DEC-001"/u);
  assert.match(html, /Optional<\/span><button[^>]*data-command="technical-action" data-value="infer-technical"/u);
  assert.match(html, /<details id="technical-questionnaire" ><summary/u);
  assert.ok(html.indexOf('Document decisions') < html.indexOf('wizard-TI-Q-001'));
  assert.doesNotMatch(html, /data-command="prepare"|Save and continue|data-value="architecture">Continue/u);
});

test('a saved decision is visibly resolved while other decisions remain reviewable', () => {
  const value = model();
  const decisions = value.pages[0].guidance.technicalDecisions;
  decisions.push({ ...decisions[0], id: 'TI-DEC-002', decision: 'Confirm a different policy' });
  decisions[0] = { ...decisions[0], status: 'Resolved', needsReview: false, recordedResolution: 'The operations team owns recovery.' };
  value.technicalDecisionSave = { id: 'TI-DEC-001', refreshed: true };
  const html = render(value);
  assert.match(html, /TI-DEC-001 saved and resolved\. 1 other decision still needs review/u);
  assert.match(html, /Document decisions — 1 need attention out of 2/u);
  assert.match(html, /<details open><summary>Recorded decisions \(1\)/u);
  assert.match(html, /class="badge good">Resolved/u);
  assert.match(html, /Confirm a different policy/u);
});

test('a failed refresh reports the successful save without claiming unverified readiness', () => {
  const value = model(); value.technicalDecisionSave = { id: 'TI-DEC-001', refreshed: false, error: '<interrupted>' };
  const html = render(value);
  assert.match(html, /TI-DEC-001 was saved, but readiness could not refresh/u);
  assert.match(html, /&lt;interrupted&gt;/u);
  assert.doesNotMatch(html, /saved and resolved/u);
});

test('technical review navigation expands its section without starting another CLI process', () => {
  for (const destination of ['questions', 'decisions']) {
    let click, selected; let focused = false; const sent = [];
    const section = { open: false, scrollIntoView() {}, focus() { focused = true; }, querySelector: () => ({ focus() { focused = true; } }) };
    const script = /<script nonce="nonce">([\s\S]*?)<\/script>/u.exec(render(model()))[1];
    vm.runInNewContext(script, { acquireVsCodeApi: () => ({ postMessage: message => { if (message.command !== 'wizard-ready') sent.push(message); } }), window: { addEventListener() {} },
      document: { body: { setAttribute() {} }, querySelectorAll: () => [], getElementById: id => { selected = id; return section; }, addEventListener: (_event, handler) => { click = handler; } } });
    click({ target: { closest: () => ({ dataset: { command: 'technical-action', value: destination } }) } });
    assert.equal(selected, destination === 'questions' ? 'technical-questionnaire' : 'technical-decisions');
    assert.equal(section.open, true); assert.equal(focused, true); assert.equal(sent.length, 0);
  }
});

test('stale answers stay visible, ready technical direction can continue, and document text is escaped', () => {
  const value = model(); value.technicalQuestions.current = false;
  value.pages[0].guidance.technicalDecisions[0].decision = '<script>bad</script>';
  let html = render(value);
  assert.match(html, /Needs refresh/u); assert.match(html, /<details id="technical-questionnaire" open>/u);
  assert.match(html, /&lt;script&gt;bad&lt;\/script&gt;/u); assert.doesNotMatch(html, /<script>bad/u);
  value.pages[0].complete = true; value.technicalQuestions.current = true;
  value.pages[0].guidance = { summary: 'Technical direction is complete and current.', nextActionId: 'continue', actions: [] };
  html = render(value); assert.match(html, /data-value="architecture">Continue/u);
});
