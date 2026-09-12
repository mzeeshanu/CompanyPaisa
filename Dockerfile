# CompanyPaisa — one container: ASP.NET Core API + the React website in wwwroot.
# Used by Railway (see railway.json); works with any Docker host.

# ---------- 1. Build the React website ----------
FROM node:22-alpine AS web
WORKDIR /web
COPY src/CompanyPaisa.Web/package.json src/CompanyPaisa.Web/package-lock.json ./
RUN npm ci --no-fund --no-audit
COPY src/CompanyPaisa.Web/ ./
RUN npx tsc -b && npx vite build --outDir /web-dist --emptyOutDir

# ---------- 2. Build and publish the API ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
COPY data/ data/
RUN dotnet publish src/CompanyPaisa.Api/CompanyPaisa.Api.csproj -c Release -o /app -p:SkipSpaBuild=true
COPY --from=web /web-dist /app/wwwroot

# ---------- 3. Runtime image ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
# Railway sets PORT at runtime; Program.cs listens on it. 8080 is the default otherwise.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CompanyPaisa.Api.dll"]
