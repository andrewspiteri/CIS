using Example.Core;

namespace Example.UnitTests;

public sealed class CoreContractTests
{
    [Fact(DisplayName = "TC-FEAT-FR-001-001-001 ExistingSourceConsumerCanConstructDeconstructAndCopyReading")]
    public void ExistingSourceConsumerCanConstructDeconstructAndCopyReading()
    {
        var identity = Guid.Parse("fe800331-3c23-4198-ae60-c38fdb47e5ca");
        var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var reading = new Reading(Id: identity, ObservedAt: timestamp, Quantity: 12.34567890m);
        reading.Validate();
        reading.Deconstruct(out Guid id, out DateTimeOffset observedAt, out decimal quantity);
        Assert.Equal(identity, id);
        Assert.Equal(timestamp, observedAt);
        Assert.Equal(12.34567890m, quantity);
        Assert.Equal(new Reading(identity, timestamp, 2m), reading with { Quantity = 2m });
        Assert.True(typeof(Reading).IsSealed);
    }

    [Fact]
    public void StoragePortKeepsItsConsumedAsyncSignatures()
    {
        var methods = typeof(IReadingStore).GetMethods();
        Assert.Equal(2, methods.Length);
        var append = Assert.Single(methods, method => method.Name == nameof(IReadingStore.AppendAsync));
        Assert.Equal(typeof(Task<bool>), append.ReturnType);
        Assert.Equal([typeof(Reading), typeof(CancellationToken)], append.GetParameters().Select(parameter => parameter.ParameterType));
        var read = Assert.Single(methods, method => method.Name == nameof(IReadingStore.ReadAllAsync));
        Assert.Equal(typeof(Task<IReadOnlyList<Reading>>), read.ReturnType);
        Assert.Equal([typeof(CancellationToken)], read.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
