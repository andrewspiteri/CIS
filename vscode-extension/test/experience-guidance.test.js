'use strict';
const assert = require('node:assert/strict');
const test = require('node:test');
const { experienceState } = require('../lib/experience-guidance');
const { renderDefinitionWizardHtml } = require('../lib/webview');

function model() {
  return { pages: [
    { id: 'technical', ordinal: 3, title: 'Technical direction', complete: false, current: true,
      issues: ['Technical decision remains unresolved: TI-DEC-001', 'Technical decision remains unresolved: TI-DEC-002'] },
    { id: 'architecture', ordinal: 4, title: 'Architecture', complete: false, current: false },
    { id: 'experience', ordinal: 6, title: 'Experience direction and UI preview', status: 'Missing', complete: false, current: false, issues: ['Canonical UI direction is missing.'] },
  ], uiQuestions: { questions: [] }, uiBaseline: { sourceHash: 'checked', repositories: [
    { id: 'customer', repositoryPath: '/product/customer', preview: { svg: '<svg/>', width: 1200, height: 900 } },
    { id: 'staff', repositoryPath: '/product/staff', preview: { svg: '<svg/>', width: 1200, height: 1200 } },
  ] } };
}
function render(data) {
  return renderDefinitionWizardHtml({ cspSource: 'test:', asWebviewUri: value => ({ toString: () => `test:${value}` }) },
    '/authority', data, 'experience', 'nonce', { Uri: { file: value => value } });
}
function upstreamReady(data) { data.pages.filter(page => page.id !== 'experience').forEach(page => { page.complete = true; page.current = true; }); }

test('imported baseline is the visual preview while missing direction points to actual upstream decisions', () => {
  const data = model(); const state = experienceState(data, '/authority'); const html = render(data);
  assert.equal(state.visual, 'Available — 2 of 2 interface control sheets');
  assert.equal(state.badge, 'Preview available · Direction pending');
  assert.equal(state.ready, false); assert.equal(data.pages[2].complete, false);
  assert.match(state.next, /2 technical document decisions/u);
  assert.equal(state.action.command, 'navigate'); assert.equal(state.action.value, 'technical');
  assert.match(html, /Review technical decisions/u); assert.match(html, /Review solution architecture/u);
  assert.equal((html.match(/<h3>One-page visual system preview<\/h3>/gu) || []).length, 1);
  assert.equal((html.match(/data-control-sheet=/gu) || []).length, 2);
  assert.doesNotMatch(html, /Complete the UI-direction questionnaire and prepare|Prepare this page to initialize|data-command="prepare"|data-command="activate"/u);
  assert.match(html, /Canonical UI direction is missing/u);
});

test('questionnaire guidance distinguishes missing, unanswered, stale and complete choices', () => {
  const data = model(); upstreamReady(data);
  assert.equal(experienceState(data, '/authority').action.label, 'Prepare UI questions');
  data.uiQuestions = { current: true, complete: false, questions: [{ id: 'UI-Q-001', status: 'Answered' }, { id: 'UI-Q-002', status: 'Unanswered' }] };
  assert.equal(experienceState(data, '/authority').direction, '1 UI questions need answers out of 2');
  assert.equal(experienceState(data, '/authority').action.command, 'focus-ui-questions');
  assert.match(render(data), /id="ui-direction-questions"/u);
  data.uiQuestions.questions[1].status = 'Derived'; data.uiQuestions.complete = true; data.uiQuestions.current = false;
  assert.equal(experienceState(data, '/authority').action.label, 'Refresh UI direction');
  data.uiQuestions.current = true;
  assert.equal(experienceState(data, '/authority').action.label, 'Prepare UI direction');
  assert.doesNotMatch(render(data), /Complete the UI-direction questionnaire and prepare/u);
});

test('complete governance remains authoritative and the delivery action is available once', () => {
  const data = model(); upstreamReady(data); Object.assign(data.pages[2], { complete: true, current: true, status: 'Ready for Approval' });
  const state = experienceState(data, '/authority');
  assert.equal(state.badge, 'Ready for Approval'); assert.equal(state.ready, true);
  assert.equal(state.action.value, 'delivery'); assert.equal(state.direction, 'Complete and current');
  const footer = render(data).split('<footer class="wizard-footer">')[1].split('</footer>')[0];
  assert.equal((footer.match(/data-value="delivery"/gu) || []).length, 1);
});

test('missing, failed and partial discovery never claims a complete preview set', () => {
  const data = model(); data.uiBaseline.repositories[1].preview.width = -1;
  assert.equal(experienceState(data, '/authority').visual, 'Available — 1 of 2 interface control sheets');
  data.uiBaseline.errors = ['Unavailable'];
  assert.equal(experienceState(data, '/authority').visual, 'Discovery failed — refresh to retry');
  assert.doesNotMatch(experienceState(data, '/authority').badge, /Preview available/u);
  delete data.uiBaseline;
  assert.equal(experienceState(data, '/authority').visual, 'Discovering the existing interfaces');
  data.uiBaseline = { repositories: [] };
  assert.equal(experienceState(data, '/authority').visual, 'No existing interface preview found');
});

test('a stale recorded preview remains visible and labelled without a second empty preview', () => {
  const data = model(); data.preview = { svgRelativePath: 'docs/design/ui-system-preview.svg', status: 'Stale' };
  const html = render(data);
  assert.match(html, /Recorded UI direction preview — stale/u);
  assert.match(html, /Previous preview — refresh required/u);
  assert.equal((html.match(/<h3>One-page visual system preview<\/h3>/gu) || []).length, 1);
  data.preview.svgRelativePath = '../escape.svg';
  assert.equal(experienceState(data, '/authority').canonicalFile, undefined);
  assert.doesNotMatch(render(data), /test:.*escape.svg/u);
});
