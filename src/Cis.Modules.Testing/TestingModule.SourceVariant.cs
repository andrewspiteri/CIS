using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class TestingModule
{
    private static Command SourceVariant(IServiceProvider services)
    {
        var command = new Command("verify-source-variant", "Verify saved original/pass, exact-variant/fail and restored/pass evidence without executing tests or applying mutation credit.");
        var repository = Repo(); var manifest = Required("--manifest"); var format = Format();
        command.Options.Add(repository); command.Options.Add(manifest); command.Options.Add(format);
        command.SetAction(parse =>
        {
            string output = parse.GetValue(format)!;
            if (output is not ("human" or "json" or "agent")) { return 2; }
            var reader = services.GetService(typeof(ICisGraphSnapshotReader)) as ICisGraphSnapshotReader;
            var result = reader is null ? new SourceVariantEvidenceResult("missing-graph-module", 2)
                { Diagnostics = ["The installed graph module is required."] }
                : new IncrementalTestService(reader).VerifySourceVariantManifest(parse.GetValue(repository)!, parse.GetValue(manifest)!);
            return RenderSourceVariant(result, output);
        });
        return command;
    }

    private static int RenderSourceVariant(SourceVariantEvidenceResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); }
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};qualified=false;partial=true;phases={result.Phases.Count}");
            foreach (string diagnostic in result.Diagnostics) { Console.WriteLine("diagnostic=" + diagnostic.Replace(';', ',')); }
        }
        else
        {
            Console.WriteLine($"Source-variant evidence: {result.Status}. Diagnostic only; native reports and mutation scores are unchanged.");
            foreach (string diagnostic in result.Diagnostics) { Console.WriteLine(diagnostic); }
        }
        return result.ExitCode;
    }
}
