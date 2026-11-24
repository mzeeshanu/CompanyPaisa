# Data validation report — 2026-11-24 14:27 UTC

Checks everything in `companypaisa.db` together, as the website sees it — markets: pk (pk-2026.09.19), anz (anz-2026.11.22), sec (sec-2026.11.24), uk (uk-2026.11.24), eu (eu-2026.11.24).

**Errors** are values that can't be right. **Warnings** are unusual values worth a look — many are real (big acquisitions, holding-company gains, mega stock grants). Nothing here changes the data.

Findings a person has already reviewed (`data/curated/audit-decisions.csv`) are not counted again; they are listed with their decision at the end. Every open finding is in `data/audit-worklist.csv`.

- Errors: **0**
- Warnings (open): **790**
- Reviewed: **318**

## Checks

| Area | Check | Level | Open | Reviewed |
|---|---|---|---|---|
| Companies | Currency without an exchange rate | Error | ✓ 0 |  |
| Companies | No location at all | Error | ✓ 0 |  |
| Companies | Only a local office, no headquarters | Warning | ✓ 0 | 1 |
| Companies | Same name under two ids | Warning | ✓ 0 | 5 |
| Companies | No industry classification | Warning | 101 |  |
| Companies | Classification not checked recently | Warning | ✓ 0 |  |
| Locations | Outside its country | Error | ✓ 0 |  |
| Locations | Missing city | Warning | ✓ 0 |  |
| Financials | No figures at all | Error | ✓ 0 |  |
| Financials | Negative revenue | Warning | ✓ 0 | 152 |
| Financials | Period in the future | Error | ✓ 0 |  |
| Financials | Duplicate period | Error | ✓ 0 |  |
| Financials | Implausibly large revenue | Warning | ✓ 0 |  |
| Financials | Impossible size | Error | ✓ 0 |  |
| Financials | Another company's figures | Error | ✓ 0 |  |
| Financials | Profit far bigger than revenue | Warning | ✓ 0 | 52 |
| Financials | Revenue jumps 10× in a year | Warning | ✓ 0 | 46 |
| Financials | Quarters don't add up to the year | Warning | ✓ 0 | 62 |
| Financials | Out of date | Warning | 39 |  |
| Financials | Annual figures missing, quarters only | Warning | 121 |  |
| Pay | Negative amount | Error | ✓ 0 |  |
| Pay | Year in the future | Error | ✓ 0 |  |
| Pay | Person record missing | Error | ✓ 0 |  |
| Pay | Same person, company and year twice | Error | ✓ 0 |  |
| Pay | Salary bigger than total | Warning | ✓ 0 |  |
| Pay | Impossibly large total | Error | ✓ 0 |  |
| Pay | Record award checked by hand | Warning | 1 |  |
| Pay | Very large total | Warning | 65 |  |
| Pay | Tiny total | Warning | ✓ 0 |  |
| Pay | Name doesn't look like a person | Warning | 3 |  |
| Pay | Pieces don't add up to the total | Warning | ✓ 0 |  |
| EdgarTools | Revenue differs from the annual report | Warning | 396 |  |
| EdgarTools | Net income differs from the annual report | Warning | 64 |  |

## Overview

### Companies by country

| | Count |
|---|---|
| US | 3,832 |
| GB | 336 |
| SE | 298 |
| PK | 228 |
| FR | 194 |
| NO | 151 |
| IT | 145 |
| CA | 139 |
| ES | 106 |
| AU | 92 |
| DK | 84 |
| FI | 83 |
| NL | 73 |
| BE | 68 |
| AT | 50 |
| LT | 17 |
| NZ | 13 |
| SI | 9 |
| LU | 2 |
| ? | 1 |

### Companies by exchange

| | Count |
|---|---|
| Nasdaq | 2,172 |
| NYSE | 1,437 |
| LSE | 336 |
| Nasdaq Stockholm | 298 |
| Pakistan Stock Exchange | 228 |
| OTC | 208 |
| Euronext Paris | 194 |
| NYSE American | 159 |
| Oslo Børs | 151 |
| Borsa Italiana | 145 |
| Bolsa de Madrid | 106 |
| Nasdaq Copenhagen | 84 |
| Nasdaq Helsinki | 83 |
| ASX | 76 |
| Euronext Amsterdam | 73 |
| Euronext Brussels | 68 |
| Wiener Börse | 50 |
| Nasdaq Vilnius | 17 |
| NZX | 11 |
| Ljubljana Stock Exchange | 9 |
| TXSE | 9 |
| Unlisted | 3 |
| Luxembourg Stock Exchange | 2 |
| Cboe | 1 |
| NYSE Arca | 1 |

### Companies by reporting currency

| | Count |
|---|---|
| USD | 4,038 |
| EUR | 776 |
| SEK | 283 |
| GBP | 273 |
| PKR | 228 |
| NOK | 107 |
| AUD | 76 |
| DKK | 74 |
| CAD | 50 |
| NZD | 11 |
| GEL | 2 |
| CNY | 1 |
| HKD | 1 |
| MXN | 1 |

### Companies by sector

| | Count |
|---|---|
| Consumer & retail | 910 |
| Finance | 888 |
| Healthcare | 824 |
| Industrials | 821 |
| Software & IT | 461 |
| Energy & utilities | 391 |
| Real estate | 367 |
| Materials | 322 |
| Business services | 285 |
| Media & telecom | 171 |
| Technology hardware | 157 |
| Transportation | 117 |
| Other | 105 |
| Semiconductors | 83 |
| Education | 19 |

### Companies with executive pay, by country

| | Count |
|---|---|
| US | 3,416 |
| PK | 123 |
| GB | 96 |
| CA | 28 |
| AU | 3 |

### Years of annual history per company

| | Count |
|---|---|
| 10+ | 2,470 |
| 6–9 | 1,737 |
| 3–5 | 1,264 |
| 1–2 | 329 |
| 0 (quarters only) | 121 |

### Newest annual figures

| | Count |
|---|---|
| FY 2025 | 4,795 |
| FY 2026 | 703 |
| FY 2024 | 263 |
| none | 121 |
| 2023 or older | 39 |

### Rows

| | Count |
|---|---|
| Companies | 5,921 |
| Locations | 5,931 |
| Annual periods | 42,033 |
| Quarterly periods | 137,185 |
| Pay rows | 88,799 |
| People | 23,230 |

## Details

### Companies · No industry classification (101)

Neither its exchange nor the SEC gave a category (--enrich categories), so it is filed under a guessed or blank sector.

