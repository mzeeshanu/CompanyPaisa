# CompanyPaisa

**Discover publicly traded companies near you — and explore their money.**

CompanyPaisa finds public companies around a user's location and shows their
financial story: revenue, quarterly income, executive compensation and up to
~10 years of history. It's also a **public API** that other applications use.

The core constraint: **no live third-party lookups at runtime.** All company,
location and financial data comes from a directory we curate ahead of time.

- Requirements, UI/UX spec, decisions log: [`docs/functional-requirements.md`](docs/functional-requirements.md)
- Clickable design prototype (sample figures): [`docs/prototype/home-prototype.html`](docs/prototype/home-prototype.html)
- Data files and database layout: [`data/README.md`](data/README.md)

## Status

- ✅ Design agreed (prototype), requirements documented
- ✅ **Phase 1 backend**: .NET 10 API, request pipeline, Excel data source, C# client, 31 tests
- ✅ **Website**: React + TypeScript — location popup, List and Map views, company panel, themes, cookie consent
- ✅ **Executive lookup**: executives of nearby companies, 10-year pay, careers across companies (39 tests)
- ✅ **Real data**: 2,639 public companies in the top 20 US metros plus Utah's Wasatch Front, from SEC EDGAR —
  ~10 years of revenue/net income and 59,475 executive pay rows for 15,881 people, linked across companies by SEC
  insider id (`tools/CompanyPaisa.Importer`, see [`data/README.md`](data/README.md))
- ✅ **US, Canada and UK**: 4,090 listed US companies and Canadian SEC filers (46 areas), plus 328 UK Main Market
  companies; refreshed monthly by a scheduled task (`tools/refresh-data.ps1`)

## Run it

Requires the .NET 10 SDK and Node.js (LTS).

**Visual Studio:** set **CompanyPaisa.Api** as the startup project, pick the `https` (or `http`) profile and press
**F5**. The API starts, launches the React dev server automatically (first run installs npm packages), and the
browser opens the website. Pick the **"API only (Swagger)"** profile to open Swagger instead.

**Command line:**

```bash
dotnet run --project src/CompanyPaisa.Api --launch-profile http
```

| URL | What |
|---|---|
| http://localhost:5104 | Website (redirects to the React dev server on :5173 in development) |
| http://localhost:5104/swagger | Swagger UI — try every API endpoint |
| http://localhost:5104/health | Health check |

Front-end only (with the API already running): `cd src/CompanyPaisa.Web && npm run dev`.
Publishing (`dotnet publish src/CompanyPaisa.Api -c Release`) builds the React site into the API's `wwwroot`,
so production is a single app.

**Deploying:** Railway (Dockerfile + `railway.json`) — see [`docs/deployment-railway.md`](docs/deployment-railway.md).

Run the tests:

```bash
dotnet test CompanyPaisa.slnx
```

## API v1

| Endpoint | Returns |
|---|---|
| `GET /api/v1/companies/near?near=84043&radiusMiles=10` | Companies near a ZIP code or `City, ST` |
| `GET /api/v1/companies/near?latitude=40.39&longitude=-111.85` | Same, from coordinates |
| …`&sector=Software&headquarteredOnly=true&sort=Growth&page=1&pageSize=50` | Filters, sort (Revenue / Growth / Profit / Distance), paging |
| `GET /api/v1/companies/{ticker}` | Profile, locations, headline indicators |
| `GET /api/v1/companies/{ticker}/financials?period=Annual&years=10` | Revenue & net income history with YoY growth |
| `GET /api/v1/companies/{ticker}/executives?years=5` | Executive pay by year |
| `GET /api/v1/companies/{ticker}/insights` | Facts (sector and city rank, streaks, best year, CEO pay vs results, margin vs sector) and similar companies nearby |
| `GET /api/v1/executives/near?near=84043&sort=TotalPay&years=10` | Executives of nearby companies with 10 years of pay (sort: Pay / TotalPay / PayGrowth / Distance / Name; `includeFormer`, `search`, `sector`) |
| `GET /api/v1/executives/{personId}` | One person's career and pay across every company they were a named executive at |
| `GET /api/v1/search?q=nvidia&limit=6` | Companies (name or ticker) and executives (name) anywhere in the data |
| `GET /api/v1/geo/lookup?q=Lehi, UT` · `GET /api/v1/geo/zip/{zip}` | ZIP / city → coordinates |
| `GET /api/v1/sectors` · `GET /api/v1/meta` · `GET /api/v1/client-config` | Reference data, data version, website settings |

Errors are RFC 7807 problem details (400 with per-field messages, 404, 401, 429).
Send an API key in the `X-Api-Key` header for the higher rate limit; anonymous calls
are allowed at a lower limit (`Api:AllowAnonymous`).

### Using it from another .NET app

Reference `CompanyPaisa.Client` (will be published as a NuGet package):

```csharp
// appsettings.json: "CompanyPaisaApi": { "BaseUrl": "https://companypaisa.com/", "ApiKey": "…" }
builder.Services.AddCompanyPaisaClient(builder.Configuration.GetSection("CompanyPaisaApi"));

// anywhere via DI:
var nearby = await companyPaisa.GetCompaniesNearZipAsync("84043", radiusMiles: 10);
var execs  = await companyPaisa.GetExecutivesNearAsync(new ExecutivesNearRequest { Near = "84043", Sort = ExecutiveSort.TotalPay });
var career = await companyPaisa.GetExecutiveAsync(execs.Items[0].PersonId);
```

## Architecture

```
src/
  CompanyPaisa.Contracts/       request/response types shared by API and client
  CompanyPaisa.Core/            domain models, interfaces, services, use-case handlers, options
  CompanyPaisa.Infrastructure/  IServiceRequestor + pipeline (logging → analytics → validation → caching), ZIP lookup, clock
  CompanyPaisa.Data/            the in-memory data set, its integrity rules, the reloading repository base
  CompanyPaisa.Data.Sqlite/     ICompanyRepository over the published SQLite database (the live data)
  CompanyPaisa.Data.Excel/      ICompanyRepository over Excel workbooks (sample data, hand-made sets)
  CompanyPaisa.Analytics/       the site's own visitor analytics: queue, background writer, Postgres / SQLite stores
  CompanyPaisa.Api/             minimal-API endpoints, API keys, rate limits, OpenAPI, health, serves the SPA
  CompanyPaisa.Client/          typed C# client for other apps
  CompanyPaisa.Web/             React + TypeScript website (Vite, D3) — calls only the public /api/v1
tools/CompanyPaisa.Importer/    builds the real data set (SEC EDGAR, UK and EU annual reports) into data/companypaisa.db
tools/CompanyPaisa.SampleData/  generates the synthetic workbook used by tests
tests/                          unit tests (Core) + end-to-end tests (Api via the client)
```

- **Endpoints are thin**: `requestor.SendAsync(new GetCompaniesNearQuery(...))`. Each use case is one
  request + one handler; validation, logging and caching are pipeline behaviors.
- **Storage is swappable**: `DataSource:Provider` picks the `ICompanyRepository` implementation, and `Analytics:Provider`
  the analytics store (Postgres, SQLite or none).
- **Everything tunable is in `appsettings.json`** (radius options, trend thresholds, cache durations,
  rate limits, UI defaults, feature flags), bound to typed options validated at startup.
  Secrets (API keys, connection strings) go in user-secrets or environment variables.

---

*Product name: CompanyPaisa · Company + Paisa (money).*
