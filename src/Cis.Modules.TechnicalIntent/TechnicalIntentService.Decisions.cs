using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentService
{
    private const string DecisionAuditPrefix = "<!-- cis:technical-decision-review ";

    public TechnicalIntentResult ResolveDecision(string workspacePath, CisTechnicalDecisionResolution input)
    {
        var state = ResolveState(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state);
        if (!ValidDecisionText(input.Resolution, 16384) || !ValidDecisionText(input.Actor, 200)
            || !string.IsNullOrWhiteSpace(input.Reason) && !ValidDecisionText(input.Reason, 4096))
            return Error("invalid", state, "Enter a substantive answer and human identity without placeholders or HTML comment markers. A separate reason is optional.");
        if (string.IsNullOrWhiteSpace(input.Id) || string.IsNullOrWhiteSpace(input.ReviewToken))
            return Error("invalid", state, "A decision ID and current review token are required.");
        if (state.Authority is null || state.CanonicalPath is null || !File.Exists(state.CanonicalPath))
            return Error("missing", state, "Create the technical-intent document before resolving its decisions.");
        var root = state.Authority.RepositoryPath;
        const string lockRelative = ".cis/local/technical-decisions.lock";
        if (!CisPathSafety.TryResolveUnderRoot(root, lockRelative, out var lockPath)
            || !CisPathSafety.TryResolveUnderRoot(root, Path.GetRelativePath(root, state.CanonicalPath), out _)
            || CisPathSafety.ContainsReparsePoint(root, lockPath) || CisPathSafety.ContainsReparsePoint(root, state.CanonicalPath))
            return Error("invalid", state, "The decision file or lock path is outside the checked authority.");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            using var writeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var document = File.ReadAllText(state.CanonicalPath);
            if (ReadNestedFrontMatter(document, "stable_id") != $"{state.Authority.Id}:spec:technical-intent")
                return Error("collision", state, "The canonical technical-intent document has a different stable identity.");
            var content = CisTechnicalIntentPresentation.RestoreManagedEvidence(document);
            var rows = ParseDecisionRows(ExtractSection(content, "Open technical decisions")).Where(row => row.Id == input.Id).ToArray();
            var lines = document.Split('\n').ToList();
            var locations = lines.Select((line, index) => (Cells: Cells(line), Index: index))
                .Where(row => row.Cells.Length == 5 && row.Cells[0] == input.Id).ToArray();
            if (rows.Length != 1 || locations.Length != 1)
                return Error("conflict", state, "The decision row is missing or ambiguous. Refresh the wizard. Nothing was saved.");
            var row = rows[0]; var location = locations[0];
            if (input.ReviewToken != DecisionReviewToken(state, row.Id, lines[location.Index]))
                return Error("conflict", state, "The decision or questionnaire changed. Refresh and review your retained edits before saving.");
            var resolution = input.Resolution.Trim(); var reason = input.Reason?.Trim() ?? ""; var actor = input.Actor.Trim();
            var now = _clock().ToString("O");
            var rationale = resolution + (reason.Length == 0 ? "" : $" Reason: {reason}");
            var writtenRow = $"| {Cell(row.Id)} | {Cell(row.Decision)} | {Cell(row.RequiredBefore)} | Resolved | {Cell(rationale)} Recorded by: {Cell(actor)} at {now}. |";
            var audit = new DecisionAudit(input.Id, resolution, reason, actor, now, Hash(writtenRow));
            var ending = lines[location.Index].EndsWith('\r') ? "\r" : "";
            lines[location.Index] = writtenRow + ending;
            var auditLine = DecisionAuditPrefix + JsonSerializer.Serialize(audit) + " -->" + ending;
            if (location.Index + 1 < lines.Count && ReadDecisionAudit(lines[location.Index + 1])?.Id == input.Id)
                lines[location.Index + 1] = auditLine;
            else lines.Insert(location.Index + 1, auditLine);
            var next = string.Join('\n', lines);
            // Other rows, authored prose, baselines and prior approval metadata are preserved.
            // An edit to Active content is detected as stale by its existing approval digest.
            if (File.ReadAllText(state.CanonicalPath) != document || state.Questionnaire is { } questionnaire
                && (!File.Exists(questionnaire.Path) || Hash(File.ReadAllText(questionnaire.Path)) != questionnaire.Digest))
                return Error("conflict", state, "Technical evidence changed while saving. Refresh the wizard. Nothing was saved.");
            var temporary = state.CanonicalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, next, new UTF8Encoding(false)); File.Move(temporary, state.CanonicalPath, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new("saved", state.Workspace!.WorkspacePath, state.Authority.Id,
                NormalizePath(Path.GetRelativePath(root, state.CanonicalPath)), null, state.Baselines, state.Warnings, [], true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Error("blocked", state, "The decision could not be saved. Another write may be running; retry when it finishes."); }
    }

    private static bool ValidDecisionText(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit
        && !value.Contains('\0') && !PlaceholderPattern().IsMatch(value)
        && !value.Contains("<!--", StringComparison.Ordinal) && !value.Contains("-->", StringComparison.Ordinal);

    private static CisTechnicalDecisionReview DescribeDecision(CisTechnicalDecisionReview review, State state, string document)
    {
        var lines = document.Split('\n');
        var row = review.DocumentLine is { } line ? lines[line - 1] : null;
        var audit = review.DocumentLine is { } number && number < lines.Length ? ReadDecisionAudit(lines[number]) : null;
        if (audit?.Id != review.Id || row is null || audit.RowHash != Hash(row.TrimEnd('\r'))) audit = null;
        var group = DecisionQuestionGroup(review.Decision);
        var answers = state.Questionnaire is { Current: true } questionnaire
            ? questionnaire.Questions.Where(question => group.Ids.Contains(question.Id) && question.Status is "Answered" or "Derived" && !string.IsNullOrWhiteSpace(question.Answer))
                .Select(question => new CisTechnicalDecisionAnswer(question.Id, question.Area, question.Answer!, question.Status, question.AnsweredBy)).ToArray()
            : [];
        return review with
        {
            ReviewToken = row is null ? null : DecisionReviewToken(state, review.Id, row),
            QuestionnaireOverlap = group.Ids.Length > 0,
            RelatedAnswers = answers,
            RemainingReview = group.Review,
            SuggestedResolution = SuggestDecisionResolution(review.Decision, group.Ids, answers),
            RecordedResolution = audit?.Resolution ?? (review.NeedsReview ? null : review.Rationale),
            RecordedReason = audit?.Reason,
            RecordedBy = audit?.Actor,
        };
    }

    private static string DecisionReviewToken(State state, string id, string row)
        => Hash($"technical-decision-v1|{state.Authority!.RepositoryPath}|{id}|{row.TrimEnd('\r')}|{state.Questionnaire?.Digest ?? "missing"}|{state.Questionnaire?.Current}|{state.Baselines.FirstOrDefault(item => item.Kind == "brd")?.Version}");

    private static DecisionAudit? ReadDecisionAudit(string line)
    {
        line = line.Trim();
        if (!line.StartsWith(DecisionAuditPrefix, StringComparison.Ordinal) || !line.EndsWith(" -->", StringComparison.Ordinal)) return null;
        try { return JsonSerializer.Deserialize<DecisionAudit>(line[DecisionAuditPrefix.Length..^4]); }
        catch (JsonException) { return null; }
    }

    // Match canonical scaffold wording, never stable ID numbers: later authored decisions may reuse those numbers.
    private static (string[] Ids, string? Review) DecisionQuestionGroup(string decision) => decision switch
    {
        "Confirm the bounded components, repository ownership, runtime processes, and dependency direction for the detected technical surfaces."
        or "Select the implementation shape, bounded components, repository ownership, runtime processes, and dependency direction."
        or "Implementation shape, surfaces, bounded components, ownership, runtime processes, and dependency direction."
            => (["TI-Q-001", "TI-Q-002", "TI-Q-003", "TI-Q-004", "TI-Q-005"], "Reuse the recorded architecture and technology baseline. Add any ownership or boundary exception that those answers do not cover."),
        "Select systems of record, consistency boundaries, storage technologies, retention enforcement, migration, backup, restore, and deletion behavior."
        or "Systems of record, consistency, storage technologies, retention, migration, backup, restore, and deletion behavior."
            => (["TI-Q-006", "TI-Q-007"], "The existing answers identify data stores. Confirm the remaining systems of record, consistency, retention, migration and recovery policies; choosing PostgreSQL alone does not settle those policies."),
        "Define external integration contracts, ownership, versioning, compatibility, authentication, failure, retry, idempotency, and test strategy."
        or "Integration contracts, compatibility, authentication, failure, retry, idempotency, and asynchronous delivery."
            => (["TI-Q-008", "TI-Q-011"], "Reuse the recorded communication and asynchronous-work choices. Add any specific contract owner, compatibility or recovery policy still required."),
        "Select the model execution, evaluation, explainability, versioning, learning, promotion, rollback, and human-oversight architecture."
        or "Model execution, evaluation, explainability, versioning, promotion, rollback, cost, and human oversight."
            => (["TI-Q-015"], "Reuse the recorded AI applicability and lifecycle choice. Confirm any model-specific exception."),
        "Define identity, authorization, policy-administration, sensitive-data, audit-access, threat, and non-disclosure boundaries."
        or "Identity, authorization, sensitive-data, privacy, compliance, threat, and non-disclosure boundaries."
            => (["TI-Q-009", "TI-Q-013"], "Reuse the identity and security baseline. Add any specific policy administration, sensitive-data or audit-access boundary that remains undecided."),
        "Define deployment topology, environments, observability, capacity and cost budgets, recovery objectives, release gates, and independent assurance."
        or "Deployment topology, observability, quality gates, recovery, constraints, exclusions, and future options."
            => (["TI-Q-010", "TI-Q-012", "TI-Q-014", "TI-Q-016"], "Reuse the deployment, operations, assurance and constraint answers. Confirm missing environment ownership, measurable capacity or cost limits and recovery objectives."),
        _ => ([], null)
    };

    private sealed record DecisionAudit(string Id, string Resolution, string Reason, string Actor, string RecordedAtUtc, string RowHash);
}
