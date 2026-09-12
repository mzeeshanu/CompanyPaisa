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

## Checks after deploying

- `https://<domain>/health` → `Healthy`
- `https://<domain>/` → the website; enter 84043
- `https://<domain>/api/v1/companies/near?near=84043` → JSON
- `https://<domain>/swagger` → API docs (if enabled)

## Local rehearsal (without Docker)

```bash
dotnet publish src/CompanyPaisa.Api -c Release -o publish
```

Then run `publish/CompanyPaisa.Api.dll` with `PORT=8099` and `ASPNETCORE_ENVIRONMENT=Production`.
(`dotnet publish` without `-p:SkipSpaBuild=true` also builds the website into `publish/wwwroot`.)
