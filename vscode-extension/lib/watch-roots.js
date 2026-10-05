'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');

// Read routing paths from the canonical workspace registry, including repositories
// that are not open as VS Code folders. Domain status remains the CLI's responsibility.
function workspaceWatchRoots(roots) {
  const selected = new Map();
  const add = root => {
    const absolute = path.resolve(root);
    selected.set(process.platform === 'win32' ? absolute.toLowerCase() : absolute, absolute);
  };
  for (const root of roots.filter(Boolean)) add(root);
  for (const root of [...selected.values()]) {
    const registry = path.join(root, '.cis', 'workspace.yml');
    try {
      if (fs.statSync(registry).size > 1024 * 1024) continue;
      let repositories = false;
      for (const line of fs.readFileSync(registry, 'utf8').replace(/^\uFEFF/u, '').split(/\r?\n/u)) {
        if (/^repositories:\s*$/u.test(line)) { repositories = true; continue; }
        if (/^[a-z_]+:/u.test(line)) repositories = false;
        const match = repositories && /^\s+(?:-\s+)?path:\s*(.+?)\s*$/u.exec(line);
        if (!match || selected.size >= 128) continue;
        let relative = match[1];
        if (relative.startsWith('"')) {
          const quoted = /^("(?:[^"\\]|\\.)*")\s*(?:#.*)?$/u.exec(relative);
          if (!quoted) continue;
          try { relative = JSON.parse(quoted[1]); } catch { continue; }
        }
        else if (relative.startsWith("'")) relative = /^'((?:[^']|'')*)'\s*(?:#.*)?$/u.exec(relative)?.[1]?.replaceAll("''", "'");
        else relative = relative.replace(/\s+#.*$/u, '').trim();
        if (!relative || typeof relative !== 'string' || /^[!&*|>]/u.test(relative)) continue;
        const participant = path.resolve(root, relative);
        if (fs.existsSync(path.join(participant, '.cis', 'repository.yml'))) add(participant);
      }
    } catch { /* Unavailable registry: keep watching the open folders; manual refresh still rechecks the CLI. */ }
  }
  return [...selected.values()];
}

function ignoredWatchEvent(uri, root) {
  if (!uri?.fsPath) return false;
  const relative = path.relative(root, uri.fsPath).replaceAll('\\', '/').toLowerCase();
  if (relative.startsWith('.cis/local/')) {
    return !/^\.cis\/local\/(?:graph\/context\.db(?:-wal|-journal)?$|(?:agents|testing|security)\/runs\/|workflows\/)/u.test(relative);
  }
  if (relative === '.github/copilot-runtime' || relative.startsWith('.github/copilot-runtime/')) return true;
  return relative.split('/').some(segment => [
    'node_modules', 'bin', 'obj', 'dist', 'build', 'coverage', '.git', '.next', 'artifacts', '.artifacts',
    '.idea', '.vs', '.codex-tmp', '.stryker-tmp', '.nuxt', '.output', '.svelte-kit', '.terraform',
    'out', 'build_out', 'nongit', 'skills-quarantine', '_old', 'tmp', 'temp', '.tmp', '.temp',
    'cache', '.cache', '__pycache__', '.pytest_cache', '.mypy_cache', '.ruff_cache',
    'test-results', 'testresults', 'playwright-report', 'blob-report',
  ].includes(segment.toLowerCase()));
}

// Editors can notify again when discovering unchanged MCP configuration. Seed
// these small, known files before watching; unknown/unreadable inputs still invalidate.
function createConfigurationWatchFilter(root) {
  const fingerprint = relative => {
    try {
      const absolute = path.join(root, relative);
      for (const part of [path.dirname(absolute), absolute]) if (fs.lstatSync(part).isSymbolicLink()) return undefined;
      const stat = fs.statSync(absolute);
      if (!stat.isFile() || stat.size > 1024 * 1024) return undefined;
      return createHash('sha256').update(fs.readFileSync(absolute)).digest('hex');
    } catch (error) { return error.code === 'ENOENT' ? 'missing' : undefined; }
  };
  const states = new Map(['.github/mcp.json', '.vscode/mcp.json', '.mcp.json'].map(relative => [relative, fingerprint(relative)]));
  return uri => {
    if (!uri?.fsPath) return false;
    const relative = path.relative(root, uri.fsPath).replaceAll('\\', '/');
    const key = process.platform === 'win32' ? relative.toLowerCase() : relative;
    if (!states.has(key)) return false;
    const previous = states.get(key);
    const current = fingerprint(relative);
    states.set(key, current);
    return current !== undefined && previous === current;
  };
}

module.exports = { workspaceWatchRoots, ignoredWatchEvent, createConfigurationWatchFilter };
