using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData("task", "Result", "", "")]
    [InlineData("context", "Request", "", "")]
    [InlineData("task", "Result", "nameof(request) + typeof(Exception).Name", "")]
    [InlineData("context", "Request", "nameof(request) + typeof(List<Order>).Name", "")]
    [InlineData("task", "Result", "", "using Result = global::Synthetic.Orders.Result;")]
    [InlineData("task", "Result", "", "using Result = Synthetic.Orders.Result<Guid>;")]
    public void DeliveryMemberDependency_BoundedLocalCandidatesPreserveIntentAndCacheUntilCandidateChanges(
        string variable, string member, string frameworkContext, string alias)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var context = frameworkContext.Length == 0 ? "" : "var request = " + variable + "; _ = " + frameworkContext + "; ";
        var imports = alias.Length == 0 ? "" : alias + "\n";
        var archive = AddDeliveryCode(f, "backend", "archive.cs", imports + "using System;\nusing System.Collections.Generic;\nnamespace Synthetic; public class Archive { public int getDocumentVersion(Holder " + variable
            + ") { " + context + "return " + variable + "." + member + "; } private record " + member + "(int Value); }");
        var root = Path.GetDirectoryName(archive)!;
        if (frameworkContext.Contains("Order", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(root, "order.cs"), "namespace Synthetic; public record Order(int Value);");
        File.WriteAllText(Path.Combine(root, "holder.cs"), "namespace Synthetic; public sealed class Holder { public int " + member + " => 1; }");
        var candidate = Path.Combine(root, "other.cs");
        var original = alias.Length == 0 ? "namespace Synthetic; public class Weather { private record " + member + "(int Value); }"
            : "namespace Synthetic.Orders; public record Result" + (alias.Contains("<Guid>", StringComparison.Ordinal) ? "<T>" : "") + "(int Value);";
        File.WriteAllText(candidate, original);
        CreateIntake(f); var generation = new DeliveryGeneration { Mode = "document-grounded" }; var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals"); var prepared = service.Delivery(f.Authority, "referrals", true, wizard.Revision);
        Assert.Empty(prepared.Errors); Assert.Equal("unknown", prepared.Stories.Single().AssessmentState); Assert.Equal(1, generation.Calls);
        var choice = new CisFeatureDeliveryReviewRequest(prepared.Stories.Single().Id, "extend", ["backend"],
            "Retain this planning choice while verifying the bounded member/type interpretation.", ["backend/archive.cs"], prepared.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        var reviewed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(reviewed.ReviewCurrent); Assert.Equal("unknown", reviewed.AssessmentState); Assert.Contains("candidate", reviewed.AssessmentReason);
        File.WriteAllText(Path.Combine(root, "calendar.cs"), "namespace Synthetic; public class Calendar { public int GetYear() => 2026; }");
        var retained = DeliveryService(f, generation).Delivery(f.Authority, "referrals", true, saved.Revision);
        Assert.Empty(retained.Errors); Assert.True(retained.Cached); Assert.True(retained.Stories.Single().ReviewCurrent);
        Assert.Equal(reviewed.Review!.SavedAt, retained.Stories.Single().Review!.SavedAt); Assert.Equal(1, generation.Calls);
        File.WriteAllText(candidate, original + "\n// Changed candidate context.\n");
        var changed = service.Delivery(f.Authority, "referrals", false);
        Assert.False(changed.Stories.Single().ReviewCurrent); Assert.Equal("needs-review", changed.Stories.Single().ReviewState);
        Assert.Contains("other.cs", changed.Stories.Single().ReviewReason);
        var renewed = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", saved.Revision!)
        { DeliveryReview = choice with { ExpectedInputHash = changed.InputHash! } });
        Assert.Empty(renewed.Errors);
        Assert.True(service.Delivery(f.Authority, "referrals", false).Stories.Single().ReviewCurrent);
    }
}
