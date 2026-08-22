namespace Cis.Modules.Plan;

public static class PlanTaskDispositionPolicy
{
    public static bool IsTerminal(string status, string category)
    {
        var normalizedStatus = status.Trim().ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        var normalizedCategory = category.Trim().ToLowerInvariant();
        if (normalizedStatus is "complete" or "completed" or "accepted" or "deferred" or "cancelled" or "canceled")
            return true;
        if (normalizedStatus == "approved" && normalizedCategory is "wireframe" or "design")
            return true;
        return normalizedStatus == "decomposed" && normalizedCategory == "coordination";
    }
}
