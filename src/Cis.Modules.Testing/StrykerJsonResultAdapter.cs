using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed class StrykerJsonResultAdapter : ICisTestResultAdapter
{
    public string Format => "stryker-json";

    public TestSuiteExecution Read(TestResultAdapterContext context)
    {
        // Assess the artifact's bytes without changing the native report.
        var repository = Path.GetFullPath(context.RepositoryPath);
        var path = Path.GetFullPath(context.ResultPath, repository);
        if (!CisPathSafety.IsUnderRoot(repository, path, allowRoot: false)
            || CisPathSafety.ContainsReparsePoint(repository, path))
            throw new InvalidDataException("Mutation report must remain inside the repository without linked paths.");
        using var source = File.OpenRead(path);
        if (source.Length is <= 0 or > 100L * 1024 * 1024)
            throw new InvalidDataException("Mutation report must contain between 1 byte and 100 MiB.");
        var bytes = new byte[(int)source.Length];
        source.ReadExactly(bytes);
        if (source.ReadByte() != -1) throw new InvalidDataException("Mutation report changed size while it was being read.");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var errors = new List<string>();
        var counts = KnownStatuses.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
        var unknown = 0;
        CheckDuplicateProperties(root, "$", errors);
        if (root.ValueKind != JsonValueKind.Object) errors.Add("Mutation report must be a JSON object.");
        if (!String(root, "schemaVersion", out var version)
            || !Regex.IsMatch(version, @"\A2(?:\.(?:0|[1-9]\d*)){0,2}\z", RegexOptions.CultureInvariant))
            errors.Add("Mutation report requires a supported schemaVersion (2, 2.x, or 2.x.y).");

        var high = Threshold(root, "thresholds", "high", required: true, integer: true, errors);
        var low = Threshold(root, "thresholds", "low", required: true, integer: true, errors);
        if (high < low) errors.Add("The high threshold must not be below the low threshold.");
        var reportBreak = Threshold(root, "cisGate", "break", required: false, integer: false, errors);
        var gate = context.MutationBreakThreshold ?? reportBreak;
        if (gate is { } configured && !ValidThreshold(configured))
        {
            errors.Add("The break threshold must be a finite number from 0 through 100.");
            gate = null;
        }

        if (!Property(root, "files", out var files) || files.ValueKind != JsonValueKind.Object)
            errors.Add("Mutation report requires a files object.");
        else
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(file.Name) || !paths.Add(file.Name.Replace('\\', '/')))
                    errors.Add("Mutation report contains an empty or duplicate file path.");
                if (file.Value.ValueKind != JsonValueKind.Object || !String(file.Value, "language", out _)
                    || !String(file.Value, "source", out _) || !Property(file.Value, "mutants", out var mutants)
                    || mutants.ValueKind != JsonValueKind.Array)
                {
                    errors.Add($"File '{file.Name}' requires language, source, and a mutants array.");
                    continue;
                }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var mutant in mutants.EnumerateArray())
                {
                    if (!String(mutant, "id", out var id) || string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                        errors.Add($"File '{file.Name}' contains a missing or duplicate mutant ID.");
                    if (!String(mutant, "mutatorName", out var mutator) || string.IsNullOrWhiteSpace(mutator)
                        || !ValidLocation(mutant)
                        || Property(mutant, "replacement", out var replacement) && replacement.ValueKind != JsonValueKind.String)
                        errors.Add($"Mutant '{file.Name}#{id}' has malformed mutation metadata.");
                    if (!String(mutant, "status", out var status) || !counts.ContainsKey(status))
                    {
                        unknown++;
                        errors.Add($"Mutant '{file.Name}#{id}' has an unknown or missing status.");
                    }
                    else counts[status]++;
                }
            }
        }

        var killed = counts["Killed"];
        var survived = counts["Survived"];
        var timedOut = counts["Timeout"];
        var noCoverage = counts["NoCoverage"];
        var denominator = killed + survived + timedOut + noCoverage;
        if (denominator == 0) errors.Add("Mutation report contains no eligible mutants.");
        if (counts["RuntimeError"] > 0) errors.Add("RuntimeError outcomes remain unresolved; mutation evidence is invalid.");
        if (counts["Pending"] > 0) errors.Add("Pending mutants are incomplete; mutation evidence is invalid.");
        var score = denominator == 0 ? 0 : killed * 100d / denominator;
        var nativeScore = denominator == 0 ? 0 : (killed + timedOut) * 100d / denominator;
        // Compare unrounded values; display precision must never change the gate.
        bool? thresholdMet = gate is null ? null : score >= gate.Value;
        var valid = errors.Count == 0;
        var qualified = valid && timedOut == 0 && noCoverage == 0 && (thresholdMet ?? survived == 0);
        var diagnostics = errors.ToList();
        if (timedOut > 0) diagnostics.Add("Timeout mutants receive no conservative kill credit and block qualification.");
        if (noCoverage > 0) diagnostics.Add("Uncovered mutants remain in the denominator and block qualification.");
        if (valid && !qualified && timedOut == 0 && noCoverage == 0)
            diagnostics.Add(gate is null ? "Mutation findings require an explicit break threshold for gate assessment."
                : "The conservative mutation score is below the break threshold.");
        var mutation = new TestMutationSummary(score, killed, survived, timedOut, noCoverage,
            high, low, gate, TestResultEvidence.Relative(repository, path))
        {
            NativeScore = nativeScore,
            Denominator = denominator,
            CompileErrors = counts["CompileError"],
            Ignored = counts["Ignored"],
            RuntimeErrors = counts["RuntimeError"],
            Pending = counts["Pending"],
            Unknown = unknown,
            ValidEvidence = valid,
            ThresholdMet = thresholdMet,
            Qualified = qualified,
            ReportSha256 = digest,
        };
        // Native Killed is counted once per file/ID. Native first-failure bail and
        // product exceptions do not require a custom assertion-message whitelist.
        return new TestSuiteExecution(context.Suite.Id, context.Suite.Layer, context.Suite.Framework,
            !valid ? "invalid-evidence" : qualified ? "passed" : "findings",
            !valid ? TestFailureKind.InvalidEvidence : qualified ? TestFailureKind.None
                : timedOut > 0 ? TestFailureKind.Timeout : TestFailureKind.Product,
            counts.Values.Sum() + unknown, killed, survived + timedOut + noCoverage + counts["RuntimeError"] + counts["Pending"] + unknown,
            counts["Ignored"] + counts["CompileError"], 0, [], null, mutation,
            [new TestArtifact("mutation-result", mutation.SourcePath, digest, bytes.LongLength)], diagnostics);
    }

    private static double? Threshold(JsonElement root, string container, string name, bool required, bool integer, List<string> errors)
    {
        if (!Property(root, container, out var parent))
        {
            if (required) errors.Add($"Mutation report requires {container}.{name}.");
            return null;
        }
        if (parent.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Mutation report {container} must be an object.");
            return null;
        }
        if (!parent.TryGetProperty(name, out var value))
        {
            if (required) errors.Add($"Mutation report requires {container}.{name}.");
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !ValidThreshold(number)
            || integer && number != Math.Truncate(number))
        {
            errors.Add($"Mutation report {container}.{name} must be a finite {(integer ? "integer" : "number")} from 0 through 100.");
            return null;
        }
        return number;
    }

    private static bool ValidThreshold(double value) => double.IsFinite(value) && value is >= 0 and <= 100;
    private static bool Property(JsonElement value, string name, out JsonElement result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out result);
    }
    private static bool String(JsonElement value, string name, out string result)
    {
        result = string.Empty;
        if (!Property(value, name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        result = property.GetString()!;
        return true;
    }
    private static bool ValidLocation(JsonElement mutant) => Property(mutant, "location", out var location)
        && Position(location, "start", out var line, out var column)
        && Position(location, "end", out var endLine, out var endColumn)
        && (endLine > line || endLine == line && endColumn >= column);
    private static bool Position(JsonElement location, string name, out int line, out int column)
    {
        line = column = 0;
        return Property(location, name, out var position)
            && Property(position, "line", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out line) && line >= 1
            && Property(position, "column", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out column) && column >= 1;
    }
    internal static void CheckDuplicateProperties(JsonElement value, string path, List<string> errors)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) errors.Add($"Duplicate JSON property at '{path}.{property.Name}'.");
                CheckDuplicateProperties(property.Value, path + "." + property.Name, errors);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckDuplicateProperties(item, path + "[]", errors);
    }

    private static readonly string[] KnownStatuses =
        ["Killed", "Survived", "Timeout", "NoCoverage", "Ignored", "CompileError", "RuntimeError", "Pending"];
}
