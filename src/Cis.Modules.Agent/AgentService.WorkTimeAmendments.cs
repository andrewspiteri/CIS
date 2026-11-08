using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    public CisAgentWorkResult AmendWorkTaskTime(string repositoryPath, string changeId, string taskId,
        string inputPath, string actor, string reason)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        try
        {
            var identity = WorkIdentity(context, changeId, taskId, diagnostics, accountingOnly: true);
            if (identity is null || diagnostics.Count > 0) return WorkFailure(diagnostics);
            var request = ReadWorkInput<CisAgentWorkTaskTimeAmendmentRequest>(context, inputPath);
            return CurrentWorkView(context, _workLedger.AmendTaskTime(context, identity, request, actor, reason),
                identity.ChangeId, identity.TaskId);
        }
        catch (Exception error) when (WorkException(error)) { return WorkFailure([error.Message]); }
    }
}
