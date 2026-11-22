<#
.SYNOPSIS
  Runs tools/edgar/extract.py (EdgarTools cross-check) in its own pinned Python environment, creating it the first time.

.DESCRIPTION
  The environment lives in tools/edgar/.venv (not committed) and uses Python 3.13: EdgarTools needs pyarrow, which may
  not have wheels for newer Pythons yet. It is rebuilt when requirements.txt changes. Arguments go to extract.py:
      powershell -ExecutionPolicy Bypass -File tools\edgar\run.ps1 --tickers GS,AAPL
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$venv = Join-Path $here '.venv'
$python = Join-Path $venv 'Scripts\python.exe'
$requirements = Join-Path $here 'requirements.txt'
$stamp = Join-Path $venv 'requirements.installed'

$wanted = Get-Content $requirements -Raw
if (-not (Test-Path $python) -or -not (Test-Path $stamp) -or (Get-Content $stamp -Raw) -ne $wanted) {
    Write-Host 'Setting up the EdgarTools Python environment (tools/edgar/.venv)...'
    if (Test-Path $venv) { Remove-Item -Recurse -Force $venv }
    & py -3.13 -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the environment: Python 3.13 is needed (py -3.13).' }
    & $python -m pip install --quiet --disable-pip-version-check -r $requirements
    if ($LASTEXITCODE -ne 0) { throw 'pip install failed.' }
    Set-Content -Path $stamp -Value $wanted -NoNewline -Encoding utf8
}

# -I: isolated mode, so nothing in the working directory can shadow a library module.
& $python -I (Join-Path $here 'extract.py') @args
exit $LASTEXITCODE