- GUD.AX (G.U.D. HOLDINGS LIMITED): ASX
- NSX.AX (National Stock Exchange Of Australia Limited): ASX
- PNW.AX (Pacific Star Network): ASX
- QUB.AX (Qube Holdings): ASX
- SVW.AX (Seven Group Holdings): ASX
- AIR.NZ (Air New Zealand): NZX
- BLT.NZ (Blis Technologies): NZX
- FPH.NZ (Fisher & Paykel Healthcare): NZX
- KPG.NZ (Kiwi Property): NZX
- MFT.NZ (Mainfreight): NZX
- NZK.NZ (NZ King Salmon Investments): NZX
- PEB.NZ (Pacific Edge Limited): NZX
- … and 89 more (all in the worklist)

### Financials · Out of date (39)

Newest annual figures are more than two years old — the company may have stopped reporting, been taken over, or changed filer.

- AKTX (Akari Therapeutics Plc): latest FY 2023
- AQB (Aquabounty Technologies Inc): latest FY 2023
- ASUR (Asure Software Inc): latest FY 2023
- BESS (Bimergen Energy Corp): latest FY 2023
- BXMT (Blackstone Mortgage Trust, Inc.): latest FY 2023
- CTXR (Citius Pharmaceuticals, Inc.): latest FY 2022
- COCP (Cocrystal Pharma, Inc.): latest FY 2020
- CYPH (Cypherpunk Technologies Inc.): latest FY 2021
- DCOY (Decoy Therapeutics Inc.): latest FY 2022
- ESOA (Energy Services of America CORP): latest FY 2022
- VRMK (Erp Operating Ltd Partnership): latest FY 2009
- FFR (FF EAI Robotics Ecosystem Inc.): latest FY 2022
- … and 27 more (all in the worklist)

### Financials · Annual figures missing, quarters only (121)

Shown with quarterly figures only; the headline 'annual revenue' comes from the last four quarters.

- ADRX (ADARx Pharmaceuticals, Inc.)
- ADIG (Adi Global Distribution Inc.)
- AVEX (AEVEX Corp.)
- AIAI (AIAI Holdings Corp)
- AIB (AIB Data Centers Inc.)
- AKTS (Aktis Oncology, Inc.)
- ALMR (Alamar Biosciences, Inc.)
- AMRO (Amaero Inc.)
- AMSS (Amass Brands)
- APUS (Apimeds Pharmaceuticals US, Inc.)
- APMD (Apnimed, Inc.)
- AADX (Applied Aerospace & Defense, Inc.)
- … and 109 more (all in the worklist)

### Pay · Record award checked by hand (1)

Above $3bn but checked against the proxy statement: Tesla's 2026 proxy reports Elon Musk's 2025 stock awards (the interim award and the 2025 CEO performance award) at $158,359,009,867.

- TSLA (Tesla, Inc.) Elon Musk 2025: 158.36bn

### Pay · Very large total (65)

Total above $150M (US-dollar equivalent) in one year — happens (mega stock grants), but check it's not a unit error.

- TSLA (Tesla, Inc.) Elon Musk 2025: 158.36bn
- PLTR (Palantir Technologies Inc.) Alexander Karp 2020: 1.10bn
- FIG (Figma, Inc.) Dylan Field 2025: 864.4M
- WELL (Welltower Inc.) Shankh Mitra 2025: 821.1M
- HOOD (Robinhood Markets, Inc.) Vladimir Tenev 2021: 796.1M
- OPEN (Opendoor Technologies Inc.) Kaz Nejatian 2025: 741.1M
- OWL (Blue Owl Capital Inc.) Michael D. Rees 2021: 700.7M
- ORCL (Oracle Corp) Clayton M. Magouyrk 2026: 627.5M
- HOOD (Robinhood Markets, Inc.) Baiju Bhatt 2021: 594.0M
- LCID (Lucid Group, Inc.) Peter Rawlinson 2021: 565.6M
- AFRM (Affirm Holdings, Inc.) Max Levchin 2021: 451.2M
- APO (Apollo Global Management, Inc.) Scott Kleinman 2021: 437.0M
- … and 53 more (all in the worklist)

### Pay · Name doesn't look like a person (3)

Digits, company words or table labels where a person's name should be.

- JDMT.KA (Janana De Malucho Textile Mills Limited): "Lt. Gen. (Retd.) Ali Kuli Khan Khattak"
- JACK (Jack In The Box Inc): "Martin"
- JACK (Jack In The Box Inc): "Tuckerr"

### EdgarTools · Revenue differs from the annual report (396)

Our revenue is more than 10% away from the headline revenue line of the company's own 10-K/20-F/40-F income statement, as EdgarTools reads it. Either our concept choice or its mapping is wrong; data/edgar-crosscheck.md lists them all.

- FCNCA (First Citizens Bancshares Inc /De/) FY 2023: ours 18.79bn, 10-K 1.18bn
- SLF (Sun Life Financial Inc) FY 2025: ours 41.90bn, 40-F 23.98bn
- SLF (Sun Life Financial Inc) FY 2024: ours 38.80bn, 40-F 22.64bn
- FCNCA (First Citizens Bancshares Inc /De/) FY 2024: ours 9.76bn, 10-K 1.37bn
- FCNCA (First Citizens Bancshares Inc /De/) FY 2025: ours 9.54bn, 10-K 1.43bn
- NTRS (Northern Trust Corp) FY 2024: ours 8.29bn, 10-K 4.73bn
- NTRS (Northern Trust Corp) FY 2025: ours 8.09bn, 10-K 5.02bn
- ZION (Zions Bancorporation, National Association /UT/) FY 2025: ours 3.39bn, 10-K 662.0M
- BPOP (Popular, Inc.) FY 2025: ours 3.20bn, 10-K 558.8M
- FUBO (FuboTV Inc.) FY 2024: ours 1.62bn, 10-K 4.22bn
- ZION (Zions Bancorporation, National Association /UT/) FY 2023: ours 3.12bn, 10-K 616.0M
- ZION (Zions Bancorporation, National Association /UT/) FY 2024: ours 3.13bn, 10-K 639.0M
- … and 384 more (all in the worklist)

### EdgarTools · Net income differs from the annual report (64)

Our net income is more than 10% away from the report's net income line. Often attributable-to-parent against consolidated (including minority interests); data/edgar-crosscheck.md lists them all.

- SRE (Sempra) FY 2024: ours 2.86bn, 10-K 3.50bn
- GEF (Greif, Inc) FY 2024: ours 268.8M, 10-K 840.0M
- SRE (Sempra) FY 2023: ours 3.08bn, 10-K 3.62bn
- ETR (Entergy Corp /De/) FY 2023: ours 2.36bn, 10-K 1.86bn
- OPTU (Optimum Communications, Inc.) FY 2025: ours -1.44bn, 10-K -1.87bn
- ETR (Entergy Corp /De/) FY 2024: ours 1.06bn, 10-K 1.47bn
- DDC (DDC Enterprise Ltd) FY 2025: ours -48.3M, 20-F -338.0M
- SRE (Sempra) FY 2025: ours 1.84bn, 10-K 2.07bn
- MLGO (MicroAlgo Inc.) FY 2023: ours -37.6M, 20-F -266.2M
- IVZ (Invesco Ltd.) FY 2024: ours 538.0M, 10-K 752.4M
- CMP (Compass Minerals International Inc) FY 2024: ours -206.1M, 10-K 0
- IVZ (Invesco Ltd.) FY 2023: ours -333.7M, 10-K -168.2M
- … and 52 more (all in the worklist)

