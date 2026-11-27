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
| `CompanyPaisa.Api.csproj` | Workstation GC with conserve mode 9: the heap hands memory back (≈325 MB instead of ≈425 MB). Override with the `DOTNET_GCConserveMemory` variable (0–9) |

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
   | `Api__EnableSwaggerUi` | `true` / `false` | API docs page at `/swagger`; **off** in production (`appsettings.Production.json`). `/openapi/v1.json` stays |

   Any other appsettings value can be overridden the same way (e.g. `Search__DefaultRadiusMiles=25`).
4. **Networking → Generate Domain** (gets `*.up.railway.app`), then **Custom Domain → companypaisa.com**
   and add the DNS record Railway shows. Railway provides the HTTPS certificate.
   Search engines should see one copy of the site: `appsettings.Production.json` sets `Hosting:PublicOrigin` to
   `https://companypaisa.com`, so every page names that address as its canonical one and any request on another host
   (the `*.up.railway.app` address, `www.`) is sent there with a 301 — only `/health` still answers on every host, for
   Railway's health check. If the domain changes, set `Hosting__PublicOrigin` in the Railway variables (or empty it to
   turn the redirect off); with a wrong value the whole site redirects away.

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

**Adding another provider:** each store is an adapter project (`CompanyPaisa.Analytics.Sqlite`, `CompanyPaisa.Analytics.Postgres`) implementing
`IAnalyticsAdapter`. Add a project, reference it from the API, add it in `AddAnalyticsAdapters` (`ApiServiceCollectionExtensions`), and set
`Analytics__Provider` to its name. A name the build doesn't have stops the app at startup with the list it does have.

## Moving analytics from Postgres to SQLite (one service fewer)

At a few visits a day, a whole Postgres service for analytics costs more than it earns. The SQLite adapter keeps the same
tables in one file on a Railway volume, and `Analytics:ImportFrom` copies the history across on the first start.

1. **Back up Postgres first** (on your PC): `pg_dump "<DATABASE_PUBLIC_URL>" --format=custom --file=companypaisa-analytics.dump`
   (Postgres service → Variables → `DATABASE_PUBLIC_URL`).
2. **Add a volume:** website service → **Volumes → New Volume**, mount path `/data-volume` (1 GB is plenty; about $0.25 a month).
3. **Variables** on the website service:

   | Variable | Value | Notes |
   |---|---|---|
   | `Analytics__Provider` | `Sqlite` | Was `Postgres` (from `appsettings.Production.json`) |
   | `Analytics__SqlitePath` | `/data-volume/analytics.db` | On the volume, so it survives deploys |
   | `Analytics__ImportFrom__Provider` | `Postgres` | One-off: where the history is |
   | `Analytics__ImportFrom__ConnectionString` | `${{Postgres.DATABASE_URL}}` | One-off: Railway fills in the private address |

   Leave `Analytics__ConnectionString` for now; nothing reads it once the provider is Sqlite.
4. **Deploy and check the log** for `Analytics recording to Sqlite` and then `Copied N analytics events from Postgres into Sqlite`.
   Open `/admin` and compare the 90-day totals with what Postgres showed. If the log says
   `unable to open database file` / permission denied, the volume is owned by root while the app runs as a normal user:
   add `RAILWAY_RUN_UID=0` to the variables and deploy again.
5. **Clean up:** delete `Analytics__ImportFrom__Provider`, `Analytics__ImportFrom__ConnectionString` and `Analytics__ConnectionString`,
   then delete the Postgres service (keep the dump).

If the copy fails (Postgres asleep or unreachable for about a minute), the log says so, nothing is copied, and the site records
new events without the history. To try again: fix the variables, delete `/data-volume/analytics.db` and redeploy — the copy only
runs while the file has no events, so it never doubles up. With a volume attached, each deploy has a few seconds of downtime
(Railway can't attach one volume to two containers at once).

**Backups:** `/admin` → **Download events (CSV)** for any range, or copy the file off the volume.

## Who can use the API

The API isn't open to the public. A call gets in when it carries either:

- **An API key** (`X-Api-Key` header), for other apps. Keys live in Railway variables, one pair per app:
  `Api__Keys__0__Name` = `partner-name`, `Api__Keys__0__Key` = a long random string (the PowerShell line above makes one).
  Set `Api__Keys__0__Enabled` = `false` to switch a key off without deleting it.
- **The website's pass**, a cookie the site gets from `/api/session` when it starts (HttpOnly, sent only to `/api`,
  signed by the server, holding nothing but its expiry — no visitor id). Pages themselves set no cookie, so Cloudflare
  can cache them (below). Calls from another website's pages, and from scripts and scrapers
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

**Only believe Cloudflare's visitor address from Cloudflare.** The app reads each visitor's IP from Cloudflare's
`CF-Connecting-IP` header (`Api:ClientIpHeader`). Someone who sends requests straight to Railway, skipping Cloudflare,
can fake that header — with a Google crawler's address, to skip the hourly caps. `Api:ClientIpHeaderOnlyFromCloudflare`
ignores the header unless the connection comes from one of Cloudflare's networks. It is **off** until checked:

