# RebornCam v0.9.16 — full camera features restored; diagnostics remain off

**Context.** After prolonged investigation of an apparent headset freeze, Meta Horizon Link showed an NVIDIA driver compatibility warning: the installed driver can freeze Link during gameplay and recommends rolling back to 591.86 or earlier. Importantly, the freeze also occurred with Orbus closed and RebornCam disabled. Therefore, **the evidence does not establish RebornCam as the cause**. v0.9.15 had temporarily disabled the experimental physical-camera subsystem while isolating the issue.

## What loads in v0.9.16

**Enabled by default (features):**

- Main creator camera / normal desktop recording, POV, third-person, selfie, static camera
- F2 Creator Studio UI and native Orbus-inspired fonts where available
- Freecam, keyframe paths and cinematic tools
- Existing FOV and other camera settings
- **Physical camera prototype and VR controller grabbing**, including F3 recall and Shift+F3 visibility controls

**Not loaded by default (diagnostics/research tools):**

- F1 desktop startup probe and desktop input testing
- UI hierarchy, native Settings/PoweUI diff, VR UI click probes
- Avatar/Trickster scene inspectors and optional disk scanner

Those nine research components remain available solely when **`[Diagnostics] LoadLegacyComponents = true`** is deliberately set, followed by restarting Orbus. They are not needed for normal play, camera usage, or development of RebornMenu.

## Why the physical-camera setting changed

v0.9.15 used `[PhysicalCamera] LoadExperimentalPrototype = false`. BepInEx stores default values in a user's config file, so merely changing that key's default from false to true would **not** re-enable the model for users who launched v0.9.15.

v0.9.16 replaces it with **`[PhysicalCamera] EnableInWorldCamera = true`**. The old `LoadExperimentalPrototype` config line is ignored. Users may still explicitly set the new key to false to disable the physical prototype.

The separate `[PhysicalCamera] PrototypeVisible = false` setting controls whether the model shows automatically. With the physical component loaded, **F3 summons/recalls it**, even when it starts hidden.

## Auto-start is unchanged

`[General] StartEnabled` has always defaulted to true, but **the user's existing choice is preserved**. Someone who set `StartEnabled = false` during troubleshooting must set it back to `true` to automatically start the desktop creator camera. F9 can still manually toggle it.

Recommended config for normal feature testing:

```ini
[General]
StartEnabled = true

[Diagnostics]
LoadLegacyComponents = false

[PhysicalCamera]
EnableInWorldCamera = true
```

After updating through RebornManager, launch Orbus and inspect the BepInEx log:

- `[Features] Physical camera/grabbing ENABLED (default).`
- `[Stability] Legacy UI, scene, PowerUI and disk diagnostics NOT loaded.`
- `[Features] Camera loaded; StartEnabled=True, legacyDiagnostics=False, inWorldCamera=True`

**This version does not modify drivers, Oculus/Quest Link, Orbus game assets or RebornMenu.** Fix/update the NVIDIA/Meta Link compatibility issue separately. Test normally after a PC and headset restart rather than trying to reproduce hangs deliberately.
