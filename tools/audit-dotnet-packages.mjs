import { spawnSync } from "node:child_process";

const project = process.argv[2] || "ChangeImpactStudio.slnx";
const audits = [
  { flag: "--vulnerable", evidence: "vulnerabilities" },
  { flag: "--deprecated", evidence: "deprecationReasons" },
];

for (const audit of audits) {
  const execution = spawnSync("dotnet", ["package", "list", "--project", project, audit.flag,
    "--include-transitive", "--format", "json", "--no-restore", "--configfile", "NuGet.Config"], {
    encoding: "utf8",
    shell: false,
    timeout: 300_000,
    maxBuffer: 32 * 1024 * 1024,
  });
  if (execution.error || execution.status !== 0) {
    console.error(execution.error?.message || execution.stderr || `dotnet package list exited ${execution.status}`);
    process.exit(3);
  }

  let document;
  try { document = JSON.parse(execution.stdout); }
  catch (error) {
    console.error(`NuGet ${audit.flag} output was not valid JSON: ${error instanceof Error ? error.message : "parse error"}`);
    process.exit(4);
  }
  const findings = [];
  visit(document, "$", audit.evidence, findings);
  if (findings.length > 0) {
    console.error(`NuGet ${audit.flag} audit found ${findings.length} package finding(s):`);
    for (const finding of findings.slice(0, 100)) console.error(`- ${finding}`);
    process.exit(1);
  }
  console.log(`NuGet ${audit.flag}: no findings.`);
}

function visit(value, path, evidence, findings) {
  if (Array.isArray(value)) {
    value.forEach((item, index) => visit(item, `${path}[${index}]`, evidence, findings));
    return;
  }
  if (!value || typeof value !== "object") return;
  if (Array.isArray(value[evidence]) && value[evidence].length > 0)
    findings.push(`${path} ${String(value.id || value.name || value.resolvedVersion || "package")}`);
  for (const [key, child] of Object.entries(value)) visit(child, `${path}.${key}`, evidence, findings);
}
