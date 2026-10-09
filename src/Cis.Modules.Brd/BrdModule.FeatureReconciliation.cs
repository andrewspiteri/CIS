using System.CommandLine;

namespace Cis.Modules.Brd;

public sealed partial class BrdModule
{
    private static Command CreateFeatureReconcileCommand(BrdBacklogService service)
    {
        var command = new Command("reconcile", "Retain an unchanged feature against a reviewed product-definition update; clear approval for renewed review.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var item = new Option<string>("--item") { Required = true, Description = "Stable HLT item ID." };
        var review = new Option<string>("--review") { Required = true, Description = "Authority-relative compatibility review JSON, including prior/current artifact hashes." };
        var actor = new Option<string>("--actor") { Required = true, Description = "Identity performing reconciliation; does not grant human approval." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Why the retained feature remains compatible with the reviewed direction." };
        foreach (var option in new Option[] { workspace, format, item, review, actor, reason }) command.Options.Add(option);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format));
            if (selected is null) return 2;
            var result = service.ReconcileFeature(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parse.GetValue(item)!, parse.GetValue(review)!, parse.GetValue(actor)!, parse.GetValue(reason)!);
            RenderFeatureLifecycle(result, selected);
            return result.ExitCode;
        });
        return command;
    }
}
