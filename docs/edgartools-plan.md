# Plan: EdgarTools in the SEC data load

Status: **in the SEC engine** (2026-11-22). Agreed decisions move to `functional-requirements.md`.

The import takes a report's revenue line only when every check in `XbrlFinancialsExtractor.UseStatementRevenue` passes,
and never for banks. A statement also confirms a published year that the company facts have since dropped
(`PublishedYears`): that year is kept instead of the company going backwards. And it supplies the newest year
when the company facts lack it and its prior-year column matches ours (`StatementYears`). The sections below record how it got there; where they say the site shows only our own reading,
this replaces them.

## Shadow mode as built (phase 2)

| Piece | What it does |
|---|---|
| `tools/edgar/extract.py` | For every SEC company (CIK taken from the filing its newest figures cite), finds the newest unamended 10-K, 20-F or 40-F and reads its income statement with `Financials.get_revenue()` / `get_net_income()` for each year shown (usually three). Writes `data/cache/edgar/<company id>.json`. A company is read again only when it files a newer annual report or EdgarTools is upgraded. `--tickers GS,AAPL` for a quick look, `--force` to reread all. |
| `tools/edgar/run.ps1` | Creates `tools/edgar/.venv` with Python 3.13 and the pinned `requirements.txt` (rebuilt when that file changes), then runs `extract.py` with `-I`. Identity: `Sec:UserAgent` from the importer's `appsettings.Local.json`. Rate: 5 requests a second. EdgarTools' own download cache: `data/cache/edgartools`. |
| `--validate` | When `data/cache/edgar` has files, compares each year with ours (years ending in January/February are the previous fiscal year, as in the SEC import). Two **warning** checks, never errors: revenue / net income more than 10% apart (both at least $10M). Writes `data/edgar-crosscheck.md` with the full list, agreement counts, years only the report has, and companies it couldn't read. |
| `tools/refresh-data.ps1` | Imports every market, runs `run.ps1` on the company list that import wrote (so new listings are included), imports the SEC market again to use the statements, then validates. A failure in any of these stops the run before anything is committed. `data/edgar-crosscheck.md` is committed with the data. |

Speed: about 2 seconds a company when a report has to be read (the first full run took 140 minutes); later monthly runs
only reread companies that filed an annual report since.

## First full comparison (2026-11-10)

3,979 SEC companies: 3,881 read, 74 with no 10-K/20-F/40-F (mostly funds and trusts), 24 without usable figures, 0 failures.

| Measure | Compared | Within 1% | 1–10% | Over 10% |
|---|---|---|---|---|
| Revenue | 9,450 | 8,897 (94%) | 59 | 494 |
| Net income | 9,981 | 9,798 (98%) | 68 | 115 |

What the differences are:

- **Banks: 154 of the 199 companies with a revenue gap.** Where a bank doesn't tag `RevenuesNetOfInterestExpense`, we
  still use the gross formula (interest income + non-interest income): Bank of America 2024 $196.4bn against the
  reported $105.9bn, Citigroup $170.3bn against $80.7bn. **EdgarTools is wrong for some banks too**: First Citizens,
  Zions, Popular and Valley come out at roughly their non-interest income alone (First Citizens 2023 $1.18bn). So
  neither source can be switched to blindly; the fix for us is net interest income + non-interest income.
- **Non-financial: 45 companies, mostly errors on our side** after spot checks: Tigo Energy is 1,000× too big (2025:
  $103.5bn against $103.5M; the company facts carry the wrong scale), Comfort Systems 2025 $1.83bn against $9.10bn
  (a sub-line), Republic Services, Williams, Essential Utilities, Hasbro and fuboTV on a different revenue concept,
  Brown-Forman including excise duty. Some are restatements, where the latest report shows earlier years as they are
  now measured (Array Digital after selling its wireless business).
- **Years only the report has: 184 companies.** Mostly small companies whose newest report is newer than our figures or
  whose years our consistency rules dropped.

Decision point for phase 3: the cross-check is clearly worth keeping. Switching the source wholesale is not, because
of the bank misreads above. The next step is fixing our own bank formula and the non-financial cases it found.

## What EdgarTools is

[dgunning/edgartools](https://github.com/dgunning/edgartools) is an MIT-licensed **Python** library (`pip install edgartools`,
latest stable 5.62.1 from 2026-10-23; 6.0 is in release candidate and removes some settings). It reads SEC EDGAR filings as
structured data:

- XBRL financial statements with **standardised rows across filers** (income statement, balance sheet, cash flow;
  annual, quarterly and TTM)
- Company facts queries by concept
- Typed forms: 10-K/10-Q sections (risk factors, Item 1C…), 8-K items and earnings, Form 3/4/5 insider transactions,
  13F holdings, Schedule 13D/G, S-1, Form D and others
- Full-text search of filings since 2001
- Built-in SEC rate limiting (9 req/s per process) and an HTTP cache

It identifies itself to the SEC with `EDGAR_IDENTITY` (a name plus contact email), like our importer's `Sec:UserAgent`.
Its documentation doesn't describe parsing executive pay out of proxy statements, so we keep our own reader for that.

