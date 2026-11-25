using System.Globalization;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Docs;

namespace Cis.Modules.Graph;

public sealed partial class GraphBuilder
{
    private void ExtractVerificationMappings(CisRepositoryContext context,
        IReadOnlyList<DocumentationCatalogEntry> documents, IReadOnlyDictionary<string, CisGraphNode> documentNodes,
        IReadOnlyDictionary<string, CisGraphNode> components, IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes, IDictionary<string, CisGraphEdge> edges, ICollection<CisGraphDiagnostic> diagnostics)
    {
        var sources = new List<(DocumentationCatalogEntry Document, VerificationDeclarations Declarations)>();
        foreach (var document in documents.Where(document => document.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadText(Path.Combine(context.RepositoryPath, document.Path.Replace('/', Path.DirectorySeparatorChar)), document.Path, diagnostics, out var content)) continue;
            var declarations = VerificationMappingExtractor.Read(content);
            foreach (var issue in declarations.Issues)
                diagnostics.Add(Diagnostic("CIS-GRAPH-VERIFY-001", "warning", issue, document.Path));
            sources.Add((document, declarations));
            foreach (var criterion in declarations.Criteria)
            {
                var localId = document.Id + "#" + criterion.Id;
                var evidence = MappingEvidence(document.Path, criterion.Line, hashes);
                var node = new CisGraphNode(CreateNodeKey(context.RepositoryId, "requirement", localId), context.RepositoryId,
                    "requirement", "acceptance-criterion", localId, criterion.Text, ["requirement"], document.Authority.ToLowerInvariant(),
                    document.Status.ToLowerInvariant(), [new(document.Path, "line", criterion.Line.ToString(CultureInfo.InvariantCulture))],
                    SortedProperties(("criterionId", criterion.Id), ("sourceDocument", document.Id), ("acceptance", "source-declared-not-assessed")), [evidence]);
                AddOrMergeNode(node, nodes, diagnostics);
                if (documentNodes.TryGetValue(document.Id, out var parent))
                    AddEdge(context.RepositoryId, "declares", parent.Key, node.Key, "declared", "high", [evidence], edges);
            }
        }
        foreach (var (document, declarations) in sources)
            foreach (var mapping in declarations.Mappings)
            {
                var criterion = ResolveUnique(nodes.Values.Where(node => node.Kind is "requirement" or "reference-item"), mapping.Criterion);
                var task = ResolveUnique(nodes.Values.Where(node => node.Kind == "document" && node.Subtype == "agent-task"), mapping.Task, paths: true);
                components.TryGetValue(mapping.Component, out var component);
                var artifact = nodes.Values.SingleOrDefault(node => node.Kind == "source-file" && node.LocalId == mapping.Artifact);
                var check = ResolveCheck(nodes.Values, mapping.Check);
                if (criterion is null || task is null || component is null || artifact is null || check is null
                    || artifact.Properties.GetValueOrDefault("component") != component.LocalId)
                {
                    diagnostics.Add(Diagnostic("CIS-GRAPH-VERIFY-001", "warning",
                        "Explicit verification mapping is unresolved, ambiguous or conflicts with confirmed artifact ownership.",
                        document.Path + ":" + mapping.Line.ToString(CultureInfo.InvariantCulture), mapping.Criterion, mapping.Task, mapping.Artifact, mapping.Check));
                    continue;
                }
                var evidence = MappingEvidence(document.Path, mapping.Line, hashes);
                var properties = SortedProperties(("mapping", "explicit"), ("result", "not-recorded"));
                var checkProperties = SortedProperties(("mapping", "explicit"), ("role", "declared-applicable-check"),
                    ("coverageAssessment", "not-assessed"), ("meaningAssessment", "not-assessed"), ("result", "not-recorded"));
                AddEdge(context.RepositoryId, "references", criterion.Key, task.Key, "declared", "high", [evidence], edges,
                    SortedProperties(("role", "implementing-task")));
                AddEdge(context.RepositoryId, "describes", task.Key, component.Key, "declared", "high", [evidence], edges, properties);
                AddEdge(context.RepositoryId, "references", task.Key, artifact.Key, "declared", "high", [evidence], edges, properties);
                AddEdge(context.RepositoryId, "implemented-by", criterion.Key, component.Key, "declared", "high", [evidence], edges, properties);
                AddEdge(context.RepositoryId, "implemented-by", criterion.Key, artifact.Key, "declared", "high", [evidence], edges, properties);
                AddEdge(context.RepositoryId, "verified-by", artifact.Key, check.Key, "declared", "high", [evidence], edges, checkProperties);
                AddEdge(context.RepositoryId, "verified-by", criterion.Key, check.Key, "declared", "high", [evidence], edges, checkProperties);
                if (GraphProjectionFingerprint.IsReference(mapping.ResultReference))
                    LinkRecordedResult(context, mapping, check, nodes, edges, diagnostics, evidence);
            }
    }

