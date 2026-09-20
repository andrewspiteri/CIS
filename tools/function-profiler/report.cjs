const fs = require('node:fs');
const path = require('node:path');
const dir = path.resolve(process.argv[2]);
const manifest = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json')));
const runs = JSON.parse(fs.readFileSync(path.join(dir, 'runs.json')));
const definitions = new Map(manifest.methods.filter(m => m.instrumented).map(m => [m.id, m]));
const csv = (rows, columns) => [columns.join(','), ...rows.map(r => columns.map(c =>
  `"${String(r[c] ?? '').replaceAll('"', '""')}"`).join(','))].join('\n');
function aggregate(values, key) {
  const result = new Map();
  for (const value of values) {
    const identity = key(value);
    const row = result.get(identity) || { id: value.id, callerId: value.callerId, calls: 0, completed: 0,
      inclusiveMs: 0, selfMs: 0, maxMs: 0, threads: new Set() };
    for (const column of ['calls', 'completed', 'inclusiveMs', 'selfMs']) row[column] += value[column];
    row.maxMs = Math.max(row.maxMs, value.maxMs);
    row.threads.add(value.threadId);
    result.set(identity, row);
  }
  return result;
}
const profiles = [];
for (const run of runs.filter(r => r.profile)) {
  const raw = JSON.parse(fs.readFileSync(path.join(dir, run.profile)));
  const aggregateMethods = aggregate(raw.methods, r => r.id);
  const rows = [...definitions.values()].map(definition => {
    const measured = aggregateMethods.get(definition.id);
    const row = { ...definition, calls: 0, completed: 0, inclusiveMs: 0, selfMs: 0, maxMs: 0, ...measured };
    row.threads = measured?.threads.size || 0;
    row.averageMs = row.completed ? row.inclusiveMs / row.completed : 0;
    row.averageSelfMs = row.completed ? row.selfMs / row.completed : 0;
    row.activeAtSnapshot = row.calls - row.completed;
    return row;
  }).sort((a, b) => b.selfMs - a.selfMs);
  const edges = [...aggregate(raw.edges, r => `${r.callerId}:${r.id}`).values()].map(r => ({ ...r,
    caller: definitions.get(r.callerId)?.method || '[uninstrumented caller / thread root]',
    callee: definitions.get(r.id)?.method, threads: r.threads.size,
    averageMs: r.completed ? r.inclusiveMs / r.completed : 0 })).sort((a,b) => b.inclusiveMs - a.inclusiveMs);
  fs.writeFileSync(path.join(dir, `${run.name}.methods.csv`), csv(rows,
    ['id','method','assembly','category','calls','completed','activeAtSnapshot','inclusiveMs','selfMs','averageMs','averageSelfMs','maxMs','threads','source','line']));
  fs.writeFileSync(path.join(dir, `${run.name}.callers.csv`), csv(edges,
    ['callerId','id','caller','callee','calls','completed','inclusiveMs','selfMs','averageMs','maxMs','threads']));
  const measured = rows.filter(r => r.calls);
  const profile = { ...run, observedMs: raw.observedMs, probeBookkeepingMs: raw.probeBookkeepingMs,
    calls: measured.reduce((sum, r) => sum + r.calls, 0), executedMethods: measured.length,
    activeAtSnapshot: measured.reduce((sum, r) => sum + r.activeAtSnapshot, 0), rows: measured, edges };
  profiles.push(profile);
  console.log(`\n${run.name}: ${profile.executedMethods} methods executed, ${profile.calls.toLocaleString()} calls, ${raw.probeBookkeepingMs.toFixed(1)}ms probe bookkeeping`);
  for (const row of measured.slice(0, 8)) console.log(`  self=${row.selfMs.toFixed(1)}ms total=${row.inclusiveMs.toFixed(1)}ms calls=${row.calls} ${row.method}`);
}
fs.writeFileSync(path.join(dir, 'summary.json'), JSON.stringify({ coverage: {
  assemblies: manifest.assemblies.length, instrumentedMethods: manifest.instrumentedMethods,
  withoutBody: manifest.methods.filter(m => !m.instrumented).length }, runs,
  profiles: profiles.map(({ rows, edges, ...p }) => ({ ...p, topSelf: rows.slice(0, 30),
    topCalls: [...rows].sort((a,b) => b.calls-a.calls).slice(0,30), topEdges: edges.slice(0,50) })) }, null, 2));
