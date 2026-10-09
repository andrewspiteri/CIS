using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class EngineeringCompletionTests
{
    [Theory]
    [InlineData("separate-session", true)]
    [InlineData("same-session", false)]
    [InlineData("review-before-implementation", false)]
    [InlineData("tampered-result", false)]
    [InlineData("wrong-manifest-path", false)]
    public void NativeImplementationMustBindSuccessfulResultAndPrecedeSeparateReview(string scenario, bool expected)
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Passed);
        const string root = ".cis/local/agents/runs/implementation-1/";
        fixture.WriteJson(root + "result.json", new { schemaVersion = 1, status = CisAgentRunStates.Succeeded, envelopeId = "implementation-envelope" });
        var result = fixture.Artifact(root + "result.json");
        fixture.WriteJson(root + "manifest.json", new
        {
            schemaVersion = 1, runId = "implementation-1", taskId = receipt.TaskId, taskContractDigest = receipt.TaskDigest,
            mode = "implement", status = CisAgentRunStates.Succeeded, outputDigest = receipt.InputDigest,
            provider = "fixture", providerSessionId = "implementation-session", completedAtUtc = "2000-01-02T00:00:00Z",
            envelopeId = "implementation-envelope", resultDigest = result.Sha256[7..]
        });
        fixture.WriteJson("review-result.json", new { schemaVersion = 2, status = CisAgentRunStates.Succeeded, envelopeId = "review-envelope", review = new { recommendation = "ready", findings = Array.Empty<object>() } });
        var reviewResult = fixture.Artifact("review-result.json");
        fixture.WriteJson("review-run.json", new
        {
            schemaVersion = 1, runId = "review-1", taskId = receipt.TaskId, taskContractDigest = receipt.TaskDigest,
            mode = "review", permission = "read-only", status = CisAgentRunStates.Succeeded, inputDigest = receipt.InputDigest,
            provider = "fixture", providerSessionId = scenario == "same-session" ? "implementation-session" : "review-session",
            startedAtUtc = scenario == "review-before-implementation" ? "2000-01-01T00:00:00Z" : "2000-01-03T00:00:00Z",
            envelopeId = "review-envelope", resultDigest = reviewResult.Sha256
        });
        if (scenario == "tampered-result") File.AppendAllText(Path.Combine(fixture.Root, root + "result.json"), " ");
        if (scenario == "wrong-manifest-path") File.Copy(Path.Combine(fixture.Root, root + "manifest.json"), Path.Combine(fixture.Root, "copied.json"));
        receipt = receipt with
        {
            Implementation = fixture.Artifact(scenario == "wrong-manifest-path" ? "copied.json" : root + "manifest.json"),
            Gates = [new("review", EngineeringGateState.Passed, "Synthetic protocol review; not live agent approval.", [fixture.Artifact("review-run.json"), reviewResult])]
        };
        var errors = fixture.Review(receipt, gateId: "review");
        Assert.Equal(expected, errors.Count == 0);
    }

    [Fact]
    public void MissingOrUnsafeArtifactDoesNotHideLaterRequiredGates()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Passed);
        receipt = receipt with { Gates = [receipt.Gates[0] with { Artifacts = [new("missing.json", "sha256:none"), new("../outside.json", "sha256:none")] }] };
        var path = Path.Combine(fixture.Root, "receipt.json");
        File.WriteAllText(path, EngineeringCompletionReview.Serialize(receipt));
        var errors = EngineeringCompletionReview.Review(fixture.Root, path, "CIS-0001", new("WORK-001", "backend", "low", null, null, "Fixture", [], [], [], "Accept", "Validate", "InProgress"),
            Path.Combine(fixture.Root, "policy.json"), null, new(1, true, [new("unit", "Required", false), new("business", "Required", false)], []), "sha256:inputs", "docs");
        Assert.Contains(errors, error => error.Contains("Gate 'unit' artifact 'missing.json'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("escapes", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'business' is missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invented-id")]
    [InlineData("changed-inputs")]
    [InlineData("changed-contract")]
    [InlineData("failed-run")]
    [InlineData("future")]
    public void ImplementationProvenanceRejectsUnverifiedClaims(string scenario)
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Passed);
        var record = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root, receipt.Implementation!.Path)))!;
        if (scenario == "missing") receipt = receipt with { Implementation = null };
        else
        {
            if (scenario == "invented-id") record["runId"] = "not-the-implementer";
            if (scenario == "changed-inputs") record["inputDigest"] = "sha256:old";
            if (scenario == "changed-contract") record["taskContractDigest"] = "sha256:old";
            if (scenario == "future") record["completedAtUtc"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
            if (scenario == "failed-run") { record["kind"] = "agent"; record["mode"] = "implement"; record["status"] = "Failed"; }
            fixture.WriteJson(receipt.Implementation.Path, record);
            receipt = receipt with { Implementation = fixture.Artifact(receipt.Implementation.Path) };
        }
        Assert.Contains(fixture.Review(receipt), error => error.Contains("implementation", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(EngineeringGateState.Missing)]
    [InlineData(EngineeringGateState.Failed)]
    [InlineData(EngineeringGateState.Stale)]
    [InlineData(EngineeringGateState.Skipped)]
    public void NonPassingGateCannotComplete(EngineeringGateState state)
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(state);
        Assert.Contains(fixture.Review(receipt), error => error.Contains(state.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OmittedRequiredGateCannotBeHiddenByGreenConfiguredChecks()
    {
        using var fixture = new Fixture();
        Assert.Contains(fixture.Review(fixture.Receipt(EngineeringGateState.Passed) with { Gates = [] }), error => error.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentNativeEvidencePassesAndTamperedArtifactFails()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Passed);
        Assert.Empty(fixture.Review(receipt));
        File.AppendAllText(Path.Combine(fixture.Root, "result.json"), " ");
        Assert.Contains(fixture.Review(receipt), error => error.Contains("modified", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeReportTamperingInvalidatesAnUnchangedManifest()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Passed);
        File.WriteAllText(Path.Combine(fixture.Root, "native.trx"), "<TestRun />");
        Assert.Contains(fixture.Review(receipt), error => error.Contains("native evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void TaskBodyChangesInvalidateContractButTransitionBookkeepingDoesNot()
    {
        var task = new PlanWorkItem("WORK-001", "backend", "low", null, "task.md", "Scoped work", [], [], [], "Accept", "Validate", "InProgress");
        const string document = "task_status: InProgress\n## Requirements\nValidate every record.\n## Completion evidence\n";
        var digest = EngineeringCompletionReview.TaskDigest(task, document);
        var snapshot = CisToolUsageSnapshot.TaskRow(CisToolUsageSnapshot.FormatValues(1, 0, 0, "sha256:" + new string('a', 64)));
        Assert.Equal(digest, EngineeringCompletionReview.TaskDigest(task with { Status = "Complete" },
            document.Replace("InProgress", "Complete", StringComparison.Ordinal) + snapshot + "\n"));
        foreach (var altered in new[] { "| CIS tool-usage snapshot | ... | Passed | acceptance verified |", snapshot.Replace("Recorded", "Passed", StringComparison.Ordinal),
            snapshot.Replace("invocations=1", "invocations=changed acceptance", StringComparison.Ordinal) })
            Assert.NotEqual(digest, EngineeringCompletionReview.TaskDigest(task, document + altered + "\n"));
        Assert.NotEqual(digest, EngineeringCompletionReview.TaskDigest(task,
            document.Replace("every record", "the first record", StringComparison.Ordinal)));
        Assert.NotEqual(digest, EngineeringCompletionReview.TaskDigest(task,
            document.Replace("Validate every record.", "Validate every record.\ntask_status: is meaningful example input", StringComparison.Ordinal)));
        Assert.NotEqual(digest, EngineeringCompletionReview.TaskDigest(task,
            document.Replace("Validate every record.", "Validate every record.\n| CIS tool-usage snapshot | Contract example |", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0, 0, "unit", "sha256:inputs")]
    [InlineData(1, 1, "unit", "sha256:inputs")]
    [InlineData(1, 0, "business", "sha256:inputs")]
    [InlineData(1, 0, "unit", "sha256:old-inputs")]
    public void NativeReportsMustContainCurrentNonSkippedTestsAtRequiredLayer(int total, int skipped, string layer, string inputDigest)
    {
        using var fixture = new Fixture();
        fixture.WriteResult(total, skipped, layer, inputDigest);
        Assert.Contains(fixture.Review(fixture.Receipt(EngineeringGateState.Passed)), error => error.Contains("native evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void InapplicabilityRequiresScopeEvidenceAndCannotBypassUnconditionalGates()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Inapplicable);
        Assert.Empty(fixture.Review(receipt));
        Assert.NotEmpty(fixture.Review(receipt, allowsInapplicability: false));
        Assert.NotEmpty(fixture.Review(receipt with { Gates = [receipt.Gates[0] with { Rationale = "TODO" }] }));
        Assert.NotEmpty(fixture.Review(receipt with { Gates = [receipt.Gates[0] with { Artifacts = [] }] }));
    }

    [Fact]
    public void ChangedContractInputsPolicyOrGraphInvalidateReceipt()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Passed);
        foreach (var changed in new[] { receipt with { TaskDigest = "old" }, receipt with { InputDigest = "old" },
                     receipt with { PolicyDigest = "old" }, receipt with { GraphBuildId = "old" } })
            Assert.Contains(fixture.Review(changed), error => error.Contains("stale", StringComparison.Ordinal));
    }

    [Fact]
    public void TraceabilityUnresolvedFindingsAndSelfReviewBlock()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Receipt(EngineeringGateState.Passed);
        Assert.NotEmpty(fixture.Review(receipt with { RequirementIds = [] }));
        Assert.NotEmpty(fixture.Review(receipt with { UnresolvedBlockingFindings = ["REVIEW-001"] }));
        Assert.NotEmpty(fixture.Review(receipt with { ReviewerRunId = receipt.ImplementerRunId }));
        Assert.NotEmpty(fixture.Review(receipt with { Gates = [receipt.Gates[0], receipt.Gates[0]] }));
    }

    [Theory]
    [InlineData("provider-a", false, "sha256:inputs", true)]
    [InlineData("provider-b", false, "sha256:inputs", true)]
    [InlineData("provider-a", true, "sha256:inputs", false)]
    [InlineData("provider-a", false, "sha256:old", false)]
    public void ReviewRequiresCurrentIndependentProviderResultWithoutBlockingFindings(string provider, bool blocking, string inputs, bool expectedPass)
    {
        using var fixture = new Fixture();
        fixture.WriteJson("review-result.json", new
        {
            schemaVersion = 2,
            envelopeId = "envelope-1",
            status = CisAgentRunStates.Succeeded,
            review = new { recommendation = "ready", findings = blocking ? new[] { new { severity = "blocking" } } : [] }
        });
        var resultArtifact = fixture.Artifact("review-result.json");
        fixture.WriteJson("review-run.json", new
        {
            schemaVersion = 1,
            runId = "review-1",
            startedAtUtc = "2000-01-02T00:00:00Z",
            inputDigest = inputs,
            status = CisAgentRunStates.Succeeded,
            mode = "review",
            permission = "read-only",
            taskId = "WORK-001",
            provider,
            envelopeId = "envelope-1",
            resultDigest = resultArtifact.Sha256,
            taskContractDigest = fixture.Receipt(EngineeringGateState.Passed).TaskDigest
        });
        var receipt = fixture.Receipt(EngineeringGateState.Passed) with
        {
            Gates = [new("review", EngineeringGateState.Passed, "Independent source and evidence review completed.", [fixture.Artifact("review-run.json"), resultArtifact])]
        };
        var errors = fixture.Review(receipt, gateId: "review");
        Assert.Equal(expectedPass, errors.Count == 0);
        File.AppendAllText(Path.Combine(fixture.Root, "review-result.json"), " ");
        Assert.NotEmpty(fixture.Review(receipt, gateId: "review"));
    }

    [Fact]
    public void ExecutionIdentityDetectsUncommittedAndNewInputsButIgnoresDisposableEvidence()
    {
        using var fixture = new Fixture();
        var context = new CisRepositoryContext(fixture.Root, "example", "docs", Path.Combine(fixture.Root, "docs"), Path.Combine(fixture.Root, "docs/catalog.yml"));
        var baseline = CisExecutionIdentity.Capture(context);
        Directory.CreateDirectory(Path.Combine(fixture.Root, ".cis/local"));
        File.WriteAllText(Path.Combine(fixture.Root, ".cis/local/output.txt"), "evidence");
        Assert.Equal(baseline, CisExecutionIdentity.Capture(context));
        File.WriteAllText(Path.Combine(fixture.Root, "New.cs"), "class Added;");
        var added = CisExecutionIdentity.Capture(context);
        Assert.NotEqual(baseline, added);
        File.WriteAllText(Path.Combine(fixture.Root, "New.cs"), "class Changed;");
        Assert.NotEqual(added, CisExecutionIdentity.Capture(context));
    }

    [Fact]
    public void NativeSecurityManifestAcceptsCleanScansButRejectsStaleOrTamperedReports()
    {
        using var fixture = new Fixture();
        fixture.WriteJson("scanner.json", Array.Empty<object>());
        fixture.WriteJson("docs/references/security-suite-profile.md", new { scope = "Synthetic scanner scope" });
        var profile = fixture.Artifact("docs/references/security-suite-profile.md");
        var scanner = fixture.Artifact("scanner.json");
        var artifacts = new[] { new { kind = "scanner-result", path = scanner.Path, digest = scanner.Sha256,
            bytes = new FileInfo(Path.Combine(fixture.Root, scanner.Path)).Length } };
        fixture.WriteJson("security.json", new
        {
            schemaVersion = 1,
            inputDigest = "sha256:inputs",
            status = "passed",
            suites = new[] { new { status = "passed", tool = "gitleaks", category = "secret", findings = Array.Empty<object>(), artifacts } },
            profilePath = profile.Path,
            profileDigest = profile.Sha256,
            artifacts
        });
        var receipt = fixture.Receipt(EngineeringGateState.Passed) with
        {
            Gates = [new("security", EngineeringGateState.Passed, "Native scanner inspected the declared source scope.", [fixture.Artifact("security.json")])]
        };
        Assert.Empty(fixture.Review(receipt, gateId: "security"));
        File.AppendAllText(Path.Combine(fixture.Root, "scanner.json"), " ");
        Assert.NotEmpty(fixture.Review(receipt, gateId: "security"));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Combine(Path.GetTempPath(), "cis-completion-tests");
        public string Root { get; } = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        private readonly PlanWorkItem _task = new("WORK-001", "backend", "low", null, "agent-tasks/WORK-001.md", "Change a behavior",
            ["REQ-001"], [], [], "Behavior is verified", "dotnet test", "InProgress");
        private readonly CisGraphMetadataReadResult _graph = new("ready", 0, null, "fresh", new("graph-1", "example", null, true, "complete"), null, [], []);
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "policy.json"), "{\"schemaVersion\":1}");
            WriteResult(1, 0, "unit", "sha256:inputs");
        }
        public void WriteResult(int total, int skipped, string layer, string inputDigest)
        {
            var path = Path.Combine(Root, "native.trx");
            File.WriteAllText(path, "<TestRun><Results><UnitTestResult testName=\"contract\" outcome=\"Passed\" /></Results></TestRun>");
            var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            var artifacts = new[] { new { kind = "test-result", path = "native.trx", digest, bytes = new FileInfo(path).Length } };
            File.WriteAllText(Path.Combine(Root, "result.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                inputDigest,
                status = "passed",
                suites = new[] { new { layer, status = "passed", total, passed = total - skipped, skipped, failed = 0, artifacts } },
                artifacts
            }));
        }
        public EngineeringCompletionReceipt Receipt(EngineeringGateState state, string inputDigest = "sha256:inputs")
        {
            var template = EngineeringCompletionReview.Template("CIS-0001", _task, Path.Combine(Root, "policy.json"), _graph,
                new(1, true, [new("unit", "Changed behavior", true)], []), inputDigest);
            var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "result.json"))));
            WriteJson(".cis/local/human-implementation.json", new
            {
                schemaVersion = 1, runId = "implementation-1", kind = "human-implemented", actor = "Synthetic protocol author",
                taskId = _task.Id, taskContractDigest = template.TaskDigest, inputDigest, completedAtUtc = "2000-01-01T00:00:00Z"
            });
            return template with
            {
                ImplementerRunId = "implementation-1",
                Implementation = Artifact(".cis/local/human-implementation.json"),
                ReviewerRunId = "review-1",
                Gates = [new("unit", state, "Fixture verifies the scoped behavior or documents its concrete inapplicability.", [new("result.json", digest)])]
            };
        }
        public void WriteJson(string path, object value)
        {
            var target = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, JsonSerializer.Serialize(value));
        }
        public EngineeringEvidenceArtifact Artifact(string path)
            => new(path, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, path)))));
        public IReadOnlyList<string> Review(EngineeringCompletionReceipt receipt, bool allowsInapplicability = true, string gateId = "unit", string inputDigest = "sha256:inputs", string documentationRoot = "docs")
        {
            var path = Path.Combine(Root, "receipt.json");
            File.WriteAllText(path, EngineeringCompletionReview.Serialize(receipt));
            return EngineeringCompletionReview.Review(Root, path, "CIS-0001", _task, Path.Combine(Root, "policy.json"), _graph,
                new(1, true, [new(gateId, "Changed behavior", allowsInapplicability)], []), inputDigest, documentationRoot);
        }
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Parent, Root)) throw new InvalidOperationException();
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
