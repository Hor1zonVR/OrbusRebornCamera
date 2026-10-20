# Inspect the native OrbusVR Settings menu (RebornCam v0.9.12)

This is a **read-only** test for RebornMenu integration. It does not yet add a Mods tab, call UI callbacks, change Orbus objects or move the VR rig.

## Exactly two captures

1. In RM, disable the old experimental **RebornMenu** for this test, and update **RebornCam to v0.9.12**.
2. Launch Orbus in VR, and **close the normal Orbus inventory and Settings menu**.
3. Focus the game window and press **Ctrl + Shift + I** once. The BepInEx log should say **[NativeMenuDiff] BASELINE captured**. No file is saved yet.
4. Open the normal in-game inventory and its **Settings** page, leaving it visibly OPEN.
5. Press **Ctrl + Shift + I** again. The second press saves one report: **OrbusMenu_YYYYMMDD_HHMMSS_mmm.txt**.
6. Find it in the selected RM instance under **BepInEx/config/RebornCam/UIReports/** and send that OrbusMenu report only. An OrbusVRInput report is NOT required.

If you don't see a file, send BepInEx/LogOutput.log and check for **[NativeMenuDiff]** messages. Each additional pair of shortcut presses starts a fresh comparison.

## What this report includes

- Which player-menu objects appeared or became active between the CLOSED and OPEN snapshots
- Prioritised Settings, inventory, journal, tab and menu objects instead of hundreds of wardrobe objects
- Up to 12 relevant IL2CPP component inventories, and observations of active UIPointer transforms on the UI-Interactable layer
- Scene context, XR status and a note if the menu is built using a different renderer rather than Unity scene objects

**Safety:** Read-only; no UI methods invoked, no values from text fields, accounts or server addresses. Unity hierarchy names can contain identifying labels, so review the report before sharing.

The scan caps at 45,000 transforms; output caps at 160 changed objects, 100 menu candidates and 10 pointer objects. It runs only on your two shortcut presses.

If it reports NO relevant active changes, that's not proof the menu doesn't exist. It may use **PowerUI documents** rendered outside normal transform activation. In that case we'll inspect the PowerUI pathway next rather than forcing buttons into unrelated canvases.
