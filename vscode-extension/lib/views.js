'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { resolveWithin } = require('./security');
const { normalize, terminal } = require('./projections');
const {
  documentStatus, isActiveCurrent, isReadyForApproval, linkedFeatureItems, nextStartableItem,
  productPaths, reviewMatchesBrd, stateOf, titleCase,
} = require('./product-journey');

class CisTreeItem {
  constructor(vscode, label, options = {}) {
    const item = new vscode.TreeItem(label, options.children?.length
      ? vscode.TreeItemCollapsibleState.Collapsed : vscode.TreeItemCollapsibleState.None);
    Object.assign(item, options);
    item.children = options.children || [];
    if (options.file) {
      item.contextValue = options.contextValue || 'cis.file';
      item.resourceUri = vscode.Uri.file(options.file);
      item.command = { command: options.preview ? 'cis.preview' : 'cis.open', title: 'Open', arguments: [item] };
      item.iconPath = options.iconPath || new vscode.ThemeIcon('markdown');
    }
    return item;
  }
}

class CisViewProvider {
  constructor(vscode, id, authority, cli) {
    this.vscode = vscode;
    this.id = id;
    this.authority = authority;
    this.cli = cli;
    this.changed = new vscode.EventEmitter();
    this.onDidChangeTreeData = this.changed.event;
    this.last = undefined;
    this.stale = false;
    this.filter = '';
  }

  markStale() { this.stale = true; }
  refresh(stale = false) { this.stale = stale; this.changed.fire(undefined); }
  setFilter(value) { this.filter = String(value || '').trim().toLowerCase(); this.refresh(false); }
  getTreeItem(item) { return item; }
  async getChildren(item) {
    if (item) return item.children || [];
    try {
      const items = await this.load();
      this.last = items; this.stale = false;
      return items;
    } catch (error) {
      if (this.last) {
        this.stale = true;
        return [this.node('Results are stale', { description: concise(error.message), icon: 'warning' }), ...this.last];
      }
      return [this.node('Unavailable', { description: concise(error.message), icon: 'error' })];
    }
  }

  node(label, options = {}) {
    const node = new CisTreeItem(this.vscode, label, {
      description: options.description,
      tooltip: options.tooltip || options.description,
      children: options.children,
      file: options.file,
      preview: options.preview,
      contextValue: options.contextValue,
      iconPath: options.icon ? new this.vscode.ThemeIcon(options.icon) : options.iconPath,
    });
    if (options.command) node.command = { command: options.command, title: label, arguments: options.arguments || [node] };
    node.cis = options.data;
    return node;
  }

  async load() {
    const root = this.authority.root();
    if (!root) {
      if (this.authority.needsSelection()) return [this.node('Select authority repository', { command: 'cis.selectAuthority', icon: 'root-folder' })];
      return [this.node('Open a repository folder', { description: 'CIS needs a local workspace folder.', icon: 'folder-opened' })];
    }
    if (this.id === 'workspace') {
      if (this.vscode.workspace.isTrusted === false) return [
        this.node('Workspace is untrusted', { description: 'Canonical Markdown remains available in Evidence; trust is required before CIS can run.', icon: 'lock' }),
      ];
      let version;
      try { version = await this.cli.version(); }
      catch (error) { return [this.node('CIS CLI is unavailable', { description: concise(error.message), icon: 'circle-slash' })]; }
      if (!version.compatible) return [
        this.node(`CIS ${version.raw} is incompatible`, { description: 'Install a compatible CIS 0.3 or later 0.x CLI and refresh.', icon: 'warning' }),
      ];
      return this.workspace(root, version);
    }
    if (!fs.existsSync(path.join(root, '.cis', 'repository.yml'))) return this.onboarding(root);
    if (this.id === 'changes') return this.changes(root);
    if (this.id === 'journey') return this.journey(root);
    if (this.id === 'evidence') return this.evidence(root);
    if (this.id === 'runs') return this.runs();
    if (this.id === 'governance') return this.governance(root);
    return [];
  }

