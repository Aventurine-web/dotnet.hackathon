using System.Text.RegularExpressions;

namespace Nordisk.Pricing;

public enum IdKind { Valid, Invalid, Coordination, ForeignNordic }

public sealed record IdResult(IdKind Kind, string? Digits12 = null, DateOnly? Born = null)
{
    public int AgeOn(DateOnly today) => today.Year - Born!.Value.Year - (today < Born.Value.AddYears(today.Year - Born.Value.Year) ? 1 : 0);
}

/// <summary>Validation of Swedish personnummer / samordningsnummer and recognition of other Nordic ID formats.</summary>
public static partial class Identity
{
    [GeneratedRegex(@"^(?<c>\d{2})?(?<y>\d{2})(?<m>\d{2})(?<d>\d{2})(?<sep>[-+]?)(?<n>\d{4})$")]
    private static partial Regex Swedish();

    [GeneratedRegex(@"^\d{11}$")] private static partial Regex Norwegian();
    [GeneratedRegex(@"^\d{6}[-+A-FU-Y]\d{3}[0-9A-FHJ-NPR-Y]$")] private static partial Regex Finnish();

    public static IdResult Parse(string? input, DateOnly today)
    {
        var s = (input ?? "").Trim().Replace(" ", "").ToUpperInvariant();
        var m = Swedish().Match(s);
        if (!m.Success)
            return Norwegian().IsMatch(s) || Finnish().IsMatch(s) ? new(IdKind.ForeignNordic) : new(IdKind.Invalid);

        int yy = int.Parse(m.Groups["y"].Value), mm = int.Parse(m.Groups["m"].Value), dd = int.Parse(m.Groups["d"].Value);
        bool coordination = dd > 60;
        int day = coordination ? dd - 60 : dd;
        bool hasCentury = m.Groups["c"].Success;
        int year = hasCentury ? int.Parse(m.Groups["c"].Value) * 100 + yy : today.Year / 100 * 100 + yy;

        if (!TryDate(year, mm, day, out var born)) return new(IdKind.Invalid);
        if (!hasCentury)
        {
            if (born > today) { year -= 100; if (!TryDate(year, mm, day, out born)) return new(IdKind.Invalid); }
            if (m.Groups["sep"].Value == "+") { year -= 100; if (!TryDate(year, mm, day, out born)) return new(IdKind.Invalid); }
        }
        if (born > today || year < 1900) return new(IdKind.Invalid);

        var ten = $"{yy:D2}{mm:D2}{dd:D2}{m.Groups["n"].Value}";
        if (!Luhn(ten)) return new(IdKind.Invalid);

        var digits12 = $"{year:D4}{mm:D2}{dd:D2}{m.Groups["n"].Value}";
        return new(coordination ? IdKind.Coordination : IdKind.Valid, digits12, born);
    }

    private static bool TryDate(int y, int m, int d, out DateOnly date)
    {
        date = default;
        if (y < 1 || y > 9999 || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return false;
        date = new DateOnly(y, m, d);
        return true;
    }

    private static bool Luhn(string tenDigits)
    {
        int sum = 0;
        for (int i = 0; i < 10; i++)
        {
            int d = tenDigits[i] - '0';
            if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return sum % 10 == 0;
    }
}

public static partial class Regnummer
{
    // Old format ABC123 and new format ABC12D. Letters I, Q, V, Å, Ä, Ö are never used.
    [GeneratedRegex(@"^[A-HJ-PR-UW-Z]{3}(\d{3}|\d{2}[A-HJ-PR-UW-Z])$")]
    private static partial Regex Format();

    /// <summary>Upper-cases and strips spaces/hyphens. Returns null if it is not a valid Swedish registration number.</summary>
    public static string? Normalize(string? input)
    {
        var s = (input ?? "").Replace(" ", "").Replace("-", "").ToUpperInvariant();
        return Format().IsMatch(s) ? s : null;
    }
}
