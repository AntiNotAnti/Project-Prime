#if MPHREAD_RMLUI_POC && !ANDROID
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static RmlUiDocumentToken _nativeQueue;

    private static void TickNativeQueue()
    {
        if (RmlUiPrototype.Pages is not { } pages) return;
        var host = RmlUiPrototype.Runtime;
        var queue = _rmlMultiplayer?.QueueSnapshot;
        if (queue?.Visible != true)
        { RetireNativeQueue(); return; }
        if (!host.IsAlive(_nativeQueue))
        {
            _nativeQueue = default;
            if (pages.Manager.Page == default || pages.Manager.ModalCount != 0
                || _nativeHunters?.Active == true || _nativeAdmin?.Active == true) return;
            _nativeQueue = pages.Manager.OpenModal(new("multiplayer-queue", "pages/play/queue.rml", "play_queue_join"));
        }
        pages.Manager.Present(_nativeQueue, queue.Version, new Dictionary<string, RmlUiBindingValue>
        {
            ["play_queue_status"] = RmlUiBindingValue.FromText(queue.Status),
            ["play_queue_position"] = RmlUiBindingValue.FromText(queue.Position),
            ["play_queue_seconds"] = RmlUiBindingValue.FromText(queue.OfferSeconds.ToString()),
            ["visible:play_queue_offer"] = RmlUiBindingValue.FromBoolean(queue.CanAccept),
            ["disabled:play_queue_join"] = RmlUiBindingValue.FromBoolean(!queue.CanJoin),
            ["disabled:play_queue_accept"] = RmlUiBindingValue.FromBoolean(!queue.CanAccept),
            ["disabled:play_queue_decline"] = RmlUiBindingValue.FromBoolean(!queue.CanDecline)
        });
    }

    private static bool HandleNativeQueue(in RmlUiIntent intent)
    {
        if (_nativeQueue == default || intent.Document != _nativeQueue) return false;
        if (intent.Kind != RmlUiIntentKind.PlayQueueAction || RmlUiPrototype.Pages?.Manager.Accept(intent) != true) return true;
        switch (intent.Argument)
        {
            case 0: _rmlMultiplayer?.QueueJoin(); break;
            case 1: _rmlMultiplayer?.QueueAccept(); break;
            case 2: _rmlMultiplayer?.QueueDecline(); break;
            case 3: _rmlMultiplayer?.QueueLeave(); break;
        }
        TickNativeQueue();
        return true;
    }

    private static void RetireNativeQueue()
    {
        var document = _nativeQueue; _nativeQueue = default;
        if (document != default && RmlUiPrototype.Runtime.IsAlive(document)
            && RmlUiPrototype.Pages?.Manager.Top == document) RmlUiPrototype.Pages.Manager.CloseModal();
    }
}
#endif
