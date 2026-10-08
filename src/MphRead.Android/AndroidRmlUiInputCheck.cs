#if MPHREAD_RMLUI_ANDROID_CHECK
using System;
using System.IO;
using System.Threading.Tasks;
using Android.Views.InputMethods;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

internal sealed partial class AndroidRmlUiSession
{
    internal RmlUiDocumentToken BeginInputCheck()
    {
        // Inspect the actual local controller and document without starting
        // profile HTTP work whose completion can change the semantic revision.
        Open(RmlUiRouteArgument.HunterLicense, refreshLicense: false);
        _licenseController!.SelectFace(LicenseFace.Account); _license!.Refresh(); Host.Update();
        return _license.Document;
    }
    internal void EndInputCheck()
    {
        if (!GameFiles.Ready) OpenSetup(required: true); else Open(RmlUiRouteArgument.Home);
    }
}

internal sealed partial class AndroidRmlUiView
{
    private async Task RunRequestedChecks()
    {
        var intent = ((Android.App.Activity)Context!).Intent;
        if (intent?.GetBooleanExtra("rmlui-ime-check", false) == true) await RunInputCheck();
        if (intent?.GetBooleanExtra("rmlui-a11y-check", false) == true) await RunAccessibilityCheck();
        if (intent?.GetBooleanExtra("rmlui-hud-check", false) == true) await RunHudCheck();
    }
    // Controlled calls exercise the real Android InputConnection, immutable UI
    // snapshot and queued native editing scope. They do not certify vendor IMEs.
    private async Task RunInputCheck()
    {
        int assertions = 0;
        RmlUiDocumentToken document = default;
        try
        {
            document = await CheckOwner(s => s.BeginInputCheck());
            ulong epoch = await Focus("license_email");
            var email = await CheckUi(() => OnCreateInputConnection(new EditorInfo())
                ?? throw new InvalidOperationException("The actual Android editor connection was unavailable."));
            Check(await CheckUi(() => email.SetComposingText(new Java.Lang.String("é🙂中"), 1)), "begin/update preedit");
            await CheckOwner(s => { s.Host.Update(); return true; });
            Check(await CheckOwner(s => s.Host.TryGetTextInputState(out var value) && value.Composing), "native preedit active");
            Check(await CheckUi(() => email.CommitText(new Java.Lang.String("é🙂中"), 1)), "commit preedit");
            Check(await CheckOwner(s => s.Host.ReadField(document, "license_email") == "é🙂中"), "Unicode scalar commit");
            await WaitSnapshot(v => v.Value == "é🙂中" && v.TextState?.Composing == false);
            Check(await CheckUi(() => email.DeleteSurroundingText(1, 0)), "delete last scalar");
            Check(await CheckOwner(s => s.Host.ReadField(document, "license_email") == "é🙂"), "BMP deletion");
            await WaitSnapshot(v => v.Value == "é🙂");
            Check(await CheckUi(() => email.DeleteSurroundingText(2, 0)), "delete UTF16 surrogate pair");
            Check(await CheckOwner(s => s.Host.ReadField(document, "license_email") == "é"), "supplementary scalar deletion");
            await WaitSnapshot(v => v.Value == "é");
            epoch = await Focus("license_password");
            await CheckOwner(s => { s.Host.SetField(document, "license_password", "contract-secret"); return true; });
            await WaitSnapshot(v => v.Password && v.Value == "contract-secret");
            var password = await CheckUi(() => OnCreateInputConnection(new EditorInfo())
                ?? throw new InvalidOperationException("Password editor connection was unavailable."));
            Check(await CheckUi(() => password.GetTextBeforeCursorFormatted(50, 0)?.ToString() == ""), "password query redacted");
            Check(!await CheckUi(() => email.CommitText(new Java.Lang.String("stale"), 1)), "stale field connection rejected");
            Check(await CheckOwner(s => s.Host.ReadField(document, "license_password") == "contract-secret"), "stale commit cannot cross fields");
            Check(await CheckUi(() => password.SetComposingText(new Java.Lang.String("preedit"), 1)), "password composition starts");
            await CheckOwner(s => { s.ReleaseInput(); return true; });
            await WaitSnapshot(v => !v.TextState.HasValue || v.TextState.Value.FocusEpoch != epoch);
            Check(!await CheckUi(() => password.CommitText(new Java.Lang.String("late"), 1)), "focus-loss commit rejected");
            await CheckUi(() => { email.CloseConnection(); password.CloseConnection(); return true; });
            await CheckOwner(s => { s.Host.SetField(document, "license_email", ""); s.Host.SetField(document, "license_password", ""); s.EndInputCheck(); return true; });
            string report = $"PASS Android InputConnection {assertions} assertions: native preedit/Unicode commit, UTF16 deletion, password redaction, stale field/focus rejection";
            Console.WriteLine("[rmlui-android] " + report);
            AndroidRmlUiCheckReport.Write(Context!, "rmlui-android-ime-check.txt", report + "\n");
        }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] InputConnection check FAILED: " + ex); }

        void Check(bool okay, string name)
        { if (!okay) throw new InvalidOperationException("Android input check failed: " + name); assertions++; }
        async Task<ulong> Focus(string id)
        {
            ulong epoch = await CheckOwner(s =>
            {
                s.Host.Update();
                if (!s.Host.FocusDocument(document, id) || !s.Host.TryGetTextInputState(out var state))
                    throw new InvalidOperationException("The native field could not receive focus: " + id);
                return state.FocusEpoch;
            });
            await WaitSnapshot(v => v.TextState?.FocusEpoch == epoch);
            return epoch;
        }
    }
    private Task<T> CheckOwner<T>(Func<AndroidRmlUiSession, T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(session => { try { completion.TrySetResult(action(session)); } catch (Exception ex) { completion.TrySetException(ex); } });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private Task<T> CheckUi<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostUi(() => { try { completion.TrySetResult(action()); } catch (Exception ex) { completion.TrySetException(ex); } });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private async Task WaitSnapshot(Func<AndroidRmlUiInputSnapshot, bool> predicate)
    {
        long deadline = Environment.TickCount64 + 5000;
        while (!predicate(System.Threading.Volatile.Read(ref _snapshot)))
        {
            if (_stop.IsCancellationRequested || Environment.TickCount64 >= deadline)
                throw new TimeoutException("The native input snapshot did not reach the requested focus/value.");
            await Task.Delay(16);
        }
    }
}
#endif
