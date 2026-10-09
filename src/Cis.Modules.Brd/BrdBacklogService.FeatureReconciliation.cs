using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class BrdBacklogService
{
    private const string StaleFeatureProductDefinition = "Product-definition baseline: the feature specification was not authored from the current consolidated product definition.";

    public BrdFeatureResult ReconcileFeature(string workspacePath, string itemId, string reviewPath, string actor, string reason)
    {
        try
        {
            return ReconcileFeatureCore(workspacePath, itemId, reviewPath, actor, reason);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or DecoderFallbackException or InvalidOperationException)
        {
            return new BrdFeatureResult("blocked", workspacePath, null, itemId, null, null, null,
                ["Feature reconciliation could not apply the reviewed evidence: " + exception.Message], false);
        }
    }

    private BrdFeatureResult ReconcileFeatureCore(string workspacePath, string itemId, string reviewPath, string actor, string reason)
    {
        var feature = ResolveFeature(workspacePath, itemId);
        BrdFeatureResult Block(params string[] errors) => new("blocked", workspacePath,
            feature.State?.Authority?.Id, itemId, feature.Item?.RequirementId, feature.RelativePath, null, errors, false);
        if (feature.Errors.Count > 0) return Block(feature.Errors.ToArray());
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason))
            return Block("Reconciliation actor and reason are required; this operation never approves a feature.");
        var state = feature.State!;
        var root = state.Authority!.RepositoryPath;
        var evidence = new FeatureReconciliationEvidence(root);
        foreach (var path in new[] { ".cis/repository.yml", ".cis/workspace.yml", CisProductDocumentPaths.SelectionFile, ".cis/local/definition-wizard/session.json" })
            evidence.WatchOptional(path);
        var reviewBytes = evidence.Read(reviewPath);
        var review = JsonSerializer.Deserialize<CisFeatureReconciliationReview>(reviewBytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("A feature compatibility review object is required.");
        if (review.SchemaVersion != 1) return Block("Unsupported reconciliation review schema.");
        var originalBytes = evidence.Read(feature.RelativePath!);
        if (!FeatureReconciliationEvidence.Matches(originalBytes, review.ExpectedFeatureSha256))
            return Block("The feature changed since review; inspect the current file before reconciling.");
        var original = Encoding.UTF8.GetString(originalBytes).TrimStart('\uFEFF');
        var providers = _productDefinitionAuthorities.Where(provider => provider.Evaluate(root).Applicable).ToArray();
        if (providers.Length != 1) return Block("Exactly one native product-definition authority is required for reconciliation.");
        var product = providers[0].Evaluate(root);
        if (!product.Active || string.IsNullOrWhiteSpace(product.BaselineHash)
            || product.BaselineHash != review.ExpectedProductDefinitionHash)
            return Block("The reviewed product definition is missing, inactive or changed.");
        var backlog = Status(workspacePath);
        if (backlog.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            return Block("An Active, current backlog is required for reconciliation.");
        if (ReadNestedFrontMatter(original, "backlog_item_hash") != ItemDigest(feature.Item!))
            return Block("The backlog item's scope or routing changed; revise the feature through ordinary scope review.");
        var assessed = ValidateFeature(workspacePath, itemId);
        if (assessed.Errors.Count > 0 || assessed.Validation is null
            || assessed.Validation.Errors.Any(error => error != StaleFeatureProductDefinition))
            return Block("Feature structure or authority is invalid; resolve its validation findings before reconciliation.");
        if (ReadNestedFrontMatter(original, "product_definition_hash") == product.BaselineHash)
            return assessed.Validation is { Valid: true, Current: true }
                ? assessed with { Status = "unchanged" }
                : Block("The feature is not current; resolve its validation warnings before reconciliation.");
        if (ReadFrontMatter(original, "status") != "Active"
            || ReadNestedFrontMatter(original, "approved_content_hash") != ContentDigest(original))
            return Block("Reconciliation requires an unchanged previously approved feature; edited or draft content needs ordinary revision.");
        if (string.IsNullOrWhiteSpace(review.PreviousProductDefinitionHash)
            || ReadNestedFrontMatter(original, "product_definition_hash") != review.PreviousProductDefinitionHash)
            return Block("The comparison does not identify this feature's prior approved product definition.");

        var inventory = providers[0].EvidencePaths(root);
        evidence.Validate(review, inventory, new HashSet<string>([feature.Path!, state.Context!.CatalogPath],
            FeatureReconciliationEvidence.PathComparer));
        var catalogRelative = Normalize(Path.GetRelativePath(root, state.Context!.CatalogPath));
        var catalog = Encoding.UTF8.GetString(evidence.Read(catalogRelative));
        var proposed = ReplaceNestedFrontMatter(original, "product_definition_hash", review.ExpectedProductDefinitionHash);
        proposed = ReplaceFrontMatter(proposed, "status", "Review Required");
        proposed = ReplaceFrontMatter(proposed, "last_reviewed", "null");
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            proposed = ReplaceNestedFrontMatter(proposed, key, "null");
        var next = new Dictionary<string, byte[]>(FeatureReconciliationEvidence.PathComparer)
        {
            [feature.Path!] = Encoding.UTF8.GetBytes(proposed),
            [state.Context.CatalogPath] = Encoding.UTF8.GetBytes(UpdateCatalogStatus(catalog,
                $"{state.Authority.Id}:feature:{feature.Item!.Id.ToLowerInvariant()}", "review-required")),
        };
        var backup = Path.Combine(root, ".cis/local/feature-reconciliation", Guid.NewGuid().ToString("N"));
        if (CisPathSafety.ContainsReparsePoint(root, backup)) return Block("The reconciliation backup path is unsafe.");
        Directory.CreateDirectory(backup);
        File.WriteAllBytes(Path.Combine(backup, "review.json"), reviewBytes);
        File.WriteAllText(Path.Combine(backup, "record.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, itemId = feature.Item.Id, actor = actor.Trim(), reason = reason.Trim(),
            recordedAtUtc = _clock().ToUniversalTime(), reviewSha256 = FeatureReconciliationEvidence.Digest(reviewBytes),
            previousProductDefinitionHash = review.PreviousProductDefinitionHash,
            productDefinitionHash = product.BaselineHash, featureSha256 = review.ExpectedFeatureSha256,
            proposedFeatureSha256 = FeatureReconciliationEvidence.Digest(next[feature.Path!]),
            approvalGranted = false,
        }));
        BrdFeatureResult? validated = null;
        var errors = new CisReconciliationFileTransaction().Apply(backup, evidence.Inputs, next, () =>
        {
            var current = providers[0].Evaluate(root);
            if (!current.Active || current.BaselineHash != product.BaselineHash
                || !inventory.SequenceEqual(providers[0].EvidencePaths(root))
                || !evidence.UnchangedExcept(next.Keys.ToHashSet(FeatureReconciliationEvidence.PathComparer))) return false;
            validated = ValidateFeatureInternal(workspacePath, itemId, "reconciled", true);
            return validated.Validation is { Valid: true, DocumentStatus: "Review Required" }
                && next.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value));
        }, inputsCurrent: () => evidence.UnchangedExcept(new HashSet<string>()));
        return errors.Count == 0 ? validated! : Block(errors.ToArray());
    }
}
