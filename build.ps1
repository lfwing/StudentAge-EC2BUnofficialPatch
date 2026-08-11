param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "EC2BUnofficialPatch.csproj"

dotnet build $projectPath `
    --configuration Release `
    "/p:GameDir=$GameDir"

if ($LASTEXITCODE -ne 0)
{
    throw "EC2BUnofficialPatch build failed."
}
