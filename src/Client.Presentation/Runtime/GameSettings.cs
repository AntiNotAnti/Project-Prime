using System;
using System.Globalization;
using MphRead.Sound;

namespace MphRead.Mods
{
    /// <summary>
    /// Makes the settings file mean something on the launcher's path.
    ///
    /// settings.json was only ever read into the engine by
    /// <c>Menu.ShowMenuPrompts</c>, which is the console menu. It parses the
    /// same file into a set of private statics and then hands those to the
    /// engine in two places -- volumes straight away, match rules from
    /// Renderer once the mode is known -- and it does the second only when a
    /// flag it sets itself is on.
    ///
    /// The launcher never runs any of that. It loads the file for its own
    /// screens, writes the file back when somebody presses save, and starts
    /// the game; so the music slider moved, the number was stored, and
    /// nothing on the machine ever read it. The same was true of the sound
    /// effects volume, the language, and every match rule: point goal, time
    /// limit, damage level, teams, friendly fire, hunter radar, affinity
    /// weapons. They were settings in the sense that they could be changed.
    ///
    /// This is the missing half, kept apart from Menu on purpose: Menu's
    /// state belongs to a console session that may never have started.
    /// </summary>
    public static class GameSettings
    {
        /// <summary>
        /// The settings this process is playing under, once something has
        /// applied a set. Null when only the console menu has run, which owns
        /// its own copy and applies it its own way.
        /// </summary>
        public static MenuSettings? Current { get; private set; }

        /// <summary>The local preference can only reduce the server's policy.</summary>
        public static bool KillcamEnabled { get; private set; }

        public static KillcamPolicy ResolveKillcamPolicy(KillcamPolicy serverPolicy)
        {
            if (!Enum.IsDefined(serverPolicy)) throw new ArgumentOutOfRangeException(nameof(serverPolicy));
            return KillcamEnabled ? serverPolicy : KillcamPolicy.Disabled;
        }

