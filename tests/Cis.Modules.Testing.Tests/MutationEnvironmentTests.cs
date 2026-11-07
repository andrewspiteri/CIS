using System.Text.Json;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class MutationRunServiceTests
{
    [Fact]
    public void NativeLaunchesShareCanonicalSessionEnvironmentWithoutChangingDeclaredIdentity()
    {
        const string name = "SSH_CLIENT";
        var previous = new[] { name }.Concat(CisNativeWorkEnvironment.Keys)
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(name, "ignored-ambient-session-one");
            using var fixture = new Target();
            fixture.Write(".cis/environment-probe.json", JsonSerializer.Serialize(new[] { name }));
            var profile = PrepareQualification(fixture);
            var declaredProfile = File.ReadAllBytes(fixture.Full("profile.json"));
            var result = fixture.Run("qualification");
            Assert.True(result.Qualified, JsonSerializer.Serialize(result, Target.Json));
            Assert.Equal(declaredProfile, File.ReadAllBytes(fixture.Full("profile.json")));
            Assert.True(result.SourceIdentity!.Unchanged);
            NativeEnvironmentProbe[] Captures() => File.ReadAllLines(fixture.Full(".cis/environment-launches.jsonl"))
                .Select(line => JsonSerializer.Deserialize<NativeEnvironmentProbe>(line, Target.Json)!).ToArray();
            var observed = Captures();
            Assert.All(observed, launch => Assert.True(string.IsNullOrEmpty(launch.Values[name]),
                "Ignored session input reached native " + launch.Kind + "/" + launch.Output));
            Assert.Contains(observed, launch => launch.Output == "baseline" && launch.Kind == "unit");
            Assert.Contains(observed, launch => launch.Output == "postflight" && launch.Kind == "unit");
            Assert.Contains(observed, launch => launch.Output == "restoration" && launch.Kind == "recovery");
            Assert.Contains(observed, launch => launch.Output == "mutation" && launch.Kind == "mutation");
            Assert.Contains(observed, launch => launch.Output == result.RunId && launch.Kind == "mutation");
            Assert.Contains(observed, launch => launch.Output == result.Canary!.RunId && launch.Kind == "canary");
            var currentReport = File.ReadAllBytes(fixture.Latest);
            Environment.SetEnvironmentVariable(name, "ignored-ambient-session-two");
            var reused = MutationRunService.InspectCorrectnessPilot(fixture.Path, "representative.json");
            Assert.True(reused.Ready, JsonSerializer.Serialize(reused, Target.Json));
            Assert.Equal(result.Pilot!.BasisSha256, reused.BasisSha256);
            foreach (var key in CisNativeWorkEnvironment.Keys) Environment.SetEnvironmentVariable(key, "bookkeeping-only-currentness-probe");
            var authorityHeld = MutationRunService.InspectCorrectnessPilot(fixture.Path, "representative.json");
            Assert.False(authorityHeld.Ready); Assert.True(authorityHeld.NativeReady);
            Assert.Equal(reused.BasisSha256, authorityHeld.BasisSha256);
            Assert.Equal(currentReport, File.ReadAllBytes(fixture.Latest));
            Assert.Equal(observed.Length, Captures().Length);
            foreach (var key in CisNativeWorkEnvironment.Keys) Environment.SetEnvironmentVariable(key, previous[key]);
            Assert.True(MutationRunService.InspectCorrectnessPilot(fixture.Path, "representative.json").Ready);
            var explicitProfile = profile with { Environment = new() { [name] = "declared-session-input" } };
            fixture.Save(explicitProfile);
            var incompatible = fixture.Run("qualification");
            Assert.Equal(4, incompatible.ExitCode); Assert.False(incompatible.Qualified);
            Assert.Equal("pilot-path-mismatch", incompatible.Pilot!.Status);
            Assert.Equal(observed.Length, Captures().Length);
            Assert.Equal(currentReport, File.ReadAllBytes(fixture.Latest));
            fixture.Save(explicitProfile with { CorrectnessPilotProfile = null });
            var explicitResult = fixture.Run();
            Assert.Equal(0, explicitResult.ExitCode); Assert.True(explicitResult.SourceIdentity!.Unchanged);
            Assert.Contains(Captures().Skip(observed.Length), launch => launch.Output == explicitResult.RunId
                && launch.Values[name] == "declared-session-input");
            Assert.NotEqual(result.SourceIdentity.Digest, explicitResult.SourceIdentity.Digest);
            Assert.Equal("ignored-ambient-session-two", Environment.GetEnvironmentVariable(name));
        }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private sealed record NativeEnvironmentProbe(string Kind, string Output, Dictionary<string, string?> Values);
}
