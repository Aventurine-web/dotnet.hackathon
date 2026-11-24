using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Nordisk.Pricing;

var builder = WebApplication.CreateBuilder(args);
var root = builder.Environment.ContentRootPath;

builder.Services.AddSingleton(Tariff.Load(Path.Combine(root, "tariff.json")));
builder.Services.AddSingleton(new DataStore(builder.Configuration["DataPath"] ?? Path.Combine(root, "Data")));
builder.Services.AddSingleton(new Loc(Path.Combine(root, "i18n")));
builder.Services.AddSingleton<PricingEngine>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Personnummer lookups must not be enumerable: limit requests per client address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => "ok");
app.MapGet("/api/languages", () => Loc.Languages.Select(l => new { code = l.Code, name = l.Name }));
app.MapGet("/api/strings/{lang}", (string lang, Loc loc) => loc.Ui(loc.Normalize(lang)));

app.MapPost("/api/quote", (QuoteRequest r, PricingEngine engine, Loc loc, ILogger<Program> log) =>
{
    if (r.Crashes is < 0 or > 20) return Results.BadRequest();
    var lang = loc.Normalize(r.Lang);
    var q = engine.Calculate(r.Personnummer, r.Regnummer, r.Crashes, DateOnly.FromDateTime(DateTime.UtcNow));

    // Never log personnummer or regnummer.
    log.LogInformation("Quote {Status} {Reason}", q.Status, q.Reason);

    return Results.Ok(new
    {
        status = q.Status,
        premium = q.Premium,
        currency = "SEK",
        title = q.Status == QuoteStatus.Priced ? null : loc.T(lang, q.Status == QuoteStatus.Referred ? "ui.title_referred" : "ui.title_declined"),
        message = q.Reason is null ? null : loc.T(lang, "reason." + q.Reason),
        lines = q.Lines.Select(l => new { text = loc.T(lang, l.Key, l.Args), amount = l.Amount }),
        car = q.Car is null ? null : $"{q.Car.Make} {q.Car.Model} {q.Car.Year}",
        tariffVersion = q.TariffVersion,
    });
});

app.Run();

public sealed record QuoteRequest(string? Personnummer, string? Regnummer, int? Crashes, string? Lang);
public partial class Program;
