using System.Globalization;
using System.Text.Json;

namespace Example.Cli;

/// <summary>Checks the exact decimal token, including exponent notation, without rounding its coefficient.</summary>
internal static class ExactQuantity
{
    internal static void Validate(string token)
    {
        // Native JSON deserialization has already checked syntax and constructor fields.
        var text = token.Trim();
        var exponentAt = text.IndexOfAny(['e', 'E']);
        var mantissa = exponentAt < 0 ? text : text[..exponentAt];
        var coefficient = mantissa.TrimStart('+', '-').Replace(".", "", StringComparison.Ordinal);
        var significant = coefficient.TrimStart('0').TrimEnd('0');
        if (significant.Length == 0) return;

        var exponent = 0;
        if (exponentAt >= 0 && !int.TryParse(text.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent))
            throw new JsonException("Quantity exponent is outside the supported exact range.");

        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var fractionalDigits = point < 0 ? 0 : mantissa.Length - point - 1;
        var trailingZeros = coefficient.Length - coefficient.TrimEnd('0').Length;
        var scale = (long)fractionalDigits - exponent - trailingZeros;
        if (mantissa.StartsWith('-') || scale > 8 || significant.Length - scale > 20)
            throw new JsonException("Quantity must be nonnegative, less than 10^20, with at most eight exact decimal places.");
    }
}
