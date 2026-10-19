using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MphRead;
using MphRead.Effects;
using MphRead.Entities;
using OpenTK.Mathematics;

// Diagnostic names for the EXISTING live schema-1 ReplayPresentationHash fields
// in Mods/Replay/SceneReplayProjection.cs. This does not compute/replace that
// hash, change its schema, normalize fields or decide canonical acceptance.
sealed partial class CheckWindow
{
    private Dictionary<string, string>? _poseFieldsBeforeDraw;
    private string? _poseProjectionBeforeRender;
    private object? _poseCanonicalFailure;
    private readonly List<object> _poseCameraTransactions = new();

    private Dictionary<string, string> PosePresentationFields()
    {
        PoseRequire(!Scene.Services.IsReplica, "named presentation witness supports existing live schema 1 only");
        var fields = new Dictionary<string, string>();
        void Add(string key, object value) => fields.Add(key, value switch
        {
            float number => number.ToString("R", CultureInfo.InvariantCulture)
                + "|f32:" + unchecked((uint)BitConverter.SingleToInt32Bits(number)).ToString("x8"),
            double number => number.ToString("R", CultureInfo.InvariantCulture)
                + "|f64:" + unchecked((ulong)BitConverter.DoubleToInt64Bits(number)).ToString("x16"),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)!
        });
        void Vector(string key, Vector3 value)
        { Add(key + ".X", value.X); Add(key + ".Y", value.Y); Add(key + ".Z", value.Z); }
        void Matrix(string key, Matrix4 value)
        { for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) Add($"{key}[{row},{col}]", value[row, col]); }
        Add("schema", (ushort)1); Add("recordingFrame", checked((uint)Scene.FrameCount)); Add("Random.Rng2", Scene.Random.Rng2);
        int index = 0;
        foreach (EntityBase entity in Scene.Entities)
        {
            string key = $"Entities[{index++}]";
            Add(key + ".Type", (int)entity.Type);
            Add(key + ".Identity", entity is PlayerEntity player ? player.SlotIndex : entity.Id);
            Add(key + ".Initialized", entity.Initialized); Add(key + ".Hidden", entity.Hidden);
            Add(key + ".Alpha", entity.Alpha); Add(key + ".Recolor", entity.Recolor); Matrix(key + ".Transform", entity.Transform);
            var models = ((IEnumerable)typeof(EntityBase).GetProperty("ReplayModels", PosePrivate)!.GetValue(entity)!)
                .Cast<ModelInstance>().ToArray();
            Add(key + ".ReplayModels.Count", models.Length);
            for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                var model = models[modelIndex]; var animation = model.AnimInfo;
                string modelKey = key + $".ReplayModels[{modelIndex}]";
                Add(modelKey + ".Model.Name", model.Model.Name); Add(modelKey + ".Active", model.Active);
                for (int slot = 0; slot < 2; slot++)
                {
                    Add(modelKey + $".AnimInfo.Index[{slot}]", animation.Index[slot]);
                    Add(modelKey + $".AnimInfo.PrevIndex[{slot}]", animation.PrevIndex[slot]);
                    Add(modelKey + $".AnimInfo.Frame[{slot}]", animation.Frame[slot]);
                    Add(modelKey + $".AnimInfo.Step[{slot}]", animation.Step[slot]);
                    Add(modelKey + $".AnimInfo.Flags[{slot}]", (int)animation.Flags[slot]);
                }
                Add(modelKey + ".AnimInfo.Node.Slot", animation.Node.Slot);
                Add(modelKey + ".AnimInfo.Material.Slot", animation.Material.Slot);
                Add(modelKey + ".AnimInfo.Texture.Slot", animation.Texture.Slot);
                Add(modelKey + ".AnimInfo.Texcoord.Slot", animation.Texcoord.Slot);
            }
            if (entity is BeamProjectileEntity beam)
            {
                Add(key + ".DrawFuncId", beam.DrawFuncId); Vector(key + ".Color", beam.Color);
                Vector(key + ".BackPosition", beam.BackPosition);
                for (int past = 0; past < beam.PastPositions.Length; past++) Vector(key + $".PastPositions[{past}]", beam.PastPositions[past]);
            }
        }
        Add("Entities.Terminator", -1);
        var elements = ((IEnumerable)PoseGet(Scene, "_activeElements")!).Cast<EffectElementEntry>().ToArray();
        Add("ActiveElements.Count", elements.Length);
        for (int elementIndex = 0; elementIndex < elements.Length; elementIndex++)
        {
            var element = elements[elementIndex]; string key = $"ActiveElements[{elementIndex}]";
            Add(key + ".EffectId", element.EffectId); Add(key + ".ElementName", element.ElementName);
            Add(key + ".CreationTime", element.CreationTime); Add(key + ".ExpirationTime", element.ExpirationTime);
            Add(key + ".Expired", element.Expired); Add(key + ".Flags", (uint)element.Flags);
            Matrix(key + ".OwnTransform", element.OwnTransform); Matrix(key + ".Transform", element.Transform);
            Vector(key + ".Acceleration", element.Acceleration); Add(key + ".ParticleAmount", element.ParticleAmount);
            Add(key + ".Particles.Count", element.Particles.Count);
            for (int particleIndex = 0; particleIndex < element.Particles.Count; particleIndex++)
            {
                var particle = element.Particles[particleIndex]; string particleKey = key + $".Particles[{particleIndex}]";
                Add(particleKey + ".ParticleId", particle.ParticleId);
                Add(particleKey + ".CreationTime", particle.CreationTime); Add(particleKey + ".ExpirationTime", particle.ExpirationTime);
                Vector(particleKey + ".Position", particle.Position); Vector(particleKey + ".Speed", particle.Speed);
                Add(particleKey + ".Scale", particle.Scale); Add(particleKey + ".Rotation", particle.Rotation);
                Add(particleKey + ".Alpha", particle.Alpha); Add(particleKey + ".Red", particle.Red);
                Add(particleKey + ".Green", particle.Green); Add(particleKey + ".Blue", particle.Blue);
                Add(particleKey + ".RwField1", particle.RwField1); Add(particleKey + ".RwField2", particle.RwField2);
                Add(particleKey + ".RwField3", particle.RwField3); Add(particleKey + ".RwField4", particle.RwField4);
            }
        }
        return fields;
    }

    private void RecordPoseCanonicalFailure(string projection, long actualSteps)
    {
        var after = PosePresentationFields();
        var differences = (_poseFieldsBeforeDraw ?? new()).Keys.Union(after.Keys)
            .Where(key => _poseFieldsBeforeDraw?.GetValueOrDefault(key) != after.GetValueOrDefault(key))
            .Select(key => new { field = key, before = _poseFieldsBeforeDraw?.GetValueOrDefault(key), after = after.GetValueOrDefault(key) }).ToArray();
        string[] previous = _posePreviousProjection!.Split(':'), current = projection.Split(':');
        _poseCanonicalFailure = new
        {
            tick = Scene.FrameCount, outputFrame = _poseOutput, actualSteps,
            alpha = MphRead.Mods.Render.FrameTiming.Alpha, presentationAlpha = MphRead.Mods.Render.FrameTiming.PresentationAlpha,
            previousCompletedOutputProjection = _posePreviousProjection, beforeRenderProjection = _poseProjectionBeforeRender,
            afterRenderProjection = projection, gameplayEqual = previous[0] == current[0], replayPresentationEqual = previous[1] == current[1],
            namedPresentationWitnessScope = "exact live schema-1 fields; before witness bounded to ticks360..400; IEEE float bits retained",
            beforePresentationFields = _poseFieldsBeforeDraw, afterPresentationFields = after, differences,
            rawDrawAnchors = PoseAnchorObservation(), lastCheckpointTick = _poseCaptured.DefaultIfEmpty(0).Max()
        };
    }
}
