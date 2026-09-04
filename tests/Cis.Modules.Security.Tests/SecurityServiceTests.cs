using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Security;
using Cis.Modules.Workflow;
using System.Text.Json;
using Xunit;

namespace Cis.Modules.Security.Tests;

public sealed class SecurityServiceTests
{
    [Fact]
    public void Inventory_ParsesAndPersistsProfile()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/security-suite-profile.md", Profile("semgrep-json", ".cis/local/security/results/semgrep.json"));
        repository.Write("docs/cis/references/accepted-security-findings.md", Acceptances());

        var result = Service().Inventory(repository.Path);

        Assert.Equal("inventoried", result.Status);
        Assert.Equal("sast", Assert.Single(result.Suites).Category);
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis", "local", "security", "inventory.json")));
    }

    [Fact]
    public void ValidateStrict_RejectsEvidenceOutsideDerivedSecurityRoot()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/security-suite-profile.md", Profile("semgrep-json", "reports/semgrep.json"));
        repository.Write("docs/cis/references/accepted-security-findings.md", Acceptances());

        var result = Service().Validate(repository.Path, strict: true);

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Diagnostics, item => item.Contains(".cis/local/security", StringComparison.Ordinal));
    }

    [Fact]
    public void SemgrepAdapter_NormalizesFindingsAndHashesEvidence()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/security/results/semgrep.json", """{"results":[{"check_id":"typescript.lang.security.audit","path":"src/app.ts","start":{"line":12},"extra":{"severity":"ERROR","message":"Unsafe use"}}]}""");
        var path = Path.Combine(repository.Path, ".cis", "local", "security", "results", "semgrep.json");

        var execution = new SemgrepJsonSecurityResultAdapter().Read(new(repository.Path, Suite("semgrep-json"), path));

        var finding = Assert.Single(execution.Findings);
        Assert.Equal("high", finding.Severity);
        Assert.Equal(64, finding.Fingerprint.Length);
        Assert.StartsWith("sha256:", Assert.Single(execution.Artifacts).Digest, StringComparison.Ordinal);
        Assert.Equal(71, Assert.Single(execution.Artifacts).Digest.Length);
    }

    [Fact]
    public void GitleaksAdapter_NeverPersistsSecretMaterial()
    {
        using var repository = TestRepository.Create();
        const string secret = "ghp_1234567890abcdefghijklmnopqrstuvwxyz";
        repository.Write(".cis/local/security/results/gitleaks.json", $$"""[{"RuleID":"github-pat","File":"fixture.env","StartLine":1,"Description":"token={{secret}}","Secret":"{{secret}}","Fingerprint":"fixture:github-pat:1"}]""");
        var path = Path.Combine(repository.Path, ".cis", "local", "security", "results", "gitleaks.json");

        var execution = new GitleaksJsonSecurityResultAdapter().Read(new(repository.Path, Suite("gitleaks-json", "secret", "gitleaks"), path));
        var serialized = System.Text.Json.JsonSerializer.Serialize(execution);

        Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        Assert.Contains("secret-like value", Assert.Single(execution.Findings).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiredOrWildcardAcceptance_IsInvalid()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/accepted-security-findings.md", """
            | Finding ID | Scanner | Fingerprint | Classification | Reason | Accepted until | Owner | Approved by | Approval reference |
            |---|---|---|---|---|---|---|---|---|
            | SEC-001 | semgrep | wildcard-* | false-positive | fixture | 2000-01-01 | owner | approver | DEC-001 |
            """);

        var result = Service().ValidateExceptions(repository.Path, strict: true);

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Diagnostics, item => item.Contains("exact scanner and fingerprint", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, item => item.Contains("expired", StringComparison.Ordinal));
    }

    [Fact]
    public void SarifTrivyAndZapAdapters_NormalizeNativeResults()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/security/results/sarif.json", """{"runs":[{"tool":{"driver":{"name":"CodeQL","rules":[{"id":"js/xss","properties":{"security-severity":"9.0"}}]}},"results":[{"ruleId":"js/xss","message":{"text":"unsafe output"},"locations":[{"physicalLocation":{"artifactLocation":{"uri":"src/app.ts"},"region":{"startLine":3}}}]}]}]}""");
        repository.Write(".cis/local/security/results/trivy.json", """{"Results":[{"Target":"pkg-lock","Vulnerabilities":[{"VulnerabilityID":"CVE-1","Severity":"HIGH","PkgName":"fixture","Title":"upgrade"}]}]}""");
        repository.Write(".cis/local/security/results/zap.json", """{"site":[{"alerts":[{"pluginid":"10001","riskcode":"3","alert":"Missing header","instances":[{"uri":"http://localhost/"}]}]}]}""");

        var sarif = new SarifSecurityResultAdapter().Read(new(repository.Path, Suite("sarif"), Path.Combine(repository.Path, ".cis/local/security/results/sarif.json")));
        var trivy = new TrivyJsonSecurityResultAdapter().Read(new(repository.Path, Suite("trivy-json", "filesystem", "trivy"), Path.Combine(repository.Path, ".cis/local/security/results/trivy.json")));
        var zap = new ZapJsonSecurityResultAdapter().Read(new(repository.Path, Suite("zap-json", "dast", "zap"), Path.Combine(repository.Path, ".cis/local/security/results/zap.json")));

        Assert.Equal("critical", Assert.Single(sarif.Findings).Severity);
        Assert.Equal("high", Assert.Single(trivy.Findings).Severity);
        Assert.Equal("high", Assert.Single(zap.Findings).Severity);
        Assert.Equal("Missing header", Assert.Single(zap.Findings).Message);
    }

    [Fact]
    public void Reconcile_ProcessSuccessWithoutDeclaredResult_IsInvalidEvidenceAndRetainsLog()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/security-suite-profile.md", Profile("semgrep-json", ".cis/local/security/results/semgrep.json"));
        repository.Write("docs/cis/references/accepted-security-findings.md", Acceptances());
        repository.Write("docs/cis/workflows/security-verification.md", Workflow());
        var resolver = new CisRepositoryContextResolver();
        var run = new WorkflowService(resolver).Run(repository.Path, "security-verification", "SEC-MISSING");

        var result = Service().Reconcile(repository.Path, run.RunId!);

        Assert.Equal("failed", result.Manifest!.Status);
        var suite = Assert.Single(result.Manifest.Suites);
        Assert.Equal("invalid-evidence", suite.Status);
        Assert.Equal(SecurityFailureKind.InvalidEvidence, suite.FailureKind);
        Assert.Contains(suite.Artifacts, artifact => artifact.Kind == "workflow-log" && artifact.Attempt == 1);
    }

    [Fact]
    public void Reconcile_MalformedDeclaredResult_IsInvalidEvidence()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/security-suite-profile.md", Profile("semgrep-json", ".cis/local/security/results/semgrep.json"));
        repository.Write("docs/cis/references/accepted-security-findings.md", Acceptances());
        repository.Write("docs/cis/workflows/security-verification.md", Workflow());
        repository.Write(".cis/local/security/results/semgrep.json", "{not-json");
        var resolver = new CisRepositoryContextResolver();
        var run = new WorkflowService(resolver).Run(repository.Path, "security-verification", "SEC-MALFORMED");

        var result = Service().Reconcile(repository.Path, run.RunId!);

        Assert.Equal("failed", result.Manifest!.Status);
        Assert.Equal("invalid-evidence", Assert.Single(result.Manifest.Suites).Status);
    }

    [Fact]
    public void LocalAiSummary_ReceivesOnlyRedactedFindingsAndCannotChangeVerdict()
    {
        using var repository = TestRepository.Create();
        const string secret = "ghp_1234567890abcdefghijklmnopqrstuvwxyz";
        repository.Write("docs/cis/references/security-suite-profile.md", Profile("semgrep-json", ".cis/local/security/results/semgrep.json"));
        repository.Write("docs/cis/references/accepted-security-findings.md", Acceptances());
        var finding = new SecurityFinding("id", "secret-rule", "gitleaks", "secret", "high", secret, "fixture.env", 1, "fingerprint");
        var execution = new SecuritySuiteExecution("api-sast", "secret", "gitleaks", "findings", SecurityFailureKind.Finding, 0, 1, 0, 0, 0, [finding], [], []);
        var manifest = new SecurityRunManifest(1, "RUN-AI", "verify", "workflow", "fixture", "revision", "profile", "test", "start", "end", "failed", [execution], []);
        repository.Write(".cis/local/security/runs/RUN-AI/manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var ai = new CapturingAi();

        var result = Service(ai).Summarise(repository.Path, "RUN-AI", noLlm: false);

        Assert.Equal("summarised", result.Status);
        Assert.DoesNotContain(secret, ai.Prompt, StringComparison.Ordinal);
        Assert.Equal("failed", result.Manifest!.Status);
        Assert.Contains("deterministicVerdictPreserved", File.ReadAllText(Path.Combine(repository.Path, ".cis/local/security/runs/RUN-AI/summary-metadata.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void LocalAiSummary_DoesNotInvokeModelWhenThereAreNoFindings()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/security-suite-profile.md", Profile("semgrep-json", ".cis/local/security/results/semgrep.json"));
        repository.Write("docs/cis/references/accepted-security-findings.md", Acceptances());
        var execution = new SecuritySuiteExecution("api-sast", "sast", "semgrep", "passed", SecurityFailureKind.None, 0, 0, 0, 0, 0, [], [], []);
        var manifest = new SecurityRunManifest(1, "RUN-CLEAN", "verify", "workflow", "fixture", "revision", "profile", "test", "start", "end", "passed", [execution], []);
        repository.Write(".cis/local/security/runs/RUN-CLEAN/manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var ai = new CapturingAi();

        var result = Service(ai).Summarise(repository.Path, "RUN-CLEAN", noLlm: false);

        Assert.Equal("summarised", result.Status);
        Assert.Equal(0, ai.Calls);
        var summary = File.ReadAllText(Path.Combine(repository.Path, ".cis/local/security/runs/RUN-CLEAN/summary.md"));
        Assert.Contains("No normalized security findings were reported; AI triage was not invoked.", summary, StringComparison.Ordinal);
        var metadata = File.ReadAllText(Path.Combine(repository.Path, ".cis/local/security/runs/RUN-CLEAN/summary-metadata.json"));
        Assert.Contains("\"generationStatus\": \"not-required\"", metadata, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("nested/run")]
    [InlineData("C:\\outside")]
    public void Status_RejectsRunIdsThatAreNotPortablePathSegments(string runId)
    {
        using var repository = TestRepository.Create();

        var result = Service().Status(repository.Path, runId);

        Assert.Equal("invalid-run", result.Status);
        Assert.Contains(result.Diagnostics, item => item.Contains("single portable path segment", StringComparison.Ordinal));
    }

    private static SecurityService Service(ICisTextGenerationService? ai = null)
    {
        var resolver = new CisRepositoryContextResolver();
        ICisSecurityResultAdapter[] adapters = [new SarifSecurityResultAdapter(), new SemgrepJsonSecurityResultAdapter(),
            new GitleaksJsonSecurityResultAdapter(), new TrivyJsonSecurityResultAdapter(), new ZapJsonSecurityResultAdapter()];
        return new SecurityService(resolver, new WorkflowService(resolver), adapters, ai);
    }

    private static SecuritySuiteProfile Suite(string format, string category = "sast", string tool = "semgrep")
        => new("api-sast", "api", category, tool, "scan", ".", format, ".cis/local/security/results/result.json", ".", "always", "pr", "critical,high", "retain");

    private static string Profile(string format, string result) => $"""
        # Security suite profile

        | Suite ID | Component | Category | Tool | Command | Working directory | Result format | Result path | Target | Applies when | CI tier | Fail severities | Artifacts |
        |---|---|---|---|---|---|---|---|---|---|---|---|---|
        | api-sast | api | sast | semgrep | scan | . | {format} | {result} | . | always | pr | critical,high | retain |
        """;

    private static string Acceptances() => """
        # Accepted security findings

        | Finding ID | Scanner | Fingerprint | Classification | Reason | Accepted until | Owner | Approved by | Approval reference |
        |---|---|---|---|---|---|---|---|---|
        """;

    private static string Workflow() => """
        # Security verification workflow

        | Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
        |---|---|---|---|---|---|---:|
        | sast | dotnet --version | . | api-sast | - | no | 30 |
        """;

    private sealed class CapturingAi : ICisTextGenerationService
    {
        public int Calls { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public CisAiStatus GetStatus() => new([]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++;
            Prompt = request.Prompt;
            return new("generated", "ollama", "fixture", "Fix the finding.", null, true);
        }
    }

    private sealed class TestRepository : IDisposable
    {
        public string Path { get; }
        private TestRepository(string path) => Path = path;
        public static TestRepository Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-security-tests", Guid.NewGuid().ToString("N"));
            var repository = new TestRepository(path);
            repository.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: fixture\ndocumentation_root: docs/cis\n");
            repository.Write("docs/cis/catalog.yml", "schema_version: 1\nrepository: fixture\ndocuments: []\n");
            return repository;
        }
        public void Write(string relative, string content)
        {
            var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
