using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    private static void AssertFeatureIntakePreservesActivatedProduct(string authority, string fixturePath,
        Func<string[], (int ExitCode, string Output, string Error)> run)
    {
        var sessionPath = Path.Combine(authority, ".cis/local/definition-wizard/session.json");
        var session = File.ReadAllBytes(sessionPath);
        var originals = Directory.GetFiles(Path.Combine(authority, "docs/cis"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "catalog.yml").ToDictionary(path => path, File.ReadAllBytes);
        var source = Path.Combine(fixturePath, "referral-brd.md");
        File.WriteAllText(source, "# Referral business requirements\n\nCustomers shall refer others.\n\n## Open decisions\n\n1. Who manages referrals?\n");
        var request = new CisFeatureIntakeRequest("Referrals", "referrals", source, "new",
            Path.Combine(fixturePath, "referrals"), "docs/cis", [], "Owner");
        var input = Path.Combine(fixturePath, "intake.json");
        File.WriteAllText(input, JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var preview = JsonDocument.Parse(Success("brd", "feature", "intake", "--input", input, "--dry-run"));
        Success("brd", "feature", "intake", "--input", input, "--yes", "--expected-plan",
            preview.RootElement.GetProperty("plan").GetProperty("planHash").GetString()!);
        Success("graph", "build");
        AssertProductCurrent();
        var registry = new WorkspaceRegistry(new CisRepositoryContextResolver());
        Assert.Contains(registry.Resolve(authority).Workspace!.DeliveryRepositories, repo => repo.Id == "referrals");
        using var feature = JsonDocument.Parse(Success("brd", "feature", "wizard", "status", "--slug", "referrals"));
        Assert.True(feature.RootElement.GetProperty("baselineCurrent").GetBoolean());
        Assert.False(feature.RootElement.GetProperty("reviewed").GetBoolean());
        Assert.Contains("referrals", Success("brd", "feature", "wizard", "list"));

        // Reimport remains proposed feature evidence, including all immutable history.
        File.AppendAllText(source, "\n## Additional scope\n\nCapture referral acceptance before the handoff.\n");
        using var updatedPreview = JsonDocument.Parse(Success("brd", "feature", "wizard", "reimport",
            "--slug", "referrals", "--source", source, "--actor", "Owner", "--dry-run"));
        Success("brd", "feature", "wizard", "reimport", "--slug", "referrals", "--source", source,
            "--actor", "Owner", "--yes", "--expected-plan", updatedPreview.RootElement.GetProperty("plan").GetProperty("planHash").GetString()!);
        Success("graph", "build");
        AssertProductCurrent();

        // New implementation must re-enter baseline checks despite the pending intake.
        var implementation = Path.Combine(request.RepositoryPath, "Referral.cs");
        File.WriteAllText(implementation, "public class Referral { public string Code { get; set; } = string.Empty; }");
        Success("graph", "build");
        using (var changed = JsonDocument.Parse(Success("definition", "status")))
        {
            var business = changed.RootElement.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("id").GetString() == "business");
            Assert.False(business.GetProperty("current").GetBoolean());
            Assert.Contains("referrals", business.GetProperty("issues").ToString());
        }
        File.Delete(implementation);
        Success("graph", "build");
        AssertProductCurrent();

        // Read projections preserve every original document and approval byte-for-byte.
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(session, File.ReadAllBytes(sessionPath));

        string Success(params string[] args)
        {
            var result = run(args);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            return result.Output;
        }
        void AssertProductCurrent()
        {
            using var status = JsonDocument.Parse(Success("definition", "status"));
            Assert.All(status.RootElement.GetProperty("pages").EnumerateArray(), page =>
            {
                Assert.True(page.GetProperty("complete").GetBoolean(), page.ToString());
                Assert.True(page.GetProperty("current").GetBoolean(), page.ToString());
                Assert.Empty(page.GetProperty("issues").EnumerateArray());
            });
            Assert.True(status.RootElement.GetProperty("technicalQuestions").GetProperty("complete").GetBoolean());
            Assert.True(status.RootElement.GetProperty("uiQuestions").GetProperty("complete").GetBoolean());
        }
    }
}
