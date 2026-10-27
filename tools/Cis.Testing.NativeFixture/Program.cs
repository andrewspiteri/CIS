using System.Text.Json;
using System.Xml.Linq;

// Fault-injection fixture for the shared runner's tests; these are synthetic protocol results.
if (args.Length < 3) return 2;
var mode = args[0];
var output = args[1];
var source = args[2];
string? configuredFilter = null;
if (mode == "config-mutation")
{
    using var config = JsonDocument.Parse(File.ReadAllText(source));
    var settings = config.RootElement.GetProperty("stryker-config");
    source = settings.GetProperty("mutate")[0].GetString()!;
    configuredFilter = settings.GetProperty("test-case-filter").GetString();
    mode = "mutation";
}
Directory.CreateDirectory(output);
Directory.CreateDirectory(".cis");
if (File.Exists(".cis/environment-probe.json"))
{
    var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(".cis/environment-probe.json"))!;
    File.WriteAllText(".cis/environment-observed.json", JsonSerializer.Serialize(names.ToDictionary(name => name, Environment.GetEnvironmentVariable)));
}
var overrideFile = Path.Combine(".cis", "fault-" + Path.GetFileName(source));
if (mode is "unit" or "mutation" && File.Exists(overrideFile))
{
    var fault = File.ReadAllText(overrideFile).Trim();
    if (fault.StartsWith(mode + ":", StringComparison.Ordinal)) mode = fault[(mode.Length + 1)..];
}
File.AppendAllText(".cis/native-calls.log", mode + " " + source + " " + (configuredFilter ?? (args.Length > 3 ? args[3] : "")) + "\n");
if (mode == "recovery") { File.Delete(".cis/instrumented"); return 0; }
if (mode == "recovery-failure") return 1;
if (mode == "unit-malformed") { File.WriteAllText(Path.Combine(output, "unit.trx"), "<TestRun>"); return 0; }
if (mode == "timeout")
{
    File.WriteAllText(".cis/instrumented", "active mutant");
    Thread.Sleep(Timeout.Infinite);
    return 1;
}
if (mode is "unit" or "unit-failure" or "unit-abort")
{
    var failed = mode == "unit-failure" || File.Exists(".cis/instrumented");
    var passed = failed ? 0 : 1;
    var row = new XElement("UnitTestResult", new XAttribute("testName", "NativeFixtureChecks.Value"),
        new XAttribute("outcome", failed ? "Failed" : "Passed"), new XAttribute("duration", "00:00:00.001"));
    var summary = new XElement("ResultSummary", new XAttribute("outcome", mode == "unit-abort" ? "Aborted" : "Completed"),
        new XElement("Counters", new XAttribute("total", 1), new XAttribute("executed", 1),
            new XAttribute("passed", passed), new XAttribute("failed", 1 - passed)));
    new XDocument(new XElement("TestRun", new XElement("Results", row), summary)).Save(Path.Combine(output, "unit.trx"));
    return failed ? 1 : 0;
}
if (mode is "mutation" or "survivor" or "pending" or "wrong-source" or "empty" or "reported-timeout")
{
    var mutant = new
    {
        id = "0", mutatorName = "Boolean", replacement = "false", status = mode == "survivor" ? "Survived" : mode == "pending" ? "Pending" : mode == "reported-timeout" ? "Timeout" : "Killed",
        location = new { start = new { line = 1, column = 1 }, end = new { line = 1, column = 2 } },
        killedBy = mode is "survivor" or "reported-timeout" ? Array.Empty<string>() : new[] { "native-case" },
    };
    var files = new Dictionary<string, object>
    {
        [source.Replace('\\', '/')] = new { language = "cs", source = mode == "wrong-source" ? "different source" : File.ReadAllText(source), mutants = new[] { mutant }.Take(mode == "empty" ? 0 : 1).ToArray() },
    };
    Directory.CreateDirectory(Path.Combine(output, "reports"));
    File.WriteAllText(Path.Combine(output, "reports", "mutation-report.json"), JsonSerializer.Serialize(new { schemaVersion = "2", thresholds = new { high = 100, low = 90 }, files }));
    return mode == "survivor" ? 2 : 0;
}
return 2;
