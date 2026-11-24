using System.Globalization;

namespace Nordisk.Pricing;

public sealed record Car(string Reg, string Make, string Model, int Year, int Value, string Fuel, int Hp);
public sealed record History(int Years, int Crashes, int Cars);

/// <summary>In-memory data from the three CSV files. In production this sits behind a database / vehicle-register API.</summary>
public sealed class DataStore
{
    private readonly Dictionary<string, Car> _cars = new();
    private readonly Dictionary<string, History> _history = new();
    private readonly Dictionary<string, int> _applications = new();
    private readonly HashSet<(string Pnr, string Reg)> _insured = new();
    public int SkippedRows { get; private set; }

    public DataStore(string dir)
    {
        foreach (var f in Rows(dir, "bilar.csv", 7))
            if (int.TryParse(f[3], out var year) && int.TryParse(f[4], out var value) && int.TryParse(f[6], out var hp))
                _cars[f[0]] = new Car(f[0], f[1], f[2], year, value, f[5].ToLowerInvariant(), hp); else SkippedRows++;

        foreach (var f in Rows(dir, "kunder.csv", 6))
            if (int.TryParse(f[2], out var years) && int.TryParse(f[3], out var crashes))
            {
                _insured.Add((f[0], f[1]));
                var h = _history.GetValueOrDefault(f[0]) ?? new History(0, 0, 0);
                _history[f[0]] = new History(h.Years + years, h.Crashes + crashes, h.Cars + 1);
            }
            else SkippedRows++;

        foreach (var f in Rows(dir, "ansokningar.csv", 4))
            if (int.TryParse(f[3], out var crashes)) _applications[f[1]] = crashes; else SkippedRows++;
    }

    public int CarCount => _cars.Count;
    public Car? FindCar(string reg) => _cars.GetValueOrDefault(reg);
    public History? FindHistory(string pnr12) => _history.GetValueOrDefault(pnr12);
    public int? FindApplicationCrashes(string pnr12) => _applications.TryGetValue(pnr12, out var c) ? c : null;
    public bool IsInsured(string pnr12, string reg) => _insured.Contains((pnr12, reg));

    private IEnumerable<string[]> Rows(string dir, string file, int columns)
    {
        foreach (var line in File.ReadLines(Path.Combine(dir, file)).Skip(1).Where(l => l.Length > 0))
        {
            var f = line.Split(',').Select(x => x.Trim()).ToArray();
            if (f.Length == columns) yield return f; else SkippedRows++;
        }
    }
}
