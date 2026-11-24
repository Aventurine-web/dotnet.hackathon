# Nordisk Bilförsäkring – pricing

Input: registration number + personnummer. Output: yearly premium in whole SEK, or a customer-friendly reason why no price is given. Website and API in one .NET 10 app.

## Run
```
cd Nordisk.Pricing && dotnet run        # http://localhost:5000 (see console), page + API
cd Nordisk.Pricing.Tests && dotnet test # 36 tests incl. all 300 applications
docker build -t nordisk-pricing . && docker run -p 8080:8080 nordisk-pricing
```
API: `POST /api/quote {"personnummer","regnummer","crashes"?,"lang"?}`, `GET /api/languages`, `GET /api/strings/{lang}`, `GET /healthz`.

## How the price is made (`PricingEngine.cs`, numbers in `tariff.json`)
`premium = max(3 500, base × age × horsepower × car value × fuel × history)`, rounded to whole kr.
- Base 4 283 kr = expected claims 2 998 kr/year for the reference driver and car ÷ 0.70 target loss ratio.
- Factors were fitted to kunder.csv (claims per insured year, all factors fitted together, thin bands rounded/shrunk).
- History (Gamma-Poisson credibility): actual crashes vs. crashes expected for the driver's age and insured years. Claim-free gives a discount (max 35%), crashes a surcharge. Source: kunder.csv (summed over all of the person's cars), else ansokningar.csv, else the optional "crashes last 5 years" field, else none.
- Every step is returned as a translated line with its kr amount; the lines add up exactly to the premium.
- No price, with a customer message in the chosen language: invalid personnummer, under 18, invalid regnummer, already insured on this car (declined); samordningsnummer, other Nordic ID, car not found, ≥3 crashes at ≥0.4/year, price above 40 000 kr (referred to manual review, GDPR art. 22).
- Not used: gender (illegal in EU pricing).
- Sanity check (test): modelled premiums for existing customers give a loss ratio of 0.72 against the 0.70 target.

## Change things
- Prices, floor, ceiling, bands: `tariff.json` (bump `version`).
- Texts / new language: `i18n/*.json` + `Loc.Languages`. A test fails if a language misses a key or placeholder.
- Data source: `DataStore.cs` (CSV in memory today; replace with a database and Transportstyrelsen vehicle-register lookup).

## Known limits / before production
- A personnummer is not proof of identity. Production needs BankID login before a price or any history is shown (decision: skipped for the hackathon).
- Translations (fi, is, nn, da, nb) are machine-drafted; have native speakers review them.
- Customer data CSVs are in `Nordisk.Pricing/Data` for the demo; production data belongs in an EU database, not in the repo.
- Rate limit 60 requests/min per IP; behind Azure ingress configure forwarded headers.
- Deploy: `az containerapp up --source . --name nordisk-pricing --location swedencentral --ingress external --target-port 8080`.
