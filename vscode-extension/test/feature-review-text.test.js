'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { renderReviewText } = require('../lib/feature-review-text');
const { renderReviewPage, STEPS } = require('../lib/feature-wizard');

test('review text formats requirements tables, lists, emphasis and fenced examples', () => {
  const html = renderReviewText('# Security\n\n| ID | Requirement |\n|---|---|\n| SEC-01 | **Protect** `userId`. |\n\n- Tenant isolation\n- Encryption\n\n```json\n{"id": "example"}\n```');
  assert.match(html, /<h4>Security<\/h4>/u); assert.match(html, /<table>/u);
  assert.match(html, /<th scope="col">Requirement<\/th>/u);
  assert.match(html, /<td><strong>Protect<\/strong> <code>userId<\/code>\.<\/td>/u);
  assert.match(html, /<ul><li>Tenant isolation<\/li><li>Encryption<\/li><\/ul>/u);
  assert.match(html, /<pre><code>/u);
});

test('source HTML and Markdown links cannot load resources or execute code', () => {
  const html = renderReviewText('<script>alert(1)</script>\n<img src=x onerror=alert(1)>\n\n[click](javascript:alert(1)) ![image](https://example.invalid/image.png)\n<!-- Hidden source metadata -->');
  assert.doesNotMatch(html, /<(?:script|img|a)\b|<[^>]+\b(?:href|src)=|Hidden source metadata/u);
  assert.match(html, /&lt;script&gt;/u); assert.match(html, /click/u);
});

test('structured review shows numbered questions, formatted suggestions and visible save progression', () => {
  const pages = STEPS.map(([id, title]) => ({ id, title, fields: [], documents: [], attention: [], complete: false, status: 'Needs attention' }));
  const technical = pages.find(page => page.id === 'technical');
  technical.fields = [{ id: 'summary', label: 'Additional review notes (optional)', suggestedAnswer: '', required: false },
    { id: 'platform', label: 'Which platform?', suggestedAnswer: '| Item | Direction |\n|---|---|\n| Runtime | Existing stack |', required: true },
    { id: 'security', label: 'How is data protected?', suggestedAnswer: '- Tenant isolation\n- Encryption', required: true }];
  const html = renderReviewPage({ page: 'technical', wizard: { pages, baselineCurrent: true }, pageDrafts: {} });
  assert.match(html, /Step 3 of 8/u); assert.match(html, /2 review questions/u);
  assert.match(html, /Question 1 of 2/u); assert.match(html, /Question 2 of 2/u);
  assert.match(html, /data-question-target="security"/u); assert.match(html, /<table>/u);
  assert.match(html, /Edit answer/u); assert.match(html, /form="feature-review-form"[^>]*>Save and continue/u);
  assert.match(html, /Next: Solution architecture and diagrams/u);
});

test('a BRD without open decisions explains narrative review and keeps a saved answer separate from new suggestions', () => {
  const current = { id: 'business', title: 'Business definition', status: 'Needs attention', complete: false, documents: [], attention: ['Review the changed source.'],
    fields: [{ id: 'summary', label: 'Review the scope.', answer: 'Previously saved scope.', suggestedAnswer: 'New source scope.', required: true }] };
  const html = renderReviewPage({ page: 'business', wizard: { pages: [current], baselineCurrent: true }, pageDrafts: {} });
  assert.match(html, /No open business decisions were found/u);
  assert.match(html, /Previously saved scope/u); assert.match(html, /New source scope/u);
  assert.match(html, /data-wizard-action="use-suggestion" data-value="summary"/u);
});
