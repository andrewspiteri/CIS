using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private const string ReviewStart = "<!-- cis:feature-review:start -->";
    private const string ReviewEnd = "<!-- cis:feature-review:end -->";
    private const string ReviewData = "<!-- cis:feature-review-data\n";
    private static readonly (string Id, string Title, string Prompt, string SourceTerms, string[] Documents)[] ReviewPages =
    [
        ("business", "Business definition", "Describe the feature's users, outcomes, included scope and exclusions.", "purpose|overview|scope|business objectives", ["specs/business-requirements.md"]),
        ("technical", "Technical direction", "Describe the technical choices this feature inherits and any changes it proposes.", "technical|non.functional|security|architecture", ["specs/technical-intent-spec.md"]),
        ("architecture", "Solution architecture and diagrams", "Describe the feature's components, responsibilities and interactions. Identify the C4 views that need to change.", "architecture|integration|system context", ["architecture/overall-solution-design.md", "architecture/high-level-architecture-diagrams.md", "references/component-sheet.md"]),
        ("contracts", "Integrations and dictionaries", "Describe API, event, data, permission and ownership changes, including the responsibility of each integration repository.", "integration|api|data model|catalogue|handoff|hand.off", ["references/dictionary-index.md", "references/api-dictionary.md", "references/data-dictionary.md", "references/event-dictionary.md"]),
        ("experience", "Experience direction and UI impact", "Identify affected public, customer or backoffice journeys and controls. If no UI changes are required, explain why.", "user experience|ui |screen|capture form|user journey|accessibility", ["design/ui-direction.md", "design/ui-system-preview.md"]),
        ("delivery", "Delivery and acceptance", "Describe the release boundary, dependencies, migration, rollout and measurable acceptance criteria.", "acceptance|rollout|delivery|mvp|testing|migration", ["plans/high-level-backlog.md"]),
    ];

    public IReadOnlyList<CisFeatureIntakePlan> Requests(string workspacePath)
    {
        var resolved = workspaces.Resolve(workspacePath);
        var authority = resolved.Workspace?.AuthorityRepository;
        if (!resolved.IsSuccess || authority is null) return [];
        var folder = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot, "specs", "feature-requests");
        if (!SafeAbsolutePath(folder) || !Directory.Exists(folder)) return [];
        return Directory.EnumerateDirectories(folder).Take(200).Where(SafeAbsolutePath)
            .Select(directory => Path.Combine(directory, "request.md")).Where(path => File.Exists(path) && SafeAbsolutePath(path))
            .Select(path => { try { return ReadRecord(path)?.Plan; } catch (Exception e) when (e is IOException or JsonException) { return null; } })
            .OfType<CisFeatureIntakePlan>().OrderBy(plan => plan.Title, StringComparer.Ordinal).ToArray();
    }

    public CisFeatureWizardResult Wizard(string workspacePath, string slug)
    {
        try
        {
            var state = ReadWizard(workspacePath, slug);
            return ProjectWizard(state);
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { return WizardError(e.Message); }
    }

    public CisFeatureWizardResult SaveWizard(string workspacePath, CisFeatureWizardAnswer answer)
    {
        try
        {
            if (answer is null || !SingleLine(answer.Actor, 200) || answer.Answers is null || answer.Answers.Count > 202
                || answer.Answers.Any(pair => pair.Value is null || pair.Value.Length > 24_000 || pair.Value.Contains('\0')
                    || pair.Value.Contains("<!-- cis:", StringComparison.Ordinal)))
                return WizardError("Provide a human actor and bounded answer text.");
            var state = ReadWizard(workspacePath, answer.Slug);
            var status = ProjectWizard(state);
            var page = status.Pages.SingleOrDefault(page => page.Id == answer.Page && page.Id != "foundation");
            if (page is null) return WizardError("Select a feature-definition page.");
            if (answer.ExpectedRevision != status.Revision) return WizardError("This feature or its baseline changed. Refresh the feature wizard before saving; your edits have been kept in the form.");
            if (answer.Answers.Keys.Any(id => !page.Fields.Any(field => field.Id == id))) return WizardError("The page contains an unknown answer field.");
            if (answer.RepositoryWork is not null)
            {
                if (answer.Page != "delivery") return WizardError("Repository work belongs to the delivery page.");
                var workErrors = ValidateRepositoryWork(state, answer.RepositoryWork);
                if (workErrors.Count > 0) return WizardError(string.Join("\n", workErrors));
            }
            if (answer.Page == "review" && (status.Pages.Any(page => page.Id != "review" && !page.Complete) || !status.BaselineCurrent))
                return WizardError("Complete the feature pages and reconcile the current product baseline before recording the definition review.");

            var lockPath = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-intake.lock");
            if (!SafeAbsolutePath(lockPath)) return WizardError("The feature lock uses an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.ReadAllText(state.RequestPath).Replace("\r\n", "\n", StringComparison.Ordinal) != state.Content
                || state.Inputs.Any(input => FileHash(input.Key) != input.Value))
                return WizardError("The feature changed while saving. Refresh before retrying.");
            var screenReview = answer.ScreenReview is null ? null : CheckScreenReview(state, answer);
            var values = state.Review.Pages.ToDictionary(pair => pair.Key,
                pair => pair.Value.Binding == state.LegacyBinding ? pair.Value with { Binding = state.Binding } : pair.Value,
                StringComparer.Ordinal);
            var answers = new Dictionary<string, string>(values.GetValueOrDefault(answer.Page)?.Answers ?? [], StringComparer.Ordinal);
            foreach (var pair in answer.Answers) answers[pair.Key] = pair.Value.Trim();
            var now = DateTimeOffset.UtcNow.ToString("O");
            if (screenReview is null) values[answer.Page] = new(answers, answer.Actor.Trim(), now, state.Binding);
            // Every substantive page edit requires the final review to be renewed.
            if (answer.Page != "review") values.Remove("review");
            var review = state.Review with { Pages = values, RepositoryWork = answer.RepositoryWork ?? state.Review.RepositoryWork };
            if (screenReview is not null) review = review with { ScreenReviews = [.. review.ScreenReviews ?? [], screenReview] };
            var content = state.Content;
            var block = RenderReview(review, state.Record.Plan);
            var start = content.IndexOf(ReviewStart, StringComparison.Ordinal);
            if (start < 0) content = content.TrimEnd() + "\n\n" + block + "\n";
            else
            {
                var end = content.IndexOf(ReviewEnd, start, StringComparison.Ordinal);
                if (end < 0) return WizardError("The saved feature-review block is incomplete. Restore its end marker before saving.");
                content = content[..start] + block + content[(end + ReviewEnd.Length)..];
            }
            content = content.Replace("\r\n", "\n", StringComparison.Ordinal);
            var temporary = state.RequestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.ReadAllText(state.RequestPath).Replace("\r\n", "\n", StringComparison.Ordinal) != state.Content) return WizardError("The request changed while saving. Refresh before retrying.");
                File.Move(temporary, state.RequestPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return ProjectWizard(state with { Content = content, Review = review }) with { Status = "saved", Applied = true };
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { return WizardError(e.Message); }
    }

    private WizardState ReadWizard(string workspacePath, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 80 || !Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            throw new InvalidDataException("Select a valid feature identifier.");
        var resolved = workspaces.Resolve(workspacePath);
        var workspace = resolved.Workspace;
        var authority = workspace?.AuthorityRepository;
        if (!resolved.IsSuccess || authority is null) throw new InvalidDataException("Select an initialized product authority.");
        var requestPath = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot, "specs/feature-requests", slug, "request.md");
        if (!SafeAbsolutePath(requestPath) || !File.Exists(requestPath)) throw new InvalidDataException("The feature request is missing or uses an unsafe path.");
        var record = ReadRecord(requestPath);
        if (record?.Plan is null) throw new InvalidDataException("The feature intake record is missing.");
        var sourceRoot = $".cis/inputs/features/{slug}/";
        if (record.Plan.Slug != slug || !Regex.IsMatch(record.Plan.SourceHash, "^sha256:[a-f0-9]{64}$")
            || (record.Plan.SourcePath != sourceRoot + "source.md"
                && record.Plan.SourcePath != sourceRoot + "revisions/" + record.Plan.SourceHash[7..] + "/source.md")
            || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, record.Plan.SourcePath, out var sourcePath)
            || !SafeAbsolutePath(sourcePath) || !File.Exists(sourcePath))
            throw new InvalidDataException("The retained feature BRD is missing or uses an unsafe path.");
        var source = File.ReadAllBytes(sourcePath);
        if (Hash(source) != record.Plan.SourceHash) throw new InvalidDataException("The retained BRD has changed. Restore the original source before reviewing this intake.");
        var content = File.ReadAllText(requestPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        FeatureReview review = new([]);
        var data = content.IndexOf(ReviewData, StringComparison.Ordinal);
        if (data >= 0)
        {
            var end = content.IndexOf("\n-->", data, StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("The feature review metadata is incomplete.");
            review = JsonSerializer.Deserialize<FeatureReview>(content[(data + ReviewData.Length)..end], Json)
                ?? throw new InvalidDataException("The feature review metadata is invalid.");
            if (review.Pages is null || review.Pages.Any(page => page.Value?.Answers is null)) throw new InvalidDataException("The feature review metadata is incomplete.");
        }
        var baseline = CisReadScope.Read(this, "feature-navigation-baseline", authority.RepositoryPath,
            () => definitions.Select(definition => definition.Evaluate(authority.RepositoryPath)).ToArray()).FirstOrDefault(item => item.Applicable);
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var documentHashes = ReviewPages.SelectMany(page => page.Documents).Distinct(StringComparer.Ordinal).Select(relative =>
        {
            var path = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot, relative);
            inputs[path] = FileHash(path);
            return relative + ":" + inputs[path];
        }).ToArray();
        inputs[workspace!.ConfigurationPath] = FileHash(workspace.ConfigurationPath);
        inputs[sourcePath] = Hash(source);
        inputs[Path.Combine(authority.RepositoryPath, ".cis/local/definition-wizard/session.json")] = FileHash(Path.Combine(authority.RepositoryPath, ".cis/local/definition-wizard/session.json"));
        var start = content.IndexOf(ReviewStart, StringComparison.Ordinal);
        var endReview = start < 0 ? -1 : content.IndexOf(ReviewEnd, start, StringComparison.Ordinal);
        if (start >= 0 && (endReview < 0 || content[start..(endReview + ReviewEnd.Length)] != RenderReview(review, record.Plan).Replace("\r\n", "\n", StringComparison.Ordinal)))
            throw new InvalidDataException("The managed feature review was edited outside the wizard. Preserve those edits and restore the managed block before saving through the wizard.");
        var requestContext = start < 0 ? content.TrimEnd() : (content[..start] + content[(endReview + ReviewEnd.Length)..]).TrimEnd();
        var prefix = string.Join('|', documentHashes) + baseline?.BaselineHash;
        var boundary = JsonSerializer.Serialize(new { workspace.Product, workspace.Ecosystem, Authority = authority,
            Repositories = workspace.Repositories.Where(repo => SamePath(repo.RepositoryPath, record.Plan.RepositoryPath)
                || record.Plan.IntegrationRepositories.Contains(repo.Id, StringComparer.Ordinal)).OrderBy(repo => repo.Id, StringComparer.Ordinal) }, Json);
        var binding = Hash(Encoding.UTF8.GetBytes(prefix + Hash(Encoding.UTF8.GetBytes(boundary)) + requestContext));
        var legacyBinding = Hash(Encoding.UTF8.GetBytes(prefix + inputs[workspace.ConfigurationPath] + requestContext));
        return new(authority, workspace!, requestPath, content, record, review, Encoding.UTF8.GetString(source), binding, baseline, inputs, legacyBinding);
    }

    private static CisFeatureWizardResult ProjectWizard(WizardState state)
    {
        var plan = state.Record.Plan;
        var pages = new List<CisFeatureWizardPage>();
        var registered = state.Workspace.Repositories.Any(repo => SamePath(repo.RepositoryPath, plan.RepositoryPath) && repo.IsProductOwned && repo.Role == "participant");
        pages.Add(new("foundation", "Feature foundation", registered ? "Complete" : "Needs attention", registered,
            registered ? [] : ["Restore the feature's product-owned repository registration."], [],
            [new("Feature request", plan.RequestPath, true), new("Original feature BRD", plan.SourcePath, true)]));
        foreach (var definition in ReviewPages)
        {
            var saved = state.Review.Pages.GetValueOrDefault(definition.Id);
            var fields = definition.Id == "business"
                ? new List<CisFeatureWizardField> { new("summary", definition.Prompt,
                    SourceExcerpt(state.Source, definition.SourceTerms), saved?.Answers.GetValueOrDefault("summary"), true) }
                : StructuredFields(state, definition.Id, saved).ToList();
            if (definition.Id == "business") fields.AddRange(plan.OpenDecisions.Select((question, index) =>
                new CisFeatureWizardField($"decision-{index + 1:000}", question, "", saved?.Answers.GetValueOrDefault($"decision-{index + 1:000}"), true)));
            var attention = new List<string>();
            var unanswered = fields.Count(field => field.Required && !HasAnswer(field.Answer));
            if (unanswered > 0) attention.Add($"{unanswered} answer{(unanswered == 1 ? " requires" : "s require")} your review and save.");
            if (saved is not null && !MatchesBinding(saved, state)) attention.Add("The feature BRD, product baseline or repository boundary changed. Review the retained answers and save this page again.");
            if (definition.Id == "delivery" && state.Review.RepositoryWork is { } repositoryWork)
                attention.AddRange(ValidateRepositoryWork(state, repositoryWork));
            var documents = definition.Documents.Select(relative =>
            {
                var path = $"{state.Authority.DocumentationRoot}/{relative}";
                var absolute = Path.Combine(state.Authority.RepositoryPath, path);
                return new CisFeatureWizardDocument(Path.GetFileNameWithoutExtension(relative).Replace('-', ' '), path, SafeAbsolutePath(absolute) && File.Exists(absolute));
            }).ToArray();
            pages.Add(new(definition.Id, definition.Title, attention.Count == 0 ? "Reviewed" : "Needs attention", attention.Count == 0, attention, fields, documents));
        }
        var current = state.Baseline is { Active: true };
        var final = state.Review.Pages.GetValueOrDefault("review");
        var reviewed = pages.All(page => page.Complete) && current && final is not null && MatchesBinding(final, state)
            && HasAnswer(final.Answers.GetValueOrDefault("summary"));
        var blockers = pages.Where(page => !page.Complete).Select(page => $"Review {page.Title.ToLowerInvariant()}.").ToList();
        if (!current) blockers.Add("The product baseline needs activation or reconciliation. Open the product wizard to resolve its reported findings.");
        if (blockers.Count == 0 && !reviewed) blockers.Add("Record your final review of the proposed feature definition.");
        pages.Add(new("review", "Review and next steps", reviewed ? "Reviewed" : "Needs attention", reviewed, blockers,
            [new("summary", "Record the conclusion of your feature-definition review.", "The proposed feature scope and its business decisions, technical changes, architecture, integrations, UI impact and acceptance criteria have been reviewed. Product backlog approval and the governed feature specification remain the next delivery gates.", final?.Answers.GetValueOrDefault("summary"), true)], []));
        var revision = Hash(Encoding.UTF8.GetBytes(state.Content + state.Binding));
        return new("status", plan, revision, reviewed, current, pages, [], false)
        { RepositoryWork = state.Review.RepositoryWork ?? [], Repositories = state.Workspace.Repositories,
            SourceHistory = (state.Record.SourceRevisions ?? []).Reverse().SelectMany(revision => new[]
            {
                new CisFeatureWizardDocument($"BRD before {revision.UpdatedAt} ({revision.Actor})", revision.SourcePath, true),
                new CisFeatureWizardDocument($"Saved answers before {revision.UpdatedAt}", revision.RequestPath, true)
            }).Where(document => CisPathSafety.TryResolveUnderRoot(state.Authority.RepositoryPath, document.Path, out var path)
                && SafeAbsolutePath(path) && File.Exists(path)).ToArray() };
    }

    private static string SourceExcerpt(string source, string terms)
    {
        var sections = Regex.Matches(source, @"(?m)^(?<level>#{1,6})\s+(?<title>[^\r\n]+)\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var excerpts = new List<string>();
        for (var i = 0; i < sections.Count && excerpts.Sum(text => text.Length) < 5000; i++)
        {
            if (!Regex.IsMatch(sections[i].Groups["title"].Value, terms, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
            var start = sections[i].Index + sections[i].Length;
            var next = i + 1;
            while (next < sections.Count && sections[next].Groups["level"].Length > sections[i].Groups["level"].Length) next++;
            var end = next < sections.Count ? sections[next].Index : source.Length;
            var excerpt = Regex.Replace(source[start..end], @"<!--.*?-->", "", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();
            excerpt = Regex.Replace(excerpt, @"!?\[([^\]]*)\]\([^)]+\)", "$1", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (excerpt.Length > 0) excerpts.Add("### " + sections[i].Groups["title"].Value + "\n\n" + BoundExcerpt(excerpt, 2200));
            i = next - 1;
        }
        return excerpts.Count > 0 ? BoundExcerpt(string.Join("\n\n", excerpts), 6000) : "";
    }

    private static string BoundExcerpt(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        const string note = "\n\n[Excerpt ends; review the full source document.]";
        var cut = text.LastIndexOf('\n', maximum - note.Length);
        if (cut <= 0) cut = maximum - note.Length;
        return text[..cut].TrimEnd() + note;
    }

    private static string RenderReview(FeatureReview review, CisFeatureIntakePlan plan)
    {
        var lines = new StringBuilder(ReviewStart + "\n\n## Feature definition review\n\n");
        foreach (var definition in ReviewPages.Select(page => (page.Id, page.Title)).Append(("review", "Final review")))
        {
            if (!review.Pages.TryGetValue(definition.Item1, out var page)) continue;
            lines.AppendLine($"### {definition.Item2}\n\nReviewed by {page.Actor} on {page.SavedAt}.\n");
            foreach (var answer in page.Answers)
            {
                if (answer.Key == "summary") lines.AppendLine(answer.Value + "\n");
                else
                {
                    var question = answer.Key.StartsWith("decision-", StringComparison.Ordinal) && int.TryParse(answer.Key[9..], out var index)
                        && index > 0 && index <= plan.OpenDecisions.Count ? plan.OpenDecisions[index - 1] : answer.Key;
                    question = QuestionsFor(definition.Item1).FirstOrDefault(item => item.Id == answer.Key)?.Label ?? question;
                    lines.AppendLine($"#### {question}\n\n{answer.Value}\n");
                }
            }
        }
        if (review.RepositoryWork is { Count: > 0 } work)
        {
            lines.AppendLine("### Repository work breakdown\n\nThese are proposed repository features. Product and implementation approvals remain separate.\n");
            foreach (var item in work)
            {
                lines.AppendLine($"#### {item.Id}: {item.Title}\n\nRepository: `{item.RepositoryId}`\n\n{item.Scope}\n");
                lines.AppendLine($"Dependencies: {(item.DependsOn.Count == 0 ? "None declared; can be planned independently." : string.Join(", ", item.DependsOn))}\n");
                if (item.ChangeIds.Count > 0) lines.AppendLine($"Linked changes: {string.Join(", ", item.ChangeIds)}\n");
            }
        }
        if (review.ScreenReviews is { Count: > 0 } screenReviews)
        {
            lines.AppendLine("### Proposed screen review\n\nThese choices concern draft feature screens; delivery design approval remains separate.\n");
            foreach (var screen in screenReviews)
                lines.AppendLine($"#### {screen.Title}\n\n{screen.Decision} — {screen.Actor}, {screen.SavedAt}\n\n{screen.Feedback}\n");
        }
        lines.AppendLine(ReviewData + JsonSerializer.Serialize(review, Json) + "\n-->");
        lines.Append(ReviewEnd);
        return lines.ToString();
    }

    private static CisFeatureWizardResult WizardError(string error) => new("blocked", null, null, false, false, [], [error], false);
    private static string FileHash(string path) => SafeAbsolutePath(path) && File.Exists(path) ? Hash(File.ReadAllBytes(path)) : "missing";
    private static bool HasAnswer(string? answer) => !string.IsNullOrWhiteSpace(answer)
        && !Regex.IsMatch(answer.Trim(), @"^(?:tbd|todo|unknown|not decided|to be decided|to be confirmed|pending)[.!]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed record PageReview(Dictionary<string, string> Answers, string Actor, string SavedAt, string Binding);
    private static bool MatchesBinding(PageReview review, WizardState state)
        => review.Binding == state.Binding || review.Binding == state.LegacyBinding;
    private sealed record FeatureReview(Dictionary<string, PageReview> Pages)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<CisFeatureRepositoryWork>? RepositoryWork { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<CisFeatureScreenReview>? ScreenReviews { get; init; }
    }
    private sealed record WizardState(CisWorkspaceRepository Authority, CisWorkspace Workspace, string RequestPath, string Content,
        IntakeRecord Record, FeatureReview Review, string Source, string Binding, CisProductDefinitionAuthority? Baseline, Dictionary<string, string> Inputs, string LegacyBinding);
}
