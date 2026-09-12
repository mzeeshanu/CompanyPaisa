# Data

| Path | What | Real or sample? |
|---|---|---|
| `sample/companypaisa.sample.xlsx` | Development workbook: 34 Wasatch Front companies | Company names and approximate locations are real. **All financial figures and executive names are synthetic.** |
| `reference/us-zip-centroids.sample.csv` | ZIP → city/coordinates for ~30 Wasatch Front ZIPs | Approximate centroids. Replace with the full US Census Gazetteer ZIP file (~33k rows) before launch. |

Regenerate the sample workbook (from the repo root):

```bash
dotnet run --project tools/CompanyPaisa.SampleData
```

The API reloads the workbook automatically when the file changes (`DataSource:Excel:ReloadOnChange`).

## Workbook layout

Columns are matched by header name (any order, case-insensitive); extra columns are ignored.

| Sheet | Required columns | Optional columns |
|---|---|---|
| `Companies` | company_id, name, ticker, exchange, sector | industry, website, employees, market_cap, description, currency, fiscal_year_end, logo_url, as_of_date |
| `Locations` | location_id, company_id, type (Headquarters / Campus / Office / Plant), label, city, state, latitude, longitude | street, postal_code |
| `Financials` | company_id, period_type (Quarterly / Annual), fiscal_year, revenue, net_income; fiscal_quarter (1-4) for quarterly rows | operating_income, eps, source_filing |
| `ExecutiveCompensation` | company_id, exec_id, exec_name, title, year | salary, bonus, stock_awards, other, total, source_filing |
| `_meta` | key, value — `data_version`, `as_of_date`, `is_sample` | |

Money is in whole dollars. On load the API checks for duplicate IDs, orphaned company_ids, bad quarters and invalid coordinates, and reports every problem with its sheet and row. If a reload fails, the API keeps serving the last good data.
