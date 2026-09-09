using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

internal static partial class RepositoryReferenceSeeder
{
    private static void EnrichTypeScriptBehavior(string root, IReadOnlyList<string> files,
        RepositoryClassification classification, Dictionary<string, IReadOnlyList<IReadOnlyList<string>>> seeds)
    {
        var commands = seeds["command-dictionary"].ToList();
        var events = seeds["event-dictionary"].ToList();
        var problems = seeds["problem-details-catalogue"].ToList();
        var rules = seeds["business-invariant-catalogue"].ToList();
        var projections = seeds["projection-dictionary"].ToList();
        var configuration = seeds["configuration-dictionary"].ToList();
        foreach (var api in seeds["api-dictionary"].Where(row => row[2] is "POST" or "PUT" or "PATCH" or "DELETE"
                     && row[22].Contains("NestJS", StringComparison.Ordinal)))
            commands.Add([CreateCatalogueId("CMD", api[0]), api[2] + " " + api[3] + " (observed request handler)", api[4],
                "HTTP request; actor and durable-command semantics require review", api[0], "unknown", api[9], "unknown", api[10], "Draft", api[20]]);

        foreach (var path in files.Where(IsExpressSource))
        {
            var source = WithoutComments(TryReadText(path));
            var code = MaskTypeScriptStrings(source);
            var relative = ToRepositoryPath(root, path);
            var component = FindComponent(root, path, classification).Id;
            string Evidence(int offset) => relative + ":" + (1 + source.AsSpan(0, offset).Count('\n'));
            foreach (Match call in Regex.Matches(code, @"\.\s*emit(?:Async)?\s*\("))
            {
                var start = call.Index + call.Length - 1;
                var end = ClosingDelimiter(source, start, '(', ')');
                if (end < 0) continue;
                var arguments = TypeScriptArguments(source[(start + 1)..end]);
                var name = arguments.Count > 0 ? Literal(arguments[0]) : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var fields = arguments.Count > 1 ? Regex.Matches(MaskTypeScriptStrings(arguments[1]), @"\b([A-Za-z_]\w*)\s*:")
                    .Select(match => match.Groups[1].Value).Distinct().Take(30).ToArray() : [];
                events.Add([CreateCatalogueId("EVT", component + "-" + name), name, "Observed runtime emission; delivery semantics require review",
                    component, "unknown", "unknown", fields.Length > 0 ? string.Join(", ", fields) : "unknown",
                    "unknown", "unknown", "Draft", Evidence(call.Index)]);
            }
            foreach (var decorator in TypeScriptDecorators(source).Where(item => item.Name is "OnEvent" or "Process"))
            {
                var name = Literal(TypeScriptArguments(decorator.Arguments).FirstOrDefault() ?? "");
                if (decorator.Name == "OnEvent" && !string.IsNullOrWhiteSpace(name))
                    events.Add([CreateCatalogueId("EVT", component + "-" + name), name, "Observed event subscription",
                        "unknown", component + " handler at " + Evidence(decorator.Start), "unknown", "unknown", "unknown", "unknown", "Draft", Evidence(decorator.Start)]);
                if (decorator.Name == "Process")
                    commands.Add([ObservedId("CMD", relative, "job:" + (name ?? "default")), name ?? "Default queue job",
                        component, "Queue processor", Evidence(decorator.Start), "unknown", "unknown", "unknown", "unknown", "Draft", Evidence(decorator.Start)]);
            }
            foreach (Match declaration in Regex.Matches(code, @"\bclass\s+(?<name>\w*(?:Projection(?:Sync)?Service|Projection|ReadModel|Projector|Command))\b"))
            {
                var name = declaration.Groups["name"].Value;
                if (name.EndsWith("Command", StringComparison.Ordinal))
                    commands.Add([ObservedId("CMD", relative, name), name, component, "Declared command type; execution semantics require review",
                        Evidence(declaration.Index), "unknown", "unknown", "unknown", "unknown", "Draft", Evidence(declaration.Index)]);
                else
                    projections.Add([ObservedId("PROJ", relative, name), name + " (declared projection service/type)",
                        "unknown", component, "unknown", "Source declaration only; CQRS, financial calculation and persisted read-model roles require review",
                        "unknown", "unknown", "Draft", Evidence(declaration.Index)]);
            }
            foreach (Match thrown in Regex.Matches(code, @"\bthrow\s+new\s+(?<type>\w*(?:Exception|Error))\s*\("))
            {
                var start = thrown.Index + thrown.Length - 1;
                var end = ClosingDelimiter(source, start, '(', ')');
                if (end < 0) continue;
                var arguments = TypeScriptArguments(source[(start + 1)..end]);
                var type = thrown.Groups["type"].Value;
                var message = SafeObservedLiteral(arguments.FirstOrDefault() ?? "") ?? "Dynamic error message; inspect implementation";
                var status = source.Contains("@nestjs/common", StringComparison.Ordinal) ? type switch
                {
                    "BadRequestException" => "400", "UnauthorizedException" => "401", "ForbiddenException" => "403",
                    "NotFoundException" => "404", "ConflictException" => "409", "UnprocessableEntityException" => "422",
                    "InternalServerErrorException" => "500", "ServiceUnavailableException" => "503", _ => "unknown",
                } : "unknown";
                problems.Add([ObservedId("PROB", relative, type + ":" + message), type, status, component, message,
                    "unknown", "unknown", "unknown", "Draft", Evidence(thrown.Index)]);
            }
            foreach (Match guard in Regex.Matches(code, @"\bif\s*\("))
            {
                var start = guard.Index + guard.Length - 1;
                var end = ClosingDelimiter(source, start, '(', ')');
                if (end < 0) continue;
                var rejection = Regex.Match(code[(end + 1)..], @"^\s*\{?\s*throw\s+new\s+(?<type>\w*(?:Exception|Error))\s*\(");
                if (!rejection.Success) continue;
                var condition = Regex.Replace(code[(start + 1)..end], @"\s+", " ").Trim();
                if (condition.Length == 0 || condition.Length > 400) continue;
                var displayedCondition = Regex.Replace(source[(start + 1)..end],
                    "(['\"`])(?:\\\\.|(?!\\1)[^\\\\])*?\\1", "[string literal]");
                displayedCondition = Regex.Replace(displayedCondition, @"\s+", " ").Trim();
                var error = rejection.Groups["type"].Value;
                rules.Add([ObservedId("INV", relative, condition + ":" + error),
                    "Observed rejection when " + displayedCondition + " (" + error + "); string literals omitted",
                    component, "Code guard; business relevance and rationale require review", Evidence(guard.Index),
                    "unknown", "unknown", "Implementation observation; not an approved business rule", "Draft"]);
            }
            foreach (Match access in Regex.Matches(code, @"\bprocess\s*\.\s*env\s*\.\s*(?<name>[A-Za-z_]\w*)\b"))
                configuration.Add([access.Groups["name"].Value, access.Groups["name"].Value, "unknown", "unknown", component,
                    IsSensitive(access.Groups["name"].Value) ? "yes" : "unknown", "Observed environment-key access; values and refresh semantics not inferred",
                    "Draft", Evidence(access.Index)]);
        }
        seeds["command-dictionary"] = MergeObservedRows(commands, 10);
        seeds["event-dictionary"] = MergeObservedRows(events, 10, 2, 3, 4, 6);
        seeds["problem-details-catalogue"] = MergeObservedRows(problems, 9);
        seeds["business-invariant-catalogue"] = MergeObservedRows(rules, 4);
        seeds["projection-dictionary"] = MergeObservedRows(projections, 9);
        seeds["configuration-dictionary"] = ConsolidateConfigurationRows(configuration);
    }

