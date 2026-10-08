#if MPHREAD_RMLUI_ANDROID_CHECK
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Android.OS;
using Android.Views;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render.Hud;
using Path = System.IO.Path;
using Environment = System.Environment;

namespace MphRead.Droid;

internal sealed partial class AndroidRmlUiSession
{
    internal void BeginHudCheck()
    {
        ClosePresenters();
        Open(RmlUiRouteArgument.Settings);
        Host.Update();
    }
    internal HudEditorSnapshot HudCheckSnapshot()
        => _hud?.SnapshotForCheck() ?? throw new InvalidOperationException("The native HUD editor was not opened.");
    internal string HudCheckGesture() => _hud?.PointerStateForCheck() ?? "editor closed";
    internal string HudCheckAcceptedProfile() => _hud?.AcceptedProfileForCheck()
        ?? throw new InvalidOperationException("The actual HUD editor handoff is unavailable.");
    internal string HudCheckSettingsDraft()
        => HudProfileStore.Serialize(_settingsPage?.CaptureHudDraft()
            ?? throw new InvalidOperationException("The actual Settings draft is unavailable."));
    internal void ActivateHudCheckButton(string id)
    {
        var document = Host.CurrentInputDocument;
        Host.Update();
        if (!Host.FocusDocument(document, id)) throw new InvalidOperationException("The actual native control cannot receive focus: " + id);
        Host.Input.Key(2, true); Host.Input.Key(2, false); Host.Update();
        while (Host.TryTakeIntent(out var intent))
            if (_hud?.HandleIntent(intent) != true && _settingsPage?.HandleAction(intent) != true)
                throw new InvalidOperationException("The controlled HUD action reached an unrelated route.");
        _hud?.Present(); Host.Update();
    }
    internal void EndHudCheck()
    {
        ClosePresenters();
        EndInputCheck();
    }
}

