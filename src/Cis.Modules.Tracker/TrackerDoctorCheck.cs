using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Tracker;

public sealed class TrackerDoctorCheck : ICisRepositoryDoctorCheck
{
    public string Name => "tracker";
    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var profile = Path.Combine(context.DocumentationPath, "references", "external-tracker-profile.md");
        if (!File.Exists(profile)) return [];
        var path = Path.Combine(context.RepositoryPath, TrackerService.ConflictsPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return [];
        try
        {
            var conflicts = JsonSerializer.Deserialize<TrackerConflict[]>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            return conflicts.Where(item => item.Status == "open").Select(item => new CisRepositoryDoctorFinding(
                "CIS-TRACKER-CONFLICT-001", "warning", "external-tracker", item.Message,
                [TrackerService.ConflictsPath, $"{item.ChangeId}/{item.TaskId}"],
                "Review canonical and remote changes, then record an explicit human resolution.",
                $"cis tracker resolve {item.ChangeId} {item.TaskId} --provider {item.Provider} --use <cis|remote|unlink> --reviewer <identity> --reason <rationale>",
                "review-required")).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [new("CIS-TRACKER-STATE-001", "warning", "external-tracker", $"Tracker conflict state is unreadable: {exception.Message}",
                [TrackerService.ConflictsPath], "Rebuild tracker state with a pull after reviewing durable task links.",
                "cis tracker pull <change-id>", "review-required")];
        }
    }
}