## Reviewed

What a person decided about each finding, and why.

### Companies · Only a local office, no headquarters (1)

- NICE (NICE Ltd.) — **real**: Israeli company (Ra'anana); only its former inContact office in Utah is on the site, by design. (2026-11-24)

### Companies · Same name under two ids (5)

- AGX, ARG.PA — Argan Inc — **real**: Two companies: Argan Inc (US electrical contractor, NYSE AGX) and Argan SA (French logistics property, Euronext ARG). (2026-11-24)
- DPZ, DOM.L — Dominos Pizza Inc — **real**: Two companies: Domino's Pizza Inc (US, Nasdaq DPZ) and Domino's Pizza Group plc (UK and Ireland master franchisee, LSE DOM). (2026-11-24)
- FMCB, FMAO — Farmers & Merchants Bancorp — **real**: Two banks: Farmers & Merchants Bank of Long Beach (OTC FMCB) and Farmers & Merchants Bancorp Inc of Archbold, Ohio (Nasdaq FMAO). (2026-11-24)
- IPAR, ITP.PA — Interparfums Inc — **real**: Interparfums SA is the Paris-listed subsidiary of Interparfums Inc (Nasdaq IPAR); both are listed and report their own accounts. (2026-11-24)
- IREN, IRE.MI — IREN Ltd — **real**: Two companies: IREN Ltd (Australian data centres, Nasdaq IREN) and Iren SpA (Italian utility, Borsa Italiana IRE). (2026-11-24)

### Financials · Negative revenue (152)

- AERA (AI Era Corp.) Q2 2021: -51,200 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- ALT (Altimmune, Inc.) FY 2022: -68,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- ANG-PD (American National Group Inc.) Q4 2018: -450,985,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- ANG-PD (American National Group Inc.) Q1 2020: -323,703,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- AMLX (Amylyx Pharmaceuticals, Inc.) Q2 2024: -1,023,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- ABUS (Arbutus Biopharma Corp) Q4 2016: -195,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- ATH-PA (Athene Holding Ltd.) Q1 2020: -1,549,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- ATH-PA (Athene Holding Ltd.) Q1 2022: -281,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- BANC (Banc Of California, Inc.) Q4 2023: -249,351,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- BMRC (Bank of Marin Bancorp) Q4 2025: -36,867,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- BENF (Beneficient) FY 2023: -54,010,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) FY 2025: -6,500,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) FY 2026: -39,125,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) Q1 2022: -10,811,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) Q2 2022: -25,117,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) Q3 2022: -17,099,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) Q3 2023: -13,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) Q2 2025: -12,623,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) Q3 2025: -2,763,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BENF (Beneficient) Q3 2026: -13,834,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BGDE (Big Digital Energy, Inc.) FY 2018: -4,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- BX (Blackstone Inc.) Q1 2020: -3,075,964,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- BHF (Brighthouse Financial, Inc.) Q2 2016: -584,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- BHF (Brighthouse Financial, Inc.) Q4 2016: -553,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- BHF (Brighthouse Financial, Inc.) Q2 2020: -922,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- BHF (Brighthouse Financial, Inc.) Q4 2022: -127,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- CFFN (Capitol Federal Financial, Inc.) FY 2023: -18,183,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- CAPS (Capstone Holding Corp.) FY 2018: -2,000,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- CG (Carlyle Group Inc.) Q1 2020: -745,700,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- CLST (Catalyst Bancorp, Inc.) Q1 2024: -3,070,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) FY 2019: -47,275,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) FY 2020: -56,471,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) FY 2023: -21,202,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q4 2018: -39,336,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q1 2019: -23,331,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q2 2019: -28,458,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q3 2019: -5,358,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q1 2020: -55,946,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q2 2020: -12,352,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q2 2021: -9,611,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q3 2021: -110,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q2 2022: -10,928,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q4 2022: -31,141,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q1 2023: -16,738,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q3 2024: -7,077,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CHMI (Cherry Hill Mortgage Investment Corp) Q1 2025: -3,050,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- CINF (Cincinnati Financial Corp) Q1 2020: -99,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- CNX (CNX Resources Corp) Q2 2021: -127,210,000 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- CNX (CNX Resources Corp) Q3 2021: -880,261,000 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- CNX (CNX Resources Corp) Q1 2022: -913,098,000 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- COHN (Cohen & Co Inc.) Q1 2022: -682,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- CSCIF (COSCIENS Biopharma Inc.) Q2 2022: -222,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- CYTK (Cytokinetics Inc) Q4 2017: -18,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- DYNR (Dynaresource, Inc.) Q2 2020: -3,409 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- EFC (Ellington Financial Inc.) Q1 2020: -112,178,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- EFC (Ellington Financial Inc.) Q2 2022: -32,909,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- EQH (Equitable Holdings, Inc.) Q2 2020: -2,550,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- ETON (Eton Pharmaceuticals, Inc.) Q3 2020: -161,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- XCUR (Exicure, Inc.) FY 2021: -483,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- XCUR (Exicure, Inc.) Q3 2021: -3,677,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- FMCC (Federal Home Loan Mortgage Corp) Q3 2015: -98,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- FMCC (Federal Home Loan Mortgage Corp) Q1 2016: -18,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- FGC (FG Communities Holdings Inc.) FY 2020: -17,156,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- FGC (FG Communities Holdings Inc.) Q1 2020: -8,677,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- FOA (Finance of America Companies Inc.) Q2 2022: -21,174,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- FOA (Finance of America Companies Inc.) Q3 2022: -37,547,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- FOA (Finance of America Companies Inc.) Q2 2023: -111,897,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- FOA (Finance of America Companies Inc.) Q3 2023: -70,440,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- FOA (Finance of America Companies Inc.) Q3 2026: -57,948,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- FRHC (Freedom Holding Corp.) Q4 2017: -2,159,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- GPOR (Gulfport Energy Corp) Q2 2016: -28,158,000 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- GPOR (Gulfport Energy Corp) Q3 2021: -242,528,000 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- GPOR (Gulfport Energy Corp) Q1 2022: -307,816,000 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- HBNC (Horizon Bancorp Inc /In/) FY 2025: -26,966,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- HBNC (Horizon Bancorp Inc /In/) Q3 2025: -236,948,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- GPUS (Hyperscale Data, Inc.) Q3 2021: -30,794,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- IEP (Icahn Enterprises L.P.) Q1 2020: -60,000,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- INBS (Intelligent Bio Solutions Inc.) Q4 2019: -798 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- INBS (Intelligent Bio Solutions Inc.) Q1 2020: -1,533 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- JXN (Jackson Financial Inc.) Q2 2020: -2,332,900,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- JXN (Jackson Financial Inc.) Q4 2022: -2,014,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- JXN (Jackson Financial Inc.) Q1 2023: -764,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- JXN (Jackson Financial Inc.) Q1 2024: -322,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- JXN (Jackson Financial Inc.) Q2 2025: -471,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- JMSB (John Marshall Bancorp, Inc.) Q3 2023: -4,836,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- KKR (KKR & Co. Inc.) Q4 2018: -178,056,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- KYNB (Kyntra Bio, Inc.) Q2 2026: -1,474,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- MBI (Mbia Inc) Q1 2020: -6,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- MBI (Mbia Inc) Q2 2024: -37,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- MSB (Mesabi Trust) Q2 2022: -4,366,811 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- MSB (Mesabi Trust) Q4 2022: -2,172,144 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- MSGK (MSG Knickerbockers Corp.) Q2 2020: -6,960,000 — **real**: Q2 2020: games cancelled (COVID-19), ticket and sponsorship refunds exceeded revenue; as filed. (2026-11-24)
- NGTF (NightFood Holdings, Inc.) Q1 2016: -1,781 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- NOG (Northern Oil & Gas, Inc.) Q1 2019: -6,934,000 — **real**: Energy producer / royalty trust: revenue includes derivative (hedging) losses or negative royalty adjustments; as filed. (2026-11-24)
- OSG (Octave Specialty Group Inc) Q1 2020: -70,000,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- ORMP (Oramed Pharmaceuticals Inc.) Q2 2016: -163,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- PTN (Palatin Technologies Inc) FY 2021: -188,597 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- PTN (Palatin Technologies Inc) Q3 2020: -288,560 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- PTN (Palatin Technologies Inc) Q4 2020: -163,971 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- PAYS (Paysign, Inc.) Q3 2020: -152,541 — **real**: Q3 2020: the 10-Q's nine-month revenue ($16.87M) is below the half-year's ($17.02M) after a revenue reversal; as filed. (2026-11-24)
- PMT (PennyMac Mortgage Investment Trust) Q1 2020: -506,517,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- PLUG (Plug Power Inc) FY 2020: -93,237,000 — **real**: 2020: $399.7M of warrant charges to Amazon and Walmart were recorded against revenue (restated 10-K/A); as filed. (2026-11-24)
- QNBC (Qnb Corp.) Q3 2026: -5,790,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- RC (Ready Capital Corp) Q1 2024: -54,079,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RC (Ready Capital Corp) Q2 2024: -31,441,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RC (Ready Capital Corp) Q2 2025: -9,773,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RC (Ready Capital Corp) Q1 2026: -63,121,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RC (Ready Capital Corp) Q2 2026: -13,837,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RWT (Redwood Trust Inc) FY 2020: -351,443,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RWT (Redwood Trust Inc) FY 2022: -7,225,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RWT (Redwood Trust Inc) Q1 2020: -841,544,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RWT (Redwood Trust Inc) Q2 2022: -70,495,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RWT (Redwood Trust Inc) Q3 2022: -2,200,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RBKB (Rhinebeck Bancorp, Inc.) Q3 2024: -329,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- RPT (Rithm Property Trust Inc.) FY 2023: -17,074,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RPT (Rithm Property Trust Inc.) FY 2024: -55,488,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RPT (Rithm Property Trust Inc.) Q2 2023: -2,414,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RPT (Rithm Property Trust Inc.) Q1 2024: -49,381,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RPT (Rithm Property Trust Inc.) Q2 2024: -8,958,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RPT (Rithm Property Trust Inc.) Q3 2024: -2,783,000 — **real**: Mortgage REIT / lender: revenue includes fair-value losses on loans and securities; as filed, or net interest income plus other income for the quarter. (2026-11-24)
- RCL (Royal Caribbean Cruises Ltd) Q3 2020: -33,688,000 — **real**: Q3 2020: cruises suspended; the 10-Q's nine-month revenue ($2,174.7M) is below the half-year's ($2,208.4M), so the quarter is -$33.7M as filed. (2026-11-24)
- MCRB (Seres Therapeutics, Inc.) Q1 2023: -522,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- STTK (Shattuck Labs, Inc.) Q2 2021: -4,231,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- SFNC (Simmons First National Corp) Q3 2025: -569,526,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- STKE (SOL Strategies Inc.) Q4 2025: -4,116,831 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- SLDP (Solid Power, Inc.) Q2 2026: -268,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- STCB (Starco Brands, Inc.) Q3 2015: -115,335 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- STEP (StepStone Group Inc.) FY 2023: -67,574,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- STEP (StepStone Group Inc.) Q2 2020: -61,413,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- STEP (StepStone Group Inc.) Q2 2022: -77,218,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- STEP (StepStone Group Inc.) Q3 2022: -158,495,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- STEP (StepStone Group Inc.) Q4 2022: -4,235,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- STEP (StepStone Group Inc.) Q4 2023: -14,612,000 — **real**: Asset manager / holding company: revenue includes investment losses and carried-interest reversals; as filed or derived from filed totals. (2026-11-24)
- STOK (Stoke Therapeutics, Inc.) Q2 2023: -2,481,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- TNGX (Tango Therapeutics, Inc.) Q3 2020: -11,261,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- TCBS (Texas Community Bancshares, Inc.) Q1 2024: -599,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- TXMD (TherapeuticsMD, Inc.) Q3 2023: -53,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- TFC (Truist Financial Corp) Q2 2024: -1,685,000,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- TRMK (Trustmark Corp) Q2 2024: -257,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- UCB (United Community Banks Inc) Q3 2026: -11,966,000 — **real**: Bank: revenue is net interest income plus noninterest income, negative in the quarter it sold securities at a large loss (balance-sheet repositioning); matches the filed lines. (2026-11-24)
- UTGN (Utg Inc) Q1 2020: -12,945,394 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- UTGN (Utg Inc) Q3 2021: -5,457,999 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- UTGN (Utg Inc) Q1 2023: -3,035,054 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- VRCA (Verrica Pharmaceuticals Inc.) Q3 2024: -1,781,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- VIR (Vir Biotechnology, Inc.) Q2 2022: -40,629,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- VIR (Vir Biotechnology, Inc.) Q1 2026: -29,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- VIR (Vir Biotechnology, Inc.) Q3 2026: -188,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- VTGN (Vistagen Therapeutics, Inc.) FY 2023: -227,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- VTGN (Vistagen Therapeutics, Inc.) Q3 2022: -893,000 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- WTM (White Mountains Insurance Group Ltd) Q4 2016: -5,500,000 — **real**: Insurer / guarantor: revenue includes investment and hedging losses (e.g. Q1 2020); as filed, or the quarter derived from filed year-to-date totals. (2026-11-24)
- WKHS (Workhorse Group Inc.) FY 2021: -851,922 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)
- WKHS (Workhorse Group Inc.) Q3 2021: -576,602 — **real**: Small reversal of previously recognised revenue (collaboration, licence or returns) in that period; as filed or derived from the filed year-to-date and annual totals. (2026-11-24)

