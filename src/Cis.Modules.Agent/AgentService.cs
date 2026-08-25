using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed record AgentProviderDescriptor(string Name, string Kind, bool DirectExecution, string Description);
public sealed record AgentTaskEnvelope(int SchemaVersion, string Id, string RepositoryId, string ChangeId, string TaskId,
    string Provider, string CanonicalTaskPath, string CanonicalTaskDigest, string PreparedAtUtc, string InstructionMarkdown,
    IReadOnlyList<string> ContextArtifacts, IReadOnlyList<string> Constraints);
public sealed record AgentResultDocument(int SchemaVersion, string EnvelopeId, string Status, string Summary,
    IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Validations, IReadOnlyList<string> Evidence);
public sealed record AgentImportRecord(string EnvelopeId, string TaskId, string ImportedAtUtc, string ResultDigest,
    string Status, string Summary, IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Validations, IReadOnlyList<string> Evidence);
public sealed record AgentResult(string Status, string? RepositoryPath, AgentTaskEnvelope? Envelope,
    IReadOnlyList<AgentProviderDescriptor> Providers, IReadOnlyList<AgentImportRecord> Imports,
    IReadOnlyList<string> Diagnostics, bool Applied)
{
    public int ExitCode => Diagnostics.Any(x => x.StartsWith("ERROR:", StringComparison.Ordinal)) ? 4 : 0;
}

public sealed partial class AgentService
{
    public const string RootPath = ".cis/local/agents";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly AgentProviderDescriptor[] ProviderDescriptors =
    [new("portable", "envelope", false, "Writes a provider-neutral JSON envelope for an external agent chosen by the human or editor client.")];
    private readonly ICisRepositoryContextResolver _resolver; private readonly Func<DateTimeOffset> _clock;
    public AgentService(ICisRepositoryContextResolver resolver, Func<DateTimeOffset>? clock = null) { _resolver = resolver; _clock = clock ?? (() => DateTimeOffset.UtcNow); }

    public AgentResult Providers(string repositoryPath)
    { var context=Resolve(repositoryPath,out var d); return New(context,d.Count==0?"available":"invalid-repository",null,[],d,false); }

    public AgentResult Prepare(string repositoryPath, string changeId, string taskId, string provider)
    {
        var context=Resolve(repositoryPath,out var d); if(context is null)return New(null,"invalid-repository",null,[],d,false);
        if(!ProviderDescriptors.Any(x=>x.Name.Equals(provider,StringComparison.OrdinalIgnoreCase)))d.Add($"ERROR: Unknown agent provider '{provider}'.");
        var task=FindTask(context,changeId,taskId,d); if(task is null||d.Count>0)return New(context,"invalid",null,ReadImports(context),d,false);
        var text=File.ReadAllText(task); var status=FrontMatter(text,"task_status")??FrontMatter(text,"status");
        if(status is not null && status.Equals("Blocked",StringComparison.OrdinalIgnoreCase))d.Add("ERROR: A blocked task cannot be prepared for an agent.");
        if(ContainsUnapprovedDesignGate(text))d.Add("ERROR: Task is behind an unapproved design gate.");
        if(d.Count>0)return New(context,"blocked",null,ReadImports(context),d,false);
        var relative=Relative(context.RepositoryPath,task); var digest=Sha(text); var id=$"{context.RepositoryId}:{changeId}:{taskId}:{digest[..12]}";
        var artifacts=new List<string>{relative};
        foreach(var candidate in new[]{"proposal.md","plan.md","decisions.md","test-cases.md","verification.md"})
        {var path=Path.Combine(context.DocumentationPath,"changes",changeId,candidate);if(File.Exists(path))artifacts.Add(Relative(context.RepositoryPath,path));}
        var envelope=new AgentTaskEnvelope(1,id,context.RepositoryId,changeId,taskId,provider,relative,digest,_clock().ToUniversalTime().ToString("O"),text,artifacts,
            ["Canonical Markdown remains authoritative.","Do not infer approvals or completion.","Return a structured result document; do not edit CIS lifecycle evidence directly."]);
        var output=EnvelopePath(context,changeId,taskId);Directory.CreateDirectory(Path.GetDirectoryName(output)!);WriteAtomic(output,JsonSerializer.Serialize(envelope,JsonOptions));
        return New(context,"prepared",envelope,ReadImports(context),d,true);
    }