        /// <summary>
        /// Apply everything that can be applied the moment it changes: the
        /// two volumes and the text language.
        ///
        /// Called when the launcher loads the file and again whenever the
        /// settings window commits, which is what makes the music slider take
        /// effect while a match is running rather than at the next launch.
        /// </summary>
        public static void Apply(MenuSettings settings)
        {
            Current = settings;
            if (TryVolume(settings.FeedbackVolume, out float feedback)) Combat.FeedbackAudio.Volume = feedback;
            global::MphRead.Hud.Network.NetworkHealthSettings.Advanced = RenderOptions.ParseOnOff(settings.AdvancedNetwork, false);
            Combat.CombatFeedbackSettings.HitMarkers = Enum.TryParse(settings.HitMarkers, true, out Combat.HitMarkerMode marker)
                && Enum.IsDefined(marker) ? marker : Combat.HitMarkerMode.Visual;
            Combat.CombatFeedbackSettings.Timing = Enum.TryParse(settings.HitMarkerTiming, true,
                out Combat.HitMarkerTiming timing) && Enum.IsDefined(timing)
                ? timing : Combat.HitMarkerTiming.Confirmed;
            Combat.CombatFeedbackSettings.HeadshotCue = RenderOptions.ParseOnOff(settings.HeadshotCue, true);
            Combat.CombatFeedbackSettings.KillConfirmation = RenderOptions.ParseOnOff(settings.KillConfirmation, true);
            KillcamEnabled = RenderOptions.ParseOnOff(settings.Killcam, false);
            global::MphRead.Hud.Radar.RadarSettings.Style = Enum.TryParse(settings.RadarStyle, true, out global::MphRead.Hud.Radar.RadarStyle radarStyle)
                && Enum.IsDefined(radarStyle) ? radarStyle : global::MphRead.Hud.Radar.RadarStyle.Enhanced;
            global::MphRead.Hud.Radar.RadarSettings.Orientation = Enum.TryParse(settings.RadarOrientation, true, out global::MphRead.Hud.Radar.RadarOrientation radarOrientation)
                && Enum.IsDefined(radarOrientation) ? radarOrientation : global::MphRead.Hud.Radar.RadarOrientation.Heading;
            global::MphRead.Hud.Radar.RadarSettings.Anchor = Enum.TryParse(settings.RadarPosition, true,
                out global::MphRead.Hud.Radar.RadarAnchor radarAnchor) && Enum.IsDefined(radarAnchor)
                ? radarAnchor : global::MphRead.Hud.Radar.RadarAnchor.TopRight;
            global::MphRead.Hud.Radar.RadarSettings.Scale = ParseRadarNumber(settings.RadarScale, 1,
                global::MphRead.Hud.Radar.RadarSettings.MinimumScale,
                global::MphRead.Hud.Radar.RadarSettings.MaximumScale);
            global::MphRead.Hud.Radar.RadarSettings.OffsetX = ParseRadarNumber(settings.RadarOffsetX, 0, -256, 256);
            global::MphRead.Hud.Radar.RadarSettings.OffsetY = ParseRadarNumber(settings.RadarOffsetY, 0, -192, 192);
            global::MphRead.Hud.Radar.RadarSettings.Range = ParseRadarNumber(settings.RadarRange,
                global::MphRead.Hud.Radar.RadarSettings.DefaultRange,
                global::MphRead.Hud.Radar.RadarSettings.MinimumRange,
                global::MphRead.Hud.Radar.RadarSettings.MaximumRange);
            global::MphRead.Hud.Radar.RadarSettings.Opacity = ParseRadarNumber(settings.RadarOpacity, 1,
                global::MphRead.Hud.Radar.RadarSettings.MinimumOpacity,
                global::MphRead.Hud.Radar.RadarSettings.MaximumOpacity);
            global::MphRead.Hud.Radar.RadarSettings.ElevationIndicators
                = RenderOptions.ParseOnOff(settings.RadarElevationIndicators, true);
            if (!string.IsNullOrWhiteSpace(settings.RadarProfileJson))
            {
                try
                {
                    global::MphRead.Hud.Radar.RadarSettings.Apply(
                        global::MphRead.Hud.Radar.RadarProfileSerializer.Import(settings.RadarProfileJson));
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException
                    or System.Text.Json.JsonException or NotSupportedException)
                {
                    // A malformed optional profile must not prevent startup;
                    // the individually validated legacy values above remain active.
                }
            }
            global::MphRead.Hud.Radar.RadarSettings.ClearModeProfiles();
            if (!string.IsNullOrWhiteSpace(settings.RadarModeProfilesJson))
            {
                try
                {
                    foreach ((string mode, global::MphRead.Hud.Radar.RadarProfile profile) in
                        global::MphRead.Hud.Radar.RadarProfileSerializer.ImportModes(settings.RadarModeProfilesJson))
                    {
                        global::MphRead.Hud.Radar.RadarSettings.SetModeProfile(mode, profile);
                    }
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException
                    or System.Text.Json.JsonException or NotSupportedException)
                {
                    global::MphRead.Hud.Radar.RadarSettings.ClearModeProfiles();
                }
            }
            global::MphRead.Hud.Radar.RadarSettings.ClearDeviceProfiles();
            if (!string.IsNullOrWhiteSpace(settings.RadarDeviceProfilesJson))
            {
                try
                {
                    foreach ((string device, global::MphRead.Hud.Radar.RadarProfile profile) in
                        global::MphRead.Hud.Radar.RadarProfileSerializer.ImportModes(settings.RadarDeviceProfilesJson))
                    {
                        if (Enum.TryParse(device, true, out global::MphRead.Hud.Radar.RadarDeviceClass parsed)
                            && Enum.IsDefined(parsed))
                            global::MphRead.Hud.Radar.RadarSettings.SetDeviceProfile(parsed, profile);
                    }
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException
                    or System.Text.Json.JsonException or NotSupportedException)
                {
                    global::MphRead.Hud.Radar.RadarSettings.ClearDeviceProfiles();
                }
            }
            if (TryVolume(settings.SfxVolume, out float sfx))
            {
                Sfx.Volume = sfx;
            }
            if (TryVolume(settings.MusicVolume, out float music))
            {
                // Not a plain assignment: the gain only reaches the stream
                // when a track starts, so a volume set mid-match would not be
                // heard until the next one.
                Music.SetUserVolume(music);
            }
            if (Enum.TryParse(settings.Language, out Language language))
            {
                // Korean builds have no localised text of their own; the
                // console path makes the same substitution.
                Scene.Language = Paths.MphKey == "AMHK0" ? Language.Japanese : language;
            }
            // Read by the renderer as it builds each scene, so a change here
            // reaches the next match; the resolution scale reaches the current
            // one on its next resize.
            RenderOptions.ResolutionScale = RenderOptions.ParseScale(settings.ResolutionScale,
                RenderOptions.ResolutionScale);
            RenderOptions.FieldOfView = RenderOptions.ParseFieldOfView(settings.FieldOfView);
            RenderOptions.Lighting = RenderOptions.ParseOnOff(settings.Lighting, RenderOptions.Lighting);
            RenderOptions.Fog = RenderOptions.ParseOnOff(settings.Fog, RenderOptions.Fog);
            // Quality parsing lives in RenderOptions so every frontend uses
            // the same preset defaults and legacy settings.json mapping.
            RenderOptions.ApplyQuality(settings);
            Cosmetics.Presentation.CosmeticPresentationPreferences.Apply(settings,
                OperatingSystem.IsAndroid()
                    || RenderOptions.GraphicsPreset == GraphicsPreset.Performance);
            RenderOptions.ShowFps = RenderOptions.ParseOnOff(settings.ShowFps, RenderOptions.ShowFps);
            // How often the picture is drawn. It does not touch the
            // simulation, which runs at 60 Hz whatever this says -- see
            // Mods/Render/FrameTiming.cs.
            Render.FrameTiming.FrameRateCap = Render.FrameTiming.ParseCap(settings.FrameRateCap,
                Render.FrameTiming.FrameRateCap);
            RenderOptions.VisualStyle = !String.IsNullOrWhiteSpace(settings.VisualStyle)
                ? RenderOptions.ParseVisualStyle(settings.VisualStyle, RenderOptions.VisualStyle)
                : RenderOptions.ParseOnOff(settings.CelShading, RenderOptions.CelShading)
                    ? VisualStyle.Cel : VisualStyle.Original;
            RenderOptions.TexturePackId = RenderOptions.ResolveTexturePackId(
                settings.TexturePack, RenderOptions.GraphicsPreset);
            // Steps and outline strength are no longer player-configurable --
            // locked at 8 steps / 50%, regardless of what an old settings.json
            // (from before this was locked down) still has saved.
            RenderOptions.CelBands = 8;
            RenderOptions.CelEdge = 0.5f;
        }

