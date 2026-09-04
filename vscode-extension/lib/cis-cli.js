'use strict';

const childProcess = require('node:child_process');
const { bound, redact, validateArgument, validateExecutable } = require('./security');
const { openCommandProgressPanel } = require('./webview');

const OUTPUT_LIMIT = 4 * 1024 * 1024;
const QUERY_CACHE_TTL_MS = 5_000;
const COMPATIBLE_CLI = Object.freeze({ major: 0, minimumMinor: 3 });

class CisCliError extends Error {
  constructor(message, kind, exitCode, details, data) {
    super(message);
    this.name = 'CisCliError';
    this.kind = kind;
    this.exitCode = exitCode;
    this.details = details;
    this.data = data;
  }
}

class CisCli {
  constructor(vscode, output, authority, processes = childProcess) {
    this.vscode = vscode;
    this.output = output;
    this.authority = authority;
    this.processes = processes;
    this.queryCache = new Map();
    this.versionCache = undefined;
  }

  executable() {
    return validateExecutable(this.vscode.workspace.getConfiguration('cis').get('executablePath', 'cis'));
  }

  root() {
    const root = this.authority.root();
    if (!root) throw new CisCliError(this.authority.needsSelection()
      ? 'Select the CIS authority repository first.' : 'Open a repository folder first.', 'no-authority');
    return root;
  }

  ensureTrusted() {
    if (this.vscode.workspace.isTrusted === false)
      throw new CisCliError('Trust this workspace before running CIS commands.', 'untrusted-workspace');
  }

  arguments(args, options = {}) {
    const validated = args.map(validateArgument);
    if (options.repository !== false && !validated.includes('--repo')) validated.push('--repo', this.root());
    if (options.format !== false && !validated.includes('--format')) validated.push('--format', options.format || 'json');
    return validated;
  }

  query(args, options = {}) {
    this.ensureTrusted();
    const root = options.repository === false ? this.authority.root() : this.root();
    const executable = this.executable();
    const commandArgs = this.arguments(args, options);
    const timeout = options.timeout || 30_000;
    const cacheable = options.cache !== false && isCacheableQuery(args);
    const cacheKey = cacheable ? JSON.stringify([executable, root, commandArgs, timeout, options.acceptStructuredFailure === true]) : undefined;
    const cached = cacheKey ? this.queryCache.get(cacheKey) : undefined;
    if (cached && (cached.expiresAt === Infinity || cached.expiresAt > Date.now())) return cached.promise;
    if (!cacheable) this.clearQueryCache();
    this.output.appendLine(`$ ${safeCommandDisplay(executable, commandArgs)}`);
    const operation = new Promise((resolve, reject) => {
      this.processes.execFile(executable, commandArgs,
        { cwd: root, windowsHide: true, timeout, maxBuffer: OUTPUT_LIMIT, shell: false },
        (error, stdout, stderr) => {
          const safeError = bound(stderr, 16_384).trim();
          if (safeError) this.output.appendLine(safeError);
          const exitCode = typeof error?.code === 'number' ? error.code : error ? 1 : 0;
          const text = String(stdout || '').trim();
          let data;
          if (text) {
            try { data = JSON.parse(text); }
            catch {
              return reject(new CisCliError('CIS returned malformed JSON.', 'invalid-evidence', exitCode,
                bound(text, 1024)));
            }
          }
          if (error) {
            const kind = error.killed ? 'timeout' : error.code === 'ENOENT' ? 'missing-cli' : 'command-failed';
            if (options.acceptStructuredFailure === true && kind === 'command-failed'
                && data && typeof data === 'object' && !Array.isArray(data))
              return resolve({ ...data, _process: { exitCode, failed: true } });
            return reject(new CisCliError(messageFrom(data, safeError, error.message), kind, exitCode, safeError, data));
          }
          if (!data || typeof data !== 'object' || Array.isArray(data))
            return reject(new CisCliError('CIS returned no structured result.', 'invalid-evidence', exitCode));
          resolve({ ...data, _process: { exitCode } });
        });
    });
    if (!cacheKey) return operation;
    const entry = { promise: undefined, expiresAt: Infinity };
    entry.promise = operation.then(result => {
      entry.expiresAt = Date.now() + QUERY_CACHE_TTL_MS;
      return result;
    }, error => {
      if (this.queryCache.get(cacheKey) === entry) this.queryCache.delete(cacheKey);
      throw error;
    });
    this.queryCache.set(cacheKey, entry);
    return entry.promise;
  }

