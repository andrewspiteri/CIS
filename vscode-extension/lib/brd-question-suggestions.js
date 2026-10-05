'use strict';

async function generateQuestionSuggestions(vscode, cli, root, assertCurrent) {
  assertCurrent();
  const ai = await cli.query(['ai', 'status'], { repository: false, cache: false, interactive: true });
  assertCurrent();
  const choices = (ai.providers || []).flatMap(provider => (provider.models?.length ? provider.models : [{}]).map(model => ({
    label: `${provider.name} / ${model.name || 'Provider default'}`, provider, model: model.name,
    description: !provider.isAvailable ? 'Unavailable' : provider.isLocal ? 'Local — runs on this computer' : 'Remote — requires permission to send question context',
    detail: provider.detail,
  })));
  if (!choices.length) throw new Error('No AI models are available. Configure a provider before generating suggestions.');
  const selected = await vscode.window.showQuickPick(choices, {
    title: 'Model for advisory BRD answers', placeHolder: 'Choose a model for the unanswered questions', matchOnDescription: true, ignoreFocusOut: true,
  });
  if (!selected) return false;
  assertCurrent();
  if (!selected.provider.isAvailable) throw new Error(selected.detail || 'The selected provider is unavailable. Check its configuration and sign-in.');
  const args = ['brd', 'questions', 'suggest', '--workspace', root, '--provider', selected.provider.name];
  if (selected.model) args.push('--model', selected.model);
  if (!selected.provider.isLocal) {
    const approved = await vscode.window.showWarningMessage(
      `Send the unanswered BRD questions and displayed context to ${selected.label}?`,
      { modal: true, detail: 'Suggestions remain advisory; no answers are recorded automatically. For unresolved decisions, the model can propose an answer with explicit assumptions for your review. CIS may retry omitted or malformed responses once per affected question using the same model.' },
      'Allow remote suggestion');
    if (approved !== 'Allow remote suggestion') return false;
    args.push('--allow-remote');
  }
  assertCurrent();
  await cli.runForeground(`Generate advisory BRD answers · ${selected.label}`, args, { repository: false, cancellable: false });
  return true;
}

module.exports = { generateQuestionSuggestions };
