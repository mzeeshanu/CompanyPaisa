# Data validation report — 2026-11-09 16:21 UTC

Checks everything in `companypaisa.db` together, as the website sees it — markets: uk (uk-2026.11.09), eu (eu-2026.11.09), anz (anz-2026.11.09), sec (sec-2026.11.09), pk (pk-2026.09.19).

**Errors** are values that can't be right. **Warnings** are unusual values worth a look — many are real (big acquisitions, holding-company gains, mega stock grants). Nothing here changes the data.

- Errors: **0**
- Warnings: **695**

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
| Financials | Negative revenue | Warning | 247 |
| Financials | Period in the future | Error | ✓ 0 |
| Financials | Duplicate period | Error | ✓ 0 |
| Financials | Implausibly large revenue | Warning | ✓ 0 |
| Financials | Profit far bigger than revenue | Warning | 74 |
| Financials | Revenue jumps 10× in a year | Warning | 54 |
| Financials | Quarters don't add up to the year | Warning | 88 |
| Financials | Out of date | Warning | 49 |
| Financials | Annual figures missing, quarters only | Warning | 108 |
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

## Overview

### Companies by country

| | Count |
|---|---|
| US | 3,820 |
| GB | 338 |
| SE | 296 |
| PK | 228 |
| FR | 193 |
| NO | 151 |
| IT | 144 |
| CA | 141 |
| ES | 106 |
| AU | 92 |
| DK | 84 |
| FI | 83 |
| NL | 73 |
| BE | 68 |
| AT | 50 |
| LT | 17 |
| NZ | 12 |
| SI | 9 |
| LU | 2 |
| ? | 1 |

### Companies by exchange

| | Count |
|---|---|
| Nasdaq | 2,149 |
| NYSE | 1,429 |
| LSE | 338 |
| Nasdaq Stockholm | 296 |
| Pakistan Stock Exchange | 228 |
| OTC | 214 |
| Euronext Paris | 193 |
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
| Cboe | 2 |
| Luxembourg Stock Exchange | 2 |
| Unlisted | 2 |

### Companies by reporting currency

| | Count |
|---|---|
| USD | 4,026 |
| EUR | 776 |
| SEK | 281 |
| GBP | 275 |
| PKR | 228 |
| NOK | 107 |
| AUD | 76 |
| DKK | 72 |
| CAD | 51 |
| NZD | 11 |
| GEL | 2 |
| CNY | 1 |
| HKD | 1 |
| MXN | 1 |

### Companies by sector

| | Count |
|---|---|
| Other | 1,695 |
| Finance | 744 |
| Healthcare | 674 |
| Consumer & retail | 588 |
| Industrials | 511 |
| Software & IT | 358 |
| Energy & utilities | 279 |
| Real estate | 230 |
| Business services | 208 |
| Materials | 193 |
| Technology hardware | 144 |
| Media & telecom | 95 |
| Transportation | 93 |
| Semiconductors | 77 |
| Education | 19 |

### Companies with executive pay, by country

| | Count |
|---|---|
| US | 3,399 |
| PK | 123 |
| GB | 96 |
| CA | 29 |
| AU | 3 |

### Years of annual history per company

| | Count |
|---|---|
| 10+ | 2,433 |
| 6–9 | 1,746 |
| 3–5 | 1,274 |
| 1–2 | 347 |
| 0 (quarters only) | 108 |

### Newest annual figures

| | Count |
|---|---|
| FY 2025 | 4,861 |
| FY 2026 | 609 |
| FY 2024 | 281 |
| none | 108 |
| 2023 or older | 49 |

### Rows

| | Count |
|---|---|
| Companies | 5,908 |
| Locations | 5,918 |
| Annual periods | 41,896 |
| Quarterly periods | 133,665 |
| Pay rows | 88,436 |
| People | 23,151 |

## Details

### Companies · Only a local office, no headquarters (1)

Expected for hand-added offices of companies based abroad (e.g. NICE in Utah); anything else is a missing headquarters.

- NICE (NICE Ltd.)

### Companies · Same name under two ids (5)

Usually two share classes or a parent and its subsidiary listed separately — one company shown twice.

- DOM.L, DPZ — Domino's Pizza
- ARG.PA, AGX — Argan
- ITP.PA, IPAR — Interparfums
- IRE.MI, IREN — Iren S.p.A.
- FMCB, FMAO — Farmers & Merchants Bancorp

