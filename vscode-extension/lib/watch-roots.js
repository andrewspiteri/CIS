'use strict';
const fs = require('node:fs');
const path = require('node:path');

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
  const relative = path.relative(root, uri.fsPath).replaceAll('\\', '/');
  if (relative.startsWith('.cis/local/')) {
    return !/^\.cis\/local\/(?:graph\/context\.db(?:-wal|-journal)?$|(?:agents|testing|security)\/runs\/|workflows\/)/u.test(relative);
  }
  return relative.split('/').some(segment => [
    'node_modules', 'bin', 'obj', 'dist', 'build', 'coverage', '.git', '.next', 'artifacts', '.artifacts',
  ].includes(segment.toLowerCase()));
}

module.exports = { workspaceWatchRoots, ignoredWatchEvent };
