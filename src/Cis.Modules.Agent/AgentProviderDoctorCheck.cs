using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed class AgentProviderDoctorCheck(IEnumerable<ICisAgentProvider> providers) : ICisRepositoryDoctorCheck
{
    public string Name => "agent-provider";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var findings = new List<CisRepositoryDoctorFinding>();
        var profile = Path.Combine(context.DocumentationPath, "references", "agent-provider-profile.md");
        if (!File.Exists(profile))
            findings.Add(Finding("CIS-AGENT-DOCTOR-001", "warning", "The canonical agent-provider profile is missing.",
                [Relative(context, profile)], "Rerun repository initialization, then review the seeded provider and isolation policy."));

        foreach (var duplicate in providers.GroupBy(item => item.Descriptor.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            findings.Add(Finding("CIS-AGENT-DOCTOR-002", "error", $"Agent provider identifier '{duplicate.Key}' is registered more than once.",
                duplicate.Select(item => item.GetType().AssemblyQualifiedName ?? item.GetType().FullName ?? duplicate.Key).ToArray(),
                "Remove or rename the conflicting provider assembly; module registration order is not a conflict policy."));

        var declaredDefault = ReadDefault(profile);
        if (declaredDefault is not null)
        {
            var registered = providers.Where(item => item.Descriptor.Id.Equals(declaredDefault, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (registered.Length == 0)
                findings.Add(Finding("CIS-AGENT-DOCTOR-003", "error", $"Declared default agent provider '{declaredDefault}' is not registered.",
                    [declaredDefault], "Install the provider assembly or select a registered provider in the canonical profile."));
            else if (registered.Length == 1)
            {
                var provider = registered[0];
                CisAgentProviderDiagnosis? diagnosis = null;
                try { diagnosis = provider.Diagnose(context.RepositoryPath); }
                catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    findings.Add(Finding("CIS-AGENT-DOCTOR-004", "warning", $"Declared default agent provider '{declaredDefault}' failed diagnosis.",
                        [exception.GetType().Name], $"Run `cis agent provider diagnose {declaredDefault}` and repair the provider boundary before execution."));
                }
                if (diagnosis is not null) AddDiagnosisFinding(findings, provider, diagnosis, true);
            }
        }

        foreach (var providerId in ReadEnabledProviders(profile).Where(id => !id.Equals(declaredDefault, StringComparison.OrdinalIgnoreCase)))
        {
            var registered = providers.Where(item => item.Descriptor.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (registered.Length == 0)
            {
                if (!providerId.Equals("portable", StringComparison.OrdinalIgnoreCase))
                    findings.Add(Finding("CIS-AGENT-DOCTOR-007", "warning", $"Enabled agent provider '{providerId}' is not registered.",
                        [providerId], "Install the provider assembly or disable the provider in the canonical profile.",
                        "cis agent providers"));
                continue;
            }
            if (registered.Length > 1) continue;
            var provider = registered[0];
            if (!provider.Descriptor.DirectExecution) continue;
            try { AddDiagnosisFinding(findings, provider, provider.Diagnose(context.RepositoryPath), false); }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                findings.Add(Finding("CIS-AGENT-DOCTOR-004", "warning", $"Enabled agent provider '{providerId}' failed diagnosis.",
                    [exception.GetType().Name], $"Run `cis agent provider diagnose {providerId}` and repair the provider boundary before execution.",
                    $"cis agent provider diagnose {providerId}"));
            }
        }

        var runsRoot = Path.Combine(context.RepositoryPath, AgentService.RootPath.Replace('/', Path.DirectorySeparatorChar), "runs");
        if (Directory.Exists(runsRoot))
        {
            foreach (var runDirectory in Directory.EnumerateDirectories(runsRoot))
            {
                var manifestPath = Path.Combine(runDirectory, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    findings.Add(Finding("CIS-AGENT-DOCTOR-005", "error", "An agent run directory has no current manifest.",
                        [Relative(context, runDirectory)], "Preserve the directory for investigation and do not infer run success."));
                    continue;
                }
                AgentRunManifest? manifest;
                try { manifest = JsonSerializer.Deserialize<AgentRunManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
                catch (JsonException) { manifest = null; }
                if (manifest is null)
                { findings.Add(Finding("CIS-AGENT-DOCTOR-005", "error", "An agent run manifest is malformed.", [Relative(context, manifestPath)], "Preserve the file for investigation and do not infer run success.")); continue; }
                if (CisAgentRunStates.IsTerminal(manifest.Status)) continue;
                if (manifest.ProcessId is null || !MatchesProcess(manifest.ProcessId.Value, manifest.ProcessStartedAtUtc))
                    findings.Add(Finding("CIS-AGENT-DOCTOR-006", "warning", $"Agent run '{manifest.RunId}' is non-terminal but its owned process is absent or stale.",
                        [manifest.RunId, manifest.Status, manifest.ProcessId?.ToString() ?? "no-process"],
                        "Inspect the run and append-only events, then resume it explicitly or retain it as interrupted evidence."));
            }
        }
        return findings;
    }

    private static string? ReadDefault(string profile)
    {
        if (!File.Exists(profile)) return null;
        foreach (var line in File.ReadLines(profile))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(item => item.Trim()).ToArray();
            if (cells.Length >= 2 && cells[0].Equals("default-provider", StringComparison.OrdinalIgnoreCase)) return cells[1] is "-" or "none" ? null : cells[1];
        }
        return null;
    }
    private static IReadOnlyList<string> ReadEnabledProviders(string profile)
    {
        if (!File.Exists(profile)) return [];
        var enabled = new List<string>();
        var inProviders = false;
        foreach (var line in File.ReadLines(profile))
        {
            if (line.Trim().Equals("## Providers", StringComparison.OrdinalIgnoreCase)) { inProviders = true; continue; }
            if (inProviders && line.TrimStart().StartsWith("## ", StringComparison.Ordinal)) break;
            if (!inProviders || !line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(item => item.Trim()).ToArray();
            if (cells.Length >= 2 && cells[1].Equals("yes", StringComparison.OrdinalIgnoreCase)
                && !cells[0].Equals("Provider", StringComparison.OrdinalIgnoreCase)) enabled.Add(cells[0]);
        }
        return enabled.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static void AddDiagnosisFinding(List<CisRepositoryDoctorFinding> findings, ICisAgentProvider provider,
        CisAgentProviderDiagnosis diagnosis, bool isDefault)
    {
        var qualifier = isDefault ? "Declared default" : "Enabled";
        if (!diagnosis.Available)
            findings.Add(Finding("CIS-AGENT-DOCTOR-004", "warning", $"{qualifier} agent provider '{provider.Descriptor.Id}' is unavailable: {diagnosis.Status}.",
                diagnosis.Diagnostics, $"Run `cis agent provider diagnose {provider.Descriptor.Id}` and restore only the reported prerequisite.",
                $"cis agent provider diagnose {provider.Descriptor.Id}"));
        else if (diagnosis.Status.Equals("authentication-unverified", StringComparison.OrdinalIgnoreCase))
            findings.Add(Finding("CIS-AGENT-DOCTOR-008", "info", $"{qualifier} agent provider '{provider.Descriptor.Id}' authentication is unverified.",
                new[] { diagnosis.Executable ?? "unresolved executable", diagnosis.Version ?? "unknown version" }.Concat(diagnosis.Diagnostics).ToArray(),
                "Use the provider-native authentication command when explicit setup is wanted. Ambient Desktop/App Server authentication may still work and must be proven by execution evidence.",
                provider is ICisAgentProviderAuthenticator authenticator
                    ? $"cis agent provider authenticate {provider.Descriptor.Id} --method {authenticator.Authentication.DefaultMethod}"
                    : $"cis agent provider diagnose {provider.Descriptor.Id}"));
    }
    private static bool MatchesProcess(int id, string? started)
    {
        if (!DateTimeOffset.TryParse(started, out var expected)) return false;
        try { using var process = Process.GetProcessById(id); return Math.Abs((process.StartTime.ToUniversalTime() - expected).TotalSeconds) <= 2; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private static string Relative(CisRepositoryContext context, string path) => Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/');
    private static CisRepositoryDoctorFinding Finding(string code, string severity, string message, IReadOnlyList<string> evidence,
        string fix, string? command = "cis agent status")
        => new(code, severity, "agent", message, evidence, fix, command, "review-required");
}
