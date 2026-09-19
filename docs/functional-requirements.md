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
- No tracking/analytics/advertising cookies, and no third-party analytics. (The site counts visits itself, without visitor cookies or stored IP addresses; see the 2026-09-17 decision.)

---

## 4. Primary User & Key Scenario

**User:** Someone curious about the public companies physically around them — residents, job seekers, employees (e.g. "how is my employer doing vs. its neighbors?"), local investors.

**Core flow:**
1. User lands → page is blurred behind a small popup asking for **location or ZIP**.
2. User shares location (or types a ZIP) → the popup fades, the page un-blurs.
3. User sees nearby companies in their preferred view (**List** by default, or **Map**), filtered by radius.
4. User clicks a company (bubble or row) → the **company's own page** opens (`/company/AAPL`): key numbers, earnings history, executive pay, locations. Back returns to the same spot in the list.
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
- **FR-13** Live data: one SQLite file (`data/companypaisa.db`, one partition per market), loaded into memory at startup, validated, reloaded when an import replaces it. Excel workbooks remain a provider for the sample data.
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
- **FR-23** Person profile: total earned (first–latest year), career as a list of roles (company, title, years, total pay), stacked pay-by-year chart **coloured by company**, and a year-by-year table (salary, bonus, stock, total). Company names link to the company page; executive names on the company page link to the person's page.
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
- **Landing / location popup:** blurred page behind a glass card — headline "Who's making money around you?", **Use my location** button, **ZIP** input, clear error messages. Below the postcode box: "Looking for a particular company or person?" with the **search by name** box, so a visitor can go straight to a page without choosing a location.
- **Top bar (sticky, glass):**
  - Row 1: wordmark · "Near *Lehi, UT 84043* · Change" · Sample-data badge · **search by name** box (its own full-width row on phones) · **Companies | Executives** switch · **Map | List** switch (Companies only) · theme button.
  - Row 2: **Within 5/10/25/50 mi** chips · Sector dropdown · "Headquartered here only" switch (Companies only).
- **Company and executive pages:** every company and person has its own address — `/company/{TICKER}` and `/executive/{personId}` (e.g. `/executive/dylan-field-2073586`). Bubbles, rows and names are real links (long-press / middle-click / "open in new tab" work; a normal click moves inside the site without reloading). Page header: wordmark (back to the search) and theme button; under it "← Back to companies near Lehi" (or "Find public companies near you" when the page was opened from a shared link, which skips the location popup).
- **Search by name** (top bar on every screen, and the location popup): type 2+ letters of a company name, ticker or person's name; up to 5 companies (name, ticker · HQ city, latest-12-month revenue) and 5 executives (name, latest title · company, latest pay) appear as you type. Arrow keys + Enter or a tap opens the page; Esc closes. Matching ignores case, accents and punctuation; every word typed must start a word of the name ("tim co" finds Tim Cook, "bank am" Bank of America); exact tickers first, then names that start with the text, then bigger companies / higher pay.
- **Search addresses:** the search on screen is always in the address bar, so it can be shared, bookmarked and reloaded: `/near/{place}` for companies, `/near/{place}/executives` for executives, plus only the settings that differ from the defaults — `radius`, `sector`, `hq=1`, `sort`, and for executives `q` (name/title filter) and `former=1`. Map / List and the theme stay personal preferences (cookie), not part of the address. `{place}` is the postcode without spaces (`84043`, `SW1A1AA`, `M5J2J2`), European / Australian / NZ / Pakistani codes with their country (`FR-75008`, `NL-1012AB`, `AU-2000`, `PK-74000`), or `me` for "my location" — the visitor's coordinates never go in the address. Opening a `/near/me` link uses the location only if the browser already allows it; otherwise the location popup asks. An unknown place opens the popup with "We couldn't find …". Changing filters updates the address without adding Back-button steps.

### 6.4 Bubbles (shared by both views)
- **Size = annual (TTM) revenue** using an **area (square-root) scale** with a **minimum size** so small companies stay clickable. Same scale everywhere so bubbles compare fairly.
- **Tint = trend status:**
  - `down` if TTM net income < 0, or YoY revenue < −1%
  - `up` if YoY revenue > +4% (and profitable)
  - `flat` otherwise
- Ticker centered on the bubble; name appears on hover / when zoomed in.
- **Hover:** tooltip with name, revenue, growth, distance. **Linked highlight:** hovering a bubble highlights its list row and vice versa.
- **Click:** opens the company's page; coming back, the page is scrolled to where it was and the last-opened bubble/row stays highlighted.

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
3. **Ranked list:** Rank-by chips (Revenue / Growth / Profit / Distance) and a **"Search name or ticker…" box** that narrows the list only — the bubbles, map and summary line keep the whole area; ranks renumber, "Showing 6 of 128 · Clear" under the rows, "No company here matches …" when nothing does; kept in the address as `q`, cleared when a new area is picked. Columns: rank · mini bubble · name + HQ badge + ticker/sector · nearest location + city + distance · TTM revenue · growth pill · revenue sparkline since 2017 · TTM net income. Fewer columns on tablet/mobile. **Column headers are tappable** (both Companies and Executives lists): tap a header to sort by it; tap the active header again to reverse the order (▼/▲ shows direction; rows with no value stay at the bottom). On phones the list always fits the screen width (no sideways scrolling); the Rank-by chips swipe sideways.
4. Empty state: "Nothing matches. Widen the radius or clear the filters."

