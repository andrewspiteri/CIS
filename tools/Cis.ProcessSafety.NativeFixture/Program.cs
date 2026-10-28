using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;

const string stdoutMarker = "process-capture-stdout";
const string stderrMarker = "process-capture-stderr";
if (args is ["emit"] or ["emit-large"])
{
    // Give the parent time to begin both reads before either pipe becomes ready.
    Thread.Sleep(100);
    if (args[0] == "emit-large")
    {
        Console.Write(new string('x', 4096));
        Console.Error.Write(new string('y', 4096));
    }
    else
    {
        Console.WriteLine(stdoutMarker);
        Console.Error.WriteLine(stderrMarker);
    }
    return 0;
}
if (args is not (["normal"] or ["one-worker"] or ["large"])) return 2;

bool constrained = args[0] == "one-worker";
ThreadPool.GetMinThreads(out _, out int minimumIo);
ThreadPool.GetMaxThreads(out _, out int maximumIo);
if (constrained && (!ThreadPool.SetMinThreads(1, minimumIo) || !ThreadPool.SetMaxThreads(1, maximumIo))) return 3;
ThreadPool.GetMinThreads(out int minimumWorkers, out _);
ThreadPool.GetMaxThreads(out int maximumWorkers, out _);
using var completed = new ManualResetEventSlim();
CisProcessResult? result = null;
string? failure = null;
bool workerIsThreadPoolThread = false;
ThreadPool.QueueUserWorkItem(_ =>
{
    workerIsThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
    try
    {
        var child = new ProcessStartInfo(Environment.ProcessPath!);
        child.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        child.ArgumentList.Add(args[0] == "large" ? "emit-large" : "emit");
        result = CisProcessSafety.Run(child, TimeSpan.FromSeconds(5), maximumCharactersPerStream: 128);
    }
    catch (Exception exception) { failure = exception.GetType().FullName; }
    finally { completed.Set(); }
});
if (!completed.Wait(TimeSpan.FromSeconds(9))) return 4;
Console.WriteLine(JsonSerializer.Serialize(new
{
    constrained,
    minimumWorkers,
    maximumWorkers,
    mainIsThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread,
    workerIsThreadPoolThread,
    helperSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CisProcessSafety).Assembly.Location))),
    failure,
    result,
}, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
return failure is null ? 0 : 1;