  async version() {
    this.ensureTrusted();
    const root = this.authority.root();
    const executable = this.executable();
    const key = JSON.stringify([executable, root]);
    if (this.versionCache?.key === key
        && (this.versionCache.expiresAt === Infinity || this.versionCache.expiresAt > Date.now()))
      return this.versionCache.promise;
    const entry = { key, promise: undefined, expiresAt: Infinity };
    const operation = new Promise((resolve, reject) => {
      this.processes.execFile(executable, ['--version'], { cwd: root, windowsHide: true, timeout: 10_000,
        maxBuffer: 64 * 1024, shell: false }, (error, stdout, stderr) => {
        if (error) return reject(new CisCliError(bound(stderr || error.message, 1024),
          error.code === 'ENOENT' ? 'missing-cli' : 'command-failed', typeof error.code === 'number' ? error.code : 1));
        const raw = String(stdout).trim();
        const match = /^(\d+)\.(\d+)\.(\d+)(?:[+-].*)?$/u.exec(raw);
        if (!match) return reject(new CisCliError('CIS returned an unsupported version response.', 'invalid-evidence', 0));
        const version = { raw, major: Number(match[1]), minor: Number(match[2]), patch: Number(match[3]) };
        version.compatible = version.major === COMPATIBLE_CLI.major && version.minor >= COMPATIBLE_CLI.minimumMinor;
        resolve(version);
      });
    });
    entry.promise = operation.then(result => {
      entry.expiresAt = Date.now() + QUERY_CACHE_TTL_MS;
      return result;
    }, error => {
      if (this.versionCache === entry) this.versionCache = undefined;
      throw error;
    });
    this.versionCache = entry;
    return entry.promise;
  }

  clearQueryCache() {
    this.queryCache.clear();
    this.versionCache = undefined;
  }

  async runForeground(title, args, options = {}) {
    this.ensureTrusted();
    const root = this.root();
    const executable = this.executable();
    this.clearQueryCache();
    const commandArgs = this.arguments(args, { format: options.format || 'agent', repository: options.repository });
    this.output.appendLine(`$ ${safeCommandDisplay(executable, commandArgs)}`);
    return this.vscode.window.withProgress({
      location: this.vscode.ProgressLocation.Notification,
      title,
      cancellable: options.cancellable === true,
    }, (progress, cancellation) => new Promise((resolve, reject) => {
      const started = Date.now();
      const process = this.processes.spawn(executable, commandArgs, { cwd: root, windowsHide: true, shell: false,
        stdio: ['ignore', 'pipe', 'pipe'] });
      let stdout = ''; let stderr = ''; let truncated = false; let cancelled = false; let finished = false; let detail;
      const command = safeCommandDisplay(executable, commandArgs);
      const cancelRun = () => { if (!finished) { cancelled = true; process.kill(); } };
      const detailModel = (state, extra = {}) => ({ state, command, cancellable: options.cancellable === true,
        durationMs: Date.now() - started, stdout: bound(stdout, 32_768), stderr: bound(stderr, 32_768), truncated, ...extra });
      const ensureDetail = () => {
        if (!detail && typeof this.vscode.window.createWebviewPanel === 'function')
          detail = openCommandProgressPanel(this.vscode, title, detailModel('running'), cancelRun);
        return detail;
      };
      const collect = (kind, chunk) => {
        const safe = redact(String(chunk));
        this.output.append(safe.slice(0, Math.max(0, OUTPUT_LIMIT - stdout.length - stderr.length)));
        if (kind === 'stdout') stdout += safe; else stderr += safe;
        if (stdout.length + stderr.length > OUTPUT_LIMIT) {
          stdout = stdout.slice(0, OUTPUT_LIMIT); stderr = stderr.slice(0, Math.max(0, OUTPUT_LIMIT - stdout.length));
          truncated = true;
        }
      };
      process.stdout.on('data', chunk => collect('stdout', chunk));
      process.stderr.on('data', chunk => collect('stderr', chunk));
      const detailTimer = options.details === false
        ? undefined
        : setTimeout(() => { if (!finished) ensureDetail(); }, 750);
      process.on('error', error => {
        if (finished) return;
        finished = true; clearTimeout(detailTimer); clearInterval(timer);
        const kind = error.code === 'ENOENT' ? 'missing-cli' : 'runner-failure';
        ensureDetail()?.update(detailModel('failure', { failureKind: kind }));
        reject(new CisCliError(bound(error.message), kind));
      });
      cancellation.onCancellationRequested(cancelRun);
      const timer = setInterval(() => {
        progress.report({ message: `Running for ${Math.floor((Date.now() - started) / 1000)}s` });
        detail?.update(detailModel(cancelled ? 'cancelled' : 'running'));
      }, 5000);
      process.on('close', code => {
        if (finished) return;
        finished = true; clearInterval(timer); clearTimeout(detailTimer);
        this.clearQueryCache();
        const result = { state: cancelled ? 'cancelled' : code === 0 ? 'success' : 'failure', exitCode: code,
          stdout: bound(stdout, OUTPUT_LIMIT), stderr: bound(stderr, OUTPUT_LIMIT), truncated, durationMs: Date.now() - started };
        if (detail || cancelled || code !== 0 || truncated) ensureDetail()?.update(detailModel(result.state, {
          exitCode: code, failureKind: cancelled ? 'cancellation' : code === 0 ? undefined : 'command-failed', cancellable: false,
        }));
        if (cancelled) return reject(new CisCliError('CIS command was cancelled.', 'cancelled', code, undefined, result));
        if (code !== 0) return reject(new CisCliError(foregroundFailureMessage(stdout, stderr, code),
          'command-failed', code, undefined, result));
        resolve(result);
      });
    }));
  }
}

