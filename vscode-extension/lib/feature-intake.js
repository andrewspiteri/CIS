'use strict';

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { escapeHtml: h, nonce, resolveWithin } = require('./security');
const { createActionPanel, studioDocument } = require('./webview');
const { STEPS, navigation, renderReviewPage, wizardScript, hasUnsavedChanges } = require('./feature-wizard');
const { REVIEW_STYLES } = require('./feature-review-text');
const { STORY_STYLES, moveStory } = require('./feature-stories');
const { reconciledDrafts, deliveryScript, DELIVERY_STYLES } = require('./feature-delivery');
const { gallery, screenScript, exportScreen, SCREEN_STYLES } = require('./feature-screens');
const { architectureGallery, ARCHITECTURE_STYLES } = require('./feature-architecture');

const ACTIONS = new Set(['choose-source', 'choose-repository', 'preview', 'apply', 'edit', 'open-request', 'open-source',
  'navigate', 'remember', 'save-page', 'refresh', 'resume', 'new-feature', 'open-document', 'product-wizard', 'start-approved-feature', 'discard-edits',
  'features-home', 'open-feature', 'add-repository-work', 'remove-repository-work',
  'reimport-source', 'apply-source-update', 'cancel-source-update', 'compare-source', 'open-source-history', 'save-continue', 'use-suggestion',
  'generate-screens', 'open-feature-screen', 'save-feature-screen', 'review-feature-screen',
  'generate-architecture', 'open-feature-diagram', 'save-feature-diagram', 'move-story', 'reconcile-delivery', 'use-delivery-assessment', 'save-delivery-decision', 'open-delivery-evidence', 'discard-delivery-decision']);

