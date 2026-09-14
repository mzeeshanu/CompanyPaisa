<#
.SYNOPSIS
  Creates (or updates) the Windows scheduled task that runs tools/refresh-data.ps1 once a month.

.DESCRIPTION
      powershell -ExecutionPolicy Bypass -File tools\register-refresh-task.ps1              # 2nd of each month, 2:00 am
      powershell -ExecutionPolicy Bypass -File tools\register-refresh-task.ps1 -Day 15 -At 23:00
      powershell -ExecutionPolicy Bypass -File tools\register-refresh-task.ps1 -Remove

  The task runs as you, only while you're signed in (no stored password). If the PC is off or asleep at the
  scheduled time, it runs as soon as it's back on. Run it now from Task Scheduler, or: Start-ScheduledTask 'CompanyPaisa data refresh'
#>
param(
    [ValidateRange(1, 28)][int]$Day = 2,
    [string]$At = '02:00',
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$name = 'CompanyPaisa data refresh'

if ($Remove) {
    Unregister-ScheduledTask -TaskName $name -Confirm:$false
    Write-Host "Removed the '$name' task."
    return
}

$script = Join-Path $PSScriptRoot 'refresh-data.ps1'
if ($script.Contains(' ')) { throw "The repo path has a space in it ($script); move it or quote the path in the task by hand." }

# The ScheduledTasks cmdlets can't make a monthly trigger, so create the task with schtasks (/IT = only while signed in,
# no stored password), then add the settings schtasks can't express.
schtasks.exe /Create /F /IT /TN $name /SC MONTHLY /D $Day /ST $At `
    /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $script" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "schtasks couldn't create the task (exit code $LASTEXITCODE)." }

$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 12) -MultipleInstances IgnoreNew
Set-ScheduledTask -TaskName $name -Settings $settings | Out-Null
$next = (Get-ScheduledTaskInfo -TaskName $name).NextRunTime
Write-Host "Scheduled '$name': day $Day of every month at $At. Next run: $next"
