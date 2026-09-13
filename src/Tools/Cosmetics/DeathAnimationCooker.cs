using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Cosmetics;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// Converts a bounded authoring document into the strict runtime format. glTF
/// importers should emit this small intermediate document; gameplay never
/// parses either JSON or glTF.
/// </summary>
internal static class DeathAnimationCooker
{
    private const int MaximumSourceBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        int skeletonCommand = Array.IndexOf(args, "-deathskeleton");
        if (skeletonCommand >= 0)
        {
            if (skeletonCommand + 2 >= args.Length
                || !Enum.TryParse(args[skeletonCommand + 2], true, out Hunter hunter)
                || ValueAfter(args, "-data") is not string data)
            {
                Console.Error.WriteLine(
                    "-deathskeleton MODEL HUNTER -data AMHE1_DIRECTORY");
                exitCode = 2;
                return true;
            }
            try
            {
                ContentEnvironment.Open(data, "AMHE1");
                ModelInstance instance = Read.GetModelInstance(args[skeletonCommand + 1]);
                byte[] signature = DeathAnimationSkeleton.ComputeSignature(hunter,
                    instance.Model.Nodes);
                Console.WriteLine($"signature\t{Convert.ToHexString(signature).ToLowerInvariant()}");
                for (int i = 0; i < instance.Model.Nodes.Count; i++)
                {
                    Node node = instance.Model.Nodes[i];
                    Console.WriteLine($"{i}\t{node.ParentIndex}\t{node.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                exitCode = 1;
            }
            return true;
        }
        int command = Array.IndexOf(args, "-deathanim");
        if (command < 0) return false;
        int outputIndex = Array.IndexOf(args, "-out");
        if (command + 1 >= args.Length || outputIndex < 0 || outputIndex + 1 >= args.Length)
        {
            Console.Error.WriteLine("-deathanim SOURCE.json -out OUTPUT.pda");
            exitCode = 2;
            return true;
        }
        try
        {
            Cook(args[command + 1], args[outputIndex + 1]);
            Console.WriteLine($"Cooked death animation: {Path.GetFullPath(args[outputIndex + 1])}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            exitCode = 1;
        }
        return true;
    }

    private static string? ValueAfter(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    internal static DeathAnimationClip ReadSource(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is 0 or > MaximumSourceBytes)
            throw new InvalidDataException("Death animation source is empty or exceeds 1 MiB.");
        DeathAnimationSource? source = JsonSerializer.Deserialize<DeathAnimationSource>(utf8,
            JsonOptions);
        if (source == null || source.Format != 1 || source.Hunter > Hunter.Guardian
            || source.Skeleton is not { Count: > 0 }
            || source.Tracks == null)
            throw new InvalidDataException("Death animation source header is invalid.");
        byte[] signature;
        try { signature = DeathAnimationSkeleton.ComputeSignature(source.Hunter, source.Skeleton); }
        catch (ArgumentException ex) { throw new InvalidDataException(ex.Message, ex); }
        var tracks = new DeathAnimationTrack[source.Tracks.Count];
        try
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                DeathAnimationTrackSource track = source.Tracks[i];
                if (track.NodeIndex >= source.Skeleton.Count || track.Frames == null)
                    throw new InvalidDataException("Animation track references an invalid skeleton node.");
                var frames = new CompressedDeathKeyframe[track.Frames.Count];
                for (int j = 0; j < frames.Length; j++)
                {
                    DeathAnimationFrameSource frame = track.Frames[j];
                    if (frame.Translation is not { Length: 3 }
                        || frame.Rotation is not { Length: 4 }
                        || frame.Scale is not { Length: 3 })
                        throw new InvalidDataException("Animation frame transform is malformed.");
                    frames[j] = new CompressedDeathKeyframe(frame.TimeMilliseconds,
                        new Vector3(frame.Translation[0], frame.Translation[1], frame.Translation[2]),
                        new Quaternion(frame.Rotation[0], frame.Rotation[1],
                            frame.Rotation[2], frame.Rotation[3]),
                        new Vector3(frame.Scale[0], frame.Scale[1], frame.Scale[2]));
                }
                tracks[i] = new DeathAnimationTrack(track.NodeIndex, frames);
            }
            return new DeathAnimationClip(source.Key, source.Hunter, signature,
                source.Duration, tracks, source.BlendDuration);
        }
        catch (ArgumentException ex) { throw new InvalidDataException(ex.Message, ex); }
    }

    private static void Cook(string sourcePath, string outputPath)
    {
        FileInfo info = new(Path.GetFullPath(sourcePath));
        if (!info.Exists || info.Length is 0 or > MaximumSourceBytes)
            throw new InvalidDataException("Death animation source is missing, empty, or exceeds 1 MiB.");
        DeathAnimationClip clip = ReadSource(File.ReadAllBytes(info.FullName));
        byte[] cooked = DeathAnimationCodec.Write(clip);
        string destination = Path.GetFullPath(outputPath);
        if (!Directory.Exists(Path.GetDirectoryName(destination)))
            throw new DirectoryNotFoundException("Death animation output directory does not exist.");
        File.WriteAllBytes(destination, cooked);
    }
}

internal sealed record DeathAnimationSource(int Format, string Key, Hunter Hunter,
    float Duration, float BlendDuration, IReadOnlyList<DeathSkeletonNode> Skeleton,
    IReadOnlyList<DeathAnimationTrackSource> Tracks);

internal sealed record DeathAnimationTrackSource(ushort NodeIndex,
    IReadOnlyList<DeathAnimationFrameSource> Frames);

internal sealed record DeathAnimationFrameSource(ushort TimeMilliseconds,
    float[] Translation, float[] Rotation, float[] Scale);
