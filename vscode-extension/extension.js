'use strict';

const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');
const { AuthoritySelector } = require('./lib/authority');
const { CisCli, CisCliError } = require('./lib/cis-cli');
const { bound, resolveWithin } = require('./lib/security');
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
  const providers = new Map(VIEW_IDS.map(id => [id, new CisViewProvider(vscode, id, authority, cli)]));
  context.subscriptions.push(output);
  for (const [id, provider] of providers) context.subscriptions.push(vscode.window.registerTreeDataProvider(`cis.${id}`, provider));

  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 50);
  status.command = authority.needsSelection() ? 'cis.selectAuthority' : 'cis.repoDoctor';
  status.text = '$(pulse) CIS'; status.tooltip = 'Change Impact Studio'; status.show(); context.subscriptions.push(status);
  const refresh = debounce(async (stale = false, invalidate = false) => {
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
  command('cis.selectAuthority', async () => { if (await authority.choose()) await refresh(false, true); });
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
    const args = ['repo', 'import', '--workspace', repository, '--source', repository, '--root', documentationRoot];
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
      const confirmed = await vscode.window.showWarningMessage(
        `Create the CIS workspace authority in ${metadata.documentationRoot} and build its context graph?`,
        { modal: true }, 'Start product definition');
      if (confirmed !== 'Start product definition') return;
      await cli.runForeground('Create CIS workspace authority',
        ['workspace', 'init', '--root', metadata.documentationRoot, '--yes'], { repository: false });
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
    const root = authority.root();
    if (!root) throw new Error('Select the CIS authority repository first.');
    const metadata = repositoryMetadata(root, vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    if (!metadata.initialized) throw new Error('Initialize the repository before starting high-level product definition.');
    const paths = productPaths(root, metadata);
    if (!fs.existsSync(paths.workspace)) {
      const confirmed = await vscode.window.showWarningMessage(
        `Create the CIS workspace authority in ${metadata.documentationRoot} and start the product-definition wizard?`,
        { modal: true }, 'Start wizard');
      if (confirmed !== 'Start wizard') return;
      await cli.runForeground('Create CIS workspace authority',
        ['workspace', 'init', '--root', metadata.documentationRoot, '--yes'], { repository: false });
      await cli.runForeground('Build CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    }
    let model = await definitionWizardModel(cli, root);
    if (!model.sessionId) {
      await queryWorkspace(cli, ['definition', 'init'], root);
      model = await definitionWizardModel(cli, root);
    }
    let controller;
    const reload = async page => {
      cli.clearQueryCache?.();
      const next = await definitionWizardModel(cli, root);
      controller.update(next, page || controller.page());
      return next;
    };
    const initialPage = model.readyToActivate === true && model.active !== false ? 'review' : undefined;
    controller = openDefinitionWizardPanel(vscode, root, model, async (action, value, wizard) => {
      if (action === 'refresh') { await reload(wizard.page()); return; }
      if (action === 'save-answer') {
        let answer;
        try { answer = JSON.parse(value); }
        catch { throw new Error('The wizard answer was malformed.'); }
        if (!answer?.page || !answer?.id || !String(answer.answer || '').trim())
          throw new Error('Enter a direction before saving it.');
        const actor = await actorIdentity(); if (!actor) return;
        await queryWorkspace(cli, ['definition', 'answer', '--page', String(answer.page), '--id', String(answer.id),
          '--answer', String(answer.answer).trim(), '--actor', actor], root);
        await reload(String(answer.page));
        return;
      }
      if (action === 'prepare') {
        if (value === 'business' && !fs.existsSync(paths.brd)) {
          await vscode.commands.executeCommand('cis.productStart');
        }
        await queryWorkspace(cli, ['definition', 'prepare', '--page', value], root);
        const next = await reload(value);
        const page = (next.pages || []).find(item => item.id === value);
        if (page?.complete && page?.current) {
          const following = (next.pages || []).find(item => Number(item.ordinal) === Number(page.ordinal) + 1);
          if (following) controller.update(next, following.id);
        }
        await refresh(false);
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
        } else if (value === 'questions') await vscode.commands.executeCommand('cis.brdAnswerQuestions');
        else if (value === 'review-brd') await vscode.commands.executeCommand('cis.brdAgentReview');
        await reload('business');
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
  });
  command('cis.brdValidate', async () => validateProductDocument(cli, refresh, showQuery,
    'Business requirements validation', ['brd', 'validate'], authority.root()));
  command('cis.brdApprove', async () => approveProductDocument(cli, refresh, showQuery, {
    label: 'business requirements', validate: ['brd', 'validate'], approve: ['brd', 'approve'],
  }, authority.root()));
  command('cis.brdAgentDraft', async () => {
    const root = authority.root(); const paths = currentProductPaths(vscode, authority);
    if (!fs.existsSync(paths.brd)) throw new Error('Create the canonical business requirements before assigning an agent draft.');
    const selected = await vscode.window.showOpenDialog({
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
      `Send ${selected.length} selected evidence source(s) (${names}) to ${providerItem.provider.displayName}? CIS will project supported documents or fresh initialized repository graphs, expose only the bounded derived evidence to that provider, run inside an isolated scratch repository, and apply only a protected one-file BRD draft.`,
      { modal: true }, 'Start BRD draft');
    if (confirmed !== 'Start BRD draft') return;
    const args = ['agent', 'author', 'brd', '--provider', providerItem.provider.id,
      '--transport', transport, '--actor', actor];
    for (const uri of selected) args.push('--reference', uri.fsPath);
    if (requestPolicy.approve) args.push('--approve-requests');
    await cli.runForeground('Draft business requirements with agent', args, { cancellable: false });
    await cli.runForeground('Refresh CIS workspace graph', ['graph', 'build', '--workspace', root], { repository: false });
    await refresh(false);
    await openCanonical(authority, paths.brd, false);
  });
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
      { label: 'BRD and authoring references', description: 'Also disclose the previously approved extracted source evidence to this provider.', include: true },
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
    let current = await cli.query(['repo', 'doctor'], { acceptStructuredFailure: true });
    let controller;
    const reportDoctorError = async error => {
      const message = conciseError(error);
      output.appendLine(`ERROR [repository-doctor]: ${message}`);
      const selected = await vscode.window.showErrorMessage(`CIS: ${message}`, 'Show output');
      if (selected === 'Show output') output.show(true);
    };
    controller = openDoctorPanel(vscode, current, async (action, value) => {
      try {
        if (action === 'refresh') {
          current = await cli.query(['repo', 'doctor', '--refresh'], { acceptStructuredFailure: true });
          controller.update(current); await refresh(false, true); return;
        }
        if (action !== 'copy-fix') return;
        const finding = (current.findings || []).find(item => item.code === value);
        const fixCommand = typeof finding?.fixCommand === 'string' ? finding.fixCommand.trim() : '';
        if (!fixCommand || fixCommand.length > 4096 || /[\u0000-\u001f\u007f]/u.test(fixCommand))
          throw new Error(`Doctor finding '${value}' does not contain a safe copyable command.`);
        await vscode.env.clipboard.writeText(fixCommand);
        await vscode.window.showInformationMessage(`Copied the suggested command for ${value}. Review it before running.`);
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
    await openCanonical(authority, file, false, allowLocalEvidence);
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
  try { return await cli.query([...args, '--workspace', root], { repository: false }); }
  catch (error) {
    if (error?.data && typeof error.data === 'object') return error.data;
    throw error;
  }
}

async function definitionWizardModel(cli, root) {
  const safe = async args => {
    try { return await queryWorkspace(cli, args, root); }
    catch (error) { return error?.data && typeof error.data === 'object' ? error.data : {}; }
  };
  const [base, brdQuestions, technicalQuestions, uiQuestions] = await Promise.all([
    safe(['definition', 'status']),
    safe(['brd', 'questions', 'guidance']),
    safe(['technical-intent', 'questions', 'status']),
    safe(['ui-direction', 'questions', 'status']),
  ]);
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

function installWatchers(context, authority, markStale) {
  const active = [];
  const reset = () => {
    while (active.length) active.pop().dispose();
    if (typeof vscode.RelativePattern !== 'function') return;
    const folders = vscode.workspace.workspaceFolders || [];
    for (const folder of folders) {
      const root = folder.uri.fsPath;
      const metadata = repositoryMetadata(root, vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
      const documentationPattern = `${metadata.documentationRoot.replaceAll('\\', '/')}/**/*.md`;
      const patterns = ['.cis/repository.yml', '.cis/workspace.yml', documentationPattern,
        '**/*.{cs,fs,vb,ts,tsx,js,jsx,mjs,cjs,swift,kt,kts,py,sql,tf,go,rs,java,cpp,c,h,hpp,gd}',
        '**/*.{csproj,fsproj,vbproj,sln,slnx,gradle}', '**/package.json', '**/Package.swift',
        '.cis/local/agents/runs/**/*.json', '.cis/local/testing/runs/**/*.json',
        '.cis/local/security/runs/**/*.json', '.cis/local/workflows/**/*.json'];
      for (const pattern of patterns) {
        const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, pattern));
        watcher.onDidChange(markStale); watcher.onDidCreate(markStale); watcher.onDidDelete(markStale); active.push(watcher);
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
  AuthoritySelector, CisCli, CisViewProvider,
};
