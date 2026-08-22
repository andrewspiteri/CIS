using Cis.Abstractions;

namespace Cis.Host;

internal sealed class CisModuleCatalog(IEnumerable<ICisModule> modules) : ICisModuleCatalog
{
    public IReadOnlyList<CisModuleDescriptor> Modules { get; } = modules
        .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
        .Select(module =>
        {
            var assemblyName = module.GetType().Assembly.GetName();
            return new CisModuleDescriptor(
                module.Name,
                module.Description,
                assemblyName.Name ?? "unknown",
                assemblyName.Version?.ToString() ?? "unknown");
        })
        .ToArray();
}
