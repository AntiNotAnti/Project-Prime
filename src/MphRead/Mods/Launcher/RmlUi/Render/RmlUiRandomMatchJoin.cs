#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal sealed partial class RmlUiLiveMenuServices
{
    private readonly object _randomJoinGate = new();
    private RandomAdmission? _randomJoin;
    private sealed class RandomAdmission
    {
        internal readonly NetClientJoinOwnership Ownership = new();
        internal readonly CancellationTokenSource Cancel;
        internal readonly string Host;
        internal readonly int Port;
        internal LaunchPlan Plan;
        internal int Suit;
        internal bool Ready;
        internal RandomAdmission(string host, int port, CancellationToken token)
        {
            Host = host; Port = port;
            Cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        }
    }

    public async Task<RmlUiJoinResult> JoinRandom(string endpoint, string playerName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!GameFiles.Ready || !ServerBrowserService.TryParseEndpoint(endpoint, LauncherPrefs.ServerAddress,
            LauncherPrefs.ServerPort, out string host, out int port))
            return new(false, default, "CONFIGURE GAME FILES AND SELECT A VALID SERVER");
        RandomAdmission candidate;
        lock (_randomJoinGate)
        {
            if (_randomJoin != null || NetSession.Active) return new(false, default, "LEAVE THE CURRENT SESSION FIRST");
            _randomJoin = candidate = new(host, port, token);
        }
        bool success = false;
        try
        {
            var hub = HubState.Capture();
            string name = String.IsNullOrWhiteSpace(playerName) ? hub.PlayerName : playerName.Trim();
            bool admitted = await Task.Run(() => NetLaunch.Connect(host, port, name, hub.DisplayHunter,
                color: hub.Suit, cancellationToken: candidate.Cancel.Token, ownership: candidate.Ownership,
                initializeFence: initialize =>
                {
                    lock (_randomJoinGate) return ReferenceEquals(_randomJoin, candidate)
                        && !candidate.Cancel.IsCancellationRequested && initialize();
                }),
                candidate.Cancel.Token).ConfigureAwait(false);
            candidate.Cancel.Token.ThrowIfCancellationRequested();
            lock (_randomJoinGate)
            {
                if (!admitted || !ReferenceEquals(_randomJoin, candidate) || !candidate.Ownership.IsCurrent)
                    return new(false, default, "SERVER ADMISSION CHANGED // SEARCHING AGAIN");
                candidate.Plan = new() { Kind = LaunchKind.Online, Hunter = NetSession.LocalHunter,
                    PlayerName = NetSession.PlayerName, RoomKey = "", Mode = GameMode.Battle, Port = port,
                    Lobby = new LobbyContext(endpoint, endpoint) };
                candidate.Suit = NetSession.LocalColor;
                candidate.Ready = success = true;
                return new(true, candidate.Plan, "");
            }
        }
        finally
        {
            if (!success)
            {
                lock (_randomJoinGate) if (ReferenceEquals(_randomJoin, candidate)) _randomJoin = null;
                candidate.Ownership.TryStopCurrent();
                candidate.Cancel.Dispose();
            }
        }
    }
    public bool CanAcceptRandomJoin(LaunchPlan plan)
    {
        lock (_randomJoinGate) return _randomJoin is { Ready: true } candidate
            && plan.Lobby != null && ReferenceEquals(plan.Lobby, candidate.Plan.Lobby)
            && !candidate.Cancel.IsCancellationRequested && candidate.Ownership.IsCurrent;
    }
    public void NoteRandomJoined(LaunchPlan plan)
    {
        lock (_randomJoinGate)
        {
            if (_randomJoin is not { Ready: true } candidate || plan.Lobby == null
                || !ReferenceEquals(plan.Lobby, candidate.Plan.Lobby)) return;
            // Called only after owner-thread AcceptJoin. Keep the ownership fence
            // through the preference write so a replacement client cannot make this
            // accepted endpoint overwrite its newer preferences.
            lock (NetSession.ClientLifecycleGate)
            {
                bool current = !candidate.Cancel.IsCancellationRequested && candidate.Ownership.IsCurrent;
                _randomJoin = null;
                candidate.Cancel.Dispose();
                if (!current) return;
                LauncherPrefs.PlayerName = candidate.Plan.PlayerName;
                LauncherPrefs.LastHunter = candidate.Plan.Hunter;
                LauncherPrefs.LastColor = candidate.Suit;
                LauncherPrefs.ServerAddress = candidate.Host;
                LauncherPrefs.ServerPort = candidate.Port;
                LauncherPrefs.LastKind = (int)LaunchKind.Online;
                LauncherPrefs.Save();
            }
        }
    }
    public void DiscardRandomJoin(LaunchPlan plan)
    {
        lock (_randomJoinGate)
        {
            if (_randomJoin is not { Ready: true } candidate || plan.Lobby == null
                || !ReferenceEquals(plan.Lobby, candidate.Plan.Lobby)) return;
            _randomJoin = null; candidate.Cancel.Cancel(); candidate.Ownership.TryStopCurrent(); candidate.Cancel.Dispose();
        }
    }
    public void CancelRandomJoin()
    {
        lock (_randomJoinGate)
        {
            if (_randomJoin is not { } candidate) return;
            _randomJoin = null; candidate.Cancel.Cancel(); candidate.Ownership.TryStopCurrent();
            if (candidate.Ready) candidate.Cancel.Dispose();
        }
    }
}
#endif
