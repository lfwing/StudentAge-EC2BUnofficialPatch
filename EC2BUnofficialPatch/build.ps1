param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "EC2BUnofficialPatch.csproj"
$updaterProjectPath = Join-Path $PSScriptRoot "UpdaterHelper\EC2BUnofficialPatch.Updater.csproj"

dotnet build $projectPath `
    --configuration Release `
    "/p:GameDir=$GameDir"

if ($LASTEXITCODE -ne 0)
{
    throw "EC2BUnofficialPatch build failed."
}

dotnet build $updaterProjectPath `
    --configuration Release

if ($LASTEXITCODE -ne 0)
{
    throw "EC2BUnofficialPatch updater helper build failed."
}