### Financials · Profit far bigger than revenue (52)

- ACTG (Acacia Research Corp) FY 2020: revenue 29.8M, net income 109.2M — **real**: 2020: gains on its equity stakes (Arix Bioscience, Veritone) and investment income. Revenue is the reported top line. (2026-11-24)
- AGIO (Agios Pharmaceuticals, Inc.) FY 2024: revenue 36.5M, net income 673.7M — **real**: 2024: $905M from selling its vorasidenib royalty to Royalty Pharma, booked as a gain. Revenue is the reported top line. (2026-11-24)
- ABTC (American Bitcoin Corp.) FY 2024: revenue 71.5M, net income 428.9M — **real**: 2024: profit includes fair-value gains on digital assets; as filed. Revenue is the reported top line. (2026-11-24)
- ARL (American Realty Investors Inc) FY 2022: revenue 37.5M, net income 373.3M — **real**: 2022: gain on selling its share of the VAA apartment joint venture. Revenue is the reported top line. (2026-11-24)
- AIV (Apartment Investment & Management Co) FY 2025: revenue 138.5M, net income 547.2M — **real**: 2025: gains on property sales. Revenue is the reported top line. (2026-11-24)
- APYX (Apyx Medical Corp) FY 2018: revenue 16.7M, net income 62.7M — **real**: 2018: sale of its core surgical business to Symmetry Surgical (discontinued operations). Revenue is the reported top line. (2026-11-24)
- AZTA (Azenta, Inc.) FY 2022: revenue 555.5M, net income 2.13bn — **real**: 2022: $3bn sale of its semiconductor automation business (discontinued operations). Revenue is the reported top line. (2026-11-24)
- CHRS (Coherus Oncology, Inc.) FY 2025: revenue 42.2M, net income 168.0M — **real**: 2025: gains on selling Udenyca and other products (discontinued operations). Revenue is the reported top line. (2026-11-24)
- CRON (Cronos Group Inc.) FY 2019: revenue 23.8M, net income 1.17bn — **real**: 2019: fair-value gain on the Altria derivative liabilities. Revenue is the reported top line. (2026-11-24)
- ELUT (Elutia Inc.) FY 2025: revenue 12.3M, net income 53.4M — **real**: 2025: gain on selling its BioEnvelope business. Revenue is the reported top line. (2026-11-24)
- FRPH (Frp Holdings, Inc.) FY 2018: revenue 22.0M, net income 124.5M — **real**: 2018: gain on selling its warehouse portfolio to Blackstone. Revenue is the reported top line. (2026-11-24)
- GAIA (Gaia, Inc) FY 2016: revenue 17.2M, net income 87.1M — **real**: 2016: gain on selling its Gaiam brand business (discontinued operations). Revenue is the reported top line. (2026-11-24)
- LGND (Ligand Pharmaceuticals Inc) FY 2019: revenue 120.3M, net income 629.3M — **real**: 2019: gain on selling its Promacta royalty to Novartis. Revenue is the reported top line. (2026-11-24)
- NCMI (National CineMedia, Inc.) FY 2023: revenue 165.2M, net income 705.2M — **real**: 2023: gain on deconsolidating National CineMedia LLC in its bankruptcy. Revenue is the reported top line. (2026-11-24)
- OSG (Octave Specialty Group Inc) FY 2022: revenue 53.6M, net income 522.1M — **real**: 2022 (as Ambac): profit includes its Bank of America litigation settlement; as filed. Revenue is the reported top line. (2026-11-24)
- SENS (Senseonics Holdings, Inc.) FY 2022: revenue 16.4M, net income 142.1M — **real**: 2022: fair-value gains on convertible notes and derivative liabilities. Revenue is the reported top line. (2026-11-24)
- SHEN (Shenandoah Telecommunications Co/Va/) FY 2021: revenue 245.2M, net income 998.8M — **real**: 2021: gain on selling its wireless business to T-Mobile. Revenue is the reported top line. (2026-11-24)
- TCI (Transcontinental Realty Investors Inc) FY 2022: revenue 36.7M, net income 468.3M — **real**: 2022: gain on selling its share of the VAA apartment joint venture. Revenue is the reported top line. (2026-11-24)
- ILLR (Triller Group Inc.) FY 2021: revenue 11.5M, net income 96.5M — **real**: 2021: profit includes large non-operating fair-value gains; as filed (not yet checked against the 10-K text). Revenue is the reported top line. (2026-11-24)
- SAFX (XCF Global, Inc.) FY 2025: revenue 20.8M, net income 74.0M — **real**: 2025: profit includes non-operating gains around its business combination; as filed (not yet checked against the 10-K text). Revenue is the reported top line. (2026-11-24)
- CNE.L (Capricorn Energy plc) FY 2021: revenue 57.1M, net income 894.5M — **real**: 2021: refund of Indian tax (about $1.06bn) under the retrospective-tax settlement. Revenue is the reported top line. (2026-11-24)
- CREI.L (Custodian Property Income REIT plc) FY 2022: revenue 39.9M, net income 122.3M — **real**: 2022: property revaluation gains (financial year to March 2022). Revenue is the reported top line. (2026-11-24)
- LMP.L (LondonMetric Property) FY 2022: revenue 133.2M, net income 734.5M — **real**: 2022: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- NOG.L (Nostrum Oil & Gas plc) FY 2023: revenue 119.6M, net income 831.7M — **real**: 2023: gain on restructuring its bonds. Revenue is the reported top line. (2026-11-24)
- SGRO.L (Segro) FY 2020: revenue 432.0M, net income 1.43bn — **real**: 2020–21: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- SGRO.L (Segro) FY 2021: revenue 546.0M, net income 4.06bn — **real**: 2020–21: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- BLEE.PA (Bleecker) FY 2021: revenue 21.8M, net income 77.4M — **real**: 2021: property revaluation gains; revenue is rents. Revenue is the reported top line. (2026-11-24)
- RF.PA (Eurazeo) FY 2023: revenue 343.7M, net income 1.82bn — **real**: 2023: investment company, profit includes portfolio gains; as filed. Revenue is the reported top line. (2026-11-24)
- ALB.MC (Corporacion Financiera Alba, S.A.) FY 2023: revenue 50.7M, net income 234.0M — **real**: Investment company: profit is mainly gains and income from its holdings. Revenue is the reported top line. (2026-11-24)
- ALB.MC (Corporacion Financiera Alba, S.A.) FY 2024: revenue 15.1M, net income 94.1M — **real**: Investment company: profit is mainly gains and income from its holdings. Revenue is the reported top line. (2026-11-24)
- EZE.MC (Grupo Ezentis SA) FY 2023: revenue 17.3M, net income 194.1M — **real**: 2023: gain on restructuring its debt. Revenue is the reported top line. (2026-11-24)
- SAGAB.ST (AB Sagax) FY 2021: revenue 3.10bn, net income 9.81bn — **real**: 2021: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- CNCJOB.ST (Concejo AB (publ)) FY 2020: revenue 255.7M, net income 1.34bn — **real**: 2020: profit includes gains on holdings; as filed (not yet checked against the annual report text). Revenue is the reported top line. (2026-11-24)
- EAST.ST (Eastnine Ab (Publ)) FY 2021: revenue 21.5M, net income 72.3M — **real**: 2021–22: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- EAST.ST (Eastnine Ab (Publ)) FY 2022: revenue 30.3M, net income 108.6M — **real**: 2021–22: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- HEBAB.ST (Heba Fastighets Aktiebolag) FY 2021: revenue 449.6M, net income 1.48bn — **real**: 2021: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- INVEB.ST (Investor Aktiebolag) FY 2021: revenue 40.74bn, net income 228.07bn — **real**: 2021: investment company: gains on its listed holdings. Revenue is the reported top line. (2026-11-24)
- JOMA.ST (John Mattson Fastighetsföretagen AB (publ)) FY 2021: revenue 407.9M, net income 1.33bn — **real**: 2021: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- TOBII.ST (Tobii AB) FY 2021: revenue 616.0M, net income 3.15bn — **real**: 2021: gain on distributing Tobii Dynavox to shareholders. Revenue is the reported top line. (2026-11-24)
- WITH.HE (WithSecure Oyj) FY 2022: revenue 134.7M, net income 430.3M — **real**: 2022: gain on demerging F-Secure's consumer business. Revenue is the reported top line. (2026-11-24)
- PRS.OL (Prosafe SE) FY 2021: revenue 141.1M, net income 927.9M — **real**: 2021: gain on its debt restructuring. Revenue is the reported top line. (2026-11-24)
- GIMB.BR (Gimv) FY 2021: revenue 20.3M, net income 205.7M — **real**: Investment company: profit is mainly gains on its portfolio. Revenue is the reported top line. (2026-11-24)
- GIMB.BR (Gimv) FY 2022: revenue 26.3M, net income 174.3M — **real**: Investment company: profit is mainly gains on its portfolio. Revenue is the reported top line. (2026-11-24)
- GIMB.BR (Gimv) FY 2024: revenue 33.9M, net income 217.1M — **real**: Investment company: profit is mainly gains on its portfolio. Revenue is the reported top line. (2026-11-24)
- GIMB.BR (Gimv) FY 2025: revenue 33.6M, net income 219.0M — **real**: Investment company: profit is mainly gains on its portfolio. Revenue is the reported top line. (2026-11-24)
- MONT.BR (Montea) FY 2021: revenue 75.6M, net income 227.7M — **real**: 2021: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- SOF.BR (Sofina) FY 2024: revenue 22.9M, net income 1.36bn — **real**: Investment company: profit is mainly gains on its portfolio. Revenue is the reported top line. (2026-11-24)
- SOF.BR (Sofina) FY 2025: revenue 17.9M, net income 113.0M — **real**: Investment company: profit is mainly gains on its portfolio. Revenue is the reported top line. (2026-11-24)
- WDP.BR (Warehouses De Pauw) FY 2021: revenue 255.1M, net income 982.3M — **real**: 2021: property revaluation gains. Revenue is the reported top line. (2026-11-24)
- ORCL.LU (Cpi Fim SA) FY 2021: revenue 67.6M, net income 294.1M — **real**: 2021–22: property company, profit includes revaluation and disposal gains; as filed. Revenue is the reported top line. (2026-11-24)
- ORCL.LU (Cpi Fim SA) FY 2022: revenue 46.4M, net income 147.2M — **real**: 2021–22: property company, profit includes revaluation and disposal gains; as filed. Revenue is the reported top line. (2026-11-24)
- IVL1L.VS (Akcinė bendrovė "Invalda INVL") FY 2024: revenue 14.1M, net income 44.4M — **real**: 2024: investment company: gains on its holdings. Revenue is the reported top line. (2026-11-24)

