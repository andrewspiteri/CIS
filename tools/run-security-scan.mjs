import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync } from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const mode = process.argv[2];
if (!new Set(["sast", "secrets", "filesystem", "configuration"]).has(mode)) {
  console.error("Usage: node tools/run-security-scan.mjs <sast|secrets|filesystem|configuration>");
  process.exit(2);
}

const root = resolve(dirname(new URL(import.meta.url).pathname.replace(/^\/(?:([A-Za-z]):)/, "$1:")), "..");
const resultRoot = join(root, ".cis", "local", "security", "results");
const cacheRoot = resolve(process.env.XDG_CACHE_HOME || join(root, ".cis", "local", "security", "cache"), "trivy");
const semgrepImage = "semgrep/semgrep@sha256:207983631beecdbe7fa29196c7f4a7a5f29033933cdb76c687ce4a672e07618d";
const gitleaksImage = "ghcr.io/gitleaks/gitleaks@sha256:cdbb7c955abce02001a9f6c9f602fb195b7fadc1e812065883f695d1eeaba854";
const trivyImage = "ghcr.io/aquasecurity/trivy@sha256:ee940acbf1f58ebadb42d01434ce4609530bf1b52536afbd1eee66cd7123c5c9";
const maximumReportBytes = 100 * 1024 * 1024;
const maximumStagedFiles = 50_000;
const maximumStagedBytes = 2 * 1024 * 1024 * 1024;
const maximumVisitedDirectories = 100_000;

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
  if (execution.status !== 0) {
    console.error(`Scanner process failed with exit code ${execution.status ?? "unknown"}.`);
    process.exit(3);
  }
}

function report(name) {
  const path = join(resultRoot, name);
  if (!existsSync(path)) {
    console.error(`Scanner completed without declared result: ${path}`);
    process.exit(4);
  }
  if (statSync(path).size > maximumReportBytes) {
    console.error(`Scanner result exceeds the ${maximumReportBytes} byte evidence limit: ${path}`);
    process.exit(4);
  }
  try {
    return JSON.parse(readFileSync(path, "utf8"));
  } catch (error) {
    console.error(`Scanner result is not valid JSON: ${error instanceof Error ? error.message : "unknown parse error"}`);
    process.exit(4);
  }
}

function gate(count, label) {
  if (count > 0) {
    console.error(`${label} found ${count} blocking finding(s). Evidence was retained.`);
    process.exit(1);
  }
  console.log(`${label}: no blocking findings.`);
}

function stageDependencyManifests() {
  const staging = join(root, ".cis", "local", "security", "scan-input", "filesystem");
  rmSync(staging, { recursive: true, force: true });
  mkdirSync(staging, { recursive: true });
  const excluded = new Set([".artifacts", ".cis", ".codex-tmp", ".git", ".stryker-tmp", ".vs", "artifacts", "bin", "node_modules", "obj"]);
  const exact = new Set([
    "directory.build.props", "directory.packages.props", "global.json", "packages.lock.json",
    "package.json", "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "bun.lock", "bun.lockb",
    "requirements.txt", "pyproject.toml", "poetry.lock", "pipfile", "pipfile.lock",
    "go.mod", "go.sum", "cargo.toml", "cargo.lock", "gemfile", "gemfile.lock", "composer.json", "composer.lock",
    "gradle.lockfile", "podfile", "podfile.lock", "package.resolved", "pubspec.yaml", "pubspec.lock"
  ]);
  const matches = name => exact.has(name.toLowerCase())
    || /\.(?:csproj|fsproj|vbproj)$/i.test(name)
    || /^requirements(?:[-_.].+)?\.txt$/i.test(name)
    || /^(?:build|settings)\.gradle(?:\.kts)?$/i.test(name);
  let count = 0;
  let bytes = 0;
  let directories = 0;
  const visit = directory => {
    directories += 1;
    if (directories > maximumVisitedDirectories) throw new Error(`Security scan input exceeds ${maximumVisitedDirectories} directories.`);
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) continue;
      const source = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (!excluded.has(entry.name.toLowerCase())) visit(source);
        continue;
      }
      if (!entry.isFile() || !matches(basename(source))) continue;
      const size = statSync(source).size;
      count += 1;
      bytes += size;
      if (count > maximumStagedFiles || bytes > maximumStagedBytes)
        throw new Error("Security scan input exceeds the bounded file or byte limit.");
      const destination = join(staging, relative(root, source));
      mkdirSync(dirname(destination), { recursive: true });
      cpSync(source, destination);
    }
  };
  visit(root);
  console.log(`Trivy filesystem input: staged ${count} dependency manifest(s).`);
  return staging;
}

