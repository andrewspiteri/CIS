using System.Net;
using System.Text.RegularExpressions;

namespace Cis.Abstractions;

public static class CisTechnicalIntentPresentation
{
    private const string Names = "baseline|business-evidence|questionnaire-evidence|surface-evidence|standards-evidence";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static string HideManagedEvidence(string content)
        => Regex.Replace(content, $@"(?<start><!-- cis:technical-intent-(?:{Names}):start -->)(?<body>.*?)(?<end><!-- cis:technical-intent-(?:{Names}):end -->)",
            match => match.Groups["body"].Value.Contains("<!-- cis:technical-intent-evidence\n", StringComparison.Ordinal)
                ? match.Value : match.Groups["start"].Value + "\n<!-- cis:technical-intent-evidence\n"
                    + WebUtility.HtmlEncode(match.Groups["body"].Value) + "\n-->\n" + match.Groups["end"].Value,
            RegexOptions.Singleline | RegexOptions.CultureInvariant, Timeout);

    public static string RestoreManagedEvidence(string content)
        => Regex.Replace(content, @"\r?\n<!-- cis:technical-intent-evidence\r?\n(?<body>.*?)\r?\n-->\r?\n",
            match => WebUtility.HtmlDecode(match.Groups["body"].Value), RegexOptions.Singleline | RegexOptions.CultureInvariant, Timeout);
}
