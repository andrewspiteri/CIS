const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const [buildArg, workspaceArg, outputArg, commandsArg] = process.argv.slice(2);
if (!outputArg) throw new Error('Usage: node measure.cjs <build directory> <authority> <new output directory> [commands.json]');
const build = path.resolve(buildArg), workspace = path.resolve(workspaceArg), output = path.resolve(outputArg);
if (fs.existsSync(output)) throw new Error('Choose a new output directory; existing measurements are preserved.');
fs.mkdirSync(output, { recursive: true });
const manifest = path.join(build, 'instrumented', 'function-profile-manifest.json');
fs.copyFileSync(manifest, path.join(output, 'manifest.json'));
const commands = commandsArg ? JSON.parse(fs.readFileSync(commandsArg)) : [
  { name: 'definition-status', args: ['definition', 'status', '--workspace', workspace, '--format', 'json'] },
  { name: 'references-validate', args: ['references', 'validate', '--repo', workspace, '--format', 'json'] },
  { name: 'graph-status', args: ['graph', 'status', '--workspace', workspace, '--format', 'json'] },
  { name: 'repo-doctor', args: ['repo', 'doctor', '--repo', workspace, '--format', 'json'] }
];
// Hash canonical documentation independently of CIS to detect unintended changes.
function snapshot(directory, root = directory, result = {}) {
  if (!fs.existsSync(directory)) return result;
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) continue;
    if (entry.isDirectory()) snapshot(file, root, result);
    else if (entry.isFile()) result[path.relative(root, file)] = crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  }
  return result;
}
const before = snapshot(path.join(workspace, 'docs', 'cis'));
fs.writeFileSync(path.join(output, 'documents-before.json'), JSON.stringify(before, null, 2));
const runs = [];
// Each command gets baseline/profile/profile/baseline. No caches are deleted.
// First and repeat runs are retained separately; these are not true cold-disk trials.
for (const command of commands) {
  if (!/^[a-z0-9-]+$/.test(command.name) || !Array.isArray(command.args)) throw new Error('Invalid command entry.');
  const counts = { baseline: 0, instrumented: 0 };
  for (const mode of ['baseline', 'instrumented', 'instrumented', 'baseline']) {
    const name = `${command.name}-${mode}-${++counts[mode]}`;
    const profilePath = path.join(output, `${name}.profile.json`);
    const env = { ...process.env, CIS_PROFILE_OUTPUT: mode === 'instrumented' ? profilePath : '', CIS_PERF_TRACE: '' };
    const started = process.hrtime.bigint();
    const result = spawnSync(path.join(build, mode, 'cis.exe'), command.args,
      { cwd: workspace, env, encoding: 'utf8', timeout: 180000, maxBuffer: 32 * 1024 * 1024, windowsHide: true });
    const wallMs = Number(process.hrtime.bigint() - started) / 1e6;
    fs.writeFileSync(path.join(output, `${name}.stdout.json`), result.stdout || '');
    fs.writeFileSync(path.join(output, `${name}.stderr.txt`), result.stderr || '');
    let jsonValid = false;
    try { JSON.parse(result.stdout); jsonValid = true; } catch {}
    const run = { name, command: command.name, args: command.args, mode, iteration: counts[mode], wallMs,
      exitCode: result.status, signal: result.signal, error: result.error?.message, jsonValid,
      profile: fs.existsSync(profilePath) ? path.basename(profilePath) : null };
    runs.push(run);
    fs.writeFileSync(path.join(output, 'runs.json'), JSON.stringify(runs, null, 2));
    console.log(`${name}: ${(wallMs / 1000).toFixed(3)}s; exit=${result.status}; JSON=${jsonValid}; profile=${!!run.profile}`);
    if (result.error || !jsonValid || (mode === 'instrumented' && !run.profile))
      throw new Error(`Measurement failed: ${name}. Inspect saved stdout/stderr before retrying.`);
  }
}
const after = snapshot(path.join(workspace, 'docs', 'cis'));
const changed = [...new Set([...Object.keys(before), ...Object.keys(after)])].filter(key => before[key] !== after[key]);
fs.writeFileSync(path.join(output, 'document-preservation.json'), JSON.stringify({ filesChecked: Object.keys(before).length, changed }, null, 2));
console.log(`Canonical document preservation: ${Object.keys(before).length} files checked; ${changed.length} changed.`);
if (changed.length) process.exitCode = 1;
