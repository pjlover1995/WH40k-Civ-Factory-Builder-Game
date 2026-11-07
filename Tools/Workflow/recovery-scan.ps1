$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $node) { exit 2 }
$featureTool = Join-Path $PSScriptRoot 'feature.mjs'
$recoveryTool = Join-Path $PSScriptRoot 'recovery.mjs'
$now = [DateTimeOffset]::UtcNow
foreach ($file in Get-ChildItem (Join-Path $root 'Docs\Features') -Filter '*.json' -File -ErrorAction SilentlyContinue) {
    try {
        $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $recovery = $record.recovery
        if (-not $recovery) { continue }
        $due = $recovery.status -in @('paused_usage_limit', 'paused_throttled') -and
            $recovery.nextAttemptAt -and [DateTimeOffset]::Parse($recovery.nextAttemptAt).ToUniversalTime() -le $now
        if ($due) {
            Push-Location $root
            try {
                & $node $recoveryTool tick $record.id
                if ($LASTEXITCODE -ne 0) { Write-Error "Retry failed for $($record.id) (exit $LASTEXITCODE)." }
            }
            finally { Pop-Location }
        }
        $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $recovery = $record.recovery
        if ($recovery.github -and $recovery.github.issue -and $recovery.github.owner -and $recovery.github.project) {
            Push-Location $root
            try {
                & $node $featureTool board $record.id $recovery.github.issue $recovery.github.owner $recovery.github.project
                if ($LASTEXITCODE -ne 0) { Write-Error "GitHub status update failed for $($record.id) (exit $LASTEXITCODE)." }
            }
            finally { Pop-Location }
        }
    } catch {
        Write-Error ("Recovery scan failed for {0}: {1}" -f $file.Name, $_.Exception.Message)
    }
}
