using System;
using System.Buffers.Binary;
using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using MphRead;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Asset-free rule/configuration and real production command-path acceptance.
// Reflection supplies connected-peer fixtures, not a live UDP or asset-world match.
internal static class MomentumRulesTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, message);
    }
    private static MatchDefinition Definition(MovementModel model) => new()
    {
        RoomKey = "MP3 PROVING GROUND", Mode = GameMode.Battle, Format = MatchFormat.Auto,
        TimeLimitSeconds = 300, PointGoal = 7, MovementModel = model
    };
    private static SessionStatePacket State(MatchDefinition definition) => new()
    {
        Phase = SessionPhase.Lobby, Policy = ServerSessionPolicy.Lobby, Revision = 9,
        MatchId = 3, AuthorityEpoch = 7, OwnerSlot = 0, MaxPlayers = 8,
        Match = definition, WorldProfile = MatchWorldProfile.Resolve(8), MapGeneration = 1
    };
    internal static int Run()
    {
        try
        {
            _checks = 0;
            ParseAndRules(); Configuration(); Wire(); CommandOwnership(); ClientAdoption();
            Console.WriteLine($"MOMENTUM RULES PASS {_checks} assertions; asset-free configuration, wire and server command paths; no live gameplay acceptance");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM RULES FAIL " + ex); return 1; }
    }

    private static void ParseAndRules()
    {
        Check((byte)MovementModel.Native == 0 && (byte)MovementModel.Momentum == 1, "stable movement enum values");
        Check((uint)MatchModifierFlags.MomentumMovement == 32768 && NetConfig.ProtocolVersion == 75, "current protocol preserves v1 bit15 contract");
        foreach (string? text in new string?[] { null, "", "Native", "native", "NATIVE", "Classic", "classic" })
            Check(MovementModelRules.TryParse(text, out var model) && model == MovementModel.Native, "native/default parse " + text);
        foreach (string text in new[] { "Momentum", "momentum", "MOMENTUM" })
            Check(MovementModelRules.TryParse(text, out var model) && model == MovementModel.Momentum, "momentum parse " + text);
        foreach (string text in new[] { "0", "1", "2", "255", "momentum-v1", "Native,Momentum", "unknown", " Native", "Momentum ", " " })
            Check(!MovementModelRules.TryParse(text, out _), "reject unknown movement spelling " + text);
        Check(new MatchDefinition().MovementModel == MovementModel.Native, "old/default match remains Native");
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3, MovementModel.Traversal })
        {
            var match = Definition(model);
            Check(MovementModelRules.IsDefined(model), "known enum admitted");
            Check(MovementModelRules.FromRules(match.Rules) == model, "rule bit maps back to selected model");
            Check((match.Rules & (MatchModifierFlags.MomentumMovement | MatchModifierFlags.Quake3Movement | MatchModifierFlags.TraversalMovement)) == MovementModelRules.Modifier(model), "one canonical movement modifier");
            Check(LobbyRules.ValidateDefinition(match, out _) == LobbyResultCode.Ok, "ordinary mode accepted");
            Check(match.NormalizeLegacy().MovementModel == model, "normalization retains movement");
            Check((match with { Mode = GameMode.InstaGib }).NormalizeLegacy().MovementModel == model, "legacy normalization retains movement");
            Check((match with { Mode = GameMode.OneInTheChamber }).NormalizeLegacy().MovementModel == model, "life-mode normalization retains movement");
            Check(model == MovementModel.Momentum ? match.ModifierSummary.Contains("Momentum", StringComparison.OrdinalIgnoreCase)
                : !match.ModifierSummary.Contains("Momentum", StringComparison.OrdinalIgnoreCase), "summary identifies selected Momentum only");
        }
        foreach (MovementModel invalid in new[] { (MovementModel)4, (MovementModel)255 })
        {
            var bad = Definition(invalid);
            Check(!MovementModelRules.IsDefined(invalid), "unknown enum not defined");
            Check(LobbyRules.ValidateDefinition(bad, out string reason) == LobbyResultCode.InvalidConfiguration && reason.Length > 0, "unknown enum rejected by admission");
            Reject(() => _ = bad.Rules, "unknown enum cannot silently serialize as Native");
            Reject(() => MovementModelRules.Modifier(invalid), "unknown enum cannot map to a modifier");
        }
        var native = Definition(MovementModel.Native);
        Check(LobbyRules.ValidateDefinition(native with { BalancedMode = true }, out _) == LobbyResultCode.Ok, "Native Balanced still valid");
        Check(LobbyRules.ValidateDefinition(native with { EnhancedHunters = true }, out _, allowRetiredEnhanced: true) == LobbyResultCode.Ok, "archived Native Enhanced compatibility remains");
        var momentum = Definition(MovementModel.Momentum);
        Check(LobbyRules.ValidateDefinition(momentum with { BalancedMode = true }, out _) == LobbyResultCode.InvalidConfiguration, "Balanced Momentum rejected");
        Check(LobbyRules.ValidateDefinition(momentum with { EnhancedHunters = true }, out _, allowRetiredEnhanced: true) == LobbyResultCode.InvalidConfiguration, "Enhanced Momentum rejected even in archived compatibility");
        foreach (var variant in new[] { native with { ThirdPersonCamera = true }, native with { LowTier = true }, native with { Fiesta = true }, native with { InstaGib = true }, native with { NoImperialist = true } })
            Check(LobbyRules.ValidateDefinition(variant, out _) == LobbyResultCode.Ok, "existing Native independent modifier remains valid");
    }

    private static void Configuration()
    {
        var state = new SceneGameState(new ScenePlayerRegistry());
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3, MovementModel.Traversal })
        {
            var match = Definition(model) with { ThirdPersonCamera = true };
            match.ApplyInitialConfiguration(state);
            Check(state.MovementModel == model && state.Mode == GameMode.Battle && state.PointGoal == 7 && state.MatchTime == 300, "initial configuration selects complete movement rules");
            Check(state.ForScene(new ScenePlayerRegistry()).MovementModel == model, "scene bootstrap copy retains movement");
            Check(state.ThirdPersonCamera, "movement does not erase camera rule");
            match.ApplyModifiers(state);
            Check(state.MovementModel == model, "modifier path selects movement");
        }
        foreach (MovementModel invalid in new[] { (MovementModel)4, (MovementModel)255 })
        {
            var good = Definition(MovementModel.Momentum) with { ThirdPersonCamera = true };
            good.ApplyInitialConfiguration(state);
            var bad = Definition(invalid) with { Mode = GameMode.Survival, PointGoal = 19, FriendlyFire = true };
            Reject(() => bad.ApplyInitialConfiguration(state), "invalid initial configuration throws");
            Check(state.MovementModel == MovementModel.Momentum && state.Mode == GameMode.Battle && state.PointGoal == 7 && state.MatchTime == 300 && !state.FriendlyFire && state.ThirdPersonCamera, "invalid initial rules cannot partially mutate scene");
            Reject(() => bad.ApplyModifiers(state), "invalid modifier application throws");
            Check(state.MovementModel == MovementModel.Momentum && !state.FriendlyFire && state.ThirdPersonCamera, "invalid modifiers cannot partially mutate scene");
        }
    }

    private static void Wire()
    {
        byte[] bytes = new byte[SessionStatePacket.Size];
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3, MovementModel.Traversal })
        foreach (bool thirdPerson in new[] { false, true })
        {
            var state = State(Definition(model) with { ThirdPersonCamera = thirdPerson });
            state.Write(bytes);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(SessionStatePacket.Protocol28Size));
            Check(flags == (uint)state.Match.Rules, "session uses full uint modifier field");
            Check(SessionStatePacket.TryRead(bytes, out var decoded) && decoded.Match == state.Match, "movement/camera session roundtrip");
            var match = new MatchStatePacket { RoomKey = state.Match.RoomKey, NextRoomKey = "", Mode = (byte)GameMode.Battle,
                MatchId = 3, AuthorityEpoch = 7, RuleBits = (uint)state.Match.Rules };
            byte[] matchBytes = new byte[MatchStatePacket.Size]; match.Write(matchBytes);
            var readMatch = MatchStatePacket.Read(matchBytes);
            Check(readMatch.RuleBits == match.RuleBits && MovementModelRules.FromRules((MatchModifierFlags)readMatch.RuleBits) == model,
                "uint MatchState preserves movement bit15 and camera bit14");
            Check((((MatchModifierFlags)readMatch.RuleBits & MatchModifierFlags.ThirdPersonCamera) != 0) == thirdPerson, "camera bit independent of movement");
        }
        State(Definition(MovementModel.Native)).Write(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(SessionStatePacket.Protocol28Size), 65535);
        Check(SessionStatePacket.TryRead(bytes, out var all, validateDefinition: false)
            && (uint)all.Match.Rules == 65535 && all.Match.MovementModel == MovementModel.Momentum && all.Match.ThirdPersonCamera,
            "all sixteen defined modifier bits structurally survive uint decoding");
        foreach (uint unknown in new[] { 1u << 19, 1u << 31, 65535u | (1u << 19) })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(SessionStatePacket.Protocol28Size), unknown);
            Check(!SessionStatePacket.TryRead(bytes, out _, validateDefinition: false), "unknown modifier bits rejected before definition validation");
        }
        foreach (MovementModel invalid in new[] { (MovementModel)4, (MovementModel)255 })
        {
            Array.Fill(bytes, (byte)0xa5);
            var state = State(Definition(invalid));
            Reject(() => state.Write(bytes), "wire writer rejects unknown movement enum");
            Check(bytes.All(value => value == 0xa5), "invalid writer leaves destination unchanged");
        }
        var command = new LobbyCommandPacket { CommandId = 10, ExpectedRevision = 9, Type = LobbyCommandType.UpdateMatch,
            Configuration = State(Definition(MovementModel.Momentum) with { ThirdPersonCamera = true }) };
        byte[] commandBytes = new byte[LobbyCommandPacket.Size]; command.Write(commandBytes);
        Check(LobbyCommandPacket.TryRead(commandBytes, out var parsed) && parsed.Configuration.Match == command.Configuration.Match, "UpdateMatch command codec carries both high rule bits");
        BinaryPrimitives.WriteUInt32LittleEndian(commandBytes.AsSpan(14 + SessionStatePacket.Protocol28Size), 1u << 19);
        Check(!LobbyCommandPacket.TryRead(commandBytes, out _), "command codec rejects unknown modifier bit19");
    }

    private static object? Invoke(object target, string name, params object?[] args)
        => target.GetType().GetMethod(name, Hidden)!.Invoke(target, args);
    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, Hidden)!.SetValue(target, value);
    private static T Get<T>(object target, string name)
        => (T)target.GetType().GetField(name, Hidden)!.GetValue(target)!;
    private static void PeerSet(object peer, string name, object value)
        => peer.GetType().GetField(name)!.SetValue(peer, value);
    private static LobbyResultCode Execute(DedicatedServer server, object peer, LobbyCommandPacket command)
    {
        // Exercise the actual command codec before the actual authority/revision path.
        byte[] bytes = new byte[LobbyCommandPacket.Size]; command.Write(bytes);
        Check(LobbyCommandPacket.TryRead(bytes, out var decoded), "fixture command decodes");
        object?[] args = { peer, decoded, 1d, null };
        return (LobbyResultCode)Invoke(server, "ExecuteLobbyCommand", args)!;
    }

    private static void CommandOwnership()
    {
        Check(!NetSession.Active, "asset-free rules test starts outside a live session");
        var server = new DedicatedServer(0, 8) { SessionPolicy = ServerSessionPolicy.Lobby };
        Set(server, "_controlPlaneOnlyForTests", true);
        Set(server, "_phase", SessionPhase.Lobby);
        Set(server, "_lobbyMatch", Definition(MovementModel.Native));
        Set(server, "_lobbyOwnerClientId", 11u);
        Set(server, "_sessionRevision", (ushort)9);
        Type peerType = typeof(DedicatedServer).GetNestedType("Peer", BindingFlags.NonPublic)!;
        object MakePeer(int slot, uint clientId)
        {
            object peer = Activator.CreateInstance(peerType, nonPublic: true)!;
            PeerSet(peer, "SlotIndex", slot); PeerSet(peer, "ClientId", clientId);
            PeerSet(peer, "EndPoint", new IPEndPoint(IPAddress.Loopback, 28000 + slot));
            PeerSet(peer, "Name", "rules-" + slot); PeerSet(peer, "Hunter", (byte)Hunter.Samus);
            PeerSet(peer, "TeamIndex", (sbyte)-1); PeerSet(peer, "LobbyReady", true);
            return peer;
        }
        object owner = MakePeer(0, 11), other = MakePeer(1, 12), unauthenticated = MakePeer(2, 0);
        IList peers = Get<IList>(server, "_peers"); peers.Add(owner); peers.Add(other);
        var proposed = State(Definition(MovementModel.Momentum) with { ThirdPersonCamera = true });
        var update = new LobbyCommandPacket { CommandId = 1, ExpectedRevision = 9,
            Type = LobbyCommandType.UpdateMatch, Configuration = proposed };
        var original = Get<MatchDefinition>(server, "_lobbyMatch");
        Check(Execute(server, other, update) == LobbyResultCode.NotOwner, "ordinary peer cannot change movement");
        Check(Execute(server, unauthenticated, update) == LobbyResultCode.NotOwner, "client zero cannot impersonate owner");
        Check(Execute(server, owner, withRevision(8)) == LobbyResultCode.StaleRevision, "stale owner cannot change movement");
        Check(Get<MatchDefinition>(server, "_lobbyMatch") == original && Get<ushort>(server, "_sessionRevision") == 9,
            "unauthorized and stale updates leave definition and revision unchanged");
        // Unknown endpoints are rejected by HandleLobbyCommand before any execution,
        // independent of a well-formed owner-looking command body.
        byte[] packet = new byte[1 + LobbyCommandPacket.Size]; packet[0] = (byte)PacketType.LobbyCommand;
        update.Write(packet.AsSpan(1));
        Invoke(server, "HandleLobbyCommand", new ReceivedPacket(new IPEndPoint(IPAddress.Loopback, 29999), packet, packet.Length), 1d);
        Check(Get<MatchDefinition>(server, "_lobbyMatch") == original && Get<ushort>(server, "_sessionRevision") == 9,
            "unconnected endpoint cannot update movement");
        Check(Execute(server, owner, update) == LobbyResultCode.Ok, "current owner applies requested movement");
        var configured = Get<MatchDefinition>(server, "_lobbyMatch");
        Check(configured.MovementModel == MovementModel.Momentum && configured.ThirdPersonCamera,
            "owner update preserves both high modifier bits");
        ushort current = Get<ushort>(server, "_sessionRevision");
        Check(current != 9 && !(bool)owner.GetType().GetField("LobbyReady")!.GetValue(owner)!
            && !(bool)other.GetType().GetField("LobbyReady")!.GetValue(other)!, "accepted movement update advances revision and clears ready roster");
        Check(Execute(server, owner, update) == LobbyResultCode.StaleRevision, "replayed old-revision update cannot replace accepted rule");
        var start = new LobbyCommandPacket { CommandId = 2, ExpectedRevision = current, Type = LobbyCommandType.StartMatch,
            Configuration = proposed };
        Check(Execute(server, owner, start) == LobbyResultCode.Ok, "real production start freezes selected movement");
        Check(Get<SessionPhase>(server, "_phase") == SessionPhase.Starting
            && Get<MatchDefinition>(server, "_frozenMatch").MovementModel == MovementModel.Momentum,
            "Starting phase owns frozen Momentum definition");
        foreach (SessionPhase phase in new[] { SessionPhase.Starting, SessionPhase.InMatch })
        {
            Set(server, "_phase", phase);
            var downgrade = update;
            downgrade.ExpectedRevision = Get<ushort>(server, "_sessionRevision");
            downgrade.Configuration = State(Definition(MovementModel.Native));
            Check(Execute(server, owner, downgrade) == LobbyResultCode.InvalidPhase, "owner cannot change active movement in " + phase);
            Check(Get<MatchDefinition>(server, "_frozenMatch").MovementModel == MovementModel.Momentum
                && Get<MatchDefinition>(server, "_lobbyMatch").MovementModel == MovementModel.Momentum,
                "rejected active update preserves frozen and configured movement");
        }
        var state = (SessionStatePacket)Invoke(server, "BuildSessionState")!;
        Check(state.Match.MovementModel == MovementModel.Momentum, "published active session uses frozen movement");

        LobbyCommandPacket withRevision(ushort revision)
        { var command = update; command.ExpectedRevision = revision; return command; }
    }

    private static void ClientAdoption()
    {
        Check(!NetSession.Active && NetSession.ServerSession == null && NetSession.ServerMatch == null,
            "client adoption fixture owns an inactive empty session");
        const BindingFlags hiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        var current = typeof(GameState).GetProperty("Current", hiddenStatic)!;
        var lastError = typeof(NetSession).GetProperty(nameof(NetSession.LastError))!;
        object? original = current.GetValue(null);
        string? originalError = NetSession.LastError;
        try
        {
            var bootstrap = new SceneGameState(new ScenePlayerRegistry());
            current.SetValue(null, bootstrap);
            NetSession.Stop();
            var lobby = State(Definition(MovementModel.Native));
            // Continuous policy avoids launcher map-prewarm workers. Adoption itself
            // has no transport requirement; no role or playback impersonation is used.
            lobby.Policy = ServerSessionPolicy.Continuous;
            lobby.MatchId = 100; lobby.AuthorityEpoch = 700; lobby.Revision = 20;
            NetSession.ApplySessionState(lobby);
            Check(NetSession.ServerSession?.Match.MovementModel == MovementModel.Native
                && bootstrap.MovementModel == MovementModel.Native, "client adopts initial Native lobby configuration");
            lobby.Match = lobby.Match with { MovementModel = MovementModel.Momentum };
            lobby.Revision++;
            NetSession.ApplySessionState(lobby);
            Check(NetSession.ServerSession?.Match.MovementModel == MovementModel.Momentum
                && bootstrap.MovementModel == MovementModel.Momentum, "same-identity lobby remains movement mutable");
            lobby.Match = lobby.Match with { MovementModel = MovementModel.Native };
            lobby.Revision++;
            NetSession.ApplySessionState(lobby);
            var frozen = lobby;
            frozen.Phase = SessionPhase.Starting; frozen.StartStage = StartStage.Loading;
            frozen.StartGeneration = 1; frozen.Revision++;
            NetSession.ApplySessionState(frozen);
            Check(NetSession.ServerSession?.Phase == SessionPhase.Starting
                && NetSession.ServerSession?.Match.MovementModel == MovementModel.Native,
                "Starting adopts and freezes chosen Native model");
            var illegal = frozen;
            illegal.Phase = SessionPhase.InMatch; illegal.Revision++;
            illegal.Match = illegal.Match with { MovementModel = MovementModel.Momentum };
            NetSession.ApplySessionState(illegal);
            Check(NetSession.ServerSession?.Revision == frozen.Revision
                && NetSession.ServerSession?.Phase == frozen.Phase
                && bootstrap.MovementModel == MovementModel.Native
                && NetSession.LastError?.Contains("frozen", StringComparison.OrdinalIgnoreCase) == true,
                "higher-revision same-match movement mutation is refused with error before state mutation");
            MatchStatePacket Control(SessionStatePacket session, MovementModel model) => new()
            {
                MatchId = session.MatchId, AuthorityEpoch = session.AuthorityEpoch,
                RoomKey = session.Match.RoomKey, NextRoomKey = "", Mode = (byte)session.Match.Mode,
                PointGoal = session.Match.PointGoal, TimeRemaining = session.Match.TimeLimitSeconds,
                Flags = MatchStatePacket.FlagInProgress,
                RuleBits = (uint)(session.Match with { MovementModel = model }).Rules
            };
            var acceptedControl = NetSession.ServerMatch!.Value;
            NetSession.ApplyMatchState(Control(frozen, MovementModel.Momentum), rotated: false);
            Check(NetSession.ServerMatch?.RuleBits == acceptedControl.RuleBits
                && NetSession.ServerSession?.Revision == frozen.Revision
                && bootstrap.MovementModel == MovementModel.Native,
                "reordered same-identity MatchState cannot flip frozen movement");
            foreach (bool oldEpoch in new[] { false, true })
            {
                var stale = illegal;
                stale.Revision += 100;
                if (oldEpoch) stale.AuthorityEpoch--; else stale.MatchId--;
                NetSession.ApplySessionState(stale);
                NetSession.ApplyMatchState(Control(stale, MovementModel.Momentum), rotated: false);
                Check(NetSession.ServerSession?.MatchId == frozen.MatchId
                    && NetSession.ServerSession?.AuthorityEpoch == frozen.AuthorityEpoch
                    && NetSession.ServerSession?.Revision == frozen.Revision
                    && NetSession.ServerMatch?.MatchId == frozen.MatchId
                    && NetSession.ServerMatch?.AuthorityEpoch == frozen.AuthorityEpoch,
                    "previous match/epoch cannot replace frozen client state");
            }
            var next = illegal;
            next.MatchId++; next.Revision += 2; next.StartGeneration++;
            NetSession.ApplySessionState(next);
            Check(NetSession.ServerSession?.MatchId == next.MatchId
                && NetSession.ServerSession?.Match.MovementModel == MovementModel.Momentum
                && NetSession.ServerMatch?.RuleBits == (uint)next.Match.Rules
                && bootstrap.MovementModel == MovementModel.Momentum,
                "new match identity legitimately selects Momentum from Native");
            var nextWorld = bootstrap.ForScene(new ScenePlayerRegistry());
            Check(nextWorld.MovementModel == MovementModel.Momentum && nextWorld.Owner == null,
                "new-scene configuration copy selects accepted model without borrowing an owner");
            NetSession.ApplySessionState(frozen);
            NetSession.ApplyMatchState(Control(frozen, MovementModel.Native), rotated: false);
            Check(NetSession.ServerSession?.MatchId == next.MatchId
                && NetSession.ServerMatch?.MatchId == next.MatchId
                && bootstrap.MovementModel == MovementModel.Momentum,
                "late previous-round packets cannot undo new identity selection");
            var restart = next;
            restart.AuthorityEpoch++; restart.Revision = 1; restart.MatchId = 1;
            restart.Match = restart.Match with { MovementModel = MovementModel.Native };
            NetSession.ApplySessionState(restart);
            Check(NetSession.ServerSession?.AuthorityEpoch == restart.AuthorityEpoch
                && NetSession.ServerSession?.Match.MovementModel == MovementModel.Native
                && bootstrap.MovementModel == MovementModel.Native,
                "new authority epoch can legitimately reset match id and movement");
            foreach (MovementModel selected in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3, MovementModel.Traversal })
            {
                NetSession.Stop();
                var matchFirst = State(Definition(selected));
                matchFirst.Policy = ServerSessionPolicy.Continuous;
                matchFirst.MatchId = 900; matchFirst.AuthorityEpoch = 901;
                matchFirst.Phase = SessionPhase.InMatch;
                MovementModel opposite = selected == MovementModel.Native ? MovementModel.Momentum : MovementModel.Native;
                NetSession.ApplyMatchState(Control(matchFirst, selected), rotated: false);
                Check(NetSession.ServerSession == null && NetSession.ServerMatch?.RuleBits == (uint)matchFirst.Match.Rules,
                    "genuine MatchState may establish first frozen model");
                NetSession.ApplyMatchState(Control(matchFirst, opposite), rotated: false);
                Check(NetSession.ServerMatch?.RuleBits == (uint)matchFirst.Match.Rules,
                    "same-identity MatchState cannot change first frozen model without a SessionState baseline");
                var contradiction = matchFirst;
                contradiction.Match = contradiction.Match with { MovementModel = opposite };
                NetSession.ApplySessionState(contradiction);
                Check(NetSession.ServerSession == null && NetSession.ServerMatch?.RuleBits == (uint)matchFirst.Match.Rules,
                    "first late non-lobby SessionState cannot contradict genuine frozen MatchState");
                NetSession.ApplySessionState(matchFirst);
                Check(NetSession.ServerSession?.Match.MovementModel == selected,
                    "matching late session adopts genuine MatchState model");

                NetSession.Stop();
                var mutable = matchFirst;
                mutable.Phase = SessionPhase.Lobby;
                mutable.Match = mutable.Match with { MovementModel = opposite };
                NetSession.ApplySessionState(mutable);
                // Lobby-generated ServerMatch is provisional. A newer complete
                // Starting definition must still be able to select either model.
                matchFirst.Phase = SessionPhase.Starting;
                matchFirst.StartStage = StartStage.Loading; matchFirst.StartGeneration = 1;
                matchFirst.Revision++;
                NetSession.ApplySessionState(matchFirst);
                Check(NetSession.ServerSession?.Phase == SessionPhase.Starting
                    && NetSession.ServerSession?.Match.MovementModel == selected,
                    "lagging mutable lobby baseline does not freeze synthesized MatchState rules");
            }
            Check(!NetSession.Active, "production client-adoption checks never start a transport session");
        }
        finally
        {
            NetSession.Stop();
            current.SetValue(null, original);
            lastError.SetValue(null, originalError);
        }
        Check(ReferenceEquals(current.GetValue(null), original), "client adoption restores original scene state and owner");
    }

}
