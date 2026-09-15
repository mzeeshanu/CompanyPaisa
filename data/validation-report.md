# Data validation report — 2026-09-15 20:29 UTC

Checks everything in `companypaisa.db` together, as the website sees it — markets: sec (sec-2026.09.15), uk (uk-2026.09.14), eu (eu-2026.09.15).

**Errors** are values that can't be right. **Warnings** are unusual values worth a look — many are real (big acquisitions, holding-company gains, mega stock grants). Nothing here changes the data.

- Errors: **0**
- Warnings: **663**

## Checks

| Area | Check | Level | Rows |
|---|---|---|---|
| Companies | Currency without an exchange rate | Error | ✓ 0 |
| Companies | No location at all | Error | ✓ 0 |
| Companies | Only a local office, no headquarters | Warning | 1 |
| Companies | Same name under two ids | Warning | 4 |
| Locations | Outside its country | Error | ✓ 0 |
| Locations | Missing city | Warning | ✓ 0 |
| Financials | No figures at all | Error | ✓ 0 |
| Financials | Negative revenue | Warning | 244 |
| Financials | Period in the future | Error | ✓ 0 |
| Financials | Duplicate period | Error | ✓ 0 |
| Financials | Implausibly large revenue | Warning | ✓ 0 |
| Financials | Profit far bigger than revenue | Warning | 78 |
| Financials | Revenue jumps 10× in a year | Warning | 50 |
| Financials | Quarters don't add up to the year | Warning | 70 |
| Financials | Out of date | Warning | 45 |
| Financials | Annual figures missing, quarters only | Warning | 105 |
| Pay | Negative amount | Error | ✓ 0 |
| Pay | Year in the future | Error | ✓ 0 |
| Pay | Person record missing | Error | ✓ 0 |
| Pay | Same person, company and year twice | Error | ✓ 0 |
| Pay | Salary bigger than total | Warning | ✓ 0 |
| Pay | Impossibly large total | Error | ✓ 0 |
| Pay | Very large total | Warning | 66 |
| Pay | Tiny total | Warning | ✓ 0 |
| Pay | Name doesn't look like a person | Warning | ✓ 0 |
| Pay | Pieces don't add up to the total | Warning | ✓ 0 |

## Overview

### Companies by country

| | Count |
|---|---|
| US | 3,834 |
| GB | 328 |
| FR | 180 |
| CA | 146 |
| IT | 139 |
| ES | 93 |
| NL | 71 |
| AU | 16 |
| ? | 1 |

### Companies by exchange

| | Count |
|---|---|
| Nasdaq | 2,163 |
| NYSE | 1,620 |
| LSE | 328 |
| OTC | 208 |
| Euronext Paris | 180 |
| Borsa Italiana | 139 |
| Bolsa de Madrid | 93 |
| Euronext Amsterdam | 71 |
| Unlisted | 4 |
| CBOE | 2 |

### Companies by reporting currency

| | Count |
|---|---|
| USD | 3,993 |
| EUR | 486 |
| GBP | 268 |
| CAD | 51 |
| AUD | 5 |
| GEL | 2 |
| CNY | 1 |
| HKD | 1 |
| MXN | 1 |

### Companies by sector

| | Count |
|---|---|
| Other | 774 |
| Finance | 715 |
| Healthcare | 674 |
| Consumer & retail | 510 |
| Industrials | 498 |
| Software & IT | 347 |
| Energy & utilities | 257 |
| Real estate | 228 |
| Business services | 213 |
| Materials | 154 |
| Technology hardware | 144 |
| Media & telecom | 105 |
| Transportation | 91 |
| Semiconductors | 77 |
| Education | 21 |

### Companies with executive pay, by country

| | Count |
|---|---|
| US | 3,417 |
| GB | 89 |
| CA | 29 |
| AU | 3 |

### Years of annual history per company

| | Count |
|---|---|
| 10+ | 2,450 |
| 6–9 | 1,196 |
| 3–5 | 860 |
| 1–2 | 197 |
| 0 (quarters only) | 105 |

### Newest annual figures

| | Count |
|---|---|
| FY 2025 | 4,014 |
| FY 2026 | 374 |
| FY 2024 | 270 |
| none | 105 |
| 2023 or older | 45 |

### Rows

| | Count |
|---|---|
| Companies | 4,808 |
| Locations | 4,818 |
| Annual periods | 36,862 |
| Quarterly periods | 132,475 |
| Pay rows | 89,740 |
| People | 23,593 |

## Details

### Companies · Only a local office, no headquarters (1)

