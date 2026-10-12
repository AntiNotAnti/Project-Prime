using System;
using System.Collections.Generic;
using System.Globalization;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Opt-in local input producer for real network clients. Never owns a bot slot.</summary>
internal static class NetAiClient
{
    internal static bool Enabled { get; private set; }
    internal static int Level { get; private set; }
    internal static uint Seed { get; private set; }
    private static Scene? _scene;
    private static uint _initialRng1, _initialRng2, _rng1, _rng2;
    private static int _randomDepth;
    private static long _inputs, _decisions, _altFrames, _spawns;
    private static double _travel;
    private static Vector3? _position;
    private static ushort _life;
    private static readonly HashSet<BeamType> Weapons = new();

    internal static bool Configure(bool enabled, string? level, string? seed, out string error)
    {
        Reset(); error = "";
        if (!enabled)
        {
            if (seed != null) { error = "-ai-seed requires -ai-net-client."; return false; }
            return true;
        }
        int difficulty = 2; uint rng = 1729;
        if (level != null && (!Int32.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out difficulty)
            || difficulty is < 0 or > 3))
        { error = "-ai-net-client level must be 0..3."; return false; }
        if (seed != null && (!UInt32.TryParse(seed, NumberStyles.None, CultureInfo.InvariantCulture, out rng) || rng == 0))
        { error = "-ai-seed must be a nonzero uint."; return false; }
        if (HitRig.Active || Environment.GetEnvironmentVariable("PRIME_NET2_SOAK") == "1")
        { error = "AI clients cannot use HitRig or the scripted soak tour."; return false; }
        Enabled = true; Level = difficulty; Seed = rng;
        return true;
    }

    internal static void Bind(Scene scene)
    {
        if (!Enabled || scene.Services.IsReplica || NetSession.Role != NetRole.Client || DemoPlayback.IsActive) return;
        _scene = scene; _position = null; _life = 0;
        _initialRng1 = Seed;
        _initialRng2 = unchecked(Seed * 1664525u + 1013904223u);
        _rng1 = _initialRng1; _rng2 = _initialRng2; _randomDepth = 0;
        Console.WriteLine($"[net-ai] level={Level} seed={Seed} rng1={_initialRng1} rng2={_initialRng2}; local client, not authority bot");
    }

    private static bool OwnsScene(Scene scene) => Enabled && ReferenceEquals(scene, _scene)
        && !scene.Services.IsReplica && NetSession.Role == NetRole.Client && !DemoPlayback.IsActive;
    internal static bool Drives(PlayerEntity player) => OwnsScene(player.OwningScene)
        && !player.IsBot && player.SlotIndex == NetSession.LocalSlot
        && (uint)player.SlotIndex < NetSession.SlotIsBot.Length && !NetSession.SlotIsBot[player.SlotIndex];

    internal static void ApplyInput(PlayerEntity player)
    {
        if (!Drives(player)) return;
        _inputs++;
        if (NetCampaignDrain.Quiescing) NetTestScript.ApplyQuiescentControls(player);
        else if (player.Health == 0) NetTestScript.Rest(player, wantBiped: false);
        else player.AiData.ProcessInput();
    }

    internal static void Decide(PlayerEntity player)
    {
        if (!Drives(player) || NetCampaignDrain.Quiescing) return;
        ushort life = NetPlayerLifecycle.Get(player.SlotIndex);
        if (life != _life) { _life = life; _position = null; _spawns++; }
        if (_position is { } previous) _travel += (player.Position - previous).Length;
        _position = player.Position;
        if (player.IsAltForm) _altFrames++;
        Weapons.Add(player.CurrentWeapon);
        _decisions++;
        using var random = UseRandom(player.OwningScene);
        player.AiData.Process();
    }

    // AI decisions use the real Scene.Random consumed by the native AI, but
    // retain their own stream across authority snapshot RNG synchronization.
    // Gameplay RNG is restored even if an AI call fails.
    internal readonly struct AiRandomScope : IDisposable
    {
        private readonly Scene _scene;
        private readonly uint _game1, _game2;
        private readonly bool _owns, _active;
        internal AiRandomScope(Scene scene)
        {
            _scene = scene; _game1 = scene.Random.Rng1; _game2 = scene.Random.Rng2;
            _active = OwnsScene(scene);
            _owns = _active && _randomDepth++ == 0;
            if (_owns) { scene.Random.SetRng1(_rng1); scene.Random.SetRng2(_rng2); }
        }
        public void Dispose()
        {
            if (!_active) return;
            _randomDepth--;
            if (!_owns) return;
            _rng1 = _scene.Random.Rng1; _rng2 = _scene.Random.Rng2;
            _scene.Random.SetRng1(_game1); _scene.Random.SetRng2(_game2);
        }
    }
    internal static AiRandomScope UseRandom(Scene scene) => new(scene);
    internal static void InitializeAtLoad(PlayerEntity player)
    {
        using var random = UseRandom(player.OwningScene);
        player.AiData.InitializeAtLoad();
    }
    internal static void InitializeAtSpawn(PlayerEntity player)
    {
        using var random = UseRandom(player.OwningScene);
        player.AiData.InitializeAtSpawn();
    }

    internal static bool HasLocalActivity => !Enabled || _inputs >= 60 && _decisions >= 60 && _travel >= 2;
    internal static object Capture() => new { Enabled, Level, Seed, InitialRng1 = _initialRng1,
        InitialRng2 = _initialRng2, CurrentAiRng1 = _rng1, CurrentAiRng2 = _rng2, LocalSlot = NetSession.LocalSlot, InputFrames = _inputs,
        DecisionFrames = _decisions, Travel = _travel, SpawnedLives = _spawns, AltFrames = _altFrames,
        Weapons, IsBot = _scene != null && (uint)NetSession.LocalSlot < _scene.Players.Items.Count
            ? _scene.Players.Items[NetSession.LocalSlot].IsBot : (bool?)null,
        RosterBot = (uint)NetSession.LocalSlot < NetSession.SlotIsBot.Length
            ? NetSession.SlotIsBot[NetSession.LocalSlot] : (bool?)null,
        ActivityPassed = HasLocalActivity };

    internal static void Reset()
    {
        Enabled = false; Level = 0; Seed = 0; _scene = null;
        _initialRng1 = _initialRng2 = _rng1 = _rng2 = 0; _randomDepth = 0; _inputs = _decisions = _altFrames = _spawns = 0;
        _travel = 0; _position = null; _life = 0; Weapons.Clear();
    }
}
