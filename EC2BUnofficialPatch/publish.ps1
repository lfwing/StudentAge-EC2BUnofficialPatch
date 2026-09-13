param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$BepInExCoreDir,
    [string]$Repository
)
$ErrorActionPreference = "Stop"
$integrationRoot = Split-Path $PSScriptRoot -Parent
if (!$BepInExCoreDir) { $BepInExCoreDir = Join-Path $GameDir "BepInEx/core" }
& python (Join-Path $integrationRoot "build.py") --game $GameDir --bepinex $BepInExCoreDir --layout all
if ($LASTEXITCODE -ne 0) { throw "Integration build failed" }
if ($Repository) {
    & python (Join-Path $integrationRoot "tools/prepare_release.py") --repository $Repository
    if ($LASTEXITCODE -ne 0) { throw "Release preparation failed" }
}
Write-Host "Integration release assets prepared locally. Nothing uploaded. Publish the schema 1 update.json (split) and update-merged.json with their matching DLL assets."
