namespace Example.Core;

public sealed record Reading(Guid Id, DateTimeOffset ObservedAt, decimal Quantity)
{
    public void Validate()
    {
        if (Id == Guid.Empty) throw new ArgumentException("A reading needs a stable identity.");
        if (ObservedAt.Offset != TimeSpan.Zero) throw new ArgumentException("Event time must be UTC.");
        if (ObservedAt.Ticks % 10 != 0) throw new ArgumentException("Event time must have microsecond precision for exact persistence.");
        if (Quantity < 0 || Quantity >= 100000000000000000000m || decimal.Round(Quantity, 8) != Quantity)
            throw new ArgumentOutOfRangeException(nameof(Quantity), "Quantity must be nonnegative, less than 10^20, with at most eight decimal places.");
    }
}
