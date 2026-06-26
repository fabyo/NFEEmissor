namespace Nfe.Core;

public static class GtinValidator
{
    private static readonly HashSet<int> ValidLengths = [8, 12, 13, 14];

    public static bool IsEmptyOrSemGtin(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value.Trim(), "SEM GTIN", StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string? value)
    {
        if (IsEmptyOrSemGtin(value)) return true;

        var digits = value!.Trim();
        if (!ValidLengths.Contains(digits.Length) || digits.Any(c => !char.IsDigit(c)))
        {
            return false;
        }

        var sum = 0;
        var weight = 3;
        for (var i = digits.Length - 2; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit == digits[^1] - '0';
    }

    public static string ToXmlValue(string? value)
        => IsEmptyOrSemGtin(value) ? "SEM GTIN" : value!.Trim();
}
