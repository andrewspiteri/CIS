import { existsSync, mkdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const mode = process.argv[2];
if (!new Set(["sast", "secrets", "filesystem"]).has(mode)) {
  console.error("Usage: node tools/run-security-scan.mjs <sast|secrets|filesystem>");
  process.exit(2);
}

const root = resolve(dirname(new URL(import.meta.url).pathname.replace(/^\/(?:([A-Za-z]):)/, "$1:")), "..");
const resultRoot = join(root, ".cis", "local", "security", "results");
const cacheRoot = resolve(process.env.XDG_CACHE_HOME || join(root, ".cis", "local", "security", "cache"), "trivy");
const semgrepImage = "semgrep/semgrep@sha256:207983631beecdbe7fa29196c7f4a7a5f29033933cdb76c687ce4a672e07618d";
const gitleaksImage = "ghcr.io/gitleaks/gitleaks@sha256:cdbb7c955abce02001a9f6c9f602fb195b7fadc1e812065883f695d1eeaba854";
const trivyImage = "ghcr.io/aquasecurity/trivy@sha256:ee940acbf1f58ebadb42d01434ce4609530bf1b52536afbd1eee66cd7123c5c9";

mkdirSync(resultRoot, { recursive: true });
mkdirSync(cacheRoot, { recursive: true });

function mount(path, target) {
  return `${resolve(path)}:${target}`;
}

function identityArguments() {
  return typeof process.getuid === "function" ? ["--user", `${process.getuid()}:${process.getgid()}`] : [];
}

function run(arguments_) {
  const execution = spawnSync("docker", arguments_, { cwd: root, stdio: "inherit", shell: false });
  if (execution.error) {
    console.error(execution.error.message);
    process.exit(3);
  }
  if (execution.status !== 0) process.exit(execution.status ?? 3);
}

function report(name) {
  const path = join(resultRoot, name);
  if (!existsSync(path)) {
    console.error(`Scanner completed without declared result: ${path}`);
    process.exit(4);
  }
  return JSON.parse(readFileSync(path, "utf8"));
}

function gate(count, label) {
  if (count > 0) {
    console.error(`${label} found ${count} blocking finding(s). Evidence was retained.`);
    process.exit(1);
  }
  console.log(`${label}: no blocking findings.`);
}

if (mode === "sast") {
  run(["run", "--rm", ...identityArguments(), "--env", "HOME=/tmp", "-v", mount(root, "/src"), "-w", "/src",
    semgrepImage, "semgrep", "scan", "--config", "auto", "--json", "--output",
    "/src/.cis/local/security/results/semgrep.json", "--exclude", ".artifacts", "--exclude", ".cis",
    "--exclude", ".codex-tmp", "--exclude", ".git", "--exclude", ".stryker-tmp", "--exclude", ".vs",
    "--exclude", "artifacts", "--exclude", "bin", "--exclude", "node_modules", "--exclude", "obj", "."]);
  const findings = report("semgrep.json").results || [];
  gate(findings.filter(item => ["ERROR", "HIGH", "CRITICAL"].includes(String(item.extra?.severity || "").toUpperCase())).length, "Semgrep SAST");
}

if (mode === "secrets") {
  const arguments_ = ["run", "--rm", ...identityArguments(), "--env", "HOME=/tmp", "-v", mount(root, "/src"), "-w", "/src",
    gitleaksImage, "detect", "--source=/src", "--no-banner", "--redact", "--report-format=json",
    "--report-path=/src/.cis/local/security/results/gitleaks.json", "--exit-code=0"];
  if (existsSync(join(root, ".gitleaks.toml"))) arguments_.push("--config=/src/.gitleaks.toml");
  run(arguments_);
  gate(report("gitleaks.json").length, "Gitleaks");
}

if (mode === "filesystem") {
  run(["run", "--rm", ...identityArguments(), "--env", "HOME=/tmp", "-v", mount(root, "/src"), "-v", mount(cacheRoot, "/cache"),
    "-w", "/src", trivyImage, "fs", "--cache-dir", "/cache", "--format", "json", "--output",
    "/src/.cis/local/security/results/trivy-fs.json", "--scanners", "vuln", "--skip-dirs", ".artifacts",
    "--skip-dirs", ".cis", "--skip-dirs", ".codex-tmp", "--skip-dirs", ".git", "--skip-dirs", ".stryker-tmp",
    "--skip-dirs", ".vs", "--skip-dirs", "artifacts", "--skip-dirs", "bin", "--skip-dirs", "node_modules",
    "--skip-dirs", "obj", "."]);
  const findings = (report("trivy-fs.json").Results || []).flatMap(item => item.Vulnerabilities || []);
  gate(findings.filter(item => ["HIGH", "CRITICAL"].includes(String(item.Severity).toUpperCase())).length, "Trivy filesystem");
}
