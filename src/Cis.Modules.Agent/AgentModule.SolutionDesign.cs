using System.CommandLine;

namespace Cis.Modules.Agent;

public sealed partial class AgentModule
{
    private static Command AuthorSolutionDesign(AgentService service)
    {
        var command = new Command("solution-design", "Infer a review-only solution architecture, component sheet and visual diagram model from owned implementation evidence.");
        var reference = new Option<string[]>("--reference") { Required = true, AllowMultipleArgumentsPerToken = true };
        var provider = Required("--provider"); var actor = Required("--actor");
        var transport = new Option<string?>("--transport");
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var approve = new Option<bool>("--approve-requests"); var repo = Repo(); var format = Format();
        foreach (var option in new Option[] { reference, provider, actor, transport, timeout, approve, repo, format }) command.Options.Add(option);
        command.SetAction(result =>
        {
            var output = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellation, progress) => service.AuthorSolutionDesign(
                result.GetValue(repo)!, result.GetValue(reference) ?? [], result.GetValue(provider)!, result.GetValue(transport),
                result.GetValue(timeout), result.GetValue(approve), result.GetValue(actor)!, cancellation, progress), output), output);
        });
        return command;
    }

    private static Command DiscoverSolutionDesign(AgentService service)
    {
        var command = new Command("solution-design", "Prepare the local architecture scaffold and implementation disclosure preview without contacting a provider.");
        var reference = new Option<string[]>("--reference") { Required = true, AllowMultipleArgumentsPerToken = true };
        var actor = Required("--actor"); var repo = Repo(); var format = Format();
        foreach (var option in new Option[] { reference, actor, repo, format }) command.Options.Add(option);
        command.SetAction(result =>
        {
            var output = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellation, progress) => service.DiscoverBrdImplementation(
                result.GetValue(repo)!, result.GetValue(reference) ?? [], result.GetValue(actor)!, cancellation, progress,
                solutionDesign: true), output), output);
        });
        return command;
    }
}
