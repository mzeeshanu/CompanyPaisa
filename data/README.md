# Data

| Path | What | Real or sample? |
|---|---|---|
| `companypaisa.db` | **The live dataset** (SQLite) — every market in one file: SEC filers (US, Canada, Australia, NZ), UK Main Market, France, the Netherlands, Italy, Spain, Pakistan. Each importer run replaces only its own market's rows | **Real**, built by `tools/CompanyPaisa.Importer` (see *Database layout*) |
| `import-report.md` | What the last import included (per metro), excluded (and why), and rows that need review | Generated |
| `import-report-enrichment.md` | The last `--enrich` run: websites, careers pages and street positions per market | Generated |
| `import-report-salaries.md` | The last `--salaries` run: filings read and matched, the companies with most filings, and the biggest unmatched employers | Generated |
| `reference/employer-aliases.csv` | Employers in the H-1B filings that are a listed company under another name ("Google LLC" → GOOGL) — add a line to match more | Hand-curated |
| `reference/company-sites.csv` | Each company's website (and where it came from) and careers page, with the date it was last looked for — edit to correct | Wikidata (CC0), the exchanges, companies' own filings and websites |
| `reference/geocoded-locations.csv` | Locations placed at their street address (with the address they were placed from) | US Census Bureau geocoder; © OpenStreetMap contributors (ODbL) |
| `import-report-pk.md` | The Pakistan run's report: included per area, excluded (and why), rows that need review | Generated |
| `reference/pk-postcodes.csv` | Pakistani postcodes → town and coordinates (the Pakistan tab) | [GeoNames](https://www.geonames.org/) PK postal codes and towns, CC BY 4.0 |
| `import-report-uk.md` | The UK run's report: included per area, excluded (and why), rows that need review | Generated |
| `curated/uk-ftse350.csv` | FTSE 100 + 250 members with the LEI each was matched to — edit an LEI to fix a wrong match | From Wikipedia's constituent tables; reviewable |
| `reference/uk-postcode-districts.csv` | UK postcode districts → place name and coordinates (the postcode search box) | [GeoNames](https://www.geonames.org/) GB postal codes, CC BY 4.0 |
| `reference/us-zip-centroids.csv` | Every US ZIP → city, state, coordinates (used by the ZIP search box) | Real — US Census Gazetteer 2025 ZCTA centroids; names (and PO-box ZIPs) from [GeoNames](https://www.geonames.org/) US postal codes, CC BY 4.0 |
| `curated/utah-offices.csv` | Utah sites of companies headquartered elsewhere (Adobe Lehi, eBay Draper, Goldman Sachs SLC…) | Hand-curated — verify and extend |
| `sample/companypaisa.sample.xlsx` | Synthetic workbook used by the automated tests (with `DataSource:Provider` "Excel") | **Synthetic figures and names** |
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
runs reuse the cache. The running API reloads the database automatically when an import replaces it.

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

## Markets and adding a country

The importer is a set of **markets**, each a class implementing `IMarketImporter` (`tools/CompanyPaisa.Importer/Publishing/Markets.cs`):

| Market | Class | What it covers |
|---|---|---|
| `sec` | `ImportPipeline` | SEC filers with a US, Canadian, Australian or NZ address: XBRL financials, proxy pay (checked against the CEO totals companies tag) |
| `uk` | `Uk/UkImportPipeline` | UK Main Market: ESEF annual reports, directors' pay |
| `eu` | `Eu/EuImportPipeline` | France, Netherlands, Italy, Spain: ESEF annual reports (financials only) |
| `pk` | `Pk/PkImportPipeline` | Pakistan Stock Exchange: companies' own annual report PDFs (revenue, profit, chief executive's pay) |

```powershell
dotnet run --project tools/CompanyPaisa.Importer -- --list-markets
dotnet run --project tools/CompanyPaisa.Importer -- --market uk            # one market (also: --uk, --eu, --pk; no option = sec)
dotnet run --project tools/CompanyPaisa.Importer -- --all --strict         # every market, then the data-quality checks
```

Every market ends by calling `DataPublisher.Publish(market, …)`, which replaces only that market's rows in
`companypaisa.db` and checks the result with the API's own rules first. To add a country:

1. **Another EU country on filings.xbrl.org** (Belgium, Sweden, Denmark…): no code — add an entry to `Importer:Eu:Countries`
   (ISO code, exchange codes for OpenFIGI, ticker suffix, areas) and matching `Ui:Coverage` entries in the API settings.
2. **A new source** (e.g. Germany's register, the ASX): a class implementing `IMarketImporter` with a new market id that
   builds `Company`, `CompanyLocation`, `FinancialPeriod` (and optionally pay) rows and calls `DataPublisher.Publish`;
   register it in `Program.cs` next to the others. Company ids must be unique across markets (use a ticker suffix like
   `.DE`) — the database's primary key refuses duplicates. The shared checks (`DataRules`, `--validate`) apply automatically.

## Monthly refresh (scheduled)

`tools/refresh-data.ps1` runs every market (`--all --refresh-lists --strict`) and, only if they all finish and
verify the database, commits and pushes the data files so Railway redeploys. It refuses to run while there are
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

## The European dataset (financials only)

```bash
dotnet run --project tools/CompanyPaisa.Importer -- --eu
```

France, the Netherlands, Italy and Spain (`Importer:Eu:Countries`), published as market `eu` in `companypaisa.db`. For each country:
ESEF filers on filings.xbrl.org → shares on the home exchange (GLEIF ISINs → OpenFIGI; if a bank's share ISIN is lost
among its bond ISINs, an OpenFIGI name search, exact name match only) → GLEIF headquarters in that country → GeoNames
postcode (`reference/eu-postcodes.csv`) → IFRS revenue and profit from each report. No executives yet, sector "Other".
Germany isn't on filings.xbrl.org. To add another country on it, add a `Countries` entry (exchange codes, ticker suffix,
areas) and matching `Ui:Coverage` entries. OpenFIGI name searches are slow without an API key (about 5 a minute), but
answers are cached in `data/cache/eu/openfigi-*.json`.

## The Pakistan dataset

```bash
dotnet run --project tools/CompanyPaisa.Importer -- --pk
```

Companies listed on the Pakistan Stock Exchange, published as market `pk` (tickers `LUCK.KA`, currency PKR). Sources are
free and public only: the exchange's company list and profile pages (dps.psx.com.pk) and the annual report each company
files with the exchange. The portal's own financial tables are licensed third-party data and are **not** used.

1. **Companies** — `/symbols`, equities only (no ETFs, debt, funds, modarabas, rights or class-B shares).
2. **Profile** — `/company/{symbol}`: description, CEO, registered address, website, fiscal year end.
3. **Annual reports** — an announcement search per company; the two latest annual reports are downloaded, turned into
   text lines with positions (`Pk/PdfLines`, PdfPig) and only those lines are cached (`cache/pk/reports/{id}.lines.gz`).
   Scanned reports give no text and are left out.
4. **Figures** (`Pk/AnnualReportReader`) — the statement of profit or loss (the group's when there is one): sales line
   ("Total income" for banks) and profit after tax for the report's year and the one before, in rupees. The chief
   executive's pay from the remuneration note, kept only when its items add up to the stated total.
5. **Place** — the town named in the address (GeoNames towns of 5,000+ people), or the head office printed in the report
   when that is in another town. `reference/pk-postcodes.csv` gives each postcode its town's position.

Check one report: `-- --debug-pk-report <pdf url | .pdf | cache .lines.gz>`; see its text: `-- --debug-pk-pdf <pdf>`.
The run's report is `import-report-pk.md`. A first run downloads ~1,300 PDFs (5–25 MB each; the exchange serves each
at ~400 KB/s) and takes a few hours; later runs only fetch new reports.

## Websites, careers pages and street positions

```bash
dotnet run --project tools/CompanyPaisa.Importer -- --enrich                 # all three steps
dotnet run --project tools/CompanyPaisa.Importer -- --enrich careers         # one step: websites, careers or geocode
```

For every company in the database, whatever its market: its **website** (Wikidata by SEC CIK, LEI or ticker; else the
domain a US filer names in its own proxy or annual report, only when it looks like the company's name), its **careers
page** (the link on its home page, honouring robots.txt; rechecked every 90 days) and the exact position of its **street
address** (US Census Bureau geocoder in the US, OpenStreetMap elsewhere at one request a second; kept only within 25
miles of the postcode). Results go to `reference/company-sites.csv` and `reference/geocoded-locations.csv`, which every
market import applies when it publishes, so a monthly refresh keeps them. Addresses no geocoder could place are listed in
`cache/enrich/geocode-misses.txt` and not retried.

## Salaries by job title and the CEO pay ratio

```bash
dotnet run --project tools/CompanyPaisa.Importer -- --salaries
```

Reads the US Department of Labor's H-1B labor condition application disclosure files (the latest two federal fiscal
years; each quarterly .xlsx is 80–250 MB, downloaded once into `cache/lca`). Every certified, full-time filing names the
employer and its tax id (EIN), the job title, the work place and the yearly salary committed. Filings are matched to the
US-listed companies in the database by EIN (from each company's SEC record), then by name (exact, or a subsidiary whose
name starts with the company's), then by `reference/employer-aliases.csv`. Job titles are grouped (abbreviations spelled
out, requisition codes dropped, levels kept apart) and summarised company-wide and per work place: 25th percentile, median,
75th percentile, lowest and highest. A title or place needs at least 3 filings. The results replace the `job_salaries`
table; the report lists the employers with the most unmatched filings, for new aliases.

The **CEO pay ratio** comes with the `sec` market import: each proxy statement it reads is searched for the disclosure
(median employee pay, CEO pay, "N to 1"), kept only when the amounts divide to the stated ratio and the CEO figure is
near a total in the proxy's own pay table.

## Database layout

`companypaisa.db` is SQLite (open it with any SQLite browser). Tables: `companies`, `locations`, `financials`,
`executive_compensation`, `people` — the same columns as the workbook below — plus `filings` (each source URL once; rows
point at it by `filing_id`, with `https://www.sec.gov/Archives/edgar/data/` stored as `~sec/`) and `meta` (per market:
`data_version`, `as_of_date`, `source`, `region`). Every row has a `market` column: `sec`, `uk`, `eu` or `pk`, the importer run
that wrote it. Publishing a market works on a copy, deletes and re-inserts that market's rows, reads the copy back with the
API's own rules, and only then replaces the file — a failed import leaves the old file untouched. `PRAGMA user_version` is
the layout version (3 since `worker_pay`, `job_salaries` and `extras`; 2 added `companies.careers_url`; an importer
upgrades an older file in place); the API refuses a
file with another one.

`worker_pay` holds each company's disclosed pay ratio by year (`median_pay`, `ceo_pay`, `ratio`, the proxy as `filing_id`;
market-owned). `job_salaries` holds salaries by job title (`title`, `occupation`, `city`/`state`/`latitude`/`longitude` for
work-place rows, empty for the company-wide row; `filings`, `low`, `median`, `high`, `min`, `max`); it isn't market-owned
— `--salaries` replaces it whole, and a company that leaves its market takes its rows with it. `extras` holds the salary
source and date range (`job_salaries.from`, `.to`, `.source`).

`new_executives` holds officer appointments read from the last 18 months of each SEC company's 8-K filings (Item 5.02): who
(`person_id` when they could be linked to someone with reported pay), `title`, `announced_on`, `starts_on`, the filing, and
the announced package as JSON (`[{"kind":"Salary","amount":850000,"label":"Base salary"}, …]`). It's written with the `sec`
market, or on its own — without re-running the whole import — with `-- --new-hires`. Try the reader on single filings with
`-- --debug-new-hires <8-K url>` or on a sample of companies with `-- --scan-new-hires 150` (or a list of tickers).

Workbooks from before the move to SQLite can be copied in once with `-- --migrate-xlsx`.

## Sample workbook layout

The synthetic test data (and any hand-made data set, with `DataSource:Provider` "Excel") is a workbook. Columns are matched by header name (any order, case-insensitive); extra columns are ignored.

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
