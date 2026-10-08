#if MPHREAD_RMLUI_ANDROID
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

internal sealed partial class AndroidRmlUiSession
{
    private RmlUiDocumentToken _queueModal;

    private void PresentQueue()
    {
        var queue = _multiplayer.QueueSnapshot;
        if (!queue.Visible)
        {
            if (_queueModal != default && Pages.Manager.Top == _queueModal) Pages.Manager.CloseModal();
            _queueModal = default;
            return;
        }
        if (!Host.IsAlive(_queueModal))
        {
            if (Pages.Manager.Page == default || Pages.Manager.ModalCount != 0 || _hunter?.Active == true || _admin?.Active == true) return;
            _queueModal = Pages.Manager.OpenModal(new("play-queue", "pages/play/queue.rml", "play_queue_join"));
        }
        Pages.Manager.Present(_queueModal, queue.Version, new Dictionary<string, RmlUiBindingValue>
        {
            ["play_queue_status"] = RmlUiBindingValue.FromText(queue.Status),
            ["play_queue_position"] = RmlUiBindingValue.FromText(queue.Position),
            ["play_queue_seconds"] = RmlUiBindingValue.FromText(queue.OfferSeconds.ToString()),
            ["visible:play_queue_offer"] = RmlUiBindingValue.FromBoolean(queue.CanAccept || queue.CanDecline),
            ["disabled:play_queue_join"] = RmlUiBindingValue.FromBoolean(!queue.CanJoin),
            ["disabled:play_queue_accept"] = RmlUiBindingValue.FromBoolean(!queue.CanAccept),
            ["disabled:play_queue_decline"] = RmlUiBindingValue.FromBoolean(!queue.CanDecline)
        });
    }

    private bool HandleQueue(in RmlUiIntent intent)
    {
        if (intent.Kind != RmlUiIntentKind.PlayQueueAction || intent.Document != _queueModal
            || !Pages.Manager.Accept(intent)) return false;
        switch (intent.Argument)
        {
            case 0: _multiplayer.QueueJoin(); break;
            case 1: _multiplayer.QueueAccept(); break;
            case 2: _multiplayer.QueueDecline(); break;
            case 3: _multiplayer.QueueLeave(); break;
        }
        PresentQueue(); return true;
    }
}
#endif