### 6.7 Company page (`/company/{TICKER}`)
- Header card: ticker chip, exchange, sector, industry; company name; trend dot · headquarters city · **nearest location's distance from the visitor's search point** (when they have one); employees and website when known; description; **KPI tiles** in one row (two per row on phones): revenue (latest quarter, or latest fiscal year for yearly-only filers), net income, revenue growth (TTM vs prior TTM), 5-year CAGR; note "Company-wide figures, not just one location".
- **Earnings** card: Quarterly (last 12 quarters) / Annual (~10 FY) toggle; bars = revenue, line = net income (dots green/red by sign), latest period emphasized; table of the last 4 quarters or last 10 years with YoY; periods link to the filing.
- **Layout:** under the header card and the full-width New leadership card, two columns on wide screens — left: **Earnings**, then **Executive pay** (the numbers); right: **At a glance**, **Pay vs similar companies**, **Similar companies nearby**, **Locations** (what they mean). One column on narrow screens in that same order (2026-09-18, requested by Zee: "left side first Earnings, below that executive earnings, right side other info").
- **Executive pay** card (beside Earnings on wide screens, below it on narrow ones): per named executive — title, name (link to their page), latest total pay + change vs prior year, stacked bar (salary / bonus / stock / other, scaled to the top earner), 5-year pay sparkline.
- **At a glance** card ("{Company} at a glance"): short facts worked out from the company's figures and the rest of the data set; each appears only when the data supports it. (1) CEO pay change vs revenue change for the same fiscal year ("CEO X's pay rose 23% to $2.8M in 2025, while revenue for fiscal 2025 rose 14%"). (2) Rank by latest-12-month revenue in its sector, shown when top 10 or top 10% ("#5 by revenue among the 77 Semiconductors companies we track"). (3) Rank among public companies headquartered in the same city, shown when top 5. (4) Revenue streak: 3+ quarters (or years, for yearly filers) in a row of growth or decline against a year before. (5) Best revenue year ("2025 was its best year yet"). (6) Profitable years out of the last ten. (7) The CEO earns a typical worker's yearly pay every N hours (same benchmark as the quick fact). (8) Net margin vs the sector median ("Keeps 28¢ of every $1 of revenue as profit; the median Technology hardware company keeps 1¢"). (9) Revenue per employee when the employee count is known, otherwise revenue per second. Ranks, sector medians and "similar" skip companies without a real sector (European filers, sector "Other").
- **New leadership** card (below At a glance, when the company announced officer appointments in the last 18 months): per person — name (links to their page when they have reported pay anywhere), title, "Announced 16 Apr 2026 · starts 5 Aug 2026", the **announced package** total, a stacked bar and list of its parts (base salary, bonus stated in dollars, sign-on / make-whole cash, stock awards, performance stock, options, relocation / other cash; same-label grants added up, "(2 grants)"), and "Read the announcement (8-K) ↗". First 3, then "Show all". Fine print: what the company said it would pay when it announced the appointment, not pay received; stock at the stated value; percentage bonus targets not counted; read automatically, check the filing. The newest appointment of the last year is also the first **At a glance** fact: "New President and Chief Executive Officer Terrence O. Moorehead joined on 5 Aug 2026 with an announced package of $8.8M."
- **Pay vs similar companies** card (left column, after At a glance; hidden when too few peers report pay): headline "Pays its CEO more than 64% / less than 72% of similar companies" (about the middle between 45% and 55%); two tiles — CEO pay and the other named executives' median pay, each with the peers' median and the % difference, and the other executives' standing ("paid less than 85% of similar"); then "The 8 companies closest in size, by CEO pay": those companies and this one (highlighted) ranked highest pay first, with bars and links to each company. **Similar** = same sector (not the European "Other"), same kind of filing (UK companies only against UK companies: their reports give executive directors' "single total figure", not a CEO line; Pakistani companies only against Pakistani companies), latest reported pay no more than a year older than this company's, revenue between a third and three times this company's — or, when that gives fewer than 20, the 20 closest in size. Fine print names how many peers and the revenue range they span. Pay in this company's pay currency (others converted at the approximate rates). UK: the top-paid executive director stands in for the CEO.
- **Similar companies nearby** card: up to 5 companies closest to its headquarters in the same sector within 100 miles (distance in whole miles, bigger companies first on ties; "same area" under a mile); when fewer than 2, "Companies nearby" of any sector within 25 miles. Each row: trend dot, name, ticker · city · distance, latest-12-month revenue, growth pill; links to that company's page.
- **Locations** card: each location (label, HQ badge, address, distance from the search point, nearest first) with "Companies near here →", which starts a new search around that location; first 6, then "Show all".
- Footer: data source note (SEC filings / UK or European annual reports / Pakistani annual reports filed with the PSX). Unknown ticker: "Company not found" with a way back to the search.
- The browser tab title is "Name (TICKER) — revenue, profit and executive pay · CompanyPaisa".

