'use strict';

const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');
const { escapeHtml: h, nonce, isValidWebviewMessage } = require('./security');
const { studioDocument } = require('./webview');

function openFeatureStory(vscode, { cli, authority, root, slug, storyId, taskId, actorIdentity, refresh, storage }) {
  const panel = vscode.window.createWebviewPanel('cis.featureStory', 'Story and tasks', vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] });
  const storageKey = `cis.storyFeedback:${root}:${slug}:${storyId}`;
  const restored = storage?.get(storageKey);
  const model = { busy: false, result: undefined, error: undefined, taskId, feedbackDrafts: restored && typeof restored === 'object' ? restored : {} };
  const persistFeedback = async () => { try { await storage?.update(storageKey, model.feedbackDrafts); } catch { model.error = 'Your draft is still in this tab, but CIS could not preserve it for reopening.'; } };
  let disposed = false;
  let refreshTimer;
  const scheduleRefresh = () => {
    clearTimeout(refreshTimer);
    if (!disposed && model.result?.taskProgress?.some(task => task.execution?.status === 'running')) {
      refreshTimer = setTimeout(() => { if (!model.busy) void action('refresh'); else scheduleRefresh(); }, 5000);
      refreshTimer.unref?.();
    }
  };
  const render = () => { if (!disposed) panel.webview.html = renderFeatureStory(panel.webview, model, nonce()); };
  const assertAuthority = () => {
    const normalize = value => process.platform === 'win32' ? path.resolve(value || '').toLowerCase() : path.resolve(value || '');
    if (!authority.root() || normalize(authority.root()) !== normalize(root)) throw new Error('The selected product changed. Reopen this story from its product.');
    if (vscode.workspace.isTrusted === false) throw new Error('Trust the workspace before loading or generating story tasks.');
  };
  const action = async (command, value) => {
    if (disposed) return;
    if (command === 'remember-feedback' || command === 'use-feedback') {
      try {
        assertAuthority();
        const payload = JSON.parse(value);
        const feedback = model.result?.taskProgress?.find(task => task.id === payload.taskId)?.reviewFeedback || [];
        const answers = command === 'use-feedback' ? { [payload.id]: feedback.find(item => item.id === payload.id)?.suggestedAnswer } : payload.answers;
        if (!answers || Object.keys(answers).length > 100 || Object.entries(answers).some(([id, text]) => !feedback.some(item => item.id === id) || typeof text !== 'string' || text.length > 8000)) return;
        model.feedbackDrafts[payload.taskId] = { ...model.feedbackDrafts[payload.taskId], ...answers };
        await persistFeedback();
        if (command === 'use-feedback') render();
      } catch { /* Ignore malformed draft messages; no canonical state is changed. */ }
      return;
    }
    if (model.busy || !['refresh', 'generate', 'approve', 'start', 'complete', 'task', 'story', 'save-feedback', 'suggest-feedback', 'executable-settings'].includes(command)) return;
    if (command === 'task' || command === 'story') {
      if (command === 'task' && !model.result?.tasks?.some(task => task.id === value)) return;
      model.taskId = command === 'task' ? value : undefined; render(); return;
    }
    clearTimeout(refreshTimer);
    model.busy = true; model.error = undefined;
    if (command === 'start') model.taskId = value;
    render();
    try {
      assertAuthority();
      if (command === 'executable-settings') {
        await vscode.commands.executeCommand('workbench.action.openSettings', '@id:cis.executablePath');
        return;
      }
      if (command === 'save-feedback' || command === 'suggest-feedback') {
        const result = model.result;
        const progress = result?.taskProgress?.find(task => task.id === value);
        if (!progress?.reviewFeedback?.length || !result.revision || !result.planHash || progress.execution?.status === 'running' || progress.execution?.applied || progress.status === 'Complete')
          throw new Error('Refresh a task with pending review findings before responding.');
        let updated;
        if (command === 'suggest-feedback') {
          const selected = await chooseStoryModel(vscode, cli, assertAuthority, 'feedback');
          if (!selected) return;
          assertAuthority(); if (disposed) return;
          const args = ['brd', 'feature', 'wizard', 'story', 'feedback-suggest', '--workspace', root, '--slug', slug, '--story', storyId,
            '--task', value, '--expected-revision', result.revision, '--provider', selected.provider, '--model', selected.model];
          if (selected.allowRemote) args.push('--allow-remote');
          updated = await cli.query(args, { repository: false, cache: false, interactive: true, acceptStructuredFailure: true, timeout: 360_000 });
        } else {
          const answers = Object.fromEntries(Object.entries(model.feedbackDrafts[value] || {}).filter(([id, text]) =>
            progress.reviewFeedback.some(item => item.id === id && text !== (item.answer || ''))));
          if (!Object.keys(answers).length) throw new Error('Enter an answer or use a suggested answer before saving.');
          const actor = await actorIdentity?.(); if (!actor) return;
          assertAuthority(); if (disposed) return;
          const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-story-feedback-'));
          const input = path.join(directory, 'answers.json');
          try {
            fs.writeFileSync(input, JSON.stringify({ slug, storyId, taskId: value, expectedPlanHash: result.planHash, expectedRevision: result.revision, actor, answers }), { encoding: 'utf8', mode: 0o600 });
            updated = await cli.query(['brd', 'feature', 'wizard', 'story', 'feedback-save', '--workspace', root, '--input', input],
              { repository: false, cache: false, interactive: true, acceptStructuredFailure: true });
          } finally { if (fs.existsSync(input)) fs.unlinkSync(input); fs.rmdirSync(directory); }
        }
        assertAuthority();
        if (updated.errors?.length || updated._process?.failed) throw new Error((updated.errors || ['Feedback action failed.']).join('\n'));
        if (updated.slug !== slug || updated.storyId !== storyId || !updated.story) throw new Error('CIS returned a different story. Your drafts are retained.');
        model.result = updated;
        if (command === 'save-feedback') { delete model.feedbackDrafts[value]; await persistFeedback(); model.notice = 'Answers saved. Retry implementation and review to apply them; the findings remain open until checked.'; }
        else model.notice = updated.taskProgress?.find(task => task.id === value)?.reviewFeedback?.some(item => item.suggestedAnswer)
          ? 'Suggestions are ready. Use or edit the supported answers, then save them.' : 'The available evidence did not support suggested answers. Enter the decisions or missing information yourself.';
        cli.clearQueryCache?.();
        return;
      }
      const args = ['brd', 'feature', 'wizard', 'story', command === 'generate' ? 'prepare' : command === 'refresh' ? 'status' : command,
        '--workspace', root, '--slug', slug, '--story', storyId];
      if (command === 'generate') {
        if (!model.result?.inputHash) throw new Error('Refresh the story before generating its task breakdown.');
        const selected = await chooseStoryModel(vscode, cli, assertAuthority);
        if (!selected) return;
        assertAuthority();
        args.push('--provider', selected.provider, '--model', selected.model);
        if (selected.allowRemote) args.push('--allow-remote');
        model.selection = selected;
        render();
        args.push('--expected-input-hash', model.result.inputHash);
      }
      if (['approve', 'start', 'complete'].includes(command)) {
        const result = model.result;
        if (!result?.planHash || !result.revision) throw new Error('Refresh this plan before recording a decision.');
        const actor = await actorIdentity?.(); if (!actor) return;
        args.push('--expected-plan-hash', result.planHash, '--expected-revision', result.revision, '--actor', actor);
        if (command === 'approve') {
          const reason = await vscode.window.showInputBox({ title: 'Approve story task plan',
            prompt: 'Record why this exact task plan is ready for work. Approval makes dependency-ready tasks available; it does not run an agent.',
            ignoreFocusOut: true, validateInput: text => !text.trim() || text.length > 2000 ? 'Enter a reason of up to 2000 characters.' : undefined });
          if (!reason?.trim()) return;
          args.push('--reason', reason.trim());
        } else {
          const progress = result.taskProgress?.find(task => task.id === value);
          if (command === 'start' ? !progress?.canExecute : !progress?.canComplete) throw new Error(progress?.blockedReason || 'Refresh this task to check its available actions.');
          args.push('--task', value);
          if (command === 'start') {
            if (Object.entries(model.feedbackDrafts[value] || {}).some(([id, text]) => progress.reviewFeedback?.some(item => item.id === id && text !== (item.answer || ''))))
              throw new Error('Save your review answers before retrying so both models receive them.');
            const preview = await cli.query(['brd', 'feature', 'wizard', 'story', 'execution-plan', '--workspace', root, '--slug', slug, '--story', storyId, '--task', value],
              { repository: false, cache: false, interactive: true, acceptStructuredFailure: true, timeout: 120_000 });
            assertAuthority(); if (disposed) return;
            if (preview.errors?.length) throw new Error(preview.errors.join('\n'));
            if (preview.planHash !== result.planHash || preview.revision !== result.revision) throw new Error('The task plan changed. Refresh before starting.');
            const selected = preview.executionPlan;
            if (!selected?.implementation || !selected.review || !selected.hash) throw new Error('CIS could not select distinct implementation and review models.');
            const label = choice => `${choice.provider} / ${choice.model}`;
            const remote = !selected.implementation.isLocal || !selected.review.isLocal;
            const approved = await vscode.window.showWarningMessage(`Start ${value} with automatic model selection?`, {
              modal: true, detail: `Complexity: ${selected.complexity}. ${(selected.reasons || []).join(' ')}\nImplementation: ${label(selected.implementation)}\nSecondary review: ${label(selected.review)}\n\n${remote ? 'Authorize both selected models to receive the task definition, acceptance criteria and linked repository context. ' : ''}CIS will perform the work in isolated copies and review it independently. Correctable findings trigger up to two correction rounds, each followed by a fresh review. Reviewed changes are applied only when there are no actionable findings and the original files are unchanged. Decisions needing your input remain pending. Completion remains yours to confirm.`,
            }, 'Start implementation and review');
            if (approved !== 'Start implementation and review') return;
            assertAuthority(); if (disposed) return;
            args[4] = 'execute';
            args.push('--expected-execution-hash', selected.hash);
            if (remote) args.push('--allow-remote');
          }
          if (command === 'complete') {
            const evidence = await vscode.window.showInputBox({ title: `Completion evidence for ${value}`,
              prompt: 'Record the work performed and verification results, including relevant file, test or review references.', ignoreFocusOut: true,
              validateInput: text => !text.trim() || text.length > 4000 ? 'Enter evidence of up to 4000 characters.' : undefined });
            if (!evidence?.trim()) return;
            const task = result.tasks.find(item => item.id === value);
            const decision = await vscode.window.showWarningMessage(`Confirm every completion criterion for ${value}`, {
              modal: true, detail: task.acceptanceCriteria.map((criterion, index) => `${index + 1}. ${criterion}`).join('\n'),
            }, 'All criteria verified');
            if (decision !== 'All criteria verified') return;
            args.push('--evidence', evidence.trim(), '--criteria-verified');
          }
        }
        assertAuthority(); if (disposed) return;
      }
      const query = () => cli.query(args, { repository: false, cache: false, interactive: true, acceptStructuredFailure: true, timeout: 660_000 });
      let result;
      if (command === 'start') {
        try { await cli.runForeground(`Implementing and reviewing ${value}`, args, { repository: false, cancellable: false }); }
        finally {
          cli.clearQueryCache?.();
          const updated = await cli.query(['brd', 'feature', 'wizard', 'story', 'status', '--workspace', root, '--slug', slug, '--story', storyId],
            { repository: false, cache: false, interactive: true, acceptStructuredFailure: true });
          assertAuthority();
          if (updated.storyId === storyId && updated.slug === slug && updated.story) { model.result = updated; render(); }
          await refresh?.();
        }
        result = model.result;
      } else result = command === 'generate' ? await vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification, title: `Breaking this story into tasks · ${model.selection.provider} / ${model.selection.model}`, cancellable: false,
      }, query) : await query();
      assertAuthority();
      if (result.errors?.length) throw new Error(result.errors.join('\n'));
      if (!result.story || result.storyId !== storyId || result.slug !== slug) throw new Error('CIS returned a different or unavailable story. Refresh the story list.');
      model.result = result;
      model.selection = undefined;
      panel.title = result.story.title;
      if (command !== 'refresh') { cli.clearQueryCache?.(); await refresh?.(); }
    } catch (error) { model.error = error.message; }
    finally { model.busy = false; render(); scheduleRefresh(); if (command === 'start' && !disposed) panel.reveal(); }
  };
  panel.onDidDispose(() => { disposed = true; clearTimeout(refreshTimer); });
  panel.onDidChangeViewState?.(event => { if (event.webviewPanel.visible && !disposed && !model.busy) void action('refresh'); });
  panel.webview.onDidReceiveMessage(message => {
    if (isValidWebviewMessage(message, new Set(['refresh', 'generate', 'approve', 'start', 'complete', 'task', 'story', 'remember-feedback', 'use-feedback', 'save-feedback', 'suggest-feedback', 'executable-settings']))) void action(message.command, message.value);
  });
  render();
  const ready = action('refresh');
  return { panel, model, action, ready, reveal: selectedTask => { model.taskId = selectedTask; panel.reveal(); return action('refresh'); } };
}

