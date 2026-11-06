using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    internal static NativeCorrectnessPilotResult InspectMutationCorrectnessPilot(string repository,
        MutationRunProfile target, CisNativeWorkAdmission? admission, ICisNativeWorkController? controller)
    {
        if (NativeProjectOverrideHold(target.Command) is { } projectHold) return projectHold;
        var result = InspectCorrectnessPilot(repository, target.CorrectnessPilotProfile, admission, workController: controller);
        if (!result.Ready) return result;
        try
        {
            var representative = ReadProfile(repository, target.CorrectnessPilotProfile!, "canary").Profile;
            var pilot = representative.CorrectnessPilot!;
            Require(IncrementalTestService.PilotEnvironmentIdentity(target.Environment)
                == IncrementalTestService.PilotEnvironmentIdentity(representative.Environment),
                "The target native environment differs from the exercised pilot.");
            var targetConfig = NativePilotConfiguration(repository, target.WorkingDirectory, target.Command, null, target.ConfigFiles);
            var pilotConfig = NativePilotConfiguration(repository, representative.WorkingDirectory, representative.Command, null, representative.ConfigFiles);
            Require(NativePilotCommand(target.Command, pilot.ToolFiles) == NativePilotCommand(representative.Command, pilot.ToolFiles)
                && targetConfig == pilotConfig && target.BreakThreshold == representative.BreakThreshold,
                "The target runner, adapter, native configuration or interpretation is not the path exercised by this pilot.");
            return result;
        }
        catch (Exception error) when (Expected(error) || error is System.Xml.XmlException)
        { return result with { Status = "pilot-path-mismatch", Ready = false, NextAction = error.Message, Diagnostics = [error.Message] }; }
    }

    internal static NativeCorrectnessPilotResult InspectMutationCorrectnessPilot(string repository,
        string profilePath, string? referencePath, CisNativeWorkAdmission? admission, ICisNativeWorkController? controller)
    {
        try
        {
            var target = ReadProfile(repository, profilePath, "qualification").Profile;
            Require(target.CorrectnessPilotProfile == referencePath,
                "The retained qualification reference differs from its current pinned native profile.");
            return InspectMutationCorrectnessPilot(repository, target, admission, controller);
        }
        catch (Exception error) when (Expected(error))
        { return new("pilot-required", false, null, error.Message) { Diagnostics = [error.Message] }; }
    }

    internal static NativeCorrectnessPilotResult InspectIncrementalPilotPath(string repository,
        IncrementalTestProfile target, CisNativeWorkAdmission? admission, ICisNativeWorkController? controller)
    {
        if (target.MutationCommand is { } mutationCommand && NativeProjectOverrideHold(mutationCommand) is { } projectHold)
            return projectHold;
        var result = InspectCorrectnessPilot(repository, target.CorrectnessPilotProfile, admission, workController: controller);
        if (!result.Ready) return result;
        try
        {
            var representative = ReadProfile(repository, target.CorrectnessPilotProfile!, "canary").Profile;
            var pilot = representative.CorrectnessPilot!;
            Require(target.ToolFiles.All(tool => pilot.ToolFiles.Any(exercised => string.Equals(Path.GetFullPath(tool),
                    Path.GetFullPath(exercised), CisPathSafety.PlatformComparison) && FileHash(tool) == FileHash(exercised))),
                "The target declares native tool code that was not exercised by its pilot.");
            Require(IncrementalTestService.PilotEnvironmentIdentity(target.Environment)
                == IncrementalTestService.PilotEnvironmentIdentity(representative.Environment),
                "The target native environment differs from the exercised pilot.");
            Require(NativePilotCommand(target.UnitCommand, pilot.ToolFiles, unitProjectSelector: true)
                == NativePilotCommand(pilot.BaselineCommand, pilot.ToolFiles, unitProjectSelector: true),
                "The target unit runner command is not the exercised pilot path.");
            var targetProject = NativeUnitProject(repository, target.WorkingDirectory, target.UnitCommand, target.ConfigFiles);
            var pilotProject = NativeUnitProject(repository, representative.WorkingDirectory, pilot.BaselineCommand, representative.ConfigFiles);
            Require(targetProject == pilotProject, "The target native test framework/project configuration differs from the pilot.");
            if (target.MutationCommand is not null)
            {
                Require(NativePilotCommand(target.MutationCommand, pilot.ToolFiles) == NativePilotCommand(representative.Command, pilot.ToolFiles)
                    && NativePilotConfiguration(repository, target.WorkingDirectory, target.MutationCommand, target.StrykerConfigFile,
                        target.ConfigFiles, target.RerunAdditionalTimeoutMilliseconds)
                        == NativePilotConfiguration(repository, representative.WorkingDirectory, representative.Command, null, representative.ConfigFiles)
                    && target.BreakThreshold == representative.BreakThreshold,
                    "The target mutation runner/configuration is not the path exercised by its pilot.");
            }
            return result;
        }
        catch (Exception error) when (Expected(error) || error is System.Xml.XmlException)
        { return result with { Status = "pilot-path-mismatch", Ready = false, NextAction = error.Message, Diagnostics = [error.Message] }; }
    }

    private static NativeCorrectnessPilotResult? NativeProjectOverrideHold(string[] command)
    {
        var projectOverride = command.FirstOrDefault(value => value.Split('=')[0] is "--project" or "-p" or "--test-projects");
        return projectOverride is null ? null : new("pilot-path-mismatch", false, null,
            "Representative compatibility cannot borrow proof for a native CLI project override (" + projectOverride.Split('=')[0]
                + "). Put source/test projects in the pinned native configuration, pin their project XML, and exercise that path.");
    }

    private static string NativePilotCommand(string[] command, string[] tools, bool unitProjectSelector = false)
    {
        Require(command.Length > 0, "The native runner command is missing.");
        var executable = Path.IsPathFullyQualified(command[0]) ? command[0]
            : tools.SingleOrDefault(tool => Path.GetFileNameWithoutExtension(tool).Equals(command[0], StringComparison.OrdinalIgnoreCase));
        Require(executable is not null && tools.Any(tool => string.Equals(Path.GetFullPath(tool), Path.GetFullPath(executable), CisPathSafety.PlatformComparison)),
            "Use an explicit declared native executable that matches the exercised pilot tool closure.");
        var values = new List<string> { Path.GetFullPath(executable), FileHash(executable) };
        for (var index = 1; index < command.Length; index++)
        {
            var value = command[index];
            if (value is "--config-file" or "-f" or "--mutate" or "-m" or "--filter" or "--test-case-filter" or "--output" or "--results-directory")
            {
                Require(++index < command.Length, "The native selector has no value.");
                values.Add(value); values.Add(value is "--config-file" or "-f" ? "<native-config>"
                    : value is "--mutate" or "-m" ? "<source-selection>" : value is "--filter" or "--test-case-filter" ? "<test-selection>" : "<output>");
            }
            else if (unitProjectSelector && value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) values.Add("<project>");
            else values.Add(value.Replace("{output}", "<output>", StringComparison.Ordinal));
        }
        return Hash(JsonSerializer.SerializeToUtf8Bytes(values, Json));
    }

    private static string NativePilotConfiguration(string repository, string workingDirectory, string[] command,
        string? declaredTemplate, string[] configFiles, int? additionalTimeout = null)
    {
        string? selected = declaredTemplate;
        var selectors = 0;
        for (var index = 1; index < command.Length; index++)
            if (command[index] is "--config-file" or "-f")
            {
                Require(++index < command.Length, "Native configuration selector has no path.");
                selected ??= Path.GetRelativePath(repository, NativePilotPath(repository, workingDirectory, command[index])).Replace('\\', '/');
                selectors++;
            }
        Require(selectors == 1 && selected is not null && configFiles.Contains(selected, StringComparer.Ordinal),
            "Use one explicit pinned Stryker configuration; implicit or unrecognized runner paths have no representative proof.");
        var path = Resolve(repository, selected);
        Require(new FileInfo(path).Length is > 0 and <= 262144, "Native configuration exceeds its bound.");
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        Require(root?["stryker-config"] is JsonObject, "The representative path requires the existing explicit Stryker configuration model.");
        var native = root!["stryker-config"]!.AsObject();
        // Compare the same recognized override that the existing incremental
        // renderer actually sends. Its exercised pilot must match that budget.
        if (additionalTimeout is { } milliseconds) native["additional-timeout"] = milliseconds;
        var projects = native["test-projects"]?.Deserialize<string[]>();
        Require(projects is { Length: > 0 and <= 32 }, "The native test-project/adapter configuration is not explicit.");
        var testProjects = projects.Select(project => Path.GetRelativePath(repository, NativePilotPath(repository, workingDirectory, project)).Replace('\\', '/')).ToArray();
        Require(testProjects.All(project => configFiles.Contains(project, StringComparer.Ordinal)), "Pin every native test project before borrowing its pilot.");
        var projectFacts = testProjects.Select(project => FileHash(Resolve(repository, project))).Order(StringComparer.Ordinal).ToArray();
        var sourceProject = native["project"]?.GetValue<string>();
        Require(sourceProject is { Length: > 0 }, "The native source project configuration is not explicit.");
        var sourcePath = NativePilotPath(repository, workingDirectory, sourceProject);
        Require(configFiles.Contains(Path.GetRelativePath(repository, sourcePath).Replace('\\', '/'), StringComparer.Ordinal),
            "Pin the native source project before borrowing its pilot.");
        // Conservatively include the existing project XML, including native build
        // dependencies. Bookkeeping-only project edits may require another pilot.
        var sourceProjectSha256 = FileHash(sourcePath);
        foreach (var name in new[] { "mutate", "test-case-filter", "project", "test-projects", "output" }) native.Remove(name);
        var build = NativeBuildInputs(repository, configFiles);
        return Hash(JsonSerializer.SerializeToUtf8Bytes(new { native, projectFacts, sourceProjectSha256, build }, Json));
    }

    private static string NativeUnitProject(string repository, string workingDirectory, string[] command, string[] configFiles)
    {
        var projects = command.Where(value => value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(projects.Length == 1, "Use one explicit pinned native test project; an unknown unit runner path needs representative evidence.");
        var full = NativePilotPath(repository, workingDirectory, projects[0]);
        var relative = Path.GetRelativePath(repository, full).Replace('\\', '/');
        Require(configFiles.Contains(relative, StringComparer.Ordinal), "Pin the native test project used by the unit runner.");
        var pinnedProjects = configFiles.Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(file => FileHash(Resolve(repository, file))).Order(StringComparer.Ordinal).ToArray();
        return Hash(JsonSerializer.SerializeToUtf8Bytes(new
        { project = FileHash(full), pinnedProjects, build = NativeBuildInputs(repository, configFiles) }, Json));
    }

    private static object NativeBuildInputs(string repository, string[] files)
        => files.Where(file => new[] { "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "nuget.config" }
                .Contains(Path.GetFileName(file), StringComparer.Ordinal) || file.EndsWith(".runsettings", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
            .Select(file => new { name = Path.GetFileName(file), hash = FileHash(Resolve(repository, file)) }).OrderBy(item => item.name, StringComparer.Ordinal).ToArray();

    private static string NativePilotPath(string repository, string workingDirectory, string value)
    {
        var full = Path.GetFullPath(value, Resolve(repository, workingDirectory, true));
        Require(CisPathSafety.IsUnderRoot(repository, full, false) && !CisPathSafety.ContainsReparsePoint(repository, full),
            "The native project/configuration path leaves its repository or crosses a linked path.");
        return full;
    }
}