### Financials · Revenue jumps 10× in a year (46)

- ABCL (AbCellera Biologics Inc.): FY 2019 11.6M → FY 2020 233.2M — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- ABCL (AbCellera Biologics Inc.): FY 2022 485.4M → FY 2023 38.0M — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- ACTG (Acacia Research Corp): FY 2018 131.5M → FY 2019 11.2M — **real**: Patent licensing revenue is lumpy (large 2018 settlements); as filed. (2026-11-24)
- AHR (American Healthcare REIT, Inc.): FY 2018 84.5M → FY 2019 1.10bn — **real**: Predecessor figures: the 2018 year is a smaller predecessor's before the mergers that formed the company; as filed (not yet checked against the 10-K text). (2026-11-24)
- AMLX (Amylyx Pharmaceuticals, Inc.): FY 2022 22.2M → FY 2023 380.8M — **real**: Product launch or first full year of sales; as filed. (2026-11-24)
- ARCT (Arcturus Therapeutics Holdings Inc.): FY 2021 12.4M → FY 2022 206.0M — **real**: Collaboration, licence or milestone revenue is lumpy: a large one-off payment in one year; as filed. (2026-11-24)
- AD (Array Digital Infrastructure, Inc.): FY 2022 4.17bn → FY 2023 100.5M — **real**: The revenue measure changed between the years (restatement, discontinued operations or a new accounting standard); each year is as its latest report states it. (2026-11-24)
- ARWR (Arrowhead Pharmaceuticals, Inc.): FY 2018 16.1M → FY 2019 168.8M — **real**: Collaboration, licence or milestone revenue is lumpy: a large one-off payment in one year; as filed. (2026-11-24)
- AAWH (Ascend Wellness Holdings, Inc.): FY 2019 12.0M → FY 2020 143.7M — **real**: Revenue rose with acquisitions and expansion; as filed (not yet checked against the filing text). (2026-11-24)
- CANG (Cango Inc.): FY 2023 1.70bn → FY 2024 89.9M — **real**: The revenue measure changed between the years (restatement, discontinued operations or a new accounting standard); each year is as its latest report states it. (car business sold; bitcoin mining from late 2024) (2026-11-24)
- DNLI (Denali Therapeutics Inc.): FY 2019 26.7M → FY 2020 335.7M — **real**: Collaboration, licence or milestone revenue is lumpy: a large one-off payment in one year; as filed. (2026-11-24)
- DBRHP (DigitalBridge Group, Inc.): FY 2018 1.17bn → FY 2019 61.0M — **real**: The revenue measure changed between the years (discontinued operations restated); as filed (not yet checked against the 10-K text). (2026-11-24)
- ECHO (EchoStar CORP): FY 2020 1.89bn → FY 2021 19.82bn — **real**: The revenue measure changed between the years (restatement, discontinued operations or a new accounting standard); each year is as its latest report states it. (DISH included from 2021 after the 2023 combination) (2026-11-24)
- FBIO (Fortress Biotech, Inc.): FY 2016 16.5M → FY 2017 187.6M — **real**: 2017: consolidation of acquired subsidiaries (Checkpoint, Journey); as filed. (2026-11-24)
- FLGT (Fulgent Genetics, Inc.): FY 2019 32.5M → FY 2020 421.7M — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- LXRX (Lexicon Pharmaceuticals, Inc.): FY 2019 322.1M → FY 2020 24.0M — **real**: Collaboration, licence or milestone revenue is lumpy: a large one-off payment in one year; as filed. (2026-11-24)
- LQDA (Liquidia Corp): FY 2024 14.0M → FY 2025 158.3M — **real**: Product launch or first full year of sales; as filed. (2026-11-24)
- LCID (Lucid Group, Inc.): FY 2021 27.1M → FY 2022 608.2M — **real**: Product launch or first full year of sales; as filed. (2026-11-24)
- MNKD (Mannkind Corp): FY 2016 174.8M → FY 2017 11.7M — **real**: Collaboration, licence or milestone revenue is lumpy: a large one-off payment in one year; as filed. (2026-11-24)
- MRNA (Moderna, Inc.): FY 2019 60.2M → FY 2020 803.0M — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- MRNA (Moderna, Inc.): FY 2020 803.0M → FY 2021 18.47bn — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- MYND (Mynd.ai, Inc.): FY 2020 26.0M → FY 2021 448.2M — **real**: Revenue rose with a business combination; as filed (not yet checked against the filing text). (2026-11-24)
- NKTR (Nektar Therapeutics): FY 2018 1.19bn → FY 2019 114.6M — **real**: 2018: $1.0bn upfront from Bristol-Myers Squibb recognised as revenue; as filed. (2026-11-24)
- NBIX (Neurocrine Biosciences Inc): FY 2016 15.0M → FY 2017 161.6M — **real**: 2017: Ingrezza launched; as filed. (2026-11-24)
- NVAX (Novavax Inc): FY 2019 18.7M → FY 2020 475.6M — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- OVID (Ovid Therapeutics Inc.): FY 2020 12.6M → FY 2021 208.4M — **real**: 2021: $196M from Takeda for soticlestat rights; as filed. (2026-11-24)
- QXO (QXO, Inc.): FY 2024 56.9M → FY 2025 6.84bn — **real**: 2025: acquisition of Beacon Roofing Supply; as filed. (2026-11-24)
- RGNX (REGENXBIO Inc.): FY 2017 10.4M → FY 2018 218.5M — **real**: Collaboration, licence or milestone revenue is lumpy: a large one-off payment in one year; as filed. (2026-11-24)
- RIOT (Riot Platforms, Inc.): FY 2020 12.1M → FY 2021 213.2M — **real**: 2021: bitcoin mining scaled up; as filed. (2026-11-24)
- RIVN (Rivian Automotive, Inc. / DE): FY 2021 55.0M → FY 2022 1.66bn — **real**: Product launch or first full year of sales; as filed. (2026-11-24)
- SDOT (Sadot Group Inc.): FY 2021 10.3M → FY 2022 161.7M — **real**: 2022: entered commodity trading; as filed. (2026-11-24)
- SIGA (Siga Technologies Inc): FY 2017 12.3M → FY 2018 477.1M — **real**: BARDA deliveries of TPOXX are lumpy (2018 large delivery); as filed. (2026-11-24)
- SIGA (Siga Technologies Inc): FY 2018 477.1M → FY 2019 26.7M — **real**: BARDA deliveries of TPOXX are lumpy (2018 large delivery); as filed. (2026-11-24)
- TARS (Tarsus Pharmaceuticals, Inc.): FY 2023 17.4M → FY 2024 183.0M — **real**: Product launch or first full year of sales; as filed. (2026-11-24)
- UROY (Uranium Royalty Corp.): FY 2025 11.2M → FY 2026 186.8M — **real**: FY2026: uranium sales; royalty company revenue is lumpy; as filed. (2026-11-24)
- VIR (Vir Biotechnology, Inc.): FY 2020 76.4M → FY 2021 1.10bn — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- VIR (Vir Biotechnology, Inc.): FY 2022 1.62bn → FY 2023 86.2M — **real**: Covid-19 product or testing revenue rose and fell; as filed. (2026-11-24)
- SEAT (Vivid Seats Inc.): FY 2019 403.6M → FY 2020 23.3M — **real**: 2020: live events cancelled (Covid-19); as filed. (2026-11-24)
- SEAT (Vivid Seats Inc.): FY 2020 23.3M → FY 2021 389.7M — **real**: 2020: live events cancelled (Covid-19); as filed. (2026-11-24)
- ZYME (Zymeworks Inc.): FY 2021 26.7M → FY 2022 412.5M — **real**: Collaboration, licence or milestone revenue is lumpy: a large one-off payment in one year; as filed. (2026-11-24)
- CSN.L (Chesnara): FY 2021 1.51bn → FY 2022 33.9M — **real**: The revenue measure changed between the years (restatement, discontinued operations or a new accounting standard); each year is as its latest report states it. (IFRS 17 from 2022) (2026-11-24)
- ENOG.L (Energean): FY 2020 28.0M → FY 2021 497.0M — **real**: 2021: first full year after buying Edison's oil and gas business (completed December 2020); as filed. (2026-11-24)
- LGEN.L (Legal & General): FY 2021 45.45bn → FY 2022 1.70bn — **real**: The revenue measure changed between the years (restatement, discontinued operations or a new accounting standard); each year is as its latest report states it. (IFRS 17 from 2022) (2026-11-24)
- RF.PA (Eurazeo): FY 2022 4.64bn → FY 2023 343.7M — **real**: The revenue measure changed between the years (restatement, discontinued operations or a new accounting standard); each year is as its latest report states it. (investment entity from 2023: holdings no longer consolidated) (2026-11-24)
- EZE.MC (Grupo Ezentis SA): FY 2021 216.3M → FY 2022 21.6M — **real**: 2022: businesses sold in its restructuring; as filed. (2026-11-24)
- STB.OL (Storebrand Asa): FY 2021 119.78bn → FY 2022 8.68bn — **real**: The revenue measure changed between the years (restatement, discontinued operations or a new accounting standard); each year is as its latest report states it. (IFRS 17 from 2022) (2026-11-24)

