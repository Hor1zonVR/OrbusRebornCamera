
# OrbusRebornCamera - Automatic Release Publisher
# Builds locally, pushes source and publishes the DLL to GitHub.

param([string]$GameDir = "")

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

# Keep tracked project files safe. Benign untracked diagnostics files
# (build-error.txt, logs) do not need to block publishing.
$changes = & git status --porcelain --untracked-files=no
Check-Result "Checking Git status"

if ($changes) {
    throw "You have uncommitted edits to tracked source files. Commit or stash before publishing."
}

# Always get the GitHub version first so this ONE command builds the newest
# release that RebornManager will offer. Git refuses to overwrite local work.
Write-Host "[0/4] Syncing newest RC version from GitHub..." -ForegroundColor Yellow
& git pull --ff-only origin main
Check-Result "Pulling latest camera source"

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

# Build against an already-prepared game; do NOT install to this folder.
# Prefer RM's tested instance library to the older standalone download path.
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $library = Join-Path $env:LOCALAPPDATA "OrbusRebornModdingInstances"
    if (Test-Path $library) {
        $dirs = @(Get-ChildItem -LiteralPath $library -Directory | Sort-Object Name)
        $preferred = @($dirs | Where-Object { $_.Name -eq "My Orbus Renewed" })
        $preferred += @($dirs | Where-Object { $_.Name -ne "My Orbus Renewed" })
        foreach ($item in $preferred) {
            if (Test-Path (Join-Path $item.FullName "BepInEx\\interop\\Il2Cppmscorlib.dll") -PathType Leaf) {
                $GameDir = $item.FullName
                break
            }
        }
    }
}
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $fallback = Join-Path $env:USERPROFILE "Downloads\\OrbusVR-Reborn-Community-PC-MODTEST\\OrbusVR-Reborn-Community-PC"
    if (Test-Path (Join-Path $fallback "BepInEx\\interop\\Il2Cppmscorlib.dll") -PathType Leaf) {
        $GameDir = $fallback
    }
}
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw "No BepInEx-generated interop found. Run a modded RM game instance once, or pass -GameDir explicitly."
}
$GameDir = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($GameDir))
Write-Host "Compile references (NOT install destination): $GameDir" -ForegroundColor Cyan
& dotnet build $project -c Release "-p:GameDir=$GameDir"
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
