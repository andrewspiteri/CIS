using System.Security.Cryptography;
using System.Text.Json;
using Cis.FunctionProfiler;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

if (args.Length != 1) throw new ArgumentException("Pass a disposable copy of the CIS build output directory.");
var directory = Path.GetFullPath(args[0]);
var manifestPath = Path.Combine(directory, "function-profile-manifest.json");
if (File.Exists(manifestPath)) throw new InvalidOperationException("Already instrumented; create a fresh output copy first.");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(directory);
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
var catalog = new List<object>();
var assemblies = new List<object>();
var id = 0;
foreach (var path in Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal))
{
    var name = Path.GetFileNameWithoutExtension(path);
    if (name != "cis" && !name.StartsWith("Cis.", StringComparison.Ordinal)) continue;
    if (name is "Cis.FunctionProfiler.Runtime" or "Cis.FunctionProfiler.Weaver") continue;
    var pdb = Path.ChangeExtension(path, ".pdb");
    var hasSymbols = File.Exists(pdb);
    using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
    {
        AssemblyResolver = resolver, InMemory = true, ReadSymbols = hasSymbols,
        SymbolReaderProvider = hasSymbols ? new PortablePdbReaderProvider() : null
    });
    if (assembly.MainModule.AssemblyReferences.Any(r => r.Name == "Cis.FunctionProfiler.Runtime") &&
        assembly.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == typeof(Probe).FullName))
        throw new InvalidOperationException($"{name} already contains probes.");
    var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    var enter = assembly.MainModule.ImportReference(typeof(Probe).GetMethod(nameof(Probe.Enter))!);
    var exit = assembly.MainModule.ImportReference(typeof(Probe).GetMethod(nameof(Probe.Exit))!);
    var methods = assembly.MainModule.GetTypes().SelectMany(t => t.Methods).ToArray();
    foreach (var method in methods)
    {
        var point = method.DebugInformation.SequencePoints.FirstOrDefault(p => !p.IsHidden);
        var instrumented = method.HasBody;
        var methodId = instrumented ? id++ : -1;
        catalog.Add(new
        {
            id = methodId, assembly = name, method = method.FullName,
            type = method.DeclaringType.FullName, name = method.Name,
            source = point?.Document.Url, line = point?.StartLine,
            category = method.Name == "MoveNext" ? "state-machine-segment" :
                method.IsConstructor ? "constructor" : method.IsGetter || method.IsSetter ? "accessor" :
                method.Name.Contains('<') || method.DeclaringType.Name.Contains('<') ? "compiler-generated" : "method",
            instrumented, reason = instrumented ? null : "No managed body (abstract/interface/external)."
        });
        if (instrumented) Instrument(method, methodId, enter, exit);
    }
    assembly.Write(path, new WriterParameters { WriteSymbols = hasSymbols,
        SymbolWriterProvider = hasSymbols ? new PortablePdbWriterProvider() : null });
    assemblies.Add(new { name, originalSha256 = before,
        instrumentedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
        instrumented = methods.Count(m => m.HasBody), withoutBody = methods.Count(m => !m.HasBody) });
}
File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { schemaVersion = 1,
    instrumentedMethods = id, assemblies, methods = catalog }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Instrumented {id} method bodies in {assemblies.Count} assemblies. Manifest: {manifestPath}");

static void Instrument(MethodDefinition method, int id, MethodReference enter, MethodReference exit)
{
    var body = method.Body;
    body.SimplifyMacros();
    if (body.Instructions.Any(i => i.OpCode.Code == Code.Jmp))
        throw new NotSupportedException($"Cannot safely instrument jmp in {method.FullName}.");
    var il = body.GetILProcessor();
    var original = body.Instructions.ToArray();
    var first = original[0];
    il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, id));
    il.InsertBefore(first, il.Create(OpCodes.Call, enter));
    VariableDefinition? result = null;
    // init setters return modreq(IsExternalInit) void, not MetadataType.Void directly.
    if (method.ReturnType.GetElementType().MetadataType != MetadataType.Void)
    {
        result = new VariableDefinition(method.ReturnType);
        body.Variables.Add(result);
    }
    body.InitLocals = true;
    var finallyStart = il.Create(OpCodes.Call, exit);
    var epilogue = result is null ? il.Create(OpCodes.Ret) : il.Create(OpCodes.Ldloc, result);
    foreach (var instruction in original)
    {
        if (instruction.OpCode.Code == Code.Tail) instruction.OpCode = OpCodes.Nop;
        if (instruction.OpCode.Code != Code.Ret) continue;
        if (result is null)
        {
            instruction.OpCode = OpCodes.Leave;
            instruction.Operand = epilogue;
        }
        else
        {
            instruction.OpCode = OpCodes.Stloc;
            instruction.Operand = result;
            il.InsertAfter(instruction, il.Create(OpCodes.Leave, epilogue));
        }
    }
    foreach (var handler in body.ExceptionHandlers)
    {
        handler.TryEnd ??= finallyStart;
        handler.HandlerEnd ??= finallyStart;
    }
    il.Append(finallyStart);
    il.Append(il.Create(OpCodes.Endfinally));
    il.Append(epilogue);
    if (result is not null) il.Append(il.Create(OpCodes.Ret));
    body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
    { TryStart = first, TryEnd = finallyStart, HandlerStart = finallyStart, HandlerEnd = epilogue });
    body.OptimizeMacros();
}
