using System.CommandLine;
using System.Text.Json;

namespace Cis.Modules.Brd;

public sealed partial class BrdModule
{
    private static Command CreateLayoutCommand(BrdService service)
    {
        var command = new Command("layout", "Preview or apply a source-bound BRD section map without rewriting business content or approving it.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var input = new Option<string>("--input") { Required = true, Description = "JSON schemaVersion and section selections; headings match exactly including numbering." };
        var yes = new Option<bool>("--yes");
        var hash = new Option<string?>("--review-hash") { Description = "Exact reviewHash from the preview; binds both document and mapping." };
        foreach (var option in new Option[] { workspace, format, input, yes, hash }) command.Options.Add(option);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)!);
            if (selected is null) return 2;
            var result = service.MapLayout(parse.GetValue(workspace)!, parse.GetValue(input)!, parse.GetValue(yes), parse.GetValue(hash));
            if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
            {
                Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};requirements={result.RequirementIds.Count}");
                Console.WriteLine("reviewHash=" + result.ReviewHash);
                if (result.Layout is { } layout)
                    foreach (var (role, selections) in layout.Sections)
                        foreach (var selection in selections) Console.WriteLine($"section={role};heading={selection.Heading};includeChildren={selection.IncludeChildren}");
                foreach (var role in result.UnmappedSections) Console.WriteLine("unmapped=" + role);
                foreach (var error in result.Errors) Console.WriteLine("error=" + error);
            }
            return result.ExitCode;
        });
        return command;
    }
}
