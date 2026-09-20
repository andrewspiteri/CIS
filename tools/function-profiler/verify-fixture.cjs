const fs = require('node:fs');
const assert = require('node:assert/strict');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const dir = path.resolve(process.argv[2]);
const output = path.join(dir, 'fixture-profile.json');
const exe = path.join(dir, 'Cis.FunctionProfiler.Fixture.exe');
let result = spawnSync(exe, { encoding: 'utf8', env: { ...process.env, CIS_PROFILE_OUTPUT: '' }, timeout: 30000 });
assert.equal(result.status, 0, result.stderr);
assert.equal(result.stdout.trim(), 'fixture passed');
assert.equal(fs.existsSync(output), false, 'disabled profiling creates no report');
result = spawnSync(exe, { encoding: 'utf8', env: { ...process.env, CIS_PROFILE_OUTPUT: output }, timeout: 30000 });
assert.equal(result.status, 0, result.stderr);
assert.equal(result.stdout.trim(), 'fixture passed');
assert.equal(result.stderr, '');
const manifest = JSON.parse(fs.readFileSync(path.join(dir, 'function-profile-manifest.json')));
const report = JSON.parse(fs.readFileSync(output));
const row = name => {
  const definition = manifest.methods.find(m => m.name === name);
  assert.ok(definition, name);
  const values = report.methods.filter(m => m.id === definition.id);
  return values.reduce((a, b) => ({ calls: a.calls + b.calls, completed: a.completed + b.completed,
    inclusiveMs: a.inclusiveMs + b.inclusiveMs, selfMs: a.selfMs + b.selfMs }),
    { calls: 0, completed: 0, inclusiveMs: 0, selfMs: 0 });
};
for (const [name, count] of Object.entries({ Recursive: 5, Parent: 1, Leaf: 3, Throws: 3,
    ParallelLeaf: 400, Iterator: 1, Asynchronous: 1, AsyncThrows: 1, ByReference: 1, Generic: 1 })) {
  assert.equal(row(name).calls, count, name);
  assert.equal(row(name).completed, count, `${name} completion`);
}
assert.ok(row('Parent').inclusiveMs >= row('Leaf').inclusiveMs);
assert.ok(row('Parent').selfMs < row('Parent').inclusiveMs);
assert.ok(row('Leaf').inclusiveMs >= 6);
for (const value of report.methods) {
  assert.equal(value.calls, value.completed, `unbalanced method ${value.id}`);
  assert.ok(value.selfMs >= 0 && value.selfMs <= value.inclusiveMs);
}
assert.equal(report.edges.reduce((sum, r) => sum + r.calls, 0), report.methods.reduce((sum, r) => sum + r.calls, 0));
console.log(`Verified exact counts, nested/self timing, recursion, exceptions/filter/finally, async, iterators, constructors, properties, generics, byref, spans, lambdas and four threads; ${manifest.instrumentedMethods} method bodies instrumented.`);
