# Data validation report — 2026-09-15 04:24 UTC

Checks every published workbook together, as the website sees it: 
`companypaisa.xlsx`, `companypaisa-uk.xlsx`, `companypaisa-eu.xlsx`.

**Errors** are values that can't be right. **Warnings** are unusual values worth a look — many are real (big acquisitions, holding-company gains, mega stock grants). Nothing here changes the data.

- Errors: **0**
- Warnings: **1302**

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
| Financials | Negative revenue | Warning | 294 |
| Financials | Period in the future | Error | ✓ 0 |
| Financials | Duplicate period | Error | ✓ 0 |
| Financials | Implausibly large revenue | Warning | ✓ 0 |
| Financials | Profit far bigger than revenue | Warning | 81 |
| Financials | Revenue jumps 10× in a year | Warning | 52 |
| Financials | Quarters don't add up to the year | Warning | 537 |
| Financials | Out of date | Warning | 153 |
| Financials | Annual figures missing, quarters only | Warning | 113 |
| Pay | Negative amount | Error | ✓ 0 |
| Pay | Year in the future | Error | ✓ 0 |
| Pay | Person record missing | Error | ✓ 0 |
| Pay | Same person, company and year twice | Error | ✓ 0 |
| Pay | Salary bigger than total | Warning | ✓ 0 |
| Pay | Very large total | Warning | 66 |
| Pay | Tiny total | Warning | ✓ 0 |
| Pay | Name doesn't look like a person | Warning | ✓ 0 |
| Pay | Pieces don't add up to the total | Warning | ✓ 0 |

## Overview

### Companies by country

| | Count |
|---|---|
| US | 3,943 |
| GB | 328 |
| FR | 179 |
| CA | 155 |
| IT | 138 |
| ES | 94 |
| NL | 71 |
| AU | 16 |
| ? | 1 |

### Companies by exchange

| | Count |
|---|---|
| Nasdaq | 2,236 |
| NYSE | 1,658 |
| LSE | 328 |
| OTC | 215 |
| Euronext Paris | 179 |
| Borsa Italiana | 138 |
| Bolsa de Madrid | 94 |
| Euronext Amsterdam | 71 |
| Unlisted | 4 |
| CBOE | 2 |

### Companies by reporting currency

| | Count |
|---|---|
| USD | 4,104 |
| EUR | 487 |
| GBP | 268 |
| CAD | 56 |
| AUD | 5 |
| GEL | 2 |
| CNY | 1 |
| HKD | 1 |
| MXN | 1 |

### Companies by sector

| | Count |
|---|---|
| Other | 774 |
| Healthcare | 739 |
| Finance | 727 |
| Consumer & retail | 512 |
| Industrials | 500 |
| Software & IT | 347 |
| Energy & utilities | 265 |
| Real estate | 242 |
| Business services | 213 |
| Materials | 167 |
| Technology hardware | 144 |
| Media & telecom | 105 |
| Transportation | 92 |
| Semiconductors | 77 |
| Education | 21 |

### Companies with executive pay, by country

| | Count |
|---|---|
| US | 3,505 |
| GB | 89 |
| CA | 29 |
| AU | 3 |

### Years of annual history per company

| | Count |
|---|---|
| 10+ | 2,464 |
| 6–9 | 1,231 |
| 3–5 | 893 |
| 1–2 | 224 |
| 0 (quarters only) | 113 |

### Newest annual figures

| | Count |
|---|---|
| FY 2025 | 4,013 |
| FY 2026 | 374 |
| FY 2024 | 272 |
| 2023 or older | 153 |
| none | 113 |

### Rows

| | Count |
|---|---|
| Companies | 4,925 |
| Locations | 4,935 |
| Annual periods | 37,425 |
| Quarterly periods | 137,906 |
| Pay rows | 91,443 |
| People | 24,041 |

## Details

### Companies · Only a local office, no headquarters (1)

Expected for hand-added offices of companies based abroad (e.g. NICE in Utah); anything else is a missing headquarters.

- NICE (NICE Ltd.)

### Companies · Same name under two ids (5)

Usually two share classes or a parent and its subsidiary listed separately — one company shown twice.

- DPZ, DOM.L — Dominos Pizza Inc
- FMCB, FMAO — Farmers & Merchants Bancorp
- GRFS, GRF.MC — Grifols SA
- IPAR, ITP.PA — Interparfums Inc
- IREN, IRE.MI — IREN Ltd

### Financials · Negative revenue (294)

Reported revenue below zero. Real for mortgage REITs and energy producers, whose revenue includes investment or hedging losses; otherwise a sign error.

- ADAM (Adamas Trust, Inc.) Q1 2020: -411,390,000
- AERA (AI Era Corp.) Q2 2021: -51,200
- ALT (Altimmune, Inc.) FY 2022: -68,000
- ANG-PD (American National Group Inc.) Q4 2018: -450,985,000
- ANG-PD (American National Group Inc.) Q1 2020: -323,703,000
- AMLX (Amylyx Pharmaceuticals, Inc.) Q2 2024: -1,023,000
- AOMR (Angel Oak Mortgage REIT, Inc.) Q1 2022: -26,656,000
- AOMR (Angel Oak Mortgage REIT, Inc.) Q2 2022: -31,565,000
- AOMR (Angel Oak Mortgage REIT, Inc.) Q3 2022: -53,417,000
- AR (ANTERO RESOURCES Corp) Q2 2016: -249,198,000
- ABUS (Arbutus Biopharma Corp) Q4 2016: -195,000
- ARMP (Armata Pharmaceuticals, Inc.) Q3 2013: -3,000
- … and 282 more

