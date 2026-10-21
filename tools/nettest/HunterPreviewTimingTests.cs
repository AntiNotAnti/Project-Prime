using System;
using System.Reflection;
using MphRead.Mods.Render;

namespace MphRead.NetTest;

internal static class HunterPreviewTimingTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException("HUNTER PREVIEW TIMING " + message);
        }
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo modelField = typeof(HunterPreviewEntity).GetField("_model", fields)!;
        FieldInfo timeField = typeof(HunterPreviewEntity).GetField("_cosmeticTime", fields)!;
        FieldInfo stepsField = typeof(HunterPreviewEntity).GetField("_previewSteps", fields)!;
        // Exercise the real preview entity and canonical animation state machine
        // without loading ROM resources or creating an OpenGL context.
        foreach (int cardCount in new[] { 1, 7 })
        {
            var previews = new HunterPreviewEntity[cardCount];
            var models = new ModelInstance[cardCount];
            for (int card = 0; card < cardCount; card++)
            {
                var preview = previews[card] = new HunterPreviewEntity(null!);
                var model = models[card] = new ModelInstance(null!);
                model.AnimInfo.Node.Slot = 0;
                model.AnimInfo.FrameCount[0] = 1000;
                model.AnimInfo.Step[0] = 1;
                model.AnimInfo.Flags[0] = AnimFlags.None;
                modelField.SetValue(preview, model);
            }
            for (int tick = 1; tick <= 60; tick++)
                for (int card = 0; card < cardCount; card++)
                {
                    previews[card].Step();
                    Check(models[card].AnimInfo.Frame[0] == tick / 2,
                        $"{cardCount} cards tick {tick}: native frame follows gameplay 30 Hz");
                }
            for (int card = 0; card < cardCount; card++)
            {
                Check(Math.Abs((float)timeField.GetValue(previews[card])! - 1f) < .00001f,
                    "cosmetic/death clock retains one second per 60 simulation ticks");
                Check((long)stepsField.GetValue(previews[card])! == 60,
                    "presentation position remains in 60 Hz simulation units");
                foreach (int refresh in new[] { 120, 144, 240 })
                {
                    double drawStep = 60 + 60d / refresh;
                    Check(models[card].PresentationSamples.TryGet(models[card].AnimInfo, 0,
                        drawStep, out var sample)
                        && sample.FromFrame == 30 && sample.ToFrame == 31
                        && Math.Abs(sample.Fraction - 30d / refresh) < .000001,
                        $"{refresh} Hz presentation spans the complete two-tick native frame");
                }
                previews[card].Step();
                Check(models[card].AnimInfo.Frame[0] == 30
                    && models[card].PresentationSamples.TryGet(models[card].AnimInfo, 0, 61.5, out var later)
                    && Math.Abs(later.Fraction - .75f) < .000001f,
                    "odd simulation tick preserves the full native interpolation interval");
                previews[card].Reset();
                Check((long)stepsField.GetValue(previews[card])! == 0
                    && !models[card].PresentationSamples.TryGet(models[card].AnimInfo, 0, 61.5, out _),
                    "reset clears animation sampling and tick parity");
                previews[card].Step();
                Check(models[card].AnimInfo.Frame[0] == 30,
                    "first step after reset does not advance a 30 Hz native frame");
            }
        }
        Console.WriteLine($"Hunter preview timing: {checks} checks passed.");
        return checks;
    }
}