async function chooseStoryModel(vscode, cli, assertAuthority, purpose = 'tasks') {
  const feedback = purpose === 'feedback';
  const status = await cli.query(['ai', 'status'], { repository: false, cache: false, interactive: true });
  assertAuthority();
  const choices = (status.providers || []).flatMap(provider => (provider.models || []).map(model => ({
    label: `${provider.name} · ${model.name}`, provider: provider.name, model: model.name, available: provider.isAvailable,
    allowRemote: !provider.isLocal,
    description: !provider.isAvailable ? 'Unavailable' : provider.isLocal ? 'Local — runs on this computer' : 'Remote — requires permission to send story context',
    detail: provider.detail || undefined,
  })));
  if (!choices.length) throw new Error('No models are available. Configure a CIS AI provider and retry.');
  const selected = await vscode.window.showQuickPick(choices, { title: feedback ? 'Model for suggested review answers' : 'Model for story task breakdown',
    placeHolder: feedback ? 'Choose a model to suggest answers from the available evidence' : 'Choose the model to define this story’s tasks', matchOnDescription: true, ignoreFocusOut: true });
  if (!selected) return undefined;
  assertAuthority();
  if (!selected.available) throw new Error(selected.detail || 'The selected model is unavailable. Check its configuration and sign-in.');
  if (selected.allowRemote) {
    const approval = await vscode.window.showWarningMessage(`Send this story’s context to ${selected.provider} / ${selected.model}?`, {
      modal: true,
      detail: feedback ? 'CIS will send the task, review findings, recorded work summaries and checks, and selected feature BRD and planning context. The model suggests answers with supporting excerpts. You must choose and save answers yourself; suggestions do not resolve findings or approve work.' : 'CIS will send the story definition, acceptance criteria, saved feature direction and constraints, repository identifiers, and selected code excerpts. The model proposes tasks; it does not execute them. If its response fails validation, CIS may make one correction request to the same model.',
    }, 'Send context and generate');
    if (approval !== 'Send context and generate') return undefined;
    assertAuthority();
  }
  return selected;
}

