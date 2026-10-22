#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal sealed partial class RmlUiLiveMenuServices
{
    private readonly object _createdJoinGate = new();
    private CreatedAdmission? _createdJoin;
    private bool _createdJoinRetired;
    private sealed class CreatedAdmission(CancellationToken token)
    {
        internal readonly NetClientJoinOwnership Ownership = new();
        internal readonly CancellationTokenSource Cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        internal LaunchPlan Plan;
        internal LobbyCreationPreferences? Preferences;
        internal bool Ready;
    }

    public async Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CreatedAdmission candidate;
        lock (_createdJoinGate)
        {
            if (_createdJoinRetired || _createdJoin != null || NetSession.Active)
                return new(false,default,"LEAVE THE CURRENT SESSION FIRST");
            _createdJoin=candidate=new(token);
        }
        bool success=false;
        try
        {
            var outcome=await LobbyCreationService.CreateAsync(request,candidate.Cancel.Token,candidate.Ownership,
                initialize=>
                {
                    lock(_createdJoinGate) return ReferenceEquals(_createdJoin,candidate)
                        && !candidate.Cancel.IsCancellationRequested && initialize();
                }).ConfigureAwait(false);
            candidate.Cancel.Token.ThrowIfCancellationRequested();
            lock(_createdJoinGate)
            {
                if(!outcome.Joined) return new(false,default,outcome.Error);
                if(!ReferenceEquals(_createdJoin,candidate)||!candidate.Ownership.IsCurrent||outcome.Plan.Lobby==null||outcome.Preferences==null)
                    return new(false,default,"CREATED LOBBY CONNECTION CHANGED // RETRY");
                candidate.Plan=outcome.Plan;
                candidate.Preferences=outcome.Preferences;
                candidate.Ready=success=true;
                return new(true,candidate.Plan,"");
            }
        }
        finally
        {
            if(!success)
            {
                lock(_createdJoinGate) if(ReferenceEquals(_createdJoin,candidate))_createdJoin=null;
                candidate.Ownership.TryStopCurrent();
                candidate.Cancel.Dispose();
            }
        }
    }
    public bool CanAcceptCreatedLobby(LaunchPlan plan)
    {
        lock(_createdJoinGate) return _createdJoin is {Ready:true} candidate && plan.Lobby!=null
            && ReferenceEquals(plan.Lobby,candidate.Plan.Lobby) && !candidate.Cancel.IsCancellationRequested
            && candidate.Ownership.IsCurrent;
    }
    public void NoteCreatedLobby(LaunchPlan plan)
    {
        lock(_createdJoinGate)
        {
            if(_createdJoin is not {Ready:true} candidate || plan.Lobby==null || !ReferenceEquals(plan.Lobby,candidate.Plan.Lobby))return;
            lock(NetSession.ClientLifecycleGate)
            {
                bool current=!candidate.Cancel.IsCancellationRequested && candidate.Ownership.IsCurrent;
                _createdJoin=null;
                candidate.Cancel.Dispose();
                if(current)candidate.Preferences!.Commit();
            }
        }
    }
    public void DiscardCreatedLobby(LaunchPlan plan)
    {
        lock(_createdJoinGate)
        {
            if(_createdJoin is not {Ready:true} candidate || plan.Lobby==null || !ReferenceEquals(plan.Lobby,candidate.Plan.Lobby))return;
            _createdJoin=null;candidate.Cancel.Cancel();candidate.Ownership.TryStopCurrent();candidate.Cancel.Dispose();
        }
    }
    public void CancelCreatedLobby()
    {
        lock(_createdJoinGate)
        {
            if(_createdJoin is not {} candidate)return;
            _createdJoin=null;candidate.Cancel.Cancel();candidate.Ownership.TryStopCurrent();
            if(candidate.Ready)candidate.Cancel.Dispose();
        }
    }
}
#endif
