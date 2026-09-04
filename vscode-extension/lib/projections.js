'use strict';

const terminal = new Set(['complete', 'completed', 'approved', 'accepted', 'decomposed', 'deferred', 'cancelled']);

function projectChangeOverview(changeResult, planResult, designResult, decisionResult = {}) {
  const change = changeResult.change || changeResult;
  const tasks = (planResult.workItems || []).map(item => ({
    id: item.id,
    title: item.title,
    category: item.category,
    complexity: item.complexity,
    status: item.status,
    dependencies: item.dependsOn || [],
    requirementIds: item.requirementIds || [],
    impactIds: item.impactIds || [],
    acceptanceCriteria: item.acceptanceCriteria,
    validation: item.validation,
    approvalGate: item.approvalGate,
    targets: item.targets || [],
    frontendType: item.frontendType,
    taskTypeKey: item.taskTypeKey,
    taskPath: change.relativePath && item.taskPath ? `${change.relativePath}/${item.taskPath}` : item.taskPath,
  }));
  const executable = tasks.filter(task => task.category !== 'coordination' && !['wireframe', 'design'].includes(task.category));
  const completed = executable.filter(task => terminal.has(normalize(task.status))).length;
  const next = tasks.find(task => normalize(task.status) === 'inprogress')
    || tasks.find(task => normalize(task.status) === 'ready')
    || tasks.find(task => normalize(task.status) === 'draft' && task.dependencies.every(id => terminal.has(normalize(tasks.find(candidate => candidate.id === id)?.status))));
  const impactIds = [...new Set((planResult.workItems || []).flatMap(item => item.impactIds || []))].sort();
  return {
    identity: { id: change.id, title: change.title, lifecycle: change.status, outcome: change.outcome },
    canonicalDocuments: change.relativePath ? {
      proposal: `${change.relativePath}/proposal.md`, impact: `${change.relativePath}/impact.md`, decisions: `${change.relativePath}/decisions.md`,
      plan: `${change.relativePath}/plan.md`, wireframes: `${change.relativePath}/wireframes.md`, design: `${change.relativePath}/design.md`,
      testCases: `${change.relativePath}/test-cases.md`, verification: `${change.relativePath}/verification.md`,
    } : {},
    scope: { baselineKind: change.baselineKind, baseline: change.baseline, roots: change.roots || [], acceptedImpactIds: impactIds },
    progress: { planStatus: planResult.planStatus, completedTasks: completed, executableTasks: executable.length,
      nextRecommendedAction: next ? `${normalize(next.status) === 'draft' ? 'Start' : 'Continue'} ${next.id}: ${next.title}` : 'Review final verification state.' },
    design: { gate: designResult.gateStatus || 'Not applicable', approval: designResult.approvalStatus || 'Not reviewed',
      artifactCount: designResult.artifacts?.length || 0 },
    decisions: decisionResult.decisions || [],
    tasks,
  };
}

function normalize(value) { return String(value || '').trim().toLowerCase().replaceAll('-', ''); }

module.exports = { normalize, projectChangeOverview, terminal };
