using System.Text;
using System.Text.Json;

namespace Cis.Modules.Plan;

internal static class PlanRoutineSummary
{
    internal static string Render(PlanResult result, string? affectedTaskId)
    {
        int limit = 8;
        int textLimit = 320;
        while (true)
        {
            PlanWorkItem[] selected = (affectedTaskId is null ? result.WorkItems
                : result.WorkItems.Where(item => item.Id == affectedTaskId)).Take(limit).ToArray();
            string[] blockers = result.Errors.Concat(result.Validation?.Errors ?? []).ToArray();
            string[] warnings = (result.Validation?.Warnings ?? []).Take(limit).Select(value => Cut(value, textLimit)!).ToArray();
            object projection = new
            {
                SchemaVersion = 1,
                Kind = "routine-summary",
                Status = Cut(result.Status, 128),
                ChangeId = Cut(result.ChangeId, 128),
                AffectedTaskId = Cut(affectedTaskId, 128),
                PlanStatus = Cut(result.PlanStatus, 128),
                result.Applied,
                result.ExitCode,
                TotalWorkItems = result.WorkItems.Count,
                WorkItems = selected.Select(item => new
                {
                    Id = Cut(item.Id, 128),
                    Status = Cut(item.Status, 128),
                    TaskPath = Cut(item.TaskPath, 512),
                    Title = Cut(item.Title, 160)
                }).ToArray(),
                OmittedWorkItems = result.WorkItems.Count - selected.Length,
                Valid = result.Validation?.Valid,
                BlockerCount = blockers.Length,
                Blockers = blockers.Take(limit).Select(value => Cut(value, textLimit)).ToArray(),
                OmittedBlockers = Math.Max(0, blockers.Length - limit),
                BlockerTextTruncated = blockers.Any(value => value.Length > textLimit),
                Warnings = warnings,
                OmittedWarnings = Math.Max(0, (result.Validation?.Warnings.Count ?? 0) - warnings.Length),
                Detail = "Use plan status <change-id> --format json for complete task and validation records; summary omissions are not successful checks.",
            };
            string output = JsonSerializer.Serialize(projection, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (output.Length + Environment.NewLine.Length <= 12_000 && Encoding.UTF8.GetByteCount(output + Environment.NewLine) <= 48_000)
            {
                return output;
            }
            limit = Math.Max(0, limit / 2);
            textLimit = Math.Max(32, textLimit / 2);
        }
    }

    private static string? Cut(string? value, int maximum)
    {
        if (value is null || value.Length <= maximum)
        {
            return value;
        }
        int end = char.IsHighSurrogate(value[maximum - 1]) ? maximum - 1 : maximum;
        return value[..end] + "…";
    }
}
