using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Effects;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal static class ReplayParticleChecks
{
    internal static void Run(string path, Vector2i size)
    {
        using var source = new PassiveReplayScene(path, size);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var pool = (Queue<EffectParticle>)typeof(Scene).GetField("_inactiveParticles", hidden)!.GetValue(source.Scene)!;
        var birth = typeof(Scene).GetMethod("InitEffectParticle", hidden)!;
        pool.Peek().Rotation = 215.14526f;
        var fresh = (EffectParticle)birth.Invoke(source.Scene, null)!;
        if (fresh.Rotation != 0) throw new InvalidDataException("Particle birth inherited its pooled predecessor's rotation.");
        // Use actual loaded assets and the production graph codec. One angle is
        // historical pool residue; the other belongs to an authored action.
        EffectElementEntry? plain = null, authored = null;
        for (int id = 1; id <= Metadata.Effects.Count && (plain == null || authored == null); id++)
        {
            Effect? effect = Read.GetEffect(id);
            if (effect == null) continue;
            bool needed = effect.Elements.Any(element => element.Actions.ContainsKey(FuncAction.SetParticleRotation)
                ? authored == null : plain == null);
            if (!needed) continue;
            var entry = source.Scene.SpawnEffectGetEntry(id, Matrix4.Identity);
            if (entry == null) continue;
            foreach (var element in entry.Elements)
                if (element.Actions.ContainsKey(FuncAction.SetParticleRotation)) authored ??= element;
                else plain ??= element;
        }
        if (plain == null || authored == null) throw new InvalidDataException("Particle test could not find both real effect rotation contracts.");
        fresh.Owner = plain; fresh.SetFuncIds(); plain.Particles.Add(fresh); fresh.Rotation = 215.14526f;
        var authoredParticle = (EffectParticle)birth.Invoke(source.Scene, null)!;
        authoredParticle.Owner = authored; authoredParticle.SetFuncIds(); authored.Particles.Add(authoredParticle); authoredParticle.Rotation = 73;
        using var checkpoint = ReplayWorldCheckpoint.Capture(source);
        using var restored = new PassiveReplayScene(path, size);
        checkpoint.Restore(restored);
        var elements = (List<EffectElementEntry>)typeof(Scene).GetField("_activeElements", hidden)!.GetValue(restored.Scene)!;
        var restoredPlain = elements.Last(element => element.EffectId == plain.EffectId && element.DefinitionIndex == plain.DefinitionIndex);
        var restoredAuthored = elements.Last(element => element.EffectId == authored.EffectId && element.DefinitionIndex == authored.DefinitionIndex);
        if (restoredPlain.Particles.Last().Rotation != 0 || restoredAuthored.Particles.Last().Rotation != 73)
            throw new InvalidDataException("Particle restore failed to normalize pool residue or preserve authored rotation.");
        Console.WriteLine("[replayparticles] PASS: real pooled birth resets rotation; historical world restore clears unowned angles and preserves authored rotation.");
    }
}
