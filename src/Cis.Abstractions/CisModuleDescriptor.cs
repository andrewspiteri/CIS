namespace Cis.Abstractions;

public sealed record CisModuleDescriptor(
    string Name,
    string Description,
    string AssemblyName,
    string AssemblyVersion);
