# Data

| Path | What | Real or sample? |
|---|---|---|
| `companypaisa.xlsx` | **The live dataset** — public companies in the top 20 US metros plus the Wasatch Front, with ~10 years of financials and executive pay | **Real**, built from SEC EDGAR by `tools/CompanyPaisa.Importer` |
| `import-report.md` | What the last import included (per metro), excluded (and why), and rows that need review | Generated |
| `companypaisa-uk.xlsx` | **UK dataset** — FTSE 350 companies (minus investment trusts): ~6 years of figures and executive directors' single total figure pay. The API merges it with the US workbook | **Real**, built by `--uk` (see below) |
| `import-report-uk.md` | The UK run's report: included per area, excluded (and why), rows that need review | Generated |
| `curated/uk-ftse350.csv` | FTSE 100 + 250 members with the LEI each was matched to — edit an LEI to fix a wrong match | From Wikipedia's constituent tables; reviewable |
| `reference/uk-postcode-districts.csv` | UK postcode districts → place name and coordinates (the postcode search box) | [GeoNames](https://www.geonames.org/) GB postal codes, CC BY 4.0 |
| `reference/us-zip-centroids.csv` | Every US ZIP → city, state, coordinates (used by the ZIP search box) | Real — US Census Gazetteer 2025 ZCTA centroids; names (and PO-box ZIPs) from [GeoNames](https://www.geonames.org/) US postal codes, CC BY 4.0 |
| `curated/utah-offices.csv` | Utah sites of companies headquartered elsewhere (Adobe Lehi, eBay Draper, Goldman Sachs SLC…) | Hand-curated — verify and extend |
| `sample/companypaisa.sample.xlsx` | Synthetic workbook used by the automated tests | **Synthetic figures and names** |
| `reference/us-zip-centroids.sample.csv` | Small hand-made ZIP table used by the tests | Approximate |
| `cache/` (git-ignored) | Downloaded SEC/Census/GeoNames responses (gzip), so re-runs are fast | Re-creatable |

## Refreshing the real data

```bash
dotnet run --project tools/CompanyPaisa.Importer
```

Settings are in `tools/CompanyPaisa.Importer/appsettings.json`: discovery (`AllListed` screens every company in the SEC's listed-ticker file that reported in the last `FiledWithinMonths`), the metros (`Regions` — each a name,
an example ZIP and anchor circles), exchanges, minimum revenue for OTC companies, years of history, and the SEC contact
header (put your email in `appsettings.Local.json`, which git ignores). The first nationwide run takes a few hours
(thousands of filings, throttled under the SEC's 10 requests/second limit; the gzip cache grows to several GB); later
runs reuse the cache. The running API reloads the workbook automatically when the file changes.

To add a metro: add a `Regions` entry in the importer settings (`Country` "US" or "CA"; a `WholeCountry` region takes everything no metro claims), and a matching
`Ui:Coverage` entry in `src/CompanyPaisa.Api/appsettings.json` so the website lists it.

**How it's built**

1. **Companies** — every company in the SEC's listed-ticker file that filed a 10-K, 10-Q, 40-F or 20-F recently, with a
   business address in the US or Canada (Canadian postal areas from GeoNames), trading on Nasdaq/NYSE/CBOE (or OTC with
   ≥ $5M revenue). Plus `curated/utah-offices.csv`. Companies based elsewhere are counted, not included.
2. **Profile** — SEC submissions record: name, ticker, exchange, SIC industry (mapped to a sector), fiscal year end, address.
3. **Location** — ZIP-code centroid (Census; GeoNames for PO-box ZIPs). Precision is ZIP-level, so companies in the
   same ZIP share a point.
4. **Financials** — SEC XBRL "company facts" (US GAAP, or IFRS for Canadian 40-F filers, in the reported currency):
   revenue and net income per SEC calendar frame (annual + quarterly);
   a missing fiscal Q4 is derived as annual minus the other three quarters. Every period links to its filing.
5. **Executive pay** — the Summary Compensation Table in each DEF 14A proxy statement (newest first, older ones only for
   missing years). Salary, bonus, stock + option awards, other (non-equity incentive, pension, all other) and total.
   The **total** is always the filed figure; if the parsed pieces don't add up, the difference is shown as "other" and
   the row is listed under *Needs review* in `import-report.md`, with a link to the filing.
6. **People** — each proxy name is matched to the company's SEC insider list (everyone who filed Forms 3/4/5, with
   their own CIK), so `person_id` = `steven-fife-<CIK>` is the same person at every company, and two different
   "John Smith"s never merge. Names that can't be matched get a company-scoped id (`john-smith-<ticker>`).

**Known gaps** (see `import-report.md`): foreign private issuers (e.g. NICE) don't file DEF 14A, so no executive pay;
a few small companies use table layouts the parser doesn't recognise yet.

## Monthly refresh (scheduled)

`tools/refresh-data.ps1` runs both importers (US + Canada, then UK with a fresh FTSE list) and, only if both finish and
verify their workbooks, commits and pushes the data files so Railway redeploys. It refuses to run while there are
uncommitted changes outside `data/`, and logs to `%LOCALAPPDATA%\CompanyPaisa\logs`.

```powershell
powershell -ExecutionPolicy Bypass -File tools\register-refresh-task.ps1          # schedule: 2nd of each month, 2 am
powershell -ExecutionPolicy Bypass -File tools\refresh-data.ps1 -NoPush          # run now, commit but don't push
Start-ScheduledTask 'CompanyPaisa data refresh'                                   # run the scheduled job now
powershell -ExecutionPolicy Bypass -File tools\register-refresh-task.ps1 -Remove  # stop scheduling
```

The task runs only while you're signed in (no stored password); if the PC was off, it runs when it's next on.

## The UK dataset

```bash
dotnet run --project tools/CompanyPaisa.Importer -- --uk                     # uses data/curated/uk-ftse350.csv
dotnet run --project tools/CompanyPaisa.Importer -- --uk --refresh-uk-list   # re-reads the FTSE 100/250 lists first
```

With `Uk:AllMainMarket` (on), every other UK filer on filings.xbrl.org with London-listed ordinary shares or REIT units is
added too: GLEIF lists each filer's ISINs and OpenFIGI maps them to London tickers (cached in
`data/cache/uk/openfigi-isins.json`). Those companies are listed for review in `curated/uk-main-market.csv`; they have no
sector yet. AIM companies don't file ESEF reports, so they aren't covered.

Settings: the `Importer:Uk` section (areas, how many reports to read for pay, rate limit). The UK sources are sent a
generic `CompanyPaisa` User-Agent — no personal contact details.

1. **Companies** — FTSE 100 + FTSE 250 from Wikipedia's constituent tables, minus investment trusts and funds. Each is
   matched by name to a filer on [filings.xbrl.org](https://filings.xbrl.org) (UK listed companies file their annual
   report in the tagged ESEF format); a wrong or missing match is fixed by setting the `lei` column in `curated/uk-ftse350.csv`.
2. **Location** — the headquarters address in the global LEI registry ([GLEIF](https://www.gleif.org)); companies
   headquartered outside the UK are left out. Placed at the centre of the postcode district (GeoNames), so precision
   is a district — roughly a few streets in a city, a few miles in the country.
3. **Financials** — IFRS revenue, profit attributable to shareholders, operating profit and EPS from each report's
   xBRL-JSON: the year reported plus its comparative, newest report winning. Tagged reports began in 2021, so ~6 years.
   Banks and insurers rarely tag plain "Revenue"; the report lists which concept was used.
4. **Directors' pay** — the "single total figure of remuneration" table in the directors' remuneration report of the
   latest 3 annual reports (each shows two years). The table is read from the report itself (a real HTML table or a
   PDF converted to positioned text). The **total** is always the stated figure; salary, bonus, long-term incentives
   and other are split where the table allows, with any gap shown as other. Annual reports are 5–40 MB, so only what was
   parsed is cached (`cache/uk/pay`), not the report.
5. **People** — named per company (`jane-smith-tsco-l`); UK directors aren't yet linked across companies.

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
