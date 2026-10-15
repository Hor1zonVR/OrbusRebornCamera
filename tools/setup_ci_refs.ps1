# One-time private reference bootstrap for RebornCam GitHub Actions.
# After this, publishing is GitHub -> Actions -> Publish RebornCam Update.
# Uses only generated BepInEx core/interop reference DLLs, not game assets.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$cameraRepo = "Hor1zonVR/OrbusRebornCamera"
$refsRepo = "Hor1zonVR/RebornCamBuildRefs"
$tag = "refs-v1"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) required. Install it, then run gh auth login."
}
& gh auth status
if ($LASTEXITCODE -ne 0) { throw "First sign in to GitHub CLI: gh auth login" }

$default = Join-Path $env:USERPROFILE "Downloads\OrbusVR-Reborn-Community-PC-MODTEST\OrbusVR-Reborn-Community-PC"
if (Test-Path (Join-Path $default "BepInEx\interop\Il2Cppmscorlib.dll")) {
    Write-Host "Default modded game: $default"
    $game = (Read-Host "Press Enter for default or enter another MODDED game folder").Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($game)) { $game = $default }
} else {
    $game = (Read-Host "Enter the folder of your MODDED Orbus test game").Trim().Trim('"')
}
if ([string]::IsNullOrWhiteSpace($game)) { throw "Game folder is required." }
$game = [IO.Path]::GetFullPath($game)

$interop = @("Il2Cppmscorlib.dll", "UnityEngine.CoreModule.dll",
    "UnityEngine.InputModule.dll", "UnityEngine.PhysicsModule.dll",
    "UnityEngine.IMGUIModule.dll", "UnityEngine.TextRenderingModule.dll",
    "UnityEngine.VRModule.dll", "UnityEngine.XRModule.dll")
$core = @("BepInEx.Core.dll", "BepInEx.Unity.IL2CPP.dll", "Il2CppInterop.Runtime.dll")
$root = Join-Path $env:TEMP ("RebornCamRefs-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$destInterop = Join-Path $root "bundle\BepInEx\interop"
$destCore = Join-Path $root "bundle\BepInEx\core"
New-Item -ItemType Directory -Force -Path $destInterop, $destCore | Out-Null

foreach ($name in $interop) {
    $src = Join-Path $game ("BepInEx\interop\" + $name)
    if (Test-Path $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $destInterop $name) }
}
foreach ($name in $core) {
    $src = Join-Path $game ("BepInEx\core\" + $name)
    if (-not (Test-Path $src)) { throw "Missing required generated dependency: $src" }
    Copy-Item -LiteralPath $src -Destination (Join-Path $destCore $name)
}
foreach ($name in @("Il2Cppmscorlib.dll", "UnityEngine.CoreModule.dll",
    "UnityEngine.InputModule.dll", "UnityEngine.PhysicsModule.dll",
    "UnityEngine.IMGUIModule.dll", "UnityEngine.TextRenderingModule.dll")) {
    if (-not (Test-Path (Join-Path $destInterop $name))) { throw "Missing required interop: $name" }
}
if (-not (Test-Path (Join-Path $destInterop "UnityEngine.XRModule.dll")) -and
    -not (Test-Path (Join-Path $destInterop "UnityEngine.VRModule.dll"))) {
    throw "Must have at least one Unity XR or VR interop module."
}

$zip = Join-Path $root "RebornCamBuildRefs.zip"
Compress-Archive -Path (Join-Path $root "bundle\*") -DestinationPath $zip -Force
Write-Host ""
Write-Host "Reference archive: $zip"
Write-Host "This includes game-generated binaries. It MUST remain private." -ForegroundColor Yellow

& gh repo view $refsRepo --json isPrivate --jq ".isPrivate" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating one-time private reference repository $refsRepo..."
    & gh repo create $refsRepo --private --add-readme
    if ($LASTEXITCODE -ne 0) { throw "Could not create private repository." }
}
$private = (& gh repo view $refsRepo --json isPrivate --jq ".isPrivate").Trim()
if ($LASTEXITCODE -ne 0 -or $private -ne "true") {
    throw "REFUSED: $refsRepo is not confirmed private. No interop archive uploaded."
}
& gh release view $tag --repo $refsRepo --json tagName 2>$null
if ($LASTEXITCODE -eq 0) {
    throw "Private refs tag $tag already exists. Update via an explicit new tag and adjust the workflow instead of overwriting."
}
& gh release create $tag $zip --repo $refsRepo --title "Private RebornCam Build References" --notes "Game-generated interop dependencies for private build automation. No game assets."
if ($LASTEXITCODE -ne 0) { throw "Could not upload build references." }
& gh variable set REBORNCAM_BUILD_REFS_REPO --repo $cameraRepo --body $refsRepo
if ($LASTEXITCODE -ne 0) { throw "Could not configure build reference repository variable." }

Write-Host ""
Write-Host "PRIVATE REFERENCES UPLOADED" -ForegroundColor Green
Write-Host "Now create a fine-grained GitHub personal access token with Contents: Read-only for ONLY $refsRepo."
Write-Host "Add that token as the REBORNCAM_BUILD_REFS_TOKEN secret to the PUBLIC camera repo:"
Write-Host "https://github.com/$cameraRepo/settings/secrets/actions"
Write-Host ""
Write-Host "Then open https://github.com/$cameraRepo/actions/workflows/publish-camera.yml"
Write-Host "Run workflow -> main -> Preview first; Stable publishes updates RM can detect."