  async workspace(root, version) {
    const metadata = repositoryMetadata(root, this.vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    if (!metadata.initialized) return this.onboarding(root);
    const [doctor, changes, providers] = await Promise.all([
      this.cli.query(['repo', 'doctor'], { acceptStructuredFailure: true }),
      this.cli.query(['change', 'list']), this.cli.query(['agent', 'providers']),
    ]);
    const active = (changes.changes || []).find(change => String(change.status).toLowerCase() !== 'closed');
    const [planResult, designResult] = active ? await Promise.allSettled([
      this.cli.query(['plan', 'show', active.id]), this.cli.query(['design', 'status', active.id]),
    ]) : [];
    const plan = planResult?.status === 'fulfilled' ? planResult.value : undefined;
    const design = designResult?.status === 'fulfilled' ? designResult.value : undefined;
    const next = plan?.workItems?.find(item => normalize(item.status) === 'inprogress')
      || plan?.workItems?.find(item => normalize(item.status) === 'ready')
      || plan?.workItems?.find(item => normalize(item.status) === 'draft'
        && (item.dependsOn || []).every(id => terminal.has(normalize(plan.workItems.find(candidate => candidate.id === id)?.status))));
    const warnings = doctor.warningCount ?? doctor.warnings ?? 0;
    const errors = doctor.errorCount ?? doctor.errors ?? 0;
    const graphFindings = (doctor.findings || []).filter(finding => finding.category === 'context-graph');
    const graphProblems = graphFindings.filter(isProblemFinding);
    const indexFindings = (doctor.findings || []).filter(finding => finding.category === 'file-index');
    const indexProblems = indexFindings.filter(isProblemFinding);
    const graphUnavailable = graphProblems.some(finding => finding.code === 'CIS-GRAPH-DOCTOR-001');
    const indexUnavailable = indexProblems.some(finding => finding.code === 'CIS-INDEX-001');
    const readme = resolveWithin(metadata.documentationPath, 'README.md');
    const product = active ? undefined : await this.productJourney(root, metadata);
    return [
      this.node(metadata.id || path.basename(root), { description: 'Authority repository', icon: 'repo' }),
      this.node(`CIS ${version.raw}`, { description: 'Compatible CLI', icon: 'terminal' }),
      this.node('Documentation', { description: metadata.documentationRoot, file: readme && fs.existsSync(readme) ? readme : undefined, icon: 'book' }),
      this.node(errors ? 'Doctor: errors' : warnings ? 'Doctor: warnings' : 'Doctor: healthy', {
        description: `${errors} errors, ${warnings} warnings`, command: 'cis.repoDoctor', icon: errors ? 'error' : warnings ? 'warning' : 'pass',
      }),
      this.node(doctor.ollama?.isAvailable ? 'Local AI available' : 'Local AI unavailable', {
        description: doctor.ollama?.models?.join(', ') || doctor.ollama?.detail || '', command: 'cis.aiStatus',
        icon: doctor.ollama?.isAvailable ? 'sparkle' : 'circle-slash',
      }),
      this.node(graphUnavailable ? 'Context graph is not built'
        : graphProblems.length ? 'Context graph is stale or invalid' : 'Context graph is current', {
        description: graphProblems[0]?.message || graphFindings[0]?.message || 'Repository Doctor reports a current graph.',
        command: 'cis.graphBuild', icon: graphProblems.length ? 'warning' : 'type-hierarchy-sub',
      }),
      this.node(indexUnavailable ? 'Routing index is not built'
        : indexProblems.length ? 'Routing index is stale or incomplete' : 'Routing index is current', {
        description: indexProblems[0]?.message || indexFindings[0]?.message || 'Repository Doctor reports a current routing index.',
        command: indexProblems.length ? 'cis.indexBuild' : undefined,
        icon: indexProblems.length ? 'warning' : 'list-tree',
      }),
      this.node(active ? active.id : 'No active change', {
        description: active ? `${active.status} — ${active.title}` : 'Complete product definition before creating a change.',
        file: active ? resolveWithin(root, `${active.relativePath}/proposal.md`) : undefined,
        icon: active ? 'git-pull-request' : 'circle-outline',
      }),
      ...(!active ? [this.node('High-level product definition wizard', {
        description: 'Business, technical, architecture, contracts, experience, delivery, and one consolidated activation.',
        command: 'cis.definitionWizard', icon: 'map',
      })] : []),
      ...(product ? [product.group] : []),
      this.node(design ? `Design gate: ${design.gateStatus}` : 'Design gate unavailable', {
        description: design ? `${design.approvalStatus}; ${design.artifacts?.length || 0} artifacts` : 'No applicable or readable design gate.',
        command: active ? 'cis.designReview' : undefined, arguments: active ? [active.id] : undefined,
        icon: design?.gateStatus === 'Approved' ? 'pass' : design ? 'debug-pause' : 'circle-outline',
      }),
      this.node(active && next ? `Next: ${next.id}` : active ? 'Next: final status review' : `Next: ${product.next.label}`, {
        description: active ? next?.title || 'No executable plan task is currently projected.' : product.next.description,
        command: active ? 'cis.openChange' : product.next.command,
        arguments: active ? [active] : product.next.arguments, icon: 'arrow-right',
      }),
      this.node(`${(providers.providers || []).length} agent providers`, { description: 'Open Runs for capability evidence.', command: 'cis.agentProviders', icon: 'hubot' }),
    ];
  }

  onboarding(root) {
    const metadata = repositoryMetadata(root, this.vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    const existing = metadata.onboardingMode === 'import';
    return [
      this.node(existing ? 'Existing repository is not imported' : 'CIS project has not been created', {
        description: existing
          ? 'Source evidence was detected. Import it so CIS classifies and indexes what already exists.'
          : 'Choose a documentation root for this new project.',
        icon: 'info',
      }),
      this.node(existing ? 'Import existing repository' : 'Create CIS project', {
        command: existing ? 'cis.repoImport' : 'cis.repoInit',
        icon: existing ? 'repo-pull' : 'add',
      }),
    ];
  }

  async productJourney(root, metadata) {
    const paths = productPaths(root, metadata);
    const stages = [];
    const action = (label, description, command, args = []) => ({ label, description, command, arguments: args });
    const stage = (label, description, icon, file, command, args) => stages.push(this.node(label, {
      description, icon, file, command, arguments: args,
    }));

    if (!fs.existsSync(paths.workspace)) {
      stage('1. Workspace authority', 'Not configured', 'circle-outline');
      stage('2. Business requirements', 'Not started', 'circle-outline');
      return {
        group: this.node('Product definition', { description: 'Start here', children: stages, icon: 'map' }),
        next: action('Start product definition', 'Create the workspace authority and open a new BRD.', 'cis.productStart'),
      };
    }
    stage('1. Workspace authority', 'Ready', 'pass');

    if (!fs.existsSync(paths.brd)) {
      stage('2. Business requirements', 'Not started', 'circle-outline');
      return {
        group: this.node('Product definition', { description: 'BRD required', children: stages, icon: 'map' }),
        next: action('Create business requirements', 'Create and open the canonical BRD.', 'cis.productStart'),
      };
    }

    const brd = stateOf(await this.workspaceQuery(['brd', 'status'], root), paths.brd);
    stage('2. Business requirements', brd.status, stateIcon(brd.status), paths.brd);
    if (!isActiveCurrent(brd)) {
      const [questionResult, productRuns] = await Promise.all([
        this.workspaceQuery(['brd', 'questions', 'list'], root),
        this.cli.query(['agent', 'runs', '--change', 'PRODUCT', '--summary', '--latest-per-task', '--limit', '10']).catch(() => ({ runs: [] })),
      ]);
      const unanswered = (questionResult.questions || []).filter(item => String(item.status).toLowerCase() === 'unanswered');
      const successful = (productRuns.runs || []).filter(run => String(run.status).toLowerCase() === 'succeeded');
      const latestProducer = successful.find(run => ['BRD-DRAFT', 'BRD-REVISION', 'BRD-QUESTION-REVISION'].includes(run.taskId));
      const latestDraft = successful.find(run => run.taskId === 'BRD-DRAFT');
      const latestReview = successful.find(run => run.taskId === 'BRD-REVIEW');
      const latestQuestionRevision = successful.find(run => run.taskId === 'BRD-QUESTION-REVISION');
      const latestQuestionEvidence = latestQuestionRevision
        ? await this.cli.query(['agent', 'show', latestQuestionRevision.runId, '--summary']).catch(() => ({})) : undefined;
      const incorporatedAnswerDigest = latestQuestionEvidence?.run?.result?.questionRevision?.answerDigest;
      const questionRevisionApplied = (latestQuestionEvidence?.run?.eventKinds || []).includes('apply');
      const allQuestionsAnswered = (questionResult.questions || []).length > 0 && unanswered.length === 0;
      const questionRevisionCompleted = String(latestQuestionRevision?.completedAtUtc || latestQuestionRevision?.updatedAtUtc || '');
      const latestDraftCompleted = String(latestDraft?.completedAtUtc || latestDraft?.updatedAtUtc || '');
      const answersWereIncorporated = latestQuestionRevision
        && questionRevisionApplied
        && incorporatedAnswerDigest === questionResult.answerDigest
        && (!latestDraft || questionRevisionCompleted >= latestDraftCompleted);
      const rawReviewDigestCurrent = reviewMatchesBrd(latestReview, paths.brd);
      const reviewFreshness = latestReview
        ? await this.workspaceQuery(['brd', 'review', 'freshness', latestReview.runId], root)
        : undefined;
      const reviewDigestCurrent = typeof reviewFreshness?.compatible === 'boolean'
        ? reviewFreshness.compatible : rawReviewDigestCurrent;
      const producerCompleted = String(latestProducer?.completedAtUtc || latestProducer?.updatedAtUtc || '');
      const reviewCompleted = String(latestReview?.completedAtUtc || latestReview?.updatedAtUtc || '');
      const retainedUnappliedRevision = ['BRD-REVISION', 'BRD-QUESTION-REVISION'].includes(latestProducer?.taskId)
        && reviewDigestCurrent === true && reviewCompleted < producerCompleted;
      const retainedUnappliedQuestionRevision = latestProducer?.taskId === 'BRD-QUESTION-REVISION'
        && retainedUnappliedRevision;
      const questionsNeedIncorporation = allQuestionsAnswered
        && (!answersWereIncorporated || retainedUnappliedQuestionRevision);
      const reviewCurrent = latestProducer && latestReview
        && reviewDigestCurrent !== false
        && (reviewCompleted >= producerCompleted || retainedUnappliedRevision);
      const reviewDescription = reviewCurrent && reviewFreshness?.status === 'question-answers-only'
        ? `Reviewed by ${latestReview.provider}; stakeholder answers recorded afterward`
        : reviewCurrent ? `Reviewed by ${latestReview.provider}`
        : latestReview && reviewDigestCurrent === false
          ? 'Superseded because the BRD evidence baseline changed after review'
          : `Required after ${latestProducer?.taskId === 'BRD-REVISION' ? 'agent revision' : 'agent draft'}`;
      if (latestProducer) stage('2a. Independent BRD review', reviewDescription,
        reviewCurrent ? 'pass' : 'circle-outline', undefined, reviewCurrent ? 'cis.agentShow' : undefined,
        reviewCurrent ? [latestReview.runId] : undefined);
      let reviewDisposition;
      let reviewFindingCount = 0;
      if (reviewCurrent) {
        const evidence = await this.cli.query(['agent', 'show', latestReview.runId, '--summary']).catch(() => ({}));
        reviewFindingCount = evidence.run?.result?.review?.findings?.length || 0;
        if (reviewFindingCount) {
          reviewDisposition = await this.workspaceQuery(['brd', 'review', 'status', latestReview.runId], root);
          const dispositionStatus = String(reviewDisposition.status || '').toLowerCase();
          stage('2b. Review recommendations', dispositionStatus === 'applied' ? 'Applied'
            : dispositionStatus === 'approved' ? 'Approved remediation scope'
              : `${reviewDisposition.pendingCount ?? reviewFindingCount} pending`,
          ['approved', 'applied'].includes(dispositionStatus) ? 'pass' : 'circle-outline', reviewDisposition.canonicalPath);
        }
      }
      if ((questionResult.questions || []).length) {
        const description = unanswered.length ? `${unanswered.length} stakeholder answer${unanswered.length === 1 ? '' : 's'} required`
          : questionsNeedIncorporation ? retainedUnappliedQuestionRevision
            ? 'Retained answer-incorporation candidate awaits safe application'
            : 'Answered; BRD content update required'
            : reviewCurrent ? 'Answers incorporated and independently reviewed'
              : 'Answers incorporated; independent review required';
        stage('2c. Answer incorporation', description,
          !unanswered.length && !questionsNeedIncorporation ? 'pass' : 'circle-outline');
      }
      const dispositionStatus = String(reviewDisposition?.status || '').toLowerCase();
      const acceptedCount = reviewDisposition?.acceptedCount
        ?? (reviewDisposition?.disposition?.findings || []).filter(item => item.decision === 'accepted').length;
      const next = latestProducer && !reviewCurrent
        ? action('Review BRD with independent agent', `Use a provider different from ${latestProducer.provider} for read-only findings.`, 'cis.brdAgentReview')
        : reviewFindingCount && !['approved', 'applied'].includes(dispositionStatus)
          ? action('Review recommendations', 'Approve each advisory recommendation as written or with exact edits, then confirm the complete bounded scope.', 'cis.brdReviewRecommendations', [latestReview.runId])
          : dispositionStatus === 'approved' && acceptedCount > 0
            ? action('Apply approved BRD recommendations with agent', 'Run a different provider in an isolated one-file revision workspace.', 'cis.brdAgentRevise', [latestReview.runId])
        : unanswered.length
        ? action('Answer open BRD questions', `${unanswered.length} stakeholder answer${unanswered.length === 1 ? ' is' : 's are'} required before validation.`, 'cis.brdAnswerQuestions')
        : questionsNeedIncorporation
        ? action('Update BRD from answered questions', 'Run a bounded one-file agent revision, then independently review the updated BRD.', 'cis.brdAgentIncorporateQuestions')
        : isReadyForApproval(brd)
        ? action('Approve business requirements', 'Record explicit human approval of the validated BRD.', 'cis.brdApprove')
        : brd.valid === false
          ? action('Draft business requirements from reference', brd.detail || 'Assign a bounded agent draft from selected reference evidence.', 'cis.brdAgentDraft')
          : action('Validate business requirements', 'Check whether the edited BRD is ready for approval.', 'cis.brdValidate');
      return { group: this.productGroup(stages, brd.status), next };
    }

    const questionnaireResult = await this.workspaceQuery(['technical-intent', 'questions', 'status', '--summary'], root);
    const questionnaireCurrent = questionnaireResult.current === true;
    const questionnaireComplete = questionnaireResult.complete === true;
    stage('3. Technical direction questionnaire', questionnaireComplete && questionnaireCurrent
      ? 'Complete' : questionnaireResult.status === 'missing' ? 'Not started'
        : `${questionnaireResult.answeredCount || 0}/${(questionnaireResult.answeredCount || 0) + (questionnaireResult.unansweredCount || 0)} resolved`,
    questionnaireComplete && questionnaireCurrent ? 'pass' : 'circle-outline',
    undefined, 'cis.technicalIntentQuestions');
    if (!questionnaireComplete || !questionnaireCurrent) {
      return {
        group: this.productGroup(stages, 'Technical direction choices required'),
        next: action('Define high-level technical direction', 'Choose product surfaces, frontend and backend technologies, architecture style, data, identity, hosting, operations, assurance, and constraints.', 'cis.technicalIntentQuestions'),
      };
    }

    const technicalManaged = fs.existsSync(paths.technicalIntent)
      && fs.readFileSync(paths.technicalIntent, 'utf8').includes('<!-- cis:technical-intent-questionnaire-evidence:start -->');
    if (!technicalManaged) {
      stage('4. Technical intent', 'Not initialized', 'circle-outline', fs.existsSync(paths.technicalIntent) ? paths.technicalIntent : undefined);
      return {
        group: this.productGroup(stages, 'Technical intent required'),
        next: action('Generate technical intent', 'Derive choices, components, interactions, architecture guidelines, and assurance direction from the completed questionnaire, approved BRD, repository classifications, and Active standards.', 'cis.technicalIntentInit'),
      };
    }

    const technical = stateOf(await this.workspaceQuery(['technical-intent', 'status'], root), paths.technicalIntent);
    stage('4. Technical intent', technical.status, stateIcon(technical.status), paths.technicalIntent);
    if (!isActiveCurrent(technical)) {
      const next = isReadyForApproval(technical)
        ? action('Approve technical intent', 'Record explicit human approval of the validated technical intent.', 'cis.technicalIntentApprove')
        : technical.valid === false
          ? action('Review technical intent skeleton', technical.detail || 'Resolve the generated architecture direction and open decisions, then validate.', 'cis.open', [{ file: paths.technicalIntent }])
          : action('Validate technical intent', 'Check whether technical intent is ready for approval.', 'cis.technicalIntentValidate');
      return { group: this.productGroup(stages, technical.status), next };
    }

    if (!fs.existsSync(paths.overallSolutionDesign) || !fs.existsSync(paths.componentSheet)) {
      stage('5. Overall solution design', 'Not initialized', 'circle-outline',
        fs.existsSync(paths.overallSolutionDesign) ? paths.overallSolutionDesign : undefined);
      stage('5a. Component sheet', 'Not initialized', 'circle-outline',
        fs.existsSync(paths.componentSheet) ? paths.componentSheet : undefined);
      return {
        group: this.productGroup(stages, 'Solution architecture required'),
        next: action('Generate overall solution design', 'Project the approved technical intent into a governed architecture narrative and structured component inventory.', 'cis.solutionDesignInit'),
      };
    }

    const solutionResult = await this.workspaceQuery(['solution-design', 'status'], root);
    const solution = stateOf(solutionResult, paths.overallSolutionDesign);
    const componentStatus = solutionResult?.validation?.componentSheetStatus || documentStatus(paths.componentSheet);
    const diagramsPresent = fs.existsSync(paths.architectureDiagrams);
    const diagramsStatus = diagramsPresent ? titleCase(documentStatus(paths.architectureDiagrams)) : 'Not prepared';
    stage('5. Overall solution design', solution.status, stateIcon(solution.status), paths.overallSolutionDesign);
    stage('5a. Component sheet', titleCase(componentStatus), stateIcon(componentStatus), paths.componentSheet);
    stage('5b. Architecture diagrams', diagramsStatus, diagramsPresent ? stateIcon(diagramsStatus) : 'circle-outline',
      diagramsPresent ? paths.architectureDiagrams : undefined, diagramsPresent ? 'cis.preview' : undefined);
    if (!isActiveCurrent(solution)) {
      const next = isReadyForApproval(solution)
        ? action('Approve solution-design bundle', 'Record one human approval for the exact overall design and component sheet.', 'cis.solutionDesignApprove')
        : solution.valid === false
          ? action('Review solution-design bundle', solution.detail || 'Resolve architecture and component ownership issues, then validate both files.', 'cis.open', [{ file: paths.overallSolutionDesign }])
          : action('Validate solution-design bundle', 'Check architecture completeness, component traceability, source currency, and bundle integrity.', 'cis.solutionDesignValidate');
      return { group: this.productGroup(stages, solution.status), next };
    }

    const uiQuestionnaireResult = await this.workspaceQuery(['ui-direction', 'questions', 'status', '--summary'], root);
    const uiQuestionnaireCurrent = uiQuestionnaireResult.current === true;
    const uiQuestionnaireComplete = uiQuestionnaireResult.complete === true;
    stage('6. UI look and feel questionnaire', uiQuestionnaireComplete && uiQuestionnaireCurrent
      ? 'Complete' : uiQuestionnaireResult.status === 'missing' ? 'Not started'
        : `${uiQuestionnaireResult.answeredCount || 0}/${(uiQuestionnaireResult.answeredCount || 0) + (uiQuestionnaireResult.unansweredCount || 0)} resolved`,
    uiQuestionnaireComplete && uiQuestionnaireCurrent ? 'pass' : 'circle-outline',
    undefined, 'cis.uiDirectionQuestions');
    if (!uiQuestionnaireComplete || !uiQuestionnaireCurrent) {
      return {
        group: this.productGroup(stages, 'High-level UI choices required'),
        next: action('Define high-level UI look and feel', 'Choose the product character, shell, navigation, density, theme, typography, reusable components, responsive behavior, accessibility, feedback, and constraints.', 'cis.uiDirectionQuestions'),
      };
    }

    if (!fs.existsSync(paths.uiDirection)) {
      stage('6a. High-level UI direction', 'Not initialized', 'circle-outline');
      return {
        group: this.productGroup(stages, 'High-level UI direction required'),
        next: action('Generate high-level UI direction', 'Project the approved architecture and resolved UI choices into one workspace-level direction for all feature designs.', 'cis.uiDirectionInit'),
      };
    }

    const uiDirectionResult = await this.workspaceQuery(['ui-direction', 'status'], root);
    const uiDirection = stateOf(uiDirectionResult, paths.uiDirection);
    const previewPresent = fs.existsSync(paths.uiSystemPreview);
    const previewStatus = previewPresent ? titleCase(documentStatus(paths.uiSystemPreview)) : 'Not prepared';
    stage('6a. High-level UI direction', uiDirection.status, stateIcon(uiDirection.status), paths.uiDirection);
    stage('6b. One-page visual system preview', previewStatus, previewPresent ? stateIcon(previewStatus) : 'circle-outline',
      previewPresent ? paths.uiSystemPreview : undefined, previewPresent ? 'cis.preview' : undefined);
    if (!isActiveCurrent(uiDirection)) {
      const next = isReadyForApproval(uiDirection)
        ? action('Approve high-level UI direction', 'Record explicit human approval of the exact workspace-level look and feel.', 'cis.uiDirectionApprove')
        : uiDirection.valid === false
          ? action('Review high-level UI direction', uiDirection.detail || 'Resolve the generated visual and interaction direction, then validate.', 'cis.open', [{ file: paths.uiDirection }])
          : action('Validate high-level UI direction', 'Check completeness, provenance, source currency, and approval integrity.', 'cis.uiDirectionValidate');
      return { group: this.productGroup(stages, uiDirection.status), next };
    }

    if (!fs.existsSync(paths.backlog)) {
      stage('7. High-level backlog', 'Not built', 'circle-outline');
      return {
        group: this.productGroup(stages, 'Backlog required'),
        next: action('Build high-level backlog', 'Derive product outcomes within the approved business, technical, and solution-architecture boundaries.', 'cis.backlogBuild'),
      };
    }

    const backlogResult = await this.workspaceQuery(['brd', 'backlog', 'status'], root);
    const backlog = stateOf(backlogResult, paths.backlog);
    stage('7. High-level backlog', backlog.status, stateIcon(backlog.status), paths.backlog);
    if (!isActiveCurrent(backlog)) {
      const next = isReadyForApproval(backlog)
        ? action('Approve high-level backlog', 'Record explicit human approval of the validated outcomes.', 'cis.backlogApprove')
        : backlog.valid === false
          ? action('Open high-level backlog', backlog.detail || 'Complete and sequence the product outcomes, then validate.', 'cis.open', [{ file: paths.backlog }])
          : action('Validate high-level backlog', 'Check whether the backlog is ready for approval.', 'cis.backlogValidate');
      return { group: this.productGroup(stages, backlog.status), next };
    }

    const definition = await this.workspaceQuery(['definition', 'status'], root);
    if (definition?.sessionId) {
      const definitionActivated = definition.active === false;
      const definitionStatus = definitionActivated
        ? 'Active'
        : definition.readyToActivate === true ? 'Ready for final approval' : 'In progress';
      stage('7a. Consolidated product definition', definitionStatus,
        definitionActivated ? 'pass' : 'circle-outline', undefined, 'cis.definitionWizard');
      if (!definitionActivated) {
        return {
          group: this.productGroup(stages, 'Consolidated product definition approval required'),
          next: action('Review and activate product definition',
            'Review the complete business, technical, architecture, dictionary, UI, diagram, and backlog baseline as one bounded product definition before feature authoring.',
            'cis.definitionWizard'),
        };
      }
    }

    const items = backlogResult.items || [];
    const linked = linkedFeatureItems(items);
    for (const item of linked) {
      const featureResult = await this.workspaceQuery(['brd', 'feature', 'status', '--item', String(item.id)], root);
      const featureFile = resolveWithin(root, featureResult.relativePath || item.featureSpecification || item.featureSpec);
      const feature = stateOf(featureResult, featureFile);
      stage(`8. ${item.id}`, feature.status, stateIcon(feature.status), featureFile);
      if (!isActiveCurrent(feature)) {
        const featureErrors = [...(featureResult.errors || []), ...(featureResult.validation?.errors || [])];
        const isTemplate = featureErrors.some(error => /TODO|TBD|placeholder|product-definition baseline|consolidated product definition/iu.test(String(error)));
        const next = isReadyForApproval(feature)
          ? action(`Approve ${item.id}`, 'Record explicit human approval of the validated feature specification.', 'cis.featureApprove', [String(item.id)])
          : feature.valid === false
            ? isTemplate
              ? action(`Draft ${item.id} with agent`, 'Replace the generated scaffold from the current governed product, technical, architecture, component, and UI baselines.', 'cis.featureAgentDraft', [String(item.id)])
              : action(`Open ${item.id} specification`, feature.detail || 'Complete this feature specification, then validate.', 'cis.open', [{ file: featureFile }])
            : action(`Validate ${item.id}`, 'Check whether the feature specification is ready for approval.', 'cis.featureValidate', [String(item.id)]);
        return { group: this.productGroup(stages, `${linked.length}/${items.length} features started`), next };
      }
    }

    const startable = nextStartableItem(items);
    if (startable) {
      stage('8. Feature specifications', `${linked.length}/${items.length} started`, 'list-unordered');
      return {
        group: this.productGroup(stages, `${linked.length}/${items.length} features started`),
        next: action(`Start ${startable.id}`, startable.outcome || 'Create the next dependency-ready feature specification.', 'cis.featureStart', [String(startable.id)]),
      };
    }

    stage('8. Feature specifications', items.length ? 'All approved' : 'No outcomes found', items.length ? 'pass' : 'warning');
    return {
      group: this.productGroup(stages, items.length ? 'Ready for change delivery' : 'Backlog needs outcomes'),
      next: items.length
        ? action('Product definition complete', 'The approved feature specifications are ready for governed change delivery.')
        : action('Open high-level backlog', 'Add at least one product outcome.', 'cis.open', [{ file: paths.backlog }]),
    };
  }

  productGroup(stages, description) {
    return this.node('Product definition', { description, children: stages, icon: 'map' });
  }

  async workspaceQuery(args, root) {
    try {
      return await this.cli.query([...args, '--workspace', root], {
        repository: false,
        acceptStructuredFailure: true,
      });
    }
    catch (error) {
      if (error?.data && typeof error.data === 'object') return error.data;
      return { status: 'Unavailable', valid: false, errors: [concise(error?.message || error)] };
    }
  }

  async journey(root) {
    const metadata = repositoryMetadata(root, this.vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    const paths = productPaths(root, metadata);
    const exists = file => file && fs.existsSync(file);
    const [brdResult, questions, intentResult, changesResult] = await Promise.all([
      this.workspaceQuery(['brd', 'status'], root),
      this.workspaceQuery(['technical-intent', 'questions', 'status', '--summary'], root),
      this.workspaceQuery(['technical-intent', 'status'], root),
      this.cli.query(['change', 'list']).catch(() => ({ changes: [] })),
    ]);
    const brd = stateOf(brdResult, paths.brd);
    const intent = stateOf(intentResult, paths.technicalIntent);
    const intentReady = isActiveCurrent(intent);
    const solutionResult = intentReady
      ? await this.workspaceQuery(['solution-design', 'status'], root)
      : { status: 'Waiting for technical intent', valid: false, current: false };
    const solution = stateOf(solutionResult, paths.overallSolutionDesign);
    const solutionReady = isActiveCurrent(solution);
    const uiQuestions = solutionReady
      ? await this.workspaceQuery(['ui-direction', 'questions', 'status', '--summary'], root)
      : { status: 'Waiting for solution design', complete: false, current: false };
    const uiResult = solutionReady && uiQuestions.complete === true && uiQuestions.current === true
      ? await this.workspaceQuery(['ui-direction', 'status'], root)
      : { status: 'Waiting for UI choices', valid: false, current: false };
    const uiDirection = stateOf(uiResult, paths.uiDirection);
    const uiReady = isActiveCurrent(uiDirection);
    const backlogResult = uiReady
      ? await this.workspaceQuery(['brd', 'backlog', 'status'], root)
      : { status: solutionReady ? 'Waiting for UI direction' : 'Waiting for solution design', valid: false, current: false, items: [] };
    const backlog = stateOf(backlogResult, paths.backlog);
    const stateNode = (label, value, file, command) => this.node(label, {
      description: value, file: exists(file) ? file : undefined, command,
      icon: ['Active', 'Complete'].includes(value) ? 'pass' : value === 'Not started' ? 'circle-outline' : 'history',
    });
    const questionnaireState = questions.complete && questions.current ? 'Complete'
      : questions.status === 'missing' ? 'Not started' : `${questions.answeredCount || 0}/${(questions.answeredCount || 0) + (questions.unansweredCount || 0)} resolved`;
    const active = (changesResult.changes || []).find(change => normalize(change.status) !== 'closed');
    const product = this.node('Product definition', {
      description: isActiveCurrent(brd) ? 'Business authority active' : brd.status,
      icon: 'symbol-ruler', children: [
        stateNode('Source evidence and references', exists(paths.brd) ? 'Indexed into product authority' : 'Add references', undefined, 'cis.contextSearch'),
        stateNode('Business requirements', brd.status, paths.brd),
        stateNode('High-level backlog', backlog.status, uiReady ? paths.backlog : undefined),
        stateNode('Feature specifications', isActiveCurrent(backlog) ? 'Outcome-by-outcome definition' : 'Waiting for backlog', undefined),
      ],
    });
    const technical = this.node('Technical definition', {
      description: isActiveCurrent(intent) ? 'Technical authority active' : intent.status,
      icon: 'circuit-board', children: [
        stateNode('Technology and architecture questionnaire', questionnaireState, paths.technicalQuestionnaire,
          'cis.technicalIntentQuestions'),
        stateNode('Technical intent', intent.status, paths.technicalIntent),
        stateNode('Overall solution design', solution.status, intentReady ? paths.overallSolutionDesign : undefined),
        stateNode('Component sheet', solutionReady || (intentReady && exists(paths.componentSheet))
          ? titleCase(solutionResult?.validation?.componentSheetStatus || documentStatus(paths.componentSheet))
          : intentReady ? 'Waiting for solution design' : 'Waiting for technical intent',
        intentReady ? paths.componentSheet : undefined),
        stateNode('High-level architecture diagrams', exists(paths.architectureDiagrams)
          ? titleCase(documentStatus(paths.architectureDiagrams))
          : solutionReady ? 'Not prepared' : 'Waiting for solution design',
        paths.architectureDiagrams, exists(paths.architectureDiagrams) ? 'cis.preview' : undefined),
        stateNode('Architecture decisions and standards', exists(paths.technicalIntent) ? 'Bound to technical authority' : 'Waiting for technical intent', paths.technicalIntent),
      ],
    });
    const uiQuestionnaireState = uiQuestions.complete && uiQuestions.current ? 'Complete'
      : uiQuestions.status === 'missing' ? 'Not started'
        : solutionReady ? `${uiQuestions.answeredCount || 0}/${(uiQuestions.answeredCount || 0) + (uiQuestions.unansweredCount || 0)} resolved`
          : 'Waiting for solution design';
    const experience = this.node('Experience definition', {
      description: uiReady ? 'UI direction active' : uiDirection.status,
      icon: 'paintcan', children: [
        stateNode('UI look and feel questionnaire', uiQuestionnaireState, solutionReady ? paths.uiDirectionQuestionnaire : undefined,
          'cis.uiDirectionQuestions'),
        stateNode('High-level UI direction', uiDirection.status,
          solutionReady && uiQuestions.complete === true ? paths.uiDirection : undefined),
        stateNode('Design guidelines and UI framework', solutionReady ? 'Bound to UI direction' : 'Waiting for solution design',
          solutionReady ? paths.uiDirection : undefined),
        stateNode('One-page visual system preview', exists(paths.uiSystemPreview)
          ? titleCase(documentStatus(paths.uiSystemPreview))
          : uiReady ? 'Not prepared' : 'Waiting for UI direction',
        paths.uiSystemPreview, exists(paths.uiSystemPreview) ? 'cis.preview' : undefined),
        stateNode('Feature wireframes and rendered designs', uiReady ? 'Defined per UI-bearing feature' : 'Waiting for UI direction', undefined),
      ],
    });
    const delivery = this.node('Feature delivery loop', {
      description: active ? `${active.id} · ${active.status}` : 'Begins after product definition',
      icon: 'git-pull-request', children: [
        this.node('1. Feature specification', { description: 'Bounded behavior and acceptance', icon: 'book' }),
        this.node('2. Impact and delivery plan', { description: 'Affected evidence, repositories, tasks, and dependencies', icon: 'list-tree' }),
        this.node('3. Wireframes and visual design', { description: 'Human review gate when frontend work applies', icon: 'layout' }),
        this.node('4. Implementation', { description: 'Governed agent or human work with retained evidence', icon: 'tools' }),
        this.node('5. Verification and acceptance', { description: 'Tests, security, assurance, handoff, and outcome acceptance', icon: 'verified-filled' }),
      ],
    });
    return [
      this.node('Journey map', { description: 'Product authority → technical authority → experience direction → repeatable delivery', icon: 'map' }),
      product, technical, experience, delivery,
    ];
  }

  async changes(root) {
    const result = await this.cli.query(['change', 'list']);
    const changes = (result.changes || []).filter(change => !this.filter
      || `${change.id} ${change.title} ${change.status}`.toLowerCase().includes(this.filter));
    if (!changes.length) return [this.node('No changes', { description: 'No CIS change dossiers were found.', icon: 'info' })];
    const enriched = await Promise.all(changes.map(async change => {
      if (normalize(change.status) === 'closed') return { change, phase: 'Closed', gate: 'Complete' };
      const [planResult, designResult] = await Promise.allSettled([
        this.cli.query(['plan', 'show', change.id]), this.cli.query(['design', 'status', change.id]),
      ]);
      const plan = planResult.status === 'fulfilled' ? planResult.value : {};
      const design = designResult.status === 'fulfilled' ? designResult.value : {};
      const taskStates = (plan.workItems || []).map(item => normalize(item.status));
      const designGate = design.gateStatus || 'Not applicable';
      const phase = normalize(designGate) === 'pausedforreview' ? 'Design review'
        : taskStates.some(value => ['inprogress', 'ready'].includes(value)) ? 'Implementation'
          : taskStates.length && taskStates.every(value => terminal.has(value)) ? 'Verification'
            : plan.planStatus ? 'Planning' : change.status || 'Proposed';
      return { change, phase, gate: designGate };
    }));
    return enriched.map(({ change, phase, gate }) => this.node(`${change.id}: ${change.title}`, {
      description: `${phase} · ${change.status} · ${gate}`,
      tooltip: `Phase: ${phase}; lifecycle: ${change.status}; blocking gate: ${gate}; projection: current`,
      file: resolveWithin(root, `${change.relativePath}/proposal.md`),
      command: 'cis.openChange', arguments: [change],
      icon: String(change.status).toLowerCase() === 'closed' ? 'pass' : 'git-pull-request',
      contextValue: 'cis.change', data: { ...change, phase, gate, freshness: 'current' },
    }));
  }

  evidence(root) {
    const metadata = repositoryMetadata(root, this.vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    const docs = metadata.documentationPath;
    const paths = productPaths(root, metadata);
    const groups = [
      ['Specifications', 'specs'], ['Architecture', 'architecture'], ['Design', 'design'], ['References', 'references'],
      ['Plans', 'plans'], ['Decisions', 'decisions'], ['Manual', 'manual'], ['Changes', 'changes'],
    ];
    const nodes = [this.node('Search bounded context', { command: 'cis.contextSearch', icon: 'search' })];
    const definitionVisuals = [
      ['High-level architecture diagrams', paths.architectureDiagrams, 'type-hierarchy-sub'],
      ['One-page visual system preview', paths.uiSystemPreview, 'preview'],
    ].filter(([, file]) => fs.existsSync(file)).map(([label, file, icon]) => this.node(label, {
      description: titleCase(documentStatus(file)), file, preview: true, icon,
    }));
    if (definitionVisuals.length) nodes.push(this.node('Definition visuals', {
      description: `${definitionVisuals.length} rendered artifacts`, children: definitionVisuals, icon: 'preview',
    }));
    for (const [label, relative] of groups) {
      const folder = resolveWithin(docs, relative);
      const files = folder ? markdownFiles(this.vscode, folder, true, 300) : [];
      nodes.push(this.node(label, { description: `${files.length} Markdown files`, children: files, icon: 'folder-library' }));
    }
    return nodes;
  }

  async runs() {
    const [providers, runs] = await Promise.all([this.cli.query(['agent', 'providers']), this.cli.query(['agent', 'runs', '--summary', '--limit', '10'])]);
    const providerNodes = (providers.providers || []).map(provider => this.node(provider.displayName || provider.id, {
      description: `${provider.kind}; ${provider.modes?.join('/') || 'no modes'}`,
      command: 'cis.agentDiagnose', arguments: [provider.id], icon: provider.directExecution ? 'hubot' : 'file-code',
      contextValue: provider.id === 'codex' ? 'cis.agentProvider.auth' : 'cis.agentProvider', data: provider,
    }));
    const runNodes = (runs.runs || []).map(run => this.node(run.runId, {
      description: `${run.status}; ${run.changeId}/${run.taskId}; attempt ${run.attempt}`,
      command: 'cis.agentShow', arguments: [run.runId], icon: stateIcon(run.status), contextValue: 'cis.agentRun', data: run,
    }));
    return [
      this.node('Providers', { description: `${providerNodes.length} discovered`, children: providerNodes, icon: 'hubot' }),
      this.node('Agent runs', { description: `${runNodes.length} retained`, children: runNodes, icon: 'history' }),
      this.node('Workflow run status', { command: 'cis.workflowStatus', icon: 'server-process' }),
      this.node('Test and coverage status', { command: 'cis.testStatus', icon: 'beaker' }),
      this.node('Security status', { command: 'cis.securityStatus', icon: 'shield' }),
      this.node('Verification status', { command: 'cis.verificationStatus', icon: 'verified-filled' }),
      this.node('Request agent work', { command: 'cis.requestAgentWork', icon: 'play' }),
    ];
  }

  async governance(root) {
    const [doctor, skills, standards, references] = await Promise.all([
      this.cli.query(['repo', 'doctor'], { acceptStructuredFailure: true }), this.cli.query(['skills', 'inventory', '--summary']),
      this.cli.query(['standards', 'inventory', '--summary']), this.cli.query(['references', 'validate']),
    ]);
    const findings = (doctor.findings || []).map(finding => this.node(finding.code, {
      description: `${finding.severity}: ${concise(finding.message, 120)}`,
      tooltip: concise(finding.message, 512), icon: finding.severity === 'error' ? 'error' : finding.severity === 'warning' ? 'warning' : 'info',
      data: finding,
    }));
    const metadata = repositoryMetadata(root, this.vscode.workspace.getConfiguration('cis').get('documentationRoot', 'docs/cis'));
    const governanceFiles = [
      ['Skills', '.github/skills'], ['Instructions', '.github/instructions'], ['Standards', `${path.relative(root, metadata.documentationPath).replaceAll('\\', '/')}/standards`],
      ['References', `${path.relative(root, metadata.documentationPath).replaceAll('\\', '/')}/references`],
    ].map(([label, relative]) => {
      const folder = resolveWithin(root, relative);
      const files = folder ? markdownFiles(this.vscode, folder, true, 300) : [];
      return this.node(label, { description: `${files.length} files`, children: files, icon: 'law' });
    });
    return [
      this.node('Repository Doctor findings', { description: `${findings.length}`, children: findings, icon: 'pulse' }),
      this.node('Skills inventory', { description: `${skills.skillCount || 0}; ${skills.status}`, command: 'cis.governanceInventory', arguments: ['skills'], icon: 'tools' }),
      this.node('Standards inventory', { description: `${standards.standardCount || 0}; ${standards.status}`, command: 'cis.governanceInventory', arguments: ['standards'], icon: 'law' }),
      this.node('References validation', { description: `${references.status}; ${references.errors || 0} errors`, command: 'cis.governanceInventory', arguments: ['references'], icon: 'references' }),
      ...governanceFiles,
    ];
  }
}

function repositoryMetadata(root, fallback) {
  const file = path.join(root, '.cis', 'repository.yml');
  const safeFallback = resolveWithin(root, fallback) || resolveWithin(root, 'docs/cis');
  if (!fs.existsSync(file)) return {
    initialized: false,
    onboardingMode: hasExistingRepositoryEvidence(root) ? 'import' : 'create',
    documentationRoot: path.relative(root, safeFallback).replaceAll('\\', '/'),
    documentationPath: safeFallback,
  };
  const text = fs.readFileSync(file, 'utf8');
  const id = /^\s*id:\s*['"]?([^'"\r\n]+)['"]?\s*$/mu.exec(text)?.[1]?.trim();
  const declared = /^documentation_root:\s*['"]?([^'"\r\n]+)['"]?\s*$/mu.exec(text)?.[1]?.trim() || fallback;
  const documentationPath = resolveWithin(root, declared) || safeFallback;
  return { initialized: true, onboardingMode: 'configured', id, documentationRoot: path.relative(root, documentationPath).replaceAll('\\', '/'), documentationPath };
}

function isProblemFinding(finding) {
  const severity = normalize(finding?.severity);
  return severity === 'error' || severity === 'warning';
}

function hasExistingRepositoryEvidence(root) {
  if (!root || !fs.existsSync(root)) return false;
  const ignored = new Set(['.git', '.cis', '.github', '.vscode', 'node_modules', 'bin', 'obj', 'dist', 'build', 'coverage', 'docs', 'cisdocs']);
  const marker = /^(?:package\.json|pyproject\.toml|pom\.xml|go\.mod|cargo\.toml|package\.swift|pubspec\.yaml|project\.godot|dockerfile|compose(?:\.[^.]+)?\.ya?ml|docker-compose(?:\.[^.]+)?\.ya?ml|[^.]+\.(?:sln|slnx|csproj|fsproj|vbproj|xcodeproj|xcworkspace|gradle|tf))$/iu;
  const source = /\.(?:cs|fs|vb|ts|tsx|js|jsx|mjs|cjs|py|java|kt|kts|swift|go|rs|rb|php|cpp|cc|cxx|c|h|hpp|dart|gd|vue|svelte)$/iu;
  const queue = [{ directory: root, depth: 0 }];
  let inspected = 0;
  while (queue.length && inspected < 500) {
    const current = queue.shift();
    let entries;
    try { entries = fs.readdirSync(current.directory, { withFileTypes: true }); }
    catch { continue; }
    for (const entry of entries) {
      if (++inspected > 500) break;
      if (entry.isDirectory()) {
        if (current.depth < 4 && !ignored.has(entry.name.toLowerCase()))
          queue.push({ directory: path.join(current.directory, entry.name), depth: current.depth + 1 });
      } else if (marker.test(entry.name) || source.test(entry.name)) return true;
    }
  }
  return false;
}

function markdownFiles(vscode, folder, recursive, limit = 300) {
  if (!folder || !fs.existsSync(folder)) return [];
  const output = [];
  const visit = (current, depth) => {
    if (output.length >= limit || depth > 5) return;
    for (const entry of fs.readdirSync(current, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      if (output.length >= limit) break;
      const full = resolveWithin(current, entry.name);
      if (!full) continue;
      if (entry.isFile() && entry.name.toLowerCase().endsWith('.md')) output.push(full);
      else if (recursive && entry.isDirectory()) visit(full, depth + 1);
    }
  };
  visit(folder, 0);
  return output.map(file => new CisTreeItem(vscode, path.basename(file, '.md'), { file }));
}

function stateIcon(status) {
  const value = String(status || '').toLowerCase();
  if (value === 'succeeded' || value === 'passed' || value === 'complete' || value === 'active') return 'pass';
  if (value === 'running' || value === 'starting') return 'sync~spin';
  if (value === 'cancelled' || value === 'interrupted') return 'circle-slash';
  if (value === 'failed' || value === 'timedout' || value === 'invalid-evidence') return 'error';
  return 'history';
}

function concise(value, limit = 180) {
  const text = String(value || '').replace(/\s+/gu, ' ').trim();
  return text.length <= limit ? text : `${text.slice(0, limit)}…`;
}

module.exports = { CisTreeItem, CisViewProvider, concise, hasExistingRepositoryEvidence, markdownFiles, repositoryMetadata, stateIcon };
