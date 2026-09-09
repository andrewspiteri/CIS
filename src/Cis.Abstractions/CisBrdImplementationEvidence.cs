namespace Cis.Abstractions;

/// <summary>Digest-bound implementation files disclosed for one BRD authoring/review run.</summary>
public sealed record CisBrdImplementationEvidence(
    string SourceId,
    string SnapshotDigest,
    string RootPath,
    string IndexDigest,
    string ProjectionDigest);