        private static float ParseRadarNumber(string? value, float fallback, float minimum, float maximum)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                && float.IsFinite(parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;

        /// <summary>
        /// Apply the match rules, after <see cref="MatchFlow.Setup"/> has
        /// chosen the defaults for the mode.
        ///
        /// Order is the whole reason this is separate: Setup writes a point
        /// goal and a time limit derived from the mode, so anything applied
        /// before it is overwritten and anything applied instead of it would
        /// have to know every mode's defaults. The console menu's equivalent
        /// sits at the same call site for the same reason.
        ///
        /// A server's rules win over these. It publishes the point goal and
        /// the clock in its match state, which is adopted a few frames later
        /// -- the local numbers are what a client plays by until the server
        /// says otherwise, rather than a second opinion about a running
        /// match.
        /// </summary>
        public static void ApplyMatchRules(Scene scene)
        {
            MenuSettings? settings = Current;
            if (settings == null)
            {
                return;
            }
            MatchRuntime match = scene.Match;
            MatchRules rules = match.Rules;
            if (TryTime(settings.TimeLimit, out float timeLimit) && timeLimit > 0)
            {
                match.MatchTime = timeLimit;
                rules = rules.With(timeLimit: TimeSpan.FromSeconds(timeLimit));
            }
            if (TryTime(settings.TimeGoal, out float timeGoal) && timeGoal > 0)
            {
                rules = rules.With(objectiveTimeGoal: TimeSpan.FromSeconds(timeGoal));
            }
            if (Int32.TryParse(settings.PointGoal, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int pointGoal) && pointGoal > 0)
            {
                rules = rules.IsSurvival ? rules.With(startingLives: pointGoal) : rules.With(scoreGoal: pointGoal);
            }
            int damage = settings.DamageLevel switch
            {
                "low" => 0,
                "high" => 2,
                "medium" => 1,
                _ => rules.DamageLevel
            };
            match.RadarPlayers = settings.HunterRadar == "on";
            match.ApplyRules(rules.With(damageLevel: damage,
                friendlyFire: settings.FriendlyFire == "on", playerRadar: match.RadarPlayers,
                affinityWeapons: settings.AffinityWeapons == "on", octolithReset: settings.PointGoal != "off"));
            // Team play is derived from the selected mode.

        }

        /// <summary>
        /// "0.5" -> 0.5, clamped.
        ///
        /// Invariant first, because that is how the settings window writes it,
        /// and then the machine's own format, because the console menu writes
        /// these with <c>decimal.ToString()</c> -- so a file last saved from
        /// the menu on a French or German system holds "0,5". Reading only one
        /// of the two silently discards whichever half of the settings the
        /// other screen wrote.
        /// </summary>
        private static bool TryVolume(string? value, out float volume)
        {
            volume = 0;
            if (!Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float parsed)
                && !Single.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture,
                    out parsed))
            {
                return false;
            }
            volume = Math.Clamp(parsed, 0, 1);
            return true;
        }

        /// <summary>"7:00" or "7" -> seconds. The format the file already uses.</summary>
        private static bool TryTime(string? value, out float seconds)
        {
            seconds = 0;
            if (String.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string[] parts = value.Trim().Split(':');
            if (parts.Length > 3)
            {
                return false;
            }
            float total = 0;
            foreach (string part in parts)
            {
                if (!Int32.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int number) || number < 0)
                {
                    return false;
                }
                total = total * 60 + number;
            }
            seconds = total;
            return true;
        }
    }
}