internal sealed partial class AndroidRmlUiView
{
    // Framework MotionEvent objects travel through the production UI-thread
    // touch adapter and its immutable document snapshot to the real owner.
    private async Task RunHudCheck()
    {
        int assertions = 0;
        bool opened = false;
        try
        {
            await CheckOwner(s => { s.BeginHudCheck(); return true; }); opened = true;
            string original = await CheckOwner(s => s.HudCheckSettingsDraft());
            int generation = HudProfiles.Generation;
            string disk = DiskFingerprint();
            await OpenEditor();
            var canvas = await Canvas();
            Check(canvas.Width > 0 && canvas.Height > 0, "actual positive native canvas bounds");
            Check(await CheckOwner(s => s.Pages.Manager.Page == s.Host.CurrentInputDocument), "native editor owns input document");
            await Activate("hud_unlock_all");
            var before = await CheckOwner(s => s.HudCheckSnapshot());
            var target = before.Elements.Last(e => e.Index != 0 && e.Enabled && e.Bounds.Width > 0 && e.Bounds.Height > 0
                && before.Surface.Contains(e.Bounds.Center));
            float x = canvas.X + target.Bounds.Center.X, y = canvas.Y + target.Bounds.Center.Y;
            float dx = Math.Clamp(canvas.Width * .04f, 12, 32), dy = Math.Clamp(canvas.Height * .04f, 12, 24);
            dx = SafePan(target.Bounds.Center.X, before.Surface.X, before.Surface.Width, dx);
            dy = SafePan(target.Bounds.Center.Y, before.Surface.Y, before.Surface.Height, dy);
            await Touch(MotionEventActions.Down, (3, x, y));
            await Touch(MotionEventActions.Move, (3, x + dx, y + dy));
            await Touch(MotionEventActions.Up, (3, x + dx, y + dy));
            var panned = await CheckOwner(s => s.HudCheckSnapshot());
            Check(panned.ProfileJson != before.ProfileJson && panned.CanUndo, "pointer ID 3 pans actual selected HUD element with undo");
            Check(await CheckOwner(s => s.HudCheckSettingsDraft()) == original, "pan remains detached from retained Settings draft");

            canvas = await Canvas();
            target = panned.Elements[panned.Selected];
            var hit = VisibleHit(panned, target); x = canvas.X + hit.X; y = canvas.Y + hit.Y;
            float distance = Math.Clamp(canvas.Width * .04f, 8, 20);
            float direction = hit.X + distance * 2 < panned.Surface.X + panned.Surface.Width - 2 ? 1 : -1;
            Check(panned.Surface.Contains(new(hit.X + direction * distance * 2, hit.Y)),
                "both pinch coordinates lie inside the actual HUD preview surface");
            Console.WriteLine($"[rmlui-android] HUD pinch start canvas={canvas}, selected={panned.Selected}, bounds={target.Bounds}, pointer=({x},{y}), distance={distance}, scale={panned.Scale}");
            await Touch(MotionEventActions.Down, (3, x, y));
            await Trace("first down");
            await Touch((MotionEventActions)((int)MotionEventActions.PointerDown | (1 << 8)), (3, x, y), (8, x + direction * distance, y));
            await Trace("second down");
            await Touch(MotionEventActions.Move, (3, x, y), (8, x + direction * distance * 2, y));
            await Trace("move");
            await Touch((MotionEventActions)((int)MotionEventActions.PointerUp | (1 << 8)), (3, x, y), (8, x + direction * distance * 2, y));
            await Touch(MotionEventActions.Up, (3, x, y));
            var pinched = await CheckOwner(s => s.HudCheckSnapshot());
            await Trace("release");
            Check(pinched.Scale != panned.Scale, "pointer IDs 3 and 8 pinch actual HUD scale");
            Check(pinched.CanUndo, "completed pinch enters actual HUD history");

            canvas = await Canvas();
            target = pinched.Elements[pinched.Selected];
            hit = VisibleHit(pinched, target); x = canvas.X + hit.X; y = canvas.Y + hit.Y;
            await Touch(MotionEventActions.Down, (3, x, y));
            await Touch(MotionEventActions.Move, (3, x + dx, y));
            await Touch(MotionEventActions.Cancel, (3, x + dx, y));
            string cancelled = (await CheckOwner(s => s.HudCheckSnapshot())).ProfileJson;
            await Touch(MotionEventActions.Move, (3, x + dx * 2, y));
            Check((await CheckOwner(s => s.HudCheckSnapshot())).ProfileJson == cancelled, "framework cancellation retires captured pointer");
            await CheckOwner(s => { s.Back(); s.Host.Update(); return true; });
            await WaitSnapshot(v => !v.HudCanvas.HasValue);
            Check(await CheckOwner(s => s.HudCheckSettingsDraft()) == original, "HUD Back restores exact retained Settings draft");

            await OpenEditor();
            await Activate("hud_unlock_all"); await Activate("hud_pick13"); await Activate("hud_nudge_left");
            string edited = (await CheckOwner(s => s.HudCheckSnapshot())).ProfileJson;
            Check(edited != original, "actual native nudge edits the detached profile");
            await Activate("hud_use");
            await WaitSnapshot(v => !v.HudCanvas.HasValue);
            string accepted = await CheckOwner(s => s.HudCheckAcceptedProfile());
            Check(await CheckOwner(s => s.HudCheckSettingsDraft()) == accepted, "Use stages full HUD into retained Settings without publishing");

            await OpenEditor(); canvas = await Canvas();
            before = await CheckOwner(s => s.HudCheckSnapshot());
            target = before.Elements.Last(e => e.Index != 0 && e.Enabled && !e.Locked && e.Bounds.Width > 0
                && e.Bounds.Height > 0 && before.Surface.Contains(e.Bounds.Center));
            x = canvas.X + target.Bounds.Center.X; y = canvas.Y + target.Bounds.Center.Y;
            var retired = await CheckOwner(s => s.Host.CurrentInputDocument);
            await Touch(MotionEventActions.Down, (3, x, y));
            await CheckOwner(s => { s.Back(); s.Host.Update(); return true; });
            await WaitSnapshot(v => v.Document != retired && !v.HudCanvas.HasValue);
            await Touch(MotionEventActions.Move, (3, x + dx, y + dy));
            await Touch(MotionEventActions.Up, (3, x + dx, y + dy));
            Check(await CheckOwner(s => s.HudCheckSettingsDraft()) == accepted, "retired document gesture cannot change resumed Settings draft");
            Check(await CheckOwner(s => !s.Host.IsAlive(retired)), "HUD Back retires real native document");
            Check(HudProfiles.Generation == generation, "gesture and staging never publish runtime HUD");
            Check(DiskFingerprint() == disk, "gesture, cancel and Use never save preferences or HUD profiles");

            string report = $"PASS Android HUD {assertions} assertions: actual MotionEvent IDs 3/8, native canvas, pan/pinch/history, cancellation, staged Settings handoff and stale document rejection";
            Console.WriteLine("[rmlui-android] " + report);
            AndroidRmlUiCheckReport.Write(Context!, "rmlui-android-hud-check.txt", report + "\n");
        }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] HUD check FAILED: " + ex); }
        finally
        {
            await CheckUi(() => { _input.Cancel(); return true; });
            if (opened) await CheckOwner(s => { s.EndHudCheck(); return true; });
        }

