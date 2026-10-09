using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void Run_PreservesPlatformDirectoryLocationsForChildBuildTools()
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        var provider = new FakeProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);

        var run = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, null, null, 60, false, "Andrew Spiteri", TestContext.Current.CancellationToken);

        Assert.Equal(0, run.ExitCode);
        var environment = provider.LastRequest!.Environment;
        // NuGet resolves its machine and user configuration using these OS-provided locations.
        // Inherit actual values: installations need not use the standard C: directory layout.
        string[] directoryNames = ["APPDATA", "LOCALAPPDATA", "ProgramData", "ALLUSERSPROFILE",
            "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432"];
        foreach (var name in directoryNames)
        {
            var inheritedValue = Environment.GetEnvironmentVariable(name);
            if (inheritedValue is null)
                Assert.False(environment.ContainsKey(name));
            else
                Assert.Equal(inheritedValue, environment[name]);
        }
    }
}
