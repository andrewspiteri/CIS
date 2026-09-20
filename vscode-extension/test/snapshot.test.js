'use strict';

const assert = require('node:assert/strict');
const path = require('node:path');
const test = require('node:test');
const { CisCli } = require('../lib/cis-cli');
const root = path.resolve('snapshot-authority');
const tick = () => new Promise(resolve => setImmediate(resolve));
const brd = ['brd', 'status', '--workspace', root];
const doctor = ['repo', 'doctor'];
const snapshot = value => ({ schemaVersion: 1, repositoryPath: root, entries: [
  { arguments: ['brd', 'status'], scope: '--workspace', exitCode: 0, data: { value }, durationMs: 1 },
  { arguments: ['repo', 'doctor'], scope: '--repo', exitCode: 5, data: { status: 'blocked' }, standardError: 'Existing finding', durationMs: 1 },
] });
function fixture(run) {
  const started = []; const messages = [];
  const vscode = { workspace: { isTrusted: true, getConfiguration: () => ({ get: () => 'cis' }) } };
  const authority = { root: () => root };
  const cli = new CisCli(vscode, { appendLine: line => messages.push(line) }, authority, {
    execFile: (_exe, args, options, callback) => { started.push({ args, options, callback }); run?.(args, callback, started.length); },
  });
  cli.enableStartupSnapshots();
  return { cli, started, messages, vscode, authority };
}

test('startup projections share one process and preserve individual failure semantics', async () => {
  const { cli, started, messages } = fixture((_args, callback) => setImmediate(() => callback(null, JSON.stringify(snapshot(1)), '')));
  const [business, blocked, rejected] = await Promise.all([
    cli.query(brd, { repository: false }), cli.query(doctor, { acceptStructuredFailure: true }),
    cli.query(doctor).catch(error => error),
  ]);
  assert.equal(started.length, 1);
  assert.deepEqual(started[0].args, ['workspace', 'snapshot', '--repo', root, '--format', 'json']);
  assert.equal(started[0].options.shell, false);
  assert.equal(business.value, 1);
  assert.deepEqual(blocked._process, { exitCode: 5, failed: true });
  assert.equal(rejected.kind, 'command-failed'); assert.equal(rejected.exitCode, 5);
  assert.equal(messages.filter(line => line.startsWith('[snapshot]')).length, 2);
  await cli.query(brd, { repository: false });
  assert.equal(started.length, 1);
});

test('uncached reads bypass snapshots; mutations and refreshes require a new checked snapshot', async () => {
  let value = 0;
  const { cli, started } = fixture((args, callback) => setImmediate(() => callback(null,
    JSON.stringify(args[0] === 'workspace' ? snapshot(++value) : { individual: true }), '')));
  assert.equal((await cli.query(brd, { repository: false })).value, 1);
  assert.equal((await cli.query(brd, { repository: false, cache: false })).individual, true);
  assert.equal((await cli.query(brd, { repository: false })).value, 1);
  await cli.query(['definition', 'prepare', '--page', 'business']);
  assert.equal((await cli.query(brd, { repository: false })).value, 2);
  cli.clearQueryCache();
  assert.equal((await cli.query(brd, { repository: false })).value, 3);
  assert.equal(started.length, 5);
});

test('an input change discards obsolete data and shares one fresh retry with new callers', async () => {
  const { cli, started } = fixture();
  const obsolete = cli.query(brd, { repository: false }).catch(error => error);
  await tick();
  cli.clearQueryCache();
  const current = cli.query(brd, { repository: false });
  started[0].callback(null, JSON.stringify(snapshot(1)), '');
  await tick();
  assert.equal(started.length, 2);
  started[1].callback(null, JSON.stringify(snapshot(2)), '');
  assert.equal((await obsolete).value, 2);
  assert.equal((await current).value, 2);
  assert.equal((await cli.query(brd, { repository: false })).value, 2);
  assert.equal(started.length, 2);
});