function openFeatureIntake(vscode, { cli, authority, root, actorIdentity, refresh, storage, initialSlug, initialPage, createNew = false }) {
  const model = { root, repositories: [], loading: true, busy: false,
    page: 'foundation', pageDrafts: {}, screenDrafts: {}, deliveryDrafts: {}, requests: [],
    draft: { title: '', slug: '', sourcePath: '', repositoryMode: 'new', repositoryPath: '', documentationRoot: 'docs/cis', integrationRepositories: [] } };
  const storageKey = `cis.featureWizard:${process.platform === 'win32' ? root.toLowerCase() : root}`;
  const restored = createNew ? undefined : storage?.get(storageKey);
  if (restored?.version === 1) {
    try { model.draft = { ...parseDraft(JSON.stringify(restored.draft)), actor: restored.draft.actor }; } catch { /* Ignore invalid editor state. */ }
    model.page = STEPS.some(([id]) => id === restored.page) ? restored.page : 'foundation';
    model.selectedSlug = typeof restored.selectedSlug === 'string' ? restored.selectedSlug : undefined;
    model.pageDrafts = restored.pageDrafts && typeof restored.pageDrafts === 'object' ? restored.pageDrafts : {};
    model.screenDrafts = restored.screenDrafts || {};
    model.deliveryDrafts = restored.deliveryDrafts || {};
    model.repositoryWorkDraft = restored.repositoryWorkDraft;
    model.draftSourceHash = restored.sourceHash;
    model.draftQuestions = restored.sourceQuestions;
    model.recoveredSourceDraft = restored.recoveredSourceDraft;
  }
  const scriptNonce = nonce();
  let disposed = false;
  const panel = createActionPanel(vscode, 'cis.featureIntake', 'Feature definition wizard', ACTIONS, action, undefined, { retainContextWhenHidden: true });
  panel.onDidDispose(() => { disposed = true; });
  function render() { if (!disposed) panel.webview.html = renderFeatureIntake(panel.webview, model, scriptNonce); }
  async function persist() {
    try {
      const featureState = { page: model.page, pageDrafts: model.pageDrafts, screenDrafts: model.screenDrafts, deliveryDrafts: model.deliveryDrafts, repositoryWorkDraft: model.repositoryWorkDraft,
        sourceHash: model.draftSourceHash, sourceQuestions: model.draftQuestions, recoveredSourceDraft: model.recoveredSourceDraft };
      await storage?.update(storageKey, { version: 1, draft: model.draft, selectedSlug: model.selectedSlug, ...featureState });
      if (model.selectedSlug) await storage?.update(`${storageKey}:${model.selectedSlug}`, featureState);
    } catch { model.error = 'CIS could not save the editor position. Any answers already saved to the feature request are preserved.'; }
  }
  function assertAuthority() {
    if (vscode.workspace.isTrusted === false) throw new Error('Trust this workspace before adding a feature.');
    if (authority.root() !== root) throw new Error('The selected authority changed. Reopen Add feature for that authority.');
  }
  async function load() {
    try {
      assertAuthority();
      const result = await cli.query(['repo', 'list', '--workspace', root], { repository: false });
      model.repositories = (result.workspace?.repositories || []).filter(item => item.role === 'participant');
      const listed = await cli.query(['brd', 'feature', 'wizard', 'list', '--workspace', root], { repository: false });
      model.requests = listed.requests || [];
      if (initialSlug || model.selectedSlug) await loadWizard(initialSlug || model.selectedSlug);
      if (STEPS.some(([id]) => id === initialPage)) model.page = initialPage;
      await loadScreens();
      await loadArchitecture();
      await loadDelivery();
    } catch (error) { model.error = error.message; }
    finally { model.loading = false; render(); }
  }
  async function loadWizard(selectedSlug) {
    const result = await cli.query(['brd', 'feature', 'wizard', 'status', '--slug', selectedSlug, '--workspace', root],
      { repository: false, acceptStructuredFailure: true });
    if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Feature status could not be read.']).join('\n'));
    if (model.selectedSlug !== selectedSlug) {
      model.featureScreens = undefined;
      model.featureArchitecture = undefined;
      model.featureDelivery = undefined;
      const saved = storage?.get(`${storageKey}:${selectedSlug}`);
      model.pageDrafts = saved?.pageDrafts || {};
      model.screenDrafts = saved?.screenDrafts || {};
      model.deliveryDrafts = saved?.deliveryDrafts || {};
      model.repositoryWorkDraft = saved?.repositoryWorkDraft;
      model.draftSourceHash = saved?.sourceHash;
      model.draftQuestions = saved?.sourceQuestions;
      model.recoveredSourceDraft = saved?.recoveredSourceDraft;
      model.page = STEPS.some(([id]) => id === saved?.page) ? saved.page : result.pages.find(page => !page.complete)?.id || 'review';
    }
    const sourceChanged = model.draftSourceHash ? model.draftSourceHash !== result.plan.sourceHash : result.sourceHistory?.length > 0;
    if (sourceChanged) model.sourceUpdate = undefined;
    if (sourceChanged && (Object.keys(model.pageDrafts).length || Object.keys(model.deliveryDrafts).length || model.repositoryWorkDraft !== undefined)) {
      model.recoveredSourceDraft = { sourceHash: model.draftSourceHash, questions: model.draftQuestions,
        answers: model.pageDrafts, deliveryDrafts: model.deliveryDrafts, repositoryWork: model.repositoryWorkDraft, earlierRecovery: model.recoveredSourceDraft };
      model.pageDrafts = {}; model.deliveryDrafts = {}; model.repositoryWorkDraft = undefined;
      model.notice = 'The BRD was updated outside this tab. Your unsaved edits are preserved below for reference. Review them against the new questions before saving answers.';
    }
    model.draftSourceHash = result.plan.sourceHash; model.draftQuestions = result.plan.openDecisions;
    model.wizard = result; model.selectedSlug = selectedSlug;
    model.changes = (await cli.query(['change', 'list'])).changes || [];
    panel.title = result.plan?.title ? `Feature: ${result.plan.title}` : 'Feature definition wizard';
    model.result = { ...(model.result?.plan?.slug === selectedSlug ? model.result : { status: 'unchanged', applied: false }), plan: result.plan };
  }
  async function savePage(continueAfterSave = false) {
    if (!model.wizard || !model.selectedSlug) return;
    if (model.page === 'delivery' && Object.keys(model.deliveryDrafts).length)
      throw new Error('Save each edited story decision, or discard its draft, before saving the delivery page. Your edits are retained.');
    if (model.page === 'review' && model.wizard.pages.some(page => page.id !== 'review' && hasUnsavedChanges(model, page)))
      throw new Error('Save or discard the unsaved feature-page edits before recording the final review.');
    const actor = model.draft.actor || await actorIdentity(); if (!actor) return false;
    assertAuthority();
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-feature-review-'));
    const input = path.join(directory, 'answers.json');
    try {
      fs.writeFileSync(input, JSON.stringify({ slug: model.selectedSlug, page: model.page,
        answers: model.pageDrafts[model.page] || {}, actor, expectedRevision: model.wizard.revision,
        ...(model.page === 'delivery' && model.repositoryWorkDraft ? { repositoryWork: model.repositoryWorkDraft } : {}) }), { encoding: 'utf8', mode: 0o600 });
      const result = await cli.query(['brd', 'feature', 'wizard', 'save', '--input', input, '--workspace', root],
        { repository: false, acceptStructuredFailure: true });
      if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Save failed.']).join('\n'));
      model.wizard = result;
      delete model.pageDrafts[model.page];
      if (model.page === 'delivery') { model.repositoryWorkDraft = undefined; model.deliveryDrafts = {}; }
      await vscode.commands.executeCommand('cis.refreshFeatures');
      const completedPage = result.pages.find(page => page.id === model.page);
      model.notice = completedPage?.complete ? 'Page saved and reviewed.' : 'Answers saved. Review the remaining questions on this step before continuing.';
      if (continueAfterSave && completedPage?.complete) {
        const next = STEPS[STEPS.findIndex(([id]) => id === model.page) + 1];
        if (next) { model.page = next[0]; model.notice = `${completedPage.title} saved and reviewed. Continue with ${next[1].toLowerCase()}.`; }
      }
      return true;
    } finally { if (fs.existsSync(input)) fs.unlinkSync(input); fs.rmdirSync(directory); }
  }
  async function queryIntake(args) {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-feature-intake-'));
    const input = path.join(directory, 'request.json');
    try {
      fs.writeFileSync(input, JSON.stringify(model.draft), { encoding: 'utf8', mode: 0o600 });
      return await cli.query(['brd', 'feature', 'intake', '--input', input, ...args, '--workspace', root],
        { repository: false, acceptStructuredFailure: true });
    } finally { if (fs.existsSync(input)) fs.unlinkSync(input); fs.rmdirSync(directory); }
  }
  async function loadScreens() {
    if (model.page !== 'experience' || !model.selectedSlug) return;
    const result = await cli.query(['brd', 'feature', 'wizard', 'screens', 'status', '--slug', model.selectedSlug, '--workspace', root],
      { repository: false, acceptStructuredFailure: true, cache: false });
    if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Screen status failed.']).join('\n'));
    model.featureScreens = result;
  }
  async function loadArchitecture() {
    if (model.page !== 'architecture' || !model.selectedSlug) return;
    const result = await cli.query(['brd', 'feature', 'wizard', 'architecture', 'status', '--slug', model.selectedSlug, '--workspace', root],
      { repository: false, acceptStructuredFailure: true, cache: false });
    if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Diagram status failed.']).join('\n'));
    model.featureArchitecture = result;
  }
  async function loadDelivery() {
    if (model.page !== 'delivery' || !model.selectedSlug) return;
    const result = await cli.query(['brd', 'feature', 'wizard', 'delivery', 'status', '--slug', model.selectedSlug, '--workspace', root],
      { repository: false, acceptStructuredFailure: true, cache: false });
    if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Delivery assessment could not be read.']).join('\n'));
    model.featureDelivery = result;
  }
  async function saveDiagram(diagram) {
    assertAuthority();
    const target = await vscode.window.showSaveDialog({ defaultUri: vscode.Uri.file(path.join(root, `${model.selectedSlug}-${diagram.id}.svg`)), filters: { 'SVG diagram': ['svg'] } });
    if (target) { assertAuthority(); await vscode.workspace.fs.writeFile(target, Buffer.from(diagram.svg, 'utf8')); }
  }
  async function saveScreen(result, value) {
    assertAuthority();
    const image = exportScreen(result, value);
    const target = await vscode.window.showSaveDialog({ defaultUri: vscode.Uri.file(path.join(root, image.filename)), filters: { 'JPG image': ['jpg'] } });
    if (target) { assertAuthority(); await vscode.workspace.fs.writeFile(target, image.bytes); }
  }
  async function reviewScreen(request) {
    if (model.page !== 'experience' || !model.wizard) throw new Error('Open the experience step to review screens.');
    // Review the checked preview independently of questionnaire drafts. The CLI
    // verifies its image/context revision; this action never saves page answers.
    const screen = model.featureScreens?.screens.find(screen => screen.plan.id === request.screenId);
    const review = model.featureScreens?.reviews?.find(review => review.key === request.screenId && review.decision === 'not-needed');
    if (!screen && !review) throw new Error('This screen is no longer available. Refresh the gallery.');
    const key = screen?.key || review.key;
    const actor = model.draft.actor || await actorIdentity(); if (!actor) return;
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-screen-review-'));
    const input = path.join(directory, 'review.json');
    try {
      assertAuthority();
      fs.writeFileSync(input, JSON.stringify({ slug: model.selectedSlug, page: 'experience', answers: {}, actor,
        expectedRevision: model.wizard.revision, screenReview: request }), { encoding: 'utf8', mode: 0o600 });
      const result = await cli.query(['brd', 'feature', 'wizard', 'save', '--input', input, '--workspace', root],
        { repository: false, acceptStructuredFailure: true });
      if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Screen review could not be saved.']).join('\n'));
      model.wizard = result;
      try { await loadScreens(); }
      catch (error) { throw new Error(`Your screen review was saved. Refresh the previews to reload it. ${error.message}`); }
      delete model.screenDrafts[key];
      model.notice = request.decision === 'not-needed' ? 'Screen marked as not needed. It stays excluded until you include it again.' : 'Screen feedback saved.';
      render();
      await vscode.commands.executeCommand('cis.refreshFeatures');
      if (request.decision !== 'not-needed') {
        try {
          await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Applying changes to the selected screen', cancellable: false }, async () => {
            assertAuthority();
            const updated = await cli.query(['brd', 'feature', 'wizard', 'screens', 'prepare', '--slug', model.selectedSlug,
              '--expected-revision', model.wizard.revision, ...(screen ? ['--screen', key] : []), '--workspace', root],
            { repository: false, acceptStructuredFailure: true, cache: false, timeout: 240_000 });
            if (updated.errors?.length || updated._process?.failed) throw new Error((updated.errors || ['Screen amendment failed.']).join('\n'));
            model.featureScreens = updated;
            model.notice = 'Screen updated. Review the new image and request further changes if needed.';
          });
        } catch (error) { throw new Error(`Your feedback was saved; the previous image is preserved. ${error.message}`); }
      }
    } finally { if (fs.existsSync(input)) fs.unlinkSync(input); fs.rmdirSync(directory); }
  }
  async function open(relative) {
    const absolute = resolveWithin(root, relative);
    if (!absolute || !fs.existsSync(absolute)) throw new Error('The feature document is no longer available.');
    await vscode.commands.executeCommand('markdown.showPreviewToSide', vscode.Uri.file(absolute));
  }
  function requireSavedAnswers() {
    const unsaved = model.wizard?.pages.filter(page => hasUnsavedChanges(model, page)) || [];
    if (unsaved.length) throw new Error(`Save or discard your unsaved edits in ${unsaved.map(page => page.title).join(', ')} before reimporting the BRD. Your edits are still in the form.`);
  }
  async function querySourceUpdate(source, actor, args) {
    return cli.query(['brd', 'feature', 'wizard', 'reimport', '--slug', model.selectedSlug,
      '--source', source, '--actor', actor, ...args, '--workspace', root],
    { repository: false, acceptStructuredFailure: true, cache: false });
  }
  async function action(command, value) {
    if (model.busy || model.loading) return;
    if (command === 'save-feature-screen') {
      try { await saveScreen(model.featureScreens, value); } catch (error) { model.error = error.message; render(); }
      return;
    }
    let message;
    if (['navigate', 'remember', 'save-page', 'refresh', 'resume', 'new-feature', 'open-document', 'product-wizard', 'start-approved-feature', 'discard-edits',
      'features-home', 'open-feature', 'add-repository-work', 'remove-repository-work',
      'reimport-source', 'apply-source-update', 'cancel-source-update', 'compare-source', 'open-source-history', 'save-continue', 'use-suggestion', 'generate-screens', 'open-feature-screen', 'review-feature-screen',
      'generate-architecture', 'open-feature-diagram', 'save-feature-diagram', 'move-story', 'reconcile-delivery', 'use-delivery-assessment', 'save-delivery-decision', 'open-delivery-evidence', 'discard-delivery-decision'].includes(command)
      || ['open-source', 'open-request'].includes(command) && value?.startsWith('{')) {
      try {
        if (typeof value !== 'string' || value.length > 1_048_576) throw new Error('Feature wizard input is too large.');
        message = JSON.parse(value);
        if (!message || typeof message !== 'object') throw new Error('Feature wizard input is invalid.');
        if (message.page && message.page !== model.page) return;
        if (message.answers) {
          if (typeof message.answers !== 'object' || Array.isArray(message.answers)
            || Object.values(message.answers).some(answer => typeof answer !== 'string' || answer.length > 24_000)) throw new Error('Feature answers are invalid.');
          model.pageDrafts[model.page] = message.answers;
        }
        if (message.screenDrafts) {
          if (typeof message.screenDrafts !== 'object' || Array.isArray(message.screenDrafts) || Object.keys(message.screenDrafts).length > 200
            || Object.values(message.screenDrafts).some(value => typeof value !== 'string' || value.length > 2000)) throw new Error('Screen feedback is invalid.');
          model.screenDrafts = { ...model.screenDrafts, ...message.screenDrafts };
        }
        if (message.deliveryDrafts) {
          if (model.page !== 'delivery' || typeof message.deliveryDrafts !== 'object' || Array.isArray(message.deliveryDrafts)
            || Object.keys(message.deliveryDrafts).length > 80 || Object.values(message.deliveryDrafts).some(d => !d || typeof d.plan !== 'string' || d.plan.length > 4000
              || typeof d.evidencePaths !== 'string' || d.evidencePaths.length > 7200 || !Array.isArray(d.owners) || d.owners.length > 8
              || d.owners.some(id => typeof id !== 'string' || id.length > 200) || !['', 'new', 'extend', 'reuse', 'out-of-scope'].includes(d.treatment)))
            throw new Error('Story decision inputs are invalid or too large.');
          model.deliveryDrafts = { ...model.deliveryDrafts, ...message.deliveryDrafts };
        }
        if (message.repositoryWork !== undefined && model.page === 'delivery') {
          if (!Array.isArray(message.repositoryWork) || message.repositoryWork.length > 100) throw new Error('Repository work is invalid.');
          model.repositoryWorkDraft = message.repositoryWork;
        }
        if (message.draft && !model.result) model.draft = { ...parseDraft(JSON.stringify(message.draft)), actor: model.draft.actor };
      } catch (error) { model.error = error.message; render(); return; }
      if (command === 'remember') { await persist(); return; }
    }
    model.busy = true; model.error = undefined;
    model.notice = undefined;
    model.storyMoveFocus = undefined;
    model.deliveryFocus = undefined;
    try {
      assertAuthority();
      if (['choose-source', 'choose-repository', 'preview'].includes(command)) {
        if (model.result?.applied || model.result?.status === 'unchanged') return;
        const draft = parseDraft(value);
        model.draft = { ...draft, actor: model.draft.actor };
        model.plan = undefined;
      }
      render();
      if (command === 'reimport-source') {
        if (!model.wizard) throw new Error('Open an existing feature first.');
        requireSavedAnswers();
        const picked = await vscode.window.showOpenDialog({ canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
          filters: { 'Markdown requirements': ['md'] }, title: 'Reimport updated feature BRD' });
        if (!picked?.[0]) return;
        const actor = await actorIdentity(); if (!actor) return;
        assertAuthority();
        model.sourceUpdate = undefined;
        const result = await querySourceUpdate(picked[0].fsPath, actor, ['--dry-run']);
        if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['BRD preview failed.']).join('\n'));
        if (result.status === 'unchanged') model.notice = 'This BRD is identical to the current source. Your feature review is unchanged.';
        else model.sourceUpdate = { plan: result.plan, actor };
      } else if (command === 'cancel-source-update') model.sourceUpdate = undefined;
      else if (command === 'compare-source') {
        const plan = model.sourceUpdate?.plan;
        if (!plan) return;
        const previous = resolveWithin(root, plan.previousSourcePath);
        if (!previous || !fs.existsSync(previous) || !fs.existsSync(plan.incomingPath)) throw new Error('A comparison document is no longer available. Choose the updated BRD again.');
        await vscode.commands.executeCommand('vscode.diff', vscode.Uri.file(previous), vscode.Uri.file(plan.incomingPath),
          `${plan.title}: current BRD ↔ updated BRD`, { preview: false });
      } else if (command === 'open-source-history') {
        if (!model.wizard?.sourceHistory?.some(document => document.path === message.target && document.exists)) throw new Error('This source revision is unavailable.');
        await open(message.target);
      } else if (command === 'apply-source-update') {
        const update = model.sourceUpdate; if (!update) return;
        requireSavedAnswers();
        const result = await querySourceUpdate(update.plan.incomingPath, update.actor, ['--yes', '--expected-plan', update.plan.planHash]);
        if (result.errors?.length || result._process?.failed) {
          model.sourceUpdate = undefined;
          throw new Error((result.errors || ['Reimport failed. Choose the updated BRD to preview again.']).join('\n'));
        }
        model.sourceUpdate = undefined;
        if (result.applied) {
          model.pageDrafts = {}; model.repositoryWorkDraft = undefined;
          model.notice = 'Updated BRD imported. Saved answers and repository work are retained. Review the feature pages against the new source; earlier BRDs and answers are available in revision history.';
          try {
            await loadWizard(model.selectedSlug);
            model.page = 'business';
            model.requests = model.requests.map(request => request.slug === model.selectedSlug ? model.wizard.plan : request);
            await vscode.commands.executeCommand('cis.refreshFeatures');
          } catch (error) { model.error = `The BRD was reimported. Refresh this feature to load its new state: ${error.message}`; }
        } else model.notice = 'This BRD is already the current source. The feature was not changed.';
      } else if (command === 'navigate') {
        if (!STEPS.some(([id]) => id === message.target)) throw new Error('Unknown feature step.');
        if (message.target !== 'foundation' && !model.wizard) throw new Error('Create the feature foundation first, then continue through its definition.');
        model.page = message.target;
      } else if (command === 'resume') {
        if (!model.requests.some(request => request.slug === message.target)) throw new Error('This saved feature is unavailable.');
        await loadWizard(message.target);
      } else if (command === 'features-home') await vscode.commands.executeCommand('cis.features.focus');
      else if (command === 'open-feature') {
        if (!model.requests.some(request => request.slug === message.target)) throw new Error('This saved feature is unavailable.');
        await persist();
        await vscode.commands.executeCommand('cis.featureWizard', { root, slug: message.target });
      } else if (command === 'add-repository-work') {
        const work = model.repositoryWorkDraft ||= structuredClone(model.wizard.repositoryWork || []);
        if (work.length >= 100) throw new Error('A feature can contain at most 100 repository features.');
        let ordinal = 1; while (work.some(item => item.id === `RW-${String(ordinal).padStart(3, '0')}`)) ordinal++;
        const repository = model.wizard.repositories?.find(repo => repo.repositoryPath === model.wizard.plan.repositoryPath)
          || model.repositories.find(repo => repo.participation === 'owned');
        work.push({ id: `RW-${String(ordinal).padStart(3, '0')}`, repositoryId: repository?.id || '', title: '', scope: '', dependsOn: [], changeIds: [] });
      } else if (command === 'remove-repository-work') {
        const work = model.repositoryWorkDraft || model.wizard.repositoryWork || [];
        if (work.some(item => item.dependsOn.includes(message.target))) throw new Error('Remove dependencies on this item before removing it.');
        model.repositoryWorkDraft = work.filter(item => item.id !== message.target);
      } else if (command === 'new-feature') {
        await persist();
        await vscode.commands.executeCommand('cis.featureAdd');
      } else if (command === 'discard-edits') {
        delete model.pageDrafts[model.page];
        if (model.page === 'delivery') { model.repositoryWorkDraft = undefined; model.deliveryDrafts = {}; }
      }
      else if (command === 'discard-delivery-decision') {
        delete model.deliveryDrafts[message.target];
      }
      else if (command === 'open-delivery-evidence') {
        const evidence = model.featureDelivery?.evidence?.find(e => e.id === message.target);
        const repository = model.wizard.repositories?.find(r => r.id === evidence?.repositoryId && r.role === 'participant' && r.participation === 'owned');
        const target = repository && resolveWithin(repository.repositoryPath || repository.path, evidence.path);
        if (!target || !fs.existsSync(target)) throw new Error('The code reference is unavailable. Refresh its evidence.');
        await vscode.window.showTextDocument(vscode.Uri.file(target), { preview: false });
      }
      else if (command === 'save-delivery-decision') {
        const story = model.featureDelivery?.stories?.find(s => s.id === message.target);
        const draft = model.deliveryDrafts[message.target];
        if (!story || !draft || !model.featureDelivery.inputHash) throw new Error('Open the story assessment before saving a delivery decision.');
        model.deliveryFocus = story.id;
        if (!draft.treatment) throw new Error('Choose a planned treatment before saving this story decision. Your repository selections and other edits are retained.');
        const savedOwnership = model.wizard.pages.find(p => p.id === 'delivery')?.fields.find(f => f.id === 'delivery-ownership')?.answer;
        const ownership = model.pageDrafts.delivery?.['delivery-ownership'];
        if (ownership !== undefined && ownership !== (savedOwnership ?? '')) throw new Error('Reconcile with the edited ownership direction before saving story decisions. Other drafts are retained.');
        const actor = model.draft.actor || await actorIdentity(); if (!actor) return;
        const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-delivery-decision-'));
        const input = path.join(directory, 'decision.json');
        try {
          assertAuthority();
          fs.writeFileSync(input, JSON.stringify({slug:model.selectedSlug,page:'delivery',answers:{},actor,expectedRevision:model.wizard.revision,
            deliveryReview:{storyId:story.id,treatment:draft.treatment,owners:draft.owners,plan:draft.plan,
              evidencePaths:draft.evidencePaths.split(/\r?\n/u).map(value=>value.trim()).filter(Boolean),expectedInputHash:model.featureDelivery.inputHash}}), {encoding:'utf8',mode:0o600});
          const saved = await cli.query(['brd','feature','wizard','save','--input',input,'--workspace',root], {repository:false,acceptStructuredFailure:true});
          if (saved.errors?.length || saved._process?.failed) throw new Error((saved.errors || ['Story decision could not be saved.']).join('\n'));
          model.wizard = saved;
          delete model.deliveryDrafts[story.id];
          await loadDelivery();
          model.deliveryFocus = story.id;
          model.notice = `${story.title}: planning decision saved. The implementation is not marked complete. Use reconciled stories as draft to include it in the story lists.`;
        } finally { if (fs.existsSync(input)) fs.unlinkSync(input); fs.rmdirSync(directory); }
      }
      else if (command === 'reconcile-delivery') {
        if (model.page !== 'delivery' || !model.wizard) throw new Error('Open Delivery and acceptance first.');
        if (model.featureDelivery) model.featureDelivery = { ...model.featureDelivery, status: 'stale' };
        render();
        const ownership = model.pageDrafts.delivery?.['delivery-ownership'];
        const current = model.wizard.pages.find(p => p.id === 'delivery')?.fields.find(f => f.id === 'delivery-ownership')?.answer;
        if (ownership !== undefined && ownership !== current) {
          const actor = model.draft.actor || await actorIdentity(); if (!actor) return;
          const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-feature-ownership-'));
          const input = path.join(directory, 'answer.json');
          try {
            fs.writeFileSync(input, JSON.stringify({ slug: model.selectedSlug, page: 'delivery',
              answers: { 'delivery-ownership': ownership }, actor, expectedRevision: model.wizard.revision }), { encoding: 'utf8', mode: 0o600 });
            const saved = await cli.query(['brd', 'feature', 'wizard', 'save', '--input', input, '--workspace', root], { repository: false, acceptStructuredFailure: true });
            if (saved.errors?.length || saved._process?.failed) throw new Error((saved.errors || ['Ownership direction could not be saved.']).join('\n'));
            model.wizard = saved;
          } finally { if (fs.existsSync(input)) fs.unlinkSync(input); fs.rmdirSync(directory); }
        }
        await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Reconciling delivery with existing implementation', cancellable: false }, async () => {
          assertAuthority();
          const result = await cli.query(['brd', 'feature', 'wizard', 'delivery', 'prepare', '--slug', model.selectedSlug,
            '--expected-revision', model.wizard.revision, '--workspace', root], { repository: false, acceptStructuredFailure: true, cache: false, timeout: 900_000 });
          if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Delivery reconciliation failed.']).join('\n'));
          model.featureDelivery = result;
          model.notice = 'Review existing capabilities, remaining work and scope conflicts. Use the reconciled stories as a draft when ready.';
        });
      }
      else if (command === 'use-delivery-assessment') {
        const ownership = model.pageDrafts.delivery?.['delivery-ownership'];
        const savedOwnership = model.wizard.pages.find(p => p.id === 'delivery')?.fields.find(f => f.id === 'delivery-ownership')?.answer;
        if (ownership !== undefined && ownership !== savedOwnership) throw new Error('Reconcile again with the edited ownership direction before using these stories.');
        await loadDelivery();
        const drafts = reconciledDrafts(model);
        if (hasUnsavedChanges(model, model.wizard.pages.find(p => p.id === 'delivery'))) {
          const choice = await vscode.window.showWarningMessage('Replace the three story lists in this form with the reconciled drafts? Other answers and repository work are kept.', { modal: true }, 'Replace story drafts');
          if (choice !== 'Replace story drafts') return;
        }
        model.pageDrafts.delivery = drafts;
        model.notice = 'Reconciled stories added to the form. Review conflicts and save the delivery page to keep the changes.';
      }
      else if (command === 'generate-architecture') {
        if (model.page !== 'architecture' || !model.wizard) throw new Error('Open the architecture step before generating diagrams.');
        if (!await savePage()) return;
        if (model.featureArchitecture?.diagrams?.length) model.featureArchitecture = { ...model.featureArchitecture, status: 'stale' };
        await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Generating feature C4 diagrams with the local model', cancellable: false }, async () => {
          assertAuthority();
          const result = await cli.query(['brd', 'feature', 'wizard', 'architecture', 'prepare', '--slug', model.selectedSlug,
            '--expected-revision', model.wizard.revision, '--workspace', root],
          { repository: false, acceptStructuredFailure: true, cache: false, timeout: 180_000 });
          if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Architecture generation failed.']).join('\n'));
          model.featureArchitecture = result;
          model.notice = result.cached ? 'The C4 diagrams already match the saved direction.' : 'Feature C4 diagrams generated. Review the proposed boundaries and relationships below.';
        });
      }
      else if (command === 'open-feature-diagram' || command === 'save-feature-diagram') {
        const diagram = model.featureArchitecture?.diagrams?.find(item => item.id === message.target);
        if (!diagram) throw new Error('This diagram is no longer available. Refresh the architecture step.');
        if (command === 'save-feature-diagram') await saveDiagram(diagram);
        else {
          const preview = createActionPanel(vscode, 'cis.featureDiagram', diagram.title, new Set(['save-feature-diagram']),
            async () => { try { await saveDiagram(diagram); } catch (error) { await vscode.window.showErrorMessage(error.message); } });
          const n = nonce();
          preview.webview.html = studioDocument(preview.webview, diagram.title,
            `<style nonce="${n}">${ARCHITECTURE_STYLES}</style>${architectureGallery({ ...model.featureArchitecture, diagrams: [diagram] }, { standalone: true })}`, n);
        }
      }
      else if (command === 'review-feature-screen') await reviewScreen(JSON.parse(message.target));
      else if (command === 'generate-screens') {
        if (model.page !== 'experience' || !model.wizard) throw new Error('Open the experience step before generating screens.');
        if (!await savePage()) return;
        if (model.featureScreens?.screens?.length) model.featureScreens = { ...model.featureScreens, status: 'stale' };
        await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Generating proposed feature screens with the local model', cancellable: false }, async () => {
          assertAuthority();
          const result = await cli.query(['brd', 'feature', 'wizard', 'screens', 'prepare', '--slug', model.selectedSlug,
            '--expected-revision', model.wizard.revision, '--workspace', root],
          { repository: false, acceptStructuredFailure: true, cache: false, timeout: 240_000 });
          if (result.errors?.length || result._process?.failed) throw new Error((result.errors || ['Screen generation failed.']).join('\n'));
          model.featureScreens = result;
          model.notice = result.cached ? 'The screens already match the saved direction.' : 'Proposed screens generated. Review the layouts, actions and states below.';
        });
      }
      else if (command === 'open-feature-screen') {
        const index = Number(message.target), screen = model.featureScreens?.screens?.[index];
        if (!Number.isSafeInteger(index) || !screen) throw new Error('This screen is no longer available.');
        const snapshot = { ...model.featureScreens, screens: [screen] };
        const preview = createActionPanel(vscode, 'cis.featureScreen', `Proposed: ${screen.plan.title}`, new Set(['save-feature-screen', 'return-to-feature']),
          async (command, value) => { try { if (command === 'return-to-feature') panel.reveal(); else await saveScreen(snapshot, value); } catch (error) { await vscode.window.showErrorMessage(error.message); } });
        const n = nonce();
        preview.webview.html = studioDocument(preview.webview, screen.plan.title,
          `<style nonce="${n}">${SCREEN_STYLES}</style>${gallery(snapshot, { standalone: true })}`, n, `<script nonce="${n}">const vscode=acquireVsCodeApi();${screenScript()}</script>`);
      }
      else if (command === 'move-story') {
        const delivery = model.wizard?.pages.find(page => page.id === 'delivery');
        if (model.page !== 'delivery' || !delivery) throw new Error('Open Delivery and acceptance before moving a story.');
        const answers = Object.fromEntries(delivery.fields.map(field => [field.id,
          model.pageDrafts.delivery?.[field.id] ?? field.answer ?? field.suggestedAnswer ?? '']));
        const moved = moveStory(answers, JSON.parse(message.target));
        model.pageDrafts.delivery = { ...answers, ...moved.fields };
        model.storyMoveFocus = moved.focus;
        model.notice = `Moved “${moved.title}” to ${moved.category}. Save this page to keep the change.`;
      }
      else if (command === 'save-page' || command === 'save-continue') await savePage(command === 'save-continue');
      else if (command === 'use-suggestion') {
        const field = model.wizard?.pages.find(page => page.id === model.page)?.fields.find(field => field.id === message.target);
        if (!field?.suggestedAnswer) throw new Error('No current suggestion is available for this question.');
        model.pageDrafts[model.page] ||= {};
        model.pageDrafts[model.page][field.id] = field.suggestedAnswer;
        model.notice = 'Current context copied into your draft. Review it before saving.';
      }
      else if (command === 'refresh') await loadWizard(model.selectedSlug);
      else if (command === 'open-document') {
        if (!model.wizard?.pages.some(page => page.documents.some(document => document.path === message.target && document.exists))) throw new Error('The selected baseline document is unavailable.');
        await open(message.target);
      } else if (command === 'product-wizard') await vscode.commands.executeCommand('cis.definitionWizard');
      else if (command === 'start-approved-feature') {
        if (!model.wizard?.reviewed) throw new Error('Complete the feature-definition review first.');
        const backlog = await cli.query(['brd', 'backlog', 'status', '--workspace', root], { repository: false, acceptStructuredFailure: true });
        if (backlog.validation?.effectiveStatus !== 'Active' || backlog.validation?.current !== true || !backlog.items?.length)
          throw new Error('There is no Active, current backlog outcome to continue. Open the product wizard and reconcile the proposed feature scope into its delivery map.');
        const selected = await vscode.window.showQuickPick(backlog.items.map(item => ({ label: item.id, description: item.outcome, item })), { placeHolder: 'Select the approved outcome for this feature' });
        if (selected) { assertAuthority(); await vscode.commands.executeCommand(selected.item.featureSpecification === 'not-created' ? 'cis.featureStart' : 'cis.featureAgentDraft', selected.item.id); }
      } else if (command === 'choose-source') {
        const picked = await vscode.window.showOpenDialog({ canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
          filters: { 'Markdown requirements': ['md'] }, title: 'Select the prepared feature BRD' });
        assertAuthority();
        if (picked?.[0]) {
          model.draft.sourcePath = picked[0].fsPath;
          if (!model.draft.title) {
            const title = path.basename(picked[0].fsPath, '.md').replace(/[_-]+/gu, ' ');
            model.draft.title = title.slice(0, 200);
            model.draft.slug = slug(title);
          }
        }
      } else if (command === 'choose-repository') {
        if (model.draft.repositoryMode === 'new') {
          const parent = await vscode.window.showOpenDialog({ canSelectFiles: false, canSelectFolders: true, canSelectMany: false,
            defaultUri: vscode.Uri.file(path.dirname(root)), title: 'Choose the parent folder for the new repository' });
          if (!parent?.[0]) return;
          const name = await vscode.window.showInputBox({ prompt: 'New repository folder name', value: model.draft.slug || 'new-feature',
            validateInput: text => /^[a-zA-Z0-9][a-zA-Z0-9._-]*$/u.test(text) && !['.', '..'].includes(text) ? undefined : 'Use a single folder name.' });
          assertAuthority();
          if (name) model.draft.repositoryPath = path.join(parent[0].fsPath, name);
        } else {
          const picked = await vscode.window.showOpenDialog({ canSelectFiles: false, canSelectFolders: true, canSelectMany: false,
            defaultUri: vscode.Uri.file(path.dirname(root)), title: 'Select the feature implementation repository' });
          assertAuthority();
          if (picked?.[0]) model.draft.repositoryPath = picked[0].fsPath;
        }
      } else if (command === 'preview') {
        model.draft.slug ||= slug(model.draft.title);
        model.draft.actor ||= await actorIdentity();
        if (!model.draft.actor) return;
        assertAuthority();
        const preview = await queryIntake(['--dry-run']);
        assertAuthority();
        if (preview.errors?.length || preview._process?.failed) throw new Error((preview.errors || ['Preview failed.']).join('\n'));
        if (preview.status === 'unchanged') { model.result = preview; await loadWizard(preview.plan.slug); }
        model.plan = preview.plan; model.warnings = preview.warnings;
      } else if (command === 'edit') {
        if (!model.result) model.plan = undefined;
      } else if (command === 'apply') {
        if (!model.plan || model.result) return;
        await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Creating feature request', cancellable: false }, async progress => {
          assertAuthority();
          const result = await queryIntake(['--yes', '--expected-plan', model.plan.planHash]);
          if (result.errors?.length || result._process?.failed) {
            model.plan = undefined;
            throw new Error((result.errors || ['Feature setup failed.']).join('\n'));
          }
          model.result = result; model.warnings = result.warnings;
          if (authority.root() !== root) {
            model.warnings = [...(model.warnings || []), 'The feature request was created in the original authority. Reselect that authority to review it.'];
            return;
          }
          progress.report({ message: 'Updating repository context' });
          try {
            await cli.query(['graph', 'build', '--workspace', root], { repository: false, timeout: 600_000 });
          } catch (error) { model.warnings = [...(model.warnings || []), `The feature request was created. Its context refresh needs attention: ${error.message}`]; }
          if (authority.root() === root) {
            try { await refresh(false, true); }
            catch (error) { model.warnings = [...(model.warnings || []), `The feature request was created. Refresh the workspace to reload its state: ${error.message}`]; }
          }
          await loadWizard(result.plan.slug); model.page = 'business';
          if (!model.requests.some(request => request.slug === result.plan.slug)) model.requests.push(result.plan);
        });
      } else if (command === 'open-request' && model.result?.plan) await open(model.result.plan.requestPath);
      else if (command === 'open-source' && model.result?.plan) await open(model.result.plan.sourcePath);
      if (['navigate', 'refresh', 'resume', 'open-feature', 'save-page', 'save-continue', 'apply-source-update'].includes(command)) { await loadScreens(); await loadArchitecture(); await loadDelivery(); }
    } catch (error) { model.error = error.message; }
    finally { model.busy = false; await persist(); render(); }
  }
  render();
  return { panel, model, ready: load(), action, reveal(page) {
    panel.reveal();
    if (STEPS.some(([id]) => id === page)) void panel.webview.postMessage?.({ type: 'feature-navigate', page });
  } };
}

