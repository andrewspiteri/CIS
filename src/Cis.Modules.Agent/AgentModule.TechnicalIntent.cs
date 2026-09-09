using System.CommandLine;

namespace Cis.Modules.Agent;

public sealed partial class AgentModule
{
    private static Command AuthorTechnicalIntent(AgentService service)
    {
        var command = new Command("technical-intent", "Infer review-only technical intent from selected product-owned implementation snapshots and the current BRD.");
        var reference = new Option<string[]>("--reference") { Required = true, AllowMultipleArgumentsPerToken = true };
        var provider = Required("--provider"); var actor = Required("--actor");
        var transport = new Option<string?>("--transport");
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var approve = new Option<bool>("--approve-requests");
        var repo = Repo(); var format = Format();
        foreach (var option in new Option[] { reference, provider, actor, transport, timeout, approve, repo, format }) command.Options.Add(option);
        command.SetAction(result =>
        {
            var output = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellation, progress) => service.AuthorTechnicalIntent(
                result.GetValue(repo)!, result.GetValue(reference) ?? [], result.GetValue(provider)!,
                result.GetValue(transport), result.GetValue(timeout), result.GetValue(approve),
                result.GetValue(actor)!, cancellation, progress), output), output);
        });
        return command;
    }
}
