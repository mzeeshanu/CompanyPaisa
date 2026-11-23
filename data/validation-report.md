# Data validation report — 2026-11-23 05:00 UTC

Checks everything in `companypaisa.db` together, as the website sees it — markets: pk (pk-2026.09.19), anz (anz-2026.11.22), sec (sec-2026.11.22), uk (uk-2026.11.23), eu (eu-2026.11.23).

**Errors** are values that can't be right. **Warnings** are unusual values worth a look — many are real (big acquisitions, holding-company gains, mega stock grants). Nothing here changes the data.

- Errors: **0**
- Warnings: **1184**

## Checks

| Area | Check | Level | Rows |
|---|---|---|---|
| Companies | Currency without an exchange rate | Error | ✓ 0 |
| Companies | No location at all | Error | ✓ 0 |
| Companies | Only a local office, no headquarters | Warning | 1 |
| Companies | Same name under two ids | Warning | 5 |
| Locations | Outside its country | Error | ✓ 0 |
| Locations | Missing city | Warning | ✓ 0 |
| Financials | No figures at all | Error | ✓ 0 |
| Financials | Negative revenue | Warning | 267 |
| Financials | Period in the future | Error | ✓ 0 |
| Financials | Duplicate period | Error | ✓ 0 |
| Financials | Implausibly large revenue | Warning | ✓ 0 |
| Financials | Impossible size | Error | ✓ 0 |
| Financials | Another company's figures | Error | ✓ 0 |
| Financials | Profit far bigger than revenue | Warning | 58 |
| Financials | Revenue jumps 10× in a year | Warning | 47 |
| Financials | Quarters don't add up to the year | Warning | 104 |
| Financials | Out of date | Warning | 49 |
| Financials | Annual figures missing, quarters only | Warning | 120 |
| Pay | Negative amount | Error | ✓ 0 |
| Pay | Year in the future | Error | ✓ 0 |
| Pay | Person record missing | Error | ✓ 0 |
| Pay | Same person, company and year twice | Error | ✓ 0 |
| Pay | Salary bigger than total | Warning | ✓ 0 |
| Pay | Impossibly large total | Error | ✓ 0 |
| Pay | Record award checked by hand | Warning | 1 |
| Pay | Very large total | Warning | 65 |
| Pay | Tiny total | Warning | ✓ 0 |
| Pay | Name doesn't look like a person | Warning | 3 |
| Pay | Pieces don't add up to the total | Warning | ✓ 0 |
| EdgarTools | Revenue differs from the annual report | Warning | 399 |
| EdgarTools | Net income differs from the annual report | Warning | 65 |

## Overview

### Companies by country

| | Count |
|---|---|
| US | 3,828 |
| GB | 337 |
| SE | 297 |
| PK | 228 |
| FR | 194 |
| NO | 151 |
| IT | 144 |
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
| Nasdaq | 2,162 |
| NYSE | 1,427 |
| LSE | 337 |
| Nasdaq Stockholm | 297 |
| Pakistan Stock Exchange | 228 |
| OTC | 209 |
| Euronext Paris | 194 |
| NYSE American | 158 |
| Oslo Børs | 151 |
| Borsa Italiana | 144 |
| Bolsa de Madrid | 106 |
| Nasdaq Copenhagen | 84 |
| Nasdaq Helsinki | 83 |
| ASX | 76 |
| Euronext Amsterdam | 73 |
| Euronext Brussels | 68 |
| Wiener Börse | 50 |
| Nasdaq Vilnius | 17 |
| NYSE Arca | 16 |
| NZX | 11 |
| Ljubljana Stock Exchange | 9 |
| TXSE | 9 |
| Unlisted | 3 |
| Cboe | 2 |
| Luxembourg Stock Exchange | 2 |

### Companies by reporting currency

