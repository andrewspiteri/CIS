const fs = require('node:fs');
const path = require('node:path');
const dir = path.resolve(process.argv[2]);
const summary = JSON.parse(fs.readFileSync(path.join(dir, 'summary.json')));
console.log('Coverage:', JSON.stringify(summary.coverage));
function differences(a,b,p='$',result=[]) {
  if (a === b) return result;
  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') result.push(p);
  else for (const key of new Set([...Object.keys(a),...Object.keys(b)])) differences(a[key],b[key],`${p}.${key}`,result);
  return result;
}
const comparisons = [];
for (const command of new Set(summary.runs.map(r=>r.command))) {
  const a = summary.runs.find(r=>r.command===command && r.mode==='baseline' && r.iteration===2);
  const b = summary.runs.find(r=>r.command===command && r.mode==='instrumented' && r.iteration===2);
  const diff = differences(JSON.parse(fs.readFileSync(path.join(dir, a.name+'.stdout.json'))),
    JSON.parse(fs.readFileSync(path.join(dir, b.name+'.stdout.json'))));
  comparisons.push({command, baselineExit:a.exitCode, instrumentedExit:b.exitCode, changedJsonPaths:diff});
}
fs.writeFileSync(path.join(dir,'output-comparison.json'),JSON.stringify(comparisons,null,2));
console.log('Output comparisons:',JSON.stringify(comparisons));
for (const profile of summary.profiles.filter(p=>p.iteration===2)) {
  console.log('\n'+profile.command, 'active='+profile.activeAtSnapshot);
  console.log('Top calls:',JSON.stringify(profile.topCalls.slice(0,5).map(r=>({method:r.method,calls:r.calls,selfMs:r.selfMs}))));
  console.log('Top edges:',JSON.stringify(profile.topEdges.filter(e=>
    /ArtifactDoctorCheck|HashInputCore|ContainsReparsePoint|CisProcessSafety::Run/.test(e.callee)).map(e=>
    ({caller:e.caller,callee:e.callee,calls:e.calls,totalMs:e.inclusiveMs}))));
}
