using System.Security.Cryptography;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData("fresh")]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("uncovered")]
    [InlineData("owners")]
    [InlineData("outside")]
    public void DeliveryCompilerBindingDistinguishesPrivateStateOwnersAndKeepsUnknownWhenCoverageIsMissing(string mode)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.cs", "namespace Synthetic; public class Archive { public int getDocumentVersion() { return new State(Policy.Days).Value; } private record State(int Value); private record Result(int Value); }");
        var root = Path.GetDirectoryName(archive)!;
        var weather = Path.Combine(root, "weather.cs");
        File.WriteAllText(weather, "namespace Synthetic; public class Weather { public int getForecast() { return new State(1).Value; } private record State(int Value); }");
        var policy = Path.Combine(root, "policy.cs"); File.WriteAllText(policy, "namespace Synthetic; public static class Policy { public static int Days => 7; }");
        if (mode == "uncovered")
        {
            File.WriteAllText(archive, "using System; namespace Synthetic; public class Archive { public string getDocumentVersion() { _ = new State(1); return ((ObsoleteAttribute)typeof(Other.State).GetCustomAttributes(false)[0]).Message!; } private record State(int Value); private record Result(int Value); }");
            File.WriteAllText(Path.Combine(root, "third.cs"), "namespace Other; [System.Obsolete(\"v1\")] public class State {}");
        }
        if (mode == "outside")
            File.WriteAllText(archive, "using System; namespace Synthetic; public class Archive { public string getDocumentVersion() { _ = new State(1); return typeof(Foreign.State).FullName!; } private record State(int Value); private record Result(int Value); }");
        if (mode == "owners") File.AppendAllText(archive, " public class OtherOwner { public int OtherMethod() => new State(1).Value; private record State(int Value); }");
        File.WriteAllText(Path.Combine(root, "component-profile.md"), "# backend\nRoot: .\nRole: implementation\n");
        CreateIntake(f); var graph = new NativeCompilerOwnershipGraph(root, mode == "owners") { Partial = mode == "partial" };
        var generation = new DeliveryGeneration { Mode = "document-grounded" };
        var service = DeliveryService(f, generation, graphReader: mode == "missing" ? null : graph);
        var wizard = service.Wizard(f.Authority, "referrals");
        var prepared = service.Delivery(f.Authority, "referrals", true, wizard.Revision);
        Assert.Empty(prepared.Errors);
        var choice = new CisFeatureDeliveryReviewRequest(prepared.Stories.Single().Id, "extend", ["backend"],
            "Use the archive's private state and its retention policy.", ["backend/archive.cs"], prepared.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        var reviewed = service.Delivery(f.Authority, "referrals", false);
        if (mode is "partial" or "outside")
        {
            Assert.False(reviewed.Stories.Single().ReviewCurrent); Assert.Equal("unknown", reviewed.Stories.Single().ReviewState);
            Assert.Contains(mode == "partial" ? "graph" : "outside", reviewed.Stories.Single().ReviewReason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(reviewed.Warnings, warning => warning.Contains("Resolve the affected story's coverage reason", StringComparison.Ordinal));
            return;
        }
        if (mode != "fresh")
        {
            var retained = reviewed.Stories.Single();
            Assert.True(retained.ReviewCurrent); Assert.Equal("current", retained.ReviewState);
            Assert.Equal("unknown", retained.AssessmentState);
            Assert.Contains("candidate files", retained.AssessmentReason, StringComparison.Ordinal);
            Assert.Equal("Reviewer", retained.Review!.Actor);
            if (mode == "uncovered")
            {
                File.WriteAllText(Path.Combine(root, "third.cs"), "namespace Other; [System.Obsolete(\"v2\")] public class State {}");
                graph.Rebuild();
                var changedCandidate = service.Delivery(f.Authority, "referrals", false).Stories.Single();
                Assert.False(changedCandidate.ReviewCurrent); Assert.Equal("needs-review", changedCandidate.ReviewState);
                Assert.Contains("third.cs", changedCandidate.ReviewReason, StringComparison.Ordinal);
                Assert.Equal(retained.Review.SavedAt, changedCandidate.Review!.SavedAt);
            }
            return;
        }
        Assert.True(reviewed.Stories.Single().ReviewCurrent);
        File.WriteAllText(weather, File.ReadAllText(weather).Replace("new State(1)", "new State(2)", StringComparison.Ordinal));
        graph.Freshness = "stale";
        var unchanged = service.Delivery(f.Authority, "referrals", true, saved.Revision);
        Assert.Empty(unchanged.Errors); Assert.True(unchanged.Cached); Assert.True(unchanged.Stories.Single().ReviewCurrent);
        Assert.Equal(reviewed.Stories.Single().Review!.SavedAt, unchanged.Stories.Single().Review!.SavedAt); Assert.Equal(1, generation.Calls);
        File.WriteAllText(policy, "namespace Synthetic; public static class Policy { public static int Days => 9; }");
        graph.Rebuild();
        var changed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Equal("needs-review", changed.ReviewState); Assert.Contains("policy.cs", changed.ReviewReason);
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("alias")]
    [InlineData("project")]
    [InlineData("namespace")]
    public void UnboundDependencyReachRetainsResponsesWithoutMakingReviewOrCompletionCurrent(string kind)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var code = kind switch
        {
            "alias" => "import { Policy } from '@app/retention'; export class Archive { getDocumentVersion() { return Policy.days; } }",
            "project" or "namespace" => "using Foreign.Retention; public class Archive { public int getDocumentVersion() => Policy.Days; }",
            _ => "export class Archive { async getDocumentVersion() { return import(targetPath); } }"
        };
        var name = kind is "project" or "namespace" ? "archive.cs" : "archive.ts";
        var archive = AddDeliveryCode(f, "backend", name, code);
        var root = Path.GetDirectoryName(archive)!;
        if (kind == "alias") File.WriteAllText(Path.Combine(root, "package.json"), "{\"dependencies\":{\"unrelated-sdk\":\"1.0.0\"}}");
        if (kind == "project")
        {
            Directory.CreateDirectory(Path.Combine(f.Root, "unowned"));
            File.WriteAllText(Path.Combine(f.Root, "unowned/Retention.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(root, "Archive.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../unowned/Retention.csproj\" /></ItemGroup></Project>");
        }
        if (kind == "namespace") AddDeliveryCode(f, "policy-contract", "policy.cs", "namespace Foreign.Retention; public static class Policy { public static int Days => 7; }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var executor = new RepeatingFindingExecutor(); var service = DeliveryService(f, new StoryTaskGeneration(), executor);
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Resolve the dependency target before relying on archive implementation.", ["backend/" + name], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        var reviewed = service.Delivery(f.Authority, "referrals", false);
        Assert.False(reviewed.Stories.Single().ReviewCurrent); Assert.Equal("unknown", reviewed.Stories.Single().ReviewState);
        Assert.Contains(reviewed.Warnings, warning => warning.Contains("Resolve the affected story's coverage reason", StringComparison.Ordinal));
        var id = reviewed.Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var plan = service.Story(f.Authority, "referrals", id, true, read.InputHash);
        var current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", plan.PlanHash!, plan.Revision!, "Reviewer", "Reviewed task contract");
        var preview = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        current = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, current.PlanHash, current.Revision,
            preview.ExecutionPlan!.Hash, "Reviewer", true, TestContext.Current.CancellationToken);
        Assert.Empty(current.Errors);
        var finding = current.TaskProgress[0].ReviewFeedback!.First();
        current = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Reviewer", new() { [finding.Id] = "Retain this response until target coverage is established." })
        { ExpectedBasisHashes = new Dictionary<string, string> { [finding.Id] = finding.BasisHash! } });
        Assert.Empty(current.Errors);
        var response = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Id == finding.Id);
        Assert.Equal("unknown", response.ApplicabilityState); Assert.Null(response.Answer); Assert.NotNull(response.RetainedAnswer);
        Assert.False(current.TaskProgress[0].Execution!.Applied); Assert.False(current.TaskProgress[0].CanComplete); Assert.Equal("Blocked", current.TaskProgress[1].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedDependencyManifestsRemainUnboundWithoutReadingTheirOutsideOwnedTargets(bool project)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var name = project ? "archive.cs" : "archive.ts";
        var code = project ? "using Foreign.Retention; public class Archive { public int getDocumentVersion() => Policy.Days; }"
            : "import { Version } from 'archive-sdk'; export class Archive { getDocumentVersion() { return Version.value; } }";
        var archive = AddDeliveryCode(f, "backend", name, code); var root = Path.GetDirectoryName(archive)!;
        var outside = Path.Combine(f.Root, "unowned-manifests"); Directory.CreateDirectory(outside);
        var target = Path.Combine(outside, project ? "Linked.csproj" : "package.json");
        File.WriteAllText(target, project ? "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Foreign.Retention\" Version=\"1.0.0\" /></ItemGroup></Project>"
            : "{\"dependencies\":{\"archive-sdk\":\"1.0.0\"}}");
        try { File.CreateSymbolicLink(Path.Combine(root, project ? "Linked.csproj" : "package.json"), target); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException
            || error is IOException io && (io.HResult & 0xffff) is 50 or 1314)
        {
            Assert.Skip("The platform does not permit creating this file symlink."); return;
        }
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Establish owned dependency coverage before relying on the archive.", ["backend/" + name], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        var reviewed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(reviewed.ReviewCurrent); Assert.Equal("unknown", reviewed.ReviewState); Assert.Contains("unbound", reviewed.ReviewReason);
        Assert.DoesNotContain(reviewed.Review!.Basis!, item => item.Key == "configuration/backend/" + (project ? "Linked.csproj" : "package.json"));
        File.WriteAllText(target, "changed outside-owned contract");
        Assert.False(service.Delivery(f.Authority, "referrals", false).Stories.Single().ReviewCurrent);
    }

    private sealed class NativeCompilerOwnershipGraph : ICisGraphSnapshotReader
    {
        private readonly string root;
        private Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        private string buildId = "";
        private readonly bool multipleOwners;
        public string Freshness { get; set; } = "fresh";
        public bool Partial { get; set; }
        public NativeCompilerOwnershipGraph(string root, bool multipleOwners = false) { this.root = root; this.multipleOwners = multipleOwners; Rebuild(); }
        public void Rebuild()
        {
            hashes = new[] { "archive.cs", "weather.cs", "policy.cs", "component-profile.md", "third.cs" }.Where(path => File.Exists(Path.Combine(root, path))).ToDictionary(path => path,
                path => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path)))).ToLowerInvariant(), StringComparer.Ordinal);
            buildId = Guid.NewGuid().ToString("N"); Freshness = "fresh";
        }
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            if (!string.Equals(Path.GetFullPath(root), Path.GetFullPath(repositoryPath), StringComparison.OrdinalIgnoreCase))
                return new("missing", 4, repositoryPath, "missing", null, null, [], [], []);
            CisGraphEvidence Evidence(string path, bool call = false) => new("repository-source", path, "line", "1", hashes[path],
                call ? "compiler-call" : "compiler-binding", call ? "cis.csharp.semantic-facts/3" : "cis.csharp.compiler/6", "high", null);
            CisGraphNode Symbol(string key, string path, string kind, string qualified, string signature) => new(key, "backend", "symbol", kind, key, qualified,
                ["csharp", "compiler-bound"], "derived", "active", [new(path, "line", "1")],
                new Dictionary<string, string> { ["binding"] = "compiler", ["external"] = "false", ["language"] = "csharp", ["component"] = "backend", ["qualifiedName"] = qualified, ["signature"] = signature }, [Evidence(path)]);
            var nodes = new List<CisGraphNode>
            {
                new("component", "backend", "component", "implementation", "backend", "backend", [], "declared", "active", [new("component-profile.md", "heading", "backend")],
                    new Dictionary<string, string> { ["root"] = ".", ["roles"] = "implementation" }, [])
            };
            var edges = new List<CisGraphEdge>();
            foreach (var owner in new[] { "Archive", "Weather" })
            {
                var path = owner.ToLowerInvariant() + ".cs"; var method = owner == "Archive" ? "getDocumentVersion" : "getForecast";
                nodes.Add(Symbol(owner + "-owner", path, "class", "Synthetic." + owner, "Synthetic." + owner));
                nodes.Add(Symbol(owner + "-type", path, "record", "Synthetic." + owner + ".State", "Synthetic." + owner + ".State"));
                nodes.Add(Symbol(owner + "-ctor", path, "constructor", "Synthetic." + owner + ".State.State(int)", "Synthetic." + owner + ".State.State(int)"));
                nodes.Add(Symbol(owner + "-method", path, "method", "Synthetic." + owner + "." + method, (owner == "Archive" && hashes.ContainsKey("third.cs") ? "string" : "int") + " Synthetic." + owner + "." + method + "()"));
                edges.Add(new(owner + "-call", "calls", owner + "-method", owner + "-ctor", "discovered", "high", [Evidence(path, true)], new Dictionary<string, string> { ["scope"] = "repository" }));
            }
            nodes.Add(Symbol("archive-result", "archive.cs", "record", "Synthetic.Archive.Result", "Synthetic.Archive.Result"));
            if (multipleOwners)
            {
                nodes.Add(Symbol("other-owner", "archive.cs", "class", "Synthetic.OtherOwner", "Synthetic.OtherOwner"));
                nodes.Add(Symbol("other-state", "archive.cs", "record", "Synthetic.OtherOwner.State", "Synthetic.OtherOwner.State"));
                nodes.Add(Symbol("other-ctor", "archive.cs", "constructor", "Synthetic.OtherOwner.State.State(int)", "Synthetic.OtherOwner.State.State(int)"));
                nodes.Add(Symbol("other-method", "archive.cs", "method", "Synthetic.OtherOwner.OtherMethod", "int Synthetic.OtherOwner.OtherMethod()"));
                edges.Add(new("other-call", "calls", "other-method", "other-ctor", "discovered", "high", [Evidence("archive.cs", true)], new Dictionary<string, string> { ["scope"] = "repository" }));
            }
            nodes.Add(Symbol("policy-type", "policy.cs", "class", "Synthetic.Policy", "Synthetic.Policy"));
            if (hashes.ContainsKey("third.cs")) nodes.Add(Symbol("third-type", "third.cs", "class", "Other.State", "Other.State"));
            var build = new CisGraphBuildMetadata(buildId, "backend", "unchanged-head", true, Partial ? "partial" : "complete");
            var graph = new CisGraphDocument(2, build, nodes, edges, new(0, Partial ? 1 : 0, 0));
            var manifest = new CisGraphManifest(2, buildId, "backend", "docs/cis", "unchanged-head", true,
                hashes.Select(item => new CisGraphInput(item.Key, item.Value)).ToArray(), ["cis.csharp.compiler/6", "cis.csharp.semantic-facts/3"]);
            return new(Freshness == "fresh" ? "available" : "stale", Freshness == "fresh" ? 0 : 5, repositoryPath, Freshness, graph, manifest,
                ["compiler.symbols", "compiler.calls"], Freshness == "fresh" ? [] : ["Stale graph input: weather.cs"], []);
        }
    }
}