1. Deploy, browse the site for a few minutes, then open Railway → the service → **Logs** and search for
   `is not a Cloudflare address`.
2. **No such line:** real visitors arrive from Cloudflare addresses. In Railway → **Variables**, add
   `Api__ClientIpHeaderOnlyFromCloudflare` = `true` and redeploy.
3. **Lines for ordinary visits:** traffic reaches the app through another proxy first; leave the setting off (turned on,
   every visitor would share that proxy's address and the hourly caps) and look into the proxy's forwarding headers.

Lines that appear later, with the setting on, are someone going around Cloudflare; the setting already ignores them.

## Page caching (Cloudflare)

Rendered pages (`/`, `/company/…`, `/executive/…`, `/near/…`) are the same for every visitor and set no cookie, so the
app marks them `Cache-Control: public, max-age=300, s-maxage=86400`: Cloudflare may keep a page for a day, a browser for
five minutes (`Hosting:PageEdgeCacheSeconds`, `Hosting:PageBrowserCacheSeconds`; 0 turns caching off). Repeat visits and
Google's crawls are then answered by Cloudflare without waking the app. After every start (a deploy is how new data
arrives) the app waits a minute, for Railway to switch visitors to the new copy, then empties Cloudflare's cache.

The website's built files under `/assets/` are named after their content (`index-DaqpULkU.js`), so they never change:
they're sent `public, max-age=31536000, immutable` and browsers and Cloudflare keep them a year without asking again.
A deploy brings new names. `index.html` and `og-image.png` keep Cloudflare's default.

Cloudflare doesn't cache HTML until told to. Two things to set up, once:

1. **Cache rule.** Cloudflare dashboard → companypaisa.com → **Caching → Cache Rules → Create rule**, name *Pages*:
   *Custom filter*: `URI Path` *does not start with* `/api/` **and** `URI Path` *does not start with* `/admin`
   **and** `URI Path` *does not start with* `/health` → **Eligible for cache**; *Edge TTL*: **Use cache-control header
   if present, bypass cache if not**; *Browser TTL*: **Respect origin**. Deploy.
2. **Purge token.** **My Profile → API Tokens → Create Token → Custom token**: permission *Zone → Cache Purge → Purge*,
   zone resources *Include → Specific zone → companypaisa.com*. Copy the token. The zone ID is on the domain's
   **Overview** page (right-hand column, *API*). Then in Railway → **Variables**:

| Variable | Value |
|---|---|
| `EdgeCache__Provider` | `Cloudflare` |
| `EdgeCache__Cloudflare__ZoneId` | the zone ID |
| `EdgeCache__Cloudflare__ApiToken` | the token. **Secret.** |

After the next deploy the log says `Purged the Cloudflare cache…` about a minute after start. If it says it couldn't,
the message names Cloudflare's reason; meanwhile **Caching → Configuration → Purge Everything** does the same by hand.
`EdgeCache__PurgeDelaySeconds` changes the one-minute wait.

**Another CDN:** each provider is an adapter project (`CompanyPaisa.EdgeCache.Cloudflare`) implementing `IEdgeCacheAdapter`, the same way analytics stores work. Add a project, reference it from the API, add it in `AddEdgeCacheAdapters` (`ApiServiceCollectionExtensions`), and set `EdgeCache__Provider` to its name. A name the build doesn't have stops the app at startup with the list it does have; a known one with missing settings only logs a warning.

**Check it:** run the line below twice. The second answer should include `cf-cache-status: HIT`.

```powershell
curl.exe -sI https://companypaisa.com/company/AAPL | Select-String cf-cache-status
```

## Checks before and after deploying

Every push to `main` and every pull request runs the tests and the website build on GitHub
(`.github/workflows/ci.yml`; the **Actions** tab). Railway deploys `main` regardless, so look for a green tick there.


- `https://<domain>/health` → `Healthy`
- `https://<domain>/` → the website; enter 84043
- `https://<domain>/company/AAPL` → view the page source: the figures are in the HTML (what search engines read);
  opened twice, its `cf-cache-status` header says `HIT` (after the cache rule above)
- `https://<domain>/api/v1/companies/near?near=84043` in a new private window → `401 API key required` (the API is closed)
- `https://<domain>/swagger` → 404 (off in production; `Api__EnableSwaggerUi=true` turns it on). `/openapi/v1.json` → the API description

## Local rehearsal (without Docker)

```bash
dotnet publish src/CompanyPaisa.Api -c Release -o publish
```

Then run `publish/CompanyPaisa.Api.dll` with `PORT=8099` and `ASPNETCORE_ENVIRONMENT=Production`.
(`dotnet publish` without `-p:SkipSpaBuild=true` also builds the website into `publish/wwwroot`.)
