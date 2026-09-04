using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.References;

public sealed class ReferenceDoctorCheck : ICisRepositoryDoctorCheck
{
    public string Name => "references";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var referenceRoot = Path.Combine(context.DocumentationPath, "references");
        if (!Directory.Exists(referenceRoot)) return [];
        var governed = Directory.EnumerateFiles(referenceRoot, "*.md")
            .Any(path => Path.GetFileName(path).Contains("dictionary", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(path).Contains("catalogue", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(path).Contains("route-map", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(path).Contains("ownership-map", StringComparison.OrdinalIgnoreCase));
        if (!governed) return [];
        var statePath = Path.Combine(context.RepositoryPath, ".cis", "local", "references", "inventory.json");
        if (!File.Exists(statePath))
            return [Finding("CIS-REF-DOCTOR-001", "warning", "Local normalized reference state is unavailable.",
                [".cis/local/references/inventory.json"], "Run reference discovery and strict validation.", "cis references discover")];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(statePath));
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
                return [Finding("CIS-REF-DOCTOR-002", "warning", "Local normalized reference state has an unsupported schema.",
                    [".cis/local/references/inventory.json"], "Regenerate disposable reference state.", "cis references discover")];
            return [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [Finding("CIS-REF-DOCTOR-003", "warning", $"Local normalized reference state is unreadable: {exception.Message}",
                [".cis/local/references/inventory.json"], "Regenerate disposable reference state.", "cis references discover")];
        }
    }

    private static CisRepositoryDoctorFinding Finding(string code, string severity, string message,
        IReadOnlyList<string> evidence, string fix, string command)
        => new(code, severity, "reference-governance", message, evidence, fix, command, "review-required");
}