| | Count |
|---|---|
| USD | 4,034 |
| EUR | 775 |
| SEK | 282 |
| GBP | 274 |
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
| Other | 1,697 |
| Finance | 746 |
| Healthcare | 679 |
| Consumer & retail | 587 |
| Industrials | 514 |
| Software & IT | 353 |
| Energy & utilities | 279 |
| Real estate | 229 |
| Business services | 207 |
| Materials | 196 |
| Technology hardware | 144 |
| Media & telecom | 94 |
| Transportation | 94 |
| Semiconductors | 78 |
| Education | 19 |

### Companies with executive pay, by country

| | Count |
|---|---|
| US | 3,399 |
| PK | 123 |
| GB | 96 |
| CA | 28 |
| AU | 3 |

### Years of annual history per company

| | Count |
|---|---|
| 10+ | 2,459 |
| 6–9 | 1,735 |
| 3–5 | 1,259 |
| 1–2 | 343 |
| 0 (quarters only) | 120 |

### Newest annual figures

| | Count |
|---|---|
| FY 2025 | 4,778 |
| FY 2026 | 702 |
| FY 2024 | 267 |
| none | 120 |
| 2023 or older | 49 |

### Rows

| | Count |
|---|---|
| Companies | 5,916 |
| Locations | 5,926 |
| Annual periods | 41,931 |
| Quarterly periods | 135,847 |
| Pay rows | 88,358 |
| People | 23,126 |

## Details

### Companies · Only a local office, no headquarters (1)

Expected for hand-added offices of companies based abroad (e.g. NICE in Utah); anything else is a missing headquarters.

- NICE (NICE Ltd.)

### Companies · Same name under two ids (5)

Usually two share classes or a parent and its subsidiary listed separately — one company shown twice.

- AGX, ARG.PA — Argan Inc
- DPZ, DOM.L — Dominos Pizza Inc
- FMCB, FMAO — Farmers & Merchants Bancorp
- IPAR, ITP.PA — Interparfums Inc
- IREN, IRE.MI — IREN Ltd

### Financials · Negative revenue (267)

Reported revenue below zero. Real for mortgage REITs and energy producers, whose revenue includes investment or hedging losses; otherwise a sign error.

- AERA (AI Era Corp.) Q2 2021: -51,200
- ALT (Altimmune, Inc.) FY 2022: -68,000
- ANG-PD (American National Group Inc.) Q4 2018: -450,985,000
- ANG-PD (American National Group Inc.) Q1 2020: -323,703,000
- AMLX (Amylyx Pharmaceuticals, Inc.) Q2 2024: -1,023,000
- ABUS (Arbutus Biopharma Corp) Q4 2016: -195,000
- ATH-PA (Athene Holding Ltd.) Q1 2020: -1,549,000,000
- ATH-PA (Athene Holding Ltd.) Q1 2022: -281,000,000
- BANC (Banc Of California, Inc.) Q4 2023: -249,351,000
- BMRC (Bank of Marin Bancorp) Q4 2025: -36,867,000
- BENF (Beneficient) FY 2023: -104,903,000
- BENF (Beneficient) FY 2024: -98,696,000
- … and 255 more

### Financials · Profit far bigger than revenue (58)

Net profit more than 3× revenue for a company with ≥ $10M revenue — possible for holding companies and one-off gains, often a revenue sub-line.

- ACTG (Acacia Research Corp) FY 2020: revenue 29.8M, net income 109.2M
- AGIO (Agios Pharmaceuticals, Inc.) FY 2024: revenue 36.5M, net income 673.7M
- ABTC (American Bitcoin Corp.) FY 2024: revenue 71.5M, net income 428.9M
- ARL (American Realty Investors Inc) FY 2022: revenue 37.5M, net income 373.3M
- AIV (Apartment Investment & Management Co) FY 2025: revenue 138.5M, net income 547.2M
- APYX (Apyx Medical Corp) FY 2018: revenue 16.7M, net income 62.7M
- AZTA (Azenta, Inc.) FY 2022: revenue 555.5M, net income 2.13bn
- CHRS (Coherus Oncology, Inc.) FY 2025: revenue 42.2M, net income 168.0M
- CRON (Cronos Group Inc.) FY 2019: revenue 23.8M, net income 1.17bn
- ELUT (Elutia Inc.) FY 2025: revenue 12.3M, net income 53.4M
- FRPH (Frp Holdings, Inc.) FY 2018: revenue 22.0M, net income 124.5M
- GAIA (Gaia, Inc) FY 2016: revenue 17.2M, net income 87.1M
- … and 46 more

