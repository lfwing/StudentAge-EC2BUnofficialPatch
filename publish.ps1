param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [string[]]$MirrorDownloadUrls = @()
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "EC2BUnofficialPatch.csproj"
[xml]$project = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version))
{
    throw "Could not read Version from the project file."
}

& (Join-Path $projectRoot "build.ps1") -GameDir $GameDir

$pluginDll = Join-Path $projectRoot "bin\Release\net472\EC2BUnofficialPatch.dll"
if (!(Test-Path -LiteralPath $pluginDll))
{
    throw "Release binary was not generated."
}

$distRoot = Join-Path $projectRoot "dist"
$releaseRoot = Join-Path $distRoot "EC2BUnofficialPatch-$version"
$pluginRoot = Join-Path $releaseRoot "BepInEx\plugins"
$contentRoot = Join-Path $pluginRoot "EC2BUnofficialPatch"
if (Test-Path -LiteralPath $releaseRoot)
{
    $resolvedDist = [IO.Path]::GetFullPath($distRoot).TrimEnd('\')
    $resolvedRelease = [IO.Path]::GetFullPath($releaseRoot)
    if (!$resolvedRelease.StartsWith($resolvedDist + "\", [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean a path outside dist."
    }
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $contentRoot -Force | Out-Null
Copy-Item -LiteralPath $pluginDll -Destination (Join-Path $pluginRoot "EC2BUnofficialPatch.dll")
Copy-Item -Path (Join-Path $projectRoot "ModAuthorTemplate\*") -Destination $contentRoot -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $releaseRoot

$hash = (Get-FileHash -LiteralPath $pluginDll -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $pluginDll).Length
$githubDownload = "https://github.com/lfwing/StudentAge-EC2BUnofficialPatch/releases/download/$version/EC2BUnofficialPatch.dll"
$downloadUrls = @($MirrorDownloadUrls | Where-Object { ![string]::IsNullOrWhiteSpace($_) }) + @($githubDownload)
$manifest = [ordered]@{
    schema = 1
    version = $version
    channel = "stable"
    gameVersion = "1.93"
    assetName = "EC2BUnofficialPatch.dll"
    size = $size
    sha256 = $hash
    releasePage = "https://github.com/lfwing/StudentAge-EC2BUnofficialPatch/releases/tag/$version"
    downloadUrls = $downloadUrls
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
$utf8NoBom = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $projectRoot "update.json"), $manifestJson + [Environment]::NewLine, $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $distRoot "update.json"), $manifestJson + [Environment]::NewLine, $utf8NoBom)
Copy-Item -LiteralPath $pluginDll -Destination (Join-Path $distRoot "EC2BUnofficialPatch.dll") -Force

$zipPath = Join-Path $distRoot "EC2BUnofficialPatch-$version-release.zip"
if (Test-Path -LiteralPath $zipPath)
{
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $releaseRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Release files generated:"
Write-Host "  $zipPath"
Write-Host "  $(Join-Path $distRoot 'EC2BUnofficialPatch.dll')"
Write-Host "  $(Join-Path $distRoot 'update.json')"
Write-Host "DLL SHA-256: $hash"
Write-Host "Upload the DLL to GitHub Release $version; commit update.json to the main branch."
