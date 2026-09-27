'use strict';

const { bound } = require('./security');

// Presentation only: these checked transport states never grant permission or change readiness.
const failures = Object.freeze({
  'untrusted-workspace': 'CIS commands are disabled in this workspace. Review Workspace Trust before running them.',
  'missing-cli': 'The CIS command could not start. Check the CIS executable path in VS Code Settings.',
  'timeout': 'CIS did not finish within the time limit. Check the current state before retrying; changes may already have been made.',
  'cancelled': 'The command was cancelled. Check the current state before retrying; cancellation does not undo completed changes.',
  'invalid-evidence': 'CIS could not read a usable result. Readiness is unverified; check the CLI output and version before retrying.',
  'output-limit': 'The CIS response exceeded the extension\'s size limit. The result is incomplete; check the CLI output for details.',
  'stale-evidence': 'The displayed evidence is out of date. Refresh it, then review any remaining findings; refreshing does not approve them.',
  'command-failed': 'CIS did not complete this command. Review the reported findings and current state before retrying.',
});

function failureMessage(error) {
  const detail = bound(error?.message || String(error || ''), 512);
  const explanation = failures[error?.kind];
  if (explanation) return `${explanation}${detail ? ` Details: ${detail}` : ''}`;
  // Keep domain blockers visible. Unknown codes do not establish a cause or a recovery operation.
  if (error?.kind && error.kind !== 'no-authority')
    return `CIS could not explain the reported state (${bound(error.kind, 80)}). Review the details and current state before retrying.${detail ? ` Details: ${detail}` : ''}`;
  return detail || 'CIS could not confirm the outcome. Check the current state and available diagnostics before retrying.';
}

function sourceOverviewNote(summary) {
  if (summary?.kind === 'local-model') return 'Selective overview from a local model. Review the full source before deciding; supporting quotations do not verify every claim.';
  if (summary?.kind === 'excerpt') return 'Selected source text. Open the full document to review its requirements and exceptions.';
  if (summary?.kind === 'unavailable') return 'No readable overview is available. Use the source document where it is accessible.';
  if (summary && summary.kind !== 'repository') return 'The overview method is not recognized. Review the full source before relying on this text.';
  return '';
}

module.exports = { failureMessage, sourceOverviewNote };
