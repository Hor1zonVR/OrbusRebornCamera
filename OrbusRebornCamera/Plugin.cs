
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace OrbusBetterMirror;

[BepInPlugin(
    "com.horizon.orbus.bettermirror",
    "OrbusVR BetterMirror",
    "0.6.0"
)]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource ModLogger = null!;

    // General settings
    internal static ConfigEntry<bool> StartEnabled = null!;
    internal static ConfigEntry<string> StartMode = null!;

    // Camera settings
    internal static ConfigEntry<float> HorizontalFov = null!;
    internal static ConfigEntry<float> Smoothing = null!;

    // Third-person settings
    internal static ConfigEntry<float> ThirdDistance = null!;
    internal static ConfigEntry<float> ThirdHeight = null!;

    // Selfie settings
    internal static ConfigEntry<float> SelfieDistance = null!;

    // Local avatar rendering
    internal static ConfigEntry<bool> ShowLocalAvatar = null!;

    public override void Load()
    {
        ModLogger = Log;

        StartEnabled = Config.Bind(
            "General",
            "StartEnabled",
            true,
            "Automatically enable BetterMirror when VR is ready."
        );

        StartMode = Config.Bind(
            "General",
            "StartMode",
            "POV",
            "Starting camera mode: POV, ThirdPerson or Selfie."
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
            "Camera distance behind the player in metres."
        );

        ThirdHeight = Config.Bind(
            "ThirdPerson",
            "Height",
            0.6f,
            "Camera height above the headset in metres."
        );

        SelfieDistance = Config.Bind(
            "Selfie",
            "Distance",
            2.0f,
            "Camera distance in front of the player in metres."
        );

        ShowLocalAvatar = Config.Bind(
            "Avatar",
            "ShowLocalAvatar",
            true,
            "Show local Minicam-layer avatar meshes in " +
            "BetterMirror's third-person, selfie and static modes. " +
            "The normal VR camera is not modified."
        );

        Log.LogInfo("==================================");
        Log.LogInfo("BetterMirror v0.6 loaded.");
        Log.LogInfo("Full Avatar Rendering Experiment");
        Log.LogInfo("==================================");

        // Existing camera controller, upgraded for v0.6.
        AddComponent<CameraDiagnostics>();

        // Keep the working v0.5 diagnostics.
        AddComponent<AvatarDiagnostics>();

        Log.LogInfo(
            "F4 Avatar Scan | Shift+F4 Extended Scan"
        );

        Log.LogInfo(
            "F5/F6/F7 Mirror | F8 Diagnostics"
        );

        Log.LogInfo(
            "F9 Camera Toggle | Ctrl+F9 Avatar Toggle"
        );

        Log.LogInfo(
            "F10 FOV | F11 Camera Mode | F12 Freeze"
        );

        Log.LogInfo(
            $"Configured FOV: {HorizontalFov.Value:0}"
        );

        Log.LogInfo(
            $"Local avatar rendering: {ShowLocalAvatar.Value}"
        );
    }
}
