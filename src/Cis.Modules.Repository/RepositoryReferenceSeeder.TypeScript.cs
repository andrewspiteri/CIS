using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

internal static partial class RepositoryReferenceSeeder
{
    private sealed record Decorator(string Name, string Arguments, int Start, int End);
    private sealed record PersistentField(string Entity, string Field, string Type, Decorator Declaration, string Path);
    private static readonly Regex DecoratorStart = new(@"\G@(?<name>[A-Za-z_]\w*)\s*\(", RegexOptions.Compiled);

    // Read declarations only. Never load target packages or execute application code.
    private static IEnumerable<Decorator> TypeScriptDecorators(string source)
    {
        var text = WithoutComments(source);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '\'' or '"' or '`')
            {
                var quote = text[index++];
                while (index < text.Length && text[index] != quote) { if (text[index] == '\\') index++; index++; }
                continue;
            }
            if (text[index] != '@') continue;
            var match = DecoratorStart.Match(text, index);
            if (!match.Success) continue;
            var open = match.Index + match.Length - 1;
            var end = ClosingDelimiter(text, open, '(', ')');
            if (end < 0) continue;
            yield return new(match.Groups["name"].Value, text[(open + 1)..end], match.Index, end + 1);
            index = end;
        }
    }

    private static string WithoutComments(string text)
        => Regex.Replace(text, "(['\"`])(?:\\\\.|(?!\\1)[^\\\\])*?\\1|//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/",
            match => match.Value.StartsWith('/') ? Regex.Replace(match.Value, @"[^\r\n]", " ") : match.Value);

    private static int ClosingDelimiter(string text, int start, char open, char close)
    {
        var depth = 0; char quote = '\0';
        for (var index = start; index < text.Length; index++)
        {
            var current = text[index];
            if (quote != '\0')
            {
                if (current == '\\') index++;
                else if (current == quote) quote = '\0';
                continue;
            }
            if (current is '\'' or '"' or '`') { quote = current; continue; }
            if (current == open) depth++;
            else if (current == close && --depth == 0) return index;
        }
        return -1;
    }

    private static string? Literal(string text)
    {
        var match = Regex.Match(text.Trim(), "^['\"](?<value>[^'\"]*)['\"]$");
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string Option(string text, string key)
    {
        var match = Regex.Match(text, @"\b" + key + "\\s*:\\s*(?<value>['\"][^'\"]*['\"]|[A-Za-z0-9_.-]+)");
        return match.Success ? match.Groups["value"].Value.Trim('\'', '"') : string.Empty;
    }

    private static IEnumerable<IReadOnlyList<string>> SeedNestApis(string root, IReadOnlyList<string> files, RepositoryClassification classification)
    {
        foreach (var path in files.Where(IsExpressSource))
        {
            var text = WithoutComments(TryReadText(path));
            if (!text.Contains("@nestjs/common", StringComparison.Ordinal)) continue;
            var decorators = TypeScriptDecorators(text).ToArray();
            var previousClassEnd = 0;
            foreach (Match declaration in Regex.Matches(text, @"\bclass\s+(?<name>\w+)[^{]*\{"))
            {
                var end = ClosingDelimiter(text, declaration.Index + declaration.Length - 1, '{', '}');
                if (end < 0) continue;
                var classDecorators = decorators.Where(item => item.Start >= previousClassEnd && item.End <= declaration.Index).ToArray();
                previousClassEnd = end + 1;
                var controller = classDecorators.LastOrDefault(item => item.Name == "Controller");
                if (controller is null) continue;
                var prefix = controller.Arguments.Trim().Length == 0 ? "" : Literal(controller.Arguments);
                if (prefix is null) continue; // Computed/object/array routes need a framework-aware provider.
                var routes = decorators.Where(item => item.Start > declaration.Index && item.End < end
                    && item.Name is "Get" or "Post" or "Put" or "Patch" or "Delete" or "Options" or "Head" or "All").ToArray();
                var component = FindComponent(root, path, classification);
                for (var index = 0; index < routes.Length; index++)
                {
                    var route = routes[index];
                    var suffix = route.Arguments.Trim().Length == 0 ? "" : Literal(route.Arguments);
                    if (suffix is null) continue;
                    var stop = index + 1 < routes.Length ? routes[index + 1].Start : end;
                    var signatureStart = route.End;
                    foreach (var decorator in decorators.Where(item => item.Start >= signatureStart && item.End < stop).OrderBy(item => item.Start))
                    {
                        if (text[signatureStart..decorator.Start].Trim().Length > 0) break;
                        signatureStart = decorator.End;
                    }
                    var following = text[signatureStart..stop];
                    // Restrict metadata to this method's signature/decorators, never its implementation body.
                    var method = Regex.Match(following, @"^\s*(?:(?:public|protected|private|async|static)\s+)*(?<name>[A-Za-z_]\w*)\s*\(");
                    if (!method.Success) continue;
                    signatureStart += method.Index;
                    var argumentsStart = signatureStart + method.Length - 1;
                    var argumentsEnd = ClosingDelimiter(text, argumentsStart, '(', ')');
                    if (argumentsEnd < 0 || argumentsEnd >= stop) continue;
                    var metadata = decorators.Where(item => item.Start >= route.End && item.End <= argumentsEnd + 1).ToArray();
                    var permission = classDecorators.Concat(metadata).Where(item => item.Name is "UseGuards" or "RequirePermissions" or "AllowedUserTypes")
                        .Select(item => item.Name + "(" + Regex.Replace(item.Arguments, @"\s+", " ").Trim() + ")").ToArray();
                    var request = Regex.Matches(text[(argumentsStart + 1)..argumentsEnd], @"@(?:Body|Query|Param)\([^)]*\)\s*\w+\??\s*:\s*([\w.<>\[\]]+)")
                        .Select(item => item.Value.Trim()).Concat(metadata.Where(item => item.Name == "ApiBody")
                            .Select(item => Option(item.Arguments, "type")).Where(item => item.Length > 0).Select(item => "Declared body: " + item)).ToArray();
                    var response = Regex.Match(text[(argumentsEnd + 1)..stop], @"^\s*:\s*(?<type>Promise<[^\r\n{]+>|[\w.\[\]]+)");
                    var operation = metadata.FirstOrDefault(item => item.Name == "ApiOperation");
                    var summary = operation is null ? "" : Option(operation.Arguments, "summary");
                    var routePath = "/" + string.Join('/', new[] { prefix.Trim('/'), suffix.Trim('/') }.Where(item => item.Length > 0));
                    var httpMethod = route.Name.ToUpperInvariant();
                    yield return new[] {
                        CreateApiId(component.Id, httpMethod, routePath), "unversioned", httpMethod, routePath,
                        component.Id, component.Id, "unclassified", "unknown",
                        request.Length == 0 ? "unknown" : string.Join("; ", request),
                        response.Success ? response.Groups["type"].Value.Trim() : "unknown",
                        permission.Length == 0 ? "unknown" : string.Join("; ", permission),
                        operation is null ? "unknown" : Option(operation.Arguments, "operationId") is { Length: > 0 } id ? id : "unknown",
                        "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "Draft",
                        ToRepositoryPath(root, path) + ":" + (1 + text.AsSpan(0, route.Start).Count('\n')), "unknown",
                        $"Observed NestJS declaration: {declaration.Groups["name"].Value}.{method.Groups["name"].Value}. {summary} Controller-relative path; global prefixes/versioning and effective policy require review."
                    };
                }
            }
        }
    }

    private static IEnumerable<PersistentField> TypeScriptFields(string root, IReadOnlyList<string> files)
    {
        foreach (var path in files.Where(IsExpressSource))
        {
            var text = WithoutComments(TryReadText(path));
            if (!text.Contains("typeorm", StringComparison.Ordinal)) continue;
            var decorators = TypeScriptDecorators(text).ToArray();
            var previousEnd = 0;
            foreach (Match declaration in Regex.Matches(text, @"\bclass\s+(?<name>\w+)[^{]*\{"))
            {
                var end = ClosingDelimiter(text, declaration.Index + declaration.Length - 1, '{', '}');
                if (end < 0) continue;
                var persistent = decorators.Any(item => item.Name == "Entity" && item.Start >= previousEnd && item.End <= declaration.Index);
                previousEnd = end + 1;
                if (!persistent) continue;
                foreach (var decorator in decorators.Where(item => item.Start > declaration.Index && item.End < end
                    && item.Name is "Column" or "PrimaryColumn" or "PrimaryGeneratedColumn" or "CreateDateColumn" or "UpdateDateColumn" or "DeleteDateColumn"
                        or "ManyToOne" or "OneToMany" or "OneToOne" or "ManyToMany"))
                {
                    var offset = decorator.End;
                    foreach (var additional in decorators.Where(item => item.Start >= offset && item.End < end).OrderBy(item => item.Start))
                    {
                        if (text[offset..additional.Start].Trim().Length > 0) break;
                        offset = additional.End;
                    }
                    var field = Regex.Match(text[offset..end], @"^\s*(?:(?:public|private|protected|readonly|declare)\s+)*(?<name>\w+)[!?]?\s*:\s*(?<type>[^;=\r\n]+)");
                    if (!field.Success) continue;
                    yield return new(declaration.Groups["name"].Value, field.Groups["name"].Value,
                        field.Groups["type"].Value.Trim(), decorator, ToRepositoryPath(root, path) + ":" + (1 + text.AsSpan(0, decorator.Start).Count('\n')));
                }
            }
        }
    }

    private static IEnumerable<IReadOnlyList<string>> SeedTypeScriptData(string root, IReadOnlyList<string> files, RepositoryClassification classification)
    {
        foreach (var field in TypeScriptFields(root, files).Where(item => item.Declaration.Name.EndsWith("Column", StringComparison.Ordinal)))
        {
            var options = new[] { "name", "type", "length", "precision", "scale", "unique", "enum" }
                .Select(key => (Key: key, Value: Option(field.Declaration.Arguments, key))).Where(item => item.Value.Length > 0)
                .Select(item => item.Key + "=" + item.Value);
            yield return new[] { field.Entity, field.Field, field.Type.Replace('|', '/'),
                Option(field.Declaration.Arguments, "nullable") == "true" ? "no" : "yes (database)",
                field.Declaration.Name + "; " + string.Join("; ", options), "unknown",
                FindComponentByRelativePath(field.Path.Split(':')[0], classification).Id,
                "Observed schema; business lifecycle requires review", "Draft", field.Path };
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> SeedTypeScriptRelationships(string root, IReadOnlyList<string> files, RepositoryClassification classification)
        => TypeScriptFields(root, files).Where(item => !item.Declaration.Name.EndsWith("Column", StringComparison.Ordinal))
            .Select(field => (IReadOnlyList<string>)new[] { field.Entity, field.Field, field.Type.Replace("[]", "").Replace('|', '/'),
                field.Declaration.Name, FindComponentByRelativePath(field.Path.Split(':')[0], classification).Id, "Draft", field.Path }).ToArray();
}
