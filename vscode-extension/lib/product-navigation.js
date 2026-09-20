'use strict';

const path = require('node:path');
const { resolveWithin } = require('./security');

function featureArgument(root, feature, page) { return { root, slug: feature.plan.slug, page }; }
function sameRepository(a, b) {
  if (!a || !b) return false;
  const normalize = value => process.platform === 'win32' ? path.resolve(value).toLowerCase() : path.resolve(value);
  return normalize(a) === normalize(b);
}

function repositoryNodes(view, root, navigation) {
  return (navigation.workspace?.repositories || []).map(repository => {
    const features = (navigation.features || []).filter(feature => sameRepository(feature.plan.repositoryPath, repository.repositoryPath)
      || feature.plan.integrationRepositories.includes(repository.id) || feature.repositoryWork.some(work => work.repositoryId === repository.id));
    return view.node(repository.id, { id: `repo:${repository.id}`, icon: 'repo',
      description: repository.role === 'authority' ? 'Product authority' : repository.participation === 'dependency' ? 'External dependency' : `${features.length} high-level features`,
      children: [view.node('Open repository folder', { command: 'cis.openRepository', arguments: [{ root, id: repository.id }], icon: 'folder-opened' }),
        ...features.map(feature => view.node(feature.plan.title, { id: `repo:${repository.id}:feature:${feature.plan.slug}`,
          command: 'cis.featureWizard', arguments: [featureArgument(root, feature)], description: feature.status, icon: 'lightbulb' }))] });
  });
}

function featureNodes(view, root, navigation, changes = []) {
  const repositories = navigation.workspace?.repositories || [];
  return (navigation.features || []).map(feature => {
    const argument = featureArgument(root, feature);
    const work = feature.repositoryWork || [];
    const repositoryIds = [...new Set([
      repositories.find(repository => sameRepository(repository.repositoryPath, feature.plan.repositoryPath))?.id,
      ...feature.plan.integrationRepositories, ...work.map(item => item.repositoryId),
    ].filter(Boolean))];
    return view.node(feature.plan.title, { id: `feature:${feature.plan.slug}`, description: `${feature.status} · ${feature.reviewedPages}/${feature.totalPages} steps`,
      icon: 'lightbulb', contextValue: 'cis.feature', data: argument,
      command: 'cis.featureWizard', arguments: [argument], children: [
        view.node('Resume feature definition', { command: 'cis.featureWizard', arguments: [argument], icon: 'map' }),
        view.node('Repository work breakdown', { description: `${work.length} proposed repository features`,
          command: 'cis.featureWizard', arguments: [featureArgument(root, feature, 'delivery')], icon: 'list-tree' }),
        ...feature.errors.map(error => view.node('Needs attention', { description: error, icon: 'warning' })),
        ...repositoryIds.map(id => {
          const items = work.filter(item => item.repositoryId === id);
          return view.node(id, { id: `feature:${feature.plan.slug}:repo:${id}`, icon: 'repo',
            description: items.length ? `${items.length} repository features` : 'Scope to break down',
            children: items.length ? items.map(item => view.node(item.title, {
              id: `feature:${feature.plan.slug}:work:${item.id}`, icon: 'symbol-event',
              description: item.dependsOn.length ? `Depends on ${item.dependsOn.join(', ')}` : 'No declared dependencies',
              tooltip: `${item.id} · Proposed work\n${item.scope}`,
              command: 'cis.featureWizard', arguments: [featureArgument(root, feature, 'delivery')],
              children: item.changeIds.map(changeId => {
                const change = changes.find(candidate => candidate.id === changeId);
                return view.node(change ? `${change.id}: ${change.title}` : changeId, { id: `feature:${feature.plan.slug}:work:${item.id}:change:${changeId}`,
                  description: change?.status || 'Linked change unavailable', icon: 'git-pull-request',
                  command: change ? 'cis.openChange' : undefined, arguments: change ? [change] : [] });
              }),
            })) : [view.node('Define repository work', { command: 'cis.featureWizard', arguments: [featureArgument(root, feature, 'delivery')], icon: 'edit' })] });
        }),
        view.node('Feature request', { file: resolveWithin(root, feature.plan.requestPath), icon: 'markdown' }),
      ] });
  });
}

function linkedChanges(feature, changes, authorityId) {
  const ids = new Set((feature.repositoryWork || []).flatMap(work => work.changeIds));
  const source = `${authorityId}:feature-intake:${feature.plan.slug}`;
  return changes.filter(change => ids.has(change.id) || (change.roots || []).some(root => root.id === source));
}

module.exports = { featureNodes, repositoryNodes, linkedChanges };