        void Check(bool okay, string name)
        { if (!okay) throw new InvalidOperationException("Android HUD check failed: " + name); assertions++; }
        float SafePan(float position, float start, float extent, float amount)
        {
            if (position + amount < start + extent - 8) return amount;
            if (position - amount > start + 8) return -amount;
            return start + extent / 2 - position;
        }
        System.Numerics.Vector2 VisibleHit(HudEditorSnapshot snapshot, HudEditorElement element)
        {
            float left = Math.Max(snapshot.Surface.X, element.Bounds.X);
            float top = Math.Max(snapshot.Surface.Y, element.Bounds.Y);
            float right = Math.Min(snapshot.Surface.X + snapshot.Surface.Width, element.Bounds.X + element.Bounds.Width);
            float bottom = Math.Min(snapshot.Surface.Y + snapshot.Surface.Height, element.Bounds.Y + element.Bounds.Height);
            Check(right > left && bottom > top, "gesture target overlaps the actual preview surface");
            return new((left + right) / 2, (top + bottom) / 2);
        }
        async Task Activate(string id) => await CheckOwner(s => { s.ActivateHudCheckButton(id); return true; });
        async Task Trace(string phase) => Console.WriteLine("[rmlui-android] HUD pinch " + phase + ": " + await CheckOwner(s => s.HudCheckGesture()));
        async Task OpenEditor()
        {
            await Activate("settings_hud_edit");
            await WaitSnapshot(v => v.HudCanvas is { Width: > 0, Height: > 0 });
        }
        async Task<RmlUiTextInputBounds> Canvas()
        {
            await WaitSnapshot(v => v.HudCanvas is { Width: > 0, Height: > 0 });
            return await CheckOwner(s => s.CaptureHudCanvas() ?? throw new InvalidOperationException("Native HUD canvas unavailable."));
        }
        async Task Touch(MotionEventActions action, params (int Id, float X, float Y)[] points)
        {
            Check(await CheckUi(() => {
                var properties = points.Select(p => new MotionEvent.PointerProperties { Id = p.Id }).ToArray();
                var coordinates = points.Select(p => new MotionEvent.PointerCoords { X = p.X, Y = p.Y, Pressure = 1, Size = 1 }).ToArray();
                long now = SystemClock.UptimeMillis();
                try {
                    using var motion = MotionEvent.Obtain(now, now, action, points.Length, properties, coordinates,
                        (MetaKeyStates)0, (MotionEventButtonState)0, 1, 1, 0, (Edge)0,
                        InputSourceType.Touchscreen, (MotionEventFlags)0)
                        ?? throw new InvalidOperationException("Android MotionEvent allocation failed.");
                    return _input.Touch(motion);
                }
                finally { foreach(var value in properties)value.Dispose();foreach(var value in coordinates)value.Dispose(); }
            }), "actual MotionEvent accepted by production touch adapter");
            await CheckOwner(s => { s.Host.Update(); return true; });
        }
        string DiskFingerprint()
        {
            string root = MphRead.Mods.Platform.AppPaths.UserDataDirectory;
            string[] files = { Path.Combine(LauncherPrefs.Directory, "launcher.txt"), Path.Combine(root, "Savedata", "settings.json"),
                Path.Combine(root, "Savedata", "hud-profiles", "active.json") };
            return String.Join("|", files.Select(file => File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : "missing"));
        }
    }
}
#endif