Expected for hand-added offices of companies based abroad (e.g. NICE in Utah); anything else is a missing headquarters.

- NICE (NICE Ltd.)

### Companies · Same name under two ids (4)

Usually two share classes or a parent and its subsidiary listed separately — one company shown twice.

- DPZ, DOM.L — Dominos Pizza Inc
- FMCB, FMAO — Farmers & Merchants Bancorp
- IPAR, ITP.PA — Interparfums Inc
- IREN, IRE.MI — IREN Ltd

### Financials · Negative revenue (244)

Reported revenue below zero. Real for mortgage REITs and energy producers, whose revenue includes investment or hedging losses; otherwise a sign error.

- AERA (AI Era Corp.) Q2 2021: -51,200
- ALT (Altimmune, Inc.) FY 2022: -68,000
- ANG-PD (American National Group Inc.) Q4 2018: -450,985,000
- ANG-PD (American National Group Inc.) Q1 2020: -323,703,000
- AMLX (Amylyx Pharmaceuticals, Inc.) Q2 2024: -1,023,000
- ABUS (Arbutus Biopharma Corp) Q4 2016: -195,000
- ATH-PA (Athene Holding Ltd.) Q1 2020: -1,549,000,000
- ATH-PA (Athene Holding Ltd.) Q1 2022: -281,000,000
- AUID (authID Inc.) Q3 2025: -106,146
- BMRC (Bank of Marin Bancorp) Q4 2025: -24,816,000
- BENF (Beneficient) FY 2023: -104,903,000
- BENF (Beneficient) FY 2024: -98,696,000
- … and 232 more

### Financials · Profit far bigger than revenue (78)

Net profit more than 3× revenue for a company with ≥ $10M revenue — possible for holding companies and one-off gains, often a revenue sub-line.

- ACTG (Acacia Research Corp) FY 2020: revenue 29.8M, net income 109.2M
- AGIO (Agios Pharmaceuticals, Inc.) FY 2024: revenue 36.5M, net income 674.0M
- ARL (American Realty Investors Inc) FY 2022: revenue 37.5M, net income 373.3M
- AMSC (American Superconductor Corp /De/) FY 2024: revenue 145.6M, net income 6.03bn
- AIV (Apartment Investment & Management Co) FY 2025: revenue 138.5M, net income 547.2M
- APYX (Apyx Medical Corp) FY 2018: revenue 16.7M, net income 62.7M
- ACGP (Associated Capital Group, Inc.) FY 2024: revenue 13.2M, net income 44.3M
- AZTA (Azenta, Inc.) FY 2022: revenue 555.5M, net income 2.13bn
- BDTX (Black Diamond Therapeutics, Inc.) FY 2025: revenue 70.0M, net income 22.37bn
- CPT (Camden Property Trust) FY 2020: revenue 10.8M, net income 123.9M
- CPT (Camden Property Trust) FY 2021: revenue 10.5M, net income 303.9M
- CPT (Camden Property Trust) FY 2025: revenue 13.0M, net income 384.5M
- … and 66 more

### Financials · Revenue jumps 10× in a year (50)

Year-on-year revenue up or down more than tenfold (both years ≥ $10M) — a real acquisition, or a switch of revenue definition or units.

- ABCL (AbCellera Biologics Inc.): FY 2019 11.6M → FY 2020 233.2M
- ABCL (AbCellera Biologics Inc.): FY 2022 485.4M → FY 2023 38.0M
- ACTG (Acacia Research Corp): FY 2018 131.5M → FY 2019 11.2M
- AHR (American Healthcare REIT, Inc.): FY 2018 84.5M → FY 2019 1.10bn
- AMLX (Amylyx Pharmaceuticals, Inc.): FY 2022 22.2M → FY 2023 380.8M
- ARCT (Arcturus Therapeutics Holdings Inc.): FY 2021 12.4M → FY 2022 206.0M
- AD (Array Digital Infrastructure, Inc.): FY 2024 3.67bn → FY 2025 163.0M
- ARWR (Arrowhead Pharmaceuticals, Inc.): FY 2018 16.1M → FY 2019 168.8M
- AAWH (Ascend Wellness Holdings, Inc.): FY 2019 12.0M → FY 2020 143.7M
- CANG (Cango Inc.): FY 2023 1.70bn → FY 2024 110.2M
- DNLI (Denali Therapeutics Inc.): FY 2019 26.7M → FY 2020 335.7M
- DBRG (DigitalBridge Group, Inc.): FY 2018 1.17bn → FY 2019 61.0M
- … and 38 more

