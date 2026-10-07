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

        public static void ApplyAudioChannels(MenuSettings settings)
        {
            if (TryVolume(settings.EffectsVolume, out float effectsGain))
                AudioMixer.SetVolume(AudioBus.SoundEffects, effectsGain);
            if (TryVolume(settings.NotificationVolume, out float notificationGain))
                AudioMixer.SetVolume(AudioBus.Notifications, notificationGain);
            if (TryVolume(settings.WeaponVolume, out float weaponGain))
                AudioMixer.SetVolume(AudioBus.Weapons, weaponGain);
            if (TryVolume(settings.PlayerVolume, out float playerGain))
                AudioMixer.SetVolume(AudioBus.Player, playerGain);
        }

        /// <summary>
        /// Apply everything that can be applied the moment it changes: the
        /// audio mixer and the text language.
        ///
        /// Called when the launcher loads the file and again whenever the
        /// settings window commits, which is what makes the music slider take
        /// effect while a match is running rather than at the next launch.
        /// </summary>
        public static void Apply(MenuSettings settings)
        {
            Current = settings;
            ApplyAudioChannels(settings);
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
            // Render scale is observed by Scene.OnDrawFrame, which reallocates
            // its target when this value changes, so saving from the pause menu
            // reaches the running match without a window resize.
            RenderOptions.ResolutionScale = RenderOptions.ParseScale(settings.ResolutionScale,
                RenderOptions.ResolutionScale);
            // Read once a frame by the camera, so this reaches the match that
            // is running behind the settings page as soon as it is saved.
            RenderOptions.FieldOfView = RenderOptions.ParseFov(settings.FieldOfView,
                RenderOptions.FieldOfView);
            RenderOptions.Lighting = RenderOptions.ParseOnOff(settings.Lighting, RenderOptions.Lighting);
            RenderOptions.Fog = RenderOptions.ParseOnOff(settings.Fog, RenderOptions.Fog);
            RenderOptions.TextureFiltering = RenderOptions.ParseOnOff(settings.TextureFiltering,
                RenderOptions.TextureFiltering);
            RenderOptions.TextureMipmaps = RenderOptions.ParseOnOff(settings.TextureMipmaps,
                RenderOptions.TextureMipmaps);
            RenderOptions.TextureAnisotropy = Math.Clamp(
                RenderOptions.ParseInt(settings.TextureAnisotropy,
                    RenderOptions.TextureAnisotropy), 1, 16);
            if (Enum.TryParse(settings.TextureSampling, true, out TextureSamplingMode textureSampling)
                && Enum.IsDefined(textureSampling))
                RenderOptions.TextureSampling = textureSampling;
            RenderOptions.ShowFps = RenderOptions.ParseOnOff(settings.ShowFps, RenderOptions.ShowFps);
            RenderOptions.SmoothNativeHud = RenderOptions.ParseOnOff(settings.SmoothNativeHud,
                RenderOptions.SmoothNativeHud);
            // How often the picture is drawn. It does not touch the
            // simulation, which runs at 60 Hz whatever this says -- see
            // Mods/Render/FrameTiming.cs.
            Render.FrameTiming.FrameRateCap = Render.FrameTiming.ParseSavedCap(settings.FrameRateCap,
                Render.FrameTiming.FrameRateCap);
            RenderOptions.CelShading = RenderOptions.ParseOnOff(settings.CelShading,
                RenderOptions.CelShading);
            RenderOptions.CelBands = Math.Clamp(
                RenderOptions.ParseInt(settings.CelBands, RenderOptions.CelBands), 2, 8);
            RenderOptions.CelEdge = Math.Clamp(
                RenderOptions.ParseInt(settings.CelEdge,
                    (int)MathF.Round(RenderOptions.CelEdge * 100)) / 100f, 0, 1);

            if (Enum.TryParse(settings.GraphicsPreset, true, out GraphicsPreset preset))
            {
                RenderOptions.Preset = preset;
                // Fast translucency is deliberately part of the Performance
                // recipe rather than a fidelity default. Custom preserves the
                // current explicit value for command-line/diagnostic tuning.
                if (preset != GraphicsPreset.Custom)
                {
                    RenderOptions.Translucency = preset == GraphicsPreset.Performance
                        ? TranslucencyMode.Fast : TranslucencyMode.Native;
                }
            }
            if (Enum.TryParse(settings.AntiAliasing, true, out AntiAliasingMode aa))
                RenderOptions.AntiAliasing = aa;
            RenderOptions.SharpenStrength = RenderOptions.ParseInt(settings.SharpenStrength,
                RenderOptions.SharpenStrength);
            RenderOptions.Bloom = RenderOptions.ParseOnOff(settings.Bloom, RenderOptions.Bloom);
            RenderOptions.BloomIntensity = RenderOptions.ParseInt(settings.BloomIntensity,
                RenderOptions.BloomIntensity);
            if (Enum.TryParse(settings.ColorGrade, true, out ColorGradeProfile grade))
                RenderOptions.ColorGrade = grade;
            RenderOptions.Gamma = RenderOptions.ParseInt(settings.Gamma, RenderOptions.Gamma);
            RenderOptions.Contrast = RenderOptions.ParseInt(settings.Contrast, RenderOptions.Contrast);
            RenderOptions.Saturation = RenderOptions.ParseInt(settings.Saturation, RenderOptions.Saturation);
            RenderOptions.EnhancedLighting = RenderOptions.ParseOnOff(settings.EnhancedLighting,
                RenderOptions.EnhancedLighting);
            RenderOptions.AdvancedMaterials = RenderOptions.ParseOnOff(settings.AdvancedMaterials,
                RenderOptions.AdvancedMaterials);
            RenderOptions.DeferredPbr = RenderOptions.ParseOnOff(settings.DeferredPbr,
                RenderOptions.DeferredPbr);
            if (Enum.TryParse(settings.ShadowQuality, true, out ShadowQuality shadows))
                RenderOptions.Shadows = shadows;
            if (Enum.TryParse(settings.AmbientOcclusion, true, out AmbientOcclusionQuality ao))
                RenderOptions.AmbientOcclusion = ao;
            RenderOptions.ContactShadows = RenderOptions.ParseOnOff(settings.ContactShadows,
                RenderOptions.ContactShadows);
            RenderOptions.EnhancedFog = RenderOptions.ParseOnOff(settings.EnhancedFog,
                RenderOptions.EnhancedFog);
            RenderOptions.VolumetricFog = RenderOptions.ParseOnOff(settings.VolumetricFog,
                RenderOptions.VolumetricFog);
            RenderOptions.InternalHdr = RenderOptions.ParseOnOff(settings.InternalHdr,
                RenderOptions.InternalHdr);
            RenderOptions.Reflections = RenderOptions.ParseOnOff(settings.Reflections,
                RenderOptions.Reflections);
            RenderOptions.DynamicGlow = RenderOptions.ParseOnOff(settings.DynamicGlow,
                RenderOptions.DynamicGlow);
            RenderOptions.ShowCustomCosmetics = RenderOptions.ParseOnOff(settings.ShowCustomCosmetics, true);
            RenderOptions.CosmeticQuality = Enum.TryParse<Cosmetics.CosmeticEffectQuality>(settings.CosmeticQuality, true, out var cosmeticQuality)
                && Enum.IsDefined(cosmeticQuality) ? cosmeticQuality : Cosmetics.CosmeticEffectQuality.Medium;
            RenderOptions.TextureReplacements = RenderOptions.ParseOnOff(settings.TextureReplacements,
                RenderOptions.TextureReplacements);
            RenderOptions.CharacterModelReplacements = RenderOptions.ParseOnOff(
                settings.CharacterModelReplacements, RenderOptions.CharacterModelReplacements);
            if (Enum.TryParse(settings.TextureUpscale, true, out TextureUpscaleMode textureUpscale))
                RenderOptions.TextureUpscale = textureUpscale;
            if (Enum.TryParse(settings.TextureQuality, true, out TextureAssetQuality textureQuality)
                && Enum.IsDefined(textureQuality))
                RenderOptions.TextureQuality = textureQuality;
            DebugLog.Line("performance", Maintenance.PerformanceSummary(settings));
        }

        /// <summary>
        /// Apply the match rules, after <see cref="GameState.Setup"/> has
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
        public static void ApplyMatchRules() => ApplyMatchRules(GameState.Current);

        /// <summary>
        /// Scene-targeted form used by launch paths after that scene's Setup.
        /// This avoids depending on which scene currently owns the legacy
        /// GameState compatibility facade while a shell/server/replay scene may
        /// also exist in the process.
        /// </summary>
        public static void ApplyMatchRules(SceneGameState state)
        {
            MenuSettings? settings = Current;
            if (settings != null)
                ApplyMatchRules(settings, state);
        }

        /// <summary>Pure rule application shared by launch code and regressions.</summary>
        internal static void ApplyMatchRules(MenuSettings settings, SceneGameState state)
        {
            if (!state.Multiplayer) return;
            if (Network.NetSession.Active)
            {
                Network.NetSession.ActiveMatchDefinition?.ApplyModifiers(state);
                return;
            }

            if (TryTime(settings.TimeLimit, out float timeLimit))
                state.MatchTime = timeLimit > 0 ? timeLimit : -1;
            if (TryTime(settings.TimeGoal, out float timeGoal) && timeGoal > 0)
                state.TimeGoal = timeGoal;
            if (Int32.TryParse(settings.PointGoal, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int pointGoal) && pointGoal >= 0)
                state.PointGoal = pointGoal;

            // Not the damage level. It is pinned to medium -- see
            // GameState.DamageLevel -- because it scales every weapon's damage
            // and was the one match rule each machine read out of its own file.
            // The key stays in settings.json and is ignored.
            state.FriendlyFire = settings.FriendlyFire == "on";
            state.RadarPlayers = settings.HunterRadar == "on";
            state.AffinityWeapons = settings.AffinityWeapons == "on";
            state.EnhancedHunters = settings.EnhancedHunters == "on";
            // Advanced combat rules are opt-in. Explicit saved on values are preserved.
            state.Fiesta = settings.Fiesta == "on"; state.OneInTheChamber = state.Mode == GameMode.OneInTheChamber;
            state.InstaGib = settings.InstaGib == "on";
            state.LowTier = settings.LowTier == "on";
            state.NoImperialist = settings.NoImperialist == "on" && !state.InstaGib;
            state.BalancedMode = settings.BalancedMode == "on";
            state.SpawnProtection = settings.SpawnProtection == "on";
            // Shadow Freeze also requires an explicit opt-in.
            state.ShadowFreeze = settings.ShadowFreeze == "on";
            state.OctolithReset = settings.AutoReset == "on";
            if (state.Mode == GameMode.OneInTheChamber) { state.PointGoal = 2; state.MatchTime = -1; }
            // Teams is not set here. GameState.Setup derives it from the mode,
            // and the launcher passes the choice through as the team id it
            // gives each player -- turning it on underneath a free-for-all
            // would put everybody on team zero with nobody to shoot.
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
