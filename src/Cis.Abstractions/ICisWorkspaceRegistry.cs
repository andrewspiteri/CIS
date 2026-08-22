namespace Cis.Abstractions;

public interface ICisWorkspaceRegistry
{
    CisWorkspaceResolution Resolve(string workspacePath);
}
