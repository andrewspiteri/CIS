'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const childProcess = require('node:child_process');
const { CisCli } = require('../lib/cis-cli');
const { confirmRepositoryImport } = require('../lib/repository-import');
const { failureMessage } = require('../lib/content-messages');

function client(processes, output = { appendLine() {} }) {
  return new CisCli({ workspace: { isTrusted: true,
    getConfiguration: () => ({ get: (key, fallback) => key === 'executablePath' ? process.execPath : fallback }) } },
  output, { root: () => os.tmpdir(), needsSelection: () => false }, processes);
}

test('large import plans reach model selection through the actual process transport', async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cis-large-import-'));
  const script = path.join(directory, 'plan.cjs');
  fs.writeFileSync(script, `
    const content = '# Guidance — café\\n' + 'Keep domain rules.\\n'.repeat(2200);
    process.stderr.write('[guidance-review] Preparing editable guidance.\\n');
    process.stdout.write(JSON.stringify({status:'dry-run', applied:false, mergeReviewHash:'sha256:'+'a'.repeat(64),
      fileMerges:Array.from({length:137}, (_,i)=>({repositoryPath:'/repo',
        relativePath:'.github/instructions/file-'+i+'.instructions.md', currentContent:content, proposedContent:content,
        guidanceReview:{status:'unavailable'}})), guidanceProviders:[{name:'codex',status:'available',isLocal:false,models:[{name:'chosen-model'}]}]}));
  `);
  const outputs = [];
  const cli = client({ execFile: (executable, args, options, callback) =>
    childProcess.execFile(executable, [script, ...args], options, (error, stdout, stderr) => {
      if (!error) outputs.push(Buffer.byteLength(stdout, 'utf8'));
      callback(error, stdout, stderr);
    }) });
  let offeredModel = false;
  try {
    const result = await confirmRepositoryImport({ window: { showQuickPick: async choices => {
      offeredModel = choices.some(choice => choice.label === 'codex / chosen-model');
      return undefined; // Stop at the user's model choice; no review or writes.
    } } }, cli, ['repo', 'import', '--source', '/repo']);
    assert.equal(result, undefined);
    assert.equal(offeredModel, true);
    assert.equal(outputs.length, 1);
    assert.ok(outputs[0] > 9 * 1024 * 1024);
    const reviewed = await cli.query(['repo', 'import', '--dry-run', '--merge-model', 'chosen-model'], { repository: false });
    assert.equal(reviewed.fileMerges.length, 137);
    assert.match(reviewed.fileMerges[136].proposedContent, /— café/u);
    assert.equal(reviewed._process.exitCode, 0);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('the larger allowance is bounded and limited to import and workspace snapshots', async () => {
  const limits = [];
  const cli = client({ execFile: (_executable, _args, options, callback) => {
    limits.push(options.maxBuffer);
    callback(null, '{"status":"ok"}', '');
  } });
  for (const args of [['repo', 'import', '--dry-run'], ['repo', 'import', '--merge-model', 'chosen-model'],
    ['workspace', 'snapshot'], ['repo', 'doctor'], ['agent', 'providers']])
    await cli.query(args, { repository: false });
  assert.deepEqual(limits, [64, 64, 64, 4, 4].map(mib => mib * 1024 * 1024));
});

test('stdout and stderr buffer exhaustion report their cause despite progress and valid-looking partial JSON', async () => {
  for (const stream of ['stdout', 'stderr']) {
    const cli = client({ execFile: (_executable, _args, _options, callback) => callback(
      Object.assign(new Error(stream + ' maxBuffer length exceeded'), { code: 'ERR_CHILD_PROCESS_STDIO_MAXBUFFER', killed: true }),
      '{"status":"dry-run","applied":false}', '[guidance-review] Waiting for model selection.\n') });
    await assert.rejects(cli.query(['repo', 'import', '--dry-run'], { repository: false, acceptStructuredFailure: true }), error => {
      assert.equal(error.kind, 'output-limit');
      assert.match(error.message, /64 MiB/u);
      assert.match(error.message, /result is incomplete/u);
      assert.equal(error.details, stream + ' maxBuffer length exceeded');
      assert.equal(error.data, undefined);
      assert.doesNotMatch(error.message, /guidance-review|timeout|malformed JSON/u);
      assert.match(failureMessage(error), /size limit.*incomplete.*64 MiB/u);
      assert.doesNotMatch(failureMessage(error), /could not explain/u);
      return true;
    });
  }
});