function renderFeatureStory(webview, model, scriptNonce) {
  const result = model.result;
  const story = result?.story;
  const review = story?.reviewCurrent ? story.review : undefined;
  const repositories = [...new Set([...(review?.owners || story?.owners || []), ...(result?.tasks || []).flatMap(task => task.repositoryIds)])];
  const textList = values => `<ul>${(values || []).map(value => `<li>${h(value)}</li>`).join('')}</ul>`;
  const tasks = result?.tasks || [];
  const approved = ['Approved', 'Complete'].includes(result?.planState);
  const selectedTask = tasks.find(task => task.id === model.taskId);
  if (selectedTask) return renderStoryTask(webview, model, selectedTask, scriptNonce);
  const body = `<style nonce="${scriptNonce}">.story-prose{white-space:pre-wrap}.story-task{margin-top:16px}.story-metadata{display:flex;gap:16px;flex-wrap:wrap}.story-criteria li{margin:10px 0}</style>
    <header class="hero"><div><p>${h(result?.featureTitle || 'Feature delivery')} · ${h(story?.phase || '')}</p><h1>${h(story?.title || 'Story and tasks')}</h1><p>Story definition, acceptance criteria and proposed delivery tasks.</p></div></header>
    ${model.error ? `<section class="card"><p role="alert">${h(model.error)}</p></section>` : ''}
    ${story ? `<section class="card"><h2>Story definition</h2><p class="story-prose">${h(result.definition)}</p><p class="muted">Definition and acceptance criteria come from the feature BRD. Task generation preserves this scope.</p><div class="story-metadata"><span><strong>Task plan:</strong> ${h(result.planState || 'Proposed')}</span><span><strong>Linked repositories:</strong> ${h(repositories.join(', ') || 'No repositories linked')}</span></div></section>
    <section class="card"><h2>Acceptance criteria</h2><ol class="story-criteria">${(story.requirements || []).map((criterion, index) => `<li id="criterion-${index + 1}">${h(criterion)}</li>`).join('')}</ol></section>
    <section class="card"><h2>Delivery approach</h2><p class="story-prose">${h(review?.plan || story.remainingWork)}</p><p><strong>Existing capability:</strong> ${h(story.existingCapability === 'unknown' ? 'Not established' : story.existingCapability)}</p>${story.conflict ? `<p role="alert">${h(story.conflict)}</p>` : ''}${story.assessmentReason ? `<p class="muted">${h(story.assessmentReason)}</p>` : ''}</section>
    <section class="card"><h2>Task breakdown</h2><p>${result.status === 'stale' ? 'The story or supporting evidence changed. Generate a current breakdown before using its tasks.' : approved ? `Plan approved by ${h(result.approvedBy)}. ${result.taskProgress.filter(task => task.status === 'Complete').length}/${tasks.length} tasks complete.` : tasks.length ? `${tasks.length} proposed tasks. These tasks have not been approved or executed. Review their scope, dependencies and completion criteria, then approve this plan.` : 'Generate concrete tasks for this story, with repository links, dependencies and testable completion criteria.'}</p>
      ${approved ? '<p>Open a ready task and select Start task. CIS chooses models for implementation and secondary review based on complexity, then performs the work. Review the outcome and record completion to unlock dependent tasks.</p>' : tasks.length && result.status === 'current' ? `<button type="button" data-command="approve" ${model.busy ? 'disabled' : ''}>Approve task plan…</button>` : ''}
      <p><strong>${tasks.length ? 'Generated with' : 'Default model'}:</strong> ${h(result.provider && result.model ? `${result.provider} / ${result.model}` : 'Choose a model when generating tasks')}.</p>
      ${model.selection ? `<p><strong>Selected for this attempt:</strong> ${h(model.selection.provider)} / ${h(model.selection.model)}</p>` : ''}
      <p class="muted">Choose a model when generating. A new breakdown replaces the current task proposal only after it passes validation.</p>
      <div class="actions"><button type="button" data-command="generate" ${approved || model.busy || !review && story.conflict || review?.treatment === 'out-of-scope' ? 'disabled' : ''}>${tasks.length ? 'Regenerate with selected model…' : 'Generate task breakdown…'}</button><button type="button" class="secondary" data-command="refresh" ${model.busy ? 'disabled' : ''}>Refresh story</button></div>
      ${approved ? '<p class="muted">The approved plan is preserved during delivery. Generation cannot replace approved work.</p>' : ''}
      ${tasks.map(task => `<article class="card story-task"><h3>${h(task.id)} · ${h(task.title)}</h3><p class="story-prose">${h(task.description)}</p><p><strong>Repositories:</strong> ${h(task.repositoryIds.join(', ') || 'No repositories linked')}</p><p><strong>Dependencies:</strong> ${h(task.dependsOn.join(', ') || 'None')}</p><p><strong>Story criteria covered:</strong> ${task.requirementNumbers.map(number => `<a href="#criterion-${h(number)}">${h(number)}</a>`).join(', ')}</p><h4>Task completion criteria</h4>${textList(task.acceptanceCriteria)}<p>${h(result.taskProgress?.find(progress => progress.id === task.id)?.status || 'Proposed')}</p><button type="button" data-command="task" data-value="${h(task.id)}" ${model.busy ? 'disabled' : ''}>Open task</button></article>`).join('')}</section>
    ${(result.warnings || []).length ? `<details class="card"><summary>Evidence and scope notes</summary>${textList(result.warnings)}</details>` : ''}` : `<section class="card"><p>${model.busy ? 'Loading story definition and acceptance criteria…' : 'The story is unavailable. Refresh to retry.'}</p><button type="button" data-command="refresh" ${model.busy ? 'disabled' : ''}>Refresh story</button></section>`}
    ${model.busy && story ? '<p role="status">Preparing the story view…</p>' : ''}`;
  const script = storyActionScript(scriptNonce);
  return studioDocument(webview, story?.title || 'Story and tasks', body, scriptNonce, script);
}

