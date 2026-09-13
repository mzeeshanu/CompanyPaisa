# Data

| Path | What | Real or sample? |
|---|---|---|
| `companypaisa.xlsx` | **The live dataset** — public companies on the Wasatch Front with ~10 years of financials and executive pay | **Real**, built from SEC EDGAR by `tools/CompanyPaisa.Importer` |
| `import-report.md` | What the last import included, excluded (and why), and rows that need review | Generated |
| `reference/ut-zip-centroids.csv` | Utah ZIP → coordinates (used by the ZIP search box) | Real — US Census Gazetteer 2025 ZCTA centroids; city names from SEC addresses where known |
| `curated/utah-offices.csv` | Utah sites of companies headquartered elsewhere (Adobe Lehi, eBay Draper, Goldman Sachs SLC…) | Hand-curated — verify and extend |
| `sample/companypaisa.sample.xlsx` | Synthetic workbook used by the automated tests | **Synthetic figures and names** |
| `reference/us-zip-centroids.sample.csv` | Small hand-made ZIP table used by the tests | Approximate |
| `cache/` (git-ignored) | Downloaded SEC/Census responses, so re-runs are fast | Re-creatable |

## Refreshing the real data

```bash
dotnet run --project tools/CompanyPaisa.Importer
```

Settings are in `tools/CompanyPaisa.Importer/appsettings.json`: region anchors (Ogden, SLC, Lehi, Provo), exchanges,
minimum revenue for OTC companies, years of history, and the SEC contact header. The first run downloads a few hundred
filings (a few minutes, throttled under the SEC's 10 requests/second limit); later runs reuse the cache. The running API
reloads the workbook automatically when the file changes.

**How it's built**

1. **Companies** — EDGAR full-text search for 10-K filers whose business address is in Utah, kept if the address is
   inside the region and the company trades on Nasdaq/NYSE/CBOE (or OTC with ≥ $5M revenue). Plus `curated/utah-offices.csv`.
2. **Profile** — SEC submissions record: name, ticker, exchange, SIC industry (mapped to a sector), fiscal year end, address.
3. **Location** — ZIP-code centroid (Census). PO-box ZIPs fall back to another ZIP in the same city. Precision is
   ZIP-level, so companies in the same ZIP share a point.
4. **Financials** — SEC XBRL "company facts": revenue and net income per SEC calendar frame (annual + quarterly);
   a missing fiscal Q4 is derived as annual minus the other three quarters. Every period links to its filing.
5. **Executive pay** — the Summary Compensation Table in each DEF 14A proxy statement (newest first, older ones only for
   missing years). Salary, bonus, stock + option awards, other (non-equity incentive, pension, all other) and total.
   The **total** is always the filed figure; if the parsed pieces don't add up, the difference is shown as "other" and
   the row is listed under *Needs review* in `import-report.md`, with a link to the filing.
6. **People** — linked across companies by normalised name ("Steven R. Fife" → `steven-fife`). A later pass can switch
   to SEC person CIKs (from Forms 3/4).

**Known gaps** (see `import-report.md`): foreign private issuers (e.g. NICE) don't file DEF 14A, so no executive pay;
a few small companies use table layouts the parser doesn't recognise yet.

## Workbook layout

Columns are matched by header name (any order, case-insensitive); extra columns are ignored.

| Sheet | Required columns | Optional columns |
|---|---|---|
| `Companies` | company_id, name, ticker, exchange, sector | industry, website, employees, market_cap, description, currency, fiscal_year_end, logo_url, as_of_date |
| `Locations` | location_id, company_id, type (Headquarters / Campus / Office / Plant), label, city, state, latitude, longitude | street, postal_code |
| `Financials` | company_id, period_type (Quarterly / Annual), fiscal_year, revenue, net_income; fiscal_quarter (1-4) for quarterly rows | operating_income, eps, source_filing |
| `ExecutiveCompensation` | company_id, **person_id**, exec_name, title, year | salary, bonus, stock_awards, other, total, source_filing |
| `People` *(optional)* | person_id, name | sec_cik |
| `_meta` | key, value — `data_version`, `as_of_date`, `is_sample`, `source`, `region` | |

**person_id is the same for a person at every company** — that's how careers across companies are linked. If a person
moves mid-year, give them a row at each company for that year. Older workbooks with a per-company `exec_id` still load.

Money is in whole dollars. On load the API checks for duplicate IDs, orphaned company_ids, bad quarters, invalid
coordinates and duplicate person/company/year pay rows, and reports every problem with its sheet and row. If a reload
fails, the API keeps serving the last good data.
