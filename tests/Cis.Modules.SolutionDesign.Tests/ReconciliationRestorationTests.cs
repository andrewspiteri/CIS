using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed partial class SolutionDesignWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconciliationTransaction_AttemptsEveryRestorationAndRetainsRecoverableOriginals(bool failRestoration)
    {
        using var fixture = ReconciliationFixture(true);
        var paths = new[] { fixture.DesignPath, fixture.SheetPath, Path.Combine(fixture.Root, "docs/catalog.yml") };
        var originals = paths.ToDictionary(path => path, File.ReadAllBytes);
        var proposed = paths.ToDictionary(path => path, _ => new byte[] { 1, 2, 3 });
        var writes = 0;
        var restored = new List<string>();
        var transaction = new ReconciliationFileTransaction((path, bytes) =>
        {
            if (++writes == 3) throw new IOException("Injected final write failure");
            if (bytes.SequenceEqual(originals[path]))
            {
                restored.Add(path);
                if (failRestoration && path == paths[0]) throw new IOException("Injected first restoration failure");
            }
            File.WriteAllBytes(path, bytes);
        });
        var backup = Path.Combine(fixture.Root, "backup");

        var errors = transaction.Apply(backup, originals, proposed, () => true);

        Assert.NotEmpty(errors);
        Assert.Contains(paths[0], restored);
        Assert.Contains(paths[1], restored);
        Assert.Equal(originals[paths[1]], File.ReadAllBytes(paths[1]));
        Assert.Equal(originals[paths[2]], File.ReadAllBytes(paths[2]));
        for (var index = 0; index < paths.Length; index++)
            Assert.Equal(originals[paths[index]], File.ReadAllBytes(Path.Combine(backup, $"{index}.original")));
        Assert.True(File.Exists(Path.Combine(backup, "inventory.json")));
        if (failRestoration)
        {
            Assert.Contains(errors, error => error.StartsWith("Restoration incomplete for " + paths[0], StringComparison.Ordinal));
            Assert.Contains(errors, error => error.StartsWith("Recover original files from ", StringComparison.Ordinal));
            Assert.Equal(proposed[paths[0]], File.ReadAllBytes(paths[0]));
        }
        else Assert.Equal(originals[paths[0]], File.ReadAllBytes(paths[0]));
    }

    [Fact]
    public void Reconcile_ChangesOnlyCanonicalMetadataAndPreservesMatchingExampleAndCommentKeys()
    {
        using var fixture = ReconciliationFixture(false);
        const string example = """

            ## Human evidence examples

            ```yaml
            status: Active
            last_reviewed: 2000-01-01
            cis:
              technical_intent_hash: example-source
              approved_by: example-person
              approved_at: example-time
              approval_reason: example-reason
              approved_bundle_hash: example-hash
            ```

            <!-- protected evidence
            status: Active
              technical_intent_hash: protected-source
              approved_bundle_hash: protected-hash
            -->
            """;
        foreach (var path in new[] { fixture.DesignPath, fixture.SheetPath })
        {
            File.AppendAllText(path, example);
            // Identically named nested keys outside cis are not lifecycle metadata.
            var content = File.ReadAllText(path);
            File.WriteAllText(path, content.Insert(content.IndexOf("cis:\n", StringComparison.Ordinal), "example_metadata:\n  approved_by: retained-person\n"));
        }
        Assert.Equal(0, fixture.Service.Approve(fixture.Root, "Owner", "Approved examples").ExitCode);
        File.AppendAllText(fixture.TechnicalIntentPath, "\nStandards changed.\n");

        var result = Reconcile(fixture);

        Assert.Equal(0, result.ExitCode);
        foreach (var path in new[] { fixture.DesignPath, fixture.SheetPath })
        {
            var content = File.ReadAllText(path);
            Assert.Contains(example.Trim(), content, StringComparison.Ordinal);
            Assert.Contains("example_metadata:\n  approved_by: retained-person", content, StringComparison.Ordinal);
        }
    }
}