function slug(value) { return String(value).toLowerCase().replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '').slice(0, 80).replace(/-$/u, ''); }

function parseDraft(value) {
  if (typeof value !== 'string' || value.length > 20000) throw new Error('The feature form is invalid.');
  const draft = JSON.parse(value);
  if (!draft || typeof draft !== 'object' || Array.isArray(draft)) throw new Error('The feature form is invalid.');
  const result = {};
  for (const key of ['title', 'slug', 'sourcePath', 'repositoryMode', 'repositoryPath', 'documentationRoot']) {
    if (typeof draft[key] !== 'string' || draft[key].length > 4096 || /[\0\r\n]/u.test(draft[key])) throw new Error('A feature field is invalid.');
    result[key] = draft[key].trim();
  }
  if (!Array.isArray(draft.integrationRepositories) || draft.integrationRepositories.length > 20
      || draft.integrationRepositories.some(id => typeof id !== 'string' || id.length > 200 || /[\0\r\n]/u.test(id))) throw new Error('Integration selection is invalid.');
  result.integrationRepositories = [...new Set(draft.integrationRepositories)];
  return result;
}

function renderFeatureIntake(webview, model, scriptNonce) {
  const d = model.draft;
  const field = (key, label, extra = '') => `<label for="${key}">${label}</label><input id="${key}" name="${key}" value="${h(d[key])}" ${extra}>`;
  const button = (action, text, secondary = false) => `<button type="button" ${secondary ? 'class="secondary"' : ''} data-feature-action="${action}" ${model.busy ? 'disabled' : ''}>${text}</button>`;
  const p = model.result?.plan || model.plan;
  const foundation = `
    ${model.error ? `<div class="notice warning" role="alert">${h(model.error)}</div>` : ''}
    ${(model.warnings || []).map(w => `<p class="notice warning">${h(w)}</p>`).join('')}
    ${model.loading ? '<p role="status">Loading product repositories…</p>' : p ? `
      <section class="card"><span class="eyebrow">${model.result ? 'Draft feature request created' : 'Review setup'}</span><h2>${h(p.title)}</h2>
      <p><strong>${p.repositoryMode === 'new' ? 'New local Git repository' : 'Existing repository'}:</strong> ${h(p.repositoryPath)}</p>
      <p><strong>Documentation:</strong> ${h(p.documentationRoot)}</p><p><strong>Integration targets:</strong> ${h(p.integrationRepositories.join(', ') || 'None selected')}</p>
      <p>The prepared BRD will be retained unchanged under the product authority. This creates a draft request and connects its repository; scope and implementation approval remain separate review steps.</p>
      <div class="actions">${model.result ? '<button data-command="open-request">Review feature request</button><button class="secondary" data-command="open-source">Open original BRD</button>' : button('apply', 'Create feature request') + button('edit', 'Back to details', true)}</div></section>
      <section class="card"><h2>${p.openDecisions.length} open decisions found in the BRD</h2><p>These remain unresolved. Review them before approving the feature scope.</p>
      ${p.openDecisions.length ? `<ol>${p.openDecisions.map(q => `<li>${h(q)}</li>`).join('')}</ol>` : '<p>No numbered decision section was detected. The source may still contain assumptions to review.</p>'}</section>
      ${model.result ? '<section class="card"><h2>Continue defining the feature</h2><p>Review business decisions, technical direction, architecture, integrations, experience and delivery in the steps on the left.</p><button type="button" data-wizard-action="navigate" data-value="business">Continue to business definition →</button></section>' : ''}
    ` : `<form id="feature-form" class="card source-review"><fieldset ${model.busy ? 'disabled' : ''}><legend>Feature details</legend>
      ${field('title', 'Feature name', 'maxlength="200" required')}${field('slug', 'Feature identifier', 'maxlength="80" placeholder="referrals"')}
      ${field('sourcePath', 'Prepared BRD (Markdown)', 'readonly required')}${button('choose-source', 'Choose BRD', true)}
      <label for="repositoryMode">Implementation repository</label><select id="repositoryMode" name="repositoryMode"><option value="new" ${d.repositoryMode === 'new' ? 'selected' : ''}>Create a new repository</option><option value="existing" ${d.repositoryMode === 'existing' ? 'selected' : ''}>Use an existing repository</option></select>
      ${field('repositoryPath', 'Repository folder', 'required')}${button('choose-repository', 'Choose repository location', true)}
      ${field('documentationRoot', 'Repository documentation folder', 'required')}
      <h3>Integrates with</h3><p>Select the existing repositories whose contracts or behaviour this feature depends on.</p>
      ${model.repositories.length ? model.repositories.map(repo => `<label class="source-choice"><input type="checkbox" name="integrationRepositories" value="${h(repo.id)}" ${d.integrationRepositories.includes(repo.id) ? 'checked' : ''}><span>${h(repo.id)}<small>${repo.participation === 'dependency' ? 'External dependency · context only' : 'Owned by this product'}</small></span></label>`).join('') : '<p>No participant repositories are registered yet.</p>'}
      <div class="actions"><button type="submit" ${model.busy ? 'disabled' : ''}>Review setup</button></div></fieldset></form>`}
      ${model.busy ? '<p role="status">CIS is preparing this action…</p>' : ''}`;
  const body = `<style nonce="${scriptNonce}">#feature-form fieldset{display:grid;gap:.65rem}#feature-form label:not(.source-choice){font-weight:600;margin-top:.65rem}#feature-form input:not([type=checkbox]),#feature-form select{width:100%;font:inherit;padding:.6rem;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border)}#feature-form button{justify-self:start}#feature-form h3{margin-bottom:0}.feature-questions{grid-template-columns:1fr}.feature-questions label{font-weight:600}#feature-review-form fieldset{min-width:0}#feature-review-form legend{font-weight:600}.feature-page{min-width:0;display:grid;gap:1rem}.wizard-steps{position:sticky;top:1rem;align-self:start}.feature-saved{display:flex;gap:.6rem;flex-wrap:wrap}.repository-work-item{display:grid;grid-template-columns:1fr 1fr;gap:1rem;border:1px solid var(--vscode-panel-border);border-radius:.4rem;padding:1rem;margin:1rem 0}.repository-work-item legend{font-weight:600}.repository-work-item label{display:grid;gap:.4rem;font-weight:600}.repository-work-item label:has(textarea){grid-column:1/-1}.repository-work-item input,.repository-work-item select,.repository-work-item textarea{width:100%;min-width:0;font:inherit;padding:.6rem;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border);border-radius:.25rem}.repository-work-item select[multiple]{min-height:5rem}.repository-work-item button{justify-self:start}@media(max-width:48rem){.wizard-steps{position:static}}@media(max-width:780px){.repository-work-item{grid-template-columns:1fr}}</style>
    <style nonce="${scriptNonce}">${REVIEW_STYLES}${STORY_STYLES}${DELIVERY_STYLES}${SCREEN_STYLES}${ARCHITECTURE_STYLES}</style>
    <header class="hero feature-hero"><div><span class="eyebrow">Feature delivery</span><h1>Feature definition wizard</h1><p>${h(model.wizard?.plan?.title || model.draft.title || 'Define a new feature from its prepared BRD, using the existing product baseline.')}</p><p class="muted">Authority: ${h(model.root)}</p></div>${model.selectedSlug ? '<button type="button" class="secondary" data-wizard-action="new-feature">Add another feature</button>' : ''}</header>
    <div class="actions"><button type="button" class="secondary" data-wizard-action="features-home">All high-level features</button><button type="button" class="secondary" data-wizard-action="product-wizard">Product definition</button>${model.wizard ? `<button type="button" data-wizard-action="reimport-source" ${model.busy ? 'disabled' : ''}>Reimport BRD</button>` : ''}</div>
    ${renderSourceUpdate(model)}
    ${model.recoveredSourceDraft ? `<details class="card source-review"><summary>Recovered unsaved edits from an earlier BRD</summary><p>These edits refer to the questions listed below. Copy any relevant text into the current feature pages after reviewing the new source.</p><textarea readonly rows="12" aria-label="Recovered unsaved edits">${h(JSON.stringify(model.recoveredSourceDraft, null, 2))}</textarea></details>` : ''}
    ${model.wizard?.sourceHistory?.length ? `<details class="card"><summary>Previous BRDs and saved answers</summary><div class="actions">${model.wizard.sourceHistory.map(document => `<button type="button" class="link" data-wizard-action="open-source-history" data-value="${h(document.path)}">${h(document.title)} ↗</button>`).join('')}</div></details>` : ''}
    ${(model.requests || []).length ? `<details class="card"><summary>High-level features (${model.requests.length})</summary><div class="feature-saved">${model.requests.map(request => `<button type="button" class="secondary" data-wizard-action="open-feature" data-value="${h(request.slug)}" ${request.slug === model.selectedSlug ? 'aria-current="true"' : ''}>${h(request.title)}</button>`).join('')}</div></details>` : ''}
    ${model.notice ? `<p class="notice" role="status">${h(model.notice)}</p>` : ''}
    ${model.busy ? '<p role="status">CIS is working. Your current page and answers are retained.</p>' : ''}
    ${model.wizard && !model.wizard.baselineCurrent ? '<section class="notice warning"><strong>Product baseline needs attention</strong><p>You can draft the feature now. Final review needs a current, activated product baseline.</p><button type="button" class="secondary" data-wizard-action="product-wizard">Open product wizard</button></section>' : ''}
    ${model.page !== 'foundation' && model.error ? `<p class="notice warning" role="alert">${h(model.error)}</p>` : ''}
    <div class="wizard-layout">${navigation(model)}<div class="feature-page">${!model.wizard || model.page === 'foundation' ? foundation : renderReviewPage(model)}</div></div>`;
  const script = `<script nonce="${scriptNonce}">
    const vscode = acquireVsCodeApi();
    document.querySelectorAll('button[data-command]').forEach(button => button.addEventListener('click', () => vscode.postMessage({ command: button.dataset.command, value: button.dataset.value || '' })));
    const form = document.getElementById('feature-form');
    function fields() {
      if (!form) return undefined;
      const data = new FormData(form); const result = Object.fromEntries(data.entries());
      result.integrationRepositories = data.getAll('integrationRepositories'); return JSON.stringify(result);
    }
    document.querySelectorAll('[data-feature-action]').forEach(button => button.addEventListener('click', () => {
      const value = fields(); const command = button.dataset.featureAction;
      document.querySelectorAll('button').forEach(item => item.disabled = true);
      vscode.postMessage({ command, value });
    }));
    form?.addEventListener('submit', event => { event.preventDefault(); const value = fields(); document.querySelectorAll('button').forEach(item => item.disabled = true); vscode.postMessage({ command: 'preview', value }); });
    const title = document.getElementById('title'); const identifier = document.getElementById('slug');
    title?.addEventListener('change', () => { if (!identifier.value) identifier.value = title.value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 80).replace(/-$/g, ''); });
    ${wizardScript(model.page || 'foundation')}${model.page === 'delivery' ? deliveryScript() : ''}${model.page === 'experience' ? screenScript() : ''}</script>`;
  return studioDocument(webview, 'Feature definition wizard', body, scriptNonce, script);
}