function storyActionScript(scriptNonce) {
  return `<script nonce="${scriptNonce}">const vscode=acquireVsCodeApi();
    function rememberFeedback(){const form=document.querySelector('[data-feedback-task]');if(form)vscode.postMessage({command:'remember-feedback',value:JSON.stringify({taskId:form.dataset.feedbackTask,answers:Object.fromEntries([...form.querySelectorAll('textarea[data-finding]')].map(input=>[input.dataset.finding,input.value]))})});}
    document.addEventListener('input',event=>{if(event.target.matches('textarea[data-finding]'))rememberFeedback();});
    document.addEventListener('click',event=>{const button=event.target.closest('button[data-command]');if(button&&!button.disabled){rememberFeedback();vscode.postMessage({command:button.dataset.command,value:button.dataset.value});}});</script>`;
}

function renderStoryTask(webview, model, task, scriptNonce) {
  const result = model.result;
  const progress = result.taskProgress?.find(item => item.id === task.id);
  const body = `<header class="hero"><div><p>${h(result.featureTitle)} · ${h(result.story.title)}</p><h1>${h(task.id)} · ${h(task.title)}</h1><p>${h(progress?.status || 'Proposed')}</p></div></header>
    ${model.error ? `<p role="alert">${h(model.error)}</p>` : ''}
    ${model.notice ? `<p role="status">${h(model.notice)}</p>` : ''}
    <div class="actions"><button type="button" data-command="story" ${model.busy ? 'disabled' : ''}>Back to story and plan</button><button type="button" data-command="refresh" ${model.busy ? 'disabled' : ''}>Refresh task</button></div>
    ${progress?.execution ? renderTaskExecution(progress.execution, progress.status, progress.reviewFeedback?.length > 0) : ''}
    ${renderReviewFeedback(model, task, progress)}
    <section class="card"><h2>Work to do</h2><p style="white-space:pre-wrap">${h(task.description)}</p><p><strong>Repositories:</strong> ${h(task.repositoryIds.join(', ') || 'No repositories linked')}</p>
    <h2>Dependencies</h2>${task.dependsOn.length ? task.dependsOn.map(id => `<button type="button" data-command="task" data-value="${h(id)}">${h(id)} · ${h(result.tasks.find(item => item.id === id)?.title || id)}</button>`).join(' ') : '<p>None</p>'}
    <h2>Task completion criteria</h2><ol>${task.acceptanceCriteria.map(criterion => `<li>${h(criterion)}</li>`).join('')}</ol>
    <h2>Story acceptance criteria covered</h2><ol>${task.requirementNumbers.map(number => `<li value="${h(number)}">${h(result.story.requirements[number - 1])}</li>`).join('')}</ol></section>
    <section class="card"><h2>Next step</h2>${progress?.blockedReason ? `<p>${h(progress.blockedReason)}</p>` : ''}
    ${progress?.status === 'Complete' ? `<p>Completed. Recorded verification: ${h(progress.evidence)}</p>` : progress?.canExecute ? progress.execution ? '<p>Inspect the failure or review findings above and save any answers before retrying. CIS checks whether retained implementation can be reused, then reviews it again and corrects actionable findings. You will see the selected models and execution scope before continuing.</p>' : '<p>Start this task to perform the work with automatically selected models. CIS chooses an implementation model for the task complexity and a distinct model for secondary review. You will see both choices and the execution scope before starting.</p>' : progress?.canComplete ? '<p>Review the implementation and secondary review results, then record completion after verifying every criterion.</p>' : ''}
    <div class="actions"><button type="button" data-command="start" data-value="${h(task.id)}" ${model.busy || !progress?.canExecute ? 'disabled' : ''}>${progress?.execution ? 'Retry implementation and review…' : 'Start task'}</button>
    <button type="button" data-command="complete" data-value="${h(task.id)}" ${model.busy || !progress?.canComplete ? 'disabled' : ''}>Record completion…</button></div></section>
    `;
  return studioDocument(webview, task.title, body, scriptNonce, storyActionScript(scriptNonce));
}

