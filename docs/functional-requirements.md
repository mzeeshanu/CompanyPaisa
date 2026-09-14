# Functional Requirements Document — CompanyPaisa

**Product:** CompanyPaisa.com — "Company + Paisa (money)"
**Author:** Zee
**Status:** Draft v0.2 — design agreed via clickable prototype, ready for Phase 1
**Last updated:** 2026-09-12 (executive lookup added)

> **Clickable prototype:** [`docs/prototype/home-prototype.html`](prototype/home-prototype.html) — open it in a browser.
> It is the visual reference for everything in §6 (UI & UX). Financial figures in it are **illustrative samples**, not real data.

---

## 1. Purpose & Vision

A web application that lets a user discover **publicly traded companies near their current location** and explore each company's financial story — revenue, net income, executive compensation, and up to ~10 years of history — to form a view on where the company is headed.

The defining constraint: **the app does not perform live third-party lookups at runtime.** All company, location, map and financial data is served from our own directory, curated ahead of time. When a user arrives, we take their location and assemble the result set entirely from local data.

---

## 2. Goals (v1)

- Given a user's location, return public companies with an office or HQ within a chosen radius.
- For each match, show the **specific nearby location** (HQ, campus, office, plant) and its distance — not just the corporate HQ.
- Show per company: profile basics, revenue & quarterly income, growth indicators, and executive compensation.
- Provide ~10 years of financial history for trend viewing.
- Present results in two switchable views (**Map** and **List**) with a distinctive "liquid glass" look in light and dark themes.
- Keep the data layer **swappable** (Excel/CSV now → SQL/NoSQL later via configuration only).

## 3. Non-Goals (v1)

- No live third-party lookups (Google Maps/Places, financial APIs, geocoders) at request time.
- No Google Maps — it requires Google's API. (See §6.5 for the self-hosted alternative.)
- No user accounts, watchlists, portfolios or trading.
- No automated predictions — we show history and simple indicators (YoY, CAGR, margin); modeling is later.
- No tracking/analytics/advertising cookies.

---

## 4. Primary User & Key Scenario

**User:** Someone curious about the public companies physically around them — residents, job seekers, employees (e.g. "how is my employer doing vs. its neighbors?"), local investors.

**Core flow:**
1. User lands → page is blurred behind a small popup asking for **location or ZIP**.
2. User shares location (or types a ZIP) → the popup fades, the page un-blurs.
3. User sees nearby companies in their preferred view (**List** by default, or **Map**), filtered by radius.
4. User clicks a company (bubble or row) → a **details panel** opens: key numbers, earnings history, executive pay.
5. First visit only: a small **cookie banner** asks whether to remember their view and theme.

---

## 5. Functional Requirements