### 6.8 Executives view & person page
- **Summary line:** "**49 executives** at 14 public companies within 10 miles of Lehi" · combined pay (latest year) · median pay · scope note ("named executive officers only").
- **Toolbar:** Rank-by chips (Latest pay / 10-year total / Pay growth / Distance / Name) · search box (debounced, name or title) · **role dropdown** (All roles · CEOs · CFOs · COOs · CTOs / CIOs · General counsel · Other executives; kept in the address as `role`) · "Include people who moved away" switch. Roles are read from the title of the nearby role shown: a title can hold several ("Chief Operating Officer and Chief Financial Officer" is both); "Former", "Deputy", "Assistant", "Associate", "Retired", "Outgoing", "Incoming" titles don't count; CTO / CIO covers chief technology, information, digital and data officers; General counsel covers chief legal officers; Other = the title names none of these roles.
- **Rows:** rank · initials avatar · name + title (+ "former") · company + ticker · city · distance · latest pay + year · change pill · pay sparkline (amber dots = company change) · 10-year total (or "N companies"). Officers appointed at a nearby company in the last 12 months get a green **New** tag; someone we already list is shown at their new company and title. Someone with no reported pay yet gets a row of their own showing the **announced package** ("package") instead of pay, "—" for change and history, "no pay yet" for the total, and links to the company page (no person page yet). Announced packages are left out of the combined and median pay.
- **Person page** (`/executive/{personId}`, same layout as the company page): header card with chips (current ticker, sector, SEC CIK when known) · avatar + name · title · company link · KPIs (latest pay, change, total earned, # companies); **Pay by year** card (stacked bars coloured by company, company legend) beside the **Career** card (roles link to company pages); **Year by year** table (salary, bonus, stock, other, total; ticker links to the company page) · scope disclaimer.
- The Executives mode is list-only for now (no Map view).

### 6.9 Responsive & accessibility
- Mobile: company and person pages stack their cards in one column; clusters scroll horizontally; map controls move to bottom-right; legend hidden.
- All bubbles and rows are keyboard-focusable links (Enter opens the page), visible focus ring, `aria-pressed` on toggles, `aria-live` summary.
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
- **Coverage (2026-09-14):** every listed US company, Canadian companies that file with the SEC, UK Main Market companies, and listed companies in France, the Netherlands, Italy and Spain (financials only), and Australian / New Zealand companies that file with the SEC — all from official filings (see decisions log). UK AIM, Germany and the rest of the ASX still to come. **Pakistan (2026-09-18):** companies listed on the Pakistan Stock Exchange, figures and the chief executive's pay read from each company's own annual report PDF (228 companies in the first run).
- **Refresh cadence:** monthly, by a scheduled task on Zee's PC (`tools/refresh-data.ps1`); the UI shows "Data as of …".

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
| 2026-09-14 | **Whole US, Canada and the UK Main Market, refreshed monthly** — requested by Zee (answers: jobs run on Zee's PC via Task Scheduler; Canada = companies that file with the SEC; UK = Main Market + AIM; stay on Excel). **US:** discovery now screens every company in the SEC's listed-ticker file (`Discovery:AllListed`) that filed a 10-K/10-Q/40-F/20-F in the last 15 months (`FiledWithinMonths`, rolling); 16 more named metros (St. Louis, Nashville, Charlotte, Raleigh–Durham, Pittsburgh, Cleveland, Columbus, Cincinnati, Kansas City, Indianapolis, Milwaukee, Las Vegas, Sacramento, Orlando, Richmond, Omaha) and a whole-country "Rest of US" region (`WholeCountry`). US-listed companies based outside the US and Canada are counted, not included. **Canada:** SEC filers with a Canadian business address (EDGAR province codes A0–B0, Z4); financials from XBRL in US GAAP or IFRS, in the currency reported (often CAD, shown as C$; `Currency:UsdPer:CAD`); pay only where a US proxy statement exists (40-F filers don't file one); Canadian postal areas (FSA, GeoNames CC BY 4.0) in `data/reference/ca-postal-areas.csv`; regions Toronto, Vancouver, Calgary, Montréal, Ottawa, Rest of Canada; location screen gets a Canada tab and accepts "M5J" / "M5J 2J2" (a bare code that is also a UK district, e.g. N1C, resolves to the UK; a full Canadian code is always Canada). **UK Main Market:** every UK filer on filings.xbrl.org with London-listed ordinary shares or REIT units (`Uk:AllMainMarket`): GLEIF gives each filer's ISINs, OpenFIGI maps them to London tickers (cached), investment trusts drop out as "Closed-End Fund"; companies beyond the FTSE 350 have no sector yet ("Other") and are listed for review in `data/curated/uk-main-market.csv`. **AIM is not done yet:** AIM companies don't file ESEF reports, so they need Companies House (free API key, created by Zee) plus an AIM company list. **Job:** `tools/refresh-data.ps1` (US + Canada import, UK import, then commit and push only the data files; refuses to run with uncommitted work outside `data/`; logs in `%LOCALAPPDATA%\CompanyPaisa\logs`), scheduled by `tools/register-refresh-task.ps1` for the 2nd of each month at 2 am (runs later if the PC was off). Monthly rather than weekly so the repo doesn't grow by a full workbook every week. Census and GeoNames downloads now use the generic User-Agent too (the contact email is only for the SEC). |
| 2026-09-14 | **Data scrubbing and verification** — requested by Zee ("make sure what we show is legit"; report button "shouldn't take over the design — a tiny popup in a corner"). (1) **Validation:** `--validate` runs 25 checks over every published workbook and writes `data/validation-report.md`; the monthly job runs it with `--strict` and publishes nothing if any error-level check fails. (2) **CEO pay cross-check:** since 2023 proxies tag the CEO's total from the summary compensation table in inline XBRL (pay versus performance, `ecd:PeoTotalCompAmt`, up to five years back). The importer reads those tags from the same proxy it parses; a matching row is verified; the same CEO with a nearby but different total is corrected to the tagged figure (the difference goes to "other"); a tag more than 2× off is itself wrong (some companies tag dollars with a thousands scale) and the table figure stays; counts go in the import report. A single-year pay total above $3bn is a validation error. (3) **Show only what we're sure of:** companies whose newest figures are more than two years old are left out; where a year was restated and its four quarters no longer add up to it, the quarters are dropped and the year kept; a reviewed `Importer:Listing:SkipTickers` list removes duplicate listings (Grifols' US listing, shown from Madrid). Revenue uses the largest line tagged per period and the accounting standard with the newest figures; pay tables in thousands are scaled; impossible pay rows are left out. (4) **Report a problem:** a small flag in the bottom-left corner opens a compact card (what's wrong, optional note, the company in view) that opens the visitor's email app addressed to `Ui:PrivacyContact`; nothing is stored by the site. (5) **Sources:** footer sources line and an "About the data" window; period and year labels link to the filing they came from. |
| 2026-09-14 | **Europe, financials only** — requested by Zee (answers: "Big 5 first", no executive pay for now). Source is the same as the UK: ESEF annual reports on filings.xbrl.org, which every company on an EU regulated market must file. **Germany isn't there** (German reports go to the Unternehmensregister, whose terms don't allow bulk downloads), so the first set is **France, Netherlands, Italy, Spain**. Separate importer run (`--eu`, settings `Importer:Eu`) writes `data/companypaisa-eu.xlsx`, merged by the API like the UK workbook. Per country: filers from filings.xbrl.org → shares on the home exchange (GLEIF ISINs → OpenFIGI, exchange codes FP / NA / IM / SM+SQ; when a bank's share ISIN is buried under bond ISINs, an OpenFIGI name search on the home exchange, exact cleaned-name match only) → GLEIF headquarters in that country → GeoNames postcode → IFRS revenue/profit from each report (same code as the UK). Ticker suffixes `.PA`, `.AS`, `.MI`, `.MC`; sector "Other" (no free sector source). Website: France / Netherlands / Italy / Spain tabs; on those tabs a postcode is sent as "FR-75008" because a bare 5-digit code is a US ZIP (the API only reads European codes with the country in front); company panel says pay isn't collected for European companies yet. Postcode table `data/reference/eu-postcodes.csv` (keyed by country in the API). Added to the monthly job. |
| 2026-09-15 | **Code review fixes, SQLite, markets** — requested by Zee ("fix the 3 bugs", "move the excel sheet to SQLite", "keep the monthly job on my PC", "I like the idea of structure for more countries"). (1) **Bugs:** searches returned at most 200 companies while the headline counted all — the site now asks for every company (API cap 2,000) and executives page 200 at a time with "Showing N of M · Show more"; behind Cloudflare the rate limiter saw Cloudflare's address, so `Api:ClientIpHeader` = `CF-Connecting-IP` in production gives each visitor their own limit; the response cache now counts rows, not entries (`Caching:MaxEntries` = 100,000 rows), and GPS coordinates are rounded to ~100 m so neighbours share cached results. (2) **SQLite:** the live data is one file, `data/companypaisa.db` (26 MB; the three workbooks were 13 MB zipped but took 12 s and ~725 MB to load — SQLite loads in ~2 s using ~240 MB). Every row carries its **market** (`sec`, `uk`, `eu`); an import replaces only its own market's rows on a copy that is read back and checked with the API's rules before it replaces the file. Filing URLs are stored once with shortened prefixes; amounts are NUMERIC (compact integers). `PRAGMA user_version` guards the layout. Excel stays as a provider for the sample data (`DataSource:Provider`). Migration verified byte-for-byte against the live (Excel) site on 11 endpoints; an automated test compares Excel and SQLite answers. (3) **Markets:** each importer implements `IMarketImporter` and publishes through `DataPublisher`; `--market <id>`, `--all [--strict]`, `--list-markets`. Adding an ESEF country is config; a new source is one class. The monthly job runs `--all --refresh-lists --strict`. |
| 2026-09-15 | **Every US city and state gets a named area** — requested by Zee ("pull the cities/states we haven't covered"). The data already had every listed US company (discovery is the SEC's whole ticker file); ~900 of them only sat under a catch-all "Rest of US". Now: 45 more metros (every city with 3+ listed companies, e.g. Jacksonville, San Antonio, Tulsa, Louisville, Northwest Arkansas, Honolulu, Boise), existing metros widened to their missing suburbs (Walnut Creek, Marin, Grapevine, South Orange County, Chester County, Lake County, Novi, Everett, Jupiter, Racine), and a region for every state plus Puerto Rico ("Rest of Texas", or just "Wyoming" where there's no metro) — 134 US areas in the importer; the site offers the 132 that have companies (Knoxville and the rest of New Mexico have none yet). "Rest of US" stays in the importer only as a fallback. The location screen's full list gets a "Find a city or state" box and scrolls inside the card. Configured in `Importer:Regions` and `Ui:Coverage`. |
| 2026-09-15 | **Australia and New Zealand: SEC filers only** — decided by Zee. Neither country publishes company financials in a free, structured form (no XBRL on filings.xbrl.org; ASIC documents are paid PDFs; ASX annual reports are PDFs and its site terms limit automated downloads), so for now only Australian and NZ companies that file with the SEC (20-F / 10-K; EDGAR country codes C3, Q2) are included, through the existing US import. Postcodes from GeoNames (`data/reference/anz-postcodes.csv`, keyed "AU:2000"); areas Sydney, Melbourne, Brisbane, Perth, Rest of Australia, Auckland, Rest of New Zealand; A$/NZ$ formatting and approximate rates; AU and NZ tabs (codes sent as "AU-2000"). 20-F filers have no US proxy, so no executive pay. Open: the full ASX list (~2,045 companies) without figures, or reading ASX annual-report PDFs. |
| 2026-09-16 | **Quick fact line** — requested by Zee ("an interesting quick fact between the bubbles and the grid"; chose revenue per second and the top-paid CEO vs a typical worker; country GDP rank and "semi trucks of cash" were discussed and not picked). One glass line under the bubble packs in List view, with ↻ for the next fact and an "i" that shows how the number is worked out and its source. The same search always opens on the same fact. (1) **Revenue per second:** the search's combined latest-12-month revenue ÷ seconds in a year (per minute or hour for tiny totals), marked ≈ when currencies were converted; the note says revenue is worldwide sales. (2) **Top-paid CEO:** the API's nearby-companies summary gains `topPaidCeo`: the best-paid chief executive of a company **headquartered** in the results (companies with only an office in range don't count; sector and HQ filters apply), from each company's latest reported year, ignoring companies whose latest pay is more than a year older than the newest in the search; titles must say CEO / chief executive and not former, deputy or vice. UK annual reports only say "Executive Director", so there the top-paid executive director is used and the sentence says so. Compared with the median full-time pay of the company's home country (`Benchmarks:MedianPay` in appsettings): US $65,052 (BLS, Q2 2026, $1,251 a week × 52), UK £39,039 (ONS ASHE, April 2025), Canada C$68,640 (Statistics Canada 14-10-0064, 2025, C$1,320 a week × 52), Australia A$90,532 (ABS Employee Earnings, August 2025, A$1,741 a week × 52); pay in another currency is converted at the approximate rates. "Every N hours" counts every hour of the year (8,766), nights and weekends included. The CEO's name opens their panel. No fact for European areas (no pay data) or where the top pay is below the median. Update the medians by hand when the statistics offices publish new figures. |
| 2026-09-17 | **Own visitor analytics** — requested by Zee (visitor counts, locations, what people look up; "our own so we have more control"; built on dependency injection so providers can be swapped; not Cloudflare D1, in case storage has to be bought; Railway, portable to Azure later; city + country; a private dashboard page). **Recording:** `CompanyPaisa.Analytics`: `IAnalyticsTracker` (Core; does nothing by default) → the website's `HttpAnalyticsTracker` → in-memory queue (`IAnalyticsSink`; when full, new events are dropped) → `AnalyticsWriter` background service → `IAnalyticsStore` / `IAnalyticsReader`, chosen by `Analytics:Provider`: **Postgres** on Railway (`Analytics__ConnectionString = ${{Postgres.DATABASE_URL}}`), a **Sqlite** file on the developer's PC, **None** in tests. Portable SQL only, so moving is pg_dump / pg_restore plus a connection string. Postgres without a connection string records nothing rather than stopping the site. **What:** a pipeline behavior (logging → analytics → validation → caching, so cached answers still count) records requests marked `ITrackedRequest`: company and executive searches (first page only; not the location screen's one-row check; point rounded to 2 decimals ≈ 1 km), company and executive panels, postcode / city lookups. The website sends page views (with the referring site's host) and a few clicks via `sendBeacon` to `POST /api/v1/events` (whitelisted names: how the location was chosen, Map/List, Companies/Executives, quick-fact ↻ and i, Show more, About / Privacy / Report opened). Each event carries device / browser / OS family, city / region / country from Cloudflare's headers (`CF-IPCountry`; city and region need Cloudflare's free "Add visitor location headers"), and source (website / public API / API key). **Privacy:** no visitor cookies and no IP addresses stored. The visitor code is a hash of IP + browser with a random salt per UTC day; salts older than yesterday are deleted, so a visitor is counted once a day but can't be followed across days. Bots (user-agent patterns) and browsers with the owner's `cp_owner` cookie aren't counted. Privacy notice and cookie banner reworded ("no tracking cookies", plus a paragraph on what is counted). **Dashboard:** `/admin` (a separate bundle visitors never download); key in `Analytics__DashboardKey` (header `X-Admin-Key`, kept in the tab's session storage; no key configured = 404). Shows visits, page views, searches, companies / executives opened; visits per day; top areas (search points named after the nearest town from the local postcode tables), companies, executives, postcodes typed, countries, cities, devices, browsers, OS, referrers, clicks, sources; 7 / 30 / 90 / 365 days; CSV per table and every event for the range; "Don't count this browser". "Visits" = distinct visitors per day, added up. Railway setup and backups: `docs/deployment-railway.md`. |
| 2026-09-13 | **Data disclaimer** — requested by Zee: the location screen and the footer say the figures come from SEC filings collected and read automatically by software built with AI tools, that automated reading can make mistakes, and (footer) that it is not financial advice. Wording lives in one place (`src/lib/disclaimer.ts`). Worded as "SEC filings read by AI-built software" rather than "internet search", because that is how the data is actually produced. |
| 2026-09-17 | **A page per company and per executive** — requested by Zee ("make the companies more like page links… clicking a company should open the page for the company"; the site was a single page with slide-in panels). The panels are replaced by full pages at `/company/{TICKER}` and `/executive/{personId}` (person ids are already readable slugs like `dylan-field-2073586`). Bubbles, list rows, executive rows, the quick fact's CEO name and every company / person name on the pages are real `<a href>` links, so they can be shared, bookmarked and opened in a new tab; a plain click moves inside the site without a reload (small router on the browser's history, no new library). **Back** (the page's link, the browser button or the phone's back gesture) returns to the search with its results, filters and scroll position intact (the position is saved in the history entry; scroll anchoring is paused for a second while the bubble grid lays itself out) and the last-opened company highlighted. A page opened from a shared link doesn't ask for a location; its back link is "Find public companies near you", and each location on a company page has "Companies near here →" to start a search there. Layout: header card with KPIs in one row, Earnings and Executive pay side by side on wide screens, Locations list (see §6.7–6.8). **Link previews and search engines:** the API answers `/company/{ticker}` and `/executive/{personId}` itself, returning the website's index.html with that page's `<title>`, description, canonical address and Open Graph tags (read straight from the data, not through the tracked queries, so opening a page still counts as one company / executive view in the analytics); unknown ones get the app with a 404. Map view bubbles open the page too. |
| 2026-09-17 | **Search by name, shareable search addresses, sitemap** — requested by Zee (picked recommendations 1–3 after the company pages shipped). (1) **Search by name:** new public endpoint `GET /api/v1/search?q=&limit=` (default 6, max 20; C# client `SearchByNameAsync`) returning companies (name / ticker) and executives (name) from the whole data set, served from an in-memory index (`INameSearchIndex`, built on first use from the repository, rebuilt when the data set reloads). Not recorded as it's typed (every keystroke would count); the website sends a `name_search` event with the text when a result is picked, and the page view is recorded as usual. Box in the top bar of every screen and in the location popup (see §6.3). (2) **Search addresses** `/near/{place}[/executives]?radius=…` (see §6.3); a shared or reloaded search skips the popup; the website sends `location_link` with the place when a search is opened from an address; "Companies near here" on a company page goes to that location's postcode. The API answers `/near/…` with the page title "Public companies near Lehi, UT 84043" / "Executives and their pay near …" for link previews (unknown place: 404 with the app). (3) **Search engines:** `/robots.txt` (everything allowed except `/admin`; the API stays crawlable because Google renders the pages with it) and `/sitemap.xml` listing the home page, every covered area's search address, every company page and every executive page (about 28,600 addresses today; a sitemap holds 50,000, so it will need splitting into several files if the data grows past that), with the data set's as-of date as last-modified. After deploying, submit `https://companypaisa.com/sitemap.xml` in Google Search Console. |
| 2026-09-17 | **Company page: "at a glance" facts and similar companies** — requested by Zee (the space under Earnings was empty; asked for "cool facts about the company or stock price history"). Chose facts from data we already have; **stock price history deferred** (no price data today; free price feeds limit volume or forbid republishing, so it needs a provider decision and a refresh job). New public endpoint `GET /api/v1/companies/{ticker}/insights` (C# client `GetCompanyInsightsAsync`) returns the facts as typed fields (each null when not supported) plus similar companies; the website words them. Worked out from an in-memory index of every company's headquarters and indicators (`ICompanyStatsIndex`, rebuilt when the data set reloads) and cached per company like the profile. Not recorded in analytics (the page view already is). Layout and wording: §6.7. |
| 2026-09-17 | **List filters, kept simple** — requested by Zee ("I don't want to turn this page into action heavy"; the city is already chosen at the top). Discussed health / size chips, a city dropdown and pay bands; chose only: (1) a name or ticker box on the **company** ranked list, filtering in the browser and touching nothing else on the screen; (2) a **role** dropdown on the **executives** list next to the existing name / title box, filtered by the API (`GET /api/v1/executives/near?role=Ceo|Cfo|Coo|Technology|Legal|Other`, C# `ExecutivesNearRequest.Role`, part of the cache key and the search analytics) because executives load a page at a time. Both kept in the search address (`q`, `role`). Rules in §6.6 / §6.8. |
| 2026-09-17 | **New executives and their announced packages** — requested by Zee ("where a new CEO or CIO or CTO is hired and we don't have the earnings information yet but we know the sign-on package… Terrence at LifeVantage has over 8.5 million… scan all the companies"). Source: each SEC company's 8-K filings with **Item 5.02** (appointment of officers / compensatory arrangements) from the last 18 months (`Importer:NewHires:Months`). **Read by fixed rules, no AI** (Zee chose the free option over paying for the Claude API, about $18 once + $1–2 a month; the AI reader stays an option for filings the rules skip). Rules: find "appointed / named / hired X as <officer title>" (CEO, CFO, COO, chief … officer, president, general counsel, treasurer, controller, EVP / SVP; interim and acting appointments and board seats ignored); follow whose terms each sentence describes (the person named most recently; a sentence naming two people is skipped); keep a dollar amount only when the words right next to it say what it is (base salary, sign-on / make-whole cash, bonus in dollars, RSUs / restricted stock, PSUs, options, relocation or other allowances) and drop: severance and change-in-control terms, "up to" caps, monthly / hourly fees, per-share prices, director retainers, reimbursements, future years' annual targets and grants ("beginning in 2027…", "in 2027 he will also receive…"), a total when its pieces follow ("$6.4M, reflecting $3.875M plus $2.525M"), a "salary" under $50,000, and any single item over $200M or 50× the salary (filing typos). The headline is **every stated dollar amount added up** (Zee's choice), with the breakdown shown. Tried on 300 companies (945 officer-change 8-Ks, 93 packages read; most other 8-Ks are board elections and departures); LifeVantage reads $8,775,000 exactly ($850K salary, $425K guaranteed bonus, $2.8M RSUs, $4.7M PSUs, starting 5 Aug 2026). People are linked to existing pages by the company's pay rows, then the SEC insider list, then a name only one person in the data set has (Moorehead → his Nature's Sunshine page). Stored in a new `new_executives` table (package as JSON); published with the SEC market, or on its own with `--new-hires` (quicker; also what refreshes it between full imports). API: `newExecutives` in `GET /api/v1/companies/{ticker}/insights`; `newHire` / `hasProfile` on executives-near rows. Website: §6.7 New leadership card and At a glance fact; §6.8 New tag. |
| 2026-09-18 | **Pay vs similar companies** — requested by Zee ("similar companies who pay better or less… if the company in its sector is paying more or less, how competitive it is"). New `payVsPeers` in `GET /api/v1/companies/{ticker}/insights` (`PeerPay` service over the company stats index and the pay rows): the CEO's latest total pay (UK: the top-paid executive director) and the median of the other named executives, each against similar companies — same sector and filing type, revenue within ×3 (else the 20 closest in size), recent pay — as the peer median and the share of peers paid less; plus this company and the 8 peers closest in size, ranked by CEO pay. First version listed the companies just above and below in pay, but with 100+ peers they all paid about the same, so it lists the closest in size instead. Card on the company page, §6.7. |
| 2026-09-18 | **Pakistan (PSX), from companies' own annual reports** — requested by Zee ("can we build this public data for Pakistan?"; after the options were explained: "Lets do what we can"). The exchange's data portal shows financial tables, but they are licensed from a data vendor (Capital Stake), so they are **not used**; quarterly "financial results" announcements are scanned images. What is free and safe: the portal's list of listed companies (`dps.psx.com.pk/symbols`) and each company's profile page (business description, CEO, registered address, website, fiscal year end), and the annual report each company files with the exchange (`POST /announcements`, titles like "Transmission of Annual Report …", PDF at `/download/document/{id}.pdf`). New market `pk` (`Pk/PkImportPipeline`, settings `Importer:Pk`, `--market pk` or `--pk`): equities only (no ETFs, debt, funds, modarabas, rights or class-B shares) → profile → the two latest annual reports → text with positions (PdfPig, Apache 2.0; spreads of two printed pages are read left then right) → **rules**: the statement of profit or loss (heading, "for the year ended", a "2026  2025" header, the unit "Rupees in '000" etc., the sales line — "Total income" for banks — and profit after tax; the group's statement when there is one, owners' share of profit; analysis pages, multi-year summaries and US-dollar translations skipped) and the note "Remuneration of chief executive (or managing director), directors and executives" (the column under the chief executive's heading found by its position on the page; kept only when the items add up to the total, one person held the post, and the total is between Rs 1M and Rs 10bn). A statement headed "Rupees" whose figures are really in thousands is caught by checking earnings per share × shares (or, without EPS, market value over 500× sales) against the exchange profile. Scanned reports (no text) and companies without a report for a year ending in the last 24 months are left out. Location: the registered office's town (GeoNames towns of 5,000+), or the head / corporate office printed in the annual report when that is in another town (Lucky Cement: registered at its Pezu plant, run from Karachi). Only the extracted text of each report is cached (`data/cache/pk/reports`), not the PDF. Ticker suffix `.KA`; sectors mapped from the exchange's 36 sectors (its name kept as the industry); currency PKR shown as "Rs", ≈ $0.0036 per rupee; margins in paisa ("17 paisa of every Rs 1"). Website: Pakistan tab (postcodes sent as "PK-74000"), areas Karachi, Lahore, Islamabad–Rawalpindi, Faisalabad, Rest of Pakistan; postcode table `data/reference/pk-postcodes.csv` (a code takes its town's position). Pay vs similar companies compares Pakistani companies only with each other. No typical-worker pay benchmark for Pakistan yet. First run: 228 companies, 123 chief executives' pay. |

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
15. ~~**UK privacy contact**~~ — **Done:** `privacy@companypaisa.com` (Cloudflare Email Routing forwards it to Zee's inbox), shown in the privacy notice via `Ui:PrivacyContact`. Still worth a quick check with someone who knows UK data law.
16. **UK pay breakdown and careers** — totals come straight from the single-figure table, but the salary / bonus / long-term split only adds up exactly for part of the rows (the rest goes to Other), and UK directors aren't linked across companies (no SEC-style ID). Improve the table reader per layout; link people via Companies House officer ids?
17. **UK AIM** — needs a free Companies House API key (Zee to create at developer.company-information.service.gov.uk) and a list of AIM companies (the LSE's monthly issuer list, downloaded by hand, or OpenFIGI's London listings). Financials would come from the accounts filed at Companies House; per-director pay is rarely tagged there.
18. **Sectors for UK companies outside the FTSE 350** — no free source gives their industry; take SIC codes from Companies House once the key exists?
19. **Data outside the repo** — the monthly job commits `companypaisa.db` (26 MB; git compresses it), so the history grows each month. Options (2026-09-15): GitHub Releases (free; job attaches the .db to a dated release, Docker build downloads it — recommended), Cloudflare R2 (free to 10 GB), a Railway volume (~$0.25/GB/month), Git LFS (1 GB free bandwidth). Decide before the file nears GitHub's 100 MB limit. The SEC download cache (`data/cache`, several GB) also sits in the OneDrive folder.
20. **Germany and the rest of Europe** — Germany isn't on filings.xbrl.org; options are the Unternehmensregister (terms restrict bulk access), company websites, or German companies that also file with the SEC. Other EU countries on filings.xbrl.org (Belgium, Sweden, Denmark, Finland, Norway, Austria, Portugal, Ireland…) can be added by config: one `Importer:Eu:Countries` entry each (exchange codes, ticker suffix, areas).
21. **European executive pay** — remuneration reports are in the local language and often separate documents; decide per country whether to parse them (and add an EU privacy notice like the UK one).
22. **Pakistan: more companies and years** — scanned annual reports (no text) are left out; reading them needs OCR. Banks' and insurers' chief executives' pay sits in a different note ("compensation of directors and key management personnel") and isn't read yet. Only two reports per company are read (three years); the multi-year summaries in each report could give six. A typical-worker pay figure for Pakistan (Pakistan Bureau of Statistics Labour Force Survey) would enable the "every N hours" fact.
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
- **Phase 3e** (2026-09-14) — Whole US, Canada (SEC filers) and UK Main Market; monthly scheduled refresh on Zee's PC.
- **Phase 4** — Map view with self-hosted MapLibre/Protomaps base map; street-level geocoding; UK AIM.
- **Phase 5** — Swap storage to a database; optional "Size by", clustering, predictive features.
