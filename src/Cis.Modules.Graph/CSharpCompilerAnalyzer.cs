using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cis.Modules.Graph;

internal static class CSharpCompilerAnalyzer
{
    public static CSharpCompilerAnalysis Analyze(IReadOnlyList<CSharpCompilerInput> inputs)
    {
        if (inputs.Count == 0)
        {
            return new CSharpCompilerAnalysis([], [], [], [], []);
        }

        var inputByPath = inputs.ToDictionary(input => input.Path, StringComparer.OrdinalIgnoreCase);
        var syntaxTrees = inputs
            .Select(input => CSharpSyntaxTree.ParseText(
                input.Content,
                new CSharpParseOptions(LanguageVersion.Preview),
                input.Path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "Cis.Repository.CompilerAnalysis",
            syntaxTrees,
            RuntimeReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var symbols = new Dictionary<string, CSharpCompilerSymbol>(StringComparer.Ordinal);
        var declarations = new List<(ISymbol Symbol, SyntaxNode Declaration, CSharpCompilerSymbol Discovered)>();
        foreach (var syntaxTree in syntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            var root = syntaxTree.GetRoot();
            foreach (var declaration in DeclarationNodes(root))
            {
                var declared = model.GetDeclaredSymbol(declaration);
                if (declared is null
                    || !TryCreateSourceSymbol(declared, declaration, inputByPath, out var discovered))
                {
                    continue;
                }

                symbols.TryAdd(discovered.LocalId, discovered);
                declarations.Add((declared, declaration, discovered));
            }
        }

        var referenced = new Dictionary<string, CSharpCompilerReferencedSymbol>(StringComparer.Ordinal);
        var annotations = new Dictionary<string, CSharpCompilerRelation>(StringComparer.Ordinal);
        var interfaces = new Dictionary<string, CSharpCompilerRelation>(StringComparer.Ordinal);
        foreach (var item in declarations)
        {
            foreach (var attribute in EffectiveAttributes(item.Symbol))
            {
                if (attribute.AttributeClass is null)
                {
                    continue;
                }

                var target = CreateReferencedSymbol(attribute.AttributeClass, "attribute", AttributeSemantics(attribute.AttributeClass));
                referenced.TryAdd(target.LocalId, target);
                AddRelation(item.Discovered, target, "annotated-by", item.Declaration, annotations);
            }

            if (item.Symbol is not INamedTypeSymbol namedType)
            {
                continue;
            }

            foreach (var implemented in namedType.Interfaces)
            {
                var definition = implemented.OriginalDefinition;
                string targetLocalId;
                if (TryCreateSourceSymbol(definition, item.Declaration, inputByPath, out var sourceInterface))
                {
                    targetLocalId = sourceInterface.LocalId;
                }
                else
                {
                    var target = CreateReferencedSymbol(definition, "interface", InterfaceSemantics(definition));
                    referenced.TryAdd(target.LocalId, target);
                    targetLocalId = target.LocalId;
                }

                AddRelation(item.Discovered.LocalId, targetLocalId, "implements", item.Declaration, interfaces);
            }

            if (namedType.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
            {
                var definition = baseType.OriginalDefinition;
                string targetLocalId;
                if (TryCreateSourceSymbol(definition, item.Declaration, inputByPath, out var sourceBase))
                {
                    targetLocalId = sourceBase.LocalId;
                }
                else
                {
                    var target = CreateReferencedSymbol(definition, SymbolKind(definition, item.Declaration), []);
                    referenced.TryAdd(target.LocalId, target);
                    targetLocalId = target.LocalId;
                }

                AddRelation(item.Discovered.LocalId, targetLocalId, "inherits", item.Declaration, interfaces);
            }
        }

        var calls = new Dictionary<string, CSharpCompilerCall>(StringComparer.Ordinal);
        foreach (var syntaxTree in syntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            var root = syntaxTree.GetRoot();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                AddCall(model, invocation, invocation.Expression, invocation.ArgumentList.Arguments.Select(argument => argument.Expression), inputByPath, symbols, referenced, calls);
            }

            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                AddCall(model, creation, creation, creation.ArgumentList?.Arguments.Select(argument => argument.Expression) ?? [], inputByPath, symbols, referenced, calls);
            }
        }

        var orderedCalls = calls.Values
            .GroupBy(call => call.CallerLocalId, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(call => call.Path, StringComparer.Ordinal)
                .ThenBy(call => call.SpanStart)
                .Select((call, ordinal) => call with { Ordinal = ordinal + 1 }))
            .OrderBy(call => call.CallerLocalId, StringComparer.Ordinal)
            .ThenBy(call => call.Ordinal)
            .ToArray();

        return new CSharpCompilerAnalysis(
            symbols.Values.OrderBy(symbol => symbol.LocalId, StringComparer.Ordinal).ToArray(),
            referenced.Values.OrderBy(symbol => symbol.LocalId, StringComparer.Ordinal).ToArray(),
            annotations.Values.OrderBy(relation => relation.SourceLocalId, StringComparer.Ordinal).ThenBy(relation => relation.TargetLocalId, StringComparer.Ordinal).ToArray(),
            interfaces.Values.OrderBy(relation => relation.SourceLocalId, StringComparer.Ordinal).ThenBy(relation => relation.TargetLocalId, StringComparer.Ordinal).ToArray(),
            orderedCalls);
    }

    private static IEnumerable<SyntaxNode> DeclarationNodes(SyntaxNode root)
        => root.DescendantNodes().Where(node => node is BaseTypeDeclarationSyntax
            or DelegateDeclarationSyntax
            or BaseMethodDeclarationSyntax
            or LocalFunctionStatementSyntax
            or BasePropertyDeclarationSyntax
            or AccessorDeclarationSyntax);

    private static void AddCall(
        SemanticModel model,
        SyntaxNode callNode,
        SyntaxNode symbolNode,
        IEnumerable<ExpressionSyntax> arguments,
        IReadOnlyDictionary<string, CSharpCompilerInput> inputs,
        IDictionary<string, CSharpCompilerSymbol> symbols,
        IDictionary<string, CSharpCompilerReferencedSymbol> referenced,
        IDictionary<string, CSharpCompilerCall> calls)
    {
        var caller = model.GetEnclosingSymbol(callNode.SpanStart);
        var symbolInfo = model.GetSymbolInfo(symbolNode);
        var target = symbolInfo.Symbol as IMethodSymbol;
        if (target is null)
        {
            var candidates = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().Take(2).ToArray();
            target = candidates.Length == 1 ? candidates[0] : null;
        }
        if (caller is null || target is null
            || !TryCreateSourceSymbol(caller, callNode, inputs, out var callerSymbol))
        {
            return;
        }
        symbols.TryAdd(callerSymbol.LocalId, callerSymbol);

        var constructedTarget = target.ReducedFrom ?? target;
        var genericArguments = constructedTarget.TypeArguments
            .Select(argument => argument.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "?")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var definition = constructedTarget.OriginalDefinition;
        string targetLocalId;
        string targetName;
        string targetQualifiedName;
        var external = !TryCreateSourceSymbol(definition, callNode, inputs, out var targetSymbol);
        if (external)
        {
            var externalTarget = CreateReferencedSymbol(definition, SymbolKind(definition, callNode), CallSemantics(definition));
            referenced.TryAdd(externalTarget.LocalId, externalTarget);
            targetLocalId = externalTarget.LocalId;
            targetName = externalTarget.Name;
            targetQualifiedName = externalTarget.QualifiedName;
        }
        else
        {
            // Roslyn can resolve repository-owned implicit members, especially default
            // constructors, even though they have no explicit declaration node. Preserve
            // them as repository symbols instead of inventing an external compiler-analysis
            // assembly identity for them.
            symbols.TryAdd(targetSymbol.LocalId, targetSymbol);
            targetLocalId = targetSymbol.LocalId;
            targetName = targetSymbol.Name;
            targetQualifiedName = targetSymbol.QualifiedName;
        }

        var path = callNode.SyntaxTree.FilePath.Replace('\\', '/');
        var line = callNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var argumentKinds = arguments.Select(argument => ArgumentKind(model, argument)).ToArray();
        var semantics = CallSemantics(definition).ToHashSet(StringComparer.Ordinal);
        if (semantics.Contains("logging"))
        {
            var consoleWrite = definition.ContainingType?.Name == "Console";
            semantics.Add(!consoleWrite
                && (IsStructuredLoggingBoundary(definition)
                    || (!argumentKinds.Contains("interpolated-string", StringComparer.Ordinal)
                        && argumentKinds.Any(value => value is "constant-string" or "message-template")))
                ? "structured-logging"
                : "unstructured-logging");
        }

        var call = new CSharpCompilerCall(
            callerSymbol.LocalId,
            callerSymbol.Name,
            targetLocalId,
            targetName,
            targetQualifiedName,
            external,
            path,
            line,
            callNode.SpanStart,
            callNode.Span.Length,
            0,
            genericArguments,
            argumentKinds,
            semantics.Order(StringComparer.Ordinal).ToArray());
        var key = $"{call.CallerLocalId}\u001f{call.TargetLocalId}\u001f{call.Path}\u001f{call.SpanStart}\u001f{call.SpanLength}";
        calls.TryAdd(key, call);
    }

    private static bool TryCreateSourceSymbol(
        ISymbol symbol,
        SyntaxNode declaration,
        IReadOnlyDictionary<string, CSharpCompilerInput> inputs,
        out CSharpCompilerSymbol discovered)
    {
        discovered = default!;
        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (sourceLocation?.SourceTree is null)
        {
            return false;
        }

        var path = sourceLocation.SourceTree.FilePath.Replace('\\', '/');
        if (!inputs.TryGetValue(path, out var input))
        {
            return false;
        }

        var definition = symbol.OriginalDefinition;
        var kind = SymbolKind(definition, declaration);
        var qualifiedName = definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return false;
        }

        var localId = string.Join('/', Encode(input.ComponentId), "csharp", Encode(kind), Encode(qualifiedName));
        var attributes = EffectiveAttributes(definition)
            .Select(attribute => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var interfaces = EffectiveInterfaces(definition)
            .Select(value => value.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var generatedReasons = GeneratedReasons(definition, input).ToArray();
        var facets = SymbolFacets(definition, attributes, interfaces, generatedReasons.Length > 0);
        discovered = new CSharpCompilerSymbol(
            localId,
            qualifiedName,
            definition.Name,
            kind,
            definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            input.ComponentId,
            path,
            sourceLocation.GetLineSpan().StartLinePosition.Line + 1,
            attributes,
            interfaces,
            facets,
            generatedReasons);
        return true;
    }

    private static CSharpCompilerReferencedSymbol CreateReferencedSymbol(
        ISymbol symbol,
        string kind,
        IReadOnlyList<string> semantics)
    {
        var definition = symbol.OriginalDefinition;
        var qualifiedName = definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            qualifiedName = definition.Name;
        }
        var assembly = definition.ContainingAssembly?.Identity.Name ?? "unresolved";
        var localId = string.Join('/', "external", "csharp", Encode(kind), Encode(assembly), Encode(qualifiedName));
        return new CSharpCompilerReferencedSymbol(
            localId,
            qualifiedName,
            definition.Name,
            kind,
            definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            assembly,
            semantics.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static void AddRelation(
        CSharpCompilerSymbol source,
        CSharpCompilerReferencedSymbol target,
        string type,
        SyntaxNode declaration,
        IDictionary<string, CSharpCompilerRelation> relations)
        => AddRelation(source.LocalId, target.LocalId, type, declaration, relations);

    private static void AddRelation(
        string sourceLocalId,
        string targetLocalId,
        string type,
        SyntaxNode declaration,
        IDictionary<string, CSharpCompilerRelation> relations)
    {
        var path = declaration.SyntaxTree.FilePath.Replace('\\', '/');
        var line = declaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var relation = new CSharpCompilerRelation(sourceLocalId, targetLocalId, type, path, line);
        relations.TryAdd($"{sourceLocalId}\u001f{type}\u001f{targetLocalId}", relation);
    }

    private static IEnumerable<AttributeData> EffectiveAttributes(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            yield return attribute;
        }

        if (symbol.ContainingType is null)
        {
            yield break;
        }

        foreach (var attribute in symbol.ContainingType.GetAttributes())
        {
            yield return attribute;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EffectiveInterfaces(ISymbol symbol)
        => symbol switch
        {
            INamedTypeSymbol named => named.AllInterfaces,
            _ when symbol.ContainingType is not null => symbol.ContainingType.AllInterfaces,
            _ => [],
        };

    private static IReadOnlyList<string> SymbolFacets(
        ISymbol symbol,
        IReadOnlyList<string> attributes,
        IReadOnlyList<string> interfaces,
        bool generated)
    {
        var facets = new SortedSet<string>(StringComparer.Ordinal);
        if (generated) facets.Add("generated");
        if (attributes.Any(IsEndpointAttribute)) facets.Add("http-endpoint");
        if (attributes.Any(IsAuthorizationAttribute)) facets.Add("authorization-boundary");
        if (interfaces.Any(IsHandlerInterface) || symbol.ContainingType?.Name.EndsWith("Handler", StringComparison.Ordinal) == true) facets.Add("application-handler");
        if (symbol.ContainingType?.Name.Contains("CommandHandler", StringComparison.OrdinalIgnoreCase) == true
            || interfaces.Any(value => value.Contains("ICommandHandler", StringComparison.OrdinalIgnoreCase))) facets.Add("command-handler");
        if (interfaces.Any(IsConsumerInterface)) facets.Add("message-consumer");
        if (interfaces.Any(IsRepositoryInterface) || symbol.ContainingType?.Name.EndsWith("Repository", StringComparison.Ordinal) == true) facets.Add("repository-boundary");
        return facets.ToArray();
    }

    private static IEnumerable<string> GeneratedReasons(ISymbol symbol, CSharpCompilerInput input)
    {
        if (symbol.IsImplicitlyDeclared) yield return "compiler-implicit";
        if (input.Path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || input.Path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || input.Path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
            yield return "generated-file-name";
        if (input.Content.AsSpan(0, Math.Min(input.Content.Length, 512)).Contains("<auto-generated", StringComparison.OrdinalIgnoreCase))
            yield return "auto-generated-header";
        if (EffectiveAttributes(symbol).Any(attribute => attribute.AttributeClass is not null && IsGeneratedAttribute(attribute.AttributeClass.Name)))
            yield return "generated-attribute";
    }

    private static IReadOnlyList<string> AttributeSemantics(INamedTypeSymbol attribute)
    {
        var result = new List<string>();
        if (IsEndpointAttribute(attribute.Name)) result.Add("http-endpoint");
        if (IsAuthorizationAttribute(attribute.Name)) result.Add("authorization");
        if (IsGeneratedAttribute(attribute.Name)) result.Add("generated");
        return result;
    }

    private static IReadOnlyList<string> InterfaceSemantics(INamedTypeSymbol type)
    {
        var result = new List<string>();
        var name = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (IsHandlerInterface(name)) result.Add("application-handler");
        if (IsConsumerInterface(name)) result.Add("message-consumer");
        if (IsRepositoryInterface(name)) result.Add("repository");
        return result;
    }

    private static IReadOnlyList<string> CallSemantics(IMethodSymbol method)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var qualified = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var container = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty;
        var typeName = method.ContainingType?.Name ?? string.Empty;
        var name = method.Name;
        if ((container.Contains("ILogger", StringComparison.OrdinalIgnoreCase)
                && name.StartsWith("Log", StringComparison.Ordinal))
            || (typeName.Contains("Logger", StringComparison.OrdinalIgnoreCase)
                && (name.StartsWith("Log", StringComparison.Ordinal)
                    || name is "Write" or "Trace" or "Debug" or "Information" or "Info" or "Warning" or "Warn" or "Error" or "Fatal"))
            || (typeName.Equals("Log", StringComparison.OrdinalIgnoreCase) && name == "Write")
            || (typeName.Equals("Console", StringComparison.Ordinal) && name is "Write" or "WriteLine"))
            result.Add("logging");
        var optionsOrConfigurationBoundary = container.Contains("Options", StringComparison.OrdinalIgnoreCase)
            || container.Contains("Configuration", StringComparison.OrdinalIgnoreCase)
            || qualified.Contains("Microsoft.Extensions.DependencyInjection", StringComparison.OrdinalIgnoreCase);
        if (optionsOrConfigurationBoundary
            && name is "AddOptions" or "Configure" or "PostConfigure" or "BindConfiguration")
        {
            result.Add("configuration");
            if (method.IsGenericMethod || name == "BindConfiguration") result.Add("options-binding");
        }
        if (container.Contains("IConfiguration", StringComparison.OrdinalIgnoreCase)
            && name is "get_Item" or "GetValue" or "GetSection" or "Bind")
        {
            result.Add("configuration");
            if (name is "get_Item" or "GetValue") result.Add("configuration-read");
        }
        if (name is "RequireAuthorization" or "AuthorizeAsync") result.Add("authorization");
        if (name is "MapGet" or "MapPost" or "MapPut" or "MapPatch" or "MapDelete" or "MapMethods")
            result.Add("http-endpoint-registration");
        if (IsPersistenceContainer(container))
        {
            result.Add("persistence");
            if (name is "SaveChanges" or "SaveChangesAsync" or "Commit" or "CommitAsync" or "Complete" or "CompleteAsync") result.Add("transaction-commit");
            else if (name is "Add" or "AddAsync" or "Update" or "Remove" or "Delete" or "Insert" or "InsertAsync" or "Store" or "StoreAsync") result.Add("state-write");
        }
        if ((container.Contains("Outbox", StringComparison.OrdinalIgnoreCase) || qualified.Contains("Outbox", StringComparison.OrdinalIgnoreCase))
            && name is "Add" or "AddAsync" or "Enqueue" or "EnqueueAsync" or "Store" or "StoreAsync")
            result.Add("outbox-write");
        if ((container.Contains("Publisher", StringComparison.OrdinalIgnoreCase) || container.Contains("EventBus", StringComparison.OrdinalIgnoreCase))
            && name is "Publish" or "PublishAsync" or "Send" or "SendAsync")
            result.Add("event-publish");
        if ((container.Contains("Idempot", StringComparison.OrdinalIgnoreCase) || container.Contains("Dedup", StringComparison.OrdinalIgnoreCase))
            && name is "Exists" or "ExistsAsync" or "TryBegin" or "TryBeginAsync" or "IsProcessed" or "IsProcessedAsync" or "Record" or "RecordAsync")
            result.Add("idempotency-check");
        return result.ToArray();
    }

    private static bool IsStructuredLoggingBoundary(IMethodSymbol method)
    {
        var parameters = method.Parameters;
        var hasEventIdentity = parameters.Any(parameter =>
            parameter.Name.Contains("event", StringComparison.OrdinalIgnoreCase)
            || parameter.Type.SpecialType is SpecialType.System_Int16
                or SpecialType.System_Int32
                or SpecialType.System_Int64);
        var hasPropertyBag = parameters.Any(parameter =>
        {
            var type = parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return type.Contains("Dictionary<", StringComparison.Ordinal)
                || type.Contains("IReadOnlyDictionary<", StringComparison.Ordinal)
                || type.Contains("IEnumerable<KeyValuePair<", StringComparison.Ordinal);
        });
        return hasEventIdentity && hasPropertyBag;
    }

    private static string ArgumentKind(SemanticModel model, ExpressionSyntax argument)
    {
        if (argument is InterpolatedStringExpressionSyntax) return "interpolated-string";
        if (argument is LiteralExpressionSyntax literal)
        {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) return "literal";
            var value = model.GetConstantValue(argument);
            return value.HasValue && value.Value is string text && text.Contains('{', StringComparison.Ordinal)
                ? "message-template"
                : "constant-string";
        }
        if (argument is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax or AnonymousMethodExpressionSyntax) return "lambda";
        if (argument is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax) return "object-creation";
        if (argument is IdentifierNameSyntax) return "identifier";
        if (argument is MemberAccessExpressionSyntax) return "member-access";
        if (argument.IsKind(SyntaxKind.NullLiteralExpression)) return "null";
        return argument.Kind().ToString().Replace("Expression", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string SymbolKind(ISymbol symbol, SyntaxNode declaration)
        => symbol switch
        {
            INamedTypeSymbol when declaration is RecordDeclarationSyntax => "record",
            INamedTypeSymbol named => named.TypeKind switch
            {
                TypeKind.Class => "class",
                TypeKind.Struct => "struct",
                TypeKind.Interface => "interface",
                TypeKind.Enum => "enum",
                TypeKind.Delegate => "delegate",
                _ => "type",
            },
            IMethodSymbol method => method.MethodKind switch
            {
                MethodKind.Constructor or MethodKind.StaticConstructor => "constructor",
                MethodKind.UserDefinedOperator or MethodKind.Conversion => "operator",
                MethodKind.LocalFunction => "local-function",
                MethodKind.PropertyGet or MethodKind.PropertySet => "accessor",
                _ => "method",
            },
            IPropertySymbol property when property.IsIndexer => "indexer",
            IPropertySymbol => "property",
            IEventSymbol => "event",
            _ => symbol.Kind.ToString().ToLowerInvariant(),
        };

    private static bool IsEndpointAttribute(string value)
        => value.Contains("HttpGet", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HttpPost", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HttpPut", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HttpPatch", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HttpDelete", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("RouteAttribute", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthorizationAttribute(string value)
        => value.EndsWith("AuthorizeAttribute", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Permission", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedAttribute(string value)
        => value.EndsWith("GeneratedCodeAttribute", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("CompilerGeneratedAttribute", StringComparison.OrdinalIgnoreCase);

    private static bool IsHandlerInterface(string value)
        => value.Contains("IRequestHandler", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ICommandHandler", StringComparison.OrdinalIgnoreCase)
            || value.Contains("IQueryHandler", StringComparison.OrdinalIgnoreCase);

    private static bool IsConsumerInterface(string value)
        => value.Contains("IConsumer", StringComparison.OrdinalIgnoreCase)
            || value.Contains("IMessageHandler", StringComparison.OrdinalIgnoreCase)
            || value.Contains("IEventHandler", StringComparison.OrdinalIgnoreCase);

    private static bool IsRepositoryInterface(string value)
        => value.Contains("IRepository", StringComparison.OrdinalIgnoreCase)
            || value.Contains("IUnitOfWork", StringComparison.OrdinalIgnoreCase);

    private static bool IsPersistenceContainer(string value)
        => value.Contains("DbContext", StringComparison.OrdinalIgnoreCase)
            || value.Contains("DbSet", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Repository", StringComparison.OrdinalIgnoreCase)
            || value.Contains("UnitOfWork", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<MetadataReference> RuntimeReferences()
    {
        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
                     ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            paths.Add(path);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location)) paths.Add(assembly.Location);
            }
            catch (NotSupportedException)
            {
                // Dynamic or host-provided assemblies do not expose stable metadata locations.
            }
        }
        return paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static string Encode(string value)
        => Uri.EscapeDataString(value);
}

internal sealed record CSharpCompilerInput(
    string Path,
    string Content,
    string ComponentId);

internal sealed record CSharpCompilerSymbol(
    string LocalId,
    string QualifiedName,
    string Name,
    string Kind,
    string Signature,
    string ComponentId,
    string Path,
    int Line,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> Facets,
    IReadOnlyList<string> GeneratedReasons);

internal sealed record CSharpCompilerReferencedSymbol(
    string LocalId,
    string QualifiedName,
    string Name,
    string Kind,
    string Signature,
    string Assembly,
    IReadOnlyList<string> Semantics);

internal sealed record CSharpCompilerRelation(
    string SourceLocalId,
    string TargetLocalId,
    string Type,
    string Path,
    int Line);

internal sealed record CSharpCompilerCall(
    string CallerLocalId,
    string CallerName,
    string TargetLocalId,
    string TargetName,
    string TargetQualifiedName,
    bool TargetExternal,
    string Path,
    int Line,
    int SpanStart,
    int SpanLength,
    int Ordinal,
    IReadOnlyList<string> GenericArguments,
    IReadOnlyList<string> ArgumentKinds,
    IReadOnlyList<string> Semantics);

internal sealed record CSharpCompilerAnalysis(
    IReadOnlyList<CSharpCompilerSymbol> Symbols,
    IReadOnlyList<CSharpCompilerReferencedSymbol> ReferencedSymbols,
    IReadOnlyList<CSharpCompilerRelation> Annotations,
    IReadOnlyList<CSharpCompilerRelation> Interfaces,
    IReadOnlyList<CSharpCompilerCall> Calls);
