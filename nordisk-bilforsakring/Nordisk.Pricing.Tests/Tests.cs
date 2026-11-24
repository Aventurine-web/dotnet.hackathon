using System.Text.RegularExpressions;
using Nordisk.Pricing;
using Xunit.Abstractions;

namespace Nordisk.Pricing.Tests;

public class Fixture
{
    public static readonly string Root = FindRoot();
    public static readonly Tariff Tariff = Tariff.Load(Path.Combine(Root, "tariff.json"));
    public static readonly DataStore Data = new(Path.Combine(Root, "Data"));
    public static readonly PricingEngine Engine = new(Tariff, Data);
    public static readonly DateOnly Today = new(2026, 11, 24);

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Nordisk.Pricing", "tariff.json"))) d = d.Parent;
        return Path.Combine(d!.FullName, "Nordisk.Pricing");
    }

    /// <summary>Builds a valid 12-digit personnummer (adds the Luhn check digit).</summary>
    public static string Pnr(string yyyymmdd, int serial3 = 123)
    {
        var nine = yyyymmdd[2..] + serial3.ToString("D3");
        int sum = 0;
        for (int i = 0; i < 9; i++) { int d = nine[i] - '0'; if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; } sum += d; }
        return yyyymmdd + serial3.ToString("D3") + ((10 - sum % 10) % 10);
    }
}

public class IdentityTests
{
    [Fact] public void Accepts_12_digits() => Assert.Equal(IdKind.Valid, Identity.Parse(Fixture.Pnr("19850615"), Fixture.Today).Kind);
    [Fact] public void Accepts_10_digits_with_dash()
    {
        var r = Identity.Parse(Fixture.Pnr("19850615")[2..].Insert(6, "-"), Fixture.Today);
        Assert.Equal(IdKind.Valid, r.Kind);
        Assert.Equal(new DateOnly(1985, 6, 15), r.Born);
    }
    [Fact] public void Plus_means_over_100_years()
    {
        var r = Identity.Parse(Fixture.Pnr("19200101")[2..].Insert(6, "+"), Fixture.Today);
        Assert.Equal(new DateOnly(1920, 1, 1), r.Born);
    }
    [Fact] public void Rejects_bad_checksum()
    {
        var p = Fixture.Pnr("19850615"); var bad = p[..11] + (p[11] == '9' ? '0' : (char)(p[11] + 1));
        Assert.Equal(IdKind.Invalid, Identity.Parse(bad, Fixture.Today).Kind);
    }
    [Fact] public void Rejects_impossible_date() => Assert.Equal(IdKind.Invalid, Identity.Parse(Fixture.Pnr("19850230"), Fixture.Today).Kind);
    [Fact] public void Rejects_future_birth() => Assert.Equal(IdKind.Invalid, Identity.Parse(Fixture.Pnr("20301010"), Fixture.Today).Kind);
    [Theory] [InlineData("")] [InlineData(null)] [InlineData("abc")] [InlineData("1985061512")] [InlineData("198506151234567")]
    public void Rejects_garbage(string? s) => Assert.Equal(IdKind.Invalid, Identity.Parse(s, Fixture.Today).Kind);
    [Fact] public void Recognises_samordningsnummer() => Assert.Equal(IdKind.Coordination, Identity.Parse(Fixture.Pnr("19850675"), Fixture.Today).Kind);
    [Fact] public void Recognises_norwegian_and_finnish_ids()
    {
        Assert.Equal(IdKind.ForeignNordic, Identity.Parse("15068512345", Fixture.Today).Kind);
        Assert.Equal(IdKind.ForeignNordic, Identity.Parse("130728-478N", Fixture.Today).Kind);
    }
    [Theory] [InlineData("abc123", "ABC123")] [InlineData("ABC 12D", "ABC12D")] [InlineData("abc-123", "ABC123")]
    public void Regnummer_normalises(string input, string expected) => Assert.Equal(expected, Regnummer.Normalize(input));
    [Theory] [InlineData("AB1234")] [InlineData("IBC123")] [InlineData("ABC1234")] [InlineData("ABC1D2")] [InlineData("")] [InlineData(null)]
    public void Regnummer_rejects(string? input) => Assert.Null(Regnummer.Normalize(input));
}

