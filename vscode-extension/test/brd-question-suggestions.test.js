'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { generateQuestionSuggestions } = require('../lib/brd-question-suggestions');
const { renderBrdQuestionsHtml } = require('../lib/webview');

function fixture({ model = 'gpt-6-astra', cancel = false, consent = true, unavailable = false, changeAt } = {}) {
  let changed = false; const runs = []; let shown;
  const vscode = { window: {
    showQuickPick: async choices => { shown = choices; if (changeAt === 'pick') changed = true; return cancel ? undefined : choices.find(choice => choice.model === model); },
    showWarningMessage: async (_title, options) => { assert.match(options.detail, /no answers are recorded/u); if (changeAt === 'consent') changed = true; return consent ? 'Allow remote suggestion' : undefined; },
  } };
  const cli = { query: async () => ({ providers: [
    { name: 'ollama', isAvailable: true, isLocal: true, models: [{ name: 'small' }, { name: 'large' }] },
    { name: 'codex', isAvailable: !unavailable, isLocal: false, models: [{ name: 'gpt-6-sol' }, { name: 'gpt-6-astra' }] },
  ] }), runForeground: async (title, args) => runs.push({ title, args }) };
  return { run: () => generateQuestionSuggestions(vscode, cli, '/example-workspace', () => { if (changed) throw new Error('Product changed'); }), runs, choices: () => shown };
}

test('stronger remote model remains selectable with Ollama running and is passed explicitly', async () => {
  const f = fixture(); assert.equal(await f.run(), true);
  assert.equal(f.choices().length, 4);
  assert.deepEqual(f.runs[0].args, ['brd', 'questions', 'suggest', '--workspace', '/example-workspace', '--provider', 'codex', '--model', 'gpt-6-astra', '--allow-remote']);
  assert.match(f.runs[0].title, /gpt-6-astra/u);
});
test('local model selection is explicit and needs no remote consent', async () => {
  const f = fixture({ model: 'large', consent: false }); assert.equal(await f.run(), true);
  assert.ok(f.runs[0].args.includes('large')); assert.ok(!f.runs[0].args.includes('--allow-remote'));
});
test('cancelled choices and rejected disclosure never launch generation', async () => {
  for (const options of [{ cancel: true }, { consent: false }]) { const f = fixture(options); assert.equal(await f.run(), false); assert.equal(f.runs.length, 0); }
});
test('unavailable model and changed product prevent generation', async () => {
  for (const options of [{ unavailable: true }, { changeAt: 'pick' }, { changeAt: 'consent' }]) {
    const f = fixture(options); await assert.rejects(f.run(), /unavailable|Product changed/u); assert.equal(f.runs.length, 0);
  }
});
test('answered questions do not inflate pending suggestion count and failure reasons remain visible', () => {
  const html = renderBrdQuestionsHtml({ cspSource: 'test:' }, { suggestionStatus: 'current', questions: [
    { id: 'Q1', status: 'Answered', answer: 'Recorded', suggestedAnswer: 'Prior suggestion' },
    { id: 'Q2', status: 'Unanswered', suggestionReason: 'No supported evidence.' },
  ] }, 'nonce');
  assert.match(html, /0 suggested/u); assert.match(html, /No supported evidence/u); assert.match(html, /choose model/u);
});

test('supported answers populate editable fields while absent answers retain explanations separately', () => {
  const html = renderBrdQuestionsHtml({ cspSource: 'test:' }, { suggestionStatus: 'current', questions: [
    { id: 'Q1', status: 'Answered', answer: 'Human recorded answer', suggestedAnswer: 'Previous suggestion' },
    { id: 'Q2', status: 'Unanswered', suggestedAnswer: 'Supported <answer>', suggestionConfidence: 'high' },
    { id: 'Q3', status: 'Unanswered', suggestedAnswer: null, suggestionReason: 'Execution profile needs a decision.' },
  ] }, 'nonce');
  assert.match(html, /1 of 2 unanswered questions have suggested answers/u);
  assert.match(html, /id="answer-Q1"[^>]*>Human recorded answer<\/textarea>/u);
  assert.match(html, /id="answer-Q2"[^>]*>Supported &lt;answer&gt;<\/textarea>/u);
  assert.match(html, /id="answer-Q3"[^>]*><\/textarea>/u);
  assert.ok(html.indexOf('Execution profile needs a decision.') < html.indexOf('data-question-id="Q1"'));
  assert.doesNotMatch(html, /data-command="accept-suggestion" data-value="Q3"/u);
});

test('successful generation with null answers explicitly explains why there is nothing to populate', () => {
  const html = renderBrdQuestionsHtml({ cspSource: 'test:' }, { suggestionStatus: 'current', questions: [
    { id: 'Q2', status: 'Unanswered', suggestedAnswer: null, suggestionReason: 'Source evidence is missing.' },
  ] }, 'nonce');
  assert.match(html, /Generation completed, but no supported answers are available to populate/u);
  assert.match(html, /id="answer-Q2"[^>]*><\/textarea>/u);
  assert.doesNotMatch(html, /data-command="accept-suggestion"/u);
});

test('proposed decisions populate the answer editor with assumptions and explicit human acceptance', () => {
  const html = renderBrdQuestionsHtml({ cspSource: 'test:' }, { suggestionStatus: 'current', questions: [{
    id: 'Q2', status: 'Unanswered', suggestedAnswer: 'Require verified source records before each run.', suggestionKind: 'proposal',
    suggestionReason: 'A proposed procedure for missing per-run evidence.', suggestionAssumptions: ['Confirm this evidence gate.'],
  }] }, 'nonce');
  assert.match(html, /Proposed decision — needs your confirmation/u);
  assert.match(html, /id="answer-Q2"[^>]*>Require verified source records before each run\.<\/textarea>/u);
  assert.match(html, /Confirm this evidence gate/u); assert.match(html, /Accept proposed answer/u);
  assert.match(html, /Nothing has been recorded/u);
});
