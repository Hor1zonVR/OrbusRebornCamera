# RebornCam v0.9.15 — core-only VR stability test

## Why this exists

A user experienced intermittent Quest Link/Oculus freezes while Orbus continued running on PC. Disabling RebornCam entirely appeared to help; turning **General → StartEnabled** off allowed roughly 50 minutes, but the freeze eventually returned during an **area transition** without the desktop creator camera ever being activated. This narrows—but does not prove—the cause. The full v0.9.14 plugin still created nine ancillary UI/PowerUI/avatar/disk-diagnostic MonoBehaviours even with the creator camera disabled.

## What changed

In `Plugin.Load()`, always register only **CameraDiagnostics** (the core creator camera, existing Creator Studio and freecam). The following components are **NOT CREATED by default**:

- DesktopStartupProbe
- DesktopMenuInput
- OrbusUIInspector
- VrUiInteractionRecorder
- NativeMenuDiffInspector
- NativePowerUiSettingsProbe
- AvatarDiagnostics
- TricksterAssetDiagnostics
- TricksterDiskDiagnostics

These can still be enabled for an intentional research session via `[Diagnostics] LoadLegacyComponents = true`, followed by a full game restart.

The experimental physical camera model and controller grab handlers are also **NOT CREATED by default**. Restore them explicitly using `[PhysicalCamera] LoadExperimentalPrototype = true` after the core has passed stability testing. Standard desktop F9 camera, F2 Studio, follow modes and freecam remain in the core.

Existing `[General] StartEnabled = false` is respected. This version does **not** attempt to fix Oculus or the scene loader itself. It provides a clean comparison and reduces RC's moving parts substantially.

## Test instructions

1. Exit OrbusVR. In RebornManager, update RebornCam to **v0.9.15** when the GitHub DLL is published.
2. In the test instance `BepInEx/config/com.horizon.orbus.bettermirror.cfg`, keep `[General] StartEnabled = false`.
3. Confirm or set:
   ```ini
   [Diagnostics]
   LoadLegacyComponents = false

   [PhysicalCamera]
   LoadExperimentalPrototype = false
   ```
4. Launch Orbus and check BepInEx log for:
   `[Stability] Legacy UI, scene, PowerUI and disk diagnostics NOT loaded.`
   `[Stability] Physical-camera prototype/grabbing NOT loaded.`
   `[Stability] Core camera loaded; StartEnabled=False, legacyDiagnostics=False, physicalPrototype=False`
5. Play normally, including some ordinary zone transitions when you were planning to move areas anyway; avoid deliberately forcing crashes. Keep the desktop camera OFF for the initial comparison.
6. If this is stable past the typical freeze interval, the new evidence strongly supports the removed ancillary components as a contributor. It does not isolate which one; don't turn all of them back on together yet.
7. If it still freezes, save `BepInEx/LogOutput.log` and your Oculus/Quest Link error log. Compare the last messages to the transition timing, and check whether the issue also occurs with RC completely disabled.

**Please don't treat a single stable session as a final fix.** We want multiple ordinary sessions with scene changes before declaring it stable.

### Important compatibility notes

The v0.9.15 default disables F1 desktop diagnostics, native Settings/PowerUI probes and experimental F3 physical-camera interactions. They are intentionally not available in stability mode. Pressing F9 still enables the separate desktop camera manually; avoid doing so during the initial test. Never use RebornCam for RebornMenu UI investigation while diagnosing headset freezing.

If you need to revert immediately, disable RebornCam in RM and restart. No game assets, game settings or other mod configs are changed.

## Next

If the test is stable, isolate individual diagnostic components only in a dedicated test instance, rather than loading all nine together. If it still freezes, inspect camera-core lifecycle around changing scenes and consider a full camera teardown on loading, separately from any Unity XR/Quest Link compositor investigation.
