param(
    [Parameter(Mandatory = $true)][string]$FeatureId,
    [Parameter(Mandatory = $true)][string]$RecordPath
)

$ErrorActionPreference = 'Stop'
$billingCredentials = @('OPENAI_API_KEY', 'OPENAI_ADMIN_KEY', 'CODEX_API_KEY', 'CODEX_ACCESS_TOKEN', 'OPENAI_BASE_URL', 'OPENAI_FEDERATION_RULE_ID', 'OPENAI_IDENTITY_TOKEN_FILE')
foreach ($name in $billingCredentials) {
    if ([Environment]::GetEnvironmentVariable($name)) {
        throw "Refusing credential or provider override $name. Use the approved ChatGPT subscription login without API billing credentials."
    }
}
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$configPath = Join-Path $codexHome 'config.toml'
if (Test-Path -LiteralPath $configPath) {
    $configText = Get-Content -LiteralPath $configPath -Raw
    if ($configText -match '(?im)^\s*\[model_providers\.' -or
        $configText -match '(?im)^\s*model_provider\s*=\s*["''](?!openai["''])') {
        throw 'A custom Codex model provider is configured. Remove or explicitly review it before enabling subscription recovery.'
    }
}
$codex = Get-Command codex -ErrorAction Stop
$loginStatus = & $codex.Source login status 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $loginStatus -notmatch '(?im)^Logged in using ChatGPT\s*$') {
    throw 'Codex CLI is not confirmed as logged in using ChatGPT; refusing to start a potentially API-billed run.'
}
$prompt = @"
Resume game feature '$FeatureId' in this repository from its durable record at '$RecordPath'.
Read AGENTS.md, Docs/CODEX_WORKFLOW.md, Docs/GAME_DESIGN_AUTHORITY.md, the feature specification and recovery checkpoints first.
Inspect git status, commits, tests and any existing pull request or issue before acting. Reconcile already completed work and do not repeat side effects.
Continue only from the last confirmed package checkpoint. Preserve unrelated changes. Do not merge, release, substitute models, or use paid API credentials.
If the account remains limited, authentication needs repair, or a manual decision is needed, stop and report that exact reason without changing the task payload.
Record the next confirmed package checkpoint and validation in the feature record. Follow all approval gates in the repository workflow.
"@
& $codex.Source exec --full-auto $prompt
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
