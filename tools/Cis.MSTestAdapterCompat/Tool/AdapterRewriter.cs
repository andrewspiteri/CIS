using Cis.MSTestAdapterCompat.Runtime;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cis.MSTestAdapterCompat.Tool;

internal static class AdapterRewriter
{
    private const string AdapterNamespace = "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter";

    internal sealed record Result(byte[] Bytes, string[] Changes);

    internal static Result Rewrite(byte[] original)
    {
        using var input = new MemoryStream(original, writable: false);
        using var module = ModuleDefinition.ReadModule(input, new ReaderParameters { InMemory = true, ReadSymbols = false });
        var token = module.GetType(AdapterNamespace + ".TestRunCancellationToken") ?? throw Unsupported("token type");
        var manager = module.GetType(AdapterNamespace + ".Execution.TestExecutionManager") ?? throw Unsupported("manager type");
        var assemblyInfo = module.GetType(AdapterNamespace + ".Execution.TestAssemblyInfo") ?? throw Unsupported("assembly info type");
        var ownedField = token.Fields.Single(field => field.Name == "_cancellationTokenSource");
        var originalField = token.Fields.Single(field => field.Name == "_originalCancellationToken");
        var managerToken = manager.Fields.Single(field => field.Name == "_testRunCancellationToken");
        var throwMethod = token.Methods.Single(method => method.Name == "ThrowIfCancellationRequested");
        var bridge = AddOwnedSourceBridge(module, token, ownedField);
        var originalBridge = AddOriginalTokenBridge(token, originalField);

        throwMethod.Body = new MethodBody(throwMethod);
        var tokenIl = throwMethod.Body.GetILProcessor();
        tokenIl.Emit(OpCodes.Ldarg_0);
        tokenIl.Emit(OpCodes.Ldfld, ownedField);
        tokenIl.Emit(OpCodes.Ldarg_0);
        tokenIl.Emit(OpCodes.Ldfld, originalField);
        tokenIl.Emit(OpCodes.Call, Shim(module, nameof(CompatibilityRuntime.ThrowIfCancellationRequested), 2));
        tokenIl.Emit(OpCodes.Ret);

        var sendResults = MoveNext(manager, "SendTestResultsAsync");
        RemoveAdmissionChecks(sendResults, throwMethod, expected: 2);
        var parallel = MoveNext(manager, "ExecuteParallelizedTestsAsync");
        RemoveAdmissionChecks(parallel, throwMethod, expected: 1);
        RequireCallCount(parallel, "System.Threading.Tasks.Task", "WhenAll", expected: 1);
        var dependency = MoveNext(manager, "ExecuteChunksInTopologicalOrderAsync");
        RemoveAdmissionChecks(dependency, throwMethod, expected: 1);
        RequireCallCount(dependency, "System.Threading.Tasks.Task", "WhenAll", expected: 1);

        var source = MoveNext(manager, "ExecuteTestsInSourceAsync");
        var managerThis = source.DeclaringType.Fields.Single(field => field.Name == "<>4__this");
        var sourceIl = source.Body.GetILProcessor();
        var dispatches = source.Body.Instructions.Where(instruction => instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == manager.FullName &&
            method.Name is "ExecuteTestsWithDependencyGraphAsync" or "ExecuteParallelizedTestsAsync" or "ExecuteTestsWithTestRunnerAsync").ToArray();
        if (dispatches.Length != 4 || dispatches.Any(instruction => ((MethodReference)instruction.Operand).ReturnType.FullName != "System.Threading.Tasks.Task"))
        {
            throw Unsupported("four joined source dispatch tasks");
        }
        foreach (var dispatch in dispatches)
        {
            InsertAfter(sourceIl, dispatch,
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, managerThis),
                Instruction.Create(OpCodes.Ldfld, managerToken), Instruction.Create(OpCodes.Call, bridge),
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, managerThis),
                Instruction.Create(OpCodes.Ldfld, managerToken), Instruction.Create(OpCodes.Call, originalBridge),
                Instruction.Create(OpCodes.Call, Shim(module, nameof(CompatibilityRuntime.ObserveCancellationAsync), 3)));
        }

        var graceful = source.Body.Instructions.Single(instruction => instruction.Operand is MethodReference method && method.Name == "get_IsGracefulStopRequested");
        InsertAfter(sourceIl, graceful,
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, managerThis),
            Instruction.Create(OpCodes.Ldfld, managerToken), Instruction.Create(OpCodes.Call, bridge),
            Instruction.Create(OpCodes.Call, Shim(module, nameof(CompatibilityRuntime.IsOwnedCancellationRequested), 1)),
            Instruction.Create(OpCodes.Or));

        AddCleanupWrapper(module, assemblyInfo);
        AddCleanupContextFactory(module, originalBridge);
        foreach (var modified in new[] { throwMethod, sendResults, parallel, dependency, source })
        {
            ExpandShortBranches(modified);
        }

        // Cecil emits managed IL rather than retaining the original ReadyToRun native code.
        using var output = new MemoryStream();
        module.Write(output);
        return new Result(output.ToArray(),
        [
            "Check both owned and original cancellation tokens.",
            "Preserve completed-result delivery: remove only its two cancellation checks.",
            "Join all parallel and dependency workers: remove only their outer creation checks.",
            "Observe four joined source dispatch tasks; accept only owned queue markers while preserving external cancellation after joining.",
            "Run existing forced cleanup for graceful stop or owned-token cancellation.",
            "Run initialized assembly cleanup once per adapter info instance; first caller owns its result/output.",
            "Create only normal class/assembly cleanup contexts independently of queue cancellation, retaining the original external-token registration."
        ]);
    }

    private static MethodDefinition AddOriginalTokenBridge(TypeDefinition token, FieldDefinition original)
    {
        var bridge = new MethodDefinition("CISCompat_GetOriginalToken", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, original.FieldType);
        bridge.Parameters.Add(new ParameterDefinition("token", ParameterAttributes.None, token));
        token.Methods.Add(bridge);
        bridge.Body.InitLocals = true;
        var empty = new VariableDefinition(original.FieldType);
        bridge.Body.Variables.Add(empty);
        var il = bridge.Body.GetILProcessor();
        var load = Instruction.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, load);
        il.Emit(OpCodes.Ldloca, empty);
        il.Emit(OpCodes.Initobj, original.FieldType);
        il.Emit(OpCodes.Ldloc, empty);
        il.Emit(OpCodes.Ret);
        il.Append(load);
        il.Emit(OpCodes.Ldfld, original);
        il.Emit(OpCodes.Ret);
        return bridge;
    }

    private static void AddCleanupContextFactory(ModuleDefinition module, MethodDefinition originalTokenBridge)
    {
        var provider = module.GetType(AdapterNamespace + ".PlatformServiceProvider") ?? throw Unsupported("context provider");
        var providerInterface = module.GetType(AdapterNamespace + ".IPlatformServiceProvider") ?? throw Unsupported("context provider interface");
        var context = module.GetType("Microsoft.VisualStudio.TestPlatform.MSTestAdapter.PlatformServices.TestContextImplementation") ?? throw Unsupported("context implementation");
        var originalFactory = providerInterface.Methods.Single(method => method.Name == "GetTestContext");
        var cleanupCalls = AllTypes(module.Types).Where(type => type.Name.StartsWith("<RunSingleTestAsync>", StringComparison.Ordinal))
            .SelectMany(type => type.Methods).Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions.Select(instruction => (Method: method, Instruction: instruction)))
            .Where(item => item.Instruction.Operand is MethodReference reference && reference.FullName == originalFactory.FullName &&
                item.Instruction.Next is { OpCode.Code: Code.Stfld, Operand: FieldReference field } &&
                (field.Name.StartsWith("<testContextForClassCleanup>", StringComparison.Ordinal) || field.Name.StartsWith("<testContextForAssemblyCleanup>", StringComparison.Ordinal)))
            .ToArray();
        if (cleanupCalls.Length != 2)
        {
            throw Unsupported("exactly two normal cleanup context factories");
        }

        var registration = context.Fields.Single(field => field.Name == "_cancellationTokenRegistration");
        var initialize = new MethodDefinition("CISCompat_RegisterExternalCleanupCancellation", MethodAttributes.Assembly | MethodAttributes.HideBySig, module.TypeSystem.Void);
        initialize.Parameters.Add(new ParameterDefinition("original", ParameterAttributes.None, originalTokenBridge.ReturnType));
        context.Methods.Add(initialize);
        var sourceGetter = module.GetMemberReferences().OfType<MethodReference>().First(method => method.Name == "get_CancellationTokenSource" && method.DeclaringType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestContext");
        var initIl = initialize.Body.GetILProcessor();
        initIl.Emit(OpCodes.Ldarg_0);
        initIl.Emit(OpCodes.Ldarg_0);
        initIl.Emit(OpCodes.Callvirt, sourceGetter);
        initIl.Emit(OpCodes.Ldarg_1);
        initIl.Emit(OpCodes.Call, Shim(module, nameof(CompatibilityRuntime.RegisterCleanupCancellation), 2));
        initIl.Emit(OpCodes.Stfld, registration);
        initIl.Emit(OpCodes.Ret);

        var factory = new MethodDefinition("CISCompat_GetCleanupTestContext", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, originalFactory.ReturnType);
        factory.Parameters.Add(new ParameterDefinition("provider", ParameterAttributes.None, providerInterface));
        foreach (var parameter in originalFactory.Parameters)
        {
            factory.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
        }
        provider.Methods.Add(factory);
        factory.Body.InitLocals = true;
        var contextLocal = new VariableDefinition(context);
        factory.Body.Variables.Add(contextLocal);
        var il = factory.Body.GetILProcessor();
        for (var index = 1; index <= 4; index++)
        {
            il.Emit(OpCodes.Ldarg, factory.Parameters[index]);
        }
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, context.Methods.Single(method => method.Name == ".ctor" && method.Parameters.Count == 5));
        il.Emit(OpCodes.Stloc, contextLocal);
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Ldarg, factory.Parameters[5]);
        il.Emit(OpCodes.Callvirt, context.Methods.Single(method => method.Name == "SetOutcome"));
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, providerInterface.Methods.Single(method => method.Name == "get_TestRunCancellationToken"));
        il.Emit(OpCodes.Call, originalTokenBridge);
        il.Emit(OpCodes.Call, initialize);
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Ret);

        foreach (var call in cleanupCalls)
        {
            call.Instruction.OpCode = OpCodes.Call;
            call.Instruction.Operand = factory;
            ExpandShortBranches(call.Method);
        }
    }

    private static MethodDefinition AddOwnedSourceBridge(ModuleDefinition module, TypeDefinition token, FieldDefinition owned)
    {
        var bridge = new MethodDefinition("CISCompat_GetOwnedSource", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, owned.FieldType);
        bridge.Parameters.Add(new ParameterDefinition("token", ParameterAttributes.None, token));
        token.Methods.Add(bridge);
        var il = bridge.Body.GetILProcessor();
        var load = Instruction.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, load);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.Append(load);
        il.Emit(OpCodes.Ldfld, owned);
        il.Emit(OpCodes.Ret);
        return bridge;
    }

    private static void AddCleanupWrapper(ModuleDefinition module, TypeDefinition assemblyInfo)
    {
        var original = assemblyInfo.Methods.Single(method => method.Name == "ExecuteAssemblyCleanupAsync");
        var originalName = original.FullName;
        var callsites = AllTypes(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction => instruction.Operand is MethodReference reference && reference.FullName == originalName).ToArray();
        if (callsites.Length != 2 || original.Parameters.Count != 1 || original.ReturnType is not GenericInstanceType taskType)
        {
            throw Unsupported("two assembly cleanup callsites");
        }

        var wrapper = new MethodDefinition(original.Name, original.Attributes, original.ReturnType);
        var context = original.Parameters[0].ParameterType;
        wrapper.Parameters.Add(new ParameterDefinition("context", ParameterAttributes.None, context));
        assemblyInfo.Methods.Add(wrapper);
        original.Name = "CISCompat_ExecuteAssemblyCleanupOriginalAsync";
        original.Attributes = (original.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Private;
        foreach (var callsite in callsites)
        {
            callsite.Operand = wrapper;
        }

        var cleanup = new GenericInstanceMethod(Shim(module, nameof(CompatibilityRuntime.CleanupOnceAsync), 4));
        cleanup.GenericArguments.Add(context);
        cleanup.GenericArguments.Add(taskType.GenericArguments.Single());
        var function = new GenericInstanceType(module.ImportReference(typeof(Func<,>)));
        function.GenericArguments.Add(context);
        function.GenericArguments.Add(original.ReturnType);
        var constructor = new MethodReference(".ctor", module.TypeSystem.Void, function) { HasThis = true };
        constructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        constructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
        var il = wrapper.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, assemblyInfo.Methods.Single(method => method.Name == "get_IsAssemblyInitializeExecuted"));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, original);
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Call, cleanup);
        il.Emit(OpCodes.Ret);

        var available = assemblyInfo.Methods.Single(method => method.Name == "get_HasExecutableCleanupMethod");
        var returns = available.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray();
        if (returns.Length != 1)
        {
            throw Unsupported("assembly cleanup availability getter");
        }
        var getterIl = available.Body.GetILProcessor();
        foreach (var instruction in new[]
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, Shim(module, nameof(CompatibilityRuntime.CleanupCompleted), 1)),
            Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Ceq), Instruction.Create(OpCodes.And)
        })
        {
            getterIl.InsertBefore(returns[0], instruction);
        }
    }

    private static void RemoveAdmissionChecks(MethodDefinition method, MethodDefinition tokenThrow, int expected)
    {
        var calls = method.Body.Instructions.Where(instruction => instruction.Operand is MethodReference reference && reference.FullName == tokenThrow.FullName).ToArray();
        if (calls.Length != expected)
        {
            throw Unsupported($"{method.DeclaringType.Name} cancellation-check count");
        }
        foreach (var call in calls)
        {
            call.OpCode = OpCodes.Pop;
            call.Operand = null;
        }
    }

    private static void RequireCallCount(MethodDefinition method, string type, string name, int expected)
    {
        if (method.Body.Instructions.Count(instruction => instruction.Operand is MethodReference reference && reference.DeclaringType.FullName == type && reference.Name == name) != expected)
        {
            throw Unsupported($"{method.DeclaringType.Name} worker join");
        }
    }

    private static MethodReference Shim(ModuleDefinition module, string name, int parameters) =>
        module.ImportReference(typeof(CompatibilityRuntime).GetMethods().Single(method => method.Name == name && method.GetParameters().Length == parameters));

    private static MethodDefinition MoveNext(TypeDefinition owner, string name) =>
        owner.NestedTypes.Single(type => type.Name.StartsWith("<" + name + ">", StringComparison.Ordinal)).Methods.Single(method => method.Name == "MoveNext");

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes))
            {
                yield return nested;
            }
        }
    }

    private static void InsertAfter(ILProcessor il, Instruction anchor, params Instruction[] instructions)
    {
        foreach (var instruction in instructions)
        {
            il.InsertAfter(anchor, instruction);
            anchor = instruction;
        }
    }

    private static void ExpandShortBranches(MethodDefinition method)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            instruction.OpCode = instruction.OpCode.Code switch
            {
                Code.Br_S => OpCodes.Br, Code.Brfalse_S => OpCodes.Brfalse, Code.Brtrue_S => OpCodes.Brtrue,
                Code.Beq_S => OpCodes.Beq, Code.Bge_S => OpCodes.Bge, Code.Bge_Un_S => OpCodes.Bge_Un,
                Code.Bgt_S => OpCodes.Bgt, Code.Bgt_Un_S => OpCodes.Bgt_Un, Code.Ble_S => OpCodes.Ble,
                Code.Ble_Un_S => OpCodes.Ble_Un, Code.Blt_S => OpCodes.Blt, Code.Blt_Un_S => OpCodes.Blt_Un,
                Code.Bne_Un_S => OpCodes.Bne_Un, Code.Leave_S => OpCodes.Leave, _ => instruction.OpCode
            };
        }
    }

    private static InvalidOperationException Unsupported(string detail) => new("Unsupported pinned adapter layout: " + detail + ".");
}
