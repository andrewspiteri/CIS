'use strict';

const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');
const { AuthoritySelector } = require('./lib/authority');
const { CisCli, CisCliError } = require('./lib/cis-cli');
const { doctorFixId, parseDoctorCommand, assertDoctorScope } = require('./lib/doctor-commands');
const { ACTIONS: GETTING_STARTED_ACTIONS, gettingStartedModel, openGettingStartedPanel } = require('./lib/getting-started');
const { bound, resolveWithin } = require('./lib/security');
const { workspaceWatchRoots, ignoredWatchEvent } = require('./lib/watch-roots');
const { CisViewProvider, markdownFiles, repositoryMetadata } = require('./lib/views');
const { projectChangeOverview } = require('./lib/projections');
const { isActiveCurrent, isReadyForApproval, nextStartableItem, productPaths, reviewMatchesBrd, stateOf } = require('./lib/product-journey');
const {
  confirmAgentRequest, openBrdQuestionsPanel, openTechnicalIntentQuestionsPanel, openUiDirectionQuestionsPanel, openDefinitionWizardPanel, openDoctorPanel, openChangeOverviewPanel, openDesignPanel, openEvidencePanel, openRecommendationReviewPanel,
  openContextPanel, openRunDetailPanel, openTaskDetailPanel,
} = require('./lib/webview');

const VIEW_IDS = ['workspace', 'journey', 'changes', 'evidence', 'runs', 'governance'];

