#if MPHREAD_AVALONIA
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Explicit queue opt-in and seat acceptance. No gameplay session is
/// created until the server offers a seat and the user accepts it.</summary>
internal static class LobbyQueueDialog
{
    internal static Task<LobbyQueueClient?> ShowAsync(PrimeOverlayHost overlays, string host, int port, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<LobbyQueueClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        LobbyQueueClient? client = null;
        CancellationTokenRegistration cancellation = default;
        bool finished = false, connecting = false, accepting = false;
        var status = PrimeChrome.Text("This server is full. Wait for a player slot?", 14);
        var position = PrimeChrome.Text("Your place is held briefly if the connection is interrupted.", 12);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        PrimePanel? panel = null;
        void Finish(bool admitted)
        {
            if (finished) return;
            finished = true; timer.Stop(); cancellation.Dispose();
            var result = admitted ? client : null;
            if (!admitted) client?.Dispose();
            if (panel != null) overlays.Close(panel);
            completion.TrySetResult(result);
        }
        var accept = new PrimeButton("ACCEPT SEAT", () =>
        {
            if (client?.AcceptSeat() == true) { accepting = true; status.Text = "Preparing your player slot…"; }
        }, true) { IsEnabled = false };
        var decline = new PrimeButton("DECLINE", () => { client?.DeclineSeat(); Finish(false); }) { IsEnabled = false };
        PrimeButton? join = null;
        async void Join()
        {
            if (finished || connecting || client != null) return;
            connecting = true; join!.IsEnabled = false; status.Text = "Connecting to the queue…";
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                if (finished) return;
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault() ?? throw new InvalidOperationException("Server address could not be resolved.");
                client = new LobbyQueueClient(new IPEndPoint(address, port));
                timer.Start();
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { connecting = false; }
        }
        join = new PrimeButton("JOIN QUEUE", Join, true);
        panel = new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title("PLAYER WAITLIST"), status, position,
            join, PrimeChrome.Columns("*,*", accept, decline), new PrimeButton("LEAVE / CANCEL", () => Finish(false))));
        panel.DetachedFromVisualTree += (_, _) => { if (!finished) Finish(false); };
        timer.Tick += (_, _) =>
        {
            if (finished || client == null) return;
            client.Poll();
            if (client.Admitted) { Finish(true); return; }
            if (client.Error != null)
            {
                status.Text = client.Error; accept.IsEnabled = decline.IsEnabled = false;
                timer.Stop(); return;
            }
            if (client.Ended)
            {
                status.Text = client.Status; position.Text = "Close this panel to choose another server or join again.";
                accept.IsEnabled = decline.IsEnabled = false; timer.Stop(); return;
            }
            status.Text = accepting ? "Preparing your player slot…" : client.Status;
            position.Text = client.SeatAvailable
                ? $"A player slot is ready. Accept within {client.RemainingOfferSeconds} seconds."
                : client.Position > 0 ? $"Position {client.Position} of {client.QueueLength}"
                : "Waiting for the server…";
            accept.IsEnabled = client.SeatAvailable && !accepting;
            decline.IsEnabled = client.SeatAvailable;
            if (!client.SeatAvailable) accepting = false;
        };
        overlays.Show(panel, PrimeModalSize.Medium, () => Finish(false));
        cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => Finish(false)));
        return completion.Task;
    }
}
#endif
