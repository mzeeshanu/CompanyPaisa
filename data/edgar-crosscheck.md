# EdgarTools cross-check — 2026-11-23 01:55 UTC

What the SEC import published (docs/edgartools-plan.md): revenue and net income against the headline lines of each company's latest annual report (10-K, 20-F or 40-F), read by [EdgarTools](https://github.com/dgunning/edgartools) (`tools/edgar/extract.py`). The import uses a report's top line only where it passes the checks; the rest are listed here. A report covers its latest three years.

- Companies read: **3,986** (ok 3,881, no report 82, no figures 23)
- Years in a report but not in our data: **219**

| Measure | Compared | Within 1% | 1–10% | Over 10% |
|---|---|---|---|---|
| Revenue | 9,477 | 9,045 | 24 | 408 |
| Net income | 10,007 | 9,872 | 53 | 82 |

## Revenue: more than 10% apart (408)

| Company | FY | Ours | Annual report | Gap | Currency |
|---|---|---|---|---|---|
| FCNCA (First Citizens Bancshares Inc /De/) | 2023 | 18.79bn | 1.18bn (10-K) | 1496% | USD |
| SLF (Sun Life Financial Inc) | 2025 | 41.90bn | 23.98bn (40-F) | 75% | CAD |
| SLF (Sun Life Financial Inc) | 2024 | 38.80bn | 22.64bn (40-F) | 71% | CAD |
| FCNCA (First Citizens Bancshares Inc /De/) | 2024 | 9.76bn | 1.37bn (10-K) | 611% | USD |
| FCNCA (First Citizens Bancshares Inc /De/) | 2025 | 9.54bn | 1.43bn (10-K) | 570% | USD |
| NTRS (Northern Trust Corp) | 2024 | 8.29bn | 4.73bn (10-K) | 75% | USD |
| NTRS (Northern Trust Corp) | 2025 | 8.09bn | 5.02bn (10-K) | 61% | USD |
| ZION (Zions Bancorporation, National Association /UT/) | 2025 | 3.39bn | 662.0M (10-K) | 411% | USD |
| BPOP (Popular, Inc.) | 2025 | 3.20bn | 558.8M (10-K) | 473% | USD |
| FUBO (FuboTV Inc.) | 2024 | 1.62bn | 4.22bn (10-K) | 62% | USD |
| ZION (Zions Bancorporation, National Association /UT/) | 2023 | 3.12bn | 616.0M (10-K) | 406% | USD |
| ZION (Zions Bancorporation, National Association /UT/) | 2024 | 3.13bn | 639.0M (10-K) | 390% | USD |
| NTRS (Northern Trust Corp) | 2023 | 6.77bn | 4.36bn (10-K) | 55% | USD |
| BPOP (Popular, Inc.) | 2024 | 2.94bn | 540.6M (10-K) | 444% | USD |
| SSB (SouthState Bank Corp) | 2025 | 2.68bn | 322.0M (10-K) | 733% | USD |
| BPOP (Popular, Inc.) | 2023 | 2.78bn | 521.9M (10-K) | 433% | USD |
| COLB (Columbia Banking System, Inc.) | 2025 | 2.30bn | 177.0M (10-K) | 1200% | USD |
| CFR (Cullen/Frost Bankers, Inc.) | 2025 | 2.24bn | 121.6M (10-K) | 1739% | USD |
| CFR (Cullen/Frost Bankers, Inc.) | 2024 | 2.06bn | 106.2M (10-K) | 1843% | USD |
| VLY (Valley National Bancorp) | 2025 | 2.03bn | 124.7M (10-K) | 1525% | USD |
| CFR (Cullen/Frost Bankers, Inc.) | 2023 | 1.99bn | 93.5M (10-K) | 2025% | USD |
| COLB (Columbia Banking System, Inc.) | 2023 | 2.00bn | 134.0M (10-K) | 1390% | USD |
| VLY (Valley National Bancorp) | 2023 | 1.89bn | 85.5M (10-K) | 2113% | USD |
| COLB (Columbia Banking System, Inc.) | 2024 | 1.93bn | 149.0M (10-K) | 1195% | USD |
| VLY (Valley National Bancorp) | 2024 | 1.85bn | 110.9M (10-K) | 1571% | USD |
| CPT (Camden Property Trust) | 2025 | 1.57bn | 13.0M (10-K) | 12035% | USD |
| CPT (Camden Property Trust) | 2023 | 1.54bn | 3.5M (10-K) | 44583% | USD |
| CPT (Camden Property Trust) | 2024 | 1.54bn | 7.1M (10-K) | 21532% | USD |
| SSB (SouthState Bank Corp) | 2023 | 1.74bn | 244.8M (10-K) | 610% | USD |
| SSB (SouthState Bank Corp) | 2024 | 1.72bn | 250.5M (10-K) | 586% | USD |
| ASB (Associated Banc-Corp) | 2025 | 1.49bn | 67.8M (10-K) | 2093% | USD |
| BANC (Banc Of California, Inc.) | 2023 | 298.8M | 1.52bn (10-K) | 80% | USD |
| HWC (Hancock Whitney Corp) | 2025 | 1.52bn | 338.9M (10-K) | 347% | USD |
| GBFH (GBank Financial Holdings Inc.) | 2025 | 76.0M | 1.24bn (10-K) | 94% | USD |
| HWC (Hancock Whitney Corp) | 2024 | 1.45bn | 304.1M (10-K) | 375% | USD |
| UBSI (United Bankshares Inc/Wv) | 2025 | 1.24bn | 103.6M (10-K) | 1094% | USD |
| HWC (Hancock Whitney Corp) | 2023 | 1.39bn | 282.4M (10-K) | 391% | USD |
| BKU (BankUnited, Inc.) | 2025 | 1.09bn | 21.7M (10-K) | 4930% | USD |
| ASB (Associated Banc-Corp) | 2023 | 1.10bn | 62.3M (10-K) | 1670% | USD |
| FULT (Fulton Financial Corp) | 2025 | 1.31bn | 276.8M (10-K) | 374% | USD |
| HOMB (Home Bancshares Inc) | 2025 | 1.09bn | 86.8M (10-K) | 1157% | USD |
| BKU (BankUnited, Inc.) | 2024 | 1.01bn | 20.2M (10-K) | 4910% | USD |
| ASB (Associated Banc-Corp) | 2024 | 1.04bn | 66.4M (10-K) | 1463% | USD |
| UBSI (United Bankshares Inc/Wv) | 2023 | 1.06bn | 109.8M (10-K) | 861% | USD |
| LEGN (Legend Biotech Corp) | 2025 | 1.03bn | 84.1M (20-F) | 1123% | USD |
| FULT (Fulton Financial Corp) | 2024 | 1.24bn | 296.0M (10-K) | 318% | USD |
| BKU (BankUnited, Inc.) | 2023 | 960.6M | 20.9M (10-K) | 4495% | USD |
| HOMB (Home Bancshares Inc) | 2024 | 1.02bn | 82.2M (10-K) | 1137% | USD |
| UBSI (United Bankshares Inc/Wv) | 2024 | 1.03bn | 103.5M (10-K) | 900% | USD |
| HOMB (Home Bancshares Inc) | 2023 | 996.9M | 83.4M (10-K) | 1095% | USD |
| BANC (Banc Of California, Inc.) | 2024 | 1.00bn | 1.89bn (10-K) | 47% | USD |
| FULT (Fulton Financial Corp) | 2023 | 1.08bn | 228.4M (10-K) | 374% | USD |
| CBC (Central Bancompany, Inc.) | 2025 | 1.02bn | 205.9M (10-K) | 396% | USD |
| CATY (Cathay General Bancorp) | 2025 | 817.9M | 31.3M (10-K) | 2516% | USD |
| CATY (Cathay General Bancorp) | 2023 | 810.0M | 23.9M (10-K) | 3284% | USD |
| WAFD (Wafd Inc) | 2026 | 814.7M | 31.9M (10-K) | 2452% | USD |
| MCHB (Mechanics Bancorp) | 2025 | 808.6M | 49.7M (10-K) | 1526% | USD |
| WSFS (Wsfs Financial Corp) | 2023 | 1.01bn | 274.3M (10-K) | 270% | USD |
| LSPD (Lightspeed Commerce Inc.) | 2025 | 1.08bn | 344.8M (40-F) | 212% | USD |
| WSFS (Wsfs Financial Corp) | 2025 | 1.07bn | 335.9M (10-K) | 217% | USD |
| WSFS (Wsfs Financial Corp) | 2024 | 1.05bn | 331.9M (10-K) | 215% | USD |
| CBC (Central Bancompany, Inc.) | 2024 | 897.7M | 195.3M (10-K) | 360% | USD |
| BANC (Banc Of California, Inc.) | 2025 | 1.12bn | 1.82bn (10-K) | 38% | USD |
| CATY (Cathay General Bancorp) | 2024 | 729.7M | 30.6M (10-K) | 2282% | USD |
| IBOC (International Bancshares Corp) | 2025 | 842.2M | 143.8M (10-K) | 486% | USD |
| WAFD (Wafd Inc) | 2025 | 725.5M | 29.7M (10-K) | 2347% | USD |
| WAFD (Wafd Inc) | 2024 | 721.5M | 27.5M (10-K) | 2523% | USD |
| IBOC (International Bancshares Corp) | 2023 | 833.4M | 141.4M (10-K) | 489% | USD |
| IBOC (International Bancshares Corp) | 2024 | 833.6M | 142.7M (10-K) | 484% | USD |
| SFNC (Simmons First National Corp) | 2023 | 805.7M | 129.1M (10-K) | 524% | USD |
| SFNC (Simmons First National Corp) | 2024 | 775.6M | 132.8M (10-K) | 484% | USD |
| BANF (Bancfirst Corp /Ok/) | 2025 | 690.6M | 94.2M (10-K) | 633% | USD |
| BUSE (First Busey Corp /NV/) | 2025 | 719.6M | 131.1M (10-K) | 449% | USD |
| CASH (Pathward Financial, Inc.) | 2025 | 839.9M | 269.3M (10-K) | 212% | USD |
| FRME (First Merchants Corp) | 2025 | 662.9M | 94.5M (10-K) | 602% | USD |
| FRME (First Merchants Corp) | 2023 | 651.0M | 85.8M (10-K) | 659% | USD |
| BKV (BKV Corp) | 2023 | 1.30bn | 739.3M (10-K) | 76% | USD |
| TBBK (Bancorp, Inc.) | 2025 | 703.8M | 141.1M (10-K) | 399% | USD |
| FRME (First Merchants Corp) | 2024 | 646.7M | 90.8M (10-K) | 612% | USD |
| BANF (Bancfirst Corp /Ok/) | 2024 | 631.4M | 91.4M (10-K) | 591% | USD |
| CASH (Pathward Financial, Inc.) | 2024 | 797.4M | 263.8M (10-K) | 202% | USD |
| BBT (Beacon Financial Corp) | 2025 | 553.0M | 33.5M (10-K) | 1551% | USD |
| BANF (Bancfirst Corp /Ok/) | 2023 | 609.9M | 96.2M (10-K) | 534% | USD |
| CVBF (Cvb Financial Corp) | 2023 | 547.3M | 34.4M (10-K) | 1491% | USD |
| MLGO (MicroAlgo Inc.) | 2023 | 81.9M | 580.0M (20-F) | 86% | USD |
| WWW (Wolverine World Wide Inc /De/) | 2023 | 2.24bn | 1.76bn (10-K) | 28% | USD |
| TIPT (Tiptree Inc.) | 2025 | 488.0M | 488,000 (10-K) | 99900% | USD |
| PRK (Park National Corp /Oh/) | 2025 | 557.2M | 70.3M (10-K) | 693% | USD |
| LEGN (Legend Biotech Corp) | 2024 | 627.3M | 144.7M (20-F) | 334% | USD |
| CVBF (Cvb Financial Corp) | 2025 | 515.5M | 37.2M (10-K) | 1286% | USD |
| MLGO (MicroAlgo Inc.) | 2024 | 75.3M | 541.5M (20-F) | 86% | USD |
| CVBF (Cvb Financial Corp) | 2024 | 501.8M | 35.8M (10-K) | 1304% | USD |
| FBK (FB Financial Corp) | 2025 | 560.0M | 96.0M (10-K) | 484% | USD |
| PRK (Park National Corp /Oh/) | 2024 | 520.6M | 63.2M (10-K) | 723% | USD |
| CASH (Pathward Financial, Inc.) | 2023 | 727.7M | 281.8M (10-K) | 158% | USD |
| BKV (BKV Corp) | 2024 | 1.04bn | 604.5M (10-K) | 72% | USD |
| DCOM (Dime Commercial Bancshares, Inc. /NY/) | 2025 | 452.9M | 19.9M (10-K) | 2175% | USD |
| GEF (Greif, Inc) | 2024 | 4.35bn | 3.93bn (10-K) | 11% | USD |
| TBBK (Bancorp, Inc.) | 2024 | 533.8M | 116.8M (10-K) | 357% | USD |
| PRK (Park National Corp /Oh/) | 2023 | 465.7M | 54.2M (10-K) | 759% | USD |
| FBK (FB Financial Corp) | 2023 | 477.8M | 78.4M (10-K) | 509% | USD |
| PEBO (Peoples Bancorp Inc) | 2025 | 459.3M | 63.4M (10-K) | 624% | USD |
| PEBO (Peoples Bancorp Inc) | 2024 | 448.1M | 62.2M (10-K) | 620% | USD |
| OCFC (Oceanfirst Financial Corp) | 2025 | 404.9M | 19.4M (10-K) | 1989% | USD |
| OCFC (Oceanfirst Financial Corp) | 2023 | 403.4M | 23.8M (10-K) | 1596% | USD |
| NBHC (National Bank Holdings Corp) | 2023 | 425.9M | 46.9M (10-K) | 808% | USD |
| NHC (National Healthcare Corp) | 2025 | 1.52bn | 1.14bn (10-K) | 33% | USD |
| TFIN (Triumph Financial, Inc.) | 2025 | 439.0M | 63.9M (10-K) | 587% | USD |
| SRCE (1St Source Corp) | 2025 | 433.8M | 58.8M (10-K) | 637% | USD |
| TFIN (Triumph Financial, Inc.) | 2023 | 418.3M | 45.4M (10-K) | 821% | USD |
| FBNC (First Bancorp /Nc/) | 2023 | 404.1M | 32.8M (10-K) | 1132% | USD |
| FBK (FB Financial Corp) | 2024 | 455.6M | 85.5M (10-K) | 433% | USD |
| NBHC (National Bank Holdings Corp) | 2025 | 416.1M | 47.2M (10-K) | 782% | USD |
| WWW (Wolverine World Wide Inc /De/) | 2025 | 1.87bn | 2.24bn (10-K) | 16% | USD |
| PBHC (Pathfinder Bancorp, Inc.) | 2025 | 372.0M | 3.4M (10-K) | 10695% | USD |
| TCBK (Trico Bancshares /) | 2023 | 418.1M | 50.1M (10-K) | 735% | USD |
| PEBO (Peoples Bancorp Inc) | 2023 | 426.8M | 59.1M (10-K) | 623% | USD |
| RBCAA (Republic Bancorp Inc /Ky/) | 2025 | 417.5M | 50.1M (10-K) | 733% | USD |
| TBBK (Bancorp, Inc.) | 2023 | 466.1M | 99.2M (10-K) | 370% | USD |
| TCBK (Trico Bancshares /) | 2025 | 419.2M | 53.4M (10-K) | 684% | USD |
| TFIN (Triumph Financial, Inc.) | 2024 | 415.9M | 50.5M (10-K) | 724% | USD |
| MLGO (MicroAlgo Inc.) | 2025 | 60.0M | 422.0M (20-F) | 86% | USD |
| OCFC (Oceanfirst Financial Corp) | 2024 | 384.2M | 23.5M (10-K) | 1532% | USD |
| NBHC (National Bank Holdings Corp) | 2024 | 406.6M | 46.2M (10-K) | 781% | USD |
| STBA (S&T Bancorp Inc) | 2023 | 407.0M | 46.6M (10-K) | 773% | USD |
| STBA (S&T Bancorp Inc) | 2025 | 402.1M | 47.2M (10-K) | 752% | USD |
| BBT (Beacon Financial Corp) | 2023 | 371.6M | 18.3M (10-K) | 1934% | USD |
| HOLO (MicroCloud Hologram Inc.) | 2025 | 56.5M | 403.7M (20-F) | 86% | USD |
| BUSE (First Busey Corp /NV/) | 2024 | 462.3M | 116.9M (10-K) | 296% | USD |
| TCBK (Trico Bancshares /) | 2024 | 395.8M | 51.3M (10-K) | 671% | USD |
| OBK (Origin Bancorp, Inc.) | 2025 | 390.8M | 46.8M (10-K) | 736% | USD |
| TIPT (Tiptree Inc.) | 2024 | 341.5M | 1.5M (10-K) | 22365% | USD |
| NIC (Nicolet Bankshares Inc) | 2025 | 392.0M | 52.2M (10-K) | 651% | USD |
| TIPT (Tiptree Inc.) | 2023 | 341.4M | 2.1M (10-K) | 16017% | USD |
| RBCAA (Republic Bancorp Inc /Ky/) | 2024 | 384.8M | 46.2M (10-K) | 733% | USD |
| STBA (S&T Bancorp Inc) | 2024 | 383.9M | 46.8M (10-K) | 720% | USD |
| DCOM (Dime Commercial Bancshares, Inc. /NY/) | 2023 | 352.8M | 16.4M (10-K) | 2046% | USD |
| BBT (Beacon Financial Corp) | 2024 | 355.2M | 18.9M (10-K) | 1776% | USD |
| BUSE (First Busey Corp /NV/) | 2023 | 441.8M | 108.4M (10-K) | 308% | USD |
| MCHB (Mechanics Bancorp) | 2024 | 380.0M | 48.1M (10-K) | 690% | USD |
| QCRH (Qcr Holdings Inc) | 2025 | 369.5M | 37.7M (10-K) | 881% | USD |
| SRCE (1St Source Corp) | 2024 | 387.1M | 57.4M (10-K) | 575% | USD |
| FBNC (First Bancorp /Nc/) | 2025 | 390.3M | 67.6M (10-K) | 477% | USD |
| QCRH (Qcr Holdings Inc) | 2023 | 353.7M | 31.6M (10-K) | 1019% | USD |
| CCB (Coastal Financial Corp) | 2025 | 541.7M | 225.2M (10-K) | 141% | USD |
| SRCE (1St Source Corp) | 2023 | 369.3M | 54.4M (10-K) | 578% | USD |
| OBK (Origin Bancorp, Inc.) | 2023 | 357.9M | 43.9M (10-K) | 715% | USD |
| BFST (Business First Bancshares, Inc.) | 2025 | 324.7M | 10.7M (10-K) | 2933% | USD |
| QCRH (Qcr Holdings Inc) | 2024 | 347.3M | 34.7M (10-K) | 902% | USD |
| OSBC (Old Second Bancorp Inc) | 2025 | 339.3M | 26.8M (10-K) | 1168% | USD |
| RBCAA (Republic Bancorp Inc /Ky/) | 2023 | 360.2M | 47.9M (10-K) | 652% | USD |
| OBK (Origin Bancorp, Inc.) | 2024 | 355.7M | 45.8M (10-K) | 677% | USD |
| GABC (German American Bancorp, Inc.) | 2025 | 361.4M | 51.5M (10-K) | 602% | USD |
| MCB (Metropolitan Bank Holding Corp.) | 2025 | 315.1M | 11.1M (10-K) | 2751% | USD |
| SYBT (Stock Yards Bancorp, Inc.) | 2025 | 397.3M | 94.5M (10-K) | 321% | USD |
| NIC (Nicolet Bankshares Inc) | 2024 | 350.3M | 48.3M (10-K) | 625% | USD |
| ATLC (Atlanticus Holdings Corp) | 2025 | 1.67bn | 1.97bn (10-K) | 15% | USD |
| DCOM (Dime Commercial Bancshares, Inc. /NY/) | 2024 | 314.1M | 16.7M (10-K) | 1778% | USD |
| WABC (Westamerica Bancorporation) | 2023 | 323.6M | 40.1M (10-K) | 706% | USD |
| FBNC (First Bancorp /Nc/) | 2024 | 350.2M | 70.1M (10-K) | 399% | USD |
| CCB (Coastal Financial Corp) | 2024 | 581.2M | 302.7M (10-K) | 92% | USD |
| UVSP (UNIVEST FINANCIAL Corp) | 2025 | 328.1M | 51.6M (10-K) | 536% | USD |
| OSBC (Old Second Bancorp Inc) | 2023 | 286.1M | 21.9M (10-K) | 1206% | USD |
| OSBC (Old Second Bancorp Inc) | 2024 | 285.5M | 23.9M (10-K) | 1095% | USD |
| BFST (Business First Bancshares, Inc.) | 2024 | 271.6M | 10.6M (10-K) | 2468% | USD |
| SYBT (Stock Yards Bancorp, Inc.) | 2024 | 352.3M | 92.6M (10-K) | 280% | USD |
| WABC (Westamerica Bancorporation) | 2024 | 293.8M | 38.6M (10-K) | 661% | USD |
| MCB (Metropolitan Bank Holding Corp.) | 2024 | 276.9M | 23.9M (10-K) | 1057% | USD |
| UVSP (UNIVEST FINANCIAL Corp) | 2023 | 296.8M | 46.0M (10-K) | 545% | USD |
| SYBT (Stock Yards Bancorp, Inc.) | 2023 | 339.6M | 89.5M (10-K) | 279% | USD |
| LEGN (Legend Biotech Corp) | 2023 | 285.1M | 35.3M (20-F) | 708% | USD |
| HOLO (MicroCloud Hologram Inc.) | 2024 | 40.8M | 290.3M (20-F) | 86% | USD |
| UVSP (UNIVEST FINANCIAL Corp) | 2024 | 299.2M | 52.5M (10-K) | 470% | USD |
| BFST (Business First Bancshares, Inc.) | 2023 | 251.8M | 9.7M (10-K) | 2495% | USD |
| CPF (Central Pacific Financial Corp) | 2025 | 292.7M | 51.8M (10-K) | 465% | USD |
| CHCO (City Holding Co) | 2025 | 314.2M | 74.3M (10-K) | 323% | USD |
| RILY (BRC Group Holdings, Inc.) | 2024 | 986.0M | 746.4M (10-K) | 32% | USD |
| CCB (Coastal Financial Corp) | 2023 | 435.7M | 198.2M (10-K) | 120% | USD |
| DDC (DDC Enterprise Ltd) | 2024 | 37.4M | 273.3M (20-F) | 86% | USD |
| BKV (BKV Corp) | 2025 | 1.14bn | 905.5M (10-K) | 26% | USD |
| DDC (DDC Enterprise Ltd) | 2025 | 39.2M | 274.0M (20-F) | 86% | USD |
| NIC (Nicolet Bankshares Inc) | 2023 | 277.5M | 42.7M (10-K) | 550% | USD |
| USO (United States Oil Fund, LP) | 2024 | 231,916 | 231.9M (10-K) | 100% | USD |
| HFWA (Heritage Financial Corp /Wa/) | 2025 | 246.1M | 19.7M (10-K) | 1146% | USD |
| HFWA (Heritage Financial Corp /Wa/) | 2023 | 243.8M | 19.3M (10-K) | 1163% | USD |
| CAC (Camden National Corp) | 2025 | 255.8M | 32.8M (10-K) | 681% | USD |
| MCB (Metropolitan Bank Holding Corp.) | 2023 | 250.7M | 27.9M (10-K) | 799% | USD |
| CHCO (City Holding Co) | 2024 | 293.6M | 72.0M (10-K) | 308% | USD |
| CHCO (City Holding Co) | 2023 | 289.9M | 69.1M (10-K) | 320% | USD |
| WABC (Westamerica Bancorporation) | 2025 | 258.1M | 37.9M (10-K) | 581% | USD |
| MBWM (Mercantile Bank Corp) | 2025 | 242.7M | 23.6M (10-K) | 926% | USD |
| CPF (Central Pacific Financial Corp) | 2024 | 250.5M | 38.7M (10-K) | 547% | USD |
| MPB (Mid Penn Bancorp Inc) | 2025 | 225.9M | 14.6M (10-K) | 1449% | USD |
| CPF (Central Pacific Financial Corp) | 2023 | 256.7M | 46.7M (10-K) | 450% | USD |
| MBWM (Mercantile Bank Corp) | 2024 | 231.5M | 22.7M (10-K) | 918% | USD |
| HBT (HBT Financial, Inc.) | 2025 | 237.1M | 31.0M (10-K) | 665% | USD |
| NRIM (Northrim Bancorp Inc) | 2025 | 212.8M | 7.7M (10-K) | 2678% | USD |
| GABC (German American Bancorp, Inc.) | 2024 | 253.3M | 48.6M (10-K) | 421% | USD |
| MBWM (Mercantile Bank Corp) | 2023 | 225.7M | 21.1M (10-K) | 968% | USD |
| EQBK (Equity Bancshares Inc) | 2024 | 225.0M | 20.9M (10-K) | 975% | USD |
| GABC (German American Bancorp, Inc.) | 2023 | 250.7M | 50.3M (10-K) | 398% | USD |
| CBNK (Capital Bancorp Inc) | 2025 | 245.2M | 45.9M (10-K) | 434% | USD |
| HBT (HBT Financial, Inc.) | 2023 | 227.1M | 28.8M (10-K) | 689% | USD |
| AHR (American Healthcare REIT, Inc.) | 2023 | 1.87bn | 1.67bn (10-K) | 12% | USD |
| HFWA (Heritage Financial Corp /Wa/) | 2024 | 216.8M | 19.0M (10-K) | 1039% | USD |
| HBT (HBT Financial, Inc.) | 2024 | 224.4M | 30.0M (10-K) | 649% | USD |
| EQBK (Equity Bancshares Inc) | 2025 | 210.1M | 21.3M (10-K) | 886% | USD |
| CFFN (Capitol Federal Financial, Inc.) | 2025 | 201.0M | 16.3M (10-K) | 1133% | USD |
| CCBG (Capital City Bank Group Inc) | 2025 | 254.0M | 74.4M (10-K) | 241% | USD |
| DDC (DDC Enterprise Ltd) | 2023 | 28.9M | 205.5M (20-F) | 86% | USD |
| BCAL (California BanCorp \ CA) | 2025 | 180.2M | 4.6M (10-K) | 3843% | USD |
| HOLO (MicroCloud Hologram Inc.) | 2023 | 28.9M | 203.5M (20-F) | 86% | USD |
| CCBG (Capital City Bank Group Inc) | 2023 | 230.6M | 63.0M (10-K) | 266% | USD |
| PFIS (Peoples Financial Services Corp.) | 2025 | 187.7M | 20.1M (10-K) | 832% | USD |
| MPB (Mid Penn Bancorp Inc) | 2024 | 179.2M | 13.2M (10-K) | 1258% | USD |
| NHC (National Healthcare Corp) | 2023 | 1.14bn | 1.31bn (10-K) | 13% | USD |
| CCBG (Capital City Bank Group Inc) | 2024 | 234.9M | 69.5M (10-K) | 238% | USD |
| ATLC (Atlanticus Holdings Corp) | 2024 | 1.15bn | 1.31bn (10-K) | 12% | USD |
| CBNK (Capital Bancorp Inc) | 2024 | 186.2M | 30.3M (10-K) | 514% | USD |
| MPB (Mid Penn Bancorp Inc) | 2023 | 167.0M | 12.7M (10-K) | 1215% | USD |
| CFFN (Capitol Federal Financial, Inc.) | 2024 | 167.3M | 15.3M (10-K) | 993% | USD |
| CAC (Camden National Corp) | 2024 | 177.0M | 28.4M (10-K) | 524% | USD |
| NRIM (Northrim Bancorp Inc) | 2024 | 155.2M | 6.7M (10-K) | 2212% | USD |
| FMNB (Farmers National Banc Corp /Oh/) | 2025 | 188.6M | 46.9M (10-K) | 302% | USD |
| FMNB (Farmers National Banc Corp /Oh/) | 2023 | 179.6M | 39.4M (10-K) | 356% | USD |
| HBCP (Home Bancorp, Inc.) | 2025 | 148.7M | 12.1M (10-K) | 1129% | USD |
| CAC (Camden National Corp) | 2023 | 163.3M | 27.1M (10-K) | 502% | USD |
| ACNB (Acnb Corp) | 2025 | 151.7M | 18.1M (10-K) | 736% | USD |
| FCBC (First Community Bankshares Inc /Va/) | 2023 | 165.1M | 31.8M (10-K) | 419% | USD |
| FCBC (First Community Bankshares Inc /Va/) | 2024 | 165.9M | 32.9M (10-K) | 404% | USD |
| FCBC (First Community Bankshares Inc /Va/) | 2025 | 167.5M | 36.7M (10-K) | 356% | USD |
| WBHC (Wilson Bank Holding Co) | 2025 | 226.8M | 357.5M (10-K) | 37% | USD |
| INBK (First Internet Bancorp) | 2024 | 134.7M | 4.6M (10-K) | 2822% | USD |
| SEAT (Vivid Seats Inc.) | 2024 | 647.9M | 775.6M (10-K) | 16% | USD |
| FMNB (Farmers National Banc Corp /Oh/) | 2024 | 170.1M | 42.5M (10-K) | 300% | USD |
| UNTY (Unity Bancorp Inc /Nj/) | 2025 | 131.8M | 4.5M (10-K) | 2798% | USD |
| WBHC (Wilson Bank Holding Co) | 2024 | 186.3M | 313.1M (10-K) | 41% | USD |
| BCAL (California BanCorp \ CA) | 2024 | 127.7M | 3.1M (10-K) | 3968% | USD |
| NRIM (Northrim Bancorp Inc) | 2023 | 129.6M | 5.9M (10-K) | 2095% | USD |
| HBCP (Home Bancorp, Inc.) | 2023 | 135.3M | 12.0M (10-K) | 1024% | USD |
| HBCP (Home Bancorp, Inc.) | 2024 | 134.9M | 11.6M (10-K) | 1059% | USD |
| MRBK (Meridian Corp) | 2025 | 126.9M | 6.3M (10-K) | 1909% | USD |
| SEAT (Vivid Seats Inc.) | 2025 | 450.5M | 570.8M (10-K) | 21% | USD |
| ALRS (Alerus Financial Corp) | 2024 | 222.0M | 102.6M (10-K) | 116% | USD |
| EQBK (Equity Bancshares Inc) | 2023 | 139.9M | 21.2M (10-K) | 561% | USD |
| PFIS (Peoples Financial Services Corp.) | 2024 | 134.3M | 16.1M (10-K) | 733% | USD |
| ALRS (Alerus Financial Corp) | 2025 | 224.4M | 108.8M (10-K) | 106% | USD |
| SEAT (Vivid Seats Inc.) | 2023 | 597.4M | 712.9M (10-K) | 16% | USD |
| FMAO (Farmers & Merchants Bancorp Inc) | 2025 | 121.4M | 6.4M (10-K) | 1795% | USD |
| INBK (First Internet Bancorp) | 2025 | 116.5M | 4.6M (10-K) | 2416% | USD |
| SFST (Southern First Bancshares Inc) | 2025 | 118.1M | 6.3M (10-K) | 1780% | USD |
| CZNC (Citizens & Northern Corp) | 2025 | 122.7M | 11.2M (10-K) | 991% | USD |
| MRBK (Meridian Corp) | 2024 | 112.3M | 5.7M (10-K) | 1859% | USD |
| CBAN (Colony Bankcorp Inc) | 2025 | 132.2M | 25.7M (10-K) | 415% | USD |
| BWFG (Bankwell Financial Group, Inc.) | 2025 | 108.3M | 2.8M (10-K) | 3733% | USD |
| UNTY (Unity Bancorp Inc /Nj/) | 2024 | 107.1M | 3.6M (10-K) | 2911% | USD |
| BMRC (Bank of Marin Bancorp) | 2023 | 105.3M | 6.6M (10-K) | 1507% | USD |
| INBK (First Internet Bancorp) | 2023 | 101.0M | 3.3M (10-K) | 2964% | USD |
| CZNC (Citizens & Northern Corp) | 2024 | 108.3M | 10.8M (10-K) | 904% | USD |
| NRP (Natural Resource Partners LP) | 2023 | 278.7M | 181.5M (10-K) | 54% | USD |
| ACNB (Acnb Corp) | 2024 | 108.3M | 12.0M (10-K) | 805% | USD |
| NRP (Natural Resource Partners LP) | 2024 | 232.4M | 136.4M (10-K) | 70% | USD |
| ACNB (Acnb Corp) | 2023 | 106.8M | 11.0M (10-K) | 870% | USD |
| FMAO (Farmers & Merchants Bancorp Inc) | 2024 | 101.5M | 5.8M (10-K) | 1651% | USD |
| PLBC (Plumas Bancorp) | 2025 | 98.3M | 3.8M (10-K) | 2505% | USD |
| CZNC (Citizens & Northern Corp) | 2023 | 104.8M | 10.3M (10-K) | 915% | USD |
| CBAN (Colony Bankcorp Inc) | 2024 | 115.5M | 23.7M (10-K) | 387% | USD |
| FMAO (Farmers & Merchants Bancorp Inc) | 2023 | 97.3M | 5.7M (10-K) | 1615% | USD |
| OPBK (OP Bancorp) | 2025 | 94.7M | 3.2M (10-K) | 2855% | USD |
| PFIS (Peoples Financial Services Corp.) | 2023 | 100.9M | 12.2M (10-K) | 726% | USD |
| WTBA (West Bancorporation Inc) | 2025 | 95.2M | 7.3M (10-K) | 1210% | USD |
| SFST (Southern First Bancshares Inc) | 2024 | 93.4M | 5.6M (10-K) | 1579% | USD |
| BWFG (Bankwell Financial Group, Inc.) | 2024 | 87.0M | 2.0M (10-K) | 4332% | USD |
| WBHC (Wilson Bank Holding Co) | 2023 | 167.2M | 250.9M (10-K) | 33% | USD |
| SFST (Southern First Bancshares Inc) | 2023 | 87.5M | 4.0M (10-K) | 2068% | USD |
| FNLC (First Bancorp, Inc /ME/) | 2025 | 94.7M | 13.9M (10-K) | 582% | USD |
| OPBK (OP Bancorp) | 2023 | 82.9M | 2.1M (10-K) | 3803% | USD |
| COSO (CoastalSouth Bancshares, Inc.) | 2025 | 82.0M | 2.0M (10-K) | 4029% | USD |
| NWFL (Norwood Financial Corp) | 2025 | 87.9M | 8.0M (10-K) | 999% | USD |
| OPBK (OP Bancorp) | 2024 | 82.0M | 3.3M (10-K) | 2416% | USD |
| PLBC (Plumas Bancorp) | 2024 | 82.5M | 3.7M (10-K) | 2103% | USD |
| OVLY (Oak Valley Bancorp) | 2025 | 81.7M | 3.5M (10-K) | 2241% | USD |
| FCCO (First Community Corp /Sc/) | 2025 | 79.0M | 922,000 (10-K) | 8465% | USD |
| MNSB (MainStreet Bancshares, Inc.) | 2023 | 80.1M | 2.1M (10-K) | 3626% | USD |
| CHMG (Chemung Financial Corp) | 2024 | 97.3M | 20.0M (10-K) | 385% | USD |
| PLBC (Plumas Bancorp) | 2023 | 80.5M | 3.7M (10-K) | 2099% | USD |
| FXNC (First National Corp /Va/) | 2025 | 90.3M | 14.3M (10-K) | 532% | USD |
| LCNB (Lcnb Corp) | 2025 | 92.0M | 16.9M (10-K) | 444% | USD |
| FDBC (Fidelity D & D Bancorp Inc) | 2025 | 93.2M | 18.4M (10-K) | 408% | USD |
| CHMG (Chemung Financial Corp) | 2025 | 95.1M | 20.7M (10-K) | 360% | USD |
| TSBK (Timberland Bancorp Inc) | 2025 | 82.6M | 9.1M (10-K) | 805% | USD |
| OVLY (Oak Valley Bancorp) | 2024 | 76.6M | 3.5M (10-K) | 2119% | USD |
| WTBA (West Bancorporation Inc) | 2024 | 79.8M | 7.2M (10-K) | 1007% | USD |
| HNVR (Hanover Bancorp, Inc. /MD) | 2025 | 73.3M | 750,000 (10-K) | 9676% | USD |
| WTBA (West Bancorporation Inc) | 2023 | 79.1M | 6.9M (10-K) | 1045% | USD |
| MNSB (MainStreet Bancshares, Inc.) | 2025 | 73.6M | 2.2M (10-K) | 3269% | USD |
| ALRS (Alerus Financial Corp) | 2023 | 168.1M | 96.8M (10-K) | 74% | USD |
| HWBK (Hawthorn Bancshares, Inc.) | 2025 | 80.2M | 9.8M (10-K) | 721% | USD |
| TSBK (Timberland Bancorp Inc) | 2023 | 79.5M | 9.1M (10-K) | 771% | USD |
| FNRN (First Northern Community Bancorp) | 2025 | 73.6M | 4.4M (10-K) | 1574% | USD |
| COSO (CoastalSouth Bancshares, Inc.) | 2024 | 69.8M | 1.9M (10-K) | 3645% | USD |
| HNVR (Hanover Bancorp, Inc. /MD) | 2024 | 68.4M | 469,000 (10-K) | 14491% | USD |
| FNLC (First Bancorp, Inc /ME/) | 2023 | 80.6M | 12.7M (10-K) | 533% | USD |
| FNLC (First Bancorp, Inc /ME/) | 2024 | 80.3M | 13.3M (10-K) | 505% | USD |
| BTCK (Teucrium Commodity Trust) | 2023 | -63.8M | -130.4M (10-K) | 51% | USD |
| TSBK (Timberland Bancorp Inc) | 2024 | 75.3M | 9.3M (10-K) | 713% | USD |
| LCNB (Lcnb Corp) | 2024 | 81.2M | 15.2M (10-K) | 434% | USD |
| FNRN (First Northern Community Bancorp) | 2024 | 70.4M | 4.5M (10-K) | 1471% | USD |
| COFS (Choiceone Financial Services Inc) | 2025 | 161.7M | 227.6M (10-K) | 29% | USD |
| FCCO (First Community Corp /Sc/) | 2024 | 66.0M | 952,000 (10-K) | 6837% | USD |
| FDBC (Fidelity D & D Bancorp Inc) | 2024 | 80.9M | 16.6M (10-K) | 389% | USD |
| MNSB (MainStreet Bancshares, Inc.) | 2024 | 65.8M | 2.0M (10-K) | 3198% | USD |
| HWBK (Hawthorn Bancshares, Inc.) | 2024 | 72.9M | 9.1M (10-K) | 699% | USD |
| BMRC (Bank of Marin Bancorp) | 2024 | 70.2M | 6.6M (10-K) | 963% | USD |
| EBMT (Eagle Bancorp Montana, Inc.) | 2025 | 91.6M | 31.0M (10-K) | 195% | USD |
| LCNB (Lcnb Corp) | 2023 | 71.8M | 12.9M (10-K) | 454% | USD |
| FCCO (First Community Corp /Sc/) | 2023 | 59.3M | 963,000 (10-K) | 6059% | USD |
| HWBK (Hawthorn Bancshares, Inc.) | 2023 | 66.7M | 8.4M (10-K) | 698% | USD |
| ATLO (Ames National Corp) | 2025 | 66.8M | 9.3M (10-K) | 622% | USD |
| USO (United States Oil Fund, LP) | 2025 | 320,800 | -56.2M (10-K) | 101% | USD |
| NWPP (New Peoples Bankshares Inc) | 2025 | 58.5M | 3.6M (10-K) | 1515% | USD |
| FXNC (First National Corp /Va/) | 2024 | 68.8M | 14.2M (10-K) | 385% | USD |
| SFDL (Security Federal Corp) | 2025 | 58.7M | 4.5M (10-K) | 1201% | USD |
| EBMT (Eagle Bancorp Montana, Inc.) | 2024 | 81.2M | 27.2M (10-K) | 198% | USD |
| CBNA (Chain Bridge Bancorp Inc) | 2025 | 55.0M | 2.4M (10-K) | 2213% | USD |
| NWPP (New Peoples Bankshares Inc) | 2024 | 55.9M | 3.8M (10-K) | 1356% | USD |
| STLE (Steele Bancorp Inc) | 2025 | 54.5M | 3.3M (10-K) | 1562% | USD |
| CBNA (Chain Bridge Bancorp Inc) | 2024 | 53.0M | 2.3M (10-K) | 2190% | USD |
| RBKB (Rhinebeck Bancorp, Inc.) | 2025 | 53.4M | 3.0M (10-K) | 1688% | USD |
| TYFG (Tri-County Financial Group, Inc.) | 2025 | 67.3M | 17.2M (10-K) | 291% | USD |
| CBFV (CB Financial Services, Inc.) | 2024 | 51.6M | 1.9M (10-K) | 2562% | USD |
| SLBK (Skyline Bankshares, Inc.) | 2025 | 58.2M | 9.4M (10-K) | 517% | USD |
| SFDL (Security Federal Corp) | 2024 | 52.1M | 4.4M (10-K) | 1080% | USD |
| NKSH (National Bankshares Inc) | 2025 | 55.6M | 8.2M (10-K) | 580% | USD |
| COFS (Choiceone Financial Services Inc) | 2024 | 92.4M | 139.1M (10-K) | 34% | USD |
| ATLO (Ames National Corp) | 2024 | 54.8M | 8.4M (10-K) | 553% | USD |
| PBHC (Pathfinder Bancorp, Inc.) | 2024 | 50.6M | 4.2M (10-K) | 1114% | USD |
| ASRV (Ameriserv Financial Inc /Pa/) | 2025 | 59.3M | 14.6M (10-K) | 305% | USD |
| CSBB (CSB Bancorp, Inc.) | 2025 | 49.7M | 5.2M (10-K) | 849% | USD |
| BOTJ (Bank Of The James Financial Group Inc) | 2025 | 48.7M | 4.3M (10-K) | 1039% | USD |
| NWFL (Norwood Financial Corp) | 2024 | 51.0M | 7.4M (10-K) | 590% | USD |
| FCAP (First Capital Inc) | 2025 | 50.6M | 7.2M (10-K) | 606% | USD |
| TYFG (Tri-County Financial Group, Inc.) | 2024 | 58.5M | 15.6M (10-K) | 274% | USD |
| BTCK (Teucrium Commodity Trust) | 2024 | -39.4M | -81.2M (10-K) | 51% | USD |
| FKYS (First Keystone Corp) | 2025 | 45.0M | 3.4M (10-K) | 1210% | USD |
| CBFV (CB Financial Services, Inc.) | 2025 | 43.5M | 2.4M (10-K) | 1688% | USD |
| CBKM (Consumers Bancorp Inc /Oh/) | 2026 | 45.8M | 4.9M (10-K) | 833% | USD |
| BOTJ (Bank Of The James Financial Group Inc) | 2024 | 44.4M | 4.0M (10-K) | 1008% | USD |
| FUSB (First US Bancshares, Inc.) | 2025 | 41.0M | 1.1M (10-K) | 3500% | USD |
| CSBB (CSB Bancorp, Inc.) | 2024 | 44.0M | 5.1M (10-K) | 756% | USD |
| RVSB (Riverview Bancorp Inc) | 2025 | 50.6M | 11.9M (10-K) | 325% | USD |
| FUSB (First US Bancshares, Inc.) | 2024 | 39.7M | 1.2M (10-K) | 3125% | USD |
| ASRV (Ameriserv Financial Inc /Pa/) | 2024 | 54.0M | 15.6M (10-K) | 246% | USD |
| SLBK (Skyline Bankshares, Inc.) | 2024 | 46.8M | 8.7M (10-K) | 435% | USD |
| NKSH (National Bankshares Inc) | 2024 | 45.4M | 7.4M (10-K) | 509% | USD |
| PROV (Provident Financial Holdings Inc) | 2026 | 40.1M | 2.3M (10-K) | 1664% | USD |
| RVSB (Riverview Bancorp Inc) | 2024 | 48.3M | 11.6M (10-K) | 317% | USD |
| PROV (Provident Financial Holdings Inc) | 2025 | 39.0M | 2.4M (10-K) | 1541% | USD |
| FCAP (First Capital Inc) | 2024 | 43.4M | 7.1M (10-K) | 515% | USD |
| CFFN (Capitol Federal Financial, Inc.) | 2023 | -18.2M | 17.6M (10-K) | 203% | USD |
| FKYS (First Keystone Corp) | 2024 | 39.0M | 3.3M (10-K) | 1082% | USD |
| ELMES (Elme Communities) | 2024 | 241.9M | 206.4M (10-K) | 17% | USD |
| USO (United States Oil Fund, LP) | 2023 | 374,000 | -34.9M (10-K) | 101% | USD |
| FCAP (First Capital Inc) | 2023 | 42.2M | 7.0M (10-K) | 502% | USD |
| SFNC (Simmons First National Corp) | 2025 | 103.2M | 138.3M (10-K) | 25% | USD |
| CBKM (Consumers Bancorp Inc /Oh/) | 2025 | 39.6M | 4.6M (10-K) | 757% | USD |
| BYFC (Broadway Financial Corp \De\) | 2025 | 34.9M | 184,000 (10-K) | 18867% | USD |
| BYFC (Broadway Financial Corp \De\) | 2024 | 33.3M | 155,000 (10-K) | 21399% | USD |
| COFS (Choiceone Financial Services Inc) | 2023 | 80.8M | 113.9M (10-K) | 29% | USD |
| ECBK (ECB Bancorp, Inc. /MD/) | 2025 | 33.2M | 598,000 (10-K) | 5459% | USD |
| SRBK (SR Bancorp, Inc.) | 2026 | 33.4M | 1.0M (10-K) | 3151% | USD |
| SRBK (SR Bancorp, Inc.) | 2025 | 33.3M | 1.0M (10-K) | 3106% | USD |
| AUBN (Auburn National Bancorporation, Inc) | 2025 | 32.8M | 619,000 (10-K) | 5198% | USD |
| WETO (Wetour Robotics Ltd) | 2025 | 5.0M | 35.6M (20-F) | 86% | USD |
| RVSB (Riverview Bancorp Inc) | 2026 | 43.1M | 12.5M (10-K) | 245% | USD |
| AUBN (Auburn National Bancorporation, Inc) | 2024 | 30.6M | 614,000 (10-K) | 4884% | USD |
| JUVF (Juniata Valley Financial Corp) | 2025 | 31.1M | 5.0M (10-K) | 517% | USD |
| RBKB (Rhinebeck Bancorp, Inc.) | 2024 | 28.7M | 3.0M (10-K) | 856% | USD |
| ECBK (ECB Bancorp, Inc. /MD/) | 2024 | 26.2M | 577,000 (10-K) | 4443% | USD |
| FMFG (Farmers & Merchants Bancshares, Inc.) | 2025 | 26.4M | 1.1M (10-K) | 2207% | USD |
| QNTO (Quaint Oak Bancorp, Inc.) | 2024 | 26.0M | 1.7M (10-K) | 1467% | USD |
| QNTO (Quaint Oak Bancorp, Inc.) | 2025 | 25.1M | 1.0M (10-K) | 2321% | USD |
| JUVF (Juniata Valley Financial Corp) | 2024 | 28.8M | 5.1M (10-K) | 468% | USD |
| BMRC (Bank of Marin Bancorp) | 2025 | 29.4M | 6.5M (10-K) | 353% | USD |
| FMFG (Farmers & Merchants Bancshares, Inc.) | 2024 | 22.6M | 1.1M (10-K) | 1899% | USD |
| USAS (Americas Gold & Silver Corp) | 2024 | 120.9M | 100.2M (40-F) | 21% | USD |
| RYM (RYTHM, Inc.) | 2024 | 18.0M | 18,000 (10-K) | 99900% | USD |
| STLE (Steele Bancorp Inc) | 2024 | 18.6M | 1.5M (10-K) | 1106% | USD |
| FDSB (Fifth District Bancorp, Inc.) | 2025 | 17.3M | 605,000 (10-K) | 2756% | USD |
| UNL (United States 12 Month Natural Gas Fund, LP) | 2023 | 6,650 | -13.1M (10-K) | 100% | USD |
| WETO (Wetour Robotics Ltd) | 2026 | 2.0M | 13.3M (20-F) | 85% | USD |
| FDSB (Fifth District Bancorp, Inc.) | 2024 | 10.1M | 619,000 (10-K) | 1526% | USD |
| SNNF (Seneca Bancorp, Inc.) | 2025 | 11.1M | 2.0M (10-K) | 467% | USD |
| ESGH (ESG Inc.) | 2024 | 21.4M | 12.7M (10-K) | 69% | USD |
| SNNF (Seneca Bancorp, Inc.) | 2024 | 10.0M | 1.7M (10-K) | 472% | USD |
| NWGL (CL Workshop Group Ltd) | 2023 | 25.5M | 17.7M (20-F) | 44% | USD |
| NTWK (Netsol Technologies Inc) | 2025 | 73.1M | 66.1M (10-K) | 11% | USD |
| PFSB (PFS Bancorp, Inc.) | 2025 | 6.6M | 481,000 (10-K) | 1266% | USD |
| PFSB (PFS Bancorp, Inc.) | 2024 | 5.9M | 473,000 (10-K) | 1151% | USD |
| NWGL (CL Workshop Group Ltd) | 2024 | 21.5M | 16.3M (20-F) | 32% | USD |
| NCRA (Nocera, Inc.) | 2025 | 11.0M | 13.6M (10-K) | 19% | USD |
| ESGH (ESG Inc.) | 2025 | 7.5M | 6.1M (10-K) | 23% | USD |
| UNL (United States 12 Month Natural Gas Fund, LP) | 2024 | 11,200 | -925,374 (10-K) | 101% | USD |
| UNL (United States 12 Month Natural Gas Fund, LP) | 2025 | 8,050 | -735,860 (10-K) | 101% | USD |
| ABVC (Abvc Biopharma, Inc.) | 2025 | 296,000 | 31,779 (10-K) | 831% | USD |
| NRXP (NRX Pharmaceuticals, Inc.) | 2025 | 1.4M | 1.2M (10-K) | 18% | USD |
| ATLX (Atlas Lithium Corp) | 2024 | 748,654 | 667,131 (10-K) | 12% | USD |
| ATLX (Atlas Lithium Corp) | 2025 | 121,388 | 92,491 (10-K) | 31% | USD |

