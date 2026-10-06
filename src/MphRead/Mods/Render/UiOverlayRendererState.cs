namespace MphRead.Mods.Render;

// Context-local native names must never cross a renderer generation. Kept free
// of platform APIs so lifecycle/failure policy can be checked without a GPU.
internal sealed class UiOverlayRendererState
{
    internal bool HasOwner { get; private set; }
    internal bool Modern { get; private set; }
    internal int Generation { get; private set; }
    internal bool Failed { get; private set; }

    internal bool Matches(bool modern, int generation) =>
        HasOwner && Modern == modern && Generation == generation;

    internal bool Begin(bool modern, int generation)
    {
        if (Matches(modern, generation)) return false;
        HasOwner = true;
        Modern = modern;
        Generation = generation;
        Failed = false;
        return true;
    }

    internal void MarkFailed() => Failed = true;

    internal void Forget()
    {
        HasOwner = false;
        Modern = false;
        Generation = 0;
        Failed = false;
    }

    internal static bool HasCompletePixels(int width, int height, int availableBytes) =>
        width > 0 && height > 0 && availableBytes >= 0
        && (long)width * height <= availableBytes / 4;
}
