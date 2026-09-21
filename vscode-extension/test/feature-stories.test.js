'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { renderStoryList } = require('../lib/feature-stories');
const { renderReviewPage, hasUnsavedChanges } = require('../lib/feature-wizard');

test('story lists show every title with expandable acceptance and hide source comments', () => {
  const html = renderStoryList('### Protect tenant data\n\nAs an operator, I want isolated tenant records.\n\n- Reject cross-tenant queries.\n<!-- Source: SEC-001 -->\n\n### Customer handoff\n\nAs a customer, I want an immediate redirect.');
  assert.match(html, /2 user stories/u);
  assert.match(html, /<summary>Protect tenant data<\/summary>/u);
  assert.match(html, /<li>Reject cross-tenant queries\.<\/li>/u);
  assert.match(html, /<summary>Customer handoff<\/summary>/u);
  assert.doesNotMatch(html, /SEC-001/u);
});

test('story titles and acceptance are passive text with no executable source markup', () => {
  const html = renderStoryList('### <img src=x onerror=alert(1)>\n\n<script>alert(1)</script>\n\n[click](https://untrusted.invalid)');
  assert.doesNotMatch(html, /<img|<script|href=/u);
  assert.match(html, /&lt;img/u);
});

test('delivery shows three story lists in the save form and keeps planning questions separate', () => {
  const fields = [
    ...['foundation', 'mvp', 'post-mvp'].map(id => ({ id: `delivery-stories-${id}`, label: id, suggestedAnswer: `### ${id} story\n\nSuggested narrative.`, required: true })),
    { id: 'delivery-boundary', label: 'Release boundary', suggestedAnswer: 'Initial scope.', required: true },
  ];
  const page = { id: 'delivery', title: 'Delivery and acceptance', fields, documents: [], attention: [], complete: false, status: 'Needs attention' };
  const model = { page: 'delivery', wizard: { pages: [page], baselineCurrent: true }, pageDrafts: {} };
  const html = renderReviewPage(model);
  const form = html.match(/<form id="feature-review-form"[^]*?<\/form>/u)[0];
  assert.match(form, /Required user stories/u);
  assert.match(form, /Foundation is required regardless of release scope/u);
  assert.equal((form.match(/Edit user stories/gu) || []).length, 3);
  assert.equal((form.match(/name="delivery-stories-/gu) || []).length, 3);
  assert.match(html, /1 review questions/u);
  assert.match(html, /Question 1 of 1/u);
  assert.doesNotMatch(html, /Question 2 of/u);
  assert.ok(html.indexOf('Required user stories') < html.indexOf('Repository feature breakdown'));
  fields[1].answer = '### My reviewed story\n\nSaved direction.';
  assert.match(renderReviewPage(model), /<summary>My reviewed story<\/summary>/u);
  model.pageDrafts.delivery = { 'delivery-stories-mvp': '### My edit\n\nUnsaved direction.' };
  assert.equal(hasUnsavedChanges(model, page), true);
  assert.match(renderReviewPage(model), /<summary>My edit<\/summary>/u);
});