## Net income: more than 10% apart (82)

| Company | FY | Ours | Annual report | Gap | Currency |
|---|---|---|---|---|---|
| SRE (Sempra) | 2024 | 2.86bn | 3.50bn (10-K) | 18% | USD |
| GEF (Greif, Inc) | 2024 | 268.8M | 840.0M (10-K) | 68% | USD |
| SRE (Sempra) | 2023 | 3.08bn | 3.62bn (10-K) | 15% | USD |
| ETR (Entergy Corp /De/) | 2023 | 2.36bn | 1.86bn (10-K) | 26% | USD |
| OPTU (Optimum Communications, Inc.) | 2025 | -1.44bn | -1.87bn (10-K) | 23% | USD |
| ETR (Entergy Corp /De/) | 2024 | 1.06bn | 1.47bn (10-K) | 28% | USD |
| DDC (DDC Enterprise Ltd) | 2025 | -48.3M | -338.0M (20-F) | 86% | USD |
| SRE (Sempra) | 2025 | 1.84bn | 2.07bn (10-K) | 11% | USD |
| MLGO (MicroAlgo Inc.) | 2023 | -37.6M | -266.2M (20-F) | 86% | USD |
| IVZ (Invesco Ltd.) | 2024 | 538.0M | 752.4M (10-K) | 28% | USD |
| CMP (Compass Minerals International Inc) | 2024 | -206.1M | 0 (10-K) | 100% | USD |
| IVZ (Invesco Ltd.) | 2023 | -333.7M | -168.2M (10-K) | 98% | USD |
| PNFP (Pinnacle Financial Partners, Inc.) | 2025 | 792.0M | 641.9M (10-K) | 23% | USD |
| ELMES (Elme Communities) | 2024 | -13.1M | -154.2M (10-K) | 92% | USD |
| DDC (DDC Enterprise Ltd) | 2023 | -22.8M | -162.0M (20-F) | 86% | USD |
| DDC (DDC Enterprise Ltd) | 2024 | -21.5M | -157.0M (20-F) | 86% | USD |
| WWW (Wolverine World Wide Inc /De/) | 2025 | 95.8M | -38.5M (10-K) | 349% | USD |
| EE (Excelerate Energy, Inc.) | 2025 | 39.2M | 167.0M (10-K) | 77% | USD |
| EE (Excelerate Energy, Inc.) | 2024 | 32.9M | 153.0M (10-K) | 79% | USD |
| MAGN (Magnera Corp) | 2023 | -79.1M | 38.0M (10-K) | 308% | USD |
| MLGO (MicroAlgo Inc.) | 2025 | 18.1M | 127.6M (20-F) | 86% | USD |
| EE (Excelerate Energy, Inc.) | 2023 | 30.4M | 126.8M (10-K) | 76% | USD |
| WWW (Wolverine World Wide Inc /De/) | 2023 | -38.5M | 45.2M (10-K) | 185% | USD |
| BALY (Bally's Corp) | 2024 | -567.8M | -650.1M (10-K) | 13% | USD |
| CMP (Compass Minerals International Inc) | 2025 | -79.8M | 0 (10-K) | 100% | USD |
| EVC (Entravision Communications Corp) | 2024 | -148.9M | -70.3M (10-K) | 112% | USD |
| HOLO (MicroCloud Hologram Inc.) | 2023 | -11.6M | -81.6M (20-F) | 86% | USD |
| ITXP (Independence Power Holdings, Inc.) | 2025 | -25,323 | 68.9M (10-K) | 100% | USD |
| POWW (Outdoor Holding Co) | 2025 | -130.8M | -65.2M (10-K) | 101% | USD |
| CHRN (ChronoScale Holdings Corp) | 2025 | -11.7M | -72.7M (10-K) | 84% | USD |
| INFQ (Infleqtion, Inc.) | 2024 | -55.7M | -51,910 (10-K) | 107282% | USD |
| HOLO (MicroCloud Hologram Inc.) | 2024 | -9.0M | -64.2M (20-F) | 86% | USD |
| NHC (National Healthcare Corp) | 2025 | 120.0M | 66.8M (10-K) | 80% | USD |
| CLF (Cleveland-Cliffs Inc.) | 2023 | 385.0M | 436.0M (10-K) | 12% | USD |
| WWW (Wolverine World Wide Inc /De/) | 2024 | 45.2M | 95.8M (10-K) | 53% | USD |
| MLGO (MicroAlgo Inc.) | 2024 | 7.3M | 52.4M (20-F) | 86% | USD |
| HOLO (MicroCloud Hologram Inc.) | 2025 | -7.0M | -50.2M (20-F) | 86% | USD |
| SRXH (SRX Global Inc.) | 2024 | -168,000 | -43.1M (10-K) | 100% | USD |
| ELMES (Elme Communities) | 2023 | -53.0M | -13.1M (10-K) | 304% | USD |
| LEVI (Levi Strauss & Co) | 2023 | 249.6M | 210.6M (10-K) | 19% | USD |
| LEVI (Levi Strauss & Co) | 2024 | 210.6M | 249.6M (10-K) | 16% | USD |
| NHC (National Healthcare Corp) | 2023 | 66.8M | 101.9M (10-K) | 34% | USD |
| RR (Richtech Robotics Inc.) | 2025 | -49.1M | -15.8M (10-K) | 212% | USD |
| APOG (Apogee Enterprises, Inc.) | 2025 | 54.1M | 85.1M (10-K) | 36% | USD |
| WETO (Wetour Robotics Ltd) | 2026 | -4.6M | -31.1M (20-F) | 85% | USD |
| NMRK (Newmark Group, Inc.) | 2024 | 85.5M | 61.2M (10-K) | 40% | USD |
| NMRK (Newmark Group, Inc.) | 2023 | 62.4M | 42.6M (10-K) | 47% | USD |
| EVC (Entravision Communications Corp) | 2023 | -15.4M | -33.1M (10-K) | 53% | USD |
| REX (REX AMERICAN RESOURCES Corp) | 2023 | 60.9M | 75.9M (10-K) | 20% | USD |
| SMA (SmartStop Self Storage REIT, Inc.) | 2023 | -2.7M | 11.6M (10-K) | 124% | USD |
| CMP (Compass Minerals International Inc) | 2023 | 10.5M | -3.1M (10-K) | 439% | USD |
| SMC (Summit Midstream Corp) | 2023 | -51.5M | -38.9M (10-K) | 32% | USD |
| SMA (SmartStop Self Storage REIT, Inc.) | 2024 | -18.4M | -5.9M (10-K) | 212% | USD |
| AMCX (AMC Global Media Inc.) | 2025 | 89.4M | 101.8M (10-K) | 12% | USD |
| WETO (Wetour Robotics Ltd) | 2025 | -1.7M | -12.5M (20-F) | 86% | USD |
| TTEC (TTEC Holdings, Inc.) | 2023 | 8.4M | 18.3M (10-K) | 54% | USD |
| DCO (Ducommun Inc /De/) | 2024 | 21.7M | 31.5M (10-K) | 31% | USD |
| NXPL (NextPlat Corp) | 2024 | -14.0M | -22.5M (10-K) | 38% | USD |
| GLTK (GlobalTech Corp) | 2025 | -11.6M | -3.1M (10-K) | 267% | USD |
| SMA (SmartStop Self Storage REIT, Inc.) | 2025 | -8.8M | -1.7M (10-K) | 404% | USD |
| RR (Richtech Robotics Inc.) | 2024 | -1.4M | -8.1M (10-K) | 83% | USD |
| GPUS (Hyperscale Data, Inc.) | 2024 | -56.2M | -62.5M (10-K) | 10% | USD |
| SMC (Summit Midstream Corp) | 2025 | -5.9M | -1.9M (10-K) | 212% | USD |
| DCO (Ducommun Inc /De/) | 2025 | -37.4M | -33.9M (10-K) | 10% | USD |
| SLSN (Solesence, Inc.) | 2024 | 1.1M | 4.2M (10-K) | 74% | USD |
| KIQSF (Kelso Technologies Inc) | 2024 | -4.6M | -1.7M (20-F) | 180% | USD |
| GEG (Great Elm Group, Inc.) | 2025 | 12.9M | 15.6M (10-K) | 17% | USD |
| EONR (EON Resources Inc.) | 2025 | -7.5M | -10.1M (10-K) | 26% | USD |
| KIQSF (Kelso Technologies Inc) | 2023 | -2.1M | -118,097 (20-F) | 1680% | USD |
| NABL (N-able, Inc.) | 2025 | -18.8M | -17.0M (10-K) | 10% | USD |
| STSR (Strategic Student & Senior Housing Trust, Inc.) | 2025 | -5.8M | -4.3M (10-K) | 35% | USD |
| POWW (Outdoor Holding Co) | 2026 | -3.5M | -4.9M (10-K) | 28% | USD |
| CAPN (Cayson Acquisition Corp) | 2025 | 3.0M | 1.6M (10-K) | 85% | USD |
| STSR (Strategic Student & Senior Housing Trust, Inc.) | 2023 | -11.1M | -9.8M (10-K) | 14% | USD |
| QETA (Quetta Acquisition Corp) | 2025 | 323,454 | -780,924 (10-K) | 141% | USD |
| SLSN (Solesence, Inc.) | 2025 | 2.8M | 1.8M (10-K) | 57% | USD |
| CRMZ (Creditriskmonitor Com Inc) | 2025 | 511,161 | 1.0M (10-K) | 50% | USD |
| CRMZ (Creditriskmonitor Com Inc) | 2024 | 1.3M | 1.7M (10-K) | 22% | USD |
| FDCT (Fdctech, Inc.) | 2024 | 247,544 | -18,781 (10-K) | 1418% | USD |
| TRT (Trio-Tech International) | 2026 | -34,000 | 219,000 (10-K) | 116% | USD |
| KIQSF (Kelso Technologies Inc) | 2025 | 447,397 | 547,157 (20-F) | 18% | USD |
| TRT (Trio-Tech International) | 2025 | -41,000 | 0 (10-K) | 100% | USD |

## Years only the report has (144 companies)

A year missing from our data: a report newer than our last import, or a year our consistency rules dropped.

- AARD (Aardvark Therapeutics, Inc.): FY 2024
- ABEO (Abeona Therapeutics Inc.): FY 2024
- ACB (Aurora Cannabis Inc): FY 2026
- ACOG (Alpha Cognition Inc.): FY 2024
- ADVB (Advanced Biomed Inc.): FY 2025
- AEMD (Aethlon Medical Inc): FY 2025, 2026
- AGQ (ProShares Trust II): FY 2023, 2024, 2025
- AIB (AIB Data Centers Inc.): FY 2025
- AKTX (Akari Therapeutics Plc): FY 2024, 2025
- ALMS (Alumis Inc.): FY 2024
- AMOD (Alpha Modus Holdings, Inc.): FY 2024
- ANIX (Anixa Biosciences Inc): FY 2025
- APLM (Apollomics Inc.): FY 2025
- APUS (Apimeds Pharmaceuticals US, Inc.): FY 2024, 2025
- AQB (Aquabounty Technologies Inc): FY 2024, 2025
- AQMS (Aqua Metals, Inc.): FY 2025
- ARAI (Arrive AI Inc.): FY 2024
- ASBP (Aspire-Lakewood Holdings, Inc.): FY 2024
- ASUR (Asure Software Inc): FY 2024, 2025
- ATHE (Alterity Therapeutics Ltd): FY 2026
- ATNM (Actinium Pharmaceuticals, Inc.): FY 2024
- AVBP (ArriVent BioPharma, Inc.): FY 2024, 2025
- BESS (Bimergen Energy Corp): FY 2024, 2025
- BFRG (BullFrog AI Holdings, Inc.): FY 2024
- BNO (United States Brent Oil Fund, LP): FY 2023, 2024, 2025
- BNTC (Benitec Biopharma Inc.): FY 2025, 2026
- BTCK (Teucrium Commodity Trust): FY 2025
- BTCO (Invesco Galaxy Bitcoin ETF): FY 2025
- BXBL (BOXABL Inc.): FY 2025
- BXMT (Blackstone Mortgage Trust, Inc.): FY 2024, 2025
- CHGA (Change Agents Corporation.): FY 2025
- CMS-PB (Consumers Energy Co): FY 2023, 2024, 2025
- CNTN (Canton Strategic Holdings, Inc.): FY 2024, 2025
- COCP (Cocrystal Pharma, Inc.): FY 2024, 2025
- COOT (Australian Oilseeds Holdings Ltd): FY 2026
- CTGO (Contango Silver & Gold Inc.): FY 2024, 2025
- CTNM (Contineum Therapeutics, Inc.): FY 2025
- CTOR (Citius Oncology, Inc.): FY 2024, 2025
- CTXR (Citius Pharmaceuticals, Inc.): FY 2024, 2025
- CYPH (Cypherpunk Technologies Inc.): FY 2024, 2025
- DCOY (Decoy Therapeutics Inc.): FY 2024, 2025
- DFNS (T3 Defense Inc.): FY 2025
- DPRO (Draganfly Inc.): FY 2024, 2025
- DRMA (Dermata Therapeutics, Inc.): FY 2024, 2025
- DWTX (Dogwood Therapeutics, Inc.): FY 2024, 2025
- EAI (Entergy Arkansas, LLC): FY 2023, 2024, 2025
- ELAB (PMGC Holdings Inc.): FY 2024
- EMAT (Evolution Metals & Technologies Corp.): FY 2024, 2025
- ENHA (Enhanced Group Inc.): FY 2024, 2025
- EQ (Equillium, Inc.): FY 2025
- ERNA (Ernexa Therapeutics Inc.): FY 2025
- ESOA (Energy Services of America CORP): FY 2024, 2025
- FABC (Fabric.AI, Inc.): FY 2025
- FAC (Factorial Energy Inc.): FY 2025
- FFR (FF EAI Robotics Ecosystem Inc.): FY 2024, 2025
- FRMM (FORUM MARKETS Inc): FY 2024
- FRNM (Freenome, Inc.): FY 2025
- GERN (Geron Corp): FY 2024, 2025
- GGROU (Golden Growers Cooperative): FY 2024, 2025
- GNPX (Genprex, Inc.): FY 2025
- GP (GREENPOWER MOTOR Co INC.): FY 2025, 2026
- GRDX (GridAI Technologies Corp.): FY 2024
- IBO (Impact Biomedical Inc.): FY 2024
- IKT (Inhibikase Therapeutics, Inc.): FY 2025
- IMMP (IMMUTEP Ltd): FY 2026
- IMSR (Terrestrial Energy Inc. /DE/): FY 2025
- INKT (MiNK Therapeutics, Inc.): FY 2024, 2025
- JSPR (Jasper Therapeutics, Inc.): FY 2024, 2025
- KFFB (Kentucky First Federal Bancorp): FY 2025, 2026
- LITS (Lite Strategy, Inc.): FY 2026
- LSPD (Lightspeed Commerce Inc.): FY 2026
- MAIA (MAIA Biotechnology, Inc.): FY 2025
- MAYS (Mays J W Inc): FY 2026
- MAZE (Maze Therapeutics, Inc.): FY 2025
- MBOT (Microbot Medical Inc.): FY 2024, 2025
- MGYR (Magyar Bancorp, Inc.): FY 2024, 2025
- MIST (Milestone Pharmaceuticals Inc.): FY 2024, 2025
- MNOV (Medicinova Inc): FY 2024
- MRLN (Merlin, Inc.): FY 2025
- MTRI (Mentari Therapeutics, Inc.): FY 2026
- NAUT (Nautilus Biotechnology, Inc.): FY 2024, 2025
- NERV (Minerva Neurosciences, Inc.): FY 2025
- NFGC (New Found Gold Corp.): FY 2024
- NG (Novagold Resources Inc): FY 2023
- NMAD (Nomad Power Solutions, Inc.): FY 2024, 2025
- NNE (Nano Nuclear Energy Inc.): FY 2024, 2025
- NORD (Nordicus Partners Corp): FY 2026
- NRP (Natural Resource Partners LP): FY 2025
- NSARO (Nstar Electric Co): FY 2023, 2024, 2025
- NWGL (CL Workshop Group Ltd): FY 2025
- NXTS (Nexentis Technologies Inc.): FY 2025
- OKLO (Oklo Inc.): FY 2024, 2025
- OKUR (OnKure Therapeutics, Inc.): FY 2025
- OMER (Omeros Corp): FY 2023, 2024, 2025
- ORKA (Oruka Therapeutics, Inc.): FY 2025
- ORMP (Oramed Pharmaceuticals Inc.): FY 2024
- OTLK (Outlook Therapeutics, Inc.): FY 2024
- PAL (Proficient Auto Logistics, Inc): FY 2023, 2024, 2025
- PCG-PA (PACIFIC GAS & ELECTRIC Co): FY 2023, 2024, 2025
- PEW (GrabAGun Digital Holdings Inc.): FY 2023
- PFSA (Profusa, Inc.): FY 2025
- PTPI (Petros Pharmaceuticals, Inc.): FY 2025
- PULM (Pulmatrix, Inc.): FY 2025
- PVLA (Palvella Therapeutics, Inc.): FY 2024, 2025
- QETA (Quetta Acquisition Corp): FY 2024
- RADX (Radiopharm Theranostics Ltd): FY 2026
- RAIN (Rain Enhancement Technologies Holdco, Inc.): FY 2024, 2025
- RAPP (Rapport Therapeutics, Inc.): FY 2024, 2025
- RKTO (Rocket One Inc.): FY 2024, 2025
- RMSG (Real Messenger Corp): FY 2024, 2025
- RPT (Rithm Property Trust Inc.): FY 2025
- RWT (Redwood Trust Inc): FY 2024, 2025
- SACH (Sachem Capital Corp.): FY 2025
- SDEV (Stablecoin Development Corp): FY 2025
- SKT (Tanger Inc.): FY 2023, 2024, 2025
- SKYA (SkyAI, Inc.): FY 2024
- SKYQ (Sky Quarry Inc.): FY 2024, 2025
- SMMT (Summit Therapeutics Inc.): FY 2025
- SNGX (Soligenix, Inc.): FY 2025
- SONM (DNA X, Inc.): FY 2025
- SR (Spire Inc): FY 2026
- SRRK (Scholar Rock Holding Corp): FY 2024
- STCB (Starco Brands, Inc.): FY 2024, 2025
- STEX (Streamex Corp.): FY 2025
- STI (Solidion Technology Inc.): FY 2024
- SWX (Southwest Gas Holdings, Inc.): FY 2024, 2025
- TIC (TIC Solutions, Inc.): FY 2023
- TMC (TMC the metals Co Inc.): FY 2024, 2025
- TMS (Teamshares Inc): FY 2025
- TNYA (Tenaya Therapeutics, Inc.): FY 2024, 2025
- TOVX (Theriva Biologics, Inc.): FY 2024, 2025
- TVRD (Tvardi Therapeutics, Inc.): FY 2025
- UBCP (United Bancorp Inc /Oh/): FY 2024, 2025
- UNG (United States Natural Gas Fund, LP): FY 2023, 2024, 2025
- VBIO (Valion Bio, Inc.): FY 2025
- VERA (Vera Therapeutics, Inc.): FY 2023, 2024, 2025
- VERU (Veru Inc.): FY 2025
- VHUB (VenHub Global, Inc.): FY 2024
- VRMK (Erp Operating Ltd Partnership): FY 2023, 2024, 2025
- VSTM (Verastem, Inc.): FY 2023
- VWAV (VisionWave Holdings, Inc.): FY 2025
- WETO (Wetour Robotics Ltd): FY 2024
- XTLB (Xtl Biopharmaceuticals Ltd): FY 2023, 2025
- ZNOG (Zion Oil & Gas Inc): FY 2024, 2025

## Not read (105)

- AKTS (Aktis Oncology, Inc.): no figures
- AMRC (Ameresco, Inc.): no figures
- APC (ARKO Petroleum Corp.): no figures
- ASM (Avino Silver & Gold Mines Ltd): no figures
- BNC (BNB Standard Corp): no figures
- BOBS (Bob's Discount Furniture, Inc.): no figures
- BTGO (Bitgo Holdings, Inc.): no figures
- CMC (COMMERCIAL METALS Co): no figures
- CNI (Canadian National Railway Co): no figures
- GIW (GigCapital8 Corp.): no figures
- GIX (GigCapital9 Corp.): no figures
- LMRI (Lumexa Imaging Holdings, Inc.): no figures
- MWH (SOLV Energy, Inc.): no figures
- NRT (North European Oil Royalty Trust): no figures
- OFRM (Once Upon a Farm, PBC): no figures
- PARK (Park Dental Partners, Inc.): no figures
- SCE-PG (SOUTHERN CALIFORNIA EDISON Co): no figures
- SVIA (Silvia, Inc.): no figures
- UCFI (CN Healthy Food Tech Group Corp.): no figures
- WLFC (Willis Lease Finance Corp): no figures
- XNDU (Xanadu Quantum Technologies Ltd): no figures
- YSS (York Space Systems Inc.): no figures
- ZENA (ZenaTech, Inc.): no figures
- AADX (Applied Aerospace & Defense, Inc.): no report
- ADIG (Adi Global Distribution Inc.): no report
- ADRX (ADARx Pharmaceuticals, Inc.): no report
- AIAI (AIAI Holdings Corp): no report
- ALMR (Alamar Biosciences, Inc.): no report
- AMRO (Amaero Inc.): no report
- AMSS (Amass Brands): no report
- APMD (Apnimed, Inc.): no report
- ARXS (Arxis, Inc.): no report
- ATTO (Attovia Therapeutics, Inc.): no report
- AVAT (Avalanche Treasury Corp): no report
- AVEX (AEVEX Corp.): no report
- BRUN (Boost Run Inc.): no report
- BSEM (BioStem Technologies, Inc.): no report
- BSPA (Ballston Spa Bancorp, Inc.): no report
- BXDC (Blackstone Digital Infrastructure Trust Inc.): no report
- CBRS (Cerebras Systems Inc.): no report
- CLBK (Columbia Financial, Inc./MD/): no report
- CSQR (Csquare, Inc.): no report
- DMRC (Digimarc Corp): no report
- ELMT (Elmet Group Co.): no report
- EROC (ERock, Inc.): no report
- EROK (EagleRock Land, LLC): no report
- EXYN (Exyn Technologies, Inc.): no report
- FBDT (First Breach, Inc.): no report
- FCBM (First Carolina Financial Services, Inc.): no report
- FFLY (FireFly Robotics, Inc.): no report
- FRBT (Forbright, Inc.): no report
- FRVO (Fervo Energy Co): no report
- GENB (Generate Biomedicines, Inc.): no report
- GMRS (GMR Solutions Inc.): no report
- GYGY (Game Your Game Inc.): no report
- HAWK (HawkEye 360, Inc.): no report
- HMH (HMH Holding Inc): no report
- HONA (Honeywell Aerospace Inc.): no report
- IAM (Iambic Therapeutics, Inc.): no report
- IOND (Ionic Digital Inc.): no report
- ITG (ITG, Inc./DE/): no report
- JAN (Janus Living, Inc.): no report
- JMKE (Jersey Mike's Subs Inc.): no report
- LCLN (Lincoln International, Inc.): no report
- LFTO (Liftoff Mobile, Inc.): no report
- LIME (Neutron Holdings, Inc.): no report
- LYNX (Lyntris Inc.): no report
- MAIR (Madison Air Solutions Corp): no report
- MBGL (Mobility Global Inc.): no report
- MFP (Midera Food Processing, Inc.): no report
- MOBI (Mobia Medical, Inc.): no report
- MSGR (MSG Rangers Corp.): no report
- NARA (Narragansett Bancorp, Inc.): no report
- NAVI (Navient Holdings, Inc.): no report
- NVRI (Enviri Corp): no report
- OBX (Obsidian Therapeutics, Inc.): no report
- OCTV (Octave Intelligence plc): no report
- ODTX (Odyssey Therapeutics, Inc.): no report
- OIG (Orion180 Insurance Group Inc.): no report
- PBAM (Private Bancorp of America, Inc.): no report
- PBLS (Parabilis Medicines, Inc.): no report
- PS (Pershing Square Inc.): no report
- QMLS (QumulusAI, Inc.): no report
- QNT (Quantinuum Inc.): no report
- REAX (Real REMAX Group Inc.): no report
- REF (Reformation Inc.): no report
- RMIX (Suncrete, Inc.): no report
- RNA (Atrium Therapeutics, Inc.): no report
- SCTX (Scribe Therapeutics, Inc.): no report
- SECZ (Securitize Corp.): no report
- SPCX (Space Exploration Technologies Corp): no report
- SSMR (Sunshine Silver Mining & Refining Co): no report
- STDN (Standard Nuclear, Inc.): no report
- SUJA (Suja Life, Inc.): no report
- SWMR (Swarmer, Inc): no report
- SWRD (Stewards, Inc.): no report
- USDE (StableCoinX Inc.): no report
- VIDA (VIDA Global Inc.): no report
- VYLR (Vylor Inc.): no report
- WCCB (West Coast Community Bancorp): no report
- WHK (WhiteHawk Minerals Corp.): no report
- XE (X-Energy, Inc.): no report
- XOM (ExxonMobil Holdings Corp): no report
- XTND (Xtend AI Robotics, Inc.): no report
- YSWY (Yesway, Inc.): no report
