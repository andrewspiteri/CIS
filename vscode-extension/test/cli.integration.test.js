'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const { CisCli } = require('../lib/cis-cli');

const repository = path.resolve(__dirname, '..', '..');
const executableName = process.platform === 'win32' ? 'cis.exe' : 'cis';
const executable = [
  path.join(repository, 'src', 'Cis.Host', 'bin', 'Release', 'net10.0', executableName),
  path.join(repository, 'src', 'Cis.Host', 'bin', 'Debug', 'net10.0', executableName),
].find(candidate => fs.existsSync(candidate));

// Trace: TC-VSC-001-001, TC-VSC-018-001, TC-VSC-020-001.
test('packaged CLI contract supports version and agent-provider discovery', { skip: executable ? false : 'Build Cis.Host before the integration test.' }, async () => {
  const vscode = {
    workspace: {
      isTrusted: true,
      getConfiguration: () => ({ get: (key, fallback) => key === 'executablePath' ? executable : fallback }),
    },
  };
  const authority = { root: () => repository, needsSelection: () => false };
  const output = { appendLine() {} };
  const cli = new CisCli(vscode, output, authority);

  const version = await cli.version();
  const providers = await cli.query(['agent', 'providers']);

  assert.equal(version.compatible, true);
  assert.equal(providers.exitCode, 0);
  assert.ok(providers.providers.some(provider => provider.id === 'codex'));
  assert.ok(providers.providers.some(provider => provider.id === 'claude'));
  assert.ok(providers.providers.every(provider => Array.isArray(provider.modes) && Array.isArray(provider.permissions)));
});
