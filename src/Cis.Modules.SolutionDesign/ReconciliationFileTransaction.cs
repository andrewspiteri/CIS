using Cis.Abstractions;

namespace Cis.Modules.SolutionDesign;

internal sealed class ReconciliationFileTransaction(Action<string, byte[]>? replace = null)
{
    private readonly CisReconciliationFileTransaction _transaction = new(replace);

    public IReadOnlyList<string> Apply(string backupDirectory, IReadOnlyDictionary<string, byte[]> originals,
        IReadOnlyDictionary<string, byte[]> proposed, Func<bool> validate)
        => _transaction.Apply(backupDirectory, originals, proposed, validate,
            "Reconciliation did not validate against unchanged technical direction.");
}