function activate(context, overrides = {}) {
  const output = vscode.window.createOutputChannel('Change Impact Studio');
  const authority = overrides.authority || new AuthoritySelector(vscode, context.workspaceState);
  const cli = overrides.cli || new CisCli(vscode, output, authority);
  let gettingStarted;
  const definitionWizards = new Map();
  const openingWizards = new Map();
  const businessDrafts = new Set();
  const providers = new Map(VIEW_IDS.map(id => [id, new CisViewProvider(vscode, id, authority, cli)]));
  context.subscriptions.push(output);
  for (const [id, provider] of providers) context.subscriptions.push(vscode.window.registerTreeDataProvider(`cis.${id}`, provider));

  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 50);
  status.command = authority.needsSelection() ? 'cis.selectAuthority' : 'cis.repoDoctor';
  status.text = '$(pulse) CIS'; status.tooltip = 'Change Impact Studio'; status.show(); context.subscriptions.push(status);
  const refresh = debounce(async (stale = false, invalidate = false) => {
    gettingStarted?.update(gettingStartedModel(vscode, authority));
    status.command = authority.needsSelection() ? 'cis.selectAuthority' : 'cis.repoDoctor';
    if (stale) {
      cli.clearQueryCache?.();
      for (const provider of providers.values()) provider.markStale();
      status.text = '$(history) CIS'; status.tooltip = 'CIS evidence changed; refresh required.'; return;
    }
    if (invalidate) cli.clearQueryCache?.();
    for (const provider of providers.values()) provider.refresh(false);
    status.text = '$(sync~spin) CIS';
    try {
      const version = await cli.version();
      status.text = version.compatible ? '$(check) CIS' : '$(warning) CIS';
      status.tooltip = version.compatible ? `CIS ${version.raw}` : `CIS ${version.raw} is not compatible with this extension.`;
    } catch (error) {
      status.text = '$(circle-slash) CIS'; status.tooltip = conciseError(error);
    }
  }, 250);

  const command = (id, handler) => context.subscriptions.push(vscode.commands.registerCommand(id, async (...args) => {
    try { return await handler(...args); }
    catch (error) {
      const message = conciseError(error);
      output.appendLine(`ERROR [${error.kind || 'extension'}${error.exitCode === undefined ? '' : `/${error.exitCode}`}]: ${message}`);
      const action = await vscode.window.showErrorMessage(`CIS: ${message}`, 'Show output');
      if (action === 'Show output') output.show(true);
      return undefined;
    }
  }));

  command('cis.refresh', () => refresh(false, true));
  command('cis.openGuide', () => vscode.commands.executeCommand('markdown.showPreview',
    vscode.Uri.file(path.join(__dirname, 'GETTING_STARTED.md'))));
  command('cis.gettingStarted', () => {
    const model = gettingStartedModel(vscode, authority);
    if (gettingStarted) { gettingStarted.update(model); gettingStarted.panel.reveal(); return; }
    let busy = false;
    gettingStarted = openGettingStartedPanel(vscode, model, async action => {
      if (busy) return;
      busy = true;
      try {
        const state = gettingStartedModel(vscode, authority);
        if (action === 'initialize' && !state.canInitialize) return;
        if (['wizard', 'import'].includes(action) && !state.canContinue) return;
        const target = GETTING_STARTED_ACTIONS[action];
        if (target) await vscode.commands.executeCommand(target);
      } catch (error) {
        output.appendLine(`ERROR [getting-started]: ${conciseError(error)}`);
        await vscode.window.showErrorMessage(`CIS: ${conciseError(error)}`);
      } finally {
        busy = false;
        gettingStarted?.update(gettingStartedModel(vscode, authority));
      }
    });
    gettingStarted.panel.onDidDispose(() => { gettingStarted = undefined; });
    context.subscriptions.push(gettingStarted.panel);
  });
  command('cis.authorityInit', async () => {
    if (vscode.workspace.isTrusted === false) throw new Error('Trust this workspace before initializing the CIS authority.');
    if (!authority.root()) await vscode.commands.executeCommand('cis.selectAuthority');
    const repository = authority.root();
    if (!repository) return;
    const state = gettingStartedModel(vscode, authority);
    if (state.configured) {
      await vscode.window.showInformationMessage('This authority is already initialized. Import repositories or open the high-level wizard.');
      return;
    }
    if (state.problem) throw new Error(state.problem);
    const documentationRoot = await vscode.window.showInputBox({
      prompt: 'Required repository-relative CIS documentation root for the authority', value: state.documentationRoot,
      validateInput: value => resolveWithin(repository, value) ? undefined : 'Enter a contained repository-relative path.',
    });
    if (!documentationRoot) return;
    const identity = await promptProductIdentity(repository);
    if (!identity) return;
    const args = ['workspace', 'init', '--repo', repository, '--root', documentationRoot, ...productIdentityArgs(identity)];
    const plan = await cli.query([...args, '--dry-run'], { repository: false, acceptStructuredFailure: true });
    showQuery('Authority initialization plan', plan);
    if (plan.errors?.length || plan.collisions?.length || plan._process?.failed)
      throw new Error('The authority initialization plan needs attention. Review its findings and run Repository Doctor before retrying.');
    const confirmed = await vscode.window.showWarningMessage('Initialize this CIS authority?', { modal: true,
      detail: `Authority: ${repository}\nProduct: ${identity.productName} (${identity.productId})\nEcosystem: ${identity.ecosystemName} (${identity.ecosystemId})\nDocuments: ${documentationRoot}\n\nThe initialization plan is open for review. CIS will create the product authority and build its context graph.`,
    }, 'Initialize authority');
    if (confirmed !== 'Initialize authority') return;
    if (authority.root() !== repository) throw new Error('The selected authority changed. Review initialization again for the new folder.');
    await cli.runForeground('Initialize CIS authority', [...args, '--yes'], { repository: false });
    await cli.runForeground('Build authority context', ['graph', 'build', '--workspace', repository], { repository: false });
    await refresh(false, true);
    await vscode.window.showInformationMessage('Authority initialized. Import your application repositories, then open the high-level wizard.');
  });
  command('cis.selectAuthority', async () => {
    const folder = await authority.choose();
    if (!folder) return;
    for (const provider of providers.values()) provider.last = undefined;
    await refresh(false, true);
    await vscode.window.showInformationMessage(`CIS authority selected: ${folder.uri.fsPath}`);
  });
  command('cis.clearAuthority', async () => { await authority.clear(); await refresh(false, true); });
  command('cis.open', item => openCanonical(authority, item?.file, false));
  command('cis.preview', item => openCanonical(authority, item?.file, true));
  command('cis.repoInit', async () => {
    const root = await vscode.window.showInputBox({ prompt: 'Required repository-relative CIS documentation root', value: 'docs/cis',
      validateInput: value => resolveWithin(authority.root(), value) ? undefined : 'Enter a contained repository-relative path.' });
    if (!root) return;
    await cli.runForeground('Initialize CIS repository', ['repo', 'init', '--root', root, '--yes']);
    await refresh(false);
  });
  command('cis.repoImport', async () => {
    const repository = authority.root();
    if (!repository) throw new Error('Open the existing repository before importing it.');
    const documentationRoot = await vscode.window.showInputBox({
      prompt: 'Required repository-relative CIS documentation root for the existing repository',
      value: 'docs/cis',
      validateInput: value => resolveWithin(repository, value) ? undefined : 'Enter a contained repository-relative path.',
    });
    if (!documentationRoot) return;
    const workspaceBoundary = readWorkspaceBoundary(repository);
    let sources = [repository];
    let participation = 'owned';
    let relationship = 'none';
    let components = [];
    let identityArgs = [];
    if (!workspaceBoundary) {
      const identity = await promptProductIdentity(repository);
      if (!identity) return;
      identityArgs = productIdentityArgs(identity);
    } else {
      const selected = await vscode.window.showOpenDialog({
        canSelectFiles: false,
        canSelectFolders: true,
        canSelectMany: true,
        title: `Select repositories to import into ${workspaceBoundary.product.name}`,
      });
      if (!selected?.length) return;
      sources = selected.map(item => item.fsPath);
      const selectedParticipation = await vscode.window.showQuickPick([
        { label: 'Owned product repository', description: 'Implementation is governed by this product workspace.', value: 'owned' },
        { label: 'External dependency repository', description: 'Imported for producer/consumer context; implementation remains outside this product.', value: 'dependency' },
      ], { placeHolder: 'How do these repositories participate in this product?' });
      if (!selectedParticipation) return;
      participation = selectedParticipation.value;
      if (participation === 'dependency') {
        const selectedRelationship = await vscode.window.showQuickPick([
          { label: 'Producer', description: 'The dependency provides capabilities or contracts consumed by this product.', value: 'producer' },
          { label: 'Consumer', description: 'The dependency consumes capabilities or contracts provided by this product.', value: 'consumer' },
          { label: 'Bidirectional', description: 'Capabilities or contracts flow in both directions.', value: 'bidirectional' },
        ], { placeHolder: 'Direction relative to this product' });
        if (!selectedRelationship) return;
        relationship = selectedRelationship.value;
        const scope = await vscode.window.showInputBox({
          prompt: 'Optional comma-separated component scope within the dependency',
          placeHolder: 'payments-api, customer-events',
        });
        components = String(scope || '').split(',').map(value => value.trim()).filter(Boolean);
      }
    }
    const args = ['repo', 'import', '--workspace', repository];
    for (const source of sources) args.push('--source', source);
    args.push('--root', documentationRoot, '--participation', participation, '--relationship', relationship);
    for (const component of components) args.push('--component', component);
    args.push(...identityArgs);
    const plan = await cli.query([...args, '--dry-run'], { repository: false });
    const imported = Array.isArray(plan.repositories) ? plan.repositories.length : 1;
    const confirmed = await vscode.window.showWarningMessage(
      `Import ${imported} existing repository into CIS? Source files are classified and indexed in place; CIS does not copy or rewrite implementation files.`,
      { modal: true }, 'Import existing repository');
    if (confirmed !== 'Import existing repository') return;
    await cli.runForeground('Import existing CIS repository', [...args, '--yes'], { repository: false });
    await cli.runForeground('Build imported repository context', ['graph', 'build', '--workspace', repository], { repository: false });
    await refresh(false);
    const next = await vscode.window.showInformationMessage(
      'The existing repository was imported and its context graph was built.', 'Open definition wizard');
    if (next === 'Open definition wizard') await vscode.commands.executeCommand('cis.definitionWizard');
  });
  command('cis.productStart', async () => {
    const root = authority.root();
    const metadata = repositoryMetadata(root, vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    if (!metadata.initialized) throw new Error('Initialize the repository before starting product definition.');
    const paths = productPaths(root, metadata);
    if (!fs.existsSync(paths.workspace)) {
      const identity = await promptProductIdentity(root);
      if (!identity) return;
      const confirmed = await vscode.window.showWarningMessage(
        `Create the ${identity.productName} product workspace in the ${identity.ecosystemName} ecosystem and build its context graph?`,
        { modal: true }, 'Start product definition');
      if (confirmed !== 'Start product definition') return;
      await cli.runForeground('Create CIS workspace authority',
        ['workspace', 'init', '--root', metadata.documentationRoot, ...productIdentityArgs(identity), '--yes'], { repository: false });
      await cli.runForeground('Build CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    }
    if (!fs.existsSync(paths.brd)) {
      const title = await vscode.window.showInputBox({
        prompt: 'Product or business requirements title',
        value: `${metadata.id || path.basename(root)} Business Requirements`,
        validateInput: value => value.trim() ? undefined : 'A title is required.',
      });
      if (!title) return;
      await cli.runForeground('Create business requirements',
        ['brd', 'init', '--workspace', root, '--title', title.trim()], { repository: false });
      await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    }
    await refresh(false);
    await openCanonical(authority, paths.brd, false);
  });
  command('cis.definitionWizard', async () => {
    if (!authority.root()) await vscode.commands.executeCommand('cis.selectAuthority');
    const root = authority.root();
    if (!root) return;
    const key = process.platform === 'win32' ? path.resolve(root).toLowerCase() : path.resolve(root);
    if (openingWizards.has(key)) {
      await openingWizards.get(key);
      definitionWizards.get(key)?.panel.reveal();
      return;
    }
    const existing = definitionWizards.get(key);
    if (existing) { existing.panel.reveal(); return; }
    const opening = (async () => {
      const wizard = await createDefinitionWizard(root);
      if (wizard) {
        definitionWizards.set(key, wizard);
        wizard.panel.onDidDispose(() => { if (definitionWizards.get(key) === wizard) definitionWizards.delete(key); });
        context.subscriptions.push(wizard.panel);
      }
    })();
    openingWizards.set(key, opening);
    try { await opening; }
    finally { if (openingWizards.get(key) === opening) openingWizards.delete(key); }
  });
  async function createDefinitionWizard(root) {
    const metadata = repositoryMetadata(root, vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    if (!metadata.initialized) throw new Error('Initialize the repository before starting high-level product definition.');
    const paths = productPaths(root, metadata);
    if (!fs.existsSync(paths.workspace)) {
      const identity = await promptProductIdentity(root);
      if (!identity) return;
      const confirmed = await vscode.window.showWarningMessage(
        `Create the ${identity.productName} product workspace in the ${identity.ecosystemName} ecosystem and start the product-definition wizard?`,
        { modal: true }, 'Start wizard');
      if (confirmed !== 'Start wizard') return;
      await cli.runForeground('Create CIS workspace authority',
        ['workspace', 'init', '--root', metadata.documentationRoot, ...productIdentityArgs(identity), '--yes'], { repository: false });
      await cli.runForeground('Build CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    }
    const loadWizard = operation => vscode.window.withProgress({
      location: vscode.ProgressLocation.Notification, title: 'Loading CIS product definition', cancellable: false,
    }, operation);
    let model = await loadWizard(async () => {
      let next = await definitionWizardModel(cli, root);
      if (!next.sessionId) {
        const initialized = await queryWorkspace(cli, ['definition', 'init'], root);
        next = await definitionWizardModel(cli, root, initialized);
      }
      return next;
    });
    let controller;
    const reload = async (page, baseResult) => {
      cli.clearQueryCache?.();
      const next = await loadWizard(() => definitionWizardModel(cli, root, baseResult));
      model = next;
      controller.update(next, page || controller.page());
      return next;
    };
    const initialPage = model.readyToActivate === true && model.active !== false ? 'review' : undefined;
    controller = openDefinitionWizardPanel(vscode, root, model, async (action, value, wizard) => {
      if (authority.root() !== root) throw new Error('The CIS authority changed. Reopen the wizard for the selected authority.');
      if (action === 'refresh') { await reload(wizard.page()); return; }
      if (action === 'save-answer') {
        let answer;
        try { answer = JSON.parse(value); }
        catch { throw new Error('The wizard answer was malformed.'); }
        if (!answer?.page || !answer?.id || !String(answer.answer || '').trim())
          throw new Error('Enter a direction before saving it.');
        const actor = await actorIdentity(); if (!actor) return;
        const answered = await loadWizard(() => queryWorkspace(cli, ['definition', 'answer', '--page', String(answer.page), '--id', String(answer.id),
          '--answer', String(answer.answer).trim(), '--actor', actor], root));
        await reload(String(answer.page), answered);
        return;
      }
      if (action === 'prepare') {
        if (value === 'business' && !fs.existsSync(paths.brd)) {
          await vscode.commands.executeCommand('cis.productStart');
        }
        const prepared = await loadWizard(() => queryWorkspace(cli, ['definition', 'prepare', '--page', value], root));
        const next = await reload(value, prepared);
        const page = (next.pages || []).find(item => item.id === value);
        if (page?.complete && page?.current) {
          const following = (next.pages || []).find(item => Number(item.ordinal) === Number(page.ordinal) + 1);
          if (following) controller.update(next, following.id);
        }
        await refresh(false);
        return;
      }
      if (action === 'open-business-dictionary') {
        const [reportIndex, itemIndex] = String(value).split(':').map(Number);
        const report = model.businessInference?.lastPreparation?.[reportIndex];
        const item = report?.inventories?.[itemIndex];
        if (!item || !(model.businessInference?.repositories || []).some(repository => repository.repositoryPath === report.repositoryPath))
          throw new Error('The selected dictionary is not in the owned product repositories.');
        const file = resolveWithin(report.repositoryPath, item.path);
        if (!file) throw new Error('The dictionary path is outside its repository.');
        await openCanonical({ root: () => report.repositoryPath }, file, false);
        return;
      }
      if (action === 'business-action') {
        if (value === 'doctor') await vscode.commands.executeCommand('cis.repoDoctor');
        else if (value === 'evidence') await vscode.commands.executeCommand('cis.contextSearch');
        else if (value === 'open-brd') {
          if (!fs.existsSync(paths.brd)) await vscode.commands.executeCommand('cis.productStart');
          else await openCanonical(authority, paths.brd, false);
        } else if (value === 'draft-brd') {
          if (!fs.existsSync(paths.brd)) await vscode.commands.executeCommand('cis.productStart');
          await vscode.commands.executeCommand('cis.brdAgentDraft');
        } else if (value === 'prepare-evidence') {
          await oneBusinessDraft(async () => {
            await cli.runForeground('Discover existing-system dictionaries', ['definition', 'prepare', '--page', 'business', '--workspace', authority.root()], { repository: false });
            await refresh(false);
          });
        } else if (value === 'infer-brd') {
          await vscode.commands.executeCommand('cis.brdInferFromProject');
        } else if (value === 'questions') await vscode.commands.executeCommand('cis.brdAnswerQuestions');
        else if (value === 'review-brd') await vscode.commands.executeCommand('cis.brdAgentReview');
        await reload('business');
        return;
      }
      if (action === 'infer-technical-intent') {
        await vscode.commands.executeCommand('cis.technicalIntentInferFromProject');
        await reload('technical');
        return;
      }
      if (action === 'infer-solution-design') {
        await vscode.commands.executeCommand('cis.solutionDesignInferFromProject');
        await reload('architecture');
        return;
      }
      if (action === 'activate') {
        const actor = await actorIdentity(); if (!actor) return;
        const confirmed = await vscode.window.showWarningMessage(
          `Approve and activate the exact current high-level product-definition baseline as ${actor}?`,
          { modal: true }, 'Approve and activate');
        if (confirmed !== 'Approve and activate') return;
        await cli.runForeground('Activate high-level product definition',
          ['definition', 'activate', '--workspace', root, '--reviewer', actor], { repository: false });
        await reload('review');
        await refresh(false);
      }
    }, value => openReportedPath(value, false), initialPage);
    return controller;
  }
  command('cis.brdValidate', async () => validateProductDocument(cli, refresh, showQuery,
    'Business requirements validation', ['brd', 'validate'], authority.root()));
  command('cis.brdApprove', async () => approveProductDocument(cli, refresh, showQuery, {
    label: 'business requirements', validate: ['brd', 'validate'], approve: ['brd', 'approve'],
  }, authority.root()));
  async function oneBusinessDraft(operation) {
    const root = path.resolve(authority.root() || '.');
    const key = process.platform === 'win32' ? root.toLowerCase() : root;
    if (businessDrafts.has(key)) return;
    businessDrafts.add(key);
    try { return await operation(); }
    finally { businessDrafts.delete(key); }
  }
  command('cis.brdAgentDraft', () => oneBusinessDraft(() => draftBusinessDefinition()));
  command('cis.brdInferFromProject', () => oneBusinessDraft(async () => {
    const root = authority.root();
    const definition = await queryWorkspace(cli, ['definition', 'status'], root);
    const inference = definition.businessInference;
    if (!inference) throw new Error('Update the CIS CLI to load the existing-project inference sources.');
    if (!inference.canDraft) throw new Error('Business inference requires a Draft or Review Required BRD. Open the current BRD to review its lifecycle.');
    if (!inference.repositories?.length) throw new Error('Import the existing product repositories before inferring the business definition.');
    const selected = await vscode.window.showQuickPick(inference.repositories.map(repository => ({
      label: repository.id, description: repository.repositoryPath, picked: true, repository,
    })), { canPickMany: true, placeHolder: 'Infer from these product repositories (select up to 10)' });
    if (!selected?.length) return;
    if (selected.length > 10) throw new Error('Select at most 10 repositories for one inference run.');
    if (authority.root() !== root) throw new Error('The CIS authority changed. Reopen business inference for the selected authority.');
    if (!fs.existsSync(currentProductPaths(vscode, authority).brd)) {
      await vscode.commands.executeCommand('cis.productStart');
      if (!fs.existsSync(currentProductPaths(vscode, authority).brd)) return;
    }
    await draftBusinessDefinition(selected.map(item => item.repository), root);
  }));
  async function draftBusinessDefinition(repositories, expectedRoot) {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    if (expectedRoot && root !== expectedRoot) throw new Error('The CIS authority changed. Reopen business inference for the selected authority.');
    if (!fs.existsSync(paths.brd)) throw new Error('Create the canonical business requirements before assigning an agent draft.');
    const selected = repositories ? repositories.map(repository => vscode.Uri.file(repository.repositoryPath)) : await vscode.window.showOpenDialog({
      canSelectFiles: true, canSelectFolders: true, canSelectMany: true,
      title: 'Select non-sensitive reference documents or initialized repositories for the BRD draft',
      filters: {
        'Supported evidence': ['docx', 'md', 'txt', 'csv', 'tsv', 'json', 'yaml', 'yml', 'xml', 'html', 'rst', 'adoc'],
        'Word documents': ['docx'],
        'Text evidence': ['md', 'txt', 'csv', 'tsv', 'json', 'yaml', 'yml', 'xml', 'html', 'rst', 'adoc'],
      },
    });
    if (!selected?.length) return;
    const available = await cli.query(['agent', 'providers']);
    const candidates = (available.providers || []).filter(provider => provider.directExecution
      && provider.modes?.includes('implement') && provider.permissions?.includes('workspace-write'));
    if (!candidates.length) throw new Error('No available agent provider supports isolated workspace-write BRD drafting. Open Runs to diagnose Codex or Claude.');
    const providerItem = await vscode.window.showQuickPick(candidates.map(provider => ({
      label: provider.displayName, description: provider.description, provider,
    })), { placeHolder: 'Select the agent that will draft the BRD' });
    if (!providerItem) return;
    const transport = providerItem.provider.transports?.length > 1
      ? await vscode.window.showQuickPick(providerItem.provider.transports, { placeHolder: 'Select the provider transport' })
      : providerItem.provider.transports?.[0];
    if (!transport) return;
    const requestPolicy = providerItem.provider.supportsInteractivePermissions
      ? await vscode.window.showQuickPick([
        { label: 'Deny approval requests', description: 'Unsupported operations fail visibly.', approve: false },
        { label: 'Approve inside scratch workspace', description: 'Only requests within the isolated authoring workspace may be approved.', approve: true },
      ], { placeHolder: 'Select the permission-request policy' })
      : { approve: false };
    if (!requestPolicy) return;
    const actor = await actorIdentity(); if (!actor) return;
    const names = selected.map(uri => path.basename(uri.fsPath)).join(', ');
    const confirmed = await vscode.window.showWarningMessage(
      `Send ${selected.length} selected evidence source(s) (${names}) to ${providerItem.provider.displayName}? ${repositories ? 'CIS first refreshes the owned product inventories, then gives the provider bounded copies of implementation and test files so it can trace how the product works. Environment/credential files and dependencies are excluded; likely credential literals are redacted. ' : ''}CIS projects supported documents, runs inside an isolated scratch repository, and applies only a protected one-file BRD draft. Repository discovery includes a coverage checklist and cached source snapshots.`,
      { modal: true }, 'Start BRD draft');
    if (confirmed !== 'Start BRD draft') return;
    if (authority.root() !== root) throw new Error('The CIS authority changed. Start the draft again for the selected authority.');
    if (repositories) await cli.runForeground('Prepare API, data and business evidence',
      ['definition', 'prepare', '--page', 'business', '--workspace', root], { repository: false });
    if (authority.root() !== root) throw new Error('The CIS authority changed. Inference stopped before drafting.');
    const args = ['agent', 'author', 'brd', '--provider', providerItem.provider.id,
      '--transport', transport, '--actor', actor];
    for (const uri of selected) args.push('--reference', uri.fsPath);
    if (requestPolicy.approve) args.push('--approve-requests');
    await cli.runForeground(repositories ? 'Infer business definition from existing project' : 'Draft business requirements with agent', args, { cancellable: false });
    await cli.runForeground(repositories ? 'Refresh CIS authority graph' : 'Refresh CIS workspace graph',
      ['graph', 'build', repositories ? '--repo' : '--workspace', root], { repository: false });
    await refresh(false);
    if (!repositories) await openCanonical(authority, paths.brd, false);
    else await vscode.window.showInformationMessage('Business definition inferred. Review the BRD and its source evidence, run Independent review, and resolve the open questions. The draft remains Review Required.');
  }
  command('cis.brdAgentIncorporateQuestions', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    if (!fs.existsSync(paths.brd)) throw new Error('Create the canonical business requirements before incorporating question answers.');
    const questions = await queryWorkspace(cli, ['brd', 'questions', 'list'], root);
    if (!(questions.questions || []).length) throw new Error('The BRD has no governed questions to incorporate.');
    if ((questions.questions || []).some(item => String(item.status).toLowerCase() !== 'answered'))
      throw new Error('Answer every governed BRD question before updating the document.');
    if (!questions.answerDigest) throw new Error('CIS could not bind the answered-question set to a stable digest.');
    const available = await cli.query(['agent', 'providers']);
    const candidates = (available.providers || []).filter(provider => provider.directExecution
      && provider.modes?.includes('implement') && provider.permissions?.includes('workspace-write'));
    if (!candidates.length) throw new Error('No available agent provider supports isolated workspace-write BRD revision. Open Runs to diagnose Codex or Claude.');
    const providerItem = await vscode.window.showQuickPick(candidates.map(provider => ({
      label: provider.displayName, description: `Incorporate only the governed human answers; ${provider.description}`, provider,
    })), { placeHolder: 'Select the agent that will update the BRD from answered questions' });
    if (!providerItem) return;
    let diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
    if (diagnosis.diagnoses?.[0]?.available === false) {
      const providerDiagnosis = diagnosis.diagnoses[0];
      const canAuthenticate = (providerDiagnosis.capabilities || []).some(capability => capability.startsWith('authentication:'));
      if (String(providerDiagnosis.status).startsWith('authentication-') && canAuthenticate) {
        const setup = await vscode.window.showWarningMessage(
          `${providerItem.provider.displayName} is installed but not authenticated. Start its provider-native login now?`,
          { modal: true }, 'Authenticate provider');
        if (setup === 'Authenticate provider') {
          await vscode.commands.executeCommand('cis.agentAuthenticate', providerItem.provider.id);
          diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
        }
      }
      if (diagnosis.diagnoses?.[0]?.available === false) {
        showQuery(`Agent provider: ${providerItem.provider.id}`, diagnosis); return;
      }
    }
    const transport = providerItem.provider.transports?.length > 1
      ? await vscode.window.showQuickPick(providerItem.provider.transports, { placeHolder: 'Select the provider transport' })
      : providerItem.provider.transports?.[0];
    if (!transport) return;
    const requestPolicy = providerItem.provider.supportsInteractivePermissions
      ? await vscode.window.showQuickPick([
        { label: 'Deny approval requests', description: 'Unsupported operations fail visibly.', approve: false },
        { label: 'Approve inside revision workspace', description: 'Only requests inside the isolated one-file scope may be approved.', approve: true },
      ], { placeHolder: 'Select the permission-request policy' }) : { approve: false };
    if (!requestPolicy) return;
    const actor = await actorIdentity(); if (!actor) return;
    const confirmed = await vscode.window.showWarningMessage(
      `Update the BRD with ${providerItem.provider.displayName} using all ${(questions.questions || []).length} governed human answer(s)? CIS will preserve the complete question table and managed evidence, apply only a one-file revision, and require an independent review afterward.`,
      { modal: true }, 'Update BRD');
    if (confirmed !== 'Update BRD') return;
    const args = ['agent', 'incorporate', 'brd-questions', '--provider', providerItem.provider.id,
      '--transport', transport, '--actor', actor];
    if (requestPolicy.approve) args.push('--approve-requests');
    await cli.runForeground('Update BRD from answered questions', args, { cancellable: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    await refresh(false);
    await openCanonical(authority, paths.brd, false);
    await vscode.window.showInformationMessage('The answered questions were incorporated into the BRD. Select a different provider for the required independent review.');
    await vscode.commands.executeCommand('cis.brdAgentReview');
  });
  command('cis.brdAgentReview', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    if (!fs.existsSync(paths.brd)) throw new Error('Create the canonical business requirements before assigning an independent review.');
    const [available, productRuns] = await Promise.all([
      cli.query(['agent', 'providers']), cli.query(['agent', 'runs', '--change', 'PRODUCT', '--summary', '--latest-per-task', '--limit', '10']),
    ]);
    const successfulProducers = (productRuns.runs || []).filter(run => ['BRD-DRAFT', 'BRD-REVISION', 'BRD-QUESTION-REVISION'].includes(run.taskId)
      && String(run.status).toLowerCase() === 'succeeded');
    const latestProducer = successfulProducers[0];
    const latestDraft = (productRuns.runs || []).find(run => run.taskId === 'BRD-DRAFT' && String(run.status).toLowerCase() === 'succeeded');
    const reviewProviders = (available.providers || []).filter(provider => provider.directExecution
      && provider.modes?.includes('review') && provider.permissions?.includes('read-only'));
    const independent = latestProducer ? reviewProviders.filter(provider => provider.id !== latestProducer.provider) : reviewProviders;
    const candidates = independent.length ? independent : reviewProviders;
    if (!candidates.length) throw new Error('No agent provider supports a read-only BRD review. Open Runs to inspect provider capabilities.');
    const providerItem = await vscode.window.showQuickPick(candidates.map(provider => ({
      label: provider.displayName, description: provider.id === latestProducer?.provider
        ? 'Same provider as the latest BRD producer; CIS will reject self-review.' : provider.description, provider,
    })), { placeHolder: 'Select a provider different from the latest BRD producer' });
    if (!providerItem) return;
    let diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
    if (diagnosis.diagnoses?.[0]?.available === false) {
      const providerDiagnosis = diagnosis.diagnoses[0];
      const authenticationMethods = (providerDiagnosis.capabilities || [])
        .filter(capability => capability.startsWith('authentication:'));
      if (String(providerDiagnosis.status).startsWith('authentication-') && authenticationMethods.length) {
        const setup = await vscode.window.showWarningMessage(
          `${providerItem.provider.displayName} is installed but not authenticated. Start its provider-native login now?`,
          { modal: true }, 'Authenticate provider');
        if (setup === 'Authenticate provider') {
          await vscode.commands.executeCommand('cis.agentAuthenticate', providerItem.provider.id);
          diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
        }
      }
      if (diagnosis.diagnoses?.[0]?.available === false) {
        showQuery(`Agent provider: ${providerItem.provider.id}`, diagnosis); return;
      }
    }
    const transport = providerItem.provider.transports?.length > 1
      ? await vscode.window.showQuickPick(providerItem.provider.transports, { placeHolder: 'Select the provider transport' })
      : providerItem.provider.transports?.[0];
    if (!transport) return;
    const sourceChoice = latestDraft ? await vscode.window.showQuickPick([
      { label: 'BRD and authoring references', description: 'Also disclose the exact authoring implementation/test snapshots and extracted references to this provider.', include: true },
      { label: 'BRD only', description: 'Review intrinsic quality without claiming coverage against the original references.', include: false },
    ], { placeHolder: 'Select the independent review evidence boundary' }) : { label: 'BRD only', include: false };
    if (!sourceChoice) return;
    const actor = await actorIdentity(); if (!actor) return;
    const confirmation = await vscode.window.showWarningMessage(
      `Start a read-only BRD review with ${providerItem.provider.displayName}? ${sourceChoice.include
        ? 'The canonical BRD and the latest extracted authoring references will be exposed in an isolated scratch repository.'
        : 'Only the canonical BRD and its governance context will be exposed in an isolated scratch repository.'} Findings are advisory and cannot approve or edit the BRD.`,
      { modal: true }, 'Start independent review');
    if (confirmation !== 'Start independent review') return;
    const args = ['agent', 'review', 'brd', '--provider', providerItem.provider.id,
      '--transport', transport, '--actor', actor];
    if (sourceChoice.include) args.push('--include-authoring-evidence');
    await cli.runForeground(`Review business requirements with ${providerItem.provider.displayName}`, args,
      { cancellable: true });
    const completed = await cli.query(['agent', 'runs', '--change', 'PRODUCT', '--task', 'BRD-REVIEW', '--summary', '--limit', '10']);
    const run = (completed.runs || []).find(item => String(item.status).toLowerCase() === 'succeeded'
      && reviewMatchesBrd(item, paths.brd) !== false);
    if (!run) throw new Error('CIS did not retain a successful structured BRD review. Open Runs for diagnostics.');
    const structured = await cli.query(['agent', 'show', run.runId, '--summary']);
    if ((structured.run?.result?.review?.findings || []).length) {
      await cli.runForeground('Initialize BRD review recommendations',
        ['brd', 'review', 'init', run.runId, '--workspace', root], { repository: false });
    }
    const reviewPath = resolveWithin(root, `.cis/local/agents/runs/${run.runId}/brd-review.md`, { allowLocalEvidence: true });
    await refreshAfterReviewCompletion(refresh);
    await openCanonical(authority, reviewPath, false, true);
  });
  command('cis.brdReviewRecommendations', async reviewRunId => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    let selectedRun = typeof reviewRunId === 'string' ? reviewRunId : undefined;
    if (!selectedRun) {
      const runs = await cli.query(['agent', 'runs', '--change', 'PRODUCT', '--task', 'BRD-REVIEW', '--summary', '--limit', '10']);
      selectedRun = (runs.runs || []).find(item => String(item.status).toLowerCase() === 'succeeded'
        && reviewMatchesBrd(item, paths.brd) !== false)?.runId;
    }
    if (!selectedRun) throw new Error('No successful structured BRD review is available to disposition.');
    const selectedEvidence = await cli.query(['agent', 'show', selectedRun, '--summary']);
    if (reviewMatchesBrd(selectedEvidence.run, paths.brd) === false) {
      const next = await vscode.window.showWarningMessage(
        'This review was superseded because the BRD evidence baseline changed after it ran. The user-authored BRD may be unchanged, but evidence-related findings can be obsolete.',
        { modal: true }, 'Start fresh review');
      if (next === 'Start fresh review') await vscode.commands.executeCommand('cis.brdAgentReview');
      return;
    }
    await cli.runForeground('Initialize BRD review recommendations',
      ['brd', 'review', 'init', selectedRun, '--workspace', root], { repository: false });
    let current = await queryWorkspace(cli, ['brd', 'review', 'status', selectedRun], root);
    let busy = false; let actor;
    let controller;
    const update = async () => {
      current = await queryWorkspace(cli, ['brd', 'review', 'status', selectedRun], root);
      controller.update(current); await refresh(false); return current;
    };
    const reportPanelError = async error => {
      const message = conciseError(error);
      output.appendLine(`ERROR [recommendation-review]: ${message}`);
      const action = await vscode.window.showErrorMessage(`CIS: ${message}`, 'Show output');
      if (action === 'Show output') output.show(true);
    };
    const continueAfterRecommendationSet = async () => {
      let progress = brdRecommendationProgress(current);
      if (progress.pending > 0 || progress.state === 'applied') return false;
      actor ||= await actorIdentity();
      if (!actor) return false;
      if (progress.state !== 'approved') {
        await cli.runForeground('Finalize approved BRD recommendation set',
          ['brd', 'review', 'approve', selectedRun, '--reviewer', actor, '--workspace', root], { repository: false });
        await update();
        progress = brdRecommendationProgress(current);
      }
      if (progress.state !== 'approved') throw new Error('The completed recommendation set could not be locked.');
      if (progress.accepted > 0) await vscode.commands.executeCommand('cis.brdAgentRevise', selectedRun);
      else await vscode.window.showInformationMessage('The legacy recommendation set contains no approved remediation. No agent revision or secondary review is required.');
      return true;
    };
    controller = openRecommendationReviewPanel(vscode, selectedRun, current, async (action, value) => {
      if (busy) return;
      busy = true;
      try {
        if (action === 'open-review') {
          const canonical = current.canonicalPath;
          if (!canonical) throw new Error('The canonical review disposition record is unavailable.');
          await openCanonical(authority, canonical, false); return;
        }
        actor ||= await actorIdentity();
        if (!actor) return;
        if (action === 'accept-all') {
          const pending = (current.disposition?.findings || [])
            .filter(item => String(item.decision).toLowerCase() === 'pending');
          if (!pending.length) { await update(); return; }
          const confirmation = await vscode.window.showWarningMessage(
            `Approve all ${pending.length} pending BRD recommendations exactly as written? Any recommendations already modified and approved remain unchanged.`,
            { modal: true }, 'Approve all as written');
          if (confirmation !== 'Approve all as written') return;
          await cli.runForeground(`Approve all ${pending.length} pending BRD recommendations`,
            brdRecommendationAcceptAllArgs(selectedRun, actor, root),
            { repository: false });
          await update();
          await continueAfterRecommendationSet();
          return;
        }
        if (action === 'accept' || action === 'modify') {
          const finding = (current.disposition?.findings || []).find(item => item.id === value && item.decision === 'pending');
          if (!finding) { await update(); return; }
          let approvedRecommendation;
          if (action === 'modify') {
            approvedRecommendation = await vscode.window.showInputBox({
              title: `Modify and approve ${finding.id}`,
              prompt: 'Edit the exact remediation text that the revision agent will be authorized to apply.',
              value: finding.approvedRecommendation || finding.recommendation || '',
              valueSelection: [0, String(finding.approvedRecommendation || finding.recommendation || '').length],
              ignoreFocusOut: true,
              validateInput: input => input.trim() ? undefined : 'Approved recommendation text is required.',
            });
            if (approvedRecommendation === undefined) return;
          }
          const args = brdRecommendationDecisionArgs(selectedRun, finding.id, actor, root,
            action === 'modify' ? approvedRecommendation : undefined);
          await cli.runForeground(`${action === 'modify' ? 'Modify and approve' : 'Approve'} ${finding.id}`,
            args, { repository: false });
          await update();
          await continueAfterRecommendationSet();
          return;
        }
      } catch (error) { await reportPanelError(error); }
      finally { busy = false; }
    });
    if (brdRecommendationProgress(current).pending === 0
      && brdRecommendationProgress(current).state !== 'applied') {
      busy = true;
      try { await continueAfterRecommendationSet(); }
      catch (error) { await reportPanelError(error); }
      finally { busy = false; }
    }
  });
  command('cis.brdAgentRevise', async reviewRunId => {
    const root = authority.root();
    if (typeof reviewRunId !== 'string' || !reviewRunId) throw new Error('Select an approved BRD review disposition set first.');
    const [available, sourceReview] = await Promise.all([
      cli.query(['agent', 'providers']), cli.query(['agent', 'show', reviewRunId, '--summary']),
    ]);
    const reviewProvider = sourceReview.run?.manifest?.provider;
    const candidates = (available.providers || []).filter(provider => provider.directExecution
      && provider.modes?.includes('implement') && provider.permissions?.includes('workspace-write')
      && provider.id !== reviewProvider);
    if (!candidates.length) throw new Error(`No implementation provider is available that differs from review provider ${reviewProvider || 'unknown'}.`);
    const providerItem = await vscode.window.showQuickPick(candidates.map(provider => ({
      label: provider.displayName, description: `Apply only the exact approved recommendations; ${provider.description}`, provider,
    })), { placeHolder: 'Select the agent that will apply the approved recommendations' });
    if (!providerItem) return;
    let diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
    if (diagnosis.diagnoses?.[0]?.available === false) {
      const providerDiagnosis = diagnosis.diagnoses[0];
      const canAuthenticate = (providerDiagnosis.capabilities || []).some(capability => capability.startsWith('authentication:'));
      if (String(providerDiagnosis.status).startsWith('authentication-') && canAuthenticate) {
        const setup = await vscode.window.showWarningMessage(
          `${providerItem.provider.displayName} is installed but not authenticated. Start its provider-native login now?`,
          { modal: true }, 'Authenticate provider');
        if (setup === 'Authenticate provider') {
          await vscode.commands.executeCommand('cis.agentAuthenticate', providerItem.provider.id);
          diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
        }
      }
      if (diagnosis.diagnoses?.[0]?.available === false) {
        showQuery(`Agent provider: ${providerItem.provider.id}`, diagnosis); return;
      }
    }
    const transport = providerItem.provider.transports?.length > 1
      ? await vscode.window.showQuickPick(providerItem.provider.transports, { placeHolder: 'Select the provider transport' })
      : providerItem.provider.transports?.[0];
    if (!transport) return;
    const requestPolicy = providerItem.provider.supportsInteractivePermissions
      ? await vscode.window.showQuickPick([
        { label: 'Deny approval requests', description: 'Unsupported operations fail visibly.', approve: false },
        { label: 'Approve inside revision workspace', description: 'Only requests inside the isolated one-file scope may be approved.', approve: true },
      ], { placeHolder: 'Select the permission-request policy' }) : { approve: false };
    if (!requestPolicy) return;
    const actor = await actorIdentity(); if (!actor) return;
    const args = ['agent', 'revise', 'brd', '--review', reviewRunId, '--provider', providerItem.provider.id,
      '--transport', transport, '--actor', actor];
    if (requestPolicy.approve) args.push('--approve-requests');
    await cli.runForeground(`Apply approved BRD recommendations with ${providerItem.provider.displayName}`, args,
      { cancellable: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    await refresh(false);
    await openCanonical(authority, currentProductPaths(vscode, authority).brd, false);
    await vscode.window.showInformationMessage('Approved recommendations were applied. Select a different provider for the required secondary review.');
    await vscode.commands.executeCommand('cis.brdAgentReview');
  });
  command('cis.brdAnswerQuestions', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    let current = await queryWorkspace(cli, ['brd', 'questions', 'guidance'], root);
    let controller; let busy = false; let actor; let completionHandled = current.unansweredCount === 0;
    const update = async () => {
      current = await queryWorkspace(cli, ['brd', 'questions', 'guidance'], root);
      controller.update(current); await refresh(false); return current;
    };
    const reportPanelError = async error => {
      const message = conciseError(error);
      output.appendLine(`ERROR [brd-questions]: ${message}`);
      const action = await vscode.window.showErrorMessage(`CIS: ${message}`, 'Show output');
      if (action === 'Show output') output.show(true);
    };
    const availableAi = async () => {
      try { return await cli.query(['ai', 'status']); }
      catch { return { providers: [] }; }
    };
    const generateSuggestions = async (interactiveRemote) => {
      const ai = await availableAi();
      const local = (ai.providers || []).find(provider => provider.isLocal && provider.isAvailable);
      if (local) {
        await cli.runForeground(`Generate advisory BRD answers with local ${local.name}`,
          ['brd', 'questions', 'suggest', '--workspace', root], { repository: false, cancellable: true });
        await update(); return true;
      }
      if (!interactiveRemote) return false;
      const remote = (ai.providers || []).filter(provider => !provider.isLocal && provider.isAvailable);
      if (!remote.length) throw new Error('No local AI provider is available. Start Ollama or configure an explicit remote text-generation provider.');
      const selected = await vscode.window.showQuickPick(remote.map(provider => ({
        label: provider.name, description: provider.detail || provider.endpoint || 'Configured remote AI provider', provider,
      })), { placeHolder: 'Select the remote provider for advisory answer suggestions' });
      if (!selected) return false;
      const approved = await vscode.window.showWarningMessage(
        `Allow CIS to send the displayed BRD questions and bounded context excerpts to ${selected.provider.name}? Suggestions remain advisory and no answer is recorded automatically.`,
        { modal: true }, 'Allow remote suggestion');
      if (approved !== 'Allow remote suggestion') return false;
      await cli.runForeground(`Generate advisory BRD answers with ${selected.provider.name}`,
        ['brd', 'questions', 'suggest', '--provider', selected.provider.name, '--allow-remote', '--workspace', root],
        { repository: false, cancellable: true });
      await update(); return true;
    };
    const saveAnswer = async (questionId, answer) => {
      const question = (current.questions || []).find(item => item.id === questionId);
      if (!question) throw new Error(`BRD question '${questionId}' is no longer current.`);
      const normalized = String(answer || '').trim();
      if (!normalized) throw new Error('A substantive answer is required.');
      if (normalized.length > 16_384) throw new Error('The answer exceeds the 16 KiB limit.');
      actor ||= await actorIdentity(); if (!actor) return false;
      const previouslyUnanswered = current.unansweredCount ?? (current.questions || [])
        .filter(item => String(item.status).toLowerCase() === 'unanswered').length;
      await cli.runForeground(`Answer ${question.id}`,
        ['brd', 'questions', 'answer', question.id, '--answer', normalized, '--actor', actor,
          '--workspace', root], { repository: false });
      await update();
      const remaining = current.unansweredCount ?? (current.questions || [])
        .filter(item => String(item.status).toLowerCase() === 'unanswered').length;
      if (!completionHandled && previouslyUnanswered > 0 && remaining === 0) {
        completionHandled = true;
        await refresh(false);
        await vscode.window.showInformationMessage('All BRD questions are answered. CIS will now update the BRD from those decisions before independent review.');
        await vscode.commands.executeCommand('cis.brdAgentIncorporateQuestions');
      }
      return true;
    };
    controller = openBrdQuestionsPanel(vscode, current, async (action, value) => {
      if (busy) return; busy = true;
      try {
        if (action === 'open-brd') { await openCanonical(authority, paths.brd, false); return; }
        if (action === 'generate-suggestions') { await generateSuggestions(true); return; }
        if (action === 'accept-suggestion') {
          const question = (current.questions || []).find(item => item.id === value);
          if (!question?.suggestedAnswer) { await update(); return; }
          await saveAnswer(question.id, question.suggestedAnswer); return;
        }
        if (action === 'save-answer') {
          let payload;
          try { payload = JSON.parse(value || '{}'); }
          catch { throw new Error('The question answer submitted by the page is malformed.'); }
          if (typeof payload.id !== 'string' || typeof payload.answer !== 'string')
            throw new Error('The question answer submitted by the page is invalid.');
          await saveAnswer(payload.id, payload.answer); return;
        }
      } catch (error) { await reportPanelError(error); }
      finally { busy = false; }
    });
  });
  command('cis.technicalIntentInferFromProject', () => oneBusinessDraft(() => inferExistingTechnicalDocument('technical-intent')));
  command('cis.solutionDesignInferFromProject', () => oneBusinessDraft(() => inferExistingTechnicalDocument('solution-design')));
  async function inferExistingTechnicalDocument(kind) {
    const architecture = kind === 'solution-design';
    const label = architecture ? 'solution architecture' : 'technical intent';
    const root = authority.root();
    const definition = await queryWorkspace(cli, ['definition', 'status'], root);
    const repositories = definition.businessInference?.repositories || [];
    if (!repositories.length) throw new Error(`Import the product-owned implementation repositories before inferring ${label}.`);
    const selected = await vscode.window.showQuickPick(repositories.map(repository => ({
      label: repository.id, description: repository.repositoryPath, picked: true, repository,
    })), { canPickMany: true, placeHolder: `Infer ${label} from these product-owned repositories` });
    if (!selected?.length) return;
    if (selected.length > 10) throw new Error('Select at most 10 repositories for one inference run.');
    const available = await cli.query(['agent', 'providers']);
    const providers = (available.providers || []).filter(provider => provider.directExecution
      && provider.modes?.includes('implement') && provider.permissions?.includes('workspace-write'));
    if (!providers.length) throw new Error(`No agent provider supports isolated ${label} drafting. Diagnose providers in Runs.`);
    const chosen = await vscode.window.showQuickPick(providers.map(provider => ({ label: provider.displayName, description: provider.description, provider })),
      { placeHolder: `Select the ${label} author` });
    if (!chosen) return;
    const transport = chosen.provider.transports?.length > 1
      ? await vscode.window.showQuickPick(chosen.provider.transports, { placeHolder: 'Select the provider transport' })
      : chosen.provider.transports?.[0];
    if (!transport) return;
    const actor = await actorIdentity(); if (!actor) return;
    const confirmation = `Infer ${label}`;
    const scope = architecture ? 'BRD, technical intent, questionnaire, architecture bundle and dictionary projections' : 'BRD, technical questionnaire and scaffold';
    const output = architecture ? 'the overall solution design and component sheet as one review-only bundle, with four visual diagram models' : 'a protected technical-intent draft';
    const confirmed = await vscode.window.showWarningMessage(
      `Send the current ${scope} and bounded implementation/test snapshots from ${selected.map(item => item.label).join(', ')} to ${chosen.provider.displayName}? CIS excludes credentials, dependencies, manifests and deployment configuration and redacts likely secret literals. CIS applies only ${output}. Observed behavior, proposed changes and missing evidence remain distinct. No approvals or human decisions are recorded.`,
      { modal: true }, confirmation);
    if (confirmed !== confirmation) return;
    if (authority.root() !== root) throw new Error('The CIS authority changed. Reopen inference for the selected authority.');
    const args = ['agent', 'author', kind, '--provider', chosen.provider.id, '--transport', transport, '--actor', actor, '--repo', root];
    for (const item of selected) args.push('--reference', item.repository.repositoryPath);
    await cli.runForeground(`Infer ${label} from existing repositories`, args, { repository: false, cancellable: false });
    if (architecture) await cli.runForeground('Render inferred architecture diagrams', ['definition', 'prepare', '--page', 'architecture', '--workspace', root], { repository: false });
    await cli.runForeground(`Refresh ${label} authority graph`, ['graph', 'build', '--repo', root], { repository: false });
    await refresh(false);
    const paths = currentProductPaths(vscode, authority);
    await openCanonical(authority, architecture ? paths.overallSolutionDesign : paths.technicalIntent, architecture);
  }
  command('cis.technicalIntentInit', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    await cli.runForeground('Generate technical intent skeleton', ['technical-intent', 'init', '--workspace', root], { repository: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    await refresh(false); await openCanonical(authority, paths.technicalIntent, false);
  });
  command('cis.technicalIntentQuestions', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    let current = await queryWorkspace(cli, ['technical-intent', 'questions', 'status'], root);
    if (current.status === 'missing' || !Array.isArray(current.questions) || !current.questions.length) {
      await cli.runForeground('Initialize high-level technical questionnaire',
        ['technical-intent', 'questions', 'init', '--workspace', root], { repository: false });
      current = await queryWorkspace(cli, ['technical-intent', 'questions', 'status'], root);
    }
    let controller; let busy = false; let actor; let completionHandled = current.complete === true;
    const update = async () => {
      current = await queryWorkspace(cli, ['technical-intent', 'questions', 'status'], root);
      controller.update(current); await refresh(false); return current;
    };
    const save = async (id, value) => {
      const question = (current.questions || []).find(item => item.id === id);
      if (!question) throw new Error(`Technical question '${id}' is no longer current.`);
      const wasResolved = ['answered', 'derived'].includes(String(question.status || '').toLowerCase());
      const answer = String(value || '').trim();
      if (!answer) throw new Error('A substantive technical direction is required.');
      if (answer.length > 16_384) throw new Error('The technical direction exceeds the 16 KiB limit.');
      actor ||= await actorIdentity(); if (!actor) return;
      await cli.runForeground(`Record ${id}`,
        ['technical-intent', 'questions', 'answer', id, '--answer', answer, '--actor', actor, '--workspace', root],
        { repository: false, details: false });
      await update();
      if (current.complete === true && current.current === true && (!completionHandled || wasResolved)) {
        completionHandled = true;
        await vscode.window.showInformationMessage(wasResolved
          ? 'Technical direction updated. CIS will now regenerate the technical intent and component map.'
          : 'High-level technical direction is complete. CIS will now generate the technical intent and component map.');
        await vscode.commands.executeCommand('cis.technicalIntentInit');
      }
    };
    controller = openTechnicalIntentQuestionsPanel(vscode, current, async (action, value) => {
      if (busy) return; busy = true;
      try {
        if (action === 'open-questionnaire') { await openCanonical(authority, paths.technicalQuestionnaire, false); return; }
        if (action === 'save-answer') {
          let payload;
          try { payload = JSON.parse(value || '{}'); } catch { throw new Error('The technical answer submitted by the page is malformed.'); }
          if (typeof payload.id !== 'string' || typeof payload.answer !== 'string')
            throw new Error('The technical answer submitted by the page is invalid.');
          await save(payload.id, payload.answer);
        }
      } catch (error) {
        const message = conciseError(error); output.appendLine(`ERROR [technical-questions]: ${message}`);
        const action = await vscode.window.showErrorMessage(`CIS: ${message}`, 'Show output');
        if (action === 'Show output') output.show(true);
      } finally { busy = false; }
    });
  });
  command('cis.technicalIntentValidate', async () => validateProductDocument(cli, refresh, showQuery,
    'Technical intent validation', ['technical-intent', 'validate'], authority.root()));
  command('cis.technicalIntentApprove', async () => approveProductDocument(cli, refresh, showQuery, {
    label: 'technical intent', validate: ['technical-intent', 'validate'], approve: ['technical-intent', 'approve'],
  }, authority.root()));
  command('cis.solutionDesignInit', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    await cli.runForeground('Generate overall solution design and component sheet',
      ['solution-design', 'init', '--workspace', root], { repository: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    await refresh(false);
    await openCanonical(authority, paths.overallSolutionDesign, false);
    await openCanonical(authority, paths.componentSheet, true);
  });
  command('cis.solutionDesignValidate', async () => validateProductDocument(cli, refresh, showQuery,
    'Solution-design bundle validation', ['solution-design', 'validate'], authority.root()));
  command('cis.solutionDesignApprove', async () => approveProductDocument(cli, refresh, showQuery, {
    label: 'overall solution design and component sheet', validate: ['solution-design', 'validate'], approve: ['solution-design', 'approve'],
  }, authority.root()));
  command('cis.uiDirectionQuestions', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    let current = await queryWorkspace(cli, ['ui-direction', 'questions', 'status'], root);
    if (current.status === 'missing' || !Array.isArray(current.questions) || !current.questions.length) {
      await cli.runForeground('Initialize high-level UI questionnaire',
        ['ui-direction', 'questions', 'init', '--workspace', root], { repository: false });
      current = await queryWorkspace(cli, ['ui-direction', 'questions', 'status'], root);
    }
    let controller; let busy = false; let actor; let completionHandled = current.complete === true;
    const update = async () => {
      current = await queryWorkspace(cli, ['ui-direction', 'questions', 'status'], root);
      controller.update(current); await refresh(false); return current;
    };
    const save = async (id, value) => {
      const question = (current.questions || []).find(item => item.id === id);
      if (!question) throw new Error(`UI-direction question '${id}' is no longer current.`);
      const wasResolved = ['answered', 'derived'].includes(String(question.status || '').toLowerCase());
      const answer = String(value || '').trim();
      if (!answer) throw new Error('A substantive UI direction is required.');
      if (answer.length > 16_384) throw new Error('The UI direction exceeds the 16 KiB limit.');
      actor ||= await actorIdentity(); if (!actor) return;
      await cli.runForeground(`Record ${id}`,
        ['ui-direction', 'questions', 'answer', id, '--answer', answer, '--actor', actor, '--workspace', root],
        { repository: false, details: false });
      await update();
      if (current.complete === true && current.current === true && (!completionHandled || wasResolved)) {
        completionHandled = true;
        await vscode.window.showInformationMessage(wasResolved
          ? 'UI direction updated. CIS will now regenerate the high-level UI document.'
          : 'High-level UI choices are complete. CIS will now generate the UI-direction document.');
        await vscode.commands.executeCommand('cis.uiDirectionInit');
      }
    };
    controller = openUiDirectionQuestionsPanel(vscode, current, async (action, value) => {
      if (busy) return; busy = true;
      try {
        if (action === 'open-questionnaire') { await openCanonical(authority, paths.uiDirectionQuestionnaire, false); return; }
        if (action === 'save-answer') {
          let payload;
          try { payload = JSON.parse(value || '{}'); } catch { throw new Error('The UI-direction answer submitted by the page is malformed.'); }
          if (typeof payload.id !== 'string' || typeof payload.answer !== 'string')
            throw new Error('The UI-direction answer submitted by the page is invalid.');
          await save(payload.id, payload.answer);
        }
      } catch (error) {
        const message = conciseError(error); output.appendLine(`ERROR [ui-direction-questions]: ${message}`);
        const action = await vscode.window.showErrorMessage(`CIS: ${message}`, 'Show output');
        if (action === 'Show output') output.show(true);
      } finally { busy = false; }
    });
  });
  command('cis.uiDirectionInit', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    await cli.runForeground('Generate high-level UI direction', ['ui-direction', 'init', '--workspace', root], { repository: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    await refresh(false); await openCanonical(authority, paths.uiDirection, false);
  });
  command('cis.uiDirectionValidate', async () => validateProductDocument(cli, refresh, showQuery,
    'High-level UI-direction validation', ['ui-direction', 'validate'], authority.root()));
  command('cis.uiDirectionApprove', async () => approveProductDocument(cli, refresh, showQuery, {
    label: 'high-level UI direction', validate: ['ui-direction', 'validate'], approve: ['ui-direction', 'approve'],
  }, authority.root()));
  command('cis.backlogBuild', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    if (!await ensureUiDirectionReady(vscode, cli, refresh, root)) return;
    await cli.runForeground('Build high-level backlog', ['brd', 'backlog', 'build', '--workspace', root], { repository: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    await refresh(false); await openCanonical(authority, paths.backlog, false);
  });
  command('cis.backlogValidate', async () => {
    const root = authority.root();
    if (!await ensureUiDirectionReady(vscode, cli, refresh, root)) return;
    return validateProductDocument(cli, refresh, showQuery,
      'High-level backlog validation', ['brd', 'backlog', 'validate'], root);
  });
  command('cis.backlogApprove', async () => {
    const root = authority.root();
    if (!await ensureUiDirectionReady(vscode, cli, refresh, root)) return;
    return approveProductDocument(cli, refresh, showQuery, {
      label: 'high-level backlog', validate: ['brd', 'backlog', 'validate'], approve: ['brd', 'backlog', 'approve'],
    }, root);
  });
  command('cis.featureStart', async itemId => {
    const root = authority.root();
    if (!await ensureUiDirectionReady(vscode, cli, refresh, root)) return;
    const selected = itemId || await selectBacklogItem(cli, root, false);
    if (!selected) return;
    await cli.runForeground(`Start feature specification ${selected}`,
      ['brd', 'backlog', 'start', '--item', selected, '--workspace', root], { repository: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    const result = await queryWorkspace(cli, ['brd', 'feature', 'status', '--item', selected], root);
    await refresh(false);
    const drafted = await vscode.commands.executeCommand('cis.featureAgentDraft', selected);
    if (!drafted && result.relativePath) await openCanonical(authority, resolveWithin(root, result.relativePath), false);
  });
  command('cis.featureAgentDraft', async itemId => {
    const root = authority.root();
    if (!await ensureUiDirectionReady(vscode, cli, refresh, root)) return false;
    const selected = itemId || await selectBacklogItem(cli, root, true); if (!selected) return false;
    const feature = await queryWorkspace(cli, ['brd', 'feature', 'status', '--item', selected], root);
    if (!feature.relativePath) throw new Error(`Start ${selected} before assigning its feature draft.`);
    const available = await cli.query(['agent', 'providers']);
    const candidates = (available.providers || []).filter(provider => provider.directExecution
      && provider.modes?.includes('implement') && provider.permissions?.includes('workspace-write'));
    if (!candidates.length) throw new Error('No available agent provider supports isolated workspace-write feature drafting. Open Runs to diagnose Codex or Claude.');
    const providerItem = await vscode.window.showQuickPick(candidates.map(provider => ({
      label: provider.displayName,
      description: `Draft only ${selected} from current governed product and technical evidence; ${provider.description}`,
      provider,
    })), { placeHolder: `Select the agent that will draft ${selected}` });
    if (!providerItem) return false;
    let diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
    if (diagnosis.diagnoses?.[0]?.available === false) {
      const providerDiagnosis = diagnosis.diagnoses[0];
      const canAuthenticate = (providerDiagnosis.capabilities || []).some(capability => capability.startsWith('authentication:'));
      if (String(providerDiagnosis.status).startsWith('authentication-') && canAuthenticate) {
        const setup = await vscode.window.showWarningMessage(
          `${providerItem.provider.displayName} is installed but not authenticated. Start its provider-native login now?`,
          { modal: true }, 'Authenticate provider');
        if (setup === 'Authenticate provider') {
          await vscode.commands.executeCommand('cis.agentAuthenticate', providerItem.provider.id);
          diagnosis = await cli.query(['agent', 'provider', 'diagnose', providerItem.provider.id]);
        }
      }
      if (diagnosis.diagnoses?.[0]?.available === false) {
        showQuery(`Agent provider: ${providerItem.provider.id}`, diagnosis); return false;
      }
    }
    const transport = providerItem.provider.transports?.length > 1
      ? await vscode.window.showQuickPick(providerItem.provider.transports, { placeHolder: 'Select the provider transport' })
      : providerItem.provider.transports?.[0];
    if (!transport) return false;
    const requestPolicy = providerItem.provider.supportsInteractivePermissions
      ? await vscode.window.showQuickPick([
        { label: 'Deny approval requests', description: 'Unsupported operations fail visibly.', approve: false },
        { label: 'Approve inside feature workspace', description: 'Only requests inside the isolated one-file authoring scope may be approved.', approve: true },
      ], { placeHolder: 'Select the permission-request policy' }) : { approve: false };
    if (!requestPolicy) return false;
    const actor = await actorIdentity(); if (!actor) return false;
    const confirmed = await vscode.window.showWarningMessage(
      `Draft ${selected} with ${providerItem.provider.displayName}? CIS will expose the current approved BRD, technical intent, solution design, component sheet, UI direction, and applicable governance in an isolated scratch repository. Only the feature specification may be changed, and deterministic validation plus human approval remain required.`,
      { modal: true }, 'Start feature draft');
    if (confirmed !== 'Start feature draft') return false;
    const args = ['agent', 'author', 'feature', '--item', selected, '--provider', providerItem.provider.id,
      '--transport', transport, '--actor', actor];
    if (requestPolicy.approve) args.push('--approve-requests');
    await cli.runForeground(`Draft ${selected} feature specification with agent`, args, { cancellable: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    const updated = await queryWorkspace(cli, ['brd', 'feature', 'status', '--item', selected], root);
    await refresh(false);
    if (updated.relativePath) await openCanonical(authority, resolveWithin(root, updated.relativePath), false);
    await vscode.window.showInformationMessage(`${selected} was drafted from the governed product baseline. Review it, resolve any open questions, and validate it before approval.`);
    return true;
  });
  command('cis.featureValidate', async itemId => {
    const root = authority.root();
    if (!await ensureUiDirectionReady(vscode, cli, refresh, root)) return;
    const selected = itemId || await selectBacklogItem(cli, root, true); if (!selected) return;
    return validateProductDocument(cli, refresh, showQuery, `${selected} feature validation`,
      ['brd', 'feature', 'validate', '--item', selected], root);
  });
  command('cis.featureApprove', async itemId => {
    const root = authority.root();
    if (!await ensureUiDirectionReady(vscode, cli, refresh, root)) return;
    const selected = itemId || await selectBacklogItem(cli, root, true); if (!selected) return;
    const approved = await approveProductDocument(cli, refresh, showQuery, {
      label: `${selected} feature specification`, validate: ['brd', 'feature', 'validate', '--item', selected],
      approve: ['brd', 'feature', 'approve', '--item', selected],
      deferRefresh: true,
    }, root);
    if (!approved) return;
    return continueApprovedFeatureDelivery(vscode, cli, refresh, root, selected);
  });
  command('cis.repoDoctor', async () => {
    const doctorRoot = authority.root();
    let current = await cli.query(['repo', 'doctor'], { acceptStructuredFailure: true });
    let controller;
    let running = false;
    const reportDoctorError = async error => {
      const message = conciseError(error);
      output.appendLine(`ERROR [repository-doctor]: ${message}`);
      const selected = await vscode.window.showErrorMessage(`CIS: ${message}`, 'Show output');
      if (selected === 'Show output') output.show(true);
    };
    controller = openDoctorPanel(vscode, current, async (action, value) => {
      try {
        if (running) return;
        if (authority.root() !== doctorRoot)
          throw new Error('The CIS authority changed. Reopen Repository Doctor before using this report.');
        if (action === 'refresh') {
          current = await cli.query(['repo', 'doctor', '--refresh'], { acceptStructuredFailure: true });
          controller.update(current); await refresh(false, true); return;
        }
        if (!['copy-fix', 'run-fix'].includes(action)) return;
        const finding = (current.findings || []).find(item => doctorFixId(item) === value);
        const fixCommand = typeof finding?.fixCommand === 'string' ? finding.fixCommand.trim() : '';
        if (!fixCommand || fixCommand.length > 4096 || /[\u0000-\u001f\u007f]/u.test(fixCommand))
          throw new Error(`Doctor finding '${value}' does not contain a safe copyable command.`);
        if (action === 'copy-fix') {
          await vscode.env.clipboard.writeText(fixCommand);
          await vscode.window.showInformationMessage(`Copied the suggested command for ${finding.code}. Review it before running.`);
          return;
        }
        const args = parseDoctorCommand(fixCommand);
        assertDoctorScope(args, doctorRoot);
        running = true;
        let commandError;
        try {
          await cli.runForeground(`Run Doctor fix: ${finding.code}`, args, { cancellable: true });
        } catch (error) { commandError = error; }
        try {
          cli.clearQueryCache?.();
          if (authority.root() === doctorRoot) {
            current = await cli.query(['repo', 'doctor', '--refresh'], { acceptStructuredFailure: true });
            controller.update(current);
          }
          await refresh(false, true);
        } catch (error) {
          if (!commandError) throw error;
          output.appendLine(`Doctor refresh failed: ${conciseError(error)}`);
        } finally { running = false; }
        if (commandError) throw commandError;
      } catch (error) { await reportDoctorError(error); }
    }, async value => {
      try { await openReportedPath(value, true); }
      catch (error) { await reportDoctorError(error); }
    });
  });
  command('cis.graphBuild', async () => { await cli.runForeground('Build CIS context graph', ['graph', 'build']); await refresh(false); });
  command('cis.indexBuild', async () => {
    await cli.runForeground('Build CIS routing index', ['index', 'build'], { cancellable: true });
    await refresh(false);
  });
  command('cis.contextSearch', async () => {
    const query = await vscode.window.showInputBox({ prompt: 'Search bounded CIS context', placeHolder: 'Terms, symbol, contract, or component' });
    if (!query) return;
    const result = await cli.query(['context', 'search', '--text', query, '--limit', '100']);
    openContextPanel(vscode, `Context: ${query}`, result,
      async (action, value) => { if (action === 'related') await vscode.commands.executeCommand('cis.graphRelated', value); },
      value => openReportedPath(value, false));
  });
  command('cis.graphRelated', async input => {
    const nodeId = typeof input === 'string' && input ? input
      : await vscode.window.showInputBox({ prompt: 'Exact graph node identity' });
    if (!nodeId) return;
    const result = await cli.query(['graph', 'related', '--id', nodeId, '--depth', '2', '--limit', '200']);
    openContextPanel(vscode, `Relationships: ${nodeId}`, result,
      async (action, value) => { if (action === 'related') await vscode.commands.executeCommand('cis.graphRelated', value); },
      value => openReportedPath(value, false));
  });
  command('cis.filterChanges', async () => {
    const value = await vscode.window.showInputBox({ prompt: 'Filter changes by ID, title, or lifecycle', value: providers.get('changes').filter });
    if (value !== undefined) providers.get('changes').setFilter(value);
  });
  command('cis.aiStatus', async () => showQuery('AI provider status', await cli.query(['ai', 'providers'])));
  command('cis.agentProviders', async () => showQuery('Agent providers', await cli.query(['agent', 'providers'])));
  command('cis.agentDiagnose', async provider => showQuery(`Agent provider: ${provider}`, await cli.query(['agent', 'provider', 'diagnose', provider])));
  command('cis.agentAuthenticate', async provider => {
    let selected = typeof provider === 'string' ? provider : undefined;
    if (!selected) {
      const inventory = await cli.query(['agent', 'providers']);
      const supported = (inventory.providers || []).filter(item => item.directExecution).map(item => ({
        label: item.displayName, description: item.id, id: item.id,
      }));
      const picked = await vscode.window.showQuickPick(supported, { placeHolder: 'Provider with CIS-managed native authentication' });
      selected = typeof picked === 'string' ? picked : picked?.id;
    }
    if (!selected) return;
    const diagnosis = await cli.query(['agent', 'provider', 'diagnose', selected]);
    const methods = (diagnosis.diagnoses?.[0]?.capabilities || [])
      .filter(capability => capability.startsWith('authentication:'))
      .map(capability => capability.slice('authentication:'.length));
    if (!methods.length) {
      showQuery(`Agent provider: ${selected}`, diagnosis); return;
    }
    const method = await vscode.window.showQuickPick(methods, {
      placeHolder: 'Select a provider-native authentication method',
    });
    if (!method) return;
    if (method === 'device') output.show(true);
    await cli.runForeground(`Authenticate ${selected}`, ['agent', 'provider', 'authenticate', selected, '--method', method],
      { cancellable: true });
    await refresh(false);
    showQuery(`Agent provider: ${selected}`, await cli.query(['agent', 'provider', 'diagnose', selected]));
  });
  command('cis.agentShow', async runId => {
    const result = await cli.query(['agent', 'show', runId]);
    const statusValue = String(result.run?.manifest?.status || '').toLowerCase().replaceAll('-', '');
    const actions = [];
    if (['prepared', 'starting', 'running', 'awaitingpermission', 'cancelling'].includes(statusValue))
      actions.push({ command: 'cancel', label: 'Cancel run', value: runId });
    if (['failed', 'cancelled', 'interrupted', 'timedout', 'invalidevidence', 'succeeded'].includes(statusValue))
      actions.push({ command: 'resume', label: 'Resume as new attempt', value: runId });
    if (statusValue === 'interrupted') actions.push({ command: 'recover', label: 'Recover orphan', value: runId });
    openRunDetailPanel(vscode, result.run || result, actions, async (action, value) => {
      await vscode.commands.executeCommand(action === 'cancel' ? 'cis.agentCancel' : action === 'recover' ? 'cis.agentRecover' : 'cis.agentResume', value);
    }, value => openReportedPath(value, true));
  });
  command('cis.requestAgentWork', input => requestAgentWork(cli, refresh, input));
  command('cis.agentCancel', run => agentStateCommand(cli, refresh, 'cancel', runIdentity(run)));
  command('cis.agentRecover', run => agentStateCommand(cli, refresh, 'recover', runIdentity(run)));
  command('cis.agentResume', run => resumeAgent(cli, refresh, runIdentity(run)));
  command('cis.workflowStatus', async () => {
    const runId = await vscode.window.showInputBox({ prompt: 'Workflow run ID' }); if (!runId) return;
    showQuery(`Workflow ${runId}`, await cli.query(['workflow', 'status', runId]));
  });
  command('cis.testStatus', async () => {
    const changeId = await vscode.window.showInputBox({ prompt: 'Change ID for executed test traceability', placeHolder: 'CIS-0001' }); if (!changeId) return;
    showQuery(`Test status ${changeId}`, await cli.query(['test', 'status', changeId]));
  });
  command('cis.securityStatus', async () => showQuery('Security status', await cli.query(['security', 'status'])));
  command('cis.verificationStatus', async () => {
    const changeId = await vscode.window.showInputBox({ prompt: 'Change ID to validate', placeHolder: 'CIS-0001' }); if (!changeId) return;
    showQuery(`Verification ${changeId}`, await cli.query(['verify', 'validate', changeId]));
  });
  command('cis.governanceInventory', async kind => {
    const commandArgs = kind === 'skills' ? ['skills', 'inventory'] : kind === 'standards' ? ['standards', 'inventory'] : ['references', 'validate'];
    showQuery(`${kind[0].toUpperCase()}${kind.slice(1)}`, await cli.query(commandArgs));
  });
  command('cis.designReview', async input => {
    const changeId = typeof input === 'string' ? input : input?.id || input?.cis?.id
      || await vscode.window.showInputBox({ prompt: 'Change ID to review', placeHolder: 'CIS-0001' });
    if (!changeId) return;
    const [design, validation] = await Promise.all([cli.query(['design', 'status', changeId]), cli.query(['design', 'validate', changeId])]);
    if (String(validation.status).toLowerCase() !== 'valid') throw new Error('The design manifest is invalid; approval actions remain disabled.');
    const root = authority.root();
    openDesignPanel(vscode, root, changeId, design, artifact => resolveWithin(root, artifact),
      async (decision, value) => vscode.commands.executeCommand('cis.designDecision', { decision, changeId: value }));
  });
  command('cis.designDecision', async ({ decision, changeId }) => {
    if (!['approve', 'reject'].includes(decision) || !changeId) throw new Error('The design decision request is invalid.');
    const actor = await actorIdentity(); if (!actor) return;
    const reason = await vscode.window.showInputBox({ prompt: `${decision === 'approve' ? 'Approval' : 'Rejection'} rationale`,
      validateInput: value => value.trim() ? undefined : 'A rationale is required.' });
    if (!reason) return;
    const confirmed = await vscode.window.showWarningMessage(`${decision === 'approve' ? 'Approve' : 'Reject'} the exact validated ${changeId} design pack?`,
      { modal: true }, 'Confirm decision');
    if (confirmed !== 'Confirm decision') return;
    await cli.runForeground(`${decision} ${changeId} design`, ['design', decision, changeId, '--reviewer', actor, '--reason', reason]);
    await refresh(false);
  });
  command('cis.taskTransition', async input => taskTransition(cli, refresh, input));
  command('cis.openTask', async input => {
    const changeId = input?.changeId || input?.cis?.changeId;
    const supplied = input?.task || input?.cis?.task;
    if (!changeId) throw new Error('A change ID is required to open task detail.');
    const plan = supplied ? undefined : await cli.query(['plan', 'show', changeId]);
    const task = supplied || (plan?.workItems || []).find(item => item.id === input?.taskId);
    if (!task) throw new Error('The selected task is no longer present in the approved plan.');
    openTaskDetailPanel(vscode, changeId, task, async (action, value) => {
      if (action === 'transition') await vscode.commands.executeCommand('cis.taskTransition', { changeId, taskId: value });
      else if (action === 'agent') await vscode.commands.executeCommand('cis.requestAgentWork', { changeId, taskId: value });
    }, value => openReportedPath(value, false));
  });
  command('cis.openChange', async change => {
    const changeId = typeof change === 'string' ? change : change?.id || change?.cis?.id;
    if (!changeId) return;
    const [changeResult, planResult, designResult, decisionResult] = await Promise.all([
      cli.query(['change', 'show', changeId]), cli.query(['plan', 'show', changeId]), cli.query(['design', 'status', changeId]),
      cli.query(['decision', 'list', changeId]),
    ]);
    const overview = projectChangeOverview(changeResult, planResult, designResult, decisionResult);
    openChangeOverviewPanel(vscode, overview, async (action, value) => {
      if (action === 'open-task') {
        const task = overview.tasks.find(item => item.id === value);
        if (!task) throw new Error(`Task ${value} is no longer present in the projected plan.`);
        await vscode.commands.executeCommand('cis.openTask', { changeId, task }); return;
      }
      await vscode.commands.executeCommand(action === 'canonical' ? 'cis.openChangeCanonical'
        : action === 'design' ? 'cis.designReview' : action === 'transition' ? 'cis.taskTransition' : 'cis.requestAgentWork', value);
    }, value => openReportedPath(value, false));
  });
  command('cis.openChangeCanonical', async changeId => {
    const result = await cli.query(['change', 'show', changeId]);
    const file = resolveWithin(authority.root(), `${result.change?.relativePath || result.relativePath}/proposal.md`);
    await openCanonical(authority, file, false);
  });

  const watcherSet = installWatchers(context, authority, () => refresh(true));
  if (typeof vscode.workspace.onDidChangeWorkspaceFolders === 'function')
    context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(() => { watcherSet.reset(); return refresh(true); }));
  if (typeof vscode.workspace.onDidGrantWorkspaceTrust === 'function')
    context.subscriptions.push(vscode.workspace.onDidGrantWorkspaceTrust(() => refresh(false)));
  void refresh(false);

  function showQuery(title, result) { openEvidencePanel(vscode, title, result, [], async () => {}, value => openReportedPath(value, true)); }

  async function openReportedPath(value, allowLocalEvidence) {
    const root = authority.root();
    if (!root || typeof value !== 'string') throw new Error('The reported evidence path is unavailable.');
    const file = resolveWithin(root, value, { allowLocalEvidence });
    if (!file) throw new Error('CIS reported a path outside the permitted evidence boundary.');
    await openCanonical(authority, file, ['erd.md', 'high-level-architecture-diagrams.md', 'overall-solution-design.md', 'component-sheet.md'].includes(path.basename(file).toLowerCase()), allowLocalEvidence);
  }

  async function openCanonical(selectedAuthority, file, preview, allowLocalEvidence = false) {
    const root = selectedAuthority.root();
    if (!root || typeof file !== 'string') throw new Error('The canonical file is unavailable.');
    const relative = path.relative(root, file);
    const contained = resolveWithin(root, relative, { allowLocalEvidence });
    if (!contained || !fs.existsSync(contained)) throw new Error('The canonical file is outside the authority repository or no longer exists.');
    const uri = vscode.Uri.file(contained);
    if (preview) await vscode.commands.executeCommand('markdown.showPreview', uri);
    else await vscode.window.showTextDocument(uri, { preview: false });
  }
}

async function requestAgentWork(cli, refresh, input) {
  const suppliedChange = typeof input === 'string' ? input : input?.changeId || input?.id || input?.cis?.changeId || input?.cis?.id;
  const changeId = suppliedChange || await vscode.window.showInputBox({ prompt: 'Approved CIS change ID', placeHolder: 'CIS-0001' });
  if (!changeId) return;
  const suppliedTask = input?.taskId || input?.task?.id || input?.cis?.taskId;
  const taskId = suppliedTask || await vscode.window.showInputBox({ prompt: 'Eligible task ID', placeHolder: 'WORK-100-BACKOFFICE' });
  if (!taskId) return;
  const [available, plan] = await Promise.all([cli.query(['agent', 'providers']), cli.query(['plan', 'show', changeId])]);
  const task = (plan.workItems || []).find(item => String(item.id).toLowerCase() === taskId.toLowerCase());
  if (!task) throw new Error(`CIS did not report task ${taskId} in the approved plan.`);
  const targets = task.targets || [];
  const target = targets.length > 1 ? await vscode.window.showQuickPick(targets, { placeHolder: 'Select the declared target repository' }) : targets[0];
  if (targets.length && !target) return;
  const providers = (available.providers || []).filter(provider => provider.directExecution);
  const providerItem = await vscode.window.showQuickPick(providers.map(provider => ({ label: provider.displayName, description: provider.description, provider })),
    { placeHolder: 'Select an available agent provider' });
  if (!providerItem) return;
  const mode = await vscode.window.showQuickPick(providerItem.provider.modes || [], { placeHolder: 'Select the declared run mode' });
  if (!mode) return;
  const permission = await vscode.window.showQuickPick(providerItem.provider.permissions || [], { placeHolder: 'Select the maximum permission ceiling' });
  if (!permission) return;
  const transport = (providerItem.provider.transports || []).length > 1
    ? await vscode.window.showQuickPick(providerItem.provider.transports, { placeHolder: 'Select the CIS provider transport' })
    : providerItem.provider.transports?.[0];
  if (!transport) return;
  const requestPolicy = providerItem.provider.supportsInteractivePermissions && permission === 'workspace-write'
    ? await vscode.window.showQuickPick([
      { label: 'Deny approval requests', description: 'Unsupported operations fail visibly; no request is approved automatically.', approve: false },
      { label: 'Approve within ceiling', description: 'CIS may approve contained command or file requests; network and dynamic grants remain denied.', approve: true },
    ], { placeHolder: 'Select the explicit permission-request policy' })
    : { label: 'Deny approval requests', approve: false };
  if (!requestPolicy) return;
  const actor = await actorIdentity(); if (!actor) return;
  await cli.query(['agent', 'prepare', changeId, taskId, '--provider', providerItem.provider.id]);
  const confirmation = await confirmAgentRequest(vscode, {
    changeId, taskId, target, providerId: providerItem.provider.id, providerName: providerItem.provider.displayName,
    mode, permission, transport, actor, approveRequests: requestPolicy.approve,
    isolation: permission === 'workspace-write' ? 'Detached worktree' : 'Read-only authority',
  });
  if (confirmation !== 'Start governed work') return;
  const args = ['agent', 'run', changeId, taskId, '--provider', providerItem.provider.id,
    '--mode', mode, '--permission', permission, '--transport', transport, '--actor', actor];
  if (target) args.push('--target', target);
  if (requestPolicy.approve) args.push('--approve-requests');
  await cli.runForeground(`CIS agent: ${changeId}/${taskId}`, args, { cancellable: false });
  await refresh(false);
}

function currentProductPaths(vscodeApi, authority) {
  const root = authority.root();
  if (!root) throw new Error('Select the CIS authority repository first.');
  const metadata = repositoryMetadata(root, vscodeApi.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
  if (!metadata.initialized) throw new Error('Initialize the repository before managing product definition.');
  return productPaths(root, metadata);
}

async function queryWorkspace(cli, args, root) {
  try { return await cli.query([...args, '--workspace', root], {
    repository: false, acceptStructuredFailure: true, interactive: true,
    ...(args[0] === 'definition' ? { timeout: 300_000 } : {}),
  }); }
  catch (error) {
    if (error?.data && typeof error.data === 'object') return error.data;
    throw error;
  }
}

async function definitionWizardModel(cli, root, baseResult) {
  const safe = async args => {
    try { return await queryWorkspace(cli, args, root); }
    catch (error) { return error?.data && typeof error.data === 'object' ? error.data : {}; }
  };
  const base = baseResult || await queryWorkspace(cli, ['definition', 'status'], root);
  if (!Array.isArray(base.pages) || !base.pages.length)
    throw new Error(`The configured CIS CLI could not load the definition wizard. ${
      (base.errors || base.diagnostics || []).join(' ') || 'Check cis.executablePath points to the current CIS build.'}`);
  const brdQuestions = await safe(['brd', 'questions', 'guidance']);
  const technicalQuestions = base.technicalQuestions ?? await safe(['technical-intent', 'questions', 'status']);
  const uiQuestions = base.uiQuestions ?? await safe(['ui-direction', 'questions', 'status']);
  return { ...base, brdQuestions, technicalQuestions, uiQuestions };
}

async function validateProductDocument(cli, refresh, showQuery, title, args, root) {
  await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
  const result = await queryWorkspace(cli, args, root);
  showQuery(title, compactValidationResult(result));
  await refresh(false);
  return result;
}

async function approveProductDocument(cli, refresh, showQuery, definition, root) {
  await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
  const validation = await queryWorkspace(cli, definition.validate, root);
  const readiness = stateOf(validation);
  if (!isReadyForApproval(readiness)) {
    showQuery(`${definition.label} validation`, compactValidationResult(validation));
    throw new Error(`${definition.label} is not ready for approval. Complete the reported validation gaps first.`);
  }
  const actor = await actorIdentity(); if (!actor) return;
  const reason = await vscode.window.showInputBox({
    prompt: `Why are the ${definition.label} accepted?`,
    validateInput: value => value.trim() ? undefined : 'A rationale is required.',
  });
  if (!reason) return;
  const confirmed = await vscode.window.showWarningMessage(
    `Approve the exact validated ${definition.label} as ${actor}?`, { modal: true }, 'Approve');
  if (confirmed !== 'Approve') return;
  await cli.runForeground(`Approve ${definition.label}`,
    [...definition.approve, '--workspace', root, '--reviewer', actor, '--reason', reason.trim()], { repository: false });
  await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
  if (!definition.deferRefresh) await refresh(false);
  return true;
}

async function continueApprovedFeatureDelivery(vscodeApi, cli, refresh, root, itemId) {
  await cli.runForeground(`Reconcile ${itemId} into business traceability`,
    ['brd', 'reconcile', '--workspace', root], { repository: false });
  await cli.runForeground('Refresh technical intent from reconciled business evidence',
    ['technical-intent', 'refresh', '--workspace', root], { repository: false });
  await cli.runForeground('Refresh CIS workspace graph',
    ['graph', 'build', '--workspace', root], { repository: false });

  const definition = await queryWorkspace(cli, ['definition', 'status'], root);
  if (definition.active !== false)
    throw new Error('The consolidated product definition did not remain activated after feature reconciliation.');
  const [feature, backlog, before] = await Promise.all([
    queryWorkspace(cli, ['brd', 'feature', 'status', '--item', itemId], root),
    queryWorkspace(cli, ['brd', 'backlog', 'status'], root),
    cli.query(['change', 'list']),
  ]);
  const featureState = stateOf(feature);
  if (!isActiveCurrent(featureState))
    throw new Error(`${itemId} did not remain Active after BRD traceability reconciliation. Review the reported product-definition currency before delivery planning.`);
  const existing = (before.changes || []).find(change => String(change.status || '').toLowerCase() !== 'closed');
  if (existing) {
    await refresh(false);
    await vscodeApi.commands.executeCommand('cis.openChange', existing);
    return existing;
  }

  const item = (backlog.items || []).find(candidate => String(candidate.id).toUpperCase() === String(itemId).toUpperCase());
  if (!item) throw new Error(`High-level backlog item was not found after approval: ${itemId}`);
  const title = `${item.id}: ${item.outcome || 'Feature delivery'}`;
  const outcome = item.outcome || `Deliver the approved ${item.id} feature specification.`;
  const featureRoot = `${feature.authorityRepositoryId || path.basename(root)}:feature:${String(item.id).toLowerCase()}`;
  const knownIds = new Set((before.changes || []).map(change => change.id));
  await cli.runForeground(`Create ${item.id} delivery change`,
    ['change', 'create', '--title', title, '--outcome', outcome, '--root', featureRoot]);
  const after = await cli.query(['change', 'list'], { cache: false });
  const created = (after.changes || []).find(change => !knownIds.has(change.id)
      && String(change.status || '').toLowerCase() !== 'closed')
    || (after.changes || []).find(change => String(change.status || '').toLowerCase() !== 'closed');
  if (!created) throw new Error(`CIS created no readable delivery change for ${item.id}.`);
  if (!feature.relativePath) throw new Error(`${item.id} has no canonical feature-specification path.`);
  await cli.runForeground(`Analyse ${item.id} delivery impact`,
    ['impact', 'analyse', created.id, '--root', featureRoot]);
  await cli.runForeground(`Derive ${item.id} delivery plan from approved scope`,
    ['plan', 'derive', created.id, '--file', feature.relativePath]);
  await refresh(false);
  await vscodeApi.commands.executeCommand('cis.openChange', created);
  return created;
}

async function ensureSolutionDesignReady(vscodeApi, cli, refresh, root) {
  const result = await queryWorkspace(cli, ['solution-design', 'status'], root);
  const state = stateOf(result);
  if (state.status === 'Active' && state.valid !== false && state.current !== false) return true;
  await refresh(false);
  const selected = await vscodeApi.window.showWarningMessage(
    `The current step is Overall Solution Design. The high-level backlog remains unavailable until the overall design and component sheet are Active and current.${state.detail ? ` ${state.detail}` : ''}`,
    'Generate overall solution design');
  if (selected === 'Generate overall solution design')
    await vscodeApi.commands.executeCommand('cis.solutionDesignInit');
  return false;
}

async function ensureUiDirectionReady(vscodeApi, cli, refresh, root) {
  if (!await ensureSolutionDesignReady(vscodeApi, cli, refresh, root)) return false;
  const result = await queryWorkspace(cli, ['ui-direction', 'status'], root);
  const state = stateOf(result);
  if (state.status === 'Active' && state.valid !== false && state.current !== false) return true;
  await refresh(false);
  const selected = await vscodeApi.window.showWarningMessage(
    `The current step is High-Level UI Direction. The high-level backlog remains unavailable until the UI look and feel is Active and current.${state.detail ? ` ${state.detail}` : ''}`,
    'Define high-level UI direction');
  if (selected === 'Define high-level UI direction')
    await vscodeApi.commands.executeCommand('cis.uiDirectionQuestions');
  return false;
}

function compactValidationResult(result) {
  if (!result || typeof result !== 'object') return result;
  const compact = { ...result };
  if (Array.isArray(compact.items)) {
    compact.itemCount = compact.items.length;
    delete compact.items;
  }
  if (Array.isArray(compact.components)) {
    compact.componentCount = compact.components.length;
    delete compact.components;
  }
  return compact;
}

async function selectBacklogItem(cli, root, linked) {
  const backlog = await queryWorkspace(cli, ['brd', 'backlog', 'status'], root);
  const items = Array.isArray(backlog.items) ? backlog.items : [];
  const candidates = linked
    ? items.filter(item => String(item.featureSpecification || item.featureSpec || '').toLowerCase() !== 'not-created')
    : items.filter(item => {
      const next = nextStartableItem(items);
      return next && String(item.id) === String(next.id);
    });
  if (!candidates.length) throw new Error(linked
    ? 'No started feature specification is available.'
    : 'No dependency-ready backlog item is available to start.');
  const selected = await vscode.window.showQuickPick(candidates.map(item => ({
    label: String(item.id), description: item.outcome || item.requirementId || '', itemId: String(item.id),
  })), { placeHolder: linked ? 'Select a feature specification' : 'Start the next dependency-ready feature' });
  return selected?.itemId;
}

async function agentStateCommand(cli, refresh, operation, runId) {
  if (!runId) throw new Error('A run ID is required.');
  const actor = await actorIdentity(); if (!actor) return;
  const reason = await vscode.window.showInputBox({ prompt: `${operation === 'cancel' ? 'Cancellation' : 'Recovery'} rationale`,
    validateInput: value => value.trim() ? undefined : 'A rationale is required.' });
  if (!reason) return;
  const approved = await vscode.window.showWarningMessage(`${operation === 'cancel' ? 'Cancel' : 'Recover'} ${runId}?`, { modal: true }, 'Confirm');
  if (approved !== 'Confirm') return;
  await cli.runForeground(`${operation} ${runId}`, ['agent', operation, runId, '--actor', actor, '--reason', reason]);
  await refresh(false);
}

async function resumeAgent(cli, refresh, runId) {
  if (!runId) throw new Error('A run ID is required.');
  const actor = await actorIdentity(); if (!actor) return;
  const reason = await vscode.window.showInputBox({ prompt: 'Why is a new attempt required?', validateInput: value => value.trim() ? undefined : 'A rationale is required.' });
  if (!reason) return;
  const message = await vscode.window.showInputBox({ prompt: 'Optional bounded continuation instruction', password: false });
  const args = ['agent', 'resume', runId, '--actor', actor, '--reason', reason];
  if (message) args.push('--message', message);
  await cli.runForeground(`Resume ${runId}`, args, { cancellable: false });
  await refresh(false);
}

async function taskTransition(cli, refresh, input) {
  const suppliedChange = typeof input === 'string' ? input : input?.changeId || input?.cis?.changeId;
  const changeId = suppliedChange || await vscode.window.showInputBox({ prompt: 'Change ID', placeHolder: 'CIS-0001' }); if (!changeId) return;
  const suppliedTask = input?.taskId || input?.task?.id || input?.cis?.taskId;
  const taskId = suppliedTask || await vscode.window.showInputBox({ prompt: 'Task ID to transition', placeHolder: 'WORK-100-BACKOFFICE' }); if (!taskId) return;
  const status = await vscode.window.showQuickPick(['InProgress', 'ReadyForReview', 'Complete', 'Blocked', 'Deferred', 'Cancelled'],
    { placeHolder: 'Select the intended governed task state' }); if (!status) return;
  const actor = await actorIdentity(); if (!actor) return;
  const reason = await vscode.window.showInputBox({ prompt: 'Transition rationale recorded by CIS', validateInput: value => value.trim() ? undefined : 'A rationale is required.' });
  if (!reason) return;
  const confirmed = await vscode.window.showWarningMessage(`Transition ${changeId}/${taskId} to ${status}?`, { modal: true }, 'Confirm transition');
  if (confirmed !== 'Confirm transition') return;
  await cli.runForeground(`Transition ${taskId}`, ['plan', 'task', 'transition', changeId, taskId, '--status', status, '--actor', actor, '--reason', reason]);
  await refresh(false);
}

async function actorIdentity() {
  const configured = vscode.workspace.getConfiguration('cis').get('actorIdentity', '');
  if (configured.trim()) return configured.trim();
  return vscode.window.showInputBox({ prompt: 'Human actor identity recorded by CIS', validateInput: value => value.trim() ? undefined : 'Identity is required.' });
}

async function promptProductIdentity(root) {
  const repositoryId = repositoryMetadata(root, 'docs/cis').id || path.basename(root);
  const productName = await vscode.window.showInputBox({
    prompt: 'Product name governed by this CIS workspace',
    value: repositoryId,
    validateInput: value => value.trim() ? undefined : 'A product name is required.',
  });
  if (!productName) return undefined;
  const productId = await vscode.window.showInputBox({
    prompt: 'Stable product ID',
    value: slugIdentity(productName),
    validateInput: validateIdentity,
  });
  if (!productId) return undefined;
  const ecosystemName = await vscode.window.showInputBox({
    prompt: 'Software ecosystem name',
    value: productName,
    validateInput: value => value.trim() ? undefined : 'An ecosystem name is required.',
  });
  if (!ecosystemName) return undefined;
  const ecosystemId = await vscode.window.showInputBox({
    prompt: 'Stable ecosystem ID',
    value: slugIdentity(ecosystemName),
    validateInput: validateIdentity,
  });
  if (!ecosystemId) return undefined;
  return { ecosystemId: ecosystemId.trim(), ecosystemName: ecosystemName.trim(), productId: productId.trim(), productName: productName.trim() };
}

function productIdentityArgs(identity) {
  return ['--ecosystem', identity.ecosystemId, '--product', identity.productId,
    '--ecosystem-name', identity.ecosystemName, '--product-name', identity.productName];
}

function slugIdentity(value) {
  return String(value || '').trim().toLowerCase().replace(/[^a-z0-9._-]+/gu, '-').replace(/^[^a-z0-9]+|[^a-z0-9]+$/gu, '') || 'product';
}

function validateIdentity(value) {
  return /^[a-z0-9][a-z0-9._-]{0,127}$/u.test(String(value || '').trim())
    ? undefined
    : 'Use 1-128 lowercase letters, numbers, dots, underscores, or hyphens; start with a letter or number.';
}

function readWorkspaceBoundary(root) {
  const workspacePath = path.join(root, '.cis', 'workspace.yml');
  if (!fs.existsSync(workspacePath)) return undefined;
  const lines = fs.readFileSync(workspacePath, 'utf8').split(/\r?\n/u);
  const sections = {};
  let section;
  for (const line of lines) {
    const header = /^([a-z_]+):\s*$/u.exec(line);
    if (header) { section = header[1]; sections[section] ||= {}; continue; }
    const value = /^\s{2}(id|name):\s*['"]?([^'"\r\n]+?)['"]?\s*$/u.exec(line);
    if (section && value) sections[section][value[1]] = value[2].trim();
  }
  if (!sections.ecosystem?.id || !sections.product?.id)
    throw new Error('The workspace registry is not schema version 2. Reinitialize it with explicit ecosystem and product identity.');
  return {
    ecosystem: { id: sections.ecosystem.id, name: sections.ecosystem.name || sections.ecosystem.id },
    product: { id: sections.product.id, name: sections.product.name || sections.product.id },
  };
}

function installWatchers(context, authority, markStale) {
  const active = [];
  const reset = () => {
    while (active.length) active.pop().dispose();
    if (typeof vscode.RelativePattern !== 'function') return;
    const folders = vscode.workspace.workspaceFolders || [];
    const roots = workspaceWatchRoots([...folders.map(folder => folder.uri.fsPath), authority.root()]);
    for (const root of roots) {
      const metadata = repositoryMetadata(root, vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
      const documentationPattern = `${metadata.documentationRoot.replaceAll('\\', '/')}/**/*.md`;
      const patterns = ['.cis/repository.yml', '.cis/workspace.yml', documentationPattern,
        '.cis/local/graph/context.db{,-wal,-journal}',
        '**/*.{cs,fs,vb,ts,tsx,js,jsx,mjs,cjs,swift,kt,kts,py,sql,tf,go,rs,java,cpp,c,h,hpp,gd}',
        '**/*.{json,jsonc,yml,yaml,xml,html,css,scss,sass,less,razor,cshtml,vue,svelte,md,mdx,toml,props,targets,config,resx,ini}',
        '**/*.{csproj,fsproj,vbproj,sln,slnx,gradle}', '**/package.json', '**/Package.swift',
        '.cis/local/agents/runs/**/*.json', '.cis/local/testing/runs/**/*.json',
        '.cis/local/security/runs/**/*.json', '.cis/local/workflows/**/*.json'];
      for (const pattern of patterns) {
        const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, pattern));
        const changed = uri => {
          if (ignoredWatchEvent(uri, root)) return;
          if (uri?.fsPath && path.resolve(uri.fsPath) === path.resolve(root, '.cis', 'workspace.yml')) reset();
          markStale();
        };
        watcher.onDidChange(changed); watcher.onDidCreate(changed); watcher.onDidDelete(changed); active.push(watcher);
      }
    }
  };
  const manager = { reset, dispose: () => { while (active.length) active.pop().dispose(); } };
  reset(); context.subscriptions.push(manager); return manager;
}

function debounce(handler, delay) {
  let timer;
  let waiters = [];
  let latestArgs = [];
  return (...args) => new Promise((resolve, reject) => {
    latestArgs = args;
    waiters.push({ resolve, reject });
    clearTimeout(timer);
    timer = setTimeout(async () => {
      timer = undefined;
      const pending = waiters;
      waiters = [];
      try {
        const result = await handler(...latestArgs);
        for (const waiter of pending) waiter.resolve(result);
      } catch (error) {
        for (const waiter of pending) waiter.reject(error);
      }
    }, delay);
  });
}

async function refreshAfterReviewCompletion(refresh, wait = delay => new Promise(resolve => setTimeout(resolve, delay))) {
  // Review completion writes several related evidence files. Windows can deliver the final
  // filesystem notification after the first explicit refresh, which otherwise leaves the
  // tree marked stale. One quiet-period refresh absorbs that trailing notification without
  // turning filesystem watching into continuous command polling.
  await refresh(false);
  await wait(400);
  await refresh(false);
}

function conciseError(error) {
  if (error instanceof CisCliError) return bound(error.message, 512);
  return bound(error?.message || String(error), 512);
}

function brdRecommendationDecisionArgs(runId, findingId, actor, workspace, approvedRecommendation) {
  const args = ['brd', 'review', 'decide', runId, findingId, '--decision', 'accepted', '--actor', actor];
  if (typeof approvedRecommendation === 'string')
    args.push('--approved-recommendation', approvedRecommendation.trim());
  args.push('--workspace', workspace);
  return args;
}

function brdRecommendationAcceptAllArgs(runId, actor, workspace) {
  return ['brd', 'review', 'accept-all', runId, '--actor', actor, '--workspace', workspace];
}

function brdRecommendationProgress(status) {
  const findings = Array.isArray(status?.disposition?.findings) ? status.disposition.findings : [];
  return {
    state: String(status?.status || 'review-required').toLowerCase(),
    pending: findings.filter(item => String(item.decision || '').toLowerCase() === 'pending').length,
    accepted: status?.acceptedCount
      ?? findings.filter(item => String(item.decision || '').toLowerCase() === 'accepted').length,
    rejected: status?.rejectedCount
      ?? findings.filter(item => String(item.decision || '').toLowerCase() === 'rejected').length,
  };
}

function runIdentity(value) { return typeof value === 'string' ? value : value?.runId || value?.cis?.runId; }

function deactivate() {}

module.exports = {
  activate, actorIdentity, agentStateCommand, brdRecommendationAcceptAllArgs, brdRecommendationDecisionArgs, brdRecommendationProgress, conciseError, deactivate, debounce, installWatchers,
  approveProductDocument, compactValidationResult, continueApprovedFeatureDelivery, currentProductPaths, definitionWizardModel, ensureSolutionDesignReady, ensureUiDirectionReady, markdownFiles, queryWorkspace, repositoryMetadata,
  refreshAfterReviewCompletion, requestAgentWork, resolveWithin, resumeAgent, runIdentity, selectBacklogItem, taskTransition, validateProductDocument,
  promptProductIdentity, productIdentityArgs, readWorkspaceBoundary, slugIdentity, validateIdentity,
  AuthoritySelector, CisCli, CisViewProvider,
};