### Financials · Negative revenue (247)

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
- … and 235 more

### Financials · Profit far bigger than revenue (74)

Net profit more than 3× revenue for a company with ≥ $10M revenue — possible for holding companies and one-off gains, often a revenue sub-line.

- CNE.L (Capricorn Energy plc) FY 2021: revenue 57.1M, net income 894.5M
- CREI.L (Custodian Property Income REIT plc) FY 2022: revenue 39.9M, net income 122.3M
- LMP.L (LondonMetric Property) FY 2022: revenue 133.2M, net income 734.5M
- NOG.L (Nostrum Oil & Gas plc) FY 2023: revenue 119.6M, net income 831.7M
- SGRO.L (Segro) FY 2020: revenue 432.0M, net income 1.43bn
- SGRO.L (Segro) FY 2021: revenue 546.0M, net income 4.06bn
- STJ.L (St. James's Place) FY 2022: revenue 26.5M, net income 406.8M
- STJ.L (St. James's Place) FY 2024: revenue 25.2M, net income 398.4M
- STJ.L (St. James's Place) FY 2025: revenue 24.2M, net income 531.1M
- AREIT.PA (Altareit) FY 2019: revenue 22.1M, net income 81.0M
- AREIT.PA (Altareit) FY 2020: revenue 16.3M, net income 69.4M
- AREIT.PA (Altareit) FY 2021: revenue 21.9M, net income 72.2M
- … and 62 more

### Financials · Revenue jumps 10× in a year (54)

Year-on-year revenue up or down more than tenfold (both years ≥ $10M) — a real acquisition, or a switch of revenue definition or units.

- CSN.L (Chesnara): FY 2021 1.51bn → FY 2022 33.9M
- ENOG.L (Energean): FY 2020 28.0M → FY 2021 497.0M
- LGEN.L (Legal & General): FY 2021 45.45bn → FY 2022 1.70bn
- RF.PA (Eurazeo): FY 2022 4.64bn → FY 2023 343.7M
- EZE.MC (Grupo Ezentis SA): FY 2021 216.3M → FY 2022 21.6M
- NEWAB.ST (New Wave Group AB): FY 2020 6.10bn → FY 2021 67.19bn
- STB.OL (Storebrand Asa): FY 2021 119.78bn → FY 2022 8.68bn
- ABCL (AbCellera Biologics Inc.): FY 2019 11.6M → FY 2020 233.2M
- ABCL (AbCellera Biologics Inc.): FY 2022 485.4M → FY 2023 38.0M
- ACTG (Acacia Research Corp): FY 2018 131.5M → FY 2019 11.2M
- AHR (American Healthcare REIT, Inc.): FY 2018 84.5M → FY 2019 1.10bn
- AMLX (Amylyx Pharmaceuticals, Inc.): FY 2022 22.2M → FY 2023 380.8M
- … and 42 more

### Financials · Quarters don't add up to the year (88)

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
- … and 76 more

### Financials · Out of date (49)

Newest annual figures are more than two years old — the company may have stopped reporting, been taken over, or changed filer.

- HEMO.L (Hemogenyx Pharmaceuticals plc): latest FY 2022
- ICON.L (Iconic Labs plc): latest FY 2022
- IPO.L (IP Group): latest FY 2021
- GROW.L (Molten Ventures): latest FY 2022
- POLN.L (Pollen Street Group): latest FY 2023
- COIL.L (Roquefort Therapeutics PLC): latest FY 2023
- SVNS.L (Solvonis Therapeutics plc): latest FY 2023
- ERC.AS (ER Capital N.V.): latest FY 2022
- EXO.AS (Exor N.V.): latest FY 2023
- BEVER.AS (N.V. Bever Holding): latest FY 2023
- FCT.MI (Fincantieri S.p.A.): latest FY 2023
- GAB.MI (Gabetti Property Solutions S.p.A.): latest FY 2023
- … and 37 more

### Financials · Annual figures missing, quarters only (108)

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
- RNA (Atrium Therapeutics, Inc.)
- ATTO (Attovia Therapeutics, Inc.)
- … and 96 more

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

- JACK (Jack In The Box Inc): "Martin"
- JACK (Jack In The Box Inc): "Tuckerr"
- JDMT.KA (Janana De Malucho Textile Mills Limited): "Lt. Gen. (Retd.) Ali Kuli Khan Khattak"

