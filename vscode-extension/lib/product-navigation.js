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
      || feature.plan.integrationRepositories.includes(repository.id) || (feature.stories || []).some(story => story.repositoryIds.includes(repository.id)));
    return view.node(repository.id, { id: `repo:${repository.id}`, icon: 'repo',
      description: repository.role === 'authority' ? 'Product authority' : repository.participation === 'dependency' ? 'External dependency' : `${features.length} high-level features`,
      children: [view.node('Open repository folder', { command: 'cis.openRepository', arguments: [{ root, id: repository.id }], icon: 'folder-opened' }),
        ...features.map(feature => view.node(feature.plan.title, { id: `repo:${repository.id}:feature:${feature.plan.slug}`,
          command: 'cis.featureWizard', arguments: [featureArgument(root, feature)], description: feature.status, icon: 'lightbulb' }))] });
  });
}

function featureNodes(view, root, navigation) {
  const saved = (navigation.features || []).map(feature => {
    const argument = featureArgument(root, feature);
    const stories = feature.stories || [];
    return view.node(feature.plan.title, { id: `feature:${feature.plan.slug}`, description: `${feature.status} · ${feature.reviewedPages}/${feature.totalPages} steps`,
      icon: 'lightbulb', contextValue: 'cis.feature', data: argument,
      command: 'cis.featureWizard', arguments: [argument], children: [
        view.node('Resume feature definition', { command: 'cis.featureWizard', arguments: [argument], icon: 'map' }),
        view.node('Story breakdown', { description: `${stories.length} stories`,
          command: 'cis.featureWizard', arguments: [featureArgument(root, feature, 'delivery')], icon: 'list-tree',
          children: stories.map(story => view.node(story.title, {
            id: `feature:${feature.plan.slug}:story:${story.id}`, icon: 'symbol-event',
            description: `${story.phase} · ${story.status} · ${story.repositoryIds.length} linked repositories`,
            command: 'cis.featureStory', arguments: [{ root, slug: feature.plan.slug, storyId: story.id }],
            children: [...(story.tasks || []).map(task => view.node(`${task.id} · ${task.title}`, {
              id: `feature:${feature.plan.slug}:story:${story.id}:task:${task.id}`, icon: task.status === 'Complete' ? 'pass' : 'tasklist',
              description: task.status, command: 'cis.featureStory', arguments: [{ root, slug: feature.plan.slug, storyId: story.id, taskId: task.id }],
              children: task.repositoryIds.map(id => view.node(id, { id: `feature:${feature.plan.slug}:story:${story.id}:task:${task.id}:repo:${id}`,
                icon: 'repo', command: 'cis.openRepository', arguments: [{ root, id }] })),
            })), ...story.repositoryIds.map(id => view.node(id, { id: `feature:${feature.plan.slug}:story:${story.id}:repo:${id}`,
              icon: 'repo', command: 'cis.openRepository', arguments: [{ root, id }] }))],
          })),
        }),
        ...feature.errors.map(error => view.node('Needs attention', { description: error, icon: 'warning' })),
        view.node('Feature request', { file: resolveWithin(root, feature.plan.requestPath), icon: 'markdown' }),
      ] });
  });
  const linked = new Set((navigation.features || []).map(feature => feature.plan.backlogItemId).filter(Boolean));
  const planned = (navigation.backlogFeatures || []).filter(item => !linked.has(item.id)).map(item => view.node(item.title, {
    id: `backlog-feature:${item.id}`, contextValue: 'cis.backlogFeature', icon: 'lightbulb',
    description: `${item.id} · ${item.status}`, command: 'cis.backlogFeature', arguments: [{ root, itemId: item.id }],
    children: [view.node(item.canStart ? 'Start feature definition' : 'Review prerequisites', {
      command: 'cis.backlogFeature', arguments: [{ root, itemId: item.id }], icon: item.canStart ? 'map' : 'info' }),
      ...(item.issues || []).map(issue => view.node('Needs attention', { description: issue, icon: 'warning' }))],
  }));
  return [...saved, ...planned];
}

function linkedChanges(feature, changes, authorityId) {
  const ids = new Set((feature.repositoryWork || []).flatMap(work => work.changeIds));
  const source = `${authorityId}:feature-intake:${feature.plan.slug}`;
  return changes.filter(change => ids.has(change.id) || (change.roots || []).some(root => root.id === source));
}

module.exports = { featureNodes, repositoryNodes, linkedChanges };
