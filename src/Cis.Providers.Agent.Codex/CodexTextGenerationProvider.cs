using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Providers.Agent.Codex;

/// <summary>Explicit remote text generation through the user's authenticated Codex CLI.</summary>
public sealed class CodexTextGenerationProvider : ICisAiProvider
{
    private readonly string _executable;
    private readonly string _codexDirectory;
    private CisAiProviderStatus? _cachedStatus;

    public CodexTextGenerationProvider() : this(CodexAgentProvider.ResolveExecutable(
        Environment.GetEnvironmentVariable("CIS_CODEX_EXECUTABLE"), Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("LOCALAPPDATA"), OperatingSystem.IsWindows()),
        Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")) { }

    internal CodexTextGenerationProvider(string executable, string codexDirectory)
    { _executable = executable; _codexDirectory = codexDirectory; }

    public string Name => "codex";

    public CisAiProviderStatus GetStatus()
    {
        if (_cachedStatus is not null) return _cachedStatus;
        try
        {
            // Read only the public model catalogue, never credentials. Availability is
            // not qualification for a task class; execution still verifies the route.
            var cache = Path.Combine(_codexDirectory, "models_cache.json");
            if (!File.Exists(cache)) return Unavailable("Open Codex to refresh its available model catalogue.");
            using var models = JsonDocument.Parse(File.ReadAllText(cache));
            var available = ReadModels(models.RootElement);
            var diagnosis = new CodexAgentProvider(_executable).Diagnose(Path.GetTempPath());
            return _cachedStatus = diagnosis.AuthenticationAvailable && available.Count > 0
                ? new(Name, "available", "Codex account", false, available, "Uses your signed-in Codex account. Text is sent remotely only with explicit authorization.")
                : Unavailable("Sign in to the Codex CLI before using this provider.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return Unavailable("Could not read the local Codex model catalogue."); }
    }

    internal static IReadOnlyList<CisAiModel> ReadModels(JsonElement root) =>
        root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array
            ? models.EnumerateArray().Where(model => model.TryGetProperty("visibility", out var visible) && visible.GetString() == "list"
                && model.TryGetProperty("slug", out var slug) && slug.ValueKind == JsonValueKind.String)
                .Select(model => new CisAiModel(model.GetProperty("slug").GetString()!)).DistinctBy(model => model.Name).ToArray()
            : [];

    public CisTextGenerationResult Generate(CisTextGenerationRequest request, string model)
    {
        if (!request.AllowRemote) return new("remote-approval-required", Name, model, null, "Authorize sending this guidance to Codex first.", false);
        var temporary = Directory.CreateTempSubdirectory("cis-codex-text-").FullName;
        try
        {
            var start = CreateStartInfo(_executable, temporary, model, request.JsonSchema);
            var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 600);
            string? session = null;
            string summary = string.Empty;
            long? input = null, output = null;
            var usedTool = false;
            string? failure = null;
            using var cancellation = new CancellationTokenSource();
            var result = CisAgentProcessRunner.RunLines(start, request.Prompt,
                TimeSpan.FromSeconds(timeoutSeconds), line =>
                {
                    var item = CodexAgentProvider.ParseJsonEvent(line, ref session, ref summary, ref input, ref output);
                    if (item.ProviderEventType == "error") failure = item.Message;
                    using var eventDocument = JsonDocument.Parse(line);
                    if (eventDocument.RootElement.TryGetProperty("item", out var errorItem)
                        && errorItem.TryGetProperty("type", out var errorType) && errorType.GetString() == "error"
                        && errorItem.TryGetProperty("message", out var message))
                        failure = CodexAgentProvider.Redact(message.GetString() ?? "Codex reported an error.");
                    // Text generation must not become repository execution.
                    if (IsToolEvent(line))
                    {
                        usedTool = true;
                        using var toolEvent = JsonDocument.Parse(line);
                        failure = "Unexpected text-review event: " + toolEvent.RootElement.GetProperty("item").GetProperty("type").GetString();
                        cancellation.Cancel();
                    }
                }, null, cancellation.Token);
            if (result.ExitCode != 0 || result.TimedOut || result.Cancelled || result.OutputTruncated || usedTool || string.IsNullOrWhiteSpace(summary))
                return new("failed", Name, model, null,
                    result.TimedOut ? $"Codex did not finish generating text within {timeoutSeconds} seconds. No proposal was returned. Try again or choose a faster model. " + failure
                    : usedTool ? failure ?? "Codex attempted tool use during a text-only review; its response was discarded."
                    : failure ?? "Codex did not return a complete text response. Check Codex authentication, model access and account limits.", false);
            return new("generated", Name, model, summary, null, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or JsonException or AggregateException)
        { return new("failed", Name, model, null, "Could not run the Codex text review: " + CodexAgentProvider.Redact(exception.Message), false); }
        finally
        {
            // This directory was created above, never derived from model output.
            try { Directory.Delete(temporary, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static bool IsToolEvent(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("item", out var item)
                && item.TryGetProperty("type", out var type) && type.GetString() is not ("agent_message" or "reasoning" or "error");
        }
        catch (JsonException) { return true; }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string directory, string model, string? schema)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in new[] { "exec", "--json", "--ephemeral", "--ignore-user-config", "--skip-git-repo-check", "--sandbox", "read-only", "--model", model })
            start.ArgumentList.Add(arg);
        foreach (var config in new[] { "approval_policy=\"never\"", "project_doc_max_bytes=0", "web_search=\"disabled\"",
            "model_reasoning_effort=\"high\"", "features.shell_tool=false", "features.apply_patch_freeform=false",
            "features.apps=false", "features.plugins=false", "features.js_repl=false", "features.multi_agent=false",
            "features.multi_agent_v2=false", "features.skip_host_skill_discovery=true", "features.memories=false",
            "features.browser=false", "features.computer_use=false", "mcp_servers={}" })
        { start.ArgumentList.Add("--config"); start.ArgumentList.Add(config); }
        if (schema is not null)
        {
            var path = Path.Combine(directory, "schema.json");
            File.WriteAllText(path, schema);
            start.ArgumentList.Add("--output-schema"); start.ArgumentList.Add(path);
        }
        start.ArgumentList.Add("-");
        return start;
    }

    private CisAiProviderStatus Unavailable(string detail) => new(Name, "unavailable", "Codex account", false, [], detail);
}
