namespace Cis.Abstractions;

public interface ICisModuleCatalog
{
    IReadOnlyList<CisModuleDescriptor> Modules { get; }
}
