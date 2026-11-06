using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private static NativeCorrectnessPilotResult InspectIncrementalCorrectnessPilot(Input input,
        CisNativeWorkAdmission? admission, ICisNativeWorkController? controller)
    {
        if (input.Profile.CorrectnessPilotProfile is null && admission?.Required != true)
            return new("legacy-unaccounted", false, null,
                "This non-adopted diagnostic has no declared representative pilot; it does not supply governed runner-readiness or qualification coverage.");
        return MutationRunService.InspectIncrementalPilotPath(input.Repository, input.Profile, admission, controller);
    }

    private static bool PilotBlocksExpansion(NativeCorrectnessPilotResult pilot, CisNativeWorkAdmission? admission)
        => admission?.Required == true && (!pilot.Ready || !pilot.ControllerAssessed);
}
