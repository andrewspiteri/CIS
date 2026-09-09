'use strict';

const assert = require('node:assert/strict');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const { EventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');
const { CisCli } = require('../lib/cis-cli');
const { openDefinitionWizardPanel, renderDefinitionWizardHtml } = require('../lib/webview');

const tick = () => new Promise(resolve => setImmediate(resolve));
const root = path.resolve('authority');
const model = { pages: [{ id: 'business', ordinal: 2, title: 'Business definition' }], currentPage: 'business' };
function api() {
  return { workspace: { isTrusted: true, getConfiguration: () => ({ get: () => 'cis' }) },
    ProgressLocation: { Notification: 1 }, window: { withProgress: (_options, run) => run({ report() {} }, { onCancellationRequested() {} }) } };
}

test('repository queries serialize writes and reads, share duplicate reads, and recover after failure', async () => {
  const started = [];
  const cli = new CisCli(api(), { appendLine() {} }, { root: () => root }, {
    execFile: (_executable, args, options, callback) => started.push({ args, options, callback }),
  });
  const prepare = cli.query(['definition', 'prepare', '--page', 'business']);
  const failed = prepare.catch(error => error);
  const status = cli.query(['definition', 'status']);
  const duplicate = cli.query(['definition', 'status']);
  const guidance = cli.query(['brd', 'questions', 'guidance']);
  const version = cli.version();
  await tick();
  assert.equal(started.length, 1);
  assert.equal(started[0].args[1], 'prepare');
  started[0].callback(Object.assign(new Error('locked'), { code: 5 }), '{"errors":["locked"]}', '');
  assert.equal((await failed).kind, 'command-failed');
  await tick();
  assert.equal(started.length, 2);
  assert.equal(started[1].args[1], 'status');
  started[1].callback(null, '{"status":"ready"}', '');
  assert.equal((await status).status, 'ready');
  assert.equal((await duplicate).status, 'ready');
  await tick();
  assert.equal(started.length, 3);
  started[2].callback(null, '{"questions":[]}', '');
  await guidance;
  await tick();
  assert.equal(started.length, 4);
  assert.deepEqual(started[3].args, ['--version']);
  started[3].callback(null, '0.3.0', '');
  await version;
  await tick();
  assert.equal(cli.commandQueues.size, 0);
});

test('interactive requests precede queued background refreshes without overlapping a running command', async () => {
  const started = [];
  const cli = new CisCli(api(), { appendLine() {} }, { root: () => root }, {
    execFile: (_exe, args, _options, callback) => started.push({ args, callback }),
  });
  const active = cli.query(['brd', 'status']);
  await tick();
  const background = cli.query(['technical-intent', 'status']);
  const action = cli.query(['definition', 'status'], { interactive: true });
  assert.equal(started.length, 1);
  started[0].callback(null, '{"status":"ready"}', ''); await active; await tick();
  assert.equal(started.length, 2);
  assert.equal(started[1].args[0], 'definition');
  started[1].callback(null, '{"pages":[]}', ''); await action; await tick();
  assert.equal(started[2].args[0], 'technical-intent');
  started[2].callback(null, '{"status":"ready"}', ''); await background; await tick();
  assert.equal(cli.commandQueues.size, 0);
});

test('foreground commands share the authority queue while another authority can proceed independently', async () => {
  const started = [];
  const local = api();
  let cancel;
  local.window.withProgress = (_options, run) => run({ report() {} }, { onCancellationRequested: callback => { cancel = callback; } });
  const cli = new CisCli(local, { append() {}, appendLine() {} }, { root: () => root }, {
    spawn: (_executable, args) => {
      const child = new EventEmitter(); child.stdout = new PassThrough(); child.stderr = new PassThrough();
      child.kill = () => child.emit('close', 1);
      started.push({ args, child }); return child;
    },
    execFile: (_executable, args, _options, callback) => started.push({ args, callback }),
  });
  const foreground = cli.runForeground('Prepare', ['definition', 'prepare', '--workspace', root], { repository: false, cancellable: true });
  const cancelled = foreground.catch(error => error);
  const same = cli.query(['definition', 'status', '--workspace', root], { repository: false });
  const otherRoot = path.resolve('another-authority');
  const other = cli.query(['definition', 'status', '--workspace', otherRoot], { repository: false });
  await tick();
  assert.equal(started.length, 2);
  assert.ok(started[0].child);
  assert.ok(started[1].args.includes(otherRoot));
  started[1].callback(null, '{"status":"ready"}', '');
  await other;
  cancel();
  assert.equal((await cancelled).kind, 'cancelled');
  await tick();
  assert.equal(started.length, 3);
  assert.ok(started[2].args.includes(root));
  started[2].callback(null, '{"status":"ready"}', '');
  await same;
});

test('queued commands recheck workspace trust before starting a process', async () => {
  const local = api();
  const callbacks = [];
  const cli = new CisCli(local, { appendLine() {} }, { root: () => root }, {
    execFile: (_executable, _args, _options, callback) => callbacks.push(callback),
  });
  const first = cli.query(['definition', 'status']);
  await tick(); // This request is running before the interactive mutation is queued.
  const second = cli.query(['definition', 'prepare', '--page', 'business']).catch(error => error);
  await tick();
  local.workspace.isTrusted = false;
  callbacks[0](null, '{"status":"ready"}', '');
  await first;
  assert.equal((await second).kind, 'untrusted-workspace');
  assert.equal(callbacks.length, 1);
});

test('wizard host drops repeated preparation and refresh messages until completion and unlocks after errors', async () => {
  const messages = []; const errors = []; const actions = [];
  let receive; let finish;
  const gate = new Promise((resolve, reject) => { finish = reject; });
  const local = { Uri: { file: value => value }, ViewColumn: { Active: 1 }, window: {
    showErrorMessage: async message => errors.push(message),
    createWebviewPanel: () => ({ webview: { cspSource: 'test:', postMessage: async message => messages.push(message),
      onDidReceiveMessage: callback => { receive = callback; } } }),
  } };
  const controller = openDefinitionWizardPanel(local, root, model, async action => { actions.push(action); if (actions.length === 1) await gate; }, async () => {});
  const prepare = receive({ command: 'prepare', value: 'business' });
  assert.equal(controller.isBusy(), true);
  await receive({ command: 'prepare', value: 'business' });
  await receive({ command: 'refresh' });
  await receive({ command: 'navigate', value: 'technical' });
  assert.deepEqual(actions, ['prepare']);
  assert.equal(controller.page(), 'business');
  controller.update(model);
  assert.match(controller.panel.webview.html, /setBusy\(true\)/u);
  finish(new Error('Fixture failure'));
  await prepare;
  assert.equal(controller.isBusy(), false);
  assert.match(errors[0], /Fixture failure/u);
  assert.deepEqual(messages.map(message => message.busy), [true, false]);
  await receive({ command: 'refresh' });
  assert.deepEqual(actions, ['prepare', 'refresh']);
  await receive({ command: 'open-business-dictionary', value: '0:0' });
  assert.deepEqual(actions, ['prepare', 'refresh', 'open-business-dictionary']);
});

test('wizard script disables controls before posting a click and restores their prior disabled state', () => {
  const html = renderDefinitionWizardHtml({ cspSource: 'test:' }, root, model, 'business', 'nonce', {});
  const script = /<script nonce="nonce">([\s\S]*?)<\/script>/u.exec(html)[1];
  const prepare = { disabled: false, dataset: { command: 'prepare', value: 'business' } };
  const unavailable = { disabled: true, dataset: {} };
  const controls = [prepare, unavailable];
  const notice = { hidden: true }; const sent = [];
  let click; let message;
  vm.runInNewContext(script, {
    acquireVsCodeApi: () => ({ postMessage: value => sent.push(value) }),
    document: { body: { setAttribute() {} }, getElementById: () => notice, querySelectorAll: () => controls,
      addEventListener: (_event, callback) => { click = callback; } },
    window: { addEventListener: (_event, callback) => { message = callback; } },
  });
  const event = { target: { closest: () => prepare } };
  click(event); click(event);
  assert.equal(sent.length, 1);
  assert.equal(prepare.disabled, true);
  assert.equal(notice.hidden, false);
  message({ data: { command: 'wizard-busy', busy: false } });
  assert.equal(prepare.disabled, false);
  assert.equal(unavailable.disabled, true);
  assert.equal(notice.hidden, true);
  click(event);
  assert.equal(sent.length, 2);
});
