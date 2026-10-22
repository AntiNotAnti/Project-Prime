using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Multiplayer;

internal static class TokenRules
{
    internal const int LifetimeTicks = 20 * 60, MaximumTokens = 64;
    internal static void DropOnDeath(Scene scene, PlayerEntity victim)
    {
        var state = scene.GameState;
        if (!state.IsTokenMode || scene.Services.IsReplica || NetObjectiveSync.IsClient(scene)) return;
        int value = 1 + state.TokenCarried[victim.SlotIndex];
        state.TokenCarried[victim.SlotIndex] = 0;
        int count = 0; ItemInstanceEntity? oldest = null;
        foreach (var item in scene.GetItemInstanceEntities())
            if (item.TokenId > 0 && item.DespawnTimer > 0)
            { count++; if (oldest == null || item.TokenId < oldest.TokenId) oldest = item; }
        if (count >= MaximumTokens && oldest != null) oldest.DespawnTimer = 0;
        var token = new ItemInstanceEntity(new(victim.Position, ItemType.ArtifactKey, LifetimeTicks), victim.NodeRef, scene)
        { TokenId = state.NextTokenId++, TokenVictimSlot = victim.SlotIndex, TokenTeam = victim.TeamIndex, TokenValue = value };
        scene.AddEntity(token);
    }

    internal static void Process(Scene scene, ItemInstanceEntity token)
    {
        if (scene.Services.IsReplica || NetObjectiveSync.IsClient(scene) || !scene.GameState.IsTokenMode
            || scene.GameState.MatchState != MatchState.InProgress) return;
        foreach (var player in scene.GetPlayerEntities())
            if (player.Health > 0 && player.LoadFlags.TestFlag(LoadFlags.Active)
                && (player.Position - token.Position).LengthSquared <= 1.5f * 1.5f)
            { Collect(scene, token, player); break; }
    }

    internal static void Collect(Scene scene, ItemInstanceEntity token, PlayerEntity player)
    {
        var state = scene.GameState;
        if (token.DespawnTimer <= 0 || player.Health == 0 || scene.Services.IsReplica || NetObjectiveSync.IsClient(scene)) return;
        int slot = player.SlotIndex;
        if (state.Mode == GameMode.Headhunter)
        {
            state.TokenCarried[slot] = Math.Min(99999, state.TokenCarried[slot] + token.TokenValue);
            state.TokensCollected[slot] += token.TokenValue;
        }
        else if (state.IsTokenMode)
        {
            bool ally = state.Teams ? player.TeamIndex == token.TokenTeam : slot == token.TokenVictimSlot;
            if (ally) state.TokenDenies[slot]++;
            else { state.TokenConfirms[slot]++; state.Points[slot]++; }
        }
        token.DespawnTimer = 0;
    }

    internal static void Bank(Scene scene, PlayerEntity player)
    {
        var state = scene.GameState;
        if (state.Mode != GameMode.Headhunter || scene.Services.IsReplica || NetObjectiveSync.IsClient(scene)) return;
        int slot = player.SlotIndex, count = state.TokenCarried[slot];
        if (count == 0) return;
        state.Points[slot] += count; state.TokensBanked[slot] += count;
        state.LargestBank[slot] = Math.Max(state.LargestBank[slot], count); state.TokenCarried[slot] = 0;
    }
}
