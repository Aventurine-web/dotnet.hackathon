namespace Nordisk.Pricing;

public enum QuoteStatus { Priced, Declined, Referred }

/// <param name="Key">Translation key. Never customer-facing text, so every language gets the same explanation.</param>
/// <param name="Amount">Kronor this step added to (or removed from) the running premium. All amounts sum to the premium.</param>
public sealed record Line(string Key, Dictionary<string, object> Args, int Amount);

public sealed record Quote(QuoteStatus Status, int? Premium, string? Reason, IReadOnlyList<Line> Lines, string TariffVersion, Car? Car);

public sealed class PricingEngine(Tariff t, DataStore data)
{
    public Quote Calculate(string? personnummer, string? regnummer, int? selfReportedCrashes, DateOnly today)
    {
        Quote Stop(QuoteStatus s, string reason, Car? car = null) => new(s, null, reason, [], t.Version, car);

        // 1. Who is asking?
        var id = Identity.Parse(personnummer, today);
        switch (id.Kind)
        {
            case IdKind.Invalid: return Stop(QuoteStatus.Declined, "invalid_pnr");
            case IdKind.Coordination: return Stop(QuoteStatus.Referred, "coordination");
            case IdKind.ForeignNordic: return Stop(QuoteStatus.Referred, "foreign_id");
        }
        var pnr = id.Digits12!;
        int age = id.AgeOn(today);
        if (age < t.MinAge) return Stop(QuoteStatus.Declined, "too_young");

        // 2. Which car?
        var plate = Regnummer.Normalize(regnummer);
        if (plate is null) return Stop(QuoteStatus.Declined, "invalid_reg");
        var car = data.FindCar(plate);
        if (car is null) return Stop(QuoteStatus.Referred, "car_not_found");
        if (data.IsInsured(pnr, plate)) return Stop(QuoteStatus.Declined, "already_insured", car);

        // 3. History: our own customer records first, then a stored application, then what the customer states.
        int years = 0, crashes = 0;
        if (data.FindHistory(pnr) is { } h) (years, crashes) = (h.Years, h.Crashes);
        else if ((data.FindApplicationCrashes(pnr) ?? selfReportedCrashes) is { } n) (years, crashes) = (5, n);

        if (years > 0 && crashes >= t.ReferralMinCrashes && (double)crashes / years >= t.ReferralMinCrashRate)
            return Stop(QuoteStatus.Referred, "too_high_risk", car);

        // 4. Price = base premium x factors. Every step is recorded so it can be explained.
        double fAge = Tariff.Lookup(t.Age, age);
        double fHp = Tariff.Lookup(t.Horsepower, car.Hp);
        double fValue = Tariff.Lookup(t.Value, car.Value);
        double fFuel = t.Fuel.GetValueOrDefault(car.Fuel, t.FuelDefault);

        // Gamma-Poisson credibility: compare actual crashes with the number expected for this driver's age.
        double expected = t.BaselineCrashFrequency * fAge * years;
        double fHistory = years == 0 ? 1.0 : Math.Max(t.HistoryFloor, (t.CredibilityShape + crashes) / (t.CredibilityShape + expected));

        double running = t.BasePremium;
        int previous = (int)Math.Round(running);
        var lines = new List<Line> { new("line.base", [], previous) };

        void Step(string key, double factor, Dictionary<string, object> args)
        {
            running *= factor;
            int rounded = (int)Math.Round(running);
            args["pct"] = Pct(factor);
            lines.Add(new Line(key, args, rounded - previous));
            previous = rounded;
        }

        Step("line.age", fAge, new() { ["age"] = age });
        Step("line.hp", fHp, new() { ["hp"] = car.Hp });
        Step("line.value", fValue, new() { ["value"] = car.Value });
        Step("line.fuel", fFuel, new() { ["fuel"] = new Ref(t.Fuel.ContainsKey(car.Fuel) ? "fuel." + car.Fuel : "fuel.other") });
        if (years == 0) lines.Add(new Line("line.history_none", [], 0));
        else Step("line.history", fHistory, new() { ["years"] = years, ["crashes"] = crashes });

        if (previous > t.CeilingPremium) return Stop(QuoteStatus.Referred, "price_ceiling", car);

        if (previous < t.MinimumPremium)
        {
            lines.Add(new Line("line.minimum", [], t.MinimumPremium - previous));
            previous = t.MinimumPremium;
        }
        return new Quote(QuoteStatus.Priced, previous, null, lines, t.Version, car);
    }

    private static string Pct(double factor)
    {
        int p = (int)Math.Round((factor - 1) * 100);
        return p > 0 ? $"+{p} %" : p < 0 ? $"-{-p} %" : "0 %";
    }
}
