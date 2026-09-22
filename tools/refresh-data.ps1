<#
.SYNOPSIS
  Scheduled data refresh: rebuilds the US + Canada (SEC), UK (Main Market) and European (ESEF) workbooks, then commits
  and pushes them so Railway redeploys the site with fresh data.

.DESCRIPTION
  Run by the Windows scheduled task "CompanyPaisa data refresh" (see tools/register-refresh-task.ps1), or by hand:
      powershell -ExecutionPolicy Bypass -File tools\refresh-data.ps1            # import, commit, push
      powershell -ExecutionPolicy Bypass -File tools\refresh-data.ps1 -NoPush    # import and commit only

  Safety:
  - Nothing is committed unless all three importers finish and verify their workbooks (they only swap a workbook in after
    reading it back with the API's own loader).
  - It refuses to run if you have uncommitted changes outside data/, so it never sweeps your work into a data commit.
  - Only data files are staged.
  Logs: %LOCALAPPDATA%\CompanyPaisa\logs\refresh-<date>.log (the last 12 are kept).
#>
param([switch]$NoPush)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $env:LOCALAPPDATA 'CompanyPaisa\logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir ("refresh-{0:yyyy-MM-dd-HHmm}.log" -f (Get-Date))

function Write-Log([string]$message) {
    $line = "{0:yyyy-MM-dd HH:mm:ss}  {1}" -f (Get-Date), $message
    Add-Content -Path $log -Value $line -Encoding utf8
    Write-Host $line
}

# Runs a native command, appending its output to the log; throws if it fails.
function Invoke-Step([string]$name, [string]$exe, [string[]]$arguments) {
    Write-Log "== $name"
    $out = Join-Path $logDir 'step.out'
    $err = Join-Path $logDir 'step.err'
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $repo -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError $err
    Get-Content $out, $err -ErrorAction SilentlyContinue | Add-Content -Path $log -Encoding utf8
    if ($p.ExitCode -ne 0) { throw "$name failed with exit code $($p.ExitCode). See $log" }
}

$dataFiles = @(
    'data/companypaisa.db',
    'data/import-report.md', 'data/import-report-uk.md', 'data/import-report-eu.md', 'data/reference/eu-postcodes.csv', 'data/curated/eu-companies.csv', 'data/reference/anz-postcodes.csv', 'data/import-report-pk.md', 'data/reference/pk-postcodes.csv',
    'data/reference/company-sites.csv', 'data/reference/geocoded-locations.csv', 'data/import-report-enrichment.md',
    'data/reference/employer-aliases.csv', 'data/import-report-salaries.md', 'data/reference/job-boards.csv', 'data/import-report-postings.md',
    'data/reference/us-zip-centroids.csv', 'data/reference/ca-postal-areas.csv', 'data/reference/uk-postcode-districts.csv',
    'data/curated/uk-ftse350.csv', 'data/curated/uk-main-market.csv', 'data/validation-report.md'
)

try {
    Set-Location $repo
    Write-Log "CompanyPaisa data refresh started in $repo"

    # Don't mix someone's unfinished work into a data commit.
    $dirty = git status --porcelain | Where-Object { $_ -notmatch '^\s*\S+\s+"?data/' }
    if ($dirty) { throw "Uncommitted changes outside data/ - commit or stash them first:`n$($dirty -join "`n")" }

    Invoke-Step 'git checkout main' 'git' @('checkout', 'main')
    Invoke-Step 'git pull' 'git' @('pull', '--ff-only')

    # Every market (US/Canada/Australia SEC filers, UK, Europe) publishes into data/companypaisa.db, then the data-quality
    # checks run with --strict: a failed market or any error-level finding stops the run before anything is published.
    Invoke-Step 'Import every market, then validate' 'dotnet' @('run', '--project', 'tools/CompanyPaisa.Importer', '-c', 'Release', '--', '--all', '--refresh-lists', '--strict')
    # Websites, careers pages and street positions for companies that are new or due a recheck (the rest are kept).
    Invoke-Step 'Websites, careers pages, street positions' 'dotnet' @('run', '--project', 'tools/CompanyPaisa.Importer', '-c', 'Release', '--', '--enrich')
    # Salaries by job title from the Department of Labor's H-1B wage filings (a new quarter's file is downloaded once).
    Invoke-Step 'Salaries by job title' 'dotnet' @('run', '--project', 'tools/CompanyPaisa.Importer', '-c', 'Release', '--', '--salaries')
    # Pay ranges from the companies' own job ads; each run adds the ads open that day to the history kept in data/cache.
    Invoke-Step 'Salaries from job ads' 'dotnet' @('run', '--project', 'tools/CompanyPaisa.Importer', '-c', 'Release', '--', '--postings')

    $existing = $dataFiles | Where-Object { Test-Path (Join-Path $repo $_) }
    Invoke-Step 'git add' 'git' (@('add', '--') + $existing)
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Log 'No data changed; nothing to commit.'; exit 0 }

    $message = "Data refresh {0:yyyy-MM-dd}`n`nAutomatic run of tools/refresh-data.ps1: SEC EDGAR (US + Canada), UK Main Market and European ESEF reports." -f (Get-Date)
    $msgFile = Join-Path $logDir 'commit-message.txt'
    Set-Content -Path $msgFile -Value $message -Encoding utf8
    Invoke-Step 'git commit' 'git' @('commit', '-F', $msgFile)

    if ($NoPush) { Write-Log 'Committed; -NoPush given, so not pushing.' }
    else { Invoke-Step 'git push' 'git' @('push', 'origin', 'main'); Write-Log 'Pushed; Railway will redeploy with the new data.' }
    Write-Log 'Done.'
}
catch {
    Write-Log "FAILED: $($_.Exception.Message)"
    exit 1
}
finally {
    Get-ChildItem $logDir -Filter 'refresh-*.log' | Sort-Object LastWriteTime -Descending | Select-Object -Skip 12 | Remove-Item -Force -ErrorAction SilentlyContinue
}
