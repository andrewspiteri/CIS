'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { renderDefinitionWizardHtml, renderTechnicalIntentQuestionsHtml } = require('../lib/webview');

function sourceQuestions() {
  return [
    { id: 'TI-Q-003', area: 'Backend', question: 'Which backend?', status: 'Derived',
      resolutionSource: 'accepted-document', answer: 'Use .NET.', evidence: ['docs/accepted.md:10 — Backend (sha256:abc)'] },
    { id: 'TI-Q-004', area: 'Architecture', question: 'Which architecture?', status: 'Derived',
      resolutionSource: 'selected-document', answer: 'Use a modular monolith.', evidence: ['docs/supplied.md:12 — Architecture (sha256:def)'] },
    { id: 'TI-Q-015', area: 'AI', question: 'Which model hosting?', status: 'Not requested',
      resolutionSource: 'scope-not-requested', answer: null,
      suggestedAnswer: 'No model capability is requested. No exclusion or choice is recorded.', evidence: ['docs/accepted.md:1 — Scope (sha256:abc)'] },
  ];
}

function renderWizard(questions) {
  return renderDefinitionWizardHtml({ cspSource: 'test:' }, '/authority', {
    technicalQuestions: { current: true, complete: true, questions },
    pages: [{ id: 'technical', ordinal: 3, title: 'Technical direction', complete: true, current: true,
      primaryPath: 'docs/supplied.md', artifactPaths: [], issues: [], guidance: { actions: [] } }],
  }, 'technical', 'nonce', {});
}

test('retained technical answers name reassessment without describing them as missing', () => {
  const questions = [{ id: 'TI-Q-004', area: 'Architecture', question: 'Which architecture?', status: 'Answered',
    resolutionSource: 'human', answer: 'Modular monolith.', answeredBy: 'Architect',
    assessmentStatus: 'NeedsReview', assessmentReason: 'BRD-FR-004 ownership changed; reassess this retained answer.' }];
  for (const html of [renderWizard(questions), renderTechnicalIntentQuestionsHtml({ cspSource: 'test:' },
    { status: 'current', questions, unansweredCount: 0, reassessmentCount: 1 }, 'nonce')]) {
    assert.match(html, /Retained; reassessment needed/u);
    assert.match(html, /BRD-FR-004 ownership changed/u);
    assert.match(html, /Modular monolith\./u);
    assert.doesNotMatch(html, /Decision required/u);
  }
});

test('wizard reuses source facts with distinct authority labels and keeps absent optional scope nonblocking', () => {
  const html = renderWizard(sourceQuestions());
  assert.match(html, /3 of 3 answers resolved/u);
  assert.match(html, /Direction from an accepted source/u);
  assert.match(html, /Supplied source direction; baseline adoption pending/u);
  assert.match(html, /Final baseline adoption is still required/u);
  assert.match(html, /docs\/accepted\.md:10/u);
  assert.match(html, /docs\/supplied\.md:12/u);
  assert.match(html, /Not requested/u);
  assert.doesNotMatch(html, /id="wizard-TI-Q-015"|data-save-question="TI-Q-015"/u);
  assert.equal((html.match(/Save an override/gu) || []).length, 2);
  assert.doesNotMatch(html, /Recorded by|Decision required/u);
});

test('standalone questions show source authority and count not-requested scope without asking for an answer', () => {
  const html = renderTechnicalIntentQuestionsHtml({ cspSource: 'test:' },
    { current: true, complete: true, questions: sourceQuestions() }, 'nonce');
  assert.match(html, /0 remaining/u);
  assert.match(html, /1 not requested/u);
  assert.match(html, /The technical questionnaire is complete/u);
  assert.match(html, /Direction from an accepted source/u);
  assert.match(html, /Supplied source direction; baseline adoption pending/u);
  assert.doesNotMatch(html, /id="technical-answer-TI-Q-015"|data-command="save-answer" data-value="TI-Q-015"/u);
  assert.doesNotMatch(html, /Derived from the existing project|Repository evidence|Recorded by/u);
});

test('draft optional scope remains visibly pending adoption without creating a manual save', () => {
  const questions = sourceQuestions().slice(2);
  questions[0] = { ...questions[0], resolutionSource: 'selected-scope-not-requested', evidence: ['docs/<supplied>.md:1'] };
  for (const html of [renderWizard(questions), renderTechnicalIntentQuestionsHtml({ cspSource: 'test:' },
    { current: true, complete: true, questions }, 'nonce')]) {
    assert.match(html, /Not requested; awaiting adoption/u);
    assert.match(html, /This draft scope still requires final baseline adoption/u);
    assert.match(html, /Supplied scope/u);
    assert.match(html, /docs\/&lt;supplied&gt;\.md:1/u);
    assert.doesNotMatch(html, /Accepted scope|data-save-question="TI-Q-015"|data-command="save-answer"/u);
  }
});