### 5.1 Location & Discovery
- **FR-1** Accept location via browser geolocation **or** manual ZIP / city entry.
- **FR-1a** Never fire the browser permission prompt on page load — only after the user clicks "Use my location" (a denied prompt is sticky).
- **FR-1b** ZIP/city resolves via a **local US ZIP centroid table** (US Census Gazetteer, ~40k rows). No runtime geocoding; full street addresses are out of scope for v1.
- **FR-1c** If the user is far from any covered company, say so plainly and suggest a covered ZIP (don't show an empty page).
- **FR-2** Radius chips: **5 / 10 / 25 / 50 mi**, default **10 mi**.
- **FR-3** Return companies with **at least one location** inside the radius; each result shows its **nearest qualifying location** and straight-line (haversine) distance.
- **FR-4** Sort by **Revenue** (default), **Growth**, **Profit**, **Distance**.
- **FR-4a** Filters: **Sector** dropdown and **"Headquartered here only"** switch. Radius + filters apply to both views.

### 5.2 Company Directory
- **FR-5** Company record: display name, ticker, exchange, website, sector/industry, employees, market cap, description, fiscal year end, currency, logo URL.
- **FR-6** One or more locations per company: label/type (**HQ / campus / office / plant**), street, city, state, postal code, lat/long.
- **FR-7** Coordinates precomputed and stored — no runtime geocoding.
- **FR-7a** Location type drives the **HQ badge** and the "Headquartered here only" filter.

### 5.3 Financials
- **FR-8** Quarterly and annual revenue, net income (plus operating income, EPS later).
- **FR-9** Executive compensation per named executive per year: salary, bonus, stock awards, other, total (from DEF 14A proxy filings).
- **FR-10** Up to ~10 years of history.
- **FR-11** Time series shown as charts + tables.
- **FR-11a** Derived indicators computed **server-side in C#**: trailing-12-month (TTM) revenue & net income, YoY growth, 5-year CAGR, net margin, and a **trend status** (see §6.4).
- **FR-11b** Every financial figure should be traceable to its source filing (store `source_filing` URL).
- **FR-11c** Always label figures as **company-wide** when the nearby location is not the HQ.

### 5.4 Data Layer (Swappable Storage)
- **FR-12** All data access through one interface, `ICompanyRepository`.
- **FR-13** v1 implementation: Excel/CSV, loaded once at startup into memory, validated.
- **FR-14** Later SQL (EF Core) / NoSQL implementations selected by config (`DataSource` connection string or file path) — no app-code changes.

### 5.5 Preferences & Consent
- **FR-15** Persist **view** (Map/List), **theme** (Auto/Light/Dark) and **map layer on/off**.
- **FR-16** Persist only after consent: first-party cookie `cp_prefs` (JSON, 1 year, `SameSite=Lax; Secure; path=/`).
- **FR-17** Consent banner appears after the location step (never stacked on top of the location popup): **"Remember my choices"** / **"Just this visit"**. Declining stores nothing and clears any earlier cookie. A **"Cookie settings"** link reopens it.
- **FR-18** Do not store the user's location without a separate, explicit opt-in (not in v1).
- **FR-15a** Also persist the lookup **mode** (Companies / Executives).

### 5.6 Executive Lookup (people)
- **FR-19** "Executives near me": list the **named executive officers of public companies that have a location within the radius** — the company's location defines "near", never where a person lives (we don't store home locations).
- **FR-20** A **person is one record across companies** (`person_id`). In real data `person_id` = the person's **SEC CIK** (reporting-owner id), so filings from different companies link to the same person.
- **FR-21** For each person show: current title and company, nearest location + distance, latest year's total pay, change vs prior year, **total pay over the last 10 years across all companies**, number of companies, pay-per-year sparkline (dots mark company changes).
- **FR-22** Sort by **Latest pay** (default), **10-year total**, **Pay growth**, **Distance**, **Name**. Filters: radius + sector (shared with Companies), **text search** on name/title, and **"Include people who moved away"** (former executives of nearby companies, flagged as former).
- **FR-23** Person profile: total earned (first–latest year), career as a list of roles (company, title, years, total pay), stacked pay-by-year chart **coloured by company**, and a year-by-year table (salary, bonus, stock, total). Company names link to the company panel; executive names in the company panel link to the person profile.
- **FR-24** Be explicit about scope: pay = **reported compensation** from DEF 14A summary compensation tables (stock at grant-date value, not realized); only roles as a **named executive officer of a public company** appear — private-company or non-NEO roles are not in SEC filings.
- **FR-25** API: `GET /api/v1/executives/near` (same location/radius/sector params as companies + `includeFormer`, `search`, `sort`, `years`) and `GET /api/v1/executives/{personId}`; available in the C# client. Behind the `Features:Executives` flag.

---

## 6. UI & UX Specification

