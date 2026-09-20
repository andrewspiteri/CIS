'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const { sourceReviewScript } = require('../lib/business-source-form');

function harness(saved = {}, values = [{ id: 'one' }, { id: 'two' }]) {
  const element = extra => ({ hidden: false, listeners: {}, addEventListener(type, handler) { this.listeners[type] = handler; },
    focus() { this.focused = true; }, ...extra });
  const cards = values.map(value => {
    const radios = ['Adopted', 'Reference', 'Rejected'].map(choice => element({ value: choice, checked: choice === value.assessment }));
    const reason = element({ value: value.reason || '' });
    const controls = { '[data-source-reason]': reason, '[data-source-error]': element({ hidden: true }),
      '[data-reset-source]': element(), '[data-confirm-source]': element() };
    return { dataset: { sourceId: value.id, reviewToken: value.token || 'current', requiresRefresh: value.requiresRefresh ? 'true' : 'false' },
      radios, reason, controls, querySelector: selector => controls[selector], querySelectorAll: () => radios,
      scrollIntoView() { this.scrolled = true; } };
  });
  const buttons = [element({ hasAttribute: name => name === 'data-save-sources' })];
  const labels = [element()]; const section = element({ open: false }); let state = saved;
  const api = { getState: () => state, setState: value => { state = JSON.parse(JSON.stringify(value)); } };
  const form = vm.runInNewContext(sourceReviewScript(), { vscode: api, document: {
    getElementById: () => section,
    querySelectorAll: selector => selector === '[data-source-id]' ? cards : selector === '[data-save-sources]' ? buttons : labels,
  } });
  return { cards, buttons, section, form, state: () => state,
    choose(index, assessment, reason) {
      cards[index].radios.forEach(radio => { radio.checked = radio.value === assessment; });
      cards[index].reason.value = reason; cards[index].reason.listeners.input();
    },
    submit: () => form.message(buttons[0]),
  };
}

test('only explicitly edited decisions are submitted, with no default assessment or blank reason', () => {
  const ui = harness();
  assert.equal(ui.buttons[0].disabled, true);
  ui.choose(0, 'Reference', '');
  assert.equal(ui.submit(), null);
  assert.equal(ui.cards[0].reason.focused, true);
  ui.choose(0, 'Reference', 'Background to the deposit workflow.');
  const message = ui.submit();
  assert.equal(message.command, 'save-source-decisions');
  assert.deepEqual(JSON.parse(message.value), [{ id: 'one', assessment: 'Reference', reason: 'Background to the deposit workflow.', reviewToken: 'current' }]);
});

test('a batch preserves unsaved drafts over refresh and clears only decisions confirmed saved', () => {
  const ui = harness();
  ui.choose(0, 'Adopted', 'Use these requirements.'); ui.choose(1, 'Rejected', 'Outside this product.');
  assert.equal(JSON.parse(ui.submit().value).length, 2);
  const reloaded = harness(ui.state(), [{ id: 'one', assessment: 'Adopted', reason: 'Use these requirements.', token: 'saved-version' }, { id: 'two' }]);
  assert.equal(reloaded.cards[1].reason.value, 'Outside this product.');
  assert.equal(reloaded.state().sourceDrafts.one, undefined);
  assert.equal(JSON.parse(reloaded.submit().value).length, 1);
  assert.equal(reloaded.section.open, true);
});

test('source changes retain edits but require explicit confirmation against the new version', () => {
  const ui = harness(); ui.choose(0, 'Reference', 'Reviewed background.');
  const reloaded = harness(ui.state(), [{ id: 'one', token: 'changed' }]);
  assert.equal(reloaded.cards[0].reason.value, 'Reviewed background.');
  assert.equal(reloaded.submit(), null);
  assert.equal(reloaded.cards[0].controls['[data-confirm-source]'].hidden, false);
  reloaded.cards[0].controls['[data-confirm-source]'].listeners.click();
  assert.equal(JSON.parse(reloaded.submit().value)[0].reviewToken, 'changed');
  reloaded.cards[0].controls['[data-reset-source]'].listeners.click();
  assert.equal(reloaded.cards[0].reason.value, ''); assert.equal(reloaded.buttons[0].disabled, true);
});

test('unreconciled evidence and a missing assessment block the whole batch', () => {
  const ui = harness({}, [{ id: 'one' }, { id: 'two', requiresRefresh: true }]);
  ui.choose(0, '', 'A reason alone cannot choose an assessment.');
  assert.equal(ui.submit(), null);
  ui.choose(0, 'Adopted', 'Current requirements.'); ui.choose(1, 'Reference', 'Needs evidence refresh.');
  assert.equal(ui.submit(), null);
  assert.match(ui.cards[1].controls['[data-source-error]'].textContent, /Refresh BRD evidence/u);
});
