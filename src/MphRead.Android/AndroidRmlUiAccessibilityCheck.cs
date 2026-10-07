#if MPHREAD_RMLUI_ANDROID_CHECK
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Graphics;
using Android.OS;
using Android.Views.Accessibility;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;
using A11yAction = Android.Views.Accessibility.Action;
using Path = System.IO.Path;
using Environment = System.Environment;

namespace MphRead.Droid;

internal sealed partial class AndroidRmlUiView
{
    // Actual Android framework node objects and native owner queue. This check
    // exercises provider semantics; physical TalkBack remains a separate gate.
    private async Task RunAccessibilityCheck()
    {
        int assertions = 0; RmlUiDocumentToken document = default;
        try
        {
            document = await CheckOwner(s => s.BeginInputCheck());
            await Wait(() => CheckUi(() => _accessibilityProvider.FindVirtualId("license_email") >= 0), "Account semantic nodes");
            Check(await CheckUi(() => AccessibilityNodeProvider == _accessibilityProvider), "actual SurfaceView exposes provider");
            int email = await CheckUi(() => _accessibilityProvider.FindVirtualId("license_email"));
            int password = await CheckUi(() => _accessibilityProvider.FindVirtualId("license_password"));
            Check(email >= 0 && password >= 0 && email != password, "independent real Account virtual nodes");
            Check(await CheckUi(() => {
                using var info = _accessibilityProvider.CreateAccessibilityNodeInfo(email);
                if (info == null) return false; using var bounds = new Rect(); info.GetBoundsInScreen(bounds);
                return info.ClassName == "android.widget.EditText" && info.ContentDescription == "EMAIL"
                    && info.Editable && info.Enabled && bounds.Width() > 0 && bounds.Height() > 0;
            }), "Android node role/name/editable/positive screen bounds");
            Check(await CheckUi(() => {
                using var info = _accessibilityProvider.CreateAccessibilityNodeInfo(password);
                return info != null && info.Password && String.IsNullOrEmpty(info.Text)
                    && info.ContentDescription == "PASSWORD" && (info.Actions & A11yAction.SetText) != 0;
            }), "secure Android node omits value and advertises text action");
            Check(await CheckUi(() => _accessibilityProvider.PerformAction(email, A11yAction.Focus, null)), "Android focus action queues native field");
            await Wait(() => CheckOwner(s => s.Host.FocusedElement() == "license_email"), "native keyboard focus");
            Check(await CheckOwner(s => s.Host.FocusedElement() == "license_email"), "owner receives provider focus");
            await Wait(() => CheckUi(() => { using var info = _accessibilityProvider.FindFocus(NodeFocus.Input); return info?.Focused == true; }), "published input focus");
            Check(await CheckUi(() => { using var info = _accessibilityProvider.FindFocus(NodeFocus.Input); return info?.Focused == true; }), "Android FindFocus reflects native keyboard focus");
            Check(await CheckUi(() => _accessibilityProvider.PerformAction(email, A11yAction.AccessibilityFocus, null)), "Android accessibility focus supported");
            Check(await CheckUi(() => { using var info = _accessibilityProvider.FindFocus(NodeFocus.Accessibility); return info?.AccessibilityFocused == true; }), "Android FindFocus reflects accessibility focus");
            await CheckOwner(s => true); // FIFO barrier after any provider focus work.
            ulong lastRevision = 0; int stableSamples = 0;
            await Wait(async () => {
                var current = await CheckOwner(s => _accessibility.Snapshot);
                stableSamples = current.Revision == lastRevision && current.Document == document ? stableSamples + 1 : 0;
                lastRevision = current.Revision;
                return stableSamples >= 3 && await CheckUi(() => _accessibilityProvider.CheckMatchesSnapshot(current));
            }, "focus/layout semantic projection");
            const string unicode = "日本語 café Привет";
            Check(await CheckUi(() => {
                using var arguments = new Bundle(); arguments.PutCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence, new Java.Lang.String(unicode));
                return _accessibilityProvider.PerformAction(email, A11yAction.SetText, arguments);
            }), "Android Unicode SetText queues owner command");
            await Wait(() => CheckOwner(s => s.Host.ReadField(document, "license_email") == unicode), "owner Unicode SetText");
            Check(await CheckOwner(s => s.Host.ReadField(document, "license_email") == unicode), "actual native field retains exact provider Unicode edit");
            Check(await CheckUi(() => { using var info = _accessibilityProvider.CreateAccessibilityNodeInfo(email); return info != null && String.IsNullOrEmpty(info.Text) && info.ContentDescription == "EMAIL"; }), "edited value remains outside Android semantic metadata");
            await CheckOwner(s => { s.Host.SetField(document, "license_password", "contract-secret"); s.Host.Update(); return true; });
            Check(await CheckUi(() => { using var info = _accessibilityProvider.CreateAccessibilityNodeInfo(password); return info != null && String.IsNullOrEmpty(info.Text) && !String.Equals(info.ContentDescription, "contract-secret"); }), "password value stays absent from provider queries");
            var stale = await CheckOwner(s => {
                var snapshot = _accessibility.Capture(s.Host); var node = snapshot.Nodes.Single(n => n.Id == "license_email");
                return new RmlUiAccessibilityCommand(snapshot.Document, snapshot.Revision, node.Key, RmlUiAccessibilityAction.SetText, "stale");
            });
            int back = await CheckUi(() => _accessibilityProvider.FindVirtualId("license_back"));
            Check(back >= 0 && await CheckUi(() => _accessibilityProvider.PerformAction(back, A11yAction.Click, null)), "real Android Back virtual button queues native click");
            await Wait(() => CheckOwner(s => !s.Host.IsAlive(document)), "typed Back retirement");
            Check(await CheckOwner(s => !s.Host.IsAlive(document)), "typed Back action retires Account document");
            await Wait(() => Task.FromResult(_accessibility.Snapshot.Document != document), "semantic document retirement");
            Check(!_accessibility.Enqueue(stale), "retired document provider command rejected");
            await CheckOwner(s => { s.EndInputCheck(); return true; });
            string report = $"PASS Android accessibility {assertions} assertions: actual virtual nodes, screen bounds, protected metadata, focus, Unicode SetText, typed Back and retirement";
            Console.WriteLine("[rmlui-android] " + report);
            AndroidRmlUiCheckReport.Write(Context!, "rmlui-android-accessibility-check.txt", report + "\n");
        }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] Accessibility check FAILED: " + ex); }
        finally
        {
            if (document != default) await CheckOwner(s => {
                if (s.Host.IsAlive(document)) { s.Host.SetField(document,"license_email",""); s.Host.SetField(document,"license_password",""); }
                s.EndInputCheck(); return true;
            });
        }
        void Check(bool okay, string name) { if (!okay) throw new InvalidOperationException("Android accessibility check failed: " + name); assertions++; }
        async Task Wait(Func<Task<bool>> predicate, string stage) {
            long deadline = Environment.TickCount64 + 5000;
            while (!await predicate()) {
                if (_stop.IsCancellationRequested || Environment.TickCount64 >= deadline) {
                    string owner = await CheckOwner(s => {
                        var snapshot = _accessibility.Snapshot;
                        return $"available={_accessibility.Available}, document={snapshot.Document.DocumentId}, revision={snapshot.Revision}, nodes={snapshot.Nodes.Count}, expectedAlive={s.Host.IsAlive(document)}, ids=[{String.Join(',', snapshot.Nodes.Select(n => n.Id).Where(id => id.Length != 0).Take(40))}]";
                    });
                    string provider = await CheckUi(() => _accessibilityProvider.CheckSnapshotSummary());
                    throw new TimeoutException($"Android accessibility {stage} timed out. Owner {owner}; provider {provider}.");
                }
                await Task.Delay(16);
            }
        }
    }
}
#endif