### 6.1 Visual identity — "liquid glass"
- Frosted-glass panels and **glass-sphere bubbles** over a slowly drifting **aurora gradient** background.
- Rejected directions (don't revisit): dark "radar/sonar" look (sweep, crosshairs, compass letters), Google Maps background.
- **Type:** Sora (headings), Manrope (body), JetBrains Mono (numbers, tickers). Numbers use tabular figures.
- **Accent:** indigo `#3F48E0` (light) / `#8E95FF` (dark).
- **Status colors (separate from accent):** growing green `#0E9E67` / `#3DDC97`, flat amber `#D5870A` / `#F5B94E`, shrinking or loss-making red `#DB3E58` / `#FF6B81` (light / dark).
- Futuristic around the edges, **plain and readable inside the data** (panel, tables, charts).
- Respect `prefers-reduced-motion` (stop aurora drift, pulses, animations).

### 6.2 Themes
- **Auto** (follows OS), **Light**, **Dark** — a single button in the top bar cycles through them.
- Every color is a CSS token; dark is a designed palette (deep night-blue ground, dimmer aurora, brighter tints), not an inversion.

### 6.3 Page structure
- **Landing / location popup:** blurred page behind a glass card — headline "Who's making money around you?", **Use my location** button, **ZIP** input, clear error messages.
- **Top bar (sticky, glass):**
  - Row 1: wordmark · "Near *Lehi, UT 84043* · Change" · Sample-data badge · **Companies | Executives** switch · **Map | List** switch (Companies only) · theme button.
  - Row 2: **Within 5/10/25/50 mi** chips · Sector dropdown · "Headquartered here only" switch (Companies only).
- **Company details panel:** slides in from the right (bottom sheet on mobile). Closes with ✕, Esc, or clicking outside.

### 6.4 Bubbles (shared by both views)
- **Size = annual (TTM) revenue** using an **area (square-root) scale** with a **minimum size** so small companies stay clickable. Same scale everywhere so bubbles compare fairly.
- **Tint = trend status:**
  - `down` if TTM net income < 0, or YoY revenue < −1%
  - `up` if YoY revenue > +4% (and profitable)
  - `flat` otherwise
- Ticker centered on the bubble; name appears on hover / when zoomed in.
- **Hover:** tooltip with name, revenue, growth, distance. **Linked highlight:** hovering a bubble highlights its list row and vice versa.
- **Click:** opens the details panel; the selected bubble/row stays highlighted across view switches.

### 6.5 Map view
- User at center ("YOU" marker with a soft pulse); **distance rings** at 1/2/5/10/25/50 mi; the **selected radius ring** is dashed accent with label "· your radius".
- Bubbles placed at their **true direction and distance** from the user; a collision force nudges overlapping bubbles apart while keeping them near their true spot.
- **Zoom / pan:** mouse wheel, pinch, drag, plus − / + / ◎ (fit to radius) buttons. Picking a radius chip animates the zoom to fit it.
- Hovering a bubble draws a line from the user to it with the distance.
- **Base map layer** (toggle "Map", on by default): pale roads (I-15, I-80, I-215, SR-85) with shields, lakes (Utah Lake, Great Salt Lake), Jordan River, soft mountain shading (Wasatch, Oquirrh, Traverse), city names (minor cities appear when zoomed in). Zooms/pans in sync with the bubbles.
  - *Prototype:* hand-drawn approximate shapes.
  - *Production:* **MapLibre GL** (open source) rendering **Protomaps vector tiles** from a **single self-hosted `.pmtiles` file** for Utah served by our own server — no Google, no API keys, no third-party runtime calls. Custom pale style per theme. Bubbles projected with `map.project(lngLat)`.
- Overlays: summary chip (count, combined revenue, # growing, # HQ), legend (size + tint), controls, one-time gesture hint.

### 6.6 List view (default)
1. **Summary line:** "**15 public companies** within 10 miles of Lehi" · combined TTM revenue · # growing · # headquartered here.
2. **By city, nearest first:** a glass card per city with its bubbles **circle-packed** (no overlap), header "Lehi · 12 · 2.1 mi". Horizontal swipe row on mobile.
3. **Ranked list:** Rank-by chips (Revenue / Growth / Profit / Distance). Columns: rank · mini bubble · name + HQ badge + ticker/sector · nearest location + city + distance · TTM revenue · growth pill · revenue sparkline since 2017 · TTM net income. Fewer columns on tablet/mobile. **Column headers are tappable** (both Companies and Executives lists): tap a header to sort by it; tap the active header again to reverse the order (▼/▲ shows direction; rows with no value stay at the bottom). On phones the list always fits the screen width (no sideways scrolling); the Rank-by chips swipe sideways.
4. Empty state: "Nothing matches. Widen the radius or clear the filters."

### 6.7 Company details panel
- Header: ticker chip, exchange, sector, "Headquartered here" chip; company name; trend dot · location name · city · **distance from you**.
- **KPI tiles:** revenue (latest quarter), net income (latest quarter), revenue growth (TTM vs prior TTM), 5-year CAGR.
- Note: "Company-wide figures, not just this location" (when not HQ).
- **Earnings tab:** Quarterly (last 12 quarters) / Annual (~10 FY) toggle; bars = revenue, line = net income (dots green/red by sign), latest period emphasized; table of the last 4 periods with YoY.
- **Executives tab:** per named executive — title, name, 2025 total pay + change vs prior year, stacked bar (salary / bonus / stock / other, scaled to the top earner), 5-year pay sparkline.
- Footer: data source note (10-K, 10-Q, DEF 14A).

### 6.8 Executives view & person panel
- **Summary line:** "**49 executives** at 14 public companies within 10 miles of Lehi" · combined pay (latest year) · median pay · scope note ("named executive officers only").
- **Toolbar:** Rank-by chips (Latest pay / 10-year total / Pay growth / Distance / Name) · search box (debounced) · "Include people who moved away" switch.
- **Rows:** rank · initials avatar · name + title (+ "former") · company + ticker · city · distance · latest pay + year · change pill · pay sparkline (amber dots = company change) · 10-year total (or "N companies").
- **Person panel** (same shell as the company panel; only one panel open at a time): chips (current ticker, sector, SEC CIK when known) · avatar + name · title · company link · KPIs (latest pay, change, total earned, # companies) · stacked pay-by-year bars coloured by company with a company legend · **Career** list · **Year by year** table · scope disclaimer.
- The Executives mode is list-only for now (no Map view).

### 6.9 Responsive & accessibility
- Mobile: details panel becomes a bottom sheet; clusters scroll horizontally; map controls move to bottom-right; legend hidden.
- All bubbles and rows are keyboard-focusable buttons (Enter opens details), visible focus ring, `aria-pressed` on toggles, `aria-live` summary.
- The List view is the accessible alternative to the Map view.

---

## 7. Data Model

Tables map to Excel sheets now, SQL tables / NoSQL collections later.

**Company**
| field | example |
|---|---|
| company_id | LFVN |
| name | LifeVantage |
| ticker / exchange | LFVN / NASDAQ |
| website | lifevantage.com |
| sector / industry | Health & wellness / … |
| employees | (value) |
| market_cap | (value) |
| fiscal_year_end | 06-30 |
| currency | USD |
| logo_url | (path) |
| hq_location_id | LFVN-001 |
| description | … |
| as_of_date | 2026-06-30 |

**Location** (many per company)
| field | example |
|---|---|
| location_id | LFVN-001 |
| company_id | LFVN |
| type | HQ / campus / office / plant |
| label | Headquarters |
| street, city, state, postal_code | …, Lehi, UT, 84043 |
| latitude / longitude | 40.4338 / -111.8880 |

**Financials** (one row per company per period)
| field | example |
|---|---|
| company_id | LFVN |
| fiscal_year / fiscal_quarter | 2026 / 2 (null for annual) |
| period_type | quarterly / annual |
| revenue, net_income | (values) |
| operating_income, eps | (values, later) |
| source_filing | SEC URL |

**ExecutiveCompensation** (one row per person per company per year)
| field | example |
|---|---|
| company_id, person_id, year | LFVN, 0001234567 (SEC CIK), 2025 |
| exec_name, title | (name as filed), Chief Executive Officer |
| salary, bonus, stock_awards, other, total | (values) |
| source_filing | SEC URL |

`person_id` is shared across companies — that's what links a career. (Older workbooks with a per-company `exec_id` still load.)

**People** (optional, one row per person)
| field | example |
|---|---|
| person_id | 0001234567 |
| name | (canonical name) |
| sec_cik | 0001234567 |

**Reference data:** `us-zip-centroids.csv` (zip, city, state, lat, lng). **`_meta` sheet:** data version and last-refreshed date (shown as "Data as of …").

---

## 8. Architecture

```
Browser: React + TypeScript (Vite) — Tailwind, D3, Framer Motion, MapLibre (map layer)
   │  JSON over HTTP
ASP.NET Core Web API (C#)
   ├─ /api/geocode?q=84043          → local ZIP table
   ├─ /api/search?lat&lng&radius&sector&hqOnly&sort
   ├─ /api/companies/{id}           → profile + locations + indicators
   ├─ /api/companies/{id}/financials?period=Q|A
   ├─ /api/companies/{id}/executives
   ├─ /tiles/utah.pmtiles           → self-hosted map tiles (static file, range requests)
   ├─ Services: SearchService (haversine + bbox prefilter, nearest location per company)
   │            MetricsService (TTM, YoY, CAGR, margin, trend status)
   └─ ICompanyRepository ── ExcelCompanyRepository (v1) │ EfCompanyRepository (later)
```

- **All business logic in C#**; the React app only renders API results.
- TypeScript API types generated from the C# OpenAPI spec (NSwag) so front and back can't drift.
- Single deployment: ASP.NET Core serves the built React files (Azure App Service or Docker).

**Solution layout (as built, Phase 1)**
```
CompanyPaisa.slnx
src/CompanyPaisa.Contracts/       DTOs + enums shared by API and client
src/CompanyPaisa.Core/            domain models, interfaces (ICompanyRepository, IGeoLocator, IDistanceCalculator,
                                  IFinancialMetricsService, IClock, IFilePathResolver, IDataChangeSignal),
                                  messaging abstractions (IServiceRequestor, IRequestHandler, IPipelineBehavior,
                                  IRequestValidator, ICacheableRequest), options, use-case handlers
src/CompanyPaisa.Infrastructure/  ServiceRequestor, Logging/Validation/Caching behaviors, CsvZipGeoLocator, clock
src/CompanyPaisa.Data.Excel/      ExcelCompanyRepository (in-memory snapshot, hot reload), workbook reader/writer
src/CompanyPaisa.Data.Sql/        (later) EF Core repository
src/CompanyPaisa.Api/             minimal-API v1 endpoints, API-key filter, rate limiter, ProblemDetails, OpenAPI,
                                  Swagger UI, health checks, SPA fallback, appsettings*.json
src/CompanyPaisa.Client/          ICompanyPaisaClient + AddCompanyPaisaClient() (future NuGet)
src/CompanyPaisa.Web/             (next) React + TypeScript (Vite)
tools/CompanyPaisa.SampleData/    generates data/sample/companypaisa.sample.xlsx
tools/CompanyPaisa.Importer/      (later) SEC EDGAR → workbook
tests/CompanyPaisa.Core.Tests/    unit tests: distance, metrics, search handler, pipeline
tests/CompanyPaisa.Api.Tests/     end-to-end: hosts the API, calls it through CompanyPaisa.Client
data/                             sample workbook, reference ZIP table (see data/README.md)
docs/                             this document, prototype/
```

**Configuration sections (appsettings.json):** `DataSource` (+ `DataSource:Excel`), `Geo`, `Search`, `Metrics`,
`Caching` (profiles: Search / Company / Reference), `Pipeline`, `Api` (keys, rate limits, CORS, OpenAPI),
`Ui` (defaults served via `/api/v1/client-config`), `Features` (flags). All bound to typed options validated at startup.

**Libraries:** ClosedXML or CsvHelper (Excel/CSV), EF Core (later), NSwag, xUnit · React, Vite, Tailwind, D3, Framer Motion, MapLibre GL, pmtiles.

---

## 9. Data Sourcing

- **Coverage (2026-09-13):** the top 20 US metros plus the Wasatch Front, discovered automatically from SEC filings by business address (see decisions log). Originally: the seed was Wasatch Front public companies (Salt Lake City → Provo, target 40–60). Prototype currently lists 34, including **LifeVantage (LFVN, Lehi)**; every name, office and coordinate must be verified before launch — several local names are private or acquired (Qualtrics, Pluralsight, Instructure, Vivint→NRG, Vivint Solar→Sunrun).
- **Financials & exec pay:** an **offline C# import tool** pulls from SEC EDGAR (10-K, 10-Q, DEF 14A / XBRL) into the workbook. Runs ahead of time, never at request time.
- **Locations:** entered by hand (address + lat/long), checked against filings/websites.
- **Refresh cadence:** quarterly, after earnings season; the UI shows "Data as of …".

---

## 10. Decisions Log

| Date | Decision |
|---|---|
| 2026-09-11 | **Backend:** C# / ASP.NET Core Web API. All business logic lives server-side. |
| 2026-09-11 | **Frontend:** React + TypeScript (Vite), Tailwind, D3, Framer Motion. Kept thin. TS types generated from the C# OpenAPI spec. |
| 2026-09-11 | **Data access:** `ICompanyRepository` + DI; Excel/CSV in v1, EF Core later, selected by config. |
| 2026-09-11 | **Deployment:** single app — ASP.NET Core serves the built React files. |
| 2026-09-11 | **Landing:** blurred background + small popup asking for location (button-triggered geolocation) or ZIP. |
| 2026-09-11 | **Manual location:** ZIP / city only via local ZIP centroid table. |
| 2026-09-11 | **Bubbles:** size = revenue (area scale, min size), tint = trend status. Revenue, not employees. |
| 2026-09-11 | **Visual style:** "liquid glass". Radar/sonar look rejected. |
| 2026-09-11 | **Google Maps rejected** (requires Google API). |
| 2026-09-11 | **Two switchable views:** Map and List; shared radius / sector / HQ-only filters. Default: List. |
| 2026-09-11 | **Themes:** Auto / Light / Dark. |
| 2026-09-11 | **Preferences:** `cp_prefs` cookie, only after consent banner; "Just this visit" stores nothing. |
| 2026-09-11 | **Radius:** chips 5/10/25/50 mi, default 10 (was open question). Straight-line distance only. |
| 2026-09-11 | **Indicators in v1:** YoY, 5-yr CAGR, margin, trend status (was open question). |
| 2026-09-11 | **Seed region:** Wasatch Front (SLC–Provo), not just Lehi. |
| 2026-09-12 | **Map view keeps a base map layer** (roads, lakes, mountains, cities) so bubbles sit in their real places; toggleable. Production uses self-hosted MapLibre + Protomaps `.pmtiles` — no Google, no runtime third-party calls. |

| 2026-09-12 | **API-first:** other apps consume the public versioned REST API (`/api/v1`) — never a direct DB connection (a read-only reporting replica may come later). A typed C# client (`CompanyPaisa.Client`, NuGet) wraps it. |
| 2026-09-12 | **Request pipeline:** own lightweight `IServiceRequestor` + `IRequestHandler<TRequest,TResponse>` with pipeline behaviors (logging, validation, caching) — not MediatR (commercial licence since 2025). Thin endpoints; logic in handlers/services; repository decorators for caching. |
| 2026-09-12 | **Configuration:** everything tunable lives in `appsettings*.json`, bound to typed options classes validated at startup (`ValidateOnStart`). Secrets only in user-secrets / env vars / Key Vault. UI defaults served to React via `/api/v1/client-config`. |
| 2026-09-12 | **API access:** API keys for external apps; the website calls anonymously with rate limiting. OpenAPI/Swagger, URL versioning, ProblemDetails, output caching, `/health`. |
| 2026-09-12 | **Repo & deployment:** one repo, one solution; front end (`CompanyPaisa.Web`) and API (`CompanyPaisa.Api`) are separate projects. Dev = two processes (Vite dev server proxies `/api`). Prod = single deployment, API serves the built React files. Front end uses only the public API, so it can move to a CDN later via config. |

| 2026-09-12 | **Website built** (`src/CompanyPaisa.Web`): React 19 + TypeScript + Vite + D3, a faithful port of the prototype running on the live API. Styling uses the prototype's CSS token system (`src/styles.css`) instead of Tailwind — it already encodes both themes and the glass look; Tailwind can be added later if wanted. TS API types are hand-mirrored from Contracts for now (generate from `/openapi/v1.json` later). |
| 2026-09-12 | **F5 experience:** `Microsoft.AspNetCore.SpaProxy` — running the API (VS or `dotnet run`) starts the Vite dev server and opens the site; Vite proxies `/api` to the API. HTTPS redirection is off in Development for that reason. A first Debug build runs `npm install` if needed; `dotnet publish` runs `npm run build` into `wwwroot`. Extra launch profile "API only (Swagger)". |

| 2026-09-12 | **Hosting: Railway** (answers open question 2). One service built from the repo `Dockerfile` (Node stage → .NET publish → ASP.NET runtime), `railway.json` health check on `/health`. App listens on `PORT` (IPv4+IPv6), trusts forwarded headers (`Hosting:TrustForwardedHeaders`) so rate limits use real visitor IPs. Data files ship in the image for now; move to a Railway volume (or Postgres later) when data changes often. Guide: `docs/deployment-railway.md`. |

| 2026-09-12 | **Executive lookup** (answers "look up by executives"): Companies / Executives switch; executives are the named officers of *nearby companies* (by company location, not residence); a person is one record across companies (`person_id` = SEC CIK in real data); 10-year pay window (`Metrics:HistoryYears`); careers span companies only where the person was a public-company NEO. Shared `INearbySearchService` now backs both company and executive searches. Sample data: 10 years of pay, 110 people, 8 career moves between local companies. |

| 2026-09-12 | **Real data from SEC EDGAR** via `tools/CompanyPaisa.Importer` (offline, cached, ≤8 req/s, User-Agent contact = Zee's email, approved). Region = Wasatch Front (Ogden, SLC, Lehi, Provo anchors); listed on Nasdaq/NYSE/CBOE, or OTC with ≥ $5M revenue; plus curated Utah sites of out-of-state companies. Financials from XBRL company facts; executive pay parsed from DEF 14A Summary Compensation Tables (filed total is authoritative, mismatches flagged in `data/import-report.md`). ZIP centroids from the 2025 Census Gazetteer. First load: 59 companies, 1,380 pay rows, 390 people. The app now serves `data/companypaisa.xlsx`; tests stay on the synthetic sample workbook. |
| 2026-09-12 | **Executive names are real** (public SEC filings) — decided by Zee. Placeholder names were only ever for the synthetic sample. |
| 2026-09-13 | **Phone layout:** header scrolls away on phones (sticky on desktop); ranked lists fit the screen (name column shrinks, secondary columns hidden); Rank-by chips swipe sideways. |
| 2026-09-13 | **Sortable columns:** tapping a list column header sorts by it; tapping again reverses the order (client-side). Rows with no value ("—") stay at the bottom either way. |
| 2026-09-13 | **Nationwide, metro by metro** — decided by Zee: the top 20 US metros plus the Wasatch Front (New York, Los Angeles, Chicago, Dallas–Fort Worth, Houston, Washington–Baltimore, Philadelphia, Atlanta, Miami–South Florida, Phoenix, Boston, SF Bay Area, Detroit, Seattle, Minneapolis–St. Paul, San Diego, Tampa Bay, Denver, Portland, Austin). Importer `Regions` = named metros with anchor circles; discovery searches every state they touch for recent 10-K and 10-Q filers (10-Q catches recent IPOs and reorganised companies), splitting date ranges so each query fits one results page; tickers missing from a submissions record fall back to the SEC ticker file. The importer reads the finished workbook back with the API's loader and fails if the API would reject it. The website lists the metros from `Ui:Coverage`. |
| 2026-09-13 | **Person ids = SEC insider CIK** (answers open question 9): proxy names are matched to the company's insider list (Forms 3/4/5 owners, "Last First Middle", nicknames and suffixes handled); id = `name-slug-CIK`, so careers link across companies and two different "John Smith"s never merge. Unmatched names get a company-scoped id (`name-slug-ticker`). |
| 2026-09-13 | **ZIP table covers the whole US** (`data/reference/us-zip-centroids.csv`): Census Gazetteer centroids + GeoNames US postal codes for city/state names and PO-box ZIPs (CC BY 4.0, credited in the footer). Importer download cache is gzip-compressed. |
| 2026-09-13 | **All of Minnesota** — requested by Zee: importer regions can list whole `States` as well as anchor circles. Metro circles win, so Twin Cities companies stay in "Minneapolis–St. Paul"; a "Rest of Minnesota" region takes every other listed Minnesota company (Hormel, Fastenal, Otter Tail, Electromed, Nuvera). Location screen now says "US areas". |
| 2026-09-13 | **United Kingdom: FTSE 350** — decided by Zee (scope FTSE 350; named directors shown, with a UK privacy notice). Investment trusts excluded. Separate importer run (`--uk`) writes `data/companypaisa-uk.xlsx`; the API merges it with the US workbook (`DataSource:Excel:AdditionalPaths`, ids must not collide; UK tickers end in `.L`). Sources: constituent list from Wikipedia's FTSE 100/250 tables, saved as a reviewable `data/curated/uk-ftse350.csv` (a hand-set LEI fixes a bad match); ESEF annual reports from filings.xbrl.org (xBRL-JSON for revenue/profit, ~6 years; the report XHTML for pay); headquarters from GLEIF; postcode districts from GeoNames (CC BY 4.0). UK sources get a generic User-Agent — no personal contact details. Pay = each executive director's "single total figure" from the directors' remuneration report (latest 3 reports ≈ 4 years), total authoritative; reports are parsed once and only the result is cached. Companies can report in one currency and pay in another (`pay_currency`). Website: £/$/€ formatting, UK postcode input (district-level), US/UK tabs on the location screen, annual-only companies show their latest year, and a privacy notice (footer) with a configurable contact (`Ui:PrivacyContact`). |
| 2026-09-13 | **Data disclaimer** — requested by Zee: the location screen and the footer say the figures come from SEC filings collected and read automatically by software built with AI tools, that automated reading can make mistakes, and (footer) that it is not financial advice. Wording lives in one place (`src/lib/disclaimer.ts`). Worded as "SEC filings read by AI-built software" rather than "internet search", because that is how the data is actually produced. |

## 11. Open Questions

1. **Company list verification** — Zee to supply/confirm the companies, offices and addresses known locally.
2. ~~**Hosting**~~ — **Decided: Railway** (see decisions log).
3. **Market cap** — needs a price feed; do we store an end-of-day snapshot at refresh time (still offline), or drop market cap from v1?
4. ~~**Executive names**~~ — **Decided: real names** from SEC filings. Still to do: a correction/contact route, and keep to what's filed (name, title, pay).
8. **Location precision** — companies are placed at their ZIP-code centroid (several Lehi companies share one point). Geocode street addresses (e.g. the free Census Geocoder, offline at import time)?
9. ~~**Person linking**~~ — **Decided: SEC insider CIKs** (see decisions log).
11. **Base map outside Utah** — the hand-drawn base map only covers the Wasatch Front; other metros show bubbles on a blank ground. Draw simple maps per metro, or move to self-hosted Protomaps tiles (Phase 4)?
12. **Storage at scale** — thousands of companies in one Excel workbook: fine while load time and memory stay acceptable; switch the `ICompanyRepository` implementation to SQLite/Postgres when they don't.
14. **Successor companies** — when a company reorganises into a new holding company (ExxonMobil → ExxonMobil Holdings Corp, July 2026), the ticker moves to a new SEC CIK with no history; the 10 years of financials and pay stay under the old CIK. Add a predecessor link (curated list, or detected from Form 8-K12B) so history carries over?
13. **Curated offices nationwide** — `data/curated/` only lists Utah offices of out-of-state companies (Adobe Lehi…). Add big offices in other metros (e.g. Google in Seattle/NYC), or keep HQs only outside Utah?
10. **Review queue** — 2,339 pay rows whose components don't add up to the filed total (total kept, difference shown as Other) and 1,338 unrecognised proxy tables at 624 companies (see `data/import-report.md`); fix parser cases or hand-correct?
15. **UK privacy contact** — the privacy notice needs a real contact (email or form) for corrections and data-protection requests before UK directors' pay goes live; set `Ui:PrivacyContact`. Worth a quick check with someone who knows UK data law.
16. **UK pay breakdown and careers** — totals come straight from the single-figure table, but the salary / bonus / long-term split only adds up exactly for part of the rows (the rest goes to Other), and UK directors aren't linked across companies (no SEC-style ID). Improve the table reader per layout; link people via Companies House officer ids?
7. **Executives on the Map** — should Executives mode get a map (company bubbles sized by total executive pay), or stay list-only?
5. **"Size by" switch** (revenue / market cap / growth) — v1 or later?
6. **Clustering in Map view** ("+7" merge bubbles when zoomed far out) — needed once the dataset grows past ~60.

---

## 12. Phasing

- **Phase 0** ✅ Requirements, stack, UX direction agreed; prototype built.
- **Phase 1** ✅ (backend, 2026-09-12) — Solution scaffold; request pipeline; `ICompanyRepository` + Excel implementation with hot reload; sample ZIP table; sample seed workbook; API v1 with keys, rate limits, OpenAPI; C# client; 31 passing tests.
  Still open from Phase 1: full Census ZIP table; **verified real company list** (currently sample data).
- **Phase 2** ✅ (2026-09-12) — List view (summary, city clusters, ranked list, filters) + location popup + themes + consent, on the live API.
- **Phase 3** ✅ (2026-09-12) — Company details panel (earnings, executives). Map view with the simplified base map also done early.
- **Phase 3b** ✅ (2026-09-12) — Executive lookup: people across companies, 10-year pay, careers; API + client + website; 39 passing tests.
- **Phase 3c** ✅ (2026-09-12) — Real dataset: SEC EDGAR importer (companies, XBRL financials, DEF 14A executive pay), Census ZIP centroids; 47 passing tests.
- **Phase 3d** (2026-09-13) — Nationwide metros: top 20 US metros + Wasatch Front, national ZIP table, SEC insider-CIK person ids, metro chips on the location screen.
- **Phase 4** — Map view with self-hosted MapLibre/Protomaps base map; street-level geocoding; scheduled quarterly refresh.
- **Phase 5** — Swap storage to a database; optional "Size by", clustering, predictive features.