public class EngineTests(ITestOutputHelper output)
{
    static Quote Q(string pnr, string reg, int? crashes = null) => Fixture.Engine.Calculate(pnr, reg, crashes, Fixture.Today);
    const string Reg = "AAA44L"; // Ford Kuga 2021 hybrid 225 hp, 234 000 kr

    [Fact] public void Premium_equals_sum_of_explanation_lines()
    {
        var q = Q(Fixture.Pnr("19850615"), Reg);
        Assert.Equal(QuoteStatus.Priced, q.Status);
        Assert.Equal(q.Premium, q.Lines.Sum(l => l.Amount));
    }
    [Fact] public void Young_driver_pays_more_than_middle_aged()
    {
        var young = Q(Fixture.Pnr("20050615"), Reg).Premium!.Value;
        var mid = Q(Fixture.Pnr("19850615"), Reg).Premium!.Value;
        var old = Q(Fixture.Pnr("19520615"), Reg).Premium!.Value;
        Assert.True(young > mid * 1.7, $"{young} vs {mid}");
        Assert.True(old < mid);
    }
    [Fact] public void Powerful_car_costs_more()
    {
        var weak = Fixture.Data.FindCar("AAB623")!; // Volvo V70, 140 hp
        Assert.True(weak.Hp < 150);
        var strong = Q(Fixture.Pnr("19850615"), Reg).Lines.First(l => l.Key == "line.hp").Amount;
        var low = Q(Fixture.Pnr("19850615"), "AAB623").Lines.First(l => l.Key == "line.hp").Amount;
        Assert.True(strong > 0); Assert.Equal(0, low);
    }
    [Fact] public void Minimum_premium_applies_to_cheapest_risk()
    {
        var cheap = Fixture.Data.FindCar("AAB623")!;
        var q = Q(Fixture.Pnr("19520615"), cheap.Reg);
        Assert.Equal(Fixture.Tariff.MinimumPremium, q.Premium);
        Assert.Contains(q.Lines, l => l.Key == "line.minimum");
    }
    [Fact] public void Claim_free_history_gives_discount_and_crashes_give_surcharge()
    {
        var pnr = Fixture.Pnr("19850615");
        var none = Q(pnr, Reg).Premium!.Value;
        var clean = Q(pnr, Reg, 0).Premium!.Value;
        var two = Q(pnr, Reg, 2).Premium!.Value;
        Assert.True(clean < none && none < two, $"{clean} {none} {two}");
    }
    [Fact] public void Three_crashes_in_five_years_goes_to_manual_review()
    {
        var q = Q(Fixture.Pnr("19850615"), Reg, 3);
        Assert.Equal(QuoteStatus.Referred, q.Status); Assert.Equal("too_high_risk", q.Reason);
    }
    [Fact] public void Price_above_ceiling_goes_to_manual_review()
    {
        var car = Fixture.Data.FindCar("AAB623")!;
        var priciest = AllCars().OrderByDescending(c => c.Value).First();
        var q = Q(Fixture.Pnr("20080615"), priciest.Reg, 2);
        output.WriteLine($"{priciest.Make} {priciest.Model} {priciest.Value} kr -> {q.Status} {q.Premium} {q.Reason}");
        Assert.Equal(QuoteStatus.Referred, q.Status); Assert.Equal("price_ceiling", q.Reason);
    }
    [Fact] public void Declines_and_referrals()
    {
        Assert.Equal("invalid_pnr", Q("123", Reg).Reason);
        Assert.Equal("too_young", Q(Fixture.Pnr("20100101"), Reg).Reason);
        Assert.Equal("invalid_reg", Q(Fixture.Pnr("19850615"), "XX").Reason);
        Assert.Equal("car_not_found", Q(Fixture.Pnr("19850615"), "ZZZ999").Reason);
        Assert.Equal("coordination", Q(Fixture.Pnr("19850675"), Reg).Reason);
        Assert.Equal("foreign_id", Q("15068512345", Reg).Reason);
    }
    [Fact] public void Existing_customer_on_own_car_is_told_it_is_already_insured()
        => Assert.Equal("already_insured", Q("193311288454", "WED538").Reason);