function renderReviewFeedback(model, task, progress) {
  const feedback = progress?.reviewFeedback || [];
  if (!feedback.length) return progress?.execution?.findings?.length
    ? '<section class="card"><h2>Review answers are unavailable</h2><p role="alert">The configured CIS executable did not return response fields for these findings. Update CIS and check the executable setting for this workspace; a workspace setting can override the user setting. Then refresh this task.</p><button type="button" data-command="executable-settings">Check CIS executable setting</button></section>' : '';
  const drafts = model.feedbackDrafts?.[task.id] || {};
  const disabled = model.busy || progress.execution?.status === 'running' || progress.execution?.applied || progress.status === 'Complete';
  return `<section class="card" data-feedback-task="${h(task.id)}"><h2>Respond to review findings</h2>
    <p>Add the decisions or information needed to address each finding. Suggested answers are advisory: use or edit them, then save. Saved answers go to both models on retry; they do not close findings, approve changes or expand the task scope.</p>
    ${model.result.planState === 'Needs review' ? '<p>This plan is stale. You can retain answers here, but the plan must be reviewed before further execution.</p>' : ''}
    <button type="button" data-command="suggest-feedback" data-value="${h(task.id)}" ${disabled ? 'disabled' : ''}>Suggest answers with selected model…</button>
    ${feedback.map((item, index) => `<article><h3>Finding ${index + 1}</h3><p style="white-space:pre-wrap">${h(item.finding)}</p>
      ${item.suggestedAnswer ? `<div><h4>Suggested answer</h4><p style="white-space:pre-wrap">${h(item.suggestedAnswer)}</p><p class="muted">Suggested by ${h(item.suggestionModel)}</p><details><summary>Supporting source excerpt</summary><p style="white-space:pre-wrap">${h(item.suggestionReason)}</p></details><button type="button" data-command="use-feedback" data-value="${h(JSON.stringify({ taskId: task.id, id: item.id }))}" ${disabled ? 'disabled' : ''}>Use suggested answer</button></div>` : '<p class="muted">No supported suggestion is currently available. Enter your answer or request suggestions.</p>'}
      <label for="feedback-${h(index)}">Your answer or direction</label><textarea id="feedback-${h(index)}" data-finding="${h(item.id)}" rows="5" maxlength="8000" ${disabled ? 'disabled' : ''}>${h(drafts[item.id] ?? item.answer ?? '')}</textarea>
      ${item.actor ? `<p class="muted">Saved by ${h(item.actor)} · ${h(item.savedAt)}</p>` : '<p class="muted">Not yet saved.</p>'}</article>`).join('')}
    <button type="button" data-command="save-feedback" data-value="${h(task.id)}" ${disabled ? 'disabled' : ''}>Save answers</button><p>After saving, use Retry implementation and review below to apply the direction and check the outcome.</p>
    ${Object.entries(drafts).filter(([id]) => !feedback.some(item => item.id === id)).map(([, text]) => `<details><summary>Retained draft from an earlier review</summary><p>The finding changed. Copy any still-relevant text into a current answer before saving.</p><p style="white-space:pre-wrap">${h(text)}</p></details>`).join('')}</section>`;
}

