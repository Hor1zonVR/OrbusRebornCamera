# Native Orbus Settings — focused PowerUI probe (RebornCam v0.9.13)

**Purpose:** locate the real document, tabs and render updates behind Orbus's native Settings menu, as the next step toward a proper RebornMenu **Mods** tab.

This is a **temporary, user-triggered, read-only experiment**. It does **not** inject HTML, edit Unity objects, invoke game menu actions, change BepInEx config or read text-field values.

## How to test

1. Update **RebornCam** to **v0.9.13** in RM, keep **RebornMenu disabled**. This is only a RebornCam diagnostic.
2. Launch Orbus in VR. With the native inventory/Settings panels **fully closed**, focus the game on your PC and press **Ctrl + Shift + P** once.
3. The BepInEx log should say **[PowerUiSettings] STARTED**. This temporarily attaches an observe-only prefix to the detected PowerUI/DOM `set_innerHTML(string)` method.
4. Return to VR, open the normal Orbus inventory, switch to **Settings** and click **one or two ordinary Settings categories/tabs**. Do not change sensitive account settings. Keep the menu visible.
5. Press **Ctrl + Shift + P** again to stop. A single report is written:
   `BepInEx/config/RebornCam/UIReports/OrbusPowerUI_YYYYMMDD_HHMMSS_mmm.txt`
6. Send that report. If it doesn't appear, send `BepInEx/LogOutput.log`, looking for lines beginning `[PowerUiSettings]`.

This automatically stops after **60 seconds** if you forget the second shortcut. It unhooks the observer on stop, timeout or component shutdown. You do not need the old `OrbusMenu_...` report again.

## Exact information recorded

- Whether legacy `PowerUI.Document`/`PowerUI.Element`, modern `PowerUI.HtmlDocument`/`Dom.Element`, the native `PlayerMenu` type and several relevant DOM classes exist.
- Method signatures, property/field **names and types only**, specifically navigation/document/event and Settings-related metadata.
- Whether `PlayerMenu.document` has a readable **type** (not its contents).
- During the short recording: total observed native HTML setter calls, element IDs **only when recognisably UI-related**, the element/document type names and a capped set of `id`/`class` identifiers mentioning menu/settings/inventory/tabs.
- Character counts, not the HTML itself. Captured markup strings are never persisted.

Maximum **72** relevant observations and **1,500** intercepted setter calls; the probe checks only the first **24,000** characters of any string to find menu-related identifiers. It never writes usernames, chat messages, account settings, full HTML, or gameplay state to the report.

**Note:** The prefix observes the original string without altering it; installing an extra Harmony prefix still carries some compatibility risk in an IL2CPP game, especially alongside QuestHelper. Test in your dedicated RM instance, and close/restart Orbus if input or the UI becomes abnormal. Disable RebornCam in RM to remove the experiment entirely.

## Interpretation

- **Recognised Settings classes/IDs and a document type:** evidence to develop a separate opt-in native-tab proof, after verifying input callbacks.
- **Only `missions-container` or inventory updates:** may indicate we are seeing unrelated redraws rather than the real Settings navigation; do not call it a Settings selector.
- **No matching setter events:** could mean Settings was already prebuilt or updates via an alternate API. Use metadata type names to design a more focused probe instead of guessing DOM insertion points.
- **Metadata only:** same conclusion—do not mutate anything until the native owner and click dispatch are verified.

No standalone wrist rune, no homemade VR pointer, and **no Mods tab is added in v0.9.13**. We're investigating the actual existing Orbus interface.