function stageConfigurationSources() {
  const staging = join(root, ".cis", "local", "security", "scan-input", "configuration");
  rmSync(staging, { recursive: true, force: true });
  mkdirSync(staging, { recursive: true });
  const excluded = new Set([".artifacts", ".cis", ".codex-tmp", ".git", ".stryker-tmp", ".vs", "artifacts", "bin", "node_modules", "obj"]);
  const exact = new Set(["dockerfile", "containerfile", "docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml"]);
  const matches = name => exact.has(name.toLowerCase())
    || /^(?:dockerfile|containerfile)(?:\..+)?$/i.test(name)
    || /\.(?:tf|yaml|yml)$/i.test(name);
  let count = 0;
  let bytes = 0;
  let directories = 0;
  const visit = directory => {
    directories += 1;
    if (directories > maximumVisitedDirectories) throw new Error(`Security scan input exceeds ${maximumVisitedDirectories} directories.`);
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) continue;
      const source = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (!excluded.has(entry.name.toLowerCase())) visit(source);
        continue;
      }
      if (!entry.isFile() || !matches(entry.name)) continue;
      const size = statSync(source).size;
      count += 1;
      bytes += size;
      if (count > maximumStagedFiles || bytes > maximumStagedBytes)
        throw new Error("Security scan input exceeds the bounded file or byte limit.");
      const destination = join(staging, relative(root, source));
      mkdirSync(dirname(destination), { recursive: true });
      cpSync(source, destination);
    }
  };
  visit(root);
  console.log(`Trivy configuration input: staged ${count} configuration file(s).`);
  return staging;
}

function stageSastSources() {
  const staging = join(root, ".cis", "local", "security", "scan-input", "sast");
  rmSync(staging, { recursive: true, force: true });
  mkdirSync(staging, { recursive: true });
  const excluded = new Set([".artifacts", ".cis", ".codex-tmp", ".git", ".stryker-tmp", ".vs", "artifacts", "bin", "node_modules", "obj"]);
  const extensions = /\.(?:c|cc|cpp|cs|cxx|fs|go|h|hpp|java|js|jsx|kt|kts|mjs|cjs|php|py|rb|rs|sh|swift|ts|tsx|vb|vue|yaml|yml)$/i;
  let count = 0;
  let bytes = 0;
  let directories = 0;
  const visit = directory => {
    directories += 1;
    if (directories > maximumVisitedDirectories) throw new Error(`Security scan input exceeds ${maximumVisitedDirectories} directories.`);
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) continue;
      const source = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (!excluded.has(entry.name.toLowerCase())) visit(source);
        continue;
      }
      if (!entry.isFile() || !extensions.test(entry.name)) continue;
      const size = statSync(source).size;
      count += 1;
      bytes += size;
      if (count > maximumStagedFiles || bytes > maximumStagedBytes)
        throw new Error("Security scan input exceeds the bounded file or byte limit.");
      const destination = join(staging, relative(root, source));
      mkdirSync(dirname(destination), { recursive: true });
      cpSync(source, destination);
    }
  };
  for (const target of ["src", "tests", "tools", "vscode-extension"])
    if (existsSync(join(root, target))) visit(join(root, target));
  console.log(`Semgrep SAST input: staged ${count} source file(s).`);
  return staging;
}

if (mode === "sast") {
  const scanInput = stageSastSources();
  run(["run", "--rm", ...identityArguments(), "--env", "HOME=/tmp", "-v", mount(root, "/src"), "-v", mount(scanInput, "/scan"), "-w", "/scan",
    semgrepImage, "semgrep", "scan", "--config", "auto", "--no-git-ignore", "--json", "--output",
    "/src/.cis/local/security/results/semgrep.json", "."]);
  const findings = report("semgrep.json").results || [];
  rmSync(scanInput, { recursive: true, force: true });
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
  const scanInput = stageDependencyManifests();
  run(["run", "--rm", ...identityArguments(), "--env", "HOME=/tmp", "-v", mount(root, "/src"), "-v", mount(cacheRoot, "/cache"),
    "-w", "/src", trivyImage, "fs", "--cache-dir", "/cache", "--format", "json", "--output",
    "/src/.cis/local/security/results/trivy-fs.json", "--scanners", "vuln", "/src/" + relative(root, scanInput).replaceAll("\\", "/")]);
  const findings = (report("trivy-fs.json").Results || []).flatMap(item => item.Vulnerabilities || []);
  rmSync(scanInput, { recursive: true, force: true });
  gate(findings.filter(item => ["HIGH", "CRITICAL"].includes(String(item.Severity).toUpperCase())).length, "Trivy filesystem");
}

if (mode === "configuration") {
  const scanInput = stageConfigurationSources();
  run(["run", "--rm", ...identityArguments(), "--env", "HOME=/tmp", "-v", mount(root, "/src"), "-v", mount(cacheRoot, "/cache"),
    "-w", "/src", trivyImage, "config", "--cache-dir", "/cache", "--format", "json", "--output",
    "/src/.cis/local/security/results/trivy-config.json", "/src/" + relative(root, scanInput).replaceAll("\\", "/")]);
  const results = report("trivy-config.json").Results || [];
  const findings = results.flatMap(item => item.Misconfigurations || []);
  rmSync(scanInput, { recursive: true, force: true });
  if (results.length === 0) {
    console.error("Trivy configuration detected no supported deployment configuration; the suite is unavailable, not passed.");
    process.exit(4);
  }
  gate(findings.filter(item => ["HIGH", "CRITICAL"].includes(String(item.Severity).toUpperCase())).length, "Trivy configuration");
}
