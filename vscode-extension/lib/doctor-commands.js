'use strict';

const crypto = require('node:crypto');
const path = require('node:path');

// Bind actions to the exact CLI suggestion, including across Doctor refreshes.
function doctorFixId(finding) {
  return crypto.createHash('sha256').update(JSON.stringify([finding.code, finding.fixCommand])).digest('hex');
}

function parseDoctorCommand(command) {
  if (typeof command !== 'string' || !command.trim() || command.length > 4096
      || /[\u0000-\u001f\u007f]/u.test(command))
    throw new Error('This finding does not contain a runnable command.');
  const args = [];
  let quote; let token = ''; let started = false;
  for (const character of command.trim()) {
    if (quote) {
      if (character === quote) quote = undefined;
      else token += character;
    } else if (character === '"' || character === "'") {
      quote = character; started = true;
    } else if (/\s/u.test(character)) {
      if (started) { args.push(token); token = ''; started = false; }
    } else {
      if (/[|;&`$<>]/u.test(character))
        throw new Error('Copy and complete commands containing placeholders or shell syntax before running them.');
      token += character; started = true;
    }
  }
  if (quote) throw new Error('Copy and correct the unmatched command quotes before running.');
  if (started) args.push(token);
  if (args.shift() !== 'cis' || !args.length)
    throw new Error('Run command supports CIS commands; copy this command to run it separately.');
  if (args.some(arg => /<[^>]+>/u.test(arg)))
    throw new Error('Copy and fill in command placeholders before running.');
  return args;
}

function assertDoctorScope(args, root) {
  for (let index = 0; index < args.length; index++) {
    const match = /^(--repo|--workspace)(?:=(.*))?$/u.exec(args[index]);
    if (!match) continue;
    const value = match[2] ?? args[++index];
    if (!value || path.relative(path.resolve(root), path.resolve(root, value)) !== '')
      throw new Error('This command targets another repository. Open Doctor for that authority before running it.');
  }
}

module.exports = { doctorFixId, parseDoctorCommand, assertDoctorScope };
