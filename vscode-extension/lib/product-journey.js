'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { resolveWithin } = require('./security');

function productPaths(root, metadata) {
  const documentation = metadata.documentationPath;
  const paths = {
    workspace: path.join(root, '.cis', 'workspace.yml'),
    brd: path.join(documentation, 'specs', 'business-requirements.md'),
    technicalQuestionnaire: path.join(documentation, 'specs', 'technical-intent-questionnaire.md'),
    technicalIntent: path.join(documentation, 'specs', 'technical-intent-spec.md'),
    overallSolutionDesign: path.join(documentation, 'architecture', 'overall-solution-design.md'),
    architectureDiagrams: path.join(documentation, 'architecture', 'high-level-architecture-diagrams.md'),
    componentSheet: path.join(documentation, 'references', 'component-sheet.md'),
    dictionaryIndex: path.join(documentation, 'references', 'dictionary-index.md'),
    uiDirectionQuestionnaire: path.join(documentation, 'specs', 'ui-direction-questionnaire.md'),
    uiDirection: path.join(documentation, 'design', 'ui-direction.md'),
    uiSystemPreview: path.join(documentation, 'design', 'ui-system-preview.md'),
    uiSystemPreviewSvg: path.join(documentation, 'design', 'ui-system-preview.svg'),
    backlog: path.join(documentation, 'plans', 'high-level-backlog.md'),
  };
  const selectionsFile = path.join(root, '.cis', 'product-documents.json');
  const assertUnlinked = file => {
    let current = root;
    for (const segment of path.relative(root, file).split(path.sep)) {
      current = path.join(current, segment);
      try { if (fs.lstatSync(current).isSymbolicLink()) throw new Error('Document selections cannot use symbolic links.'); }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
    }
  };
  assertUnlinked(selectionsFile);
  if (fs.existsSync(selectionsFile)) {
    const selections = JSON.parse(fs.readFileSync(selectionsFile, 'utf8'));
    if (!selections || typeof selections !== 'object' || Array.isArray(selections)) throw new Error('Invalid document selections.');
    const keys = { business: 'brd', technical: 'technicalIntent', architecture: 'overallSolutionDesign',
      components: 'componentSheet', diagrams: 'architectureDiagrams', experience: 'uiDirection', delivery: 'backlog' };
    for (const [role, relative] of Object.entries(selections)) {
      const target = resolveWithin(root, relative);
      if (!Object.hasOwn(keys, role) || !target || !/\.md$/iu.test(relative)) throw new Error('Invalid project document selection.');
      assertUnlinked(target);
      paths[keys[role]] = target;
    }
  }
  return paths;
}

function documentStatus(file) {
  if (!file || !fs.existsSync(file)) return 'Missing';
  const text = fs.readFileSync(file, 'utf8');
  return /^status:\s*['"]?([^'"\r\n]+)['"]?\s*$/imu.exec(text)?.[1]?.trim() || 'Present';
}

function reviewMatchesBrd(run, file) {
  const reviewed = run?.taskDigest || run?.manifest?.taskDigest;
  if (!reviewed || !file || !fs.existsSync(file)) return undefined;
  const current = crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  return String(reviewed).toLowerCase() === current;
}

function stateOf(result, file) {
  const validation = result?.validation || {};
  const status = validation.effectiveStatus || validation.documentStatus
    || result?.effectiveStatus || result?.documentStatus || result?.status || documentStatus(file);
  const errors = validation.errors || result?.errors || [];
  const warnings = validation.warnings || result?.warnings || [];
  return {
    status: titleCase(status),
    valid: validation.valid ?? result?.valid,
    current: validation.current ?? result?.current,
    errors,
    warnings,
    detail: errors[0] || warnings[0] || '',
  };
}

function isActiveCurrent(state) {
  return normalize(state?.status) === 'active' && state.current !== false && state.valid !== false;
}

function isReadyForApproval(state) {
  return normalize(state?.status) === 'readyforapproval'
    || (state?.valid === true && state?.current !== false && ['draft', 'reviewrequired'].includes(normalize(state?.status)));
}

function isFeatureMissing(value) {
  const normalized = normalize(value);
  return !normalized || ['notcreated', 'none', 'missing'].includes(normalized);
}

function nextStartableItem(items) {
  const all = Array.isArray(items) ? items : [];
  const linked = new Map(all.map(item => [String(item.id), !isFeatureMissing(item.featureSpecification || item.featureSpec)]));
  return all.find(item => isFeatureMissing(item.featureSpecification || item.featureSpec)
    && (item.dependsOn || []).every(id => linked.get(String(id)) === true));
}

function linkedFeatureItems(items) {
  return (Array.isArray(items) ? items : []).filter(item => !isFeatureMissing(item.featureSpecification || item.featureSpec));
}

function normalize(value) {
  return String(value || '').trim().toLowerCase().replaceAll(/[^a-z0-9]/gu, '');
}

function titleCase(value) {
  const text = String(value || 'Unknown').trim().replaceAll(/[-_]+/gu, ' ');
  return text.replace(/\b\w/gu, match => match.toUpperCase());
}

module.exports = {
  documentStatus, isActiveCurrent, isFeatureMissing, isReadyForApproval,
  linkedFeatureItems, nextStartableItem, normalize, productPaths, reviewMatchesBrd, stateOf, titleCase,
};
