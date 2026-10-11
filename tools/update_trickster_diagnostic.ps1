# Update RebornCam from GitHub, compile against the local Orbus client,
# and install the updated DLL. Run while located in OrbusRebornCamera-1.
# Does not install Python, modify the game's Unity assets, or start the game.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = (Get-Location).Path
$projectDir = Join-Path $root "OrbusRebornCamera"
$projectFile = Join-Path $projectDir "BetterMirror.csproj"
if (-not (Test-Path $projectFile)) {
    throw "Open PowerShell inside your OrbusRebornCamera-1 folder first. Missing: $projectFile"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK isn't on PATH. This is required to compile your BepInEx mod."
}
if (Get-Process -Name vrclient,OrbusVR -ErrorAction SilentlyContinue) {
    throw "Close OrbusVR before replacing its RebornCam DLL, then rerun this script."
}

$game = Join-Path $env:USERPROFILE "Downloads\OrbusVR-Reborn-Community-PC-MODTEST\OrbusVR-Reborn-Community-PC"
if (Test-Path (Join-Path $game "BepInEx\interop\UnityEngine.CoreModule.dll")) {
    Write-Host "Found the old modded Reborn test game:" -ForegroundColor Cyan
    Write-Host $game
    $confirm = Read-Host "Use that game? Press Enter for yes, or N for Renewed/another instance"
    if ($confirm -match '^(?i)n(o)?$') {
        $game = (Read-Host "Paste the full path to the MODDED Orbus game folder").Trim().Trim('"')
    }
} else {
    Write-Host "Please choose your modded Orbus game." -ForegroundColor Yellow
    $game = (Read-Host "Paste the full path to the MODDED Orbus game folder").Trim().Trim('"')
}
if (-not (Test-Path (Join-Path $game "BepInEx\interop\UnityEngine.CoreModule.dll"))) {
    throw "No generated IL2CPP interop DLLs found at $game. Choose your modded copy with BepInEx."
}
$pluginDir = Join-Path $game "BepInEx\plugins"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

$base = "https://raw.githubusercontent.com/Hor1zonVR/OrbusRebornCamera/main/OrbusRebornCamera"
$files = @("Plugin.cs", "TricksterAssetDiagnostics.cs", "TricksterDiskDiagnostics.cs")
foreach ($file in $files) {
    Write-Host "Updating $file..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri "$base/$file" -OutFile (Join-Path $projectDir $file)
}

Write-Host "Building RebornCam against your game DLLs..." -ForegroundColor Cyan
& dotnet build $projectFile -c Release "-p:GameDir=$game"
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed. Your installed BetterMirror.dll was left unchanged."
}
$dll = Join-Path $projectDir "bin\Release\net6.0\BetterMirror.dll"
if (-not (Test-Path $dll)) {
    throw "Build completed but the compiled DLL is missing: $dll"
}

$existing = @(Get-ChildItem -Path $pluginDir -Filter BetterMirror.dll -Recurse -File -ErrorAction SilentlyContinue)
$dest = if ($existing.Count -gt 0) {
    $existing[0].FullName
} else {
    Join-Path $pluginDir "BetterMirror.dll"
}
if (Test-Path $dest) {
    Copy-Item -Path $dest -Destination ($dest + ".bak") -Force
}
Copy-Item -Path $dll -Destination $dest -Force

Write-Host ""
Write-Host "RebornCam updated and installed!" -ForegroundColor Green
Write-Host "Plugin: $dest"
Write-Host "Now launch Orbus and play normally. About 25 seconds after launch,"
Write-Host "the asset scan begins in the background (once per installation)."
Write-Host "When it finishes, send BepInEx\TricksterAssetReport.txt to ChatGPT."
