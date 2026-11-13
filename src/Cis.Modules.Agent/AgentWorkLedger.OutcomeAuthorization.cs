using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    private static CisOutcomeAuthorization? ReadAllocationAuthority(CisRepositoryContext context, string changeId,
        CisAgentWorkAllocationRequest request, string actor)
    {
        var authorization = CisOutcomeAuthority.Read(context, changeId);
        if (authorization is null)
        {
            if (request.AuthorizationId is not null || request.AuthorizationDigest is not null)
                throw new InvalidDataException("The allocation references a missing canonical outcome authorization.");
            return null;
        }
        if (!OutcomeController(authorization, actor)
            || !authorization.Request.DistributeResources && actor.Trim() != authorization.Reviewer)
            throw new InvalidDataException("This actor has no delegated resource-distribution authority for the outcome.");
        if (request.AuthorizationId is not null && request.AuthorizationId != authorization.Id
            || request.AuthorizationDigest is not null && request.AuthorizationDigest != authorization.Digest)
            throw new InvalidDataException("The outcome authorization changed; read its current decision before distributing resources.");
        var grant = authorization.Request;
        if (request.ChangeSeconds < 0 || request.ChangeAttempts < 0 || request.RestorationSeconds < 0
            || request.ChangeSeconds > grant.Seconds || request.ChangeAttempts > grant.Attempts
            || request.RestorationSeconds > grant.RestorationSeconds)
            throw new InvalidDataException("The requested overall time, attempts or restoration exceeds the authorized outcome. Present that specific increase for a new decision; existing costs remain unchanged.");
        if (request.DecisionReference != grant.DecisionReference)
            throw new InvalidDataException("Use the canonical outcome's actual decision reference; distribution is not another human approval.");
        if (request.Controllers is null || request.Controllers.Any(controller => !OutcomeController(authorization, controller)))
            throw new InvalidDataException("A distribution cannot delegate a controller outside the authorized outcome.");
        ValidateOutcomeStorage(context, grant.Storage, request.Storage);
        return authorization;
    }

    private static bool OutcomeController(CisOutcomeAuthorization authorization, string actor)
        => actor == authorization.Reviewer || CisOutcomeAuthority.IsController(authorization, actor);

    private static void ValidateOutcomeStorage(CisRepositoryContext context, CisOwnedStoragePolicy granted, CisOwnedStoragePolicy? requested)
    {
        if (requested is null || requested.Roots is null || requested.CleanupControllers is null || requested.MaximumBytes != granted.MaximumBytes
            || requested.RestorationBytes != granted.RestorationBytes
            || requested.AllowNativeScratch != granted.AllowNativeScratch
            || !requested.CleanupControllers.ToHashSet(StringComparer.Ordinal).SetEquals(granted.CleanupControllers))
            throw new InvalidDataException("Use the authorized finite storage ceiling, restoration reserve and cleanup delegation; a higher or different policy requires its specific new decision.");
        string Key(CisOwnedStorageRoot root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.RepositoryPath, context.RepositoryPath))
            + "::" + root.Path.Replace('\\', '/').TrimEnd('/') + "::" + root.Scratch;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var required = granted.Roots.Select(Key).ToHashSet(comparer);
        var allowed = required.ToHashSet(comparer);
        foreach (var repository in granted.Roots.Select(root => Path.GetFullPath(root.RepositoryPath, context.RepositoryPath))
            .Append(context.RepositoryPath).Distinct(comparer))
        {
            // Existing controller normalization conservatively counts its own management data.
            allowed.Add(Key(new(repository, ".cis/local/artifacts")));
            allowed.Add(Key(new(repository, ".cis/local/agents") { Scratch = true }));
        }
        var actual = requested.Roots.Select(Key).ToHashSet(comparer);
        if (!required.IsSubsetOf(actual) || !actual.IsSubsetOf(allowed))
            throw new InvalidDataException("Owned storage roots differ from the authorized outcome; keep its declared roots and existing controller management roots.");
    }

    private static void RefreshOutcomeLimits(Change change, CisOutcomeAuthorization authorization, CisAgentWorkAllocationRequest request)
    {
        if (change.AuthorizationId != authorization.Id)
            throw new InvalidDataException("Existing historical allocation authority cannot be replaced by task distribution. Preserve its original ownership and decision.");
        ValidateOriginalOutcome(change, authorization);
        if (request.StorageCeilingReference != change.StorageReference)
            throw new InvalidDataException("Retain the existing shared storage-ceiling reference when distributing outcome work.");
        var milliseconds = Milliseconds(request.ChangeSeconds);
        var restoration = Milliseconds(request.RestorationSeconds);
        if (milliseconds != change.Milliseconds || request.ChangeAttempts != change.Attempts || restoration != change.Restoration)
        {
            if (change.AuthorizationDigest == authorization.Digest)
                throw new InvalidDataException("Task distribution cannot increase or reset the unchanged overall authorization.");
            if (milliseconds < change.Consumed + Reserved(change, null) + restoration
                || request.ChangeAttempts < change.UsedAttempts || restoration < change.Restoration)
                throw new InvalidDataException("The amended outcome must preserve consumed work, reservations, used attempts and protected restoration.");
            change.Milliseconds = milliseconds;
            change.Attempts = request.ChangeAttempts;
            change.Restoration = restoration;
        }
        change.AuthorizationDigest = authorization.Digest;
        change.Controllers = authorization.Request.Controllers.Append(authorization.Reviewer).Distinct(StringComparer.Ordinal).ToList();
    }

    private static void ValidateOutcomeDistribution(Change change)
    {
        // Bindings and descendants share owners; summing the unique owners avoids multiplying capacity on a split.
        if (change.Owners.Values.Sum(owner => owner.Milliseconds) > change.Milliseconds
            || change.Owners.Values.Sum(owner => (long)owner.Attempts) > change.Attempts
            || change.Owners.Values.Sum(owner => owner.Restoration) > change.Restoration)
            throw new InvalidDataException("Aggregate task time, attempts and restoration must fit the one authorized outcome total. Redistribute unspent capacity instead of creating another allowance.");
    }

    private static void RefreshOutcomeStorage(CisRepositoryContext context, Ledger state, Change change,
        CisOutcomeAuthorization authorization, CisAgentWorkAllocationRequest request)
    {
        if (change.Storage is not { } existing || request.Storage is not { } policy
            || System.Text.Json.JsonSerializer.Serialize(existing.Policy, Json) == System.Text.Json.JsonSerializer.Serialize(policy, Json)) return;
        ValidateOriginalOutcome(change, authorization);
        if (change.AuthorizationId != authorization.Id || change.AuthorizationDigest == authorization.Digest)
            throw new InvalidDataException("Storage distribution cannot replace the unchanged outcome grant; present the specific policy amendment first.");
        if (StorageGroup(state, change).Length != 1)
            throw new InvalidDataException("This ceiling is shared with other outcomes. A policy increase needs its existing owning authority; one outcome cannot rewrite another's storage decision.");
        ValidateStoragePolicy(context, policy);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var retained = PolicyPaths(existing.Policy).ToHashSet(comparer);
        if (policy.MaximumBytes < existing.Policy.MaximumBytes || policy.RestorationBytes < existing.Policy.RestorationBytes
            || !retained.IsSubsetOf(PolicyPaths(policy).ToHashSet(comparer)))
            throw new InvalidDataException("The amended storage grant must retain measured roots, the existing ceiling and protected restoration; it cannot hide prior usage.");
        // Only a verified current human amendment reaches here. Claims and pending reservations stay intact.
        existing.Policy = policy;
    }

    private static void ValidateBoundOutcome(CisRepositoryContext context, Change change, string? controller)
    {
        if (change.AuthorizationId is null) return;
        var authorization = CisOutcomeAuthority.Read(context, change.Id)
            ?? throw new InvalidDataException("Restore the retained outcome authorization before launching more work.");
        if (authorization.Id != change.AuthorizationId || controller is null || !OutcomeController(authorization, controller))
            throw new InvalidDataException("Native/direct admission needs a current controller of the same authorized outcome.");
        ValidateOriginalOutcome(change, authorization);
        if (change.Milliseconds > Milliseconds(authorization.Request.Seconds)
            || change.Attempts > authorization.Request.Attempts || change.Restoration > Milliseconds(authorization.Request.RestorationSeconds))
            throw new InvalidDataException("The retained allocation exceeds the current outcome limits; resolve the specific resource difference without resetting history.");
        ValidateOutcomeDistribution(change);
    }

    private static void ValidateOriginalOutcome(Change change, CisOutcomeAuthorization authorization)
    {
        if (change.OriginalAuthorizationDigest != authorization.Digest
            && !authorization.History.Any(revision => revision.Digest == change.OriginalAuthorizationDigest))
            throw new InvalidDataException("Restore the original outcome decision and its amendment history; a replacement record cannot reset allocation authority.");
    }

    private static void ValidateOutcomeState(Change change)
    {
        if (change.AuthorizationId is null)
        {
            if (change.AuthorizationDigest is not null || change.OriginalAuthorizationDigest is not null)
                throw new InvalidDataException("Outcome accounting provenance is incomplete; restore its original record.");
            return;
        }
        if (!BoundedText(change.AuthorizationId) || !BoundedText(change.AuthorizationDigest) || !BoundedText(change.OriginalAuthorizationDigest))
            throw new InvalidDataException("Outcome accounting provenance is incomplete; restore its original decision without resetting capacity.");
        ValidateOutcomeDistribution(change);
    }
}