## First spike (2026-11-09)

EdgarTools 5.62.1 in a Python 3.13 venv, compared with our database:

| Company | Ours (before) | `Company().income_statement()` (company facts) | `get_financials()` (the 10-K's own statement) |
|---|---|---|---|
| Apple FY2026 | $478.8B | $478.8B | — |
| Goldman Sachs 2025 | $125.1B | **no revenue line** | $58.3B "Total net revenues" |
| JPMorgan 2025 | $182.4B | — | $182.4B |
| Philip Morris 2025 | $40.6B | — | $40.6B |
| Mastercard 2025 | $32.8B | — | $32.8B |

What it taught us:

- **Its value is the 10-K statement layout, not the company facts.** The company-facts route is what we already do,
  and is weaker for banks. Reading the filing's presentation (which line the company itself calls the total) is what
  found Goldman's headline figure.
- **The bank error was ours to fix, and is fixed** (FRD 2026-11-09): we now prefer a bank's tagged net revenues over
  the gross formula. Goldman, Wells Fargo, Fannie Mae, Freddie Mac and 8 others changed. Philip Morris and Mastercard
  already matched.
- So EdgarTools is now a **cross-check and an extra-data source**, not a fix we urgently need. Phase 2 (shadow
  comparison using `get_financials()`) is the useful next step. Phase 3 (switching source) only happens if the
  comparison finds more systematic errors like the bank one.

## Why bother: what it could fix or add

| Today | With EdgarTools |
|---|---|
| Revenue comes from our own list of 11 US-GAAP concepts plus a bank formula (interest income + non-interest income). Goldman 2024 reads $127B against the $53.5B it reports. | Its standardised income statement yields one "total revenue" line per filer, including banks' net revenue. This could settle the open bank, Philip Morris and Mastercard question. |
| Only revenue and net profit per year. | Operating income, assets, cash, debt, employees and quarterly/TTM figures from the same statements. |
| New-hire 8-Ks are read by our own HTML parser. | 8-K item 5.02 text is extracted by the library; we keep our parsing of the pay terms. |
| Insiders are used only to link people to ids. | Form 4 transactions (executives buying or selling shares) could become a new feature. |

## How it fits a C# importer

The importer, API and site stay in C#. The import runs on this PC, never on Railway, so **Python is only needed here
and nothing changes in the deployment.**

```
tools/edgar/            new: Python project (uv, pinned edgartools==5.62.x, Python 3.12)
  extract.py            for each CIK: standardised statements (+ later Form 4, 8-K 5.02) → data/cache/edgar/<cik>.json
refresh-data.ps1        new step before "Import every market": uv run tools/edgar/extract.py
SEC import (C#)         reads data/cache/edgar/*.json when present; falls back to its own XBRL reading per company
--validate              new check: our figure vs the EdgarTools figure, warning above 10% difference
```

Rules:
- **One SEC client at a time.** The SEC limit is 10 requests a second per IP across all processes, so the Python step
  runs before the C# SEC import, never alongside it.
- **Identity comes from the existing setting** (`Sec:UserAgent` in `appsettings.Local.json`), passed in as
  `EDGAR_IDENTITY`, so there's one place to change it.
- **Pinned version.** It is mainly maintained by one person and has frequent releases. We upgrade on purpose, never
  automatically, and stay on 5.x until 6.0 is stable.
- **JSON in between, not pandas in C#.** The C# side reads plain JSON files, so it doesn't depend on Python types, and
  the files are cached like our other downloads.

## Phases

1. **Spike (1 session).** Install it in a scratch venv. Pull standardised statements for about 50 companies chosen for
   the hard cases: banks (GS, JPM, SCHW), excise (PM, MO), networks (MA, V), insurers, REITs, foreign filers (20-F/40-F,
   e.g. SHOP), FedEx and the tiny-profit companies. Compare with our database and write the table into this document.
   **Decision point:** keep going only if it agrees on the ordinary companies and is right on the hard ones.
2. **Shadow mode.** Add `tools/edgar`, the refresh step and the `--validate` comparison. The site still shows our own
   figures, and we read the differences report for a month's refresh.
3. **Switch revenue/profit source.** Where EdgarTools has the company, its standardised figures become the source.
   Our extractor stays as the fallback, and the change is recorded in the FRD and the import report.
4. **New data (optional, each its own decision).** More statement lines on the company page; Form 4 insider trades;
   8-K 5.02 text for the new-hire reader.

## Risks

- Python 3.15 is installed here, but pyarrow (a required dependency) may not ship wheels for it yet. Use uv with a
  pinned 3.12 instead.
- Its standardisation is its own mapping and can be wrong too. Phase 2's comparison is what protects us.
- The first full extraction for about 4,000 companies takes hours at 9 req/s, the same as our current first SEC import.
  After that, filings are cached.
- Install size: pandas, pyarrow and lxml add about 200 MB to this PC, and nothing to the deployed site.
