using System.Security.Cryptography;
using System.Text.Json;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed class WorkloadEvidenceTests
{
    [Fact]
    public void ComponentAndInstallationSuccessDoNotReplaceFailedWorkloadEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "cis-workload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var profile = Path.Combine(root, "workload-evidence.json");
            Assert.NotEmpty(WorkloadEvidenceReview.Review(root, profile, true));
            Assert.Empty(WorkloadEvidenceReview.Review(root, profile, false));
            File.WriteAllText(Path.Combine(root, "receipt.txt"), "retained fixture evidence");
            var fields = new Dictionary<string, object> {
                ["schemaVersion"] = 1, ["applicable"] = true, ["sourceBytes"] = 1_000_000,
                ["recordCount"] = 10000, ["shape"] = "Mixed small and large events", ["concurrency"] = 2,
                ["wallSeconds"] = 240, ["cpuSeconds"] = 180, ["memoryBytes"] = 1_000_000_000, ["storageBytes"] = 2_000_000_000,
                ["softwareIdentity"] = "fixture-build", ["fixtureIdentity"] = "synthetic-provider-hour",
                ["fixtureDifferences"] = "Synthetic values, representative record and byte distribution",
                ["componentResult"] = "passed", ["installationResult"] = "passed", ["endToEndResult"] = "timeout",
                ["artifactPath"] = "receipt.txt", ["artifactSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "receipt.txt")))) };
            File.WriteAllText(profile, JsonSerializer.Serialize(fields));
            Assert.Contains(WorkloadEvidenceReview.Review(root, profile, true), item => item.Contains("has not passed"));
            fields["endToEndResult"] = "passed";
            File.WriteAllText(profile, JsonSerializer.Serialize(fields));
            Assert.Empty(WorkloadEvidenceReview.Review(root, profile, true));
            File.AppendAllText(Path.Combine(root, "receipt.txt"), "changed");
            Assert.Contains(WorkloadEvidenceReview.Review(root, profile, true), item => item.Contains("digest"));
            File.WriteAllText(profile, "{\"schemaVersion\":1,\"applicable\":false,\"rationale\":\"TODO\"}");
            Assert.NotEmpty(WorkloadEvidenceReview.Review(root, profile, true));
            Assert.True(WorkloadEvidenceReview.SuggestsVolumeSensitiveWork("Build a large recorded dataset"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
