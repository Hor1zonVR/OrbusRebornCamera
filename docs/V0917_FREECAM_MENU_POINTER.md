# RebornCamera v0.9.17 - desktop pointer in freecam

## Objective

Use the desktop mouse for Orbus menu navigation while retaining the detached
creator camera. This enables a direct test of server-selection navigation in
a VR session, without moving the headset rig or teleporting the avatar.

## Why this change was required

Prior code in DesktopMenuInput explicitly blocked the pointer when either
`XRSettings.isDeviceActive` or `CameraDiagnostics.DesktopFreecamActive`
was true. Additionally, the component loaded only when
`[Diagnostics] LoadLegacyComponents=true`. Both restrictions prevented it
from being used for normal RC freecam navigation.

## Desktop controls

1. Launch the game using your usual modded RM instance.
2. Press **Ctrl+F11** to enter RC detached freecam.
3. Press **Tab** to toggle the mouse pointer **on**.
4. **Left click** any visible standard Unity UI menu buttons.
5. **Hold right mouse** to fly the camera with WASD/Q/E. Release RMB to
   resume clicking and hover inspection.
6. Press **F2** if you need the Creator Studio; input to the game pointer is
   paused while the Creator Studio is open.
7. Press **Tab** to disable the pointer, or **Ctrl+F11** to leave freecam
   (which also disables it).

## What works, what needs verification

- Standard Unity UI buttons, toggles, dropdowns and text input fields retain
  the existing opt-in support.
- A visible `UI-Interactable` collider behind a nonstandard Orbus menu is
  reported, but no unverified Orbus method is invoked.
- Native Orbus PowerUI, character-selection or VR-only server-browser input
  may have a **different** event system. If you see "custom VR click withheld"
  or "no standard UI handler", this is a detection success, **not** a working
  menu click. Send that log to map the correct native click path.
- The menu must actually be rendered and reachable from the desktop camera.
  World-space UI anchored to the headset may not remain visible after flying
  far away. This update does not relocate a menu or spoof the VR pointer.
- This release does not offer general desktop-only startup or new menu-open
  shortcuts; Orbus itself still controls menu visibility and session joining.

## Test / diagnostics

1. Update RM after publishing the GitHub release (the repo's release.ps1
   builds with your local game's IL2CPP interop).
2. In freecam, enable the pointer with Tab and try clicking one harmless
   menu control before using the server selection.
3. Inspect the BepInEx log lines starting `[FreecamUI]` and `[DesktopUI]`.
4. If clicking is not recognized, note the displayed hover status in
   the lower-left RC pointer bar. Capture a screenshot showing the menu
   and hover target and share a short log excerpt.

Guardrails: UI adapter is idle until the Tab opt-in. No VR tracking,
player transform, world streaming, or session network code is modified.
Native game controls remain authoritative.
