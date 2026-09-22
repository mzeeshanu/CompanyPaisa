# Deploying CompanyPaisa to Railway

CompanyPaisa deploys as **one Railway service**: a Docker image containing the ASP.NET Core API with the
React website in `wwwroot`. Railway builds it from the repo's `Dockerfile` (selected by `railway.json`).

## What's already set up

| File | Purpose |
|---|---|
| `Dockerfile` | 3 stages: Node builds the website → .NET SDK publishes the API (+ data files) → small ASP.NET runtime image |
| `.dockerignore` | Keeps bin/obj/node_modules/tests out of the build |
| `railway.json` | Dockerfile builder, health check on `/health`, restart on failure |
| `appsettings.Production.json` | Data paths inside the container, trusts Railway's proxy headers |
| `Program.cs` | Listens on Railway's `PORT` (IPv4 + IPv6); forwarded headers so rate limits see real visitor IPs and https is detected |

## First deployment

1. **Push the repo to GitHub** (needs Git installed).
2. In Railway: **New Project → Deploy from GitHub repo** → pick the repo. Railway finds `railway.json` and builds the Dockerfile.
   (Alternative without GitHub: install the Railway CLI and run `railway up` from the repo root.)
3. **Variables** (service → Variables). Double underscore `__` means a nested setting:

   | Variable | Example | Notes |
   |---|---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` | Already set in the image |
   | `Api__Keys__0__Name` | `my-other-app` | One entry per external app |
   | `Api__Keys__0__Key` | a long random string (≥ 16 chars) | **Secret** — never commit keys |
   | `Api__CorsAllowedOrigins__0` | `https://myotherapp.com` | Only if another *website* calls the API from the browser |
   | `Api__EnableSwaggerUi` | `true` / `false` | Public API docs at `/swagger` |

   Any other appsettings value can be overridden the same way (e.g. `Search__DefaultRadiusMiles=25`).
4. **Networking → Generate Domain** (gets `*.up.railway.app`), then **Custom Domain → companypaisa.com**
   and add the DNS record Railway shows. Railway provides the HTTPS certificate.

## Updating data

The sample workbook is baked into the image, so today **changing data = commit the new workbook + redeploy**.

When the real data starts changing more often, switch to a **Railway volume** so data can be replaced without a
redeploy (the API reloads the workbook automatically when the file changes):

1. Service → **Volumes → New Volume**, mount path `/data`.
2. Upload the workbook and ZIP table to the volume.
3. Set `DataSource__Excel__Path=/data/companypaisa.xlsx` and `Geo__ZipTablePath=/data/us-zip-centroids.csv`.

Later, a database (Railway Postgres) plugs in through `DataSource__Provider` + `DataSource__ConnectionString`
once the SQL repository exists.

## Visitor analytics (Postgres)

The site records its own analytics (visits, areas searched, companies and executives opened, a few clicks) into a
**Railway Postgres** database and shows them on a private page, `/admin`. Until the variables below are set it records
nothing and the site works as before.

1. **Create the database:** in the project, **+ Create → Database → PostgreSQL**. Railway names the service `Postgres`.
2. **Connect the website:** website service → **Variables**:

   | Variable | Value | Notes |
   |---|---|---|
   | `Analytics__ConnectionString` | `${{Postgres.DATABASE_URL}}` | A reference: Railway fills in the private address and password |
   | `Analytics__DashboardKey` | a long random string | **Secret.** The key for `/admin`. Make one with the PowerShell line below |

   ```powershell
   $b = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); -join ($b | ForEach-Object { $_.ToString('x2') })
   ```
3. **Deploy.** The log shows `Analytics recording to Postgres`; the tables are created on first start.
4. **Cities (Cloudflare):** Cloudflare dashboard → companypaisa.com → **Rules → Transform Rules → Managed Transforms** → turn on
   **Add visitor location headers**. Country works without it; city and region need it. Free.
5. **Open the dashboard:** `https://companypaisa.com/admin` → paste the key → tick **Don't count this browser** on each of
   your own phones and computers.
6. **Usage alert:** Workspace → Usage → add an email alert (e.g. $7) below your usage limit. A hard limit stops the whole site.

**What is stored:** event name, what it was about (ticker, person id, area label), the search point rounded to ~1 km, city /
region / country from Cloudflare, device / browser / OS family, the referring site's host, and a visitor code that changes
every day (a hash of IP + browser with a random daily salt that is deleted after two days). No IP addresses, and no cookies
except the owner's "don't count me" cookie.

**Download / back up:** `/admin` has CSV downloads (each table, and every event for a range). A full copy needs `pg_dump` on
your PC (`winget install PostgreSQL.PostgreSQL.17`) and the database's **public** URL (Postgres service → Variables →
`DATABASE_PUBLIC_URL`):

```bash
pg_dump "<DATABASE_PUBLIC_URL>" --format=custom --file=companypaisa-analytics.dump
```

**Moving to another host (e.g. Azure Database for PostgreSQL):** `pg_restore --no-owner --dbname "<new url>" companypaisa-analytics.dump`,
then change `Analytics__ConnectionString`. Nothing in the code changes; `Analytics:Provider` also accepts `Sqlite` (a file) or `None`.

## Who can use the API

The API isn't open to the public. A call gets in when it carries either:

- **An API key** (`X-Api-Key` header), for other apps. Keys live in Railway variables, one pair per app:
  `Api__Keys__0__Name` = `partner-name`, `Api__Keys__0__Key` = a long random string (the PowerShell line above makes one).
  Set `Api__Keys__0__Enabled` = `false` to switch a key off without deleting it.
- **The website's pass**, a cookie every page of the site hands out (HttpOnly, sent only to `/api`, signed by the server,
  holding nothing but its expiry — no visitor id). Calls from another website's pages, and from scripts and scrapers
  (curl, python-requests, headless Chrome… — `Api:BlockedUserAgents`), are refused.

One Railway variable to set, so passes survive a redeploy (otherwise open pages quietly fetch a new one):

| Variable | Value |
|---|---|
| `Api__SiteSession__Secret` | a long random string (the PowerShell line above). **Secret.** |

Limits per visitor IP without a key: 120 API calls a minute and 2,000 an hour; 1,200 server-rendered pages an hour
(`Api:RateLimits`). With a key: 1,200 a minute.

**Cloudflare (dashboard, free plan)** — the bot defences in front of the app:

1. **Security → Bots → Bot Fight Mode: On.** Challenges known bad bots before they reach Railway.
2. **Security → Bots → Block AI bots: On** if you don't want AI crawlers copying the pages (your call: it also keeps
   the site out of some AI answers).
3. **Security → WAF → Rate limiting rules → Create rule**: *URI Path starts with `/api/`*, *same IP*, *more than 100
   requests in 10 seconds* → *Block for 10 seconds*. Catches floods before they cost anything.

## Checks after deploying

- `https://<domain>/health` → `Healthy`
- `https://<domain>/` → the website; enter 84043
- `https://<domain>/company/AAPL` → view the page source: the figures are in the HTML (what search engines read)
- `https://<domain>/api/v1/companies/near?near=84043` in a new private window → `401 API key required` (the API is closed)
- `https://<domain>/swagger` → API docs (if enabled)

## Local rehearsal (without Docker)

```bash
dotnet publish src/CompanyPaisa.Api -c Release -o publish
```

Then run `publish/CompanyPaisa.Api.dll` with `PORT=8099` and `ASPNETCORE_ENVIRONMENT=Production`.
(`dotnet publish` without `-p:SkipSpaBuild=true` also builds the website into `publish/wwwroot`.)