test('an optional capability becomes a visible targeted question when the backend finds unresolved requested scope', () => {
  const questions = [{ ...sourceQuestions()[2], status: 'Unanswered', resolutionSource: 'technical-intent-document',
    suggestedAnswer: 'The current scope must use AI; choose the hosting boundary.' }];
  const html = renderWizard(questions);
  assert.match(html, /0 of 1 answers resolved/u);
  assert.match(html, /Decision required/u);
  assert.match(html, /data-save-question="TI-Q-015"/u);
  assert.match(html, /choose the hosting boundary/u);
});

for (const problem of [
  { status: 'Import problem', resolutionSource: 'import-problem', label: 'Import problem',
    source: 'Source cannot be extracted after <unclosed> block.', instruction: /Repair the reported source import problem/u },
  { status: 'Conflict', resolutionSource: 'source-conflict', label: 'Source conflict',
    source: 'The primary data store is PostgreSQL.\nThe primary data store is SQLite.', instruction: /Reconcile the conflicting source statements/u },
]) {
  test(`${problem.status} keeps source evidence visible without offering a questionnaire save`, () => {
    const questions = [sourceQuestions()[0], { id: 'TI-Q-006', area: 'Persistence', question: 'Which store?',
      ...problem, answer: null, suggestedAnswer: problem.source, evidence: ['docs/input.md:25 — <source>'] }];
    for (const html of [renderWizard(questions), renderTechnicalIntentQuestionsHtml({ cspSource: 'test:' },
      { current: false, complete: false, questions }, 'nonce')]) {
      assert.ok(html.includes(problem.label));
      assert.match(html, problem.instruction);
      assert.match(html, /docs\/input\.md:25 — &lt;source&gt;/u);
      assert.doesNotMatch(html, /id="(?:wizard|technical-answer)-TI-Q-006"|data-save-question="TI-Q-006"|data-command="save-answer" data-value="TI-Q-006"/u);
      assert.match(html, /Use \.NET\./u); // Unaffected accepted direction remains available.
      if (problem.status === 'Conflict') {
        assert.match(html, /The primary data store is PostgreSQL\.<br>The primary data store is SQLite\./u);
      } else assert.match(html, /after &lt;unclosed&gt; block/u);
    }
  });
}

test('a conflicting recorded human override can be corrected without converting source conflicts into questionnaire saves', () => {
  const question = { id: 'TI-Q-006', area: 'Persistence', question: 'Which store?', status: 'Conflict',
    resolutionSource: 'source-conflict', answer: 'SQLite', answeredBy: 'Recorded owner', answeredAtUtc: '2026-11-02T12:00:00Z',
    suggestedAnswer: 'Accepted source: the primary data store is PostgreSQL.\nRecorded override: SQLite.',
    evidence: ['docs/accepted.md:16', 'docs/questionnaire.md:45'] };
  const wizard = renderWizard([question]);
  const standalone = renderTechnicalIntentQuestionsHtml({ cspSource: 'test:' },
    { current: false, complete: false, questions: [question] }, 'nonce');
  for (const html of [wizard, standalone]) {
    assert.match(html, /Source conflict/u);
    assert.match(html, /Recorded override — correction pending reconciliation/u);
    assert.match(html, />SQLite<\/textarea>/u);
    assert.match(html, /Recorded by Recorded owner at 2026-11-02T12:00:00Z/u);
    assert.match(html, />Correct recorded override<\/button>/u);
    assert.match(html, /Saving an override does not approve changed product direction/u);
    assert.match(html, /docs\/accepted\.md:16/u);
    assert.match(html, /docs\/questionnaire\.md:45/u);
    assert.doesNotMatch(html, /Recorded by unknown|Save direction/u);
  }
  assert.match(wizard, /data-save-question="TI-Q-006"/u);
  assert.match(standalone, /data-command="save-answer" data-value="TI-Q-006"/u);
  // An import problem remains read-only even when a prior human answer is retained.
  assert.doesNotMatch(renderWizard([{ ...question, status: 'Import problem', resolutionSource: 'import-problem' }]),
    /Correct recorded override|data-save-question="TI-Q-006"/u);
});
