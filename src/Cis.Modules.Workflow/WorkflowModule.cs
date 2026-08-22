using System.CommandLine; using System.Text.Json; using Cis.Abstractions; using Microsoft.Extensions.DependencyInjection;
namespace Cis.Modules.Workflow;
public sealed class WorkflowModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "workflow"; public string Description => "Run deterministic repository-owned workflows with resumable local evidence.";
    public void RegisterServices(IServiceCollection s) => s.AddSingleton<WorkflowService>();
    public void RegisterCommands(ICisCommandRegistry c, IServiceProvider p)
    { var s=p.GetRequiredService<WorkflowService>(); var root=new Command(Name,Description); root.Subcommands.Add(Simple("list","List workflows.",s.List)); root.Subcommands.Add(Id("describe","Describe a workflow.",s.Describe,"workflow"));
      var run=new Command("run","Run or resume a workflow."); var workflowId=new Argument<string>("workflow"); var runId=new Option<string?>("--run-id"); var runRepo=Repo(); var runFormat=Format(); run.Arguments.Add(workflowId); run.Options.Add(runId); run.Options.Add(runRepo); run.Options.Add(runFormat); run.SetAction(x=>Render(s.Run(x.GetValue(runRepo)!,x.GetValue(workflowId)!,x.GetValue(runId)),x.GetValue(runFormat)!)); root.Subcommands.Add(run);
      root.Subcommands.Add(Id("status","Report a run.",s.Status,"run-id")); root.Subcommands.Add(Id("log","Report run log locations.",s.Log,"run-id")); root.Subcommands.Add(Id("summarise","Write a local Markdown run summary.",s.Summarise,"run-id")); c.Add(root); }
    private static Command Simple(string n,string d,Func<string,WorkflowResult>a){var c=new Command(n,d);var r=Repo();var f=Format();c.Options.Add(r);c.Options.Add(f);c.SetAction(x=>Render(a(x.GetValue(r)!),x.GetValue(f)!));return c;}
    private static Command Id(string n,string d,Func<string,string,WorkflowResult>a,string label){var c=new Command(n,d);var id=new Argument<string>(label);var r=Repo();var f=Format();c.Arguments.Add(id);c.Options.Add(r);c.Options.Add(f);c.SetAction(x=>Render(a(x.GetValue(r)!,x.GetValue(id)!),x.GetValue(f)!));return c;}
    private static int Render(WorkflowResult r,string f){if(f=="json")Console.WriteLine(JsonSerializer.Serialize(r,JsonOptions));else if(f=="agent"){Console.WriteLine($"status={r.Status};exitCode={r.ExitCode};runId={r.RunId??"none"};workflows={r.Workflows.Count};applied={r.Applied.ToString().ToLowerInvariant()}");foreach(var d in r.Diagnostics)Console.WriteLine($"diagnostic={d.Replace(';',',')}");}else if(f=="human"){Console.WriteLine($"Workflow: {r.Status}; run={r.RunId??"none"}");foreach(var w in r.Workflows)Console.WriteLine($"- {w.Id}: {w.Steps.Count} step(s)");foreach(var d in r.Diagnostics)Console.WriteLine(d);}else{return 2;}return r.ExitCode;}
    private static Option<string> Repo()=>new("--repo"){DefaultValueFactory=_=>Directory.GetCurrentDirectory()};private static Option<string> Format()=>new("--format"){DefaultValueFactory=_=>"human"};
}