test('different authorities, custom options, and interactive reads retain individual dispatch', async () => {
  const { cli, started } = fixture((args, callback) => setImmediate(() => callback(null,
    JSON.stringify(args[0] === 'workspace' ? snapshot(1) : { individual: true }), '')));
  const foreign = ['brd', 'status', '--workspace', path.resolve('other-authority')];
  assert.equal((await cli.query(foreign, { repository: false })).individual, true);
  assert.equal((await cli.query(brd, { repository: false, interactive: true })).individual, true);
  assert.equal((await cli.query(['agent', 'runs', '--limit', '2'])).individual, true);
  assert.equal(started.filter(item => item.args[0] === 'workspace').length, 0);
});

test('specific review reads never start unrelated workspace snapshots', async () => {
  const { cli, started } = fixture((_args, callback) => setImmediate(() => {
    cli.clearQueryCache(); // A trailing review-output notification arrives during the read.
    callback(null, '{"status":"Succeeded","runId":"RUN-REVIEW"}', '');
  }));
  for (const args of [
    ['agent', 'runs', '--change', 'PRODUCT', '--task', 'BRD-REVIEW', '--summary', '--limit', '10'],
    ['agent', 'show', 'RUN-REVIEW', '--summary'],
  ]) assert.equal((await cli.query(args)).status, 'Succeeded');
  assert.equal(started.length, 2);
  assert.equal(started.some(item => item.args[0] === 'workspace'), false);
});

test('repeated input changes stop after one retry without returning obsolete results', async () => {
  const { cli, started } = fixture();
  const pending = cli.query(brd, { repository: false }).catch(error => error);
  const secondReader = cli.query(doctor, { acceptStructuredFailure: true }).catch(error => error);
  await tick(); cli.clearQueryCache(); started[0].callback(null, JSON.stringify(snapshot(1)), '');
  await tick(); assert.equal(started.length, 2);
  cli.clearQueryCache(); started[1].callback(null, JSON.stringify(snapshot(2)), '');
  assert.equal((await pending).kind, 'stale-evidence');
  assert.equal((await secondReader).kind, 'stale-evidence');
  assert.equal(started.length, 2);
  const fresh = cli.query(brd, { repository: false }); await tick();
  started[2].callback(null, JSON.stringify(snapshot(3)), '');
  assert.equal((await fresh).value, 3);
});

test('changing authority during a snapshot never retries or returns the previous authority', async () => {
  const { cli, started, authority } = fixture();
  const pending = cli.query(brd, { repository: false }).catch(error => error);
  await tick(); authority.root = () => path.resolve('different-authority'); cli.clearQueryCache();
  started[0].callback(null, JSON.stringify(snapshot(1)), '');
  assert.equal((await pending).kind, 'stale-evidence'); assert.equal(started.length, 1);
});

test('older CLIs fall back once; runtime and malformed-snapshot failures stay visible', async () => {
  const legacy = fixture((args, callback) => setImmediate(() => args[0] === 'workspace'
    ? callback(Object.assign(new Error('unsupported'), { code: 1 }), 'Usage: cis workspace [command]', "Unrecognized command or argument 'snapshot'.")
    : callback(null, '{"individual":true}', '')));
  assert.equal((await legacy.cli.query(brd, { repository: false })).individual, true);
  assert.equal((await legacy.cli.query(doctor)).individual, true);
  assert.equal(legacy.started.filter(item => item.args[0] === 'workspace').length, 1);
  const broken = fixture((_args, callback) => setImmediate(() => callback(null, '{"schemaVersion":999}', '')));
  await assert.rejects(broken.cli.query(brd, { repository: false }), { kind: 'invalid-evidence' });
  assert.equal(broken.started.length, 1);
  const timedOut = fixture((_args, callback) => setImmediate(() => callback(Object.assign(new Error('timeout'), { killed: true }), '', '')));
  await assert.rejects(timedOut.cli.query(brd, { repository: false }), { kind: 'timeout' });
  assert.equal(timedOut.started.length, 1);
});

test('workspace trust is required before starting a snapshot', async () => {
  const { cli, started, vscode } = fixture();
  const pending = cli.query(brd, { repository: false }).catch(error => error);
  vscode.workspace.isTrusted = false;
  assert.equal((await pending).kind, 'untrusted-workspace');
  assert.equal(started.length, 0);
});