### Financials · Quarters don't add up to the year (62)

- RIME (Algorhythm Holdings, Inc.) FY 2016: quarters 53.0M vs year 48.9M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RIME (Algorhythm Holdings, Inc.) FY 2017: quarters 61.8M vs year 52.9M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RIME (Algorhythm Holdings, Inc.) FY 2018: quarters 48.2M vs year 60.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RIME (Algorhythm Holdings, Inc.) FY 2019: quarters 38.4M vs year 46.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RIME (Algorhythm Holdings, Inc.) FY 2020: quarters 43.3M vs year 38.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RIME (Algorhythm Holdings, Inc.) FY 2021: quarters 48.2M vs year 45.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RIME (Algorhythm Holdings, Inc.) FY 2022: quarters 38.7M vs year 47.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RIME (Algorhythm Holdings, Inc.) FY 2023: quarters 32.6M vs year 39.3M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- ABTC (American Bitcoin Corp.) FY 2020: quarters 13.9M vs year 12.6M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RCEL (AVITA Medical, Inc.) FY 2020: quarters 17.9M vs year 14.3M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RCEL (AVITA Medical, Inc.) FY 2021: quarters 33.0M vs year 29.2M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- BRT (BRT Apartments Corp.) FY 2016: quarters 101.8M vs year 98.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- BRT (BRT Apartments Corp.) FY 2017: quarters 108.5M vs year 105.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- CSR (Centerspace) FY 2016: quarters 199.9M vs year 188.3M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- CZWI (Citizens Community Bancorp Inc.) FY 2016: quarters 25.3M vs year 24.0M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- CZWI (Citizens Community Bancorp Inc.) FY 2017: quarters 29.7M vs year 27.0M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- CZWI (Citizens Community Bancorp Inc.) FY 2018: quarters 40.8M vs year 37.7M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- FABC (Fabric.AI, Inc.) FY 2015: quarters 18.6M vs year 24.4M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- FABC (Fabric.AI, Inc.) FY 2016: quarters 15.5M vs year 14.6M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- FNWB (First Northwest Bancorp) FY 2016: quarters 35.2M vs year 33.6M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- FNWB (First Northwest Bancorp) FY 2017: quarters 40.1M vs year 37.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- HNVR (Hanover Bancorp, Inc. /MD) FY 2021: quarters 55.1M vs year 45.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- HTB (HomeTrust Bancshares, Inc.) FY 2017: quarters 116.4M vs year 107.3M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- HTB (HomeTrust Bancshares, Inc.) FY 2018: quarters 125.2M vs year 120.3M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- HTB (HomeTrust Bancshares, Inc.) FY 2019: quarters 136.5M vs year 129.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- HTB (HomeTrust Bancshares, Inc.) FY 2021: quarters 148.8M vs year 143.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- HTB (HomeTrust Bancshares, Inc.) FY 2022: quarters 162.5M vs year 149.9M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- HTB (HomeTrust Bancshares, Inc.) FY 2023: quarters 202.0M vs year 188.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- INSE (Inspired Entertainment, Inc.) FY 2016: quarters 116.0M vs year 119.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- INSE (Inspired Entertainment, Inc.) FY 2017: quarters 126.9M vs year 122.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- INVU (Investview, Inc.) FY 2017: quarters 13.2M vs year 12.9M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- INVU (Investview, Inc.) FY 2018: quarters 30.7M vs year 17.9M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- INVU (Investview, Inc.) FY 2019: quarters 26.0M vs year 29.7M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- INVU (Investview, Inc.) FY 2020: quarters 25.7M vs year 24.2M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- INVU (Investview, Inc.) FY 2021: quarters 89.3M vs year 38.3M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- MRDN (Meridian Holdings Inc./NV) FY 2022: quarters 37.9M vs year 36.0M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- MIR (Mirion Technologies, Inc.) FY 2021: quarters 644.6M vs year 611.6M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- MYGN (Myriad Genetics Inc) FY 2018: quarters 796.1M vs year 743.7M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- MYGN (Myriad Genetics Inc) FY 2019: quarters 813.4M vs year 851.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- OSK (Oshkosh Corp) FY 2017: quarters 7.20bn vs year 6.83bn — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- OSK (Oshkosh Corp) FY 2018: quarters 7.92bn vs year 7.71bn — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- OSK (Oshkosh Corp) FY 2021: quarters 7.95bn vs year 7.74bn — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- PBFS (Pioneer Bancorp, Inc./MD) FY 2019: quarters 68.5M vs year 64.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- PBFS (Pioneer Bancorp, Inc./MD) FY 2020: quarters 58.5M vs year 64.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- PBFS (Pioneer Bancorp, Inc./MD) FY 2021: quarters 56.2M vs year 57.6M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- PBFS (Pioneer Bancorp, Inc./MD) FY 2022: quarters 68.1M vs year 56.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- PBFS (Pioneer Bancorp, Inc./MD) FY 2024: quarters 86.7M vs year 82.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RJET (Republic Airways Holdings Inc.) FY 2020: quarters 511.4M vs year 545.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- REPX (Riley Exploration Permian, Inc.) FY 2021: quarters 185.3M vs year 151.0M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RGLD (Royal Gold Inc) FY 2018: quarters 429.8M vs year 459.0M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RGLD (Royal Gold Inc) FY 2019: quarters 467.9M vs year 423.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- RGLD (Royal Gold Inc) FY 2020: quarters 561.6M vs year 498.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- SCND (Scientific Industries Inc) FY 2022: quarters 10.9M vs year 11.4M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- SPHR (Sphere Entertainment Co.) FY 2020: quarters 1.20bn vs year 1.44bn — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- SPHR (Sphere Entertainment Co.) FY 2021: quarters 755.9M vs year 789.9M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- SPHR (Sphere Entertainment Co.) FY 2022: quarters 590.5M vs year 610.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- SPHR (Sphere Entertainment Co.) FY 2023: quarters 723.3M vs year 573.8M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- SPHR (Sphere Entertainment Co.) FY 2024: quarters 1.13bn vs year 1.03bn — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- SRTA (Strata Critical Medical, Inc.) FY 2021: quarters 67.2M vs year 50.5M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- TDW (Tidewater Inc) FY 2016: quarters 625.0M vs year 979.1M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- TDW (Tidewater Inc) FY 2017: quarters 490.9M vs year 601.6M — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)
- ZWS (Zurn Elkay Water Solutions Corp) FY 2017: quarters 1.99bn vs year 1.92bn — **real**: Its stored year end is 31 December (from its latest annual report), but earlier years ended in another month (a change of year end, or a predecessor's years), so their calendar-labelled quarters can't add up to them. Figures as filed; the year of the change not yet checked. (2026-11-24)

