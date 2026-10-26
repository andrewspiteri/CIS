using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

#if COMPAT_METHOD_PARALLEL
[assembly: Parallelize(Workers = 2, Scope = ExecutionScope.MethodLevel)]
#endif

namespace CompatFixture.Tests;

[TestClass]
public sealed class PolicyTests
{
    public TestContext TestContext { get; set; } = null!;
    private static int classCleanupCalls;

    [ClassInitialize]
    public static void BeforeClass(TestContext context)
    {
#if COMPAT_OMIT_ASSEMBLY_INIT
        Lifecycle.Begin("request-without-assembly-initialize-hook");
#endif
        classCleanupCalls = 0;
        Lifecycle.Write("class-initialize", nameof(PolicyTests));
    }

    [TestInitialize]
    public void BeforeTest() => Lifecycle.Write("test-start", TestContext.TestName);

    [TestCleanup]
    public void AfterTest() => Lifecycle.Write("test-cleanup", TestContext.TestName, TestContext.CurrentTestOutcome.ToString());

    [ClassCleanup]
    public static void AfterClass()
    {
        Lifecycle.Write("class-cleanup", nameof(PolicyTests));
        Assert.AreEqual(1, Interlocked.Increment(ref classCleanupCalls), "Class cleanup was invoked twice in one request.");
    }

    [TestMethod]
    public void A_RequiresPositiveValue() => Assert.AreEqual(1, PositivePolicy.RequirePositive(1));

    [TestMethod]
#if COMPAT_DEPENDENCIES
    [DependsOn(nameof(A_RequiresPositiveValue), ProceedOnFailure = true)]
#endif
    public void B_RejectsZeroAfterOrdinaryWork()
    {
        Thread.Sleep(3000);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PositivePolicy.RequirePositive(0));
    }

    [TestMethod]
    public void C_PreservesAnotherPositiveValue()
    {
        Thread.Sleep(3000);
        Assert.AreEqual(3, PositivePolicy.RequirePositive(3));
    }

    [TestMethod]
    public void D_PreservesLargerPositiveValue()
    {
        Thread.Sleep(3000);
        Assert.AreEqual(7, PositivePolicy.RequirePositive(7));
    }
}

[TestClass]
public sealed class OutsideCohortTests
{
    [TestMethod]
    public void ExcludedFailure() => Assert.Fail("The exact native cohort filter must exclude this tripwire.");
}

[TestClass]
public sealed class AssemblyHooks
{
#if !COMPAT_OMIT_ASSEMBLY_INIT
    [AssemblyInitialize]
    public static void BeforeAssembly(TestContext context) => Lifecycle.Begin("assembly-initialize");
#endif

    [AssemblyCleanup]
    public static void AfterAssembly()
    {
        Lifecycle.Write("assembly-cleanup", nameof(AssemblyHooks));
        Assert.AreEqual(1, Lifecycle.IncrementCleanup(), "Assembly cleanup was invoked twice in one request.");
        Console.WriteLine("COMPAT_FIXTURE_ASSEMBLY_CLEANUP_ONCE");
        if (Environment.GetEnvironmentVariable("CIS_COMPAT_FIXTURE_CLEANUP_FAILURE") == "1")
            throw new InvalidOperationException("COMPAT_FIXTURE_EXPECTED_ASSEMBLY_CLEANUP_FAILURE");
    }
}

internal static class Lifecycle
{
    private static readonly object Gate = new();
    private static int request;
    private static int cleanupCalls;

    internal static void Begin(string phase)
    {
        request++;
        cleanupCalls = 0;
        Write(phase, nameof(AssemblyHooks));
    }

    internal static int IncrementCleanup() => Interlocked.Increment(ref cleanupCalls);

    internal static void Write(string phase, string? test, string? outcome = null)
    {
        var path = Environment.GetEnvironmentVariable("CIS_COMPAT_FIXTURE_LIFECYCLE")
            ?? throw new InvalidOperationException("Supply the fixture lifecycle output path.");
        lock (Gate)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                throw new IOException("Fixture lifecycle capture exceeded its bound.");
            File.AppendAllText(path, JsonSerializer.Serialize(new
            {
                phase, test, outcome, request, processId = Environment.ProcessId, utc = DateTimeOffset.UtcNow
            }) + "\n");
        }
    }
}