function isCacheableQuery(args) {
  const command = args.join(' ');
  return [
    /^repo doctor(?: |$)/u,
    /^change (?:list|show)(?: |$)/u,
    /^plan show(?: |$)/u,
    /^design (?:status|validate)(?: |$)/u,
    /^decision list(?: |$)/u,
    /^agent (?:providers|runs|show)(?: |$)/u,
    /^agent provider diagnose(?: |$)/u,
    /^ai (?:status|providers)(?: |$)/u,
    /^context search(?: |$)/u,
    /^graph (?:related|status)(?: |$)/u,
    /^workflow status(?: |$)/u,
    /^test status(?: |$)/u,
    /^security status(?: |$)/u,
    /^verify validate(?: |$)/u,
    /^skills inventory(?: |$)/u,
    /^standards inventory(?: |$)/u,
    /^references (?:validate|inventory|status)(?: |$)/u,
    /^brd (?:status|questions guidance|questions status|review status|backlog status|feature status)(?: |$)/u,
    /^technical-intent (?:status|questions status)(?: |$)/u,
  ].some(pattern => pattern.test(command));
}

function foregroundFailureMessage(stdout, stderr, exitCode) {
  const outputLines = String(stdout || '').split(/\r?\n/u).map(line => line.trim()).filter(Boolean);
  const diagnostic = outputLines.find(line => /^diagnostic=ERROR:/iu.test(line))
    || outputLines.find(line => /^(?:error|diagnostic)=/iu.test(line));
  if (diagnostic) return bound(diagnostic.replace(/^(?:error|diagnostic)=/iu, '').trim(), 1024);
  const nonTelemetryError = String(stderr || '').split(/\r?\n/u).map(line => line.trim()).filter(Boolean)
    .find(line => !line.startsWith('agent-event='));
  return bound(nonTelemetryError || outputLines[0] || `CIS exited with code ${exitCode}.`, 1024);
}

function messageFrom(data, stderr, fallback) {
  const diagnostic = Array.isArray(data?.diagnostics) ? data.diagnostics.find(item => typeof item === 'string') : undefined;
  return bound(diagnostic || stderr || fallback || 'CIS command failed.', 1024);
}

function safeCommandDisplay(executable, args) {
  const hiddenAfter = new Set(['--message', '--reason', '--rationale', '--answer']);
  let hideNext = false;
  return [executable, ...args].map(value => {
    if (hideNext) { hideNext = false; return '[REDACTED-INPUT]'; }
    if (hiddenAfter.has(value)) hideNext = true;
    return /\s/u.test(value) ? `"${redact(value)}"` : redact(value);
  }).join(' ');
}

module.exports = { COMPATIBLE_CLI, CisCli, CisCliError, OUTPUT_LIMIT, QUERY_CACHE_TTL_MS, foregroundFailureMessage, isCacheableQuery, safeCommandDisplay };