### Financials · Profit far bigger than revenue (81)

Net profit more than 3× revenue for a company with ≥ $10M revenue — possible for holding companies and one-off gains, often a revenue sub-line.

- ACTG (Acacia Research Corp) FY 2020: revenue 29.8M, net income 109.2M
- AGIO (Agios Pharmaceuticals, Inc.) FY 2024: revenue 36.5M, net income 674.0M
- ARL (American Realty Investors Inc) FY 2022: revenue 37.5M, net income 373.3M
- AMSC (American Superconductor Corp /De/) FY 2024: revenue 145.6M, net income 6.03bn
- AIV (Apartment Investment & Management Co) FY 2025: revenue 138.5M, net income 547.2M
- APYX (Apyx Medical Corp) FY 2018: revenue 16.7M, net income 62.7M
- ABR (Arbor Realty Trust Inc) FY 2016: revenue 14.9M, net income 62.5M
- ABR (Arbor Realty Trust Inc) FY 2017: revenue 11.0M, net income 97.5M
- ABR (Arbor Realty Trust Inc) FY 2018: revenue 10.1M, net income 148.1M
- ACGP (Associated Capital Group, Inc.) FY 2024: revenue 13.2M, net income 44.3M
- AZTA (Azenta, Inc.) FY 2022: revenue 555.5M, net income 2.13bn
- BDTX (Black Diamond Therapeutics, Inc.) FY 2025: revenue 70.0M, net income 22.37bn
- … and 69 more

### Financials · Revenue jumps 10× in a year (52)

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
- … and 40 more

### Financials · Quarters don't add up to the year (537)

For December year-ends, the four quarters differ from the annual figure by more than 2% (revenue ≥ $10M). Usually a restatement: the year was restated later (a business sold, an accounting change) while the quarters are as first reported.

- ACHC (Acadia Healthcare Company, Inc.) FY 2018: quarters 3.01bn vs year 1.90bn
- AAMI (Acadian Asset Management Inc.) FY 2020: quarters 612.3M vs year 697.9M
- AAMI (Acadian Asset Management Inc.) FY 2021: quarters 539.4M vs year 523.8M
- ADEA (Adeia Inc.) FY 2021: quarters 753.0M vs year 391.2M
- ADT (ADT Inc.) FY 2022: quarters 5.17bn vs year 4.38bn
- AEHR (Aehr Test Systems) FY 2017: quarters 24.3M vs year 18.9M
- AEHR (Aehr Test Systems) FY 2018: quarters 25.3M vs year 29.6M
- AEHR (Aehr Test Systems) FY 2019: quarters 22.8M vs year 21.1M
- AEHR (Aehr Test Systems) FY 2020: quarters 13.6M vs year 22.3M
- AEHR (Aehr Test Systems) FY 2021: quarters 28.2M vs year 16.6M
- AEHR (Aehr Test Systems) FY 2022: quarters 61.1M vs year 50.8M
- AEHR (Aehr Test Systems) FY 2023: quarters 75.9M vs year 65.0M
- … and 525 more

### Financials · Out of date (153)

Newest annual figures are more than two years old — the company may have stopped reporting, been taken over, or changed filer.

- ACHV (Achieve Life Sciences, Inc.): latest FY 2019
- AIXC (AIxCrypto Holdings, Inc.): latest FY 2022
- AKTX (Akari Therapeutics Plc): latest FY 2023
- ALXO (Alx Oncology Holdings Inc): latest FY 2022
- AMH (American Homes 4 Rent): latest FY 2020
- AOMR (Angel Oak Mortgage REIT, Inc.): latest FY 2021
- APVO (Aptevo Therapeutics Inc.): latest FY 2022
- AQB (Aquabounty Technologies Inc): latest FY 2023
- ABR (Arbor Realty Trust Inc): latest FY 2018
- ARMP (Armata Pharmaceuticals, Inc.): latest FY 2014
- ASUR (Asure Software Inc): latest FY 2023
- AVIR (Atea Pharmaceuticals, Inc.): latest FY 2021
- … and 141 more

### Financials · Annual figures missing, quarters only (113)

Shown with quarterly figures only; the headline 'annual revenue' comes from the last four quarters.

- ADAM (Adamas Trust, Inc.)
- ADIG (Adi Global Distribution Inc.)
- AVEX (AEVEX Corp.)
- AIAI (AIAI Holdings Corp)
- AIB (AIB Data Centers Inc.)
- AKTS (Aktis Oncology, Inc.)
- ALMR (Alamar Biosciences, Inc.)
- AMSS (Amass Brands)
- APA (APA Corp)
- APMD (Apnimed, Inc.)
- AADX (Applied Aerospace & Defense, Inc.)
- APC (ARKO Petroleum Corp.)
- … and 101 more

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