### Financials · Revenue jumps 10× in a year (47)

Year-on-year revenue up or down more than tenfold (both years ≥ $10M) — a real acquisition, or a switch of revenue definition or units.

- ABCL (AbCellera Biologics Inc.): FY 2019 11.6M → FY 2020 233.2M
- ABCL (AbCellera Biologics Inc.): FY 2022 485.4M → FY 2023 38.0M
- ACTG (Acacia Research Corp): FY 2018 131.5M → FY 2019 11.2M
- AHR (American Healthcare REIT, Inc.): FY 2018 84.5M → FY 2019 1.10bn
- AMLX (Amylyx Pharmaceuticals, Inc.): FY 2022 22.2M → FY 2023 380.8M
- ARCT (Arcturus Therapeutics Holdings Inc.): FY 2021 12.4M → FY 2022 206.0M
- AD (Array Digital Infrastructure, Inc.): FY 2022 4.17bn → FY 2023 100.5M
- ARWR (Arrowhead Pharmaceuticals, Inc.): FY 2018 16.1M → FY 2019 168.8M
- AAWH (Ascend Wellness Holdings, Inc.): FY 2019 12.0M → FY 2020 143.7M
- CANG (Cango Inc.): FY 2023 1.70bn → FY 2024 89.9M
- DNLI (Denali Therapeutics Inc.): FY 2019 26.7M → FY 2020 335.7M
- DBRHP (DigitalBridge Group, Inc.): FY 2018 1.17bn → FY 2019 61.0M
- … and 35 more

### Financials · Quarters don't add up to the year (104)

For December year-ends, the four quarters differ from the annual figure by more than 2% (revenue ≥ $10M). Usually a restatement: the year was restated later (a business sold, an accounting change) while the quarters are as first reported.

- AEHR (Aehr Test Systems) FY 2017: quarters 24.3M vs year 18.9M
- AEHR (Aehr Test Systems) FY 2018: quarters 25.3M vs year 29.6M
- AEHR (Aehr Test Systems) FY 2019: quarters 22.8M vs year 21.1M
- AEHR (Aehr Test Systems) FY 2020: quarters 13.6M vs year 22.3M
- AEHR (Aehr Test Systems) FY 2021: quarters 28.2M vs year 16.6M
- AEHR (Aehr Test Systems) FY 2022: quarters 61.1M vs year 50.8M
- AEHR (Aehr Test Systems) FY 2023: quarters 81.5M vs year 65.0M
- AEHR (Aehr Test Systems) FY 2024: quarters 50.7M vs year 66.2M
- AEHR (Aehr Test Systems) FY 2025: quarters 53.2M vs year 59.0M
- RIME (Algorhythm Holdings, Inc.) FY 2016: quarters 53.0M vs year 48.9M
- RIME (Algorhythm Holdings, Inc.) FY 2017: quarters 61.8M vs year 52.9M
- RIME (Algorhythm Holdings, Inc.) FY 2018: quarters 48.2M vs year 60.8M
- … and 92 more

### Financials · Out of date (49)

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
- … and 37 more

### Financials · Annual figures missing, quarters only (120)

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
- … and 108 more

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
- … and 53 more

### Pay · Name doesn't look like a person (3)

Digits, company words or table labels where a person's name should be.

- JDMT.KA (Janana De Malucho Textile Mills Limited): "Lt. Gen. (Retd.) Ali Kuli Khan Khattak"
- JACK (Jack In The Box Inc): "Martin"
- JACK (Jack In The Box Inc): "Tuckerr"

### EdgarTools · Revenue differs from the annual report (399)

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
- … and 387 more

### EdgarTools · Net income differs from the annual report (65)

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
- … and 53 more