    private static IReadOnlyList<IReadOnlyList<string>> MergeObservedRows(IEnumerable<IReadOnlyList<string>> rows,
        int evidence, params int[] otherFields)
        => rows.GroupBy(row => row[0], StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var row = group.First().ToArray();
            foreach (var index in otherFields.Append(evidence))
            {
                var values = group.Select(item => item[index]).Where(value => value != "unknown").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                if (values.Length > 0) row[index] = string.Join("; ", values);
            }
            return (IReadOnlyList<string>)row;
        }).OrderBy(row => row[0], StringComparer.Ordinal).ToArray();

    private static string ObservedId(string prefix, string path, string value)
        => CreateCatalogueId(prefix, Path.GetFileNameWithoutExtension(path)) + "-" +
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + value)))[..12];

    private static string? SafeObservedLiteral(string value)
    {
        var literal = Literal(value.Trim());
        if (literal is null || literal.Length > 240 || literal.Any(char.IsControl)) return null;
        return Regex.IsMatch(literal, @"(?i)(?:password|secret|token|api.?key)\s*[:=]|https?://|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}")
            ? "Sensitive-shaped literal omitted; inspect implementation" : literal;
    }

    private static string MaskTypeScriptStrings(string source)
    {
        var characters = source.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (characters[index] is not ('\'' or '"' or '`')) continue;
            var quote = characters[index]; characters[index] = ' ';
            for (index++; index < characters.Length; index++)
            {
                var character = characters[index];
                if (character is not ('\r' or '\n')) characters[index] = ' ';
                if (character == '\\' && index + 1 < characters.Length) { characters[++index] = ' '; continue; }
                if (character == quote) break;
            }
        }
        return new string(characters);
    }

    private static IReadOnlyList<string> TypeScriptArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return [];
        var code = MaskTypeScriptStrings(arguments); var depth = 0; var start = 0; var values = new List<string>();
        for (var index = 0; index < code.Length; index++)
        {
            if (code[index] is '(' or '[' or '{') depth++;
            if (code[index] is ')' or ']' or '}') depth--;
            if (code[index] != ',' || depth != 0) continue;
            values.Add(arguments[start..index].Trim()); start = index + 1;
        }
        values.Add(arguments[start..].Trim());
        return values;
    }
}
