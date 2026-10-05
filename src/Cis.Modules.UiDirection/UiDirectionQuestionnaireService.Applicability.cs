using System.Text.RegularExpressions;

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
}
