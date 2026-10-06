using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher
{
    [Flags]
    public enum MenuStageVisibility
    {
        None = 0,
        HideLocalPlayer = 1 << 0,
        HidePickups = 1 << 1,
        HideCombatActors = 1 << 2,
        HideCombatEffects = 1 << 3,
        Default = HideLocalPlayer | HidePickups | HideCombatActors | HideCombatEffects
    }

    /// <summary>
    /// One authored menu-stage recipe.
    ///
    /// The source picture is still rendered from the real room and its native
    /// intro camera. These values decide where on that authored camera path the
    /// capture happens and how the launcher frames/presents it afterwards.
    /// This keeps the POC data-driven without teaching the UI renderer anything
    /// about room geometry.
    /// </summary>
    public readonly record struct MenuStageProfile(
        string Name,
        int IntroFrame,
        float Zoom,
        float FocusX,
        float FocusY,
        float DriftX,
        float DriftY,
        float Softness,
        float Saturation,
        float CoolShift,
        float Vignette,
        float LeftScrim,
        float RightScrim,
        float FloorFade,
        float HighlightGlow,
        float Atmosphere,
        float Dust,
        float HunterBackdropHaze,
        float ForegroundHaze,
        float HunterLeft,
        float HunterTop,
        float HunterRight,
        float HunterBottom,
        float HunterDistanceScale,
        MenuStageVisibility Visibility)
    {
        public static MenuStageProfile Default => new(
            "default",
            IntroFrame: 12,
            Zoom: 0.88f,
            FocusX: 0.47f,
            FocusY: 0.44f,
            DriftX: 0.20f,
            DriftY: 0.14f,
            Softness: 0.30f,
            Saturation: 0.82f,
            CoolShift: 0.08f,
            Vignette: 0.18f,
            LeftScrim: 0.34f,
            RightScrim: 0.18f,
            FloorFade: 0.16f,
            HighlightGlow: 0.10f,
            Atmosphere: 0.12f,
            Dust: 0.12f,
            HunterBackdropHaze: 0.28f,
            ForegroundHaze: 0.045f,
            HunterLeft: 0.40f,
            HunterTop: 0.075f,
            HunterRight: 0.81f,
            HunterBottom: 0.94f,
            HunterDistanceScale: 0.84f,
            Visibility: MenuStageVisibility.Default);
    }

    /// <summary>
    /// Project Prime menu-stage authoring table.
    ///
    /// Built-in rooms borrow the original game's intro-camera path, but the
    /// frame sampled from that path is explicitly authored here instead of
    /// taking whichever frame the thumbnail worker happened to reach first.
    /// Custom maps keep using their explicit MapDefinition.Preview camera.
    /// </summary>
    public static class LauncherMenuStage
    {
        private static readonly Dictionary<string, MenuStageProfile> _rooms =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["MP3 PROVING GROUND"] = MenuStageProfile.Default with
                {
                    Name = "proving-ground-hero",
                    IntroFrame = 20,
                    Zoom = 0.84f,
                    // The first Menu Stage capture left the central bridge
                    // cutting straight through Trace's torso. Pull the room
                    // left and a little lower so its perspective lines lead
                    // toward the Hunter instead of bisecting the silhouette.
                    FocusX = 0.445f,
                    FocusY = 0.455f,
                    Softness = 0.31f,
                    Saturation = 0.88f,
                    CoolShift = 0.055f,
                    LeftScrim = 0.36f,
                    RightScrim = 0.18f,
                    Atmosphere = 0.14f,
                    Dust = 0.14f,
                    HunterBackdropHaze = 0.34f,
                    ForegroundHaze = 0.052f
                },
                ["MP1 SANCTORUS"] = MenuStageProfile.Default with
                {
                    Name = "sanctorus-hero",
                    IntroFrame = 24,
                    Zoom = 0.87f,
                    FocusX = 0.50f,
                    FocusY = 0.44f,
                    Saturation = 0.88f,
                    CoolShift = 0.05f,
                    Vignette = 0.16f,
                    Atmosphere = 0.10f,
                    HunterBackdropHaze = 0.25f,
                    ForegroundHaze = 0.038f
                },
                ["UNIT1 ALINOS LANDFALL"] = MenuStageProfile.Default with
                {
                    Name = "alinos-adventure",
                    IntroFrame = 28,
                    Zoom = 0.90f,
                    FocusX = 0.48f,
                    FocusY = 0.42f,
                    Saturation = 0.90f,
                    CoolShift = 0.02f,
                    HighlightGlow = 0.14f,
                    Atmosphere = 0.16f,
                    Dust = 0.18f,
                    HunterBackdropHaze = 0.22f,
                    ForegroundHaze = 0.035f
                },
                ["MP11 BREAKTHROUGH"] = MenuStageProfile.Default with
                {
                    Name = "breakthrough-studio",
                    IntroFrame = 18,
                    Zoom = 0.92f,
                    FocusX = 0.52f,
                    FocusY = 0.46f,
                    Softness = 0.24f,
                    CoolShift = 0.12f,
                    Vignette = 0.22f,
                    Atmosphere = 0.08f,
                    HunterBackdropHaze = 0.30f,
                    ForegroundHaze = 0.042f
                },
                ["AD2 ALINOS PERCH"] = MenuStageProfile.Default with
                {
                    Name = "perch-settings",
                    IntroFrame = 16,
                    Zoom = 0.94f,
                    FocusX = 0.50f,
                    FocusY = 0.44f,
                    Softness = 0.22f,
                    Saturation = 0.84f,
                    Vignette = 0.24f,
                    Atmosphere = 0.08f,
                    HunterBackdropHaze = 0.20f,
                    ForegroundHaze = 0.030f
                }
            };

        public static MenuStageProfile ForRoom(string? roomKey)
        {
            if (!String.IsNullOrWhiteSpace(roomKey)
                && _rooms.TryGetValue(roomKey.Trim(), out MenuStageProfile profile))
                return profile;
            return MenuStageProfile.Default;
        }

        public static MenuStageProfile Current
        {
            get
            {
                MenuStageProfile room = ForRoom(LauncherBackdrop.RoomKey);
                return LauncherBackdrop.Scene switch
                {
                    LauncherBackdropScene.Adventure => room with
                    {
                        Saturation = Math.Max(room.Saturation, 0.90f),
                        HighlightGlow = Math.Max(room.HighlightGlow, 0.14f),
                        Atmosphere = Math.Max(room.Atmosphere, 0.15f)
                    },
                    LauncherBackdropScene.ReplayStudio => room with
                    {
                        CoolShift = Math.Max(room.CoolShift, 0.12f),
                        Vignette = Math.Max(room.Vignette, 0.22f)
                    },
                    LauncherBackdropScene.Settings => room with
                    {
                        Softness = Math.Min(room.Softness, 0.24f),
                        Vignette = Math.Max(room.Vignette, 0.22f)
                    },
                    _ => room
                };
            }
        }

        public static bool Has(MenuStageVisibility flag, string? roomKey = null)
            => (ForRoom(roomKey ?? LauncherBackdrop.RoomKey).Visibility & flag) != 0;
    }
}
