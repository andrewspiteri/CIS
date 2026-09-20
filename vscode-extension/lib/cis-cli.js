'use strict';

const childProcess = require('node:child_process');
const path = require('node:path');
const { bound, redact, validateArgument, validateExecutable } = require('./security');
const { openCommandProgressPanel } = require('./webview');

const OUTPUT_LIMIT = 4 * 1024 * 1024;
const SNAPSHOT_OUTPUT_LIMIT = 16 * OUTPUT_LIMIT;
// Read projections remain valid for the current repository generation. The extension
// clears this cache when a watched repository input changes or a CIS mutation runs.
const QUERY_CACHE_TTL_MS = Number.POSITIVE_INFINITY;
const COMPATIBLE_CLI = Object.freeze({ major: 0, minimumMinor: 3 });
// Only these exact projections are bundled by workspace snapshot schema 1.
// Specific runs, filtered lists and custom options must go directly to their command.
const SNAPSHOT_QUERIES = new Set([
  [['brd', 'status'], '--workspace'],
  [['technical-intent', 'questions', 'status', '--summary'], '--workspace'],
  [['technical-intent', 'status'], '--workspace'],
  [['change', 'list'], '--repo'],
  [['agent', 'providers'], '--repo'],
  [['agent', 'runs', '--summary', '--limit', '10'], '--repo'],
  [['repo', 'doctor'], '--repo'],
  [['skills', 'inventory', '--summary'], '--repo'],
  [['brd', 'questions', 'list'], '--workspace'],
  [['standards', 'inventory', '--summary'], '--repo'],
  [['references', 'validate'], '--repo'],
  [['agent', 'runs', '--change', 'PRODUCT', '--summary', '--latest-per-task', '--limit', '10'], '--repo'],
  [['definition', 'status'], '--workspace'],
  [['brd', 'questions', 'guidance'], '--workspace'],
  [['brd', 'feature', 'wizard', 'navigation'], '--workspace'],
].map(query => JSON.stringify(query)));

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
    this.commandQueues = new Map();
    this.queryGeneration = 0;
    this.startupSnapshots = false;
    this.workspaceSnapshot = undefined;
    this.snapshotUnsupported = new Set();
  }

  enableStartupSnapshots() { this.startupSnapshots = true; }

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
    // Questionnaire answers are text data passed as a single argv value, never through a shell.
    const validated = args.map((value, index) => validateArgument(value, {
      allowLineBreaks: index > 0 && args[index - 1] === '--answer',
    }));
    if (options.repository !== false && !validated.includes('--repo')) validated.push('--repo', this.root());
    if (options.format !== false && !validated.includes('--format')) validated.push('--format', options.format || 'json');
    return validated;
  }

  scheduleCommand(args, root, operation, interactive = false) {
    // Even status projections can update shared derived evidence. Never overlap
    // repository commands from this extension for the same authority.
    const option = name => {
      const index = args.indexOf(name);
      return index >= 0 ? args[index + 1] : args.find(arg => arg.startsWith(`${name}=`))?.slice(name.length + 1);
    };
    const scope = path.resolve(root || '.', option('--workspace') || option('--repo') || root || '.');
    const key = process.platform === 'win32' ? scope.toLowerCase() : scope;
    let queue = this.commandQueues.get(key);
    if (!queue) { queue = { pending: [], running: false }; this.commandQueues.set(key, queue); }
    return new Promise((resolve, reject) => {
      queue.pending.push({ operation, interactive, resolve, reject });
      if (!queue.running) {
        queue.running = true;
        queueMicrotask(() => this.drainQueue(key, queue));
      }
    });
  }

  drainQueue(key, queue) {
    if (!queue.pending.length) { this.commandQueues.delete(key); return; }
    // User actions precede pending background refreshes; an active process is never interrupted.
    const preferred = queue.pending.findIndex(job => job.interactive);
    const [job] = queue.pending.splice(preferred < 0 ? 0 : preferred, 1);
    void Promise.resolve().then(() => { this.ensureTrusted(); return job.operation(); })
      .then(job.resolve, job.reject).then(() => this.drainQueue(key, queue));
  }

  query(args, options = {}) {
    this.ensureTrusted();
    const root = options.repository === false ? this.authority.root() : this.root();
    const executable = this.executable();
    const commandArgs = this.arguments(args, options);
    const timeout = options.timeout || 30_000;
    const outputLimit = args[0] === 'workspace' && args[1] === 'snapshot' ? SNAPSHOT_OUTPUT_LIMIT : OUTPUT_LIMIT;
    const readOnly = isCacheableQuery(args);
    const cacheable = options.cache !== false && readOnly;
    const cacheKey = cacheable ? JSON.stringify([executable, root, commandArgs, timeout, options.acceptStructuredFailure === true]) : undefined;
    const cached = cacheKey ? this.queryCache.get(cacheKey) : undefined;
    if (cached && (cached.expiresAt === Infinity || cached.expiresAt > Date.now())) return cached.promise;
    if (!readOnly) this.clearQueryCache();
    const queuedAt = performance.now();
    const execute = () => this.scheduleCommand(commandArgs, root, () => new Promise((resolve, reject) => {
      const startedAt = performance.now();
      this.output.appendLine(`$ ${safeCommandDisplay(executable, commandArgs)}`);
      this.processes.execFile(executable, commandArgs,
        { cwd: root, windowsHide: true, timeout, maxBuffer: outputLimit, shell: false },
        (error, stdout, stderr) => {
          const safeError = bound(stderr, 16_384).trim();
          if (safeError) this.output.appendLine(safeError);
          const exitCode = typeof error?.code === 'number' ? error.code : error ? 1 : 0;
          this.output.appendLine(`[timing] run=${Math.round(performance.now() - startedAt)}ms; queue=${Math.round(startedAt - queuedAt)}ms; exit=${exitCode}`);
          // System.CommandLine may print help (non-JSON stdout) for an unknown
          // optional command, using exit 1 or 2 depending on the CLI version.
          if (error && args[0] === 'workspace' && args[1] === 'snapshot' && [1, 2].includes(exitCode)
              && /Unrecognized command or argument 'snapshot'/u.test(safeError))
            return reject(new CisCliError(safeError, 'unsupported-snapshot', exitCode, safeError));
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
    }), options.interactive === true || !readOnly);
    const snapshotKey = this.startupSnapshots && cacheable && options.interactive !== true
      && timeout === 30_000 ? snapshotQueryKey(commandArgs, root) : undefined;
    const operation = snapshotKey
      ? this.readCurrentWorkspaceSnapshot(executable, root).then(snapshot => {
        const projection = snapshot?.get(snapshotKey);
        if (!projection) return execute();
        if (!projection.data || typeof projection.data !== 'object' || Array.isArray(projection.data))
          throw new CisCliError('CIS returned no structured result for this snapshot query.', 'invalid-evidence', projection.exitCode, bound(projection.standardError));
        if (projection.exitCode !== 0 && options.acceptStructuredFailure !== true)
          throw new CisCliError(messageFrom(projection.data, projection.standardError), 'command-failed',
            projection.exitCode, bound(projection.standardError), projection.data);
        return { ...projection.data, _process: { exitCode: projection.exitCode,
          ...(projection.exitCode !== 0 ? { failed: true } : {}) } };
      }) : execute();
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

  async readCurrentWorkspaceSnapshot(executable, root) {
    for (let attempt = 0; attempt < 2; attempt++) {
      const generation = this.queryGeneration;
      const snapshot = await this.readWorkspaceSnapshot(executable, root);
      if (normalizedPath(this.authority.root()) !== normalizedPath(root) || this.executable() !== executable)
        throw new CisCliError('The selected CIS authority or executable changed. Refresh the selected workspace.', 'stale-evidence');
      if (generation === this.queryGeneration) return snapshot;
      // Reject the obsolete data, but allow one fresh read after trailing file events.
      // Concurrent callers share the replacement snapshot through readWorkspaceSnapshot.
      if (attempt === 0 && !this.workspaceSnapshot)
        this.output.appendLine('[snapshot] Inputs changed during loading; retrying once with current evidence.');
    }
    throw new CisCliError('CIS inputs kept changing during the workspace refresh. Refresh again when changes settle.', 'stale-evidence');
  }

  readWorkspaceSnapshot(executable, root) {
    const key = JSON.stringify([executable, normalizedPath(root)]);
    if (this.snapshotUnsupported.has(key)) return Promise.resolve(undefined);
    if (this.workspaceSnapshot?.key === key) return this.workspaceSnapshot.promise;
    const entry = { key, promise: undefined };
    entry.promise = this.query(['workspace', 'snapshot', '--repo', root],
      { repository: false, cache: false, timeout: 120_000 }).then(result => {
      if (result.schemaVersion !== 1 || normalizedPath(result.repositoryPath) !== normalizedPath(root)
          || !Array.isArray(result.entries))
        throw new CisCliError('CIS returned an unsupported workspace snapshot.', 'invalid-evidence');
      const projections = new Map();
      for (const projection of result.entries) {
        if (!Array.isArray(projection.arguments) || !projection.arguments.every(arg => typeof arg === 'string')
            || !['--repo', '--workspace'].includes(projection.scope) || !Number.isInteger(projection.exitCode))
          throw new CisCliError('CIS returned a malformed snapshot query.', 'invalid-evidence');
        const projectionKey = JSON.stringify([projection.arguments, projection.scope]);
        if (projections.has(projectionKey)) throw new CisCliError('CIS returned duplicate snapshot queries.', 'invalid-evidence');
        projections.set(projectionKey, projection);
        this.output.appendLine(`[snapshot] ${safeCommandDisplay('', projection.arguments).trim()}; run=${Math.round(projection.durationMs || 0)}ms; exit=${projection.exitCode}`);
        if (projection.standardError) this.output.appendLine(bound(projection.standardError, 16_384));
      }
      return projections;
    }).catch(error => {
      if (this.workspaceSnapshot === entry) this.workspaceSnapshot = undefined;
      // Older compatible CLIs do not expose this optional endpoint. Other failures
      // remain visible rather than silently replacing a failed check with a second one.
      if (error.kind === 'unsupported-snapshot') {
        this.snapshotUnsupported.add(key);
        this.output.appendLine('CIS CLI does not support workspace snapshots; using individual queries.');
        return undefined;
      }
      throw error;
    });
    this.workspaceSnapshot = entry;
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
    const operation = this.scheduleCommand([], root, () => new Promise((resolve, reject) => {
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
    }));
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
    this.queryGeneration++;
    this.workspaceSnapshot = undefined;
    this.queryCache.clear();
    this.versionCache = undefined;
  }

  async runForeground(title, args, options = {}) {
    this.ensureTrusted();
    const root = this.root();
    const executable = this.executable();
    this.clearQueryCache();
    const commandArgs = this.arguments(args, { format: options.format || 'agent', repository: options.repository });
    return this.scheduleCommand(commandArgs, root, () => this.vscode.window.withProgress({
      location: this.vscode.ProgressLocation.Notification,
      title,
      cancellable: options.cancellable === true,
    }, (progress, cancellation) => new Promise((resolve, reject) => {
      this.output.appendLine(`$ ${safeCommandDisplay(executable, commandArgs)}`);
      const started = Date.now();
      const process = this.processes.spawn(executable, commandArgs, { cwd: root, windowsHide: true, shell: false,
        stdio: ['ignore', 'pipe', 'pipe'] });
      let stdout = ''; let stderr = ''; let truncated = false; let cancelled = false; let finished = false; let detail;
      const command = safeCommandDisplay(executable, commandArgs);
      const cancelRun = () => { if (!finished) { cancelled = true; process.kill(); } };
      const detailModel = (state, extra = {}) => ({ state, command, cancellable: options.cancellable === true,
        durationMs: Date.now() - started, stdout: bound(stdout, 32_768), stderr: bound(stderr, 32_768), truncated, ...extra });
      const ensureDetail = () => {
        if (options.details !== false && !detail && typeof this.vscode.window.createWebviewPanel === 'function')
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
    })), true);
  }
}

function isCacheableQuery(args) {
  const command = args.join(' ');
  return [
    /^workspace snapshot(?: |$)/u,
    /^repo doctor(?: |$)/u,
    /^repo list(?: |$)/u,
    /^brd feature wizard (?:navigation|list|status)(?: |$)/u,
    /^brd feature wizard screens status(?: |$)/u,
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
    /^brd (?:status|questions (?:guidance|status|list)|review (?:status|freshness)|backlog status|feature status)(?: |$)/u,
    /^technical-intent (?:status|questions status)(?: |$)/u,
    /^solution-design status(?: |$)/u,
    /^ui-direction (?:baseline|status|questions status)(?: |$)/u,
    /^definition status(?: |$)/u,
  ].some(pattern => pattern.test(command));
}

function normalizedPath(value) {
  if (typeof value !== 'string' || !value) return undefined;
  const resolved = path.resolve(value);
  return process.platform === 'win32' ? resolved.toLowerCase() : resolved;
}

function snapshotQueryKey(args, root) {
  if (!root || !['brd', 'technical-intent', 'repo', 'change', 'agent', 'skills', 'standards', 'references', 'definition'].includes(args[0]))
    return undefined;
  const query = []; let scope; let format;
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    if (arg === '--repo' || arg === '--workspace') {
      if (scope || normalizedPath(args[++index]) !== normalizedPath(root)) return undefined;
      scope = arg;
    } else if (arg === '--format') {
      if (format) return undefined;
      format = args[++index];
    } else query.push(arg);
  }
  const key = scope && format === 'json' ? JSON.stringify([query, scope]) : undefined;
  return SNAPSHOT_QUERIES.has(key) ? key : undefined;
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
