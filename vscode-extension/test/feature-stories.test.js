'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { renderStoryList, parseStories, moveStory } = require('../lib/feature-stories');
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

const MVP = 'delivery-stories-mvp';
const LATER = 'delivery-stories-post-mvp';
const request = (fieldId, value, index = 0) => ({ fieldId, index, key: parseStories(value).stories[index].key });

test('each MVP and Post-MVP card has the corresponding move button, including while collapsed', () => {
  const story = '### Customer handoff\n\nAs a customer, I want to continue.';
  assert.match(renderStoryList(story, { fieldId: MVP }), />Move to Post-MVP<\/button>/u);
  assert.match(renderStoryList(story, { fieldId: LATER }), />Promote to MVP<\/button>/u);
  assert.match(renderStoryList(story, { fieldId: LATER, busy: true }), /disabled>Promote to MVP/u);
  assert.doesNotMatch(renderStoryList(story, { fieldId: 'delivery-stories-foundation' }), /move-story/u);
});

test('moving a complete story preserves acceptance, hidden references and other stories in both directions', () => {
  const story = '### Customer handoff\n\nAs a customer, I want an immediate redirect.\n\n- Use the current tab.\n<!-- Source: DIR-001\n### Hidden heading\n-->';
  const second = '### Export\n\nUse an approved request.\n\n```text\n### This is an example\n```\n<!-- Source: EXP-001 -->';
  let answers = { [MVP]: story + '\n\n' + second, [LATER]: 'No Post-MVP stories are established by the current BRD. Review and confirm that no later delivery is required.' };
  const moved = moveStory(answers, request(MVP, answers[MVP]));
  assert.equal(moved.fields[MVP], second);
  assert.equal(moved.fields[LATER], story);
  assert.equal(parseStories(moved.fields[MVP]).stories.length, 1);
  assert.deepEqual(moved.focus, { fieldId: LATER, index: 0 });
  answers = { ...answers, ...moved.fields };
  const promoted = moveStory(answers, request(LATER, answers[LATER]));
  assert.equal(promoted.fields[MVP], second + '\n\n' + story);
  assert.equal(promoted.fields[LATER], 'No Post-MVP stories are currently selected.');
  assert.equal(promoted.category, 'MVP');
});

test('moving the last story leaves a reviewable empty list and round-trips into it without a stale empty message', () => {
  const story = '### Single story\n\nAcceptance details.';
  const result = moveStory({ [MVP]: story, [LATER]: '' }, request(MVP, story));
  assert.equal(result.fields[MVP], 'No MVP stories are currently selected.');
  const restored = moveStory(result.fields, request(LATER, result.fields[LATER]));
  assert.equal(restored.fields[MVP], story);
});

test('stale cards, Foundation moves and oversized destinations leave both lists intact', () => {
  const story = '### Capture\n\nExisting criteria.\n<!-- Source: FRM-001 -->';
  const action = request(MVP, story);
  const answers = { [MVP]: story.replace('Existing criteria.', 'My unsaved edits.'), [LATER]: 'Reviewed release note.' };
  const original = structuredClone(answers);
  assert.throws(() => moveStory(answers, action), /Your edits are retained/u);
  assert.deepEqual(answers, original);
  assert.throws(() => moveStory(answers, { ...action, fieldId: 'delivery-stories-foundation' }), /Select an MVP or Post-MVP/u);
  answers[MVP] = story; answers[LATER] = 'Long release note: ' + 'x'.repeat(23970);
  assert.throws(() => moveStory(answers, action), /both lists are unchanged/u);
  assert.equal(answers[MVP], story);
});
