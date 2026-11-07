$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scan = Join-Path $PSScriptRoot 'recovery-scan.ps1'
$taskName = 'WH30K Codex Usage Recovery'
$powerShell = Join-Path $PSHOME 'powershell.exe'
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$taskCommand = '"{0}" -NoProfile -ExecutionPolicy Bypass -File "{1}"' -f $powerShell, $scan
& (Join-Path $env:SystemRoot 'System32\schtasks.exe') /Create /TN $taskName /TR $taskCommand /SC MINUTE /MO 15 /F /RU $user /IT | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Task Scheduler rejected the recovery task (exit $LASTEXITCODE)." }
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2) -MultipleInstances IgnoreNew
Set-ScheduledTask -TaskName $taskName -Settings $settings | Out-Null
Write-Output "Installed '$taskName'. It runs as $user every 15 minutes while that user is signed in."
