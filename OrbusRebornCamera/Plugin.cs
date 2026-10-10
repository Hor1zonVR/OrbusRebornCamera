
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace OrbusBetterMirror;

[BepInPlugin(
    "com.horizon.orbus.bettermirror",
    "OrbusRebornCamera",
    "0.8.1"
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

    // Intentionally OFF until randomly spawned collectibles can be excluded
    // from recording-only camera output. This is for private development tests.
    internal static ConfigEntry<bool> EnableExperimentalDrone = null!;
    internal static ConfigEntry<float> DroneMoveSpeed = null!;
    internal static ConfigEntry<float> DroneBoostMultiplier = null!;
    internal static ConfigEntry<float> DroneMouseSensitivity = null!;
    internal static ConfigEntry<float> DroneMovementSmoothing = null!;

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

        EnableExperimentalDrone = Config.Bind(
            "ExperimentalDrone",
            "EnableExperimentalDrone",
            false,
            "SAFETY: Disabled by default. Private testing ONLY. " +
            "Freecam can reveal randomly spawned collectibles; do not " +
            "enable on public multiplayer servers until protected " +
            "object filtering has been implemented and reviewed."
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

        CreatorPanelVisible = Config.Bind(
            "CreatorPanel",
            "OpenAtStartup",
            false,
            "Show the desktop creator controls when the game starts. " +
            "Press F2 to show or hide the panel at any time."
        );

        Log.LogInfo("==================================");
        Log.LogInfo("OrbusRebornCamera v0.8.1 - desktop creator panel");
        Log.LogInfo("Physical Camera Grabbing Experiment");
        Log.LogInfo("==================================");

        // Existing working desktop camera and diagnostics.
        AddComponent<CameraDiagnostics>();
        AddComponent<AvatarDiagnostics>();

        // Physical prototype model from v0.7.0.
        AddComponent<PhysicalCameraPrototype>();

        // New controller input and grabbing component.
        AddComponent<PhysicalCameraGrab>();

        Log.LogInfo("F3 = Recall physical camera");
        Log.LogInfo("Shift+F3 = Show/Hide physical camera");
        Log.LogInfo("Left/Right grip = Grab and release camera");

        Log.LogInfo("F2 = Toggle desktop creator panel");
        Log.LogInfo("Creator Panel: camera modes, FOV, smoothness, distance, detach, avatar");
        Log.LogInfo("F9 = Desktop camera toggle");
        Log.LogInfo("Ctrl+F9 = Desktop avatar toggle");
        Log.LogInfo("F10 = FOV | F11 = Camera mode | F12 = Freeze");
        Log.LogInfo("Ctrl+F11 = Toggle experimental desktop drone (private testing only)");
        Log.LogInfo("Drone: hold RMB + WASD, Q/E vertical, Shift boost, Ctrl slow");
        Log.LogWarning("Drone is disabled by default until collectible filtering exists.");
        Log.LogInfo("F4/F8 = Diagnostics");

        Log.LogInfo(
            $"Physical camera grab radius: {PrototypeGrabRadius.Value:0.00}m"
        );
    }
}