    [Fact] public void Existing_customer_history_is_used_for_a_new_car()
    {
        // Find a customer with crashes who is not insured on Reg.
        var q = Q("193311288454", Reg);
        Assert.Contains(q.Lines, l => l.Key == "line.history" || l.Key == "line.history_none");
        Assert.Contains(q.Lines, l => l.Key == "line.history");
    }

    [Fact] public void All_300_applications_get_a_clean_answer()
    {
        var rows = File.ReadLines(Path.Combine(Fixture.Root, "Data", "ansokningar.csv")).Skip(1).Select(l => l.Split(',')).ToList();
        var results = rows.Select(r => Q(r[1], r[2])).ToList();
        foreach (var g in results.GroupBy(r => r.Status + " " + r.Reason)) output.WriteLine($"{g.Key}: {g.Count()}");
        var prices = results.Where(r => r.Premium.HasValue).Select(r => r.Premium!.Value).OrderBy(x => x).ToList();
        output.WriteLine($"priced {prices.Count}, min {prices.First()}, median {prices[prices.Count / 2]}, mean {prices.Average():F0}, max {prices.Last()}");
        Assert.Equal(300, results.Count);
        Assert.All(prices, p => Assert.InRange(p, Fixture.Tariff.MinimumPremium, Fixture.Tariff.CeilingPremium));
        Assert.All(results.Where(r => r.Status == QuoteStatus.Priced), r => Assert.Equal(r.Premium, r.Lines.Sum(l => l.Amount)));
    }

    [Fact] public void Existing_customers_are_priced_close_to_their_real_cost()
    {
        // Sanity check of calibration: for each customer car, quote an identical stranger-free profile and compare totals.
        var rows = File.ReadLines(Path.Combine(Fixture.Root, "Data", "kunder.csv")).Skip(1).Select(l => l.Split(',')).Take(2000).ToList();
        double claims = 0, premium = 0;
        foreach (var r in rows)
        {
            var id = Identity.Parse(r[0], Fixture.Today); if (id.Kind != IdKind.Valid) continue;
            int years = int.Parse(r[2]);
            // Quote as if the car were not yet insured (other pnr digits), using the customer's own crashes as stated history is not possible: use no history.
            var q = Fixture.Engine.Calculate(Fixture.Pnr(r[0][..8], 999), r[1], null, Fixture.Today);
            if (q.Premium is null) continue;
            claims += double.Parse(r[4]); premium += q.Premium.Value * years;
        }
        output.WriteLine($"Historic claims / modelled premium = {claims / premium:F2} (target loss ratio {Fixture.Tariff.TargetLossRatio})");
        Assert.InRange(claims / premium, 0.55, 0.95);
    }

    static IEnumerable<Car> AllCars() =>
        File.ReadLines(Path.Combine(Fixture.Root, "Data", "bilar.csv")).Skip(1).Select(l => Fixture.Data.FindCar(l.Split(',')[0])!);
}

public class LocalizationTests
{
    static readonly Loc Loc = new(Path.Combine(Fixture.Root, "i18n"));

    [Fact] public void Every_language_has_every_key_and_the_same_placeholders()
    {
        var en = Loc.Table("en");
        foreach (var (code, _, _) in Nordisk.Pricing.Loc.Languages)
        {
            var t = Loc.Table(code);
            Assert.Equal(en.Keys.OrderBy(x => x), t.Keys.OrderBy(x => x));
            foreach (var k in en.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(t[k]), $"{code}:{k} empty");
                Assert.Equal(Placeholders(en[k]), Placeholders(t[k]));
            }
        }
    }

    [Fact] public void Every_reason_the_engine_can_return_is_translated()
    {
        foreach (var r in new[] { "invalid_pnr", "coordination", "foreign_id", "too_young", "invalid_reg", "car_not_found", "already_insured", "too_high_risk", "price_ceiling" })
            Assert.True(Loc.Table("en").ContainsKey("reason." + r), r);
    }

    static string Placeholders(string s) => string.Join(",", Regex.Matches(s, @"\{\w+\}").Select(m => m.Value).OrderBy(x => x));
}
