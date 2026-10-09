
# OrbusRebornCamera - Automatic Release Publisher
# Builds locally, pushes source and publishes the DLL to GitHub.

$ErrorActionPreference = "Stop"

$repo = "Hor1zonVR/OrbusRebornCamera"
$root = $PSScriptRoot
$project = Join-Path $root "OrbusRebornCamera\BetterMirror.csproj"
$pluginFile = Join-Path $root "OrbusRebornCamera\Plugin.cs"
$dll = Join-Path $root "OrbusRebornCamera\bin\Release\net6.0\BetterMirror.dll"

Set-Location $root

function Check-Result {
    param([string]$Step)

    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed (exit code $LASTEXITCODE). Release cancelled."
    }
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "   OrbusRebornCamera Release Tool" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Check required tools.
foreach ($tool in @("git", "dotnet", "gh")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Missing required tool: $tool"
    }
}

# Confirm we are on the main branch.
$branch = (& git branch --show-current).Trim()
Check-Result "Checking Git branch"

if ($branch -ne "main") {
    throw "Switch to the main branch before publishing."
}

# Only publish committed changes.
$changes = & git status --porcelain
Check-Result "Checking Git status"

if ($changes) {
    throw "You have uncommitted changes. Commit them before publishing."
}

# Read the actual plugin version from Plugin.cs.
$source = Get-Content $pluginFile -Raw

$pattern = '(?s)\[BepInPlugin\(\s*"[^"]+"\s*,\s*"[^"]+"\s*,\s*"(?<version>\d+\.\d+\.\d+)"'

$match = [regex]::Match($source, $pattern)

if (-not $match.Success) {
    throw "Could not find the plugin version in Plugin.cs."
}

$version = $match.Groups["version"].Value
$tag = "v$version"

Write-Host "Plugin version: $version" -ForegroundColor Green
Write-Host "Release tag:    $tag"
Write-Host ""

# Build the camera.
Write-Host "[1/4] Building camera DLL..." -ForegroundColor Yellow

& dotnet build $project -c Release
Check-Result "Camera build"

if (-not (Test-Path $dll)) {
    throw "Build completed, but BetterMirror.dll was not found."
}

Write-Host "Build succeeded!" -ForegroundColor Green

# Push latest committed source to GitHub.
Write-Host ""
Write-Host "[2/4] Pushing source to GitHub..." -ForegroundColor Yellow

& git push origin main
Check-Result "Git push"

$commit = (& git rev-parse HEAD).Trim()
Check-Result "Reading commit"

# Publish the release and upload the DLL.
Write-Host ""
Write-Host "[3/4] Publishing $tag..." -ForegroundColor Yellow

& gh release create $tag $dll `
    --repo $repo `
    --target $commit `
    --title "OrbusRebornCamera $tag" `
    --generate-notes

Check-Result "GitHub release"

# Confirm the release exists.
Write-Host ""
Write-Host "[4/4] Checking published release..." -ForegroundColor Yellow

& gh release view $tag --repo $repo
Check-Result "Release verification"

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host "   RELEASE PUBLISHED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green
Write-Host ""
Write-Host "Version: $tag"
Write-Host "DLL: BetterMirror.dll"
Write-Host "URL: https://github.com/$repo/releases/tag/$tag" -ForegroundColor Cyan
