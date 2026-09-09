using Cis.Abstractions;

namespace Cis.Modules.Graph;

/// <summary>Shares graph and dependent reads during a bounded read-only projection.</summary>
public sealed class GraphReadScope : IDisposable
{
    private readonly CisReadScope _scope = CisReadScope.Enter();
    private GraphReadScope() { }
    public static GraphReadScope Enter() => new();
    internal static T Read<T>(object reader, string operation, string repositoryPath, Func<T> read) where T : notnull
        => CisReadScope.Read(reader, operation, repositoryPath, read);
    public void Dispose() => _scope.Dispose();
}
