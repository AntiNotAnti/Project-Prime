using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

internal static class RetroMotionPreviewCheck
{
    public static void Run()
    {
        byte[] clipSet = Fixture();
        var idle = new Anim { Duration = 1, Tick = .5, Keys = 3 };
        idle.Rot[10] = new List<double[]> {
            new[]{0d,0d,0d,1d}, new[]{0d,0d,0d,1d}, new[]{0d,0d,0d,1d}
        };
        idle.Trans[10] = new List<double[]> {
            new[]{0d,0d,0d}, new[]{2d,0d,0d}, new[]{4d,0d,0d}
        };
        RetroMotionPreview.Inventory sample = RetroMotionPreview.Build(
            clipSet, id => id == 7 ? idle : throw new InvalidDataException("Unexpected source clip"));
        Check(sample.Format == 1 && sample.TotalClips == 1 && sample.SampledClips == 1,
            "bounded clip index and sparse samples");
        var clip = sample.Clips.Single();
        Check(clip.Name == "idle" && clip.ResourceIds.Single() == 7 &&
            clip.Status == "sparse-source-pose-preview" &&
            clip.Samples.Count == RetroMotionPreview.SamplesPerClip,
            "sampled clip retains exact source ID");
        Check(clip.Samples[0].Bones.Single().Translation![0] == 0 &&
            clip.Samples[^1].Bones.Single().Translation![0] > 3.9 &&
            clip.Samples[2].Bones.Single().Rotation![3] == 1,
            "source transforms sampled without replacing animation");
        string json = System.Text.Json.JsonSerializer.Serialize(sample);
        Check(json.Contains("source-animation") == false &&
            json.Contains("sparse-source-pose-preview") &&
            json.Contains("TimeSeconds"), "sidecar is inspectable JSON");
        var invalid = new Anim { Duration = 1, Tick = 0, Keys = 2 };
        Check(RetroMotionPreview.Build(clipSet, _ => invalid).Clips[0].Status ==
            "unsupported-timing-or-channel-budget",
            "bad source animation timing cannot drive rendering");
        Check(RetroMotionPreview.Build(clipSet, _ => throw new InvalidDataException("missing"))
            .Clips[0].Status == "source-animation-unavailable",
            "missing source clip gets a traceable status rather than a fabricated pose");
        var nan = new Anim { Duration = 1, Tick = .5, Keys = 2 };
        nan.Rot[1] = new List<double[]> {new[]{double.NaN,0d,0d,1d},new[]{0d,0d,0d,1d}};
        Check(RetroMotionPreview.Build(clipSet, _ => nan).Clips[0].Status ==
            "source-animation-unavailable",
            "non-finite quaternion sample cannot be published");
        Console.WriteLine("PASS: local sparse source-motion sampling and invalid-clip refusal");
    }

    private static byte[] Fixture()
    {
        var raw = new byte[52];
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(0, 4), 0x00010001);
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(10, 4), 1);
        "idle\0"u8.CopyTo(raw.AsSpan(14));
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(23, 4), 7);
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(27, 4), 12);
        "idle\0"u8.CopyTo(raw.AsSpan(31));
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(44, 4), 3);
        return raw;
    }
    private static void Check(bool test, string description)
    {
        if(!test) throw new InvalidOperationException(description);
    }
}
