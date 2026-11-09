# Plan: EdgarTools in the SEC data load

Status: **proposed** (2026-11-09). Nothing here is built yet; agreed decisions move to `functional-requirements.md`.

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