    public AgentResult ImportResult(string repositoryPath,string envelopePath,string resultPath)
    {
        var context=Resolve(repositoryPath,out var d);if(context is null)return New(null,"invalid-repository",null,[],d,false);
        var envelope=Read<AgentTaskEnvelope>(ResolveInput(context,envelopePath,d),"envelope",d);var result=Read<AgentResultDocument>(ResolveInput(context,resultPath,d),"result",d);
        if(envelope is null||result is null)return New(context,"invalid",envelope,ReadImports(context),d,false);
        if(result.SchemaVersion!=1)d.Add("ERROR: Unsupported agent result schema version.");
        if(!result.EnvelopeId.Equals(envelope.Id,StringComparison.Ordinal))d.Add("ERROR: Result envelope identity does not match.");
        var task=Path.Combine(context.RepositoryPath,envelope.CanonicalTaskPath.Replace('/',Path.DirectorySeparatorChar));
        if(!File.Exists(task)||Sha(File.ReadAllText(task))!=envelope.CanonicalTaskDigest)d.Add("ERROR: Canonical task changed after envelope preparation; prepare a new envelope.");
        foreach(var file in result.ChangedFiles)if(!SafeChangedFile(context,file))d.Add($"ERROR: Changed file must be repository-relative or workspace-qualified as <repository-id>::<relative-path>: {file}");
        if(string.IsNullOrWhiteSpace(result.Summary))d.Add("ERROR: Agent result summary is required.");
        if(d.Count>0)return New(context,"rejected",envelope,ReadImports(context),d,false);
        var raw=File.ReadAllText(ResolveInput(context,resultPath,d)!);var record=new AgentImportRecord(envelope.Id,envelope.TaskId,_clock().ToUniversalTime().ToString("O"),Sha(raw),result.Status,result.Summary,result.ChangedFiles,result.Validations,result.Evidence);
        var output=Path.Combine(context.RepositoryPath,RootPath.Replace('/',Path.DirectorySeparatorChar),"results",SafeFile(envelope.ChangeId),SafeFile(envelope.TaskId),record.ResultDigest+".json");Directory.CreateDirectory(Path.GetDirectoryName(output)!);WriteAtomic(output,JsonSerializer.Serialize(record,JsonOptions));
        AppendCanonicalEvidence(task,record,Relative(context.RepositoryPath,output));return New(context,"imported",envelope,ReadImports(context),d,true);
    }

    public AgentResult Status(string repositoryPath)
    {var context=Resolve(repositoryPath,out var d);return New(context,d.Count==0?"available":"invalid-repository",null,context is null?[]:ReadImports(context),d,false);}