module.exports = { openFeatureIntake, renderFeatureIntake, parseDraft, slug };

function renderSourceUpdate(model) {
  const plan = model.sourceUpdate?.plan;
  if (!plan) return '';
  const decisions = (title, items) => items.length ? `<details><summary>${h(title)} (${items.length})</summary><ul>${items.map(item => `<li>${h(item)}</li>`).join('')}</ul></details>` : '';
  return `<section class="card"><span class="eyebrow">Review BRD update</span><h2>Reimport ${h(plan.title)}</h2>
    <p>Updated document: ${h(plan.incomingPath)}</p><p>${h(plan.previousBytes)} → ${h(plan.incomingBytes)} bytes · ${h(plan.retainedDecisionAnswers)} saved decision answers matched to unchanged questions.</p>
    <p>Feature identity, repositories, saved summaries and repository work are retained. New or changed questions need answers. Removed or ambiguous answers remain in revision history.</p>
    ${decisions('New or changed questions', plan.addedDecisions)}${decisions('Removed or replaced questions', plan.removedDecisions)}
    <p><strong>Review required after import:</strong> ${h(plan.pagesRequiringReview.join(', '))}. Existing answers remain available for you to check and save again.</p>
    <p>The current BRD and saved review will be retained in revision history.</p>
    <div class="actions"><button type="button" class="secondary" data-wizard-action="compare-source" ${model.busy ? 'disabled' : ''}>Compare BRD changes</button><button type="button" data-wizard-action="apply-source-update" ${model.busy ? 'disabled' : ''}>Apply updated BRD</button><button type="button" class="secondary" data-wizard-action="cancel-source-update" ${model.busy ? 'disabled' : ''}>Cancel update</button></div></section>`;
}