function renderTaskExecution(execution, taskStatus, hasFeedback) {
  const choice = model => model ? `${model.provider} / ${model.model}` : 'Unavailable';
  const outcomes = { running: 'Implementation and review are running', reviewed: 'Review passed', 'changes-requested': 'Review requires changes', blocked: 'Input is needed before work can continue', failed: 'Execution failed', cancelled: 'Execution cancelled', 'review-failed': 'Review could not finish', stale: 'Task scope changed during execution' };
  const latest = [...new Map((execution.runs || []).filter(run => run.stage === 'Implementation').map(run => [run.repositoryId, run])).values()];
  return `<section class="card"><h2>Work summary</h2><p><strong>Outcome:</strong> ${h(outcomes[execution.status] || execution.status)}</p>
    <p><strong>Complexity:</strong> ${h(execution.selection?.complexity)} · <strong>Implementation:</strong> ${h(choice(execution.selection?.implementation))} · <strong>Review:</strong> ${h(choice(execution.selection?.review))}</p>
    <p>${execution.status === 'running' ? 'This view refreshes automatically while execution is running.' : execution.applied ? taskStatus === 'Complete' ? 'Reviewed changes were applied and completion was recorded.' : 'Reviewed changes were applied. Verify the outcome before recording completion.' : 'Changes have not been applied. The isolated work and review findings are retained below.'}</p>
    ${latest.map(run => `<article><h3>${h(run.repositoryId)}</h3><p style="white-space:pre-wrap">${h(run.summary || 'No work summary was recorded.')}</p></article>`).join('')}
    ${(execution.errors || []).map(error => `<p role="alert">${h(error)}</p>`).join('')}
    ${!hasFeedback && (execution.findings || []).length ? `<h3>Review findings</h3><ul>${execution.findings.map(finding => `<li>${h(finding)}</li>`).join('')}</ul>` : ''}
    ${(execution.runs || []).map(run => `<details><summary>${h(run.stage)} · ${h(run.repositoryId)} · round ${h(run.round || 1)} · ${h(run.status)}</summary><p>${h(run.provider)} / ${h(run.model)}</p><p style="white-space:pre-wrap">${h(run.summary)}</p><h4>Recorded checks</h4><ul>${(run.validations || []).map(validation => `<li>${h(validation)}</li>`).join('')}</ul>${run.changedFiles?.length ? `<h4>Files changed in this working copy</h4><ul>${run.changedFiles.map(file => `<li>${h(file)}</li>`).join('')}</ul>` : ''}<p>Run: ${h(run.runId)}</p><p>Working copy: ${h(run.workingDirectory)}</p></details>`).join('')}
    ${(execution.changedFiles || []).length ? `<h3>${execution.applied ? 'Applied files' : 'Candidate files — not applied'}</h3><ul>${execution.changedFiles.map(file => `<li>${h(file)}</li>`).join('')}</ul>` : ''}</section>`;
}

module.exports = { openFeatureStory, renderFeatureStory, chooseStoryModel };
