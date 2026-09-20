'use strict';
const assert = require('node:assert/strict');
const test = require('node:test');
const { renderUiBaseline, uiBaselineQuestions, uiBaselineSource } = require('../lib/ui-baseline');

function baseline() { return { repositories: [
  { id: 'customer', repositoryPath: '/product/customer', filesRead: 12, facts: [
    { area: 'Frameworks', summary: 'Angular with custom controls', evidence: [{ relativePath: 'package.json', line: 4 }] },
  ], tokens: [{ name: '$brand', value: '#ff6841', evidence: { relativePath: 'src/styles.scss', line: 2 } }] },
  { id: 'staff', repositoryPath: '/product/staff', filesRead: 19, facts: [
    { area: 'Frameworks', summary: 'Angular and PrimeNG', evidence: [{ relativePath: 'package.json', line: 9 }] },
  ], tokens: [] },
], suggestions: { 'UI-Q-003': 'Preserve the existing sidebar.' }, warnings: ['Source observations only.'] }; }

test('UI baseline shows distinct interfaces, real color swatches, and direct source links', () => {
  const html = renderUiBaseline(baseline(), { pages: [{ id: 'technical', complete: false, current: true }] });
  assert.match(html, /Angular with custom controls/u); assert.match(html, /Angular and PrimeNG/u);
  assert.match(html, /fill="#ff6841"/u);
  assert.match(html, /href="file:[^"]+styles.scss#L2" target="_blank"/u);
  assert.match(html, /data-command="open-ui-baseline-source" data-value="0:1"/u);
  assert.match(html, /Review technical decisions/u);
  assert.doesNotMatch(html, /data-command="activate"/u);
  const source = uiBaselineSource(baseline(), '0:1');
  assert.equal(source.repository.id, 'customer'); assert.equal(source.evidence.line, 2);
  for (const value of ['../../secret', '-1:0', '0:1000', '0:1junk']) assert.equal(uiBaselineSource(baseline(), value), undefined);
});

test('baseline suggestions prefill unresolved direction and preserve human answers', () => {
  const questions = [{ id: 'UI-Q-003', status: 'Unanswered', suggestedAnswer: 'Generic shell.' },
    { id: 'UI-Q-003', status: 'Answered', answer: 'Keep the agreed layout.', answeredBy: 'Design owner' }];
  const result = uiBaselineQuestions({ uiBaseline: baseline(), uiQuestions: { questions } });
  assert.equal(result[0].suggestedAnswer, 'Preserve the existing sidebar.');
  assert.equal(result[0].status, 'Unanswered'); assert.equal(result[0].answer, undefined);
  assert.equal(result[1], questions[1]);
});

test('baseline escapes source text and excludes unsafe color and evidence attributes', () => {
  const data = baseline();
  data.repositories[0].facts[0].summary = '<script>bad</script>';
  data.repositories[0].tokens[0].value = 'red" onload="bad';
  data.repositories[0].facts[0].evidence[0].relativePath = '../secret';
  const html = renderUiBaseline(data, {});
  assert.match(html, /&lt;script&gt;bad/u); assert.doesNotMatch(html, /<script>|onload=|href="[^"]*secret/u);
  assert.match(renderUiBaseline({ errors: ['Unavailable'] }, {}), /UI discovery needs attention/u);
});
