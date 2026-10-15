# Installs the experimental RC desktop-startup diagnostic build into ONE modded
# Orbus instance. It never edits the original/unmodded game installation.
# Freecam/menu navigation without a headset is NOT guaranteed by this probe.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

Write-Host ""
Write-Host "REBORNCAM - DESKTOP STARTUP PROBE v0.1 (EXPERIMENTAL)" -ForegroundColor Cyan
Write-Host "Tests XR initialization, menu camera visibility and mouse physics raycasts."
Write-Host "It does NOT provide working virtual hands or menu clicks yet." -ForegroundColor Yellow
Write-Host ""

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found. Install .NET SDK or use the existing RebornCam dev PC."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git was not found. Install Git for Windows to fetch this test branch."
}
if (Get-Process -Name vrclient,OrbusVR -ErrorAction SilentlyContinue) {
    throw "Close OrbusVR before updating its mod DLL."
}

$default = Join-Path $env:USERPROFILE "Downloads\OrbusVR-Reborn-Community-PC-MODTEST\OrbusVR-Reborn-Community-PC"
if (Test-Path (Join-Path $default "BepInEx\interop\Il2Cppmscorlib.dll")) {
    Write-Host "Suggested test instance: $default"
    $game = (Read-Host "Press Enter to use it or paste another MODDED game directory").Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($game)) { $game = $default }
} else {
    $game = (Read-Host "Paste the MODDED game directory containing BepInEx").Trim().Trim('"')
}
if ([string]::IsNullOrWhiteSpace($game)) { throw "Game directory required." }
$game = [IO.Path]::GetFullPath($game)

$interop = Join-Path $game "BepInEx\interop"
$core = Join-Path $game "BepInEx\core"
foreach ($required in @(
    (Join-Path $interop "Il2Cppmscorlib.dll"),
    (Join-Path $interop "UnityEngine.CoreModule.dll"),
    (Join-Path $interop "UnityEngine.IMGUIModule.dll"),
    (Join-Path $interop "UnityEngine.PhysicsModule.dll"),
    (Join-Path $core "BepInEx.Unity.IL2CPP.dll")
)) {
    if (-not (Test-Path $required)) { throw "Missing dependency: $required" }
}
$plugins = Join-Path $game "BepInEx\plugins"
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
$existing = @(Get-ChildItem -Path $plugins -Filter BetterMirror.dll -Recurse -File -ErrorAction SilentlyContinue)
if ($existing.Count -gt 1) {
    throw "More than one BetterMirror.dll exists. Fix duplicates first: $($existing.FullName -join '; ')"
}
$installed = if ($existing.Count -eq 1) { $existing[0].FullName } else { Join-Path $plugins "BetterMirror.dll" }

$root = Join-Path $env:TEMP ("RC-Desktop-Probe-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$repo = "https://github.com/Hor1zonVR/OrbusRebornCamera.git"
Write-Host ""
Write-Host "Downloading isolated desktop probe source..." -ForegroundColor Cyan
& git clone --depth 1 --branch "feature/desktop-startup-probe-v0.1" --single-branch $repo $root
if ($LASTEXITCODE -ne 0) { throw "Failed to download the desktop test branch." }

$project = Join-Path $root "OrbusRebornCamera\BetterMirror.csproj"
Write-Host "Building against your installed Orbus interop DLLs..." -ForegroundColor Cyan
& dotnet build $project -c Release "-p:GameDir=$game"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed. Installed BetterMirror.dll was NOT changed. Source: $root"
}
$dll = Join-Path $root "OrbusRebornCamera\bin\Release\net6.0\BetterMirror.dll"
if (-not (Test-Path $dll)) { throw "Build returned success but BetterMirror.dll was not produced." }

$backup = $null
if (Test-Path $installed) {
    $backup = $installed + ".bak-desktopprobe-" + (Get-Date -Format "yyyyMMdd-HHmmss")
    Copy-Item -LiteralPath $installed -Destination $backup
}
Copy-Item -LiteralPath $dll -Destination $installed -Force

Write-Host ""
Write-Host "DESKTOP PROBE INSTALLED" -ForegroundColor Green
Write-Host "Game:      $game"
Write-Host "DLL:       $installed"
if ($backup) { Write-Host "Backup:    $backup" }
Write-Host "Build:     $root"
Write-Host ""
Write-Host "TEST: Disconnect Quest Link/Air Link. Launch this modded Orbus instance." -ForegroundColor Cyan
Write-Host "If the game reaches a view, press F1 for the desktop diagnostic overlay."
Write-Host "Move the mouse and click objects. This logs hits but does NOT activate menus."
Write-Host "Log: $game\BepInEx\LogOutput.log"
Write-Host "If startup fails before BepInEx loads, check the Unity Player.log as well."