    private static CisGraphEvidence MappingEvidence(string path, int line, IReadOnlyDictionary<string, string> hashes)
        => Evidence("canonical-markdown", path, "line", line.ToString(CultureInfo.InvariantCulture), hashes.GetValueOrDefault(path),
            "explicit-verification-declaration", "cis.markdown.verification-map/1", "high");

    private static CisGraphNode? ResolveUnique(IEnumerable<CisGraphNode> candidates, string identity, bool paths = false)
    {
        var matches = candidates.Where(node => node.Key == identity || node.LocalId == identity
            || paths && node.Locations.Any(location => location.Path == identity)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static CisGraphNode? ResolveCheck(IEnumerable<CisGraphNode> nodes, string identity)
    {
        var inventory = nodes.ToArray();
        var matches = inventory.Where(node => node.Kind == "test" && (node.Key == identity || node.LocalId == identity
            || node.Locations.Any(location => location.Path + "#" + node.Label == identity)
            || QualifiedCheckName(node, inventory) == identity)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? QualifiedCheckName(CisGraphNode check, IReadOnlyList<CisGraphNode> nodes)
    {
        var location = check.Locations.FirstOrDefault();
        if (location is null) return null;
        _ = int.TryParse(location.LocatorValue, CultureInfo.InvariantCulture, out var testLine);
        var candidates = nodes.Where(node => node.Kind == "symbol" && node.Subtype == "method"
            && node.Label.Contains("." + check.Label + "(", StringComparison.Ordinal)
            && node.Locations.Any(item => item.Path == location.Path))
            .Select(node => (Node: node, Line: int.TryParse(node.Locations.First(item => item.Path == location.Path).LocatorValue,
                CultureInfo.InvariantCulture, out var line) ? line : 0))
            .Where(item => item.Line >= testLine).OrderBy(item => item.Line).ToArray();
        if (candidates.Length == 0) return null;
        var label = candidates[0].Node.Label;
        return label[..label.IndexOf('(')];
    }

    private void LinkRecordedResult(CisRepositoryContext context, VerificationMapping mapping, CisGraphNode check,
        IDictionary<string, CisGraphNode> nodes, IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics, CisGraphEvidence mappingEvidence)
    {
        var saved = GraphEvidenceReceipt.Read(context.RepositoryPath, mapping.ResultReference);
        var receipt = saved.Receipt;
        var localId = "verification-result/" + Uri.EscapeDataString(mapping.ResultReference);
        var node = new CisGraphNode(CreateNodeKey(context.RepositoryId, "reference-item", localId), context.RepositoryId,
            "reference-item", "verification-result", localId, "Recorded native verification", ["verification", "recorded-result"],
            "derived", "active", [new(mappingEvidence.Path, mappingEvidence.LocatorKind, mappingEvidence.LocatorValue)],
            SortedProperties(("graphIdentity", mapping.ResultReference), ("receiptState", saved.State),
                ("recordedStatus", receipt?.Status ?? "unknown"), ("inputState", RecordedInputState(saved)),
                ("externalInputs", saved.Pins.Count(pin => pin.Role == "input" && pin.State == "unassessed-external").ToString(CultureInfo.InvariantCulture)),
                ("applicability", "requires-current-native-verification")), [mappingEvidence]);
        AddOrMergeNode(node, nodes, diagnostics);
        var outcome = RecordedCaseOutcome(context.RepositoryPath, mapping, check, nodes.Values.ToArray(), saved);
        AddEdge(context.RepositoryId, "references", check.Key, node.Key, "declared", "high", [mappingEvidence], edges,
            SortedProperties(("role", "explicit-result-reference"), ("nativeOutcome", outcome), ("applicability", "requires-current-native-verification")));
        if (receipt is null)
        {
            diagnostics.Add(Diagnostic("CIS-GRAPH-VERIFY-002", "warning", "Explicit native result reference is unavailable or unsupported: " + saved.State, mappingEvidence.Path));
            return;
        }
        foreach (var pin in saved.Pins)
        {
            var pinId = "verification-input/" + Uri.EscapeDataString(mapping.ResultReference) + "/" + Uri.EscapeDataString(pin.Role + ":" + pin.Path);
            var evidence = Evidence("saved-native-evidence", mapping.ResultReference, "json", "inputs-and-report-pins", saved.SemanticHash,
                "recorded-receipt-pin", "cis.markdown.verification-map/1", "high");
            var pinNode = new CisGraphNode(CreateNodeKey(context.RepositoryId, "reference-item", pinId), context.RepositoryId,
                "reference-item", "verification-input", pinId, pin.Role, ["verification", "recorded-input"], "derived", "active",
                [new(mapping.ResultReference, "json", pin.Role)], SortedProperties(("graphIdentity", pin.Role + ":" + pin.Path),
                    ("path", pin.Path), ("recordedSha256", pin.RecordedSha256), ("observedSha256", pin.ActualSha256 ?? "unassessed"),
                    ("state", pin.State), ("role", pin.Role)), [evidence]);
            AddOrMergeNode(pinNode, nodes, diagnostics);
            AddEdge(context.RepositoryId, "generated-from", node.Key, pinNode.Key, "discovered", "high", [evidence], edges);
        }
    }

    private static string RecordedInputState(GraphEvidenceReceipt saved)
    {
        if (saved.Receipt is null) return "unknown";
        var inputs = saved.Pins.Where(pin => pin.Role == "input").ToArray();
        if (inputs.Any(pin => pin.State is "changed" or "missing" or "unreadable")) return "inputs-changed";
        return inputs.Length > 0 && inputs.All(pin => pin.State is "matches" or "unassessed-external")
            ? "repository-inputs-match-receipt" : "unknown";
    }

    private string RecordedCaseOutcome(string repository, VerificationMapping mapping, CisGraphNode check,
        IReadOnlyList<CisGraphNode> nodes, GraphEvidenceReceipt saved)
    {
        var receipt = saved.Receipt;
        if (receipt is not { Status: "verified", ExitCode: 0, ScopeComplete: true } || receipt.ProfilePath is null
            || !GraphEvidenceReceipt.TrySafePath(repository, receipt.ProfilePath, out var profilePath)
            || GraphEvidenceReceipt.InspectPin(repository, receipt.ProfilePath, receipt.ProfileSha256 ?? "", null, "profile").State != "matches") return "unknown";
        try
        {
            if (new FileInfo(profilePath).Length > 1024 * 1024) return "unknown";
            using var profile = JsonDocument.Parse(File.ReadAllBytes(profilePath));
            var format = profile.RootElement.TryGetProperty("unitResultFormat", out var declared) ? declared.GetString() : "trx";
            var adapters = _testResultAdapters.Where(adapter => adapter.Format == format).ToArray();
            if (adapters.Length != 1) return "unknown";
            var path = check.Locations[0].Path;
            var batches = receipt.Batches.Where(batch => batch.SourceFile == mapping.Artifact
                && receipt.Closures.Any(closure => closure.Id == batch.ClosureId
                    && closure.TestInputIndices.Any(index => index >= 0 && index < receipt.Inputs.Count && receipt.Inputs[index].Path == path))).ToArray();
            if (batches.Length != 1) return "unknown";
            var batch = batches[0];
            if (batch.Executions.Count(execution => execution is { Role: "unit", Status: "exited", ExitCode: 0, HasError: false, OutputTruncated: false }) != 1
                || saved.Pins.SingleOrDefault(pin => pin.Role == "unit-report" && pin.Path == batch.UnitReportPath)?.State != "matches"
                || !GraphEvidenceReceipt.TrySafePath(repository, batch.UnitReportPath, out var reportPath)) return "unknown";
            var suite = new TestSuiteProfile("graph-recorded-result", mapping.Component, "unit", "native", "", ".", format!, batch.UnitReportPath,
                "", "", "", "", "", "");
            var result = adapters[0].Read(new(repository, suite, reportPath, null, null));
            if (GraphEvidenceReceipt.InspectPin(repository, batch.UnitReportPath, batch.UnitReportSha256, batch.UnitReportBytes, "unit-report").State != "matches") return "unknown";
            var qualified = QualifiedCheckName(check, nodes);
            var shortNameIsUnique = nodes.Count(node => node.Kind == "test" && node.Label == check.Label) == 1;
            var cases = result.Cases.Where(item => item.Name == qualified || shortNameIsUnique && item.Name == check.Label).ToArray();
            if (cases.Length == 0 || result.Total == 0 || result.Passed != batch.UnitPassed) return "unknown";
            if (cases.Any(item => item.Status == "failed")) return "recorded-failed";
            if (cases.Any(item => item.Status == "skipped")) return "recorded-skipped";
            return cases.All(item => item.Status == "passed") ? "recorded-passed" : "unknown";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or ArgumentException or System.Xml.XmlException or FormatException or OverflowException or KeyNotFoundException or NotSupportedException)
        {
            return "unknown";
        }
    }
}
