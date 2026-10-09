using System.CommandLine;
using System.Text.Json;

namespace Cis.Modules.Repository;

internal static class RepositoryExampleCommand
{
    internal static Command Create()
    {
        var command = new Command("example", "Inspect or export the bundled native .NET engineering example.");
        var repository = new Option<string>("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
        var destination = new Option<string?>("--destination") { Description = "New repository-relative directory; omit to list bundled files." };
        var dryRun = new Option<bool>("--dry-run");
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "human" };
        command.Options.Add(repository);
        command.Options.Add(destination);
        command.Options.Add(dryRun);
        command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = parse.GetValue(format);
            if (selected is not ("human" or "json" or "agent")) { Console.Error.WriteLine("Expected human, json or agent format."); return 2; }
            var result = new RepositoryExample(AppContext.BaseDirectory).Export(parse.GetValue(repository)!, parse.GetValue(destination), parse.GetValue(dryRun));
            if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            else
            {
                Console.WriteLine($"example={result.Name} status={result.Status} files={result.Files.Count} destination={result.Destination ?? "none"}");
                foreach (var error in result.Errors) Console.Error.WriteLine(error);
            }
            return result.ExitCode;
        });
        return command;
    }
}
