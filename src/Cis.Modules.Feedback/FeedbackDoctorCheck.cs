using Cis.Abstractions;

namespace Cis.Modules.Feedback;

public sealed class FeedbackDoctorCheck : ICisRepositoryDoctorCheck
{
    public string Name => "feedback";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var path = ToolUsageStore.LedgerPath(context.RepositoryPath);
        if (!File.Exists(path))
        {
            return
            [
                new CisRepositoryDoctorFinding(
                    "CIS-FEEDBACK-001",
                    "information",
                    "feedback",
                    "No local CIS tool-usage history has been recorded yet.",
                    [path],
                    "Run any CIS command from or against this initialized repository; recording is automatic.",
                    "cis feedback summary",
                    "automatic"),
            ];
        }

        return
        [
            new CisRepositoryDoctorFinding(
                "CIS-FEEDBACK-002",
                "information",
                "feedback",
                "The local CIS tool-usage ledger is available.",
                [path],
                "Review deterministic savings and repeated-failure opportunities periodically.",
                "cis feedback opportunities",
                "active"),
        ];
    }
}