### Financials · Quarters don't add up to the year (70)

For December year-ends, the four quarters differ from the annual figure by more than 2% (revenue ≥ $10M). Usually a restatement: the year was restated later (a business sold, an accounting change) while the quarters are as first reported.

- AEHR (Aehr Test Systems) FY 2018: quarters 25.3M vs year 29.6M
- AEHR (Aehr Test Systems) FY 2024: quarters 48.2M vs year 66.2M
- RIME (Algorhythm Holdings, Inc.) FY 2016: quarters 53.0M vs year 48.9M
- RIME (Algorhythm Holdings, Inc.) FY 2017: quarters 61.8M vs year 52.9M
- RIME (Algorhythm Holdings, Inc.) FY 2018: quarters 48.2M vs year 60.8M
- RIME (Algorhythm Holdings, Inc.) FY 2019: quarters 38.4M vs year 46.5M
- RIME (Algorhythm Holdings, Inc.) FY 2020: quarters 43.3M vs year 38.5M
- RIME (Algorhythm Holdings, Inc.) FY 2021: quarters 48.2M vs year 45.8M
- RIME (Algorhythm Holdings, Inc.) FY 2022: quarters 38.7M vs year 47.5M
- RCEL (AVITA Medical, Inc.) FY 2020: quarters 17.9M vs year 14.3M
- BRT (BRT Apartments Corp.) FY 2016: quarters 101.8M vs year 98.5M
- BRT (BRT Apartments Corp.) FY 2017: quarters 108.5M vs year 105.8M
- … and 58 more

### Financials · Out of date (45)

Newest annual figures are more than two years old — the company may have stopped reporting, been taken over, or changed filer.

- AKTX (Akari Therapeutics Plc): latest FY 2023
- AQB (Aquabounty Technologies Inc): latest FY 2023
- ASUR (Asure Software Inc): latest FY 2023
- BESS (Bimergen Energy Corp): latest FY 2023
- BXMT (Blackstone Mortgage Trust, Inc.): latest FY 2023
- CTXR (Citius Pharmaceuticals, Inc.): latest FY 2022
- CIA (Citizens, Inc.): latest FY 2022
- COCP (Cocrystal Pharma, Inc.): latest FY 2020
- DCOY (Decoy Therapeutics Inc.): latest FY 2022
- ESOA (Energy Services of America CORP): latest FY 2022
- GERN (Geron Corp): latest FY 2023
- GGROU (Golden Growers Cooperative): latest FY 2023
- … and 33 more

### Financials · Annual figures missing, quarters only (105)

Shown with quarterly figures only; the headline 'annual revenue' comes from the last four quarters.

- ADIG (Adi Global Distribution Inc.)
- AVEX (AEVEX Corp.)
- AIAI (AIAI Holdings Corp)
- AIB (AIB Data Centers Inc.)
- AKTS (Aktis Oncology, Inc.)
- ALMR (Alamar Biosciences, Inc.)
- AMSS (Amass Brands)
- APMD (Apnimed, Inc.)
- AADX (Applied Aerospace & Defense, Inc.)
- APC (ARKO Petroleum Corp.)
- ARXS (Arxis, Inc.)
- RNA (Atrium Therapeutics, Inc.)
- … and 93 more

### Pay · Very large total (66)

Total above $150M (US-dollar equivalent) in one year — happens (mega stock grants), but check it's not a unit error.

- TSLA (Tesla, Inc.) Elon Musk 2018: 2.28bn
- PLTR (Palantir Technologies Inc.) Alexander Karp 2020: 1.10bn
- FIG (Figma, Inc.) Dylan Field 2025: 864.4M
- TTD (Trade Desk, Inc.) Jeff T. Green 2021: 835.0M
- WELL (Welltower Inc.) Shankh Mitra 2025: 821.1M
- HOOD (Robinhood Markets, Inc.) Vladimir Tenev 2021: 796.1M
- OPEN (Opendoor Technologies Inc.) Kaz Nejatian 2025: 741.1M
- OWL (Blue Owl Capital Inc.) Michael D. Rees 2021: 700.7M
- HOOD (Robinhood Markets, Inc.) Baiju Bhatt 2021: 594.0M
- LCID (Lucid Group, Inc.) Peter Rawlinson 2021: 565.6M
- AFRM (Affirm Holdings, Inc.) Max Levchin 2021: 451.2M
- APO (Apollo Global Management, Inc.) Scott Kleinman 2021: 437.0M
- … and 54 more