    private static void AppendCanonicalEvidence(string task,AgentImportRecord record,string path)
    {
        var text=File.ReadAllText(task);const string heading="## Agent result imports";var row=$"| {record.ImportedAtUtc} | `{record.EnvelopeId}` | {Escape(record.Status)} | {Escape(record.Summary)} | `{path}` | `{record.ResultDigest}` |";
        if(!text.Contains(heading,StringComparison.Ordinal))text=text.TrimEnd()+$"\n\n{heading}\n\n| Imported UTC | Envelope | Status | Summary | Local evidence | Digest |\n|---|---|---|---|---|---|\n{row}\n";
        else {var next=text.IndexOf("\n## ",text.IndexOf(heading,StringComparison.Ordinal)+heading.Length,StringComparison.Ordinal);if(next<0)text=text.TrimEnd()+"\n"+row+"\n";else text=text.Insert(next,"\n"+row);}
        WriteAtomic(task,text);
    }
    private static string Escape(string v)=>v.Replace('|','/').Replace('\r',' ').Replace('\n',' ').Trim();
    private static bool ContainsUnapprovedDesignGate(string text)=>text.Contains("global-design-approval",StringComparison.OrdinalIgnoreCase)&&!text.Contains("global-design-approval | approved",StringComparison.OrdinalIgnoreCase);
    private static string? FrontMatter(string text,string key){var m=Regex.Match(text,$"(?m)^{Regex.Escape(key)}:\\s*(?<v>[^\\r\\n]+)");return m.Success?m.Groups["v"].Value.Trim().Trim('\'', '"'):null;}
    private static string? FindTask(CisRepositoryContext c,string change,string task,List<string>d){var root=Path.Combine(c.DocumentationPath,"changes",change,"agent-tasks");if(!Directory.Exists(root)){d.Add($"ERROR: Change task directory does not exist: {change}");return null;}var match=Directory.EnumerateFiles(root,"*.md").FirstOrDefault(x=>Path.GetFileNameWithoutExtension(x).Equals(task,StringComparison.OrdinalIgnoreCase));if(match is null)d.Add($"ERROR: Unknown task '{task}'.");return match;}
    private static string EnvelopePath(CisRepositoryContext c,string change,string task)=>Path.Combine(c.RepositoryPath,RootPath.Replace('/',Path.DirectorySeparatorChar),"envelopes",SafeFile(change),SafeFile(task)+".json");
    private static string? ResolveInput(CisRepositoryContext c,string path,List<string>d){var absolute=Path.IsPathRooted(path)?Path.GetFullPath(path):Path.GetFullPath(Path.Combine(c.RepositoryPath,path));if(!File.Exists(absolute)){d.Add($"ERROR: File does not exist: {path}");return null;}return absolute;}
    private static T? Read<T>(string?path,string label,List<string>d){if(path is null)return default;try{return JsonSerializer.Deserialize<T>(File.ReadAllText(path),JsonOptions)??throw new JsonException("empty");}catch(JsonException e){d.Add($"ERROR: Invalid {label}: {e.Message}");return default;}}
    private static IReadOnlyList<AgentImportRecord> ReadImports(CisRepositoryContext c){var root=Path.Combine(c.RepositoryPath,RootPath.Replace('/',Path.DirectorySeparatorChar),"results");if(!Directory.Exists(root))return[];return Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories).Select(x=>{try{return JsonSerializer.Deserialize<AgentImportRecord>(File.ReadAllText(x),JsonOptions);}catch(JsonException){return null;}}).Where(x=>x is not null).Cast<AgentImportRecord>().OrderBy(x=>x.ImportedAtUtc,StringComparer.Ordinal).ToArray();}
    private CisRepositoryContext? Resolve(string p,out List<string>d){var r=_resolver.Resolve(p);d=r.Errors.Select(x=>"ERROR: "+x).ToList();return r.Context;}
    private static AgentResult New(CisRepositoryContext?c,string s,AgentTaskEnvelope?e,IReadOnlyList<AgentImportRecord>i,IReadOnlyList<string>d,bool a)=>new(s,c?.RepositoryPath,e,ProviderDescriptors,i,d,a);
    private static bool SafeChangedFile(CisRepositoryContext context,string value)
    {
        var separator=value.IndexOf("::",StringComparison.Ordinal);
        if(separator<0)return SafeRelative(value);
        if(value.IndexOf("::",separator+2,StringComparison.Ordinal)>=0)return false;
        var repositoryId=value[..separator];var relative=value[(separator+2)..];
        if(repositoryId.Length==0||relative.Length==0||repositoryId.Any(ch=>!char.IsLetterOrDigit(ch)&&ch is not '-' and not '_' and not '.'))return false;
        var workspace=Path.Combine(context.RepositoryPath,".cis","workspace.yml");
        if(!File.Exists(workspace)||!File.ReadLines(workspace).Any(line=>Regex.IsMatch(line,$"^\\s*-?\\s*id:\\s*[\\\"']?{Regex.Escape(repositoryId)}[\\\"']?\\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)))return false;
        return SafeRelative(relative);
    }
    private static bool SafeRelative(string p)=>!string.IsNullOrWhiteSpace(p)&&!Path.IsPathRooted(p)&&!p.Replace('\\','/').Split('/').Any(x=>x is ".." or "");private static string SafeFile(string v)=>new(v.Select(ch=>char.IsLetterOrDigit(ch)||ch is '-' or '_'?ch:'-').ToArray());
    private static string Relative(string root,string p)=>Path.GetRelativePath(root,p).Replace(Path.DirectorySeparatorChar,'/');private static string Sha(string v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();
    private static void WriteAtomic(string p,string v){var tmp=p+".tmp";File.WriteAllText(tmp,v);File.Move(tmp,p,true);}
}
