# RebornCam -> RebornManager stable release publisher.
# Builds with the installed game's generated BepInEx IL2CPP interop on Windows.
# WARNING: stable releases are offered to every RM user of this mod.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$repo = "Hor1zonVR/OrbusRebornCamera"

Write-Host "RC / REBORNMANAGER RELEASE PUBLISHER" -ForegroundColor Cyan
Write-Host "This publishes a public STABLE DLL. Every RM user may receive the update." -ForegroundColor Yellow
foreach ($cmd in @("git", "gh", "dotnet")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        throw "Missing $cmd. Install Git for Windows, GitHub CLI and the .NET SDK."
    }
}
if (Get-Process -Name vrclient,OrbusVR -ErrorAction SilentlyContinue) {
    throw "Close Orbus before building a release."
}
& gh auth status
if ($LASTEXITCODE -ne 0) { throw "Run 'gh auth login' first." }

$default = Join-Path $env:USERPROFILE "Downloads\OrbusVR-Reborn-Community-PC-MODTEST\OrbusVR-Reborn-Community-PC"
if (Test-Path (Join-Path $default "BepInEx\interop\Il2Cppmscorlib.dll")) {
    Write-Host "Suggested modded game: $default"
    $game = (Read-Host "Press Enter to use it or paste a different MODDED game directory").Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($game)) { $game = $default }
} else {
    $game = (Read-Host "Enter your MODDED Orbus game directory").Trim().Trim('"')
}
if ([string]::IsNullOrWhiteSpace($game)) { throw "A game folder is required." }
$game = [IO.Path]::GetFullPath($game)
foreach ($file in @(
    "BepInEx\interop\Il2Cppmscorlib.dll",
    "BepInEx\interop\UnityEngine.CoreModule.dll",
    "BepInEx\interop\UnityEngine.IMGUIModule.dll",
    "BepInEx\interop\UnityEngine.PhysicsModule.dll",
    "BepInEx\core\BepInEx.Unity.IL2CPP.dll"
)) {
    if (-not (Test-Path (Join-Path $game $file))) { throw "Missing file: $file" }
}

$root = Join-Path $env:TEMP ("RC-RM-Release-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
& git clone --depth 1 --single-branch --branch main "https://github.com/$repo.git" $root
if ($LASTEXITCODE -ne 0) { throw "Git clone failed." }
$source = Get-Content (Join-Path $root "OrbusRebornCamera\Plugin.cs") -Raw
$match = [regex]::Match($source, '(?s)\[BepInPlugin\(\s*"[^"]+"\s*,\s*"[^"]+"\s*,\s*"(?<version>\d+\.\d+\.\d+)"')
if (-not $match.Success) { throw "No version found in Plugin.cs." }
$tag = "v" + $match.Groups["version"].Value
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not determine commit." }

Write-Host "Building $tag against $game" -ForegroundColor Cyan
$project = Join-Path $root "OrbusRebornCamera\BetterMirror.csproj"
& dotnet build $project -c Release "-p:GameDir=$game"
if ($LASTEXITCODE -ne 0) { throw "Build failed. No release published. Source: $root" }
$dll = Join-Path $root "OrbusRebornCamera\bin\Release\net6.0\BetterMirror.dll"
if (-not (Test-Path $dll)) { throw "DLL missing despite build success." }

Write-Host "Built DLL: $dll" -ForegroundColor Green
Write-Host "WARNING: This is untested desktop diagnostic code, not working flatscreen mode." -ForegroundColor Yellow
Write-Host "Publishing a stable release will make it available to ALL RM users." -ForegroundColor Yellow
$confirm = Read-Host "Type PUBLISH to create stable GitHub Release $tag"
if ($confirm -cne "PUBLISH") {
    Write-Host "Cancelled. Build retained at $dll"
    exit 0
}

& gh release create $tag $dll --repo $repo --target $commit --title "OrbusRebornCamera $tag" --notes "Experimental desktop startup diagnostics (F1), freecam and keyframe foundations. Headset-free server joining and laser-pointer UI interactions are NOT implemented or verified. Test in a separate modded instance."
if ($LASTEXITCODE -ne 0) { throw "GitHub release publishing failed." }
& gh release view $tag --repo $repo --json tagName,assets,url
if ($LASTEXITCODE -ne 0) { throw "Published release could not be verified." }
Write-Host "Published $tag. RebornManager will detect the new stable release." -ForegroundColor Green
