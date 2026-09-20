using System.Runtime.CompilerServices;

namespace Cis.FunctionProfiler.Fixture;

internal static class Program
{
    private static int _value = 7;
    public static async Task Main()
    {
        Check(Recursive(4) == 5, "recursion");
        Check(Parent() == 6, "nesting");
        try { Throws(); } catch (InvalidOperationException) { }
        Check(FilterAndFinally() == 3, "filter/finally");
        Check(Iterator().Sum() == 3, "iterator");
        Check(await Asynchronous() == 8, "async");
        try { await AsyncThrows(); } catch (InvalidOperationException) { }
        Check(new Sample(4).Value == 4, "constructor/accessor");
        try { _ = new Sample(-1); } catch (ArgumentException) { }
        Check(new Pair(3, 4).Sum == 7, "struct/record");
        Check((new Pair(3, 4) with { First = 5 }).Sum == 9, "init setter");
        ByReference() = 11;
        Check(_value == 11, "byref return");
        Check(Generic(13) == 13, "generic");
        Check(SpanValue([1, 2, 3]) == 6, "ref struct");
        Check(LocalAndLambda() == 5, "local function/lambda");
        var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        { for (var i = 0; i < 100; i++) ParallelLeaf(); })).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        Console.WriteLine("fixture passed");
    }
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Recursive(int remaining) => remaining == 0 ? 1 : 1 + Recursive(remaining - 1);
    private static int Parent() => Leaf() + Leaf() + Leaf();
    private static int Leaf() { Thread.Sleep(2); return 2; }
    private static void Throws() => throw new InvalidOperationException();
    private static int FilterAndFinally()
    {
        var value = 0;
        try { Throws(); }
        catch (InvalidOperationException exception) when (exception.Message.Length > 0) { value = 2; }
        finally { value++; }
        return value;
    }
    private static IEnumerable<int> Iterator() { yield return 1; yield return 2; }
    private static async Task<int> Asynchronous() { await Task.Delay(5); return 8; }
    private static async Task AsyncThrows() { await Task.Yield(); Throws(); }
    private static ref int ByReference() => ref _value;
    private static T Generic<T>(T value) => value;
    private static int SpanValue(ReadOnlySpan<int> values)
    { var sum = 0; foreach (var value in values) sum += value; return sum; }
    private static int LocalAndLambda()
    { int Local(int v) => v + 1; Func<int, int> lambda = v => Local(v) + 1; return lambda(3); }
    private static void ParallelLeaf() => Thread.SpinWait(20);
    private sealed class Sample
    {
        public int Value { get; }
        public Sample(int value) { if (value < 0) throw new ArgumentException(); Value = value; }
    }
    private readonly record struct Pair(int First, int Second)
    { public int Sum => First + Second; }
}
