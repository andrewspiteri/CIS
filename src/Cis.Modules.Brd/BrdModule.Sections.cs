using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class BrdModule
{
    private static Command CreateSectionsCommand(BrdService service, ICisTextGenerationService generation)
    {
        var sections = new Command("sections", "Propose missing BRD sections for human diff review, then apply the exact approved proposal.");
        var suggest = new Command("suggest", "Generate a local proposal from existing BRD content without changing the BRD.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var provider = new Option<string?>("--provider"); var model = new Option<string?>("--model");
        var remote = new Option<bool>("--allow-remote") { Description = "Authorize sending the current BRD text to the selected remote provider." };
        foreach (var option in new Option[] { workspace, format, provider, model, remote }) suggest.Options.Add(option);
        suggest.SetAction(parse => RenderSections(service.SuggestSections(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
            generation, parse.GetValue(provider), parse.GetValue(model), parse.GetValue(remote)), parse.GetValue(format)!));
        sections.Subcommands.Add(suggest);
        var apply = new Command("apply", "Apply the human-reviewed proposal. Rebuild the graph after applying; this does not approve the BRD.");
        var applyWorkspace = WorkspaceOption(); var applyFormat = FormatOption();
        var id = new Option<string>("--proposal") { Required = true };
        var actor = new Option<string>("--actor") { Required = true };
        foreach (var option in new Option[] { applyWorkspace, applyFormat, id, actor }) apply.Options.Add(option);
        apply.SetAction(parse => RenderSections(service.ApplySections(parse.GetValue(applyWorkspace) ?? Directory.GetCurrentDirectory(),
            parse.GetValue(id)!, parse.GetValue(actor)!), parse.GetValue(applyFormat)!));
        sections.Subcommands.Add(apply);
        return sections;
    }

    private static int RenderSections(BrdSectionProposal result, string format)
    {
        if (GetFormat(format) is not { } selected) return 2;
        if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};proposal={result.Id};sections={result.Sections.Count}");
            foreach (var warning in result.Warnings) Console.WriteLine("warning=" + warning);
            foreach (var error in result.Errors) Console.WriteLine("error=" + error);
        }
        return result.ExitCode;
    }
}