const data = JSON.stringify(profiles).replaceAll('<', '\\u003c');
const runData = JSON.stringify(runs).replaceAll('<', '\\u003c');
const html = `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>CIS function performance · BridgeLink</title><style>
:root{font:15px system-ui;color:#e7edf6;background:#0c1423;color-scheme:dark}body{max-width:1500px;margin:auto;padding:32px}h1{font-size:30px;margin:0 0 12px}p{line-height:1.6;color:#b7c6db;max-width:1100px}.cards{display:flex;gap:16px;flex-wrap:wrap;margin:24px 0}.card{background:#142239;border:1px solid #30435f;border-radius:10px;padding:18px;min-width:180px}.card strong{display:block;font-size:26px;color:#79dbc4}label{display:inline-block;margin:8px 14px 16px 0}select,input{font:inherit;padding:8px;background:#142239;border:1px solid #4a6080;border-radius:6px}input{min-width:320px}table{border-collapse:collapse;width:100%;font-size:13px}th,td{text-align:right;padding:10px;border-bottom:1px solid #26364f;vertical-align:top}th{color:#a9c2e8;background:#142239;position:sticky;top:0}th:first-child,td:first-child{text-align:left}td:first-child{max-width:650px;overflow-wrap:anywhere}small{color:#8ca6c8}a{color:#79dbc4}.table{overflow:auto;max-height:680px}button{background:#203655;color:white;padding:8px;border:1px solid #4a6080;border-radius:5px;cursor:pointer}.bar{height:4px;background:#5acbb2;margin-top:8px}#detail{white-space:pre-wrap;background:#142239;padding:16px;border-radius:8px;overflow-wrap:anywhere}caption{text-align:left;padding:15px 0;color:#a9c2e8}footer{margin:30px 0;color:#a9c2e8}
</style><h1>CIS function performance</h1><p>BridgeLink · Local Windows · Every managed CIS method body instrumented. Select a run and sort by <b>self time</b> to find where time is spent, or by <b>calls</b> to find repeated work. Click a method to see its callers.</p>
<div class="cards"><div class="card"><strong>${manifest.instrumentedMethods.toLocaleString()}</strong>instrumented method bodies</div><div class="card"><strong>${manifest.assemblies.length}</strong>CIS assemblies</div><div class="card"><strong id="wall"></strong>command elapsed</div><div class="card"><strong id="calls"></strong>measured invocations</div></div>
<p><b>Total time = completed calls × average duration.</b> Total includes children and overlaps between methods; do not sum that column. Self time excludes instrumented children and their probe bookkeeping, but includes framework calls, I/O and waiting. Async/iterator <code>MoveNext</code> counts execution segments. The wrapper counts logical starts. Background continuations have a thread root rather than an inferred async parent.</p>
<table id="runs"><caption>Ordinary and instrumented command times, in execution order (seconds). Existing caches were retained; these are not cold-disk benchmarks.</caption><thead><tr><th>Command</th><th>Ordinary 1</th><th>Instrumented 1</th><th>Instrumented 2</th><th>Ordinary 2</th></tr></thead><tbody></tbody></table>
<label>Run <select id="run"></select></label><label>Sort <select id="sort"><option value="selfMs">Self time</option><option value="inclusiveMs">Total time</option><option value="calls">Calls</option><option value="averageMs">Average time</option><option value="maxMs">Maximum time</option></select></label><label>Find <input id="search" placeholder="Function, type or source file"></label><label><input id="generated" type="checkbox" style="min-width:0" checked> Include accessors and generated methods</label>
<p id="meta"></p><div class="table"><table><thead><tr><th>Method / source</th><th>Calls</th><th>Total ms</th><th>Self ms</th><th>Average ms</th><th>Max ms</th></tr></thead><tbody id="methods"></tbody></table></div><p id="count"></p><div id="detail">Select a method to inspect caller counts.</div><footer>Complete CSV exports include all instrumented methods, including zero-call rows. Raw JSON and the coverage manifest are next to this report. Probe bookkeeping is measured separately, but instrumented JIT/code-shape overhead cannot be subtracted reliably.</footer>
<script>const profiles=${data};const runs=${runData};const byId=new Map(profiles.flatMap(p=>p.rows.map(r=>[r.id,r])));const $=id=>document.getElementById(id);const fmt=n=>n.toLocaleString(undefined,{maximumFractionDigits:3});
for(const p of profiles){const o=document.createElement('option');o.textContent=p.name;$('run').append(o)}
for(const name of [...new Set(runs.map(r=>r.command))]){const tr=document.createElement('tr');const rows=runs.filter(r=>r.command===name);for(const v of [name,...rows.map(r=>(r.wallMs/1000).toFixed(3))]){const td=document.createElement('td');td.textContent=v;tr.append(td)}$('runs').tBodies[0].append(tr)}
function render(){const p=profiles[$('run').selectedIndex];if(!p)return;$('wall').textContent=(p.wallMs/1000).toFixed(3)+' s';$('calls').textContent=p.calls.toLocaleString();$('meta').textContent=p.executedMethods.toLocaleString()+' distinct methods executed; '+p.activeAtSnapshot+' calls still active at snapshot; '+fmt(p.probeBookkeepingMs)+' ms measured probe bookkeeping. ';
const link=document.createElement('a');link.href=p.name+'.methods.csv';link.textContent='Download every method as CSV';$('meta').append(link);const q=$('search').value.toLowerCase();const rows=p.rows.filter(r=>(r.method+' '+r.source).toLowerCase().includes(q)&&($('generated').checked||['method','constructor'].includes(r.category))).sort((a,b)=>b[$('sort').value]-a[$('sort').value]);$('methods').replaceChildren();const max=rows[0]?.[$('sort').value]||1;
for(const r of rows.slice(0,250)){const tr=document.createElement('tr');const name=document.createElement('td');const button=document.createElement('button');button.textContent=r.method;button.onclick=()=>detail(p,r);name.append(button);const source=document.createElement('small');source.textContent=' '+(r.source? r.source+':'+r.line:r.category);name.append(document.createElement('br'),source);const bar=document.createElement('div');bar.className='bar';bar.style.width=(100*r[$('sort').value]/max)+'%';name.append(bar);tr.append(name);for(const v of [r.calls,r.inclusiveMs,r.selfMs,r.averageMs,r.maxMs]){const td=document.createElement('td');td.textContent=fmt(v);tr.append(td)}$('methods').append(tr)}$('count').textContent='Showing '+Math.min(250,rows.length)+' of '+rows.length+' matching executed methods.'}
function detail(p,r){const edges=p.edges.filter(e=>e.id===r.id).sort((a,b)=>b.inclusiveMs-a.inclusiveMs);$('detail').textContent=r.method+'\\n'+r.category+'; '+r.calls+' calls; '+r.completed+' completed\\n\\nCallers (physical stack):\\n'+edges.map(e=>e.calls+' calls · '+fmt(e.inclusiveMs)+' ms · '+e.caller).join('\\n')}
for(const id of ['run','sort','generated'])$(id).onchange=render;$('search').oninput=render;render();</script></html>`;
fs.writeFileSync(path.join(dir, 'report.html'), html);
console.log(`\nReport: ${path.join(dir, 'report.html')}`);
