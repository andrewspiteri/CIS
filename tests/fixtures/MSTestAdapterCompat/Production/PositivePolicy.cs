namespace CompatFixture;

public static class PositivePolicy
{
    public static int RequirePositive(int value) => value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
