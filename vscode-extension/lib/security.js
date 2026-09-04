'use strict';

const crypto = require('node:crypto');
const path = require('node:path');

const deniedSegments = new Set(['node_modules', '.git', '.svn', '.hg', '.idea', '.vscode-test', 'secrets']);
const allowedLocalPrefixes = [
  '.cis/local/agents/runs/',
  '.cis/local/testing/runs/',
  '.cis/local/security/runs/',
  '.cis/local/workflows/',
  '.cis/local/diagnostics/',
];

function resolveWithin(root, relative, options = {}) {
  if (typeof root !== 'string' || typeof relative !== 'string' || !relative.trim() || path.isAbsolute(relative)) return undefined;
  const canonicalRoot = path.resolve(root);
  const candidate = path.resolve(canonicalRoot, relative);
  if (candidate !== canonicalRoot && !candidate.startsWith(canonicalRoot + path.sep)) return undefined;
  const normalized = path.relative(canonicalRoot, candidate).replaceAll('\\', '/');
  if (!isAllowedRelative(normalized, options)) return undefined;
  return candidate;
}

function isAllowedRelative(relative, options = {}) {
  const normalized = String(relative || '').replaceAll('\\', '/').replace(/^\.\//, '');
  const lower = normalized.toLowerCase();
  if (!normalized || normalized === '.') return true;
  const segments = lower.split('/');
  if (segments.some(segment => deniedSegments.has(segment))) return false;
  if (segments.some(segment => segment === '.env' || segment.startsWith('.env.'))) return false;
  if (lower === '.cis/local' || lower.startsWith('.cis/local/')) {
    return options.allowLocalEvidence === true && allowedLocalPrefixes.some(prefix => lower.startsWith(prefix));
  }
  return true;
}

function validateExecutable(value) {
  if (typeof value !== 'string' || !value.trim()) throw new Error('The CIS executable path is empty.');
  if (/[\0\r\n]/u.test(value)) throw new Error('The CIS executable path contains unsupported control characters.');
  return value.trim();
}

function validateArgument(value) {
  if (typeof value !== 'string' || /[\0\r\n]/u.test(value)) throw new Error('A CIS argument is invalid.');
  return value;
}

function redact(value) {
  let text = String(value ?? '');
  text = text.replace(/(authorization\s*[=:]\s*)(?:bearer\s+)?[^\s,;"']+/giu, '$1[REDACTED]');
  text = text.replace(/((?:token|password|secret|api[_-]?key|authorization)\s*[=:]\s*)([^\s,;"']+)/giu, '$1[REDACTED]');
  text = text.replace(/\b(?:gho|ghp|github_pat|sk|xox[baprs])-[-_A-Za-z0-9]{8,}\b/gu, '[REDACTED]');
  text = text.replace(/(bearer\s+)[-_A-Za-z0-9.]{8,}/giu, '$1[REDACTED]');
  return text;
}

function bound(value, maximum = 4096) {
  const text = redact(value);
  return text.length <= maximum ? text : `${text.slice(0, maximum)}\n… output truncated by CIS extension`;
}

const HTML_ENTITIES = Object.freeze({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' });

function escapeHtml(value) { return String(value ?? '').replace(/[&<>"']/gu, character => HTML_ENTITIES[character]); }

function nonce() { return crypto.randomBytes(18).toString('base64url'); }

function isValidWebviewMessage(message, allowedCommands) {
  return Boolean(message && typeof message === 'object' && !Array.isArray(message)
    && typeof message.command === 'string' && allowedCommands.has(message.command)
    && (message.value === undefined || typeof message.value === 'string'));
}

module.exports = {
  allowedLocalPrefixes,
  bound,
  escapeHtml,
  isAllowedRelative,
  isValidWebviewMessage,
  nonce,
  redact,
  resolveWithin,
  validateArgument,
  validateExecutable,
};
