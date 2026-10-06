using MphRead.Mods.Render;

int checks = 0;
void Check(bool value, string description)
{
    if (!value) throw new InvalidOperationException(description);
    Console.WriteLine("UIOVERLAY PASS " + description);
    checks++;
}

var owner = new UiOverlayRendererState();
Check(!owner.HasOwner && !owner.Failed, "new overlay has no context-local owner");
Check(owner.Begin(true, 10) && owner.Modern && owner.Matches(true, 10), "first Vulkan generation requires upload");
Check(!owner.Begin(true, 10), "surface reattach on the same device retains the uploaded raster");
owner.MarkFailed();
Check(!owner.Begin(true, 10) && owner.Failed, "failed setup is not retried every frame in one generation");
Check(owner.Begin(true, 11) && !owner.Failed, "device reconstruction clears failure and requires reupload");
owner.MarkFailed();
Check(owner.Begin(false, 11) && !owner.Failed && !owner.Modern, "backend switch discards the Vulkan failure state");
Check(owner.Begin(false, 12), "new EGL context cannot reuse old native names");
owner.MarkFailed();
owner.Forget();
Check(!owner.HasOwner && !owner.Failed && !owner.Matches(false, 12), "loss/teardown forgets ownership and failure");
Check(owner.Begin(false, 12), "same-number context after explicit teardown still requires upload");
Check(UiOverlayRendererState.HasCompletePixels(2, 3, 24), "complete tightly packed RGBA raster accepted");
Check(UiOverlayRendererState.HasCompletePixels(2, 3, 28), "reused producer buffer may contain extra capacity");
Check(!UiOverlayRendererState.HasCompletePixels(2, 3, 23), "truncated raster rejected");
Check(!UiOverlayRendererState.HasCompletePixels(0, 3, 24)
    && !UiOverlayRendererState.HasCompletePixels(2, -1, 24), "nonpositive dimensions rejected");
Check(!UiOverlayRendererState.HasCompletePixels(int.MaxValue, int.MaxValue, int.MaxValue), "pixel extent cannot wrap into a valid upload");
Check(!UiOverlayRendererState.HasCompletePixels(32768, 32768, 0), "4 GiB raster cannot wrap to zero required bytes");
Console.WriteLine($"UIOVERLAY {checks} checks PASS");
