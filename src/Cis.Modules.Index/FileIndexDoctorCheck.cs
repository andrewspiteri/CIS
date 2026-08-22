using Cis.Abstractions;

namespace Cis.Modules.Index;

public sealed class FileIndexDoctorCheck : ICisRepositoryDoctorCheck
{
    private readonly FileIndexService _service;

    public FileIndexDoctorCheck(FileIndexService service)
    {
        _service = service;
    }

    public string Name => "file-index";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var result = _service.Status(context.RepositoryPath, null);
        if (!result.IndexAvailable)
        {
            return
            [
                new CisRepositoryDoctorFinding(
                    "CIS-INDEX-001",
                    "warning",
                    "file-index",
                    "The derived per-file routing-card index is unavailable.",
                    [result.OutputPath ?? ".cis/local/index-cards"],
                    "Build a bounded first batch with local Ollama, then continue until pending coverage is zero.",
                    "cis index build --limit 100",
                    "review-required"),
            ];
        }

        if (!string.Equals(result.Status, "fresh", StringComparison.Ordinal))
        {
            return
            [
                new CisRepositoryDoctorFinding(
                    "CIS-INDEX-002",
                    "warning",
                    "file-index",
                    $"The file routing index is stale or incomplete: {result.Stale} stale, {result.Missing} missing, {result.Removed} removed.",
                    [result.OutputPath ?? ".cis/local/index-cards"],
                    "Refresh changed and missing cards. Unchanged cards will be reused by content hash.",
                    "cis index build --limit 100",
                    "review-required"),
            ];
        }

        return
        [
            new CisRepositoryDoctorFinding(
                "CIS-INDEX-003",
                "information",
                "file-index",
                $"The file routing index is fresh for {result.Fresh} file(s).",
                [result.OutputPath ?? ".cis/local/index-cards"],
                "Use `cis index find --text <terms>` before broad source searches.",
                "cis index find --text <terms>",
                "active"),
        ];
    }
}
