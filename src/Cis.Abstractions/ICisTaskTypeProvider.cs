namespace Cis.Abstractions;

public sealed record CisTaskTypeDefinition(
    string Key,
    string Version,
    string Category,
    string Title,
    int Order,
    string CreationPolicy,
    IReadOnlyList<string> TriggerTerms,
    IReadOnlyList<string> DependsOnTypeKeys,
    string DefaultComplexity,
    string Purpose,
    string AcceptanceCriteria,
    string Validation,
    string ApprovalGate = "none",
    string CapabilityKey = "",
    IReadOnlyList<string>? ConflictsWithTypeKeys = null,
    IReadOnlyList<string>? ReplacesTypeKeys = null,
    string ProviderKey = "");

public interface ICisTaskTypeProvider
{
    string ProviderKey { get; }

    IReadOnlyList<CisTaskTypeDefinition> GetTaskTypes();
}
