
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace OrbusBetterMirror;

[BepInPlugin(
    "com.horizon.orbus.bettermirror",
    "OrbusRebornCamera",
    "0.7.0"
)]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource ModLogger = null!;

    // Existing general settings.
    internal static ConfigEntry<bool> StartEnabled = null!;
    internal static ConfigEntry<string> StartMode = null!;

    // Existing desktop camera settings.
    internal static ConfigEntry<float> HorizontalFov = null!;
    internal static ConfigEntry<float> Smoothing = null!;

    // Existing third-person settings.
    internal static ConfigEntry<float> ThirdDistance = null!;
    internal static ConfigEntry<float> ThirdHeight = null!;

    // Existing selfie settings.
    internal static ConfigEntry<float> SelfieDistance = null!;

    // Existing avatar rendering.
    internal static ConfigEntry<bool> ShowLocalAvatar = null!;

    // New physical camera prototype settings.
    internal static ConfigEntry<bool> PrototypeVisible = null!;
    internal static ConfigEntry<float> PrototypeSpawnDistance = null!;

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
            "Show local Minicam-layer avatar meshes in external desktop camera modes. The headset camera is not modified."
        );

        // Physical camera prototype.
        PrototypeVisible = Config.Bind(
            "PhysicalCamera",
            "PrototypeVisible",
            true,
            "Show the experimental physical camera placeholder in VR."
        );

        PrototypeSpawnDistance = Config.Bind(
            "PhysicalCamera",
            "SpawnDistance",
            0.75f,
            "Distance in metres in front of the headset when spawning or recalling the physical camera."
        );

        Log.LogInfo("==================================");
        Log.LogInfo("OrbusRebornCamera v0.7.0");
        Log.LogInfo("Physical Camera Prototype");
        Log.LogInfo("==================================");

        // Preserve the existing desktop camera.
        AddComponent<CameraDiagnostics>();

        // Preserve the existing diagnostics.
        AddComponent<AvatarDiagnostics>();

        // New, independent VR prop prototype.
        AddComponent<PhysicalCameraPrototype>();

        Log.LogInfo("Desktop camera controller loaded.");
        Log.LogInfo("Physical camera prototype controller loaded.");

        Log.LogInfo(
            "F3 = Recall physical camera | Shift+F3 = Show/Hide"
        );

        Log.LogInfo(
            "F9 = Desktop camera toggle | Ctrl+F9 = Avatar toggle"
        );

        Log.LogInfo(
            "F10 = FOV | F11 = Camera mode | F12 = Freeze"
        );

        Log.LogInfo(
            "F4 = Avatar diagnostics | F8 = Camera diagnostics"
        );

        Log.LogInfo(
            $"Configured FOV: {HorizontalFov.Value:0}"
        );

        Log.LogInfo(
            $"Physical prototype visible: {PrototypeVisible.Value}"
        );
    }
}
