using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.UiDirection;

public sealed partial class UiDirectionQuestionnaireService
{
    public static bool RequiresUi(IReadOnlyList<UiDirectionQuestion> questions)
        => !questions.Any(question => question.Id == "UI-Q-001" && IsResolved(question) && NoFrontendAnswer(question.Answer!));

    private static bool Complete(IReadOnlyList<UiDirectionQuestion> questions)
        => !RequiresUi(questions) || questions.All(IsResolved);

    // Recognize an explicit recorded scope decision, never absence of frontend code,
    // unaccepted suggestions, mentions of CLI/APIs, or a mixed frontend/backend scope.
    private static bool NoFrontendAnswer(string answer) => Regex.IsMatch(answer.Trim(),
        @"\A(?:No (?:user-facing UI|visual UI|graphical UI|frontend)|(?:This |The )?project does not (?:employ|have|require) (?:a |any )?(?:frontend|UI))\.?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? RecordedNoFrontend(string root, string docs)
    {
        var path = Path.Combine(root, docs, "specs", "technical-intent-questionnaire.md");
        if (!File.Exists(path)) return null;
        var content = File.ReadAllText(path);
        var section = Regex.Match(content, @"(?ms)^### TI-Q-002[^\r\n]*\r?\n(?<body>.*?)(?=^### TI-Q-|\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["body"].Value;
        if (ReadBullet(section, "Status") is not ("Answered" or "Derived")) return null;
        var answer = Regex.Match(section, @"(?ms)^#### Recorded answer\s*\r?\n(?<answer>.*)\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["answer"].Value.Trim();
        return NoFrontendAnswer(answer) ? Normalize(Path.GetRelativePath(root, path)) + "#TI-Q-002 (" + Digest(section) + ")" : null;
    }

    // The current solution bundle supplies approval/currentness; retain the actual
    // canonical source citation instead of inventing a newly answered questionnaire.
    private static string? AcceptedNoFrontend(string root, string docs)
        => AcceptedFrontend(root, docs) is { } source && CompatibleNoFrontend(source.Answer) ? source.Evidence : null;

    private static (string Answer, string Evidence)? AcceptedFrontend(string root, string docs)
    {
        var path = CisProductDocumentPaths.Resolve(Path.Combine(root, docs), "specs", "technical-intent-spec.md");
        if (!File.Exists(path)) return null;
        var content = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!Regex.IsMatch(content, @"(?m)^status:\s*Active\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || ReadNested(content, "approved_by") is not { Length: > 0 } reviewer || reviewer is "null" or "Not recorded") return null;
        var section = Regex.Match(content, @"(?m)^(?<heading>#{2,3} Frontend technology)\s*$\n(?<body>(?s:.*?))(?=^#{1,3} |\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!section.Success) return null;
        var line = content[..section.Index].Count(character => character == '\n') + 1;
        return (section.Groups["body"].Value,
            Normalize(Path.GetRelativePath(root, path)) + ":" + line + " — Frontend technology (" + Digest(content) + ")");
    }

    private static bool ConflictingRecordedFrontend(string root, string docs)
    {
        if (AcceptedFrontend(root, docs) is not { } source) return false;
        var path = Path.Combine(root, docs, "specs", "technical-intent-questionnaire.md");
        if (!File.Exists(path)) return false;
        var section = Regex.Match(File.ReadAllText(path), @"(?ms)^### TI-Q-002[^\r\n]*\r?\n(?<body>.*?)(?=^### TI-Q-|\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["body"].Value;
        if (ReadBullet(section, "Status") is not ("Answered" or "Derived")) return false;
        var answer = Regex.Match(section, @"(?ms)^#### Recorded answer\s*\r?\n(?<answer>.*)\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["answer"].Value;
        return CompatibleNoFrontend(source.Answer) && VisualChoice(answer)
            || VisualChoice(source.Answer) && CompatibleNoFrontend(answer);
    }

    private static bool CompatibleNoFrontend(string answer)
        => !VisualChoice(answer) && Regex.Split(answer.Trim(), @"\r?\n|(?<=[.!?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Any(part => NoFrontendAnswer(part.Trim()) || Regex.IsMatch(part.Trim(),
                @"\A(?:The |This )?(?:product|project) has no (?:visual frontend|frontend application(?: or browser/mobile/desktop presentation technology)?)\.?\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    private static bool VisualChoice(string answer) => Regex.IsMatch(answer,
        @"\b(?:customer|public|backoffice|admin)\s+(?:web|UI|screen|dashboard|frontend|portal)\b|\b(?:React|Angular|Vue|SwiftUI|Jetpack Compose|\.NET MAUI)\b|\b(?:include|introduce|build|deliver|provide|ship|support|require|add)(?:s|d|ing)?\s+(?:(?:a|an|the|also|new|visual|graphical|user-facing|desktop|mobile|web|browser)[ -]+)*(?:UI|frontend|screen|dashboard|interface|application)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
