
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace OrbusBetterMirror;

[BepInPlugin(
    "com.horizon.orbus.bettermirror",
    "OrbusRebornCamera",
    "0.9.13"
)]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource ModLogger = null!;

    internal static ConfigEntry<bool> StartEnabled = null!;
    internal static ConfigEntry<string> StartMode = null!;

    internal static ConfigEntry<float> HorizontalFov = null!;
    internal static ConfigEntry<float> Smoothing = null!;

    internal static ConfigEntry<float> ThirdDistance = null!;
    internal static ConfigEntry<float> ThirdHeight = null!;
    internal static ConfigEntry<float> SelfieDistance = null!;

    internal static ConfigEntry<bool> ShowLocalAvatar = null!;

    internal static ConfigEntry<bool> PrototypeVisible = null!;
    internal static ConfigEntry<float> PrototypeSpawnDistance = null!;
    internal static ConfigEntry<float> PrototypeGrabRadius = null!;

    // Shared VR/desktop freecam: same keyboard/mouse controls and keyframes.
    internal static ConfigEntry<float> DroneMoveSpeed = null!;
    internal static ConfigEntry<float> DroneBoostMultiplier = null!;
    internal static ConfigEntry<float> DroneMouseSensitivity = null!;
    internal static ConfigEntry<float> DroneMovementSmoothing = null!;
    internal static ConfigEntry<float> KeyframeDuration = null!;
    internal static ConfigEntry<float> DroneRollSpeed = null!;
    internal static ConfigEntry<bool> ShowKeyframePath = null!;
    internal static ConfigEntry<bool> UseOrbusUiFont = null!;
    internal static ConfigEntry<bool> ExtendedRenderEnabled = null!;
    internal static ConfigEntry<float> ExtendedRenderFarClip = null!;

    // Ctrl+F4 is always available on demand when a rare goblin is found.
    // The separate, slower disk scan is opt-in.
    internal static ConfigEntry<bool> AutoScanTricksterFiles = null!;

    internal static ConfigEntry<bool> CreatorPanelVisible = null!;

    public override void Load()
    {
        ModLogger = Log;

        StartEnabled = Config.Bind(
            "General",
            "StartEnabled",
            true,
            "Automatically enable the desktop creator camera when VR is ready."
        );

        StartMode = Config.Bind(
            "General",
            "StartMode",
            "POV",
            "Starting mode: POV, ThirdPerson or Selfie."
        );

        HorizontalFov = Config.Bind(
            "Camera",
            "HorizontalFov",
            110f,
            "Horizontal camera FOV in degrees. Range: 50 to 150."
        );

        Smoothing = Config.Bind(
            "Camera",
            "Smoothing",
            7f,
            "Follow smoothing. Higher values follow faster."
        );

        ThirdDistance = Config.Bind(
            "ThirdPerson",
            "Distance",
            2.5f,
            "Distance behind the player in metres."
        );

        ThirdHeight = Config.Bind(
            "ThirdPerson",
            "Height",
            0.6f,
            "Height above the headset in metres."
        );

        SelfieDistance = Config.Bind(
            "Selfie",
            "Distance",
            2.0f,
            "Distance in front of the player in metres."
        );

        ShowLocalAvatar = Config.Bind(
            "Avatar",
            "ShowLocalAvatar",
            true,
            "Show local avatar meshes in external desktop camera modes."
        );

        PrototypeVisible = Config.Bind(
            "PhysicalCamera",
            "PrototypeVisible",
            false,
            "Show the optional experimental physical camera placeholder in VR. " +
            "Off by default; independent of the desktop drone."
        );

        PrototypeSpawnDistance = Config.Bind(
            "PhysicalCamera",
            "SpawnDistance",
            0.75f,
            "Distance in metres from the headset when recalling the camera."
        );

        PrototypeGrabRadius = Config.Bind(
            "PhysicalCamera",
            "GrabRadius",
            0.28f,
            "Maximum distance in metres between the controller and camera grip when picking it up."
        );

        DroneMoveSpeed = Config.Bind(
            "ExperimentalDrone", "MoveSpeed", 4f,
            "Normal drone flight speed in metres per second (0.25 to 20)."
        );

        DroneBoostMultiplier = Config.Bind(
            "ExperimentalDrone", "BoostMultiplier", 3f,
            "Hold Shift to boost drone speed by this multiplier (1 to 8)."
        );

        DroneMouseSensitivity = Config.Bind(
            "ExperimentalDrone", "MouseSensitivity", 2.5f,
            "Mouse-look sensitivity while holding right mouse (0.1 to 8)."
        );

        DroneMovementSmoothing = Config.Bind(
            "ExperimentalDrone", "MovementSmoothing", 12f,
            "Acceleration and deceleration smoothing; 0 is instant."
        );

        DroneRollSpeed = Config.Bind(
            "ExperimentalDrone", "RollDegreesPerSecond", 65f,
            "Bank the detached creator camera with Z/C while RMB flying; X levels it.");
        UseOrbusUiFont = Config.Bind(
            "CreatorPanel", "UseOrbusFont", true,
            "Prefer an already-loaded Orbus scene TextMesh font for the creator " +
            "panel title, section headings and tabs. Never bundles font files; " +
            "falls back to the native readable Unity IMGUI font if unavailable.");

        ShowKeyframePath = Config.Bind(
            "CreatorPanel", "ShowKeyframePath", true,
            "Show keyframe trajectory and markers ONLY on the desktop creator camera " +
            "when the F2 panel is visible, not during playback or in the VR headset.");

        KeyframeDuration = Config.Bind(
            "Freecam",
            "KeyframeDuration",
            12f,
            "Seconds to play a cinematic camera path (2 to 120). " +
            "Affects the desktop recording camera only."
        );

        ExtendedRenderEnabled = Config.Bind(
            "CinematicRender", "EnableExtendedRendering", true,
            "When freecam is active, expand only the separate desktop " +
            "camera's draw distance, disable its occlusion culling, and " +
            "try disabling per-layer distance culling. Cannot load absent geometry."
        );

        ExtendedRenderFarClip = Config.Bind(
            "CinematicRender", "FarClipMeters", 1200f,
            "Desired freecam draw distance for ALREADY LOADED objects " +
            "(150 to 5000 Unity units). Desktop creator camera only."
        );

        AutoScanTricksterFiles = Config.Bind(
            "Diagnostics",
            "AutoScanTricksterFiles",
            false,
            "Optionally scan installed Unity game files in the background " +
            "once and write BepInEx/TricksterAssetReport.txt. No Python, " +
            "gameplay changes, or rare NPC spawn required. Delete the report " +
            "to request a new scan on the next launch."
        );

        CreatorPanelVisible = Config.Bind(
            "CreatorPanel",
            "OpenAtStartup",
            false,
            "Show the desktop creator controls when the game starts. " +
            "Press F2 to show or hide the panel at any time."
        );

        Log.LogInfo("==================================");
        Log.LogInfo("OrbusRebornCamera v0.9.13 - Orbus-inspired Creator Studio theme and in-game font");
        Log.LogInfo("Physical Camera Grabbing Experiment");
        Log.LogInfo("==================================");

        // Existing working desktop camera and diagnostics.
        AddComponent<CameraDiagnostics>();
        AddComponent<DesktopStartupProbe>();
        AddComponent<DesktopMenuInput>();
        AddComponent<OrbusUIInspector>();
        AddComponent<VrUiInteractionRecorder>();
        AddComponent<NativeMenuDiffInspector>();
        AddComponent<NativePowerUiSettingsProbe>();
        AddComponent<AvatarDiagnostics>();
        AddComponent<TricksterAssetDiagnostics>();
        AddComponent<TricksterDiskDiagnostics>();

        // Physical prototype model from v0.7.0.
        AddComponent<PhysicalCameraPrototype>();

        // New controller input and grabbing component.
        AddComponent<PhysicalCameraGrab>();

        Log.LogInfo("F3 = Recall physical camera");
        Log.LogInfo("Shift+F3 = Show/Hide physical camera");
        Log.LogInfo("Left/Right grip = Grab and release camera");

        Log.LogInfo("[DesktopProbe] F1 = desktop UI / camera diagnostics");
        Log.LogInfo("Ctrl+Shift+F1 = broad UI report / VR input capture");
        Log.LogInfo("Ctrl+Shift+I twice = compare CLOSED vs OPEN native Orbus Settings (one concise report)");
        Log.LogInfo("Ctrl+Shift+P twice = read-only POWERUI SETTINGS observer (close menu first; then open Settings)");
        Log.LogInfo("Ctrl+Shift+F11 = recenter freecam to local player (CAMERA ONLY)");
        Log.LogInfo("Freecam warns beyond 35m from player; world streaming follows avatar");
        Log.LogInfo("Extended far clipping / occlusion for ALREADY LOADED objects (desktop camera only)");
        Log.LogInfo("Ctrl+Shift+F8 = compare loaded scenery near freecam and player");
        Log.LogInfo("Ctrl+Shift+F9 = toggle detached camera's extended rendering live");
        Log.LogInfo("TAB = desktop-only mouse menu pointer (VR headset must be inactive)");
        Log.LogInfo("Left click = verified Unity UI handler only; Orbus custom clicks remain diagnostic");
        Log.LogInfo("UI-Interactable layer 19 is targeted behind terrain without moving the rig");
        Log.LogInfo("F1 = pointer and scene diagnostics");
        Log.LogInfo("F2 = Toggle desktop creator panel");
        Log.LogInfo("Creator Panel: camera modes, FOV, smoothness, distance, detach, avatar");
        Log.LogInfo("F9 = Desktop camera toggle");
        Log.LogInfo("Ctrl+F9 = Desktop avatar toggle");
        Log.LogInfo("F10 = FOV | F11 = Camera mode | F12 = Freeze");
        Log.LogInfo("Ctrl+F11 = Toggle freecam (same controls in VR and desktop sessions)");
        Log.LogInfo("Freecam: RMB+WASD even with F2 open (click outside panel); Q/E up/down");
        Log.LogInfo("While flying: Z/C bank camera, X level roll, Shift boost, Ctrl slow");
        Log.LogInfo("F2 opens draggable Creator Studio: Camera / Freecam / Keyframes");
        Log.LogInfo("Stable GitHub releases are built automatically for RM on version updates");
        Log.LogWarning(
            "Trickster Goblin visual filtering is not implemented yet. " +
            "Use freecam only where the server owner permits it.");
        Log.LogInfo("F4/F8 = Diagnostics");
        Log.LogInfo("Ctrl+F4 = Read-only Trickster model diagnostic, use if you encounter the rare enemy");
        Log.LogInfo("Optional Trickster disk scan defaults OFF; see [Diagnostics] AutoScanTricksterFiles");

        Log.LogInfo(
            $"Physical camera grab radius: {PrototypeGrabRadius.Value:0.00}m"
        );
    }
}
