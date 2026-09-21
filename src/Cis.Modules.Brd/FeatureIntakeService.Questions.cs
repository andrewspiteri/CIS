using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record ReviewQuestion(string Id, string Label, string SourceTerms, string BaselineTerms = "");

    private static IReadOnlyList<ReviewQuestion> QuestionsFor(string page) => page switch
    {
        "technical" => [
            new("technical-platform", "Which existing technologies, services and deployment conventions will this feature use?", "technology stack|runtime|hosting|deployment", "stack|runtime|language|framework|hosting|deployment"),
            new("technical-decisions", "Which implementation choices still need to be resolved?", "technical specification|technical design|implementation parameters|outstanding technical|supplier"),
            new("technical-security", "How will identity, tenant isolation and sensitive data be protected?", "security|identity|access control|privacy", "security|identity|access|privacy"),
            new("technical-operations", "What are the performance, availability, monitoring and recovery requirements?", "performance|availability|reliability|monitoring|observability|disaster|recovery", "performance|availability|reliability|observability|recovery")],
        "architecture" => [
            new("architecture-boundary", "Which components belong to this feature, and what remains in the existing product or external systems?", "system context|platform boundar|responsibilit|out.of.scope|scope exclusion|organisation model", "system context|boundar|container|component"),
            new("architecture-flows", "How will the components interact during the main journeys and failure cases?", "journey|hand.off|handoff|hosted|reconciliation|redirect|webhook", "interaction|integration|flow"),
            new("architecture-views", "Which C4 diagrams need to change, and what must each view show?", "c4|architecture diagram|container diagram|component diagram")],
        "contracts" => [
            new("contracts-interfaces", "Which API, event and webhook contracts are added or changed, and who provides and consumes them?", "api|webhook|event|integration contract", "api|event|contract"),
            new("contracts-data", "Which records, identifiers and data relationships must the dictionaries describe?", "data model|entity|entities|data dictionary|identifiers|snapshots|correlation", "entit|data model|data dictionary"),
            new("contracts-ownership", "Who owns each integration and how are permissions, compatibility and failure handling defined?", "ownership|authoritative|bank.policy|bank.*relationship|access control|reconcil|freshness", "ownership|permission|compatibility|failure")],
        "experience" => [
            new("experience-journeys", "Which public, customer and backoffice journeys change?", "journey|hosted|direct mode|capture|administration|user experience"),
            new("experience-controls", "Which screens, controls, validation messages and empty or failure states are required?", "form|field|validation|duplicate|error|screen|controls"),
            new("experience-baseline", "How will the existing visual direction, responsive behaviour and accessibility requirements apply?", "accessibility|responsive|localisation|browser|hosted", "visual|design tokens|component|responsive|accessibility")],
        "delivery" => [
            new("delivery-ownership", "Which capabilities stay in existing repositories, and what should this feature add or extend?", "authoritative ownership|source ownership|existing capability|existing implementation|responsibilit"),
            new("delivery-stories-foundation", "Foundation — required regardless of release scope", ""),
            new("delivery-stories-mvp", "MVP — required for the first release", ""),
            new("delivery-stories-post-mvp", "Post-MVP — later delivery", ""),
            new("delivery-boundary", "What is included in the first release, and what is explicitly deferred?", "mvp|release|in.scope|out.of.scope|future|exclusion"),
            new("delivery-dependencies", "Which repository work can proceed independently, and which contracts or decisions must come first?", "dependenc|assumption|prerequisite|technical specification"),
            new("delivery-acceptance", "What measurable acceptance checks prove that the feature works?", "acceptance|testing|test scenario|success criteria"),
            new("delivery-rollout", "How will migration, rollout, rollback and operational handover be handled?", "migration|rollout|rollback|deployment|support model|recovery", "migration|rollout|rollback|deployment|recovery")],
        _ => []
    };

    private static IReadOnlyList<CisFeatureWizardField> StructuredFields(WizardState state, string page, PageReview? saved)
    {
        // Older CIS versions recorded one narrative per page. Keep that explicit review
        // valid; never fabricate separate human answers from it or rewrite it on read.
        var legacyNarrative = HasAnswer(saved?.Answers.GetValueOrDefault("summary"))
            && !saved!.Answers.Any(pair => pair.Key != "summary" && HasAnswer(pair.Value));
        var stories = page == "delivery" ? SuggestStories(state.Source) : null;
        var fields = QuestionsFor(page).Select(question => new CisFeatureWizardField(question.Id, question.Label,
            stories?.GetValueOrDefault(question.Id) ?? SuggestAnswer(state, page, question), saved?.Answers.GetValueOrDefault(question.Id), !legacyNarrative)).ToList();
        fields.Insert(0, new("summary", legacyNarrative ? "Previously reviewed narrative" : "Additional review notes (optional)", "",
            saved?.Answers.GetValueOrDefault("summary"), legacyNarrative));
        return fields;
    }

    private static string SuggestAnswer(WizardState state, string page, ReviewQuestion question)
    {
        var excerpt = SourceExcerpt(state.Source, question.SourceTerms);
        if (excerpt.Length > 0) return excerpt;
        if (question.Id == "architecture-views")
            return $"Proposed C4 coverage for {state.Record.Plan.Title}:\n\n"
                + "- System context: show the feature's users, the existing product and external integration partners.\n"
                + "- Containers: show the applications, services and data stores involved, their owners and the links between them.\n"
                + "- Components: show the responsibilities inside the feature's implementation repository and its integration adapters.\n"
                + "- Dynamic views: show the main customer journey, asynchronous handoffs and important failure paths.\n\n"
                + "Identify the existing views to update and any new views needed before implementation.";
        if (question.BaselineTerms.Length > 0)
        {
            var definition = ReviewPages.Single(item => item.Id == page);
            foreach (var document in definition.Documents)
            {
                var path = Path.Combine(state.Authority.RepositoryPath, state.Authority.DocumentationRoot, document);
                if (!SafeAbsolutePath(path) || !File.Exists(path) || new FileInfo(path).Length > 2_097_152) continue;
                var text = CisReadScope.Read(typeof(FeatureIntakeService), "feature-question-context", path, () => File.ReadAllText(path, Encoding.UTF8));
                excerpt = SourceExcerpt(text, question.BaselineTerms);
                if (excerpt.Length > 0) return "Existing product direction to review for this feature:\n\n" + excerpt;
            }
        }
        // Some BRDs keep delivery/technical parameters as bullets rather than sections.
        // Preserve those actual statements instead of presenting an unrelated section.
        var plainSource = Regex.Replace(state.Source, @"<!--.*?-->", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var relevantLines = plainSource.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0
            && !line.StartsWith('#') && Regex.IsMatch(line, question.SourceTerms,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Take(12).ToArray();
        return relevantLines.Length > 0 ? BoundExcerpt(string.Join("\n\n", relevantLines), 3000) : "";
    }
}
