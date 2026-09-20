'use strict';
const test = require('node:test'), assert = require('node:assert/strict'), vm = require('node:vm');
const { technicalDecisionFormScript, renderTechnicalDecisionForm } = require('../lib/technical-decision-form');
const { escapeHtml } = require('../lib/security');

function harness(saved = {}, values = [{ id: 'one' }, { id: 'two' }]) {
  let state = saved;
  const forms = values.map(value => {
    const input = text => ({ value: text || '', listeners: {}, addEventListener(event, handler) { this.listeners[event] = handler; } });
    const controls = { '[data-technical-resolution]': input(value.resolution),
      '[data-technical-stale]': { hidden: true }, '[data-technical-rebase]': { hidden: true }, '[data-technical-error]': { hidden: true } };
    return { dataset: { technicalDecisionId: value.id, reviewToken: value.token || 'current', answerRecorded: String(Boolean(value.recorded)) }, controls, querySelector: selector => controls[selector] };
  });
  const form = vm.runInNewContext(technicalDecisionFormScript(), { vscode: { getState: () => state, setState: value => { state = JSON.parse(JSON.stringify(value)); } }, document: { querySelectorAll: () => forms } });
  return { forms, state: () => state,
    click(index, attribute) { return form.message({ closest: () => forms[index], hasAttribute: name => name === attribute }); },
    edit(index, resolution) { const control = forms[index].controls['[data-technical-resolution]']; control.value = resolution; control.listeners.input(); },
  };
}

test('suggestions are prefilled on every render and one answer saves without a separate reason', () => {
  const decision = { id: 'one', needsReview: true, reviewToken: 'current', suggestedResolution: 'Keep PostgreSQL as the primary store to preserve the existing data boundary.' };
  for (const id of ['one', 'newly-discovered']) {
    const html = renderTechnicalDecisionForm({ ...decision, id }, escapeHtml);
    assert.match(html, /<textarea data-technical-resolution[^>]*>Keep PostgreSQL/u);
    assert.match(html, /Suggested answer — review and edit/u);
    assert.doesNotMatch(html, /data-technical-reason|Use recorded answers/u);
  }
  const ui = harness({}, [{ id: 'one', resolution: decision.suggestedResolution }]);
  assert.deepEqual(ui.state().technicalDecisionDrafts, {}, 'Prefilling does not record a choice or create an edit');
  const message = ui.click(0, 'data-save-technical-decision');
  assert.equal(message.command, 'save-technical-decision');
  assert.deepEqual(JSON.parse(message.value), { id: 'one', reviewToken: 'current', resolution: decision.suggestedResolution });
  ui.edit(0, ''); assert.equal(ui.click(0, 'data-save-technical-decision'), null);
});

test('saving one decision preserves other unsaved edits and independent wizard form state', () => {
  const ui = harness({ sourceDrafts: { retained: true } });
  ui.edit(0, 'Keep PostgreSQL for the existing data boundary.'); ui.edit(1, 'Keep API versioning for compatibility.');
  const next = harness(ui.state(), [{ id: 'one', resolution: 'Keep PostgreSQL for the existing data boundary.', token: 'saved', recorded: true }, { id: 'two', resolution: 'A new suggestion.' }]);
  assert.equal(next.state().technicalDecisionDrafts.one, undefined);
  assert.equal(next.forms[1].controls['[data-technical-resolution]'].value, 'Keep API versioning for compatibility.');
  assert.equal(next.state().sourceDrafts.retained, true);
});

test('stale decisions retain drafts even when they match a suggestion and require review before saving', () => {
  const ui = harness(); ui.edit(0, 'Keep PostgreSQL.');
  const next = harness(ui.state(), [{ id: 'one', token: 'changed', resolution: 'Keep PostgreSQL.' }]);
  assert.equal(next.click(0, 'data-save-technical-decision'), null);
  assert.equal(next.forms[0].controls['[data-technical-stale]'].hidden, false);
  next.click(0, 'data-keep-technical-edits');
  assert.equal(JSON.parse(next.click(0, 'data-save-technical-decision').value).reviewToken, 'changed');
  next.edit(0, 'New manual text.'); next.click(0, 'data-discard-technical-edits');
  assert.equal(next.forms[0].controls['[data-technical-resolution]'].value, 'Keep PostgreSQL.');
});

test('upgrading the form preserves previous reason text and never overwrites saved answers with suggestions', () => {
  const html = renderTechnicalDecisionForm({ id: 'one', recordedResolution: 'Existing choice.', recordedReason: 'Existing rationale.', suggestedResolution: 'Different suggestion.' }, escapeHtml);
  assert.match(html, /Existing choice\.\n\nReason: Existing rationale\./u);
  assert.doesNotMatch(html, /Different suggestion/u);
  const previous = { technicalDecisionDrafts: { one: { resolution: 'My unsaved choice.', reason: 'My unsaved rationale.', reviewToken: 'current' } } };
  const ui = harness(previous);
  const answer = JSON.parse(ui.click(0, 'data-save-technical-decision').value);
  assert.equal(answer.resolution, 'My unsaved choice.\n\nReason: My unsaved rationale.');
  assert.equal(answer.reason, undefined);
  const next = harness(ui.state());
  assert.equal(next.forms[0].controls['[data-technical-resolution]'].value, answer.resolution);
});

test('one answer field escapes source text and retains answer provenance', () => {
  const html = renderTechnicalDecisionForm({ id: 'TI-DEC-1', reviewToken: 'checked', needsReview: true,
    suggestedResolution: '</textarea><script>bad</script>', relatedAnswers: [
      { id: 'TI-Q-1', area: 'Data', answer: 'PostgreSQL', status: 'Answered', answeredBy: 'Owner' },
      { id: 'TI-Q-2', area: 'Cache', answer: 'Redis', status: 'Derived' } ] }, escapeHtml);
  assert.match(html, /data-technical-resolution/u); assert.doesNotMatch(html, /data-technical-reason/u);
  assert.equal((html.match(/<textarea /gu) || []).length, 1);
  assert.match(html, /Save answer/u); assert.match(html, /Recorded by Owner/u); assert.match(html, /Derived from repository evidence/u);
  assert.doesNotMatch(html, /<script>bad/u); assert.match(html, /&lt;\/textarea&gt;/u);
});
