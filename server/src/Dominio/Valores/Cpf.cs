namespace Xaquiamuni.Dominio.Valores;

public sealed class Cpf : IEquatable<Cpf>
{
    private readonly string _normalizedDigits;

    private Cpf(string normalizedDigits)
    {
        _normalizedDigits = normalizedDigits;
    }

    public static bool TryCreate(string? value, out Cpf? cpf)
    {
        cpf = null;
        if (!TryNormalize(value, out var normalizedDigits) || !HasValidCheckDigits(normalizedDigits))
        {
            return false;
        }

        cpf = new Cpf(normalizedDigits);
        return true;
    }

    public string GetNormalizedDigitsForProtection() => _normalizedDigits;

    public bool Equals(Cpf? other) =>
        other is not null && string.Equals(_normalizedDigits, other._normalizedDigits, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Cpf other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_normalizedDigits);

    public override string ToString() => "[CPF]";

    private static bool TryNormalize(string? value, out string normalizedDigits)
    {
        normalizedDigits = string.Empty;
        if (value is null)
        {
            return false;
        }

        if (value.Length == 11)
        {
            if (!IsAsciiDigits(value))
            {
                return false;
            }

            normalizedDigits = value;
            return true;
        }

        if (value.Length != 14
            || value[3] != '.'
            || value[7] != '.'
            || value[11] != '-')
        {
            return false;
        }

        Span<char> digits = stackalloc char[11];
        var digitIndex = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (index is 3 or 7 or 11)
            {
                continue;
            }

            var character = value[index];
            if (character is < '0' or > '9')
            {
                return false;
            }

            digits[digitIndex++] = character;
        }

        normalizedDigits = new string(digits);
        return true;
    }

    private static bool IsAsciiDigits(string value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidCheckDigits(string digits)
    {
        var allDigitsEqual = true;
        for (var index = 1; index < digits.Length; index++)
        {
            if (digits[index] != digits[0])
            {
                allDigitsEqual = false;
                break;
            }
        }

        if (allDigitsEqual)
        {
            return false;
        }

        var firstCheckDigit = CalculateCheckDigit(digits.AsSpan(0, 9));
        if (digits[9] - '0' != firstCheckDigit)
        {
            return false;
        }

        var secondCheckDigit = CalculateCheckDigit(digits.AsSpan(0, 10));
        return digits[10] - '0' == secondCheckDigit;
    }

    private static int CalculateCheckDigit(ReadOnlySpan<char> digits)
    {
        var weight = digits.Length + 1;
        var sum = 0;
        foreach (var digit in digits)
        {
            sum += (digit - '0') * weight--;
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
