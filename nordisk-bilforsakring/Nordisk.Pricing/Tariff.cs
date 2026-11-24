using System.Text.Json;

namespace Nordisk.Pricing;

public sealed record Band(int UpTo, double Factor);

/// <summary>All pricing rules and numbers. Loaded from tariff.json so they can change without code changes.</summary>
public sealed class Tariff
{
    public string Version { get; init; } = "";
    public double BaseClaimsPerYear { get; init; }
    public double TargetLossRatio { get; init; }
    public int MinimumPremium { get; init; }
    public int CeilingPremium { get; init; }
    public int MinAge { get; init; }
    public double BaselineCrashFrequency { get; init; }
    public double CredibilityShape { get; init; }
    public double HistoryFloor { get; init; }
    public int ReferralMinCrashes { get; init; }
    public double ReferralMinCrashRate { get; init; }
    public Band[] Age { get; init; } = [];
    public Band[] Horsepower { get; init; } = [];
    public Band[] Value { get; init; } = [];
    public Dictionary<string, double> Fuel { get; init; } = new();
    public double FuelDefault { get; init; } = 1.0;

    public double BasePremium => BaseClaimsPerYear / TargetLossRatio;

    public static double Lookup(Band[] bands, int value) => bands.First(b => value <= b.UpTo).Factor;

    public static Tariff Load(string path) =>
        JsonSerializer.Deserialize<Tariff>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
