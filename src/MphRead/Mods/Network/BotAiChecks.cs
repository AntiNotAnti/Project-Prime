using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>
/// Pure/headless regression checks for bot decision policy and generated navigation.
/// Asset-backed combat is still covered by NetBotCheck; these checks make tactical
/// regressions cheap enough to run on every relevant change.
/// </summary>
public static class BotAiChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string name)
        {
            if(!condition)throw new InvalidOperationException("[bot-ai] FAIL "+name);
            checks++;Console.WriteLine("[bot-ai] PASS "+name);
        }

        AiFlags3 scripted = AiFlags3.Bit1 | AiFlags3.Bit2 | AiFlags3.Despawned
            | AiFlags3.Invulnerable | AiFlags3.Bit5 | AiFlags3.NoInput;
        AiFlags3 persistent = PlayerEntity.FilterBotLifecycleFlags(scripted, singlePlayer: false);
        Check((persistent & PlayerEntity.BotScriptedLifecycleFlags) == 0
            && persistent.TestFlag(AiFlags3.NoInput),
            "persistent bots ignore story destroy, fade, despawn and invulnerability actions");
        Check(PlayerEntity.FilterBotLifecycleFlags(scripted, singlePlayer: true) == scripted,
            "single-player scripted hunters retain native lifecycle actions");

        Check(PlayerEntity.PlayerAiData.TacticalViewportContainsForTest(new(.5f,.5f)),
            "viewport accepts center");
        Check(!PlayerEntity.PlayerAiData.TacticalViewportContainsForTest(new(-.01f,.5f))
            && !PlayerEntity.PlayerAiData.TacticalViewportContainsForTest(new(.5f,1.01f)),
            "viewport rejects negative and overflow coordinates");

        Check(!PlayerEntity.PlayerAiData.TacticalSplashClearanceForTest(2.5f,2f,false)
            && !PlayerEntity.PlayerAiData.TacticalSplashClearanceForTest(12f,2f,true)
            && PlayerEntity.PlayerAiData.TacticalSplashClearanceForTest(12f,2f,false),
            "self-damage weapons require target and wall blast clearance");

        float closeShock=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.ShockCoil,6,false,false,false,false,1,true);
        float closeImp=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.Imperialist,6,false,false,false,false,1,true);
        Check(closeShock>closeImp,"close range favors Shock Coil over Imperialist");

        float longShock=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.ShockCoil,28,false,false,false,false,1,true);
        float longImp=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.Imperialist,28,false,false,false,false,1,true);
        Check(longImp>longShock,"long range favors Imperialist over Shock Coil");
        Check(PlayerEntity.PlayerAiData.TacticalHunterWeaponBiasForTest(
            Hunter.Trace,BeamType.Imperialist,26,false)
            >PlayerEntity.PlayerAiData.TacticalHunterWeaponBiasForTest(
                Hunter.Trace,BeamType.Battlehammer,26,false),
            "Trace strongly prefers long-range Imperialist play");
        Check(PlayerEntity.PlayerAiData.TacticalHunterWeaponBiasForTest(
            Hunter.Noxus,BeamType.Imperialist,10,true)>0,
            "Noxus recognizes frozen-target finish opportunities");

        float ordinary=PlayerEntity.PlayerAiData.TacticalTargetUtilityForTest(
            14,true,false,false,1,0,false,2);
        float objective=PlayerEntity.PlayerAiData.TacticalTargetUtilityForTest(
            14,true,false,true,1,0,false,2);
        float threat=PlayerEntity.PlayerAiData.TacticalTargetUtilityForTest(
            14,true,false,false,1,0,true,2);
        Check(objective>ordinary&&threat>ordinary,"objective and recent threat raise target priority");
        Check(PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Headhunter,5,false,false,false)
            >PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Headhunter,1,false,false,false),
            "Headhunter prioritizes enemies carrying larger token banks");
        Check(PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Hardpoint,0,true,false,false)>0
            &&PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Hardpoint,0,false,false,false)==0,
            "Hardpoint prioritizes enemies occupying the active objective");
        Check(PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Relic,0,false,true,false)>0,
            "Relic prioritizes the current carrier");

        Check(MapNodePacker.NavigationAnchorTypeForTest(MapNavigationLinkKind.Jump,
            MapNavigationAnchorKind.Auto,false,4)==NodeType.Aerial,
            "jump destination infers aerial node");
        Check(MapNodePacker.NavigationAnchorTypeForTest(MapNavigationLinkKind.Teleporter,
            MapNavigationAnchorKind.Auto,true,0)==NodeType.Special,
            "teleporter endpoint infers special node");
        Check(MapNodePacker.NavigationAnchorTypeForTest(MapNavigationLinkKind.Manual,
            MapNavigationAnchorKind.AltForm,true,0)==NodeType.AltForm,
            "authored alt-form node overrides automatic semantics");

        var jumpDefinition=new MapDefinition();
        jumpDefinition.JumpPads.Add(new MapJumpPad{Position=new[]{0f,0f,0f},Target=new[]{4f,3f,0f}});
        var effectiveLinks=MapNodePacker.EffectiveLinks(jumpDefinition);
        Check(effectiveLinks.Count==1&&effectiveLinks[0].Kind==MapNavigationLinkKind.JumpPad
            &&effectiveLinks[0].ToNodeKind==MapNavigationAnchorKind.Aerial,
            "target-based jump pad contributes an aerial navigation link");

        var hazard=MapNodePacker.Analyze(new[]{Floor(-8,8,-8,8,0,Terrain.Lava)});
        Check(hazard.Types.Length>0&&hazard.Types.All(t=>t==NodeType.Hazard),
            "damaging terrain generates hazard nodes");

        var tiers=MapNodePacker.Analyze(new[]{
            Floor(-8,0,-8,8,0,Terrain.Metal),
            Floor(0,8,-8,8,4,Terrain.Metal)
        });
        Check(tiers.Types.Contains(NodeType.Vantage),"elevated ledge generates vantage nodes");
        bool hasOneWayDrop=false;
        for(int i=0;i<tiers.Positions.Length;i++)
        {
            foreach(int j in tiers.Neighbours[i])
            {
                if(tiers.Positions[i].Y-tiers.Positions[j].Y<2)continue;
                bool reverse=tiers.Neighbours[j].Contains(i);
                if(!reverse){hasOneWayDrop=true;break;}
            }
            if(hasOneWayDrop)break;
        }
        Check(hasOneWayDrop,"generated drop does not become an impossible reverse climb");

        Check(BotStrategicPolicy.FocusPenalty(false, 3, 3)
                > BotStrategicPolicy.FocusPenalty(true, 3, 3)
            && BotStrategicPolicy.NodeAssignmentPenalty(2, false, false)
                > BotStrategicPolicy.NodeAssignmentPenalty(2, false, true)
            && BotStrategicPolicy.NodeAssignmentPenalty(1, false, false) == 0f,
            "squad bots spread visible target focus and reserve scoring zones");
        Check(BotGoalPolicy.Utility(BotGoalKind.Pickup, 30f, .2f,
                currentlyCommitted: false, contested: false, recovery: true)
                > BotGoalPolicy.Utility(BotGoalKind.Pickup, 30f, .2f,
                    currentlyCommitted: false, contested: false, recovery: false),
            "injured bots only get recovery bonus for actual health pickups");
        Check(BotPositionUtility.FutureScore(12f, 12f, false, -1f)
            > BotPositionUtility.FutureScore(29f, 12f, false, 1f)
            && BotPositionUtility.FutureScore(12f, 12f, true, 1f)
                < BotPositionUtility.FutureScore(12f, 12f, true, -1f),
            "predictive positioning rewards future range and avoiding enemy aim");
        Check(BotWeaponTactics.HitProbability(14f, .05f, .6f, false)
                > BotWeaponTactics.HitProbability(14f, .25f, .6f, false)
            && BotWeaponTactics.HitProbability(14f, .25f, .6f, true)
                > BotWeaponTactics.HitProbability(14f, .25f, .6f, false),
            "observed lateral speed and freeze affect anticipated weapon precision");
        Check(BotRoutePolicy.ShouldUseNativeJump(NodeType.Aerial, 2f, .9f, true)
            && !BotRoutePolicy.ShouldUseNativeJump(NodeType.Aerial, 2f, .9f, false)
            && !BotRoutePolicy.ShouldUseNativeJump(NodeType.AltForm, 2f, .9f, true)
            && !BotRoutePolicy.ShouldUseNativeJump(NodeType.Aerial, 4f, .9f, true)
            && BotRoutePolicy.StalledAfterInterval(.1f, 30)
            && !BotRoutePolicy.StalledAfterInterval(1.5f, 30),
            "native jump eligibility and bounded stalled-route recovery");
        ushort[] routed = [2, 1, 1, 2, 1, 3];
        Check(BotRoutePolicy.NextHopIndex(routed, 0, 0, 4) == 1
            && BotRoutePolicy.NextHopIndex(routed, 0, 2, 4) == 2
            && BotRoutePolicy.NextHopIndex(routed, 0, 3, 4) == 3,
            "native directed route segments resolve the correct next hop");
        Check(BotRoutePolicy.NextHopIndex([1, 9], 0, 0, 4) == -1
            && BotRoutePolicy.NextHopIndex([0, 1], 0, 0, 4) == -1
            && BotRoutePolicy.NextHopIndex([1], 0, 0, 4) == -1
            && BotRoutePolicy.NextHopIndex(routed, 0, 4, 4) == -1,
            "native routing rejects malformed or unavailable destinations");
        Check(BotGoalPolicy.WantsObjective(true, false,
                float.PositiveInfinity, 0.75f)
            && BotGoalPolicy.WantsObjective(true, true, 23f, 0.75f)
            && !BotGoalPolicy.WantsObjective(true, true, 4f, 0.75f)
            && !BotGoalPolicy.WantsObjective(true, false, 19f, 0.19f)
            && !BotGoalPolicy.WantsObjective(false, false, 19f, 1f),
            "objective scoring competes with combat only when it should");
        Check(BotGoalPolicy.Utility(BotGoalKind.Objective, 28f, 0.7f,
                currentlyCommitted: true, contested: true)
                > BotGoalPolicy.Utility(BotGoalKind.Objective, 28f, 0.7f,
                    currentlyCommitted: false, contested: false)
            && BotGoalPolicy.Utility(BotGoalKind.Pickup, 30f, 0.2f,
                currentlyCommitted: false, contested: false)
                > BotGoalPolicy.Utility(BotGoalKind.Pickup, 30f, 1f,
                    currentlyCommitted: false, contested: false),
            "utility planner prioritizes contested, committed and recovery goals");
        Check(BotGoalPolicy.KeepCommittedGoal(true, true, false,
                float.PositiveInfinity, 0.65f)
            && BotGoalPolicy.KeepCommittedGoal(true, true, true, 18f, 0.65f),
            "committed objective survives hidden or distant combat distraction");
        Check(!BotGoalPolicy.KeepCommittedGoal(true, true, true, 4f, 0.65f)
            && !BotGoalPolicy.KeepCommittedGoal(true, true, true, 18f, 0.20f)
            && !BotGoalPolicy.KeepCommittedGoal(false, true, false,
                float.PositiveInfinity, 1f)
            && !BotGoalPolicy.KeepCommittedGoal(true, false, true, 18f, 1f),
            "urgent combat, critically low health, and empty commitments interrupt goals");
        Check(BotStrategicPolicy.NodeScore(BotSquadRole.Vanguard, 40f,
                owned: false, contested: false, hostileOccupier: false,
                teammateClaims: 0, nearbyTeammates: 0, hardpoint: false) > 0,
            "far but reachable objectives retain a nonzero strategic score");

        // Bot motor: smooth acceleration, reversal braking, deterministic aim limits.
        float move = 0;
        for (int i = 0; i < 8; i++)
            move = BotMotorMath.StepMoveAxis(move, 1, .25f, .40f);
        Check(move == 1, "motor reaches full native movement input");
        float reverseFirst = BotMotorMath.StepMoveAxis(move, -1, .25f, .40f);
        Check(reverseFirst > 0 && reverseFirst < 1,
            "direction reversal brakes instead of snapping");
        Check(BotMotorMath.StepMoveAxis(.12f, -1, .40f, .52f) == 0,
            "small-speed reversal must stop at zero, never cross in one tick");
        for (int i = 0; i < 8; i++)
            reverseFirst = BotMotorMath.StepMoveAxis(reverseFirst, -1, .25f, .40f);
        Check(reverseFirst == -1, "motor completes the requested reversal");
        Check(BotMotorMath.StepMoveAxis(float.NaN, 1, .25f, .40f) == 0,
            "non-finite movement inputs are rejected");
        float angularVelocity = 0;
        float firstTurn = BotMotorMath.StepAimAxis(ref angularVelocity, 35, 16, 4.8f);
        Check(firstTurn > 0 && firstTurn < 16, "aim accelerates instead of snapping");
        for (int i = 0; i < 10; i++)
            BotMotorMath.StepAimAxis(ref angularVelocity, 35, 16, 4.8f);
        Check(angularVelocity <= 16 && angularVelocity > 0,
            "aim preserves its angular speed limit");
        float previousTurn = angularVelocity;
        float reversingTurn = BotMotorMath.StepAimAxis(ref angularVelocity, -35, 16, 4.8f);
        Check(reversingTurn <= previousTurn && reversingTurn > -16,
            "opposite aim request decelerates before reversing");
        float rate = 10;
        float noWrongWay = BotMotorMath.StepAimAxis(ref rate, -1f, 16f, 3f);
        Check(noWrongWay == 0 && rate > 0,
            "reversing aim holds orientation until it can turn correctly");
        float fineTurn = BotMotorMath.StepAimAxis(ref angularVelocity, .15f, 16, 4.8f);
        Check(MathF.Abs(fineTurn) <= .15f && float.IsFinite(fineTurn),
            "reticle settles without overshooting small errors");
        Check(BotMotorMath.StepAimAxis(ref angularVelocity, float.NaN, 16, 4.8f) == 0
            && angularVelocity == 0, "invalid aim inputs fail closed");

        Check(!PlayerEntity.PlayerAiData.TacticalChargeDecisionNeedsRefreshForTest(
                weaponChanged: false, targetChanged: false, charging: true, remainingFrames: 0),
            "active charge does not randomly change its release decision");
        Check(PlayerEntity.PlayerAiData.TacticalChargeDecisionNeedsRefreshForTest(
                weaponChanged: true, targetChanged: false, charging: true, remainingFrames: 15)
            && PlayerEntity.PlayerAiData.TacticalChargeDecisionNeedsRefreshForTest(
                weaponChanged: false, targetChanged: false, charging: false, remainingFrames: 0),
            "charge decisions refresh on a weapon swap or between attacks");

        Check(MathF.Abs(BotMotorMath.SafePitchDegrees(new(0, 10, 10)) - 45) < 0.001f,
            "unnormalized target/hearing positions produce finite correct aim pitch");
        Check(BotMotorMath.SafePitchDegrees(OpenTK.Mathematics.Vector3.Zero) == 0
            && BotMotorMath.SafePitchDegrees(new(float.NaN, 0, 1)) == 0,
            "degenerate and non-finite perception vectors cannot poison bot aim");

        Check(BotDodgeMath.ThreatensPosition(
                new(-10, 0, 0), new(1, 0, 0), new(0, 0, .25f),
                radius: 1, horizonTicks: 15, out float incomingTicks, out _)
            && MathF.Abs(incomingTicks - 10) < 0.001f,
            "visible approaching projectile predicts imminent danger");
        Check(!BotDodgeMath.ThreatensPosition(
                new(10, 0, 0), new(1, 0, 0), Vector3.Zero,
                radius: 1, horizonTicks: 15, out _, out _),
            "receding projectile does not trigger evasive behavior");
        Check(!BotDodgeMath.ThreatensPosition(
                new(-10, 0, 4), new(1, 0, 0), Vector3.Zero,
                radius: 1, horizonTicks: 15, out _, out _),
            "projectile passing safely beside bot is ignored");
        Check(!BotDodgeMath.ThreatensPosition(
                Vector3.Zero, new(float.NaN, 0, 0), Vector3.Zero,
                radius: 1, horizonTicks: 15, out _, out _),
            "invalid projectile kinematics cannot affect bot movement");

        float stockHammer = PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.Battlehammer, 14, affinity: false, current: false,
            targetAltForm: false, targetFrozen: false, selfHealthFraction: 1,
            visible: true);
        float burstHammer = PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.Battlehammer, 14, affinity: false, current: false,
            targetAltForm: false, targetFrozen: false, selfHealthFraction: 1,
            visible: true, balancedMode: true);
        Check(burstHammer > stockHammer,
            "balanced burst rifle favors midrange where its actual projectiles excel");

        Vector3 gunshot = BotPerceptionMath.QuantizedSoundCue(
            Vector3.Zero, new Vector3(11.7f, 1.3f, 6.1f));
        Check(Vector3.Distance(gunshot, new Vector3(11.7f, 1.3f, 6.1f)) > 0.2f,
            "hearing remembers a coarse position, never a live exact target");
        Vector3 prediction = BotPerceptionMath.RememberedPosition(
            new(2, 0, 3), new(.48f, 0, 0), unseenFrames: 120);
        Check(MathF.Abs(prediction.X - 4.88f) < .02f,
            "visual memory stops extrapolating after twelve frames");
        Check(BotPerceptionMath.Confidence(40, 80, heardOnly: true) < .25f
            && BotPerceptionMath.Confidence(40, 80, heardOnly: false) == .5f,
            "audio uncertainty stays below last-seen visual confidence");
        Check(BotPerceptionMath.Confidence(0, 80, heardOnly: false) == 0,
            "expired contact has zero target confidence");

        Check(BotPositionUtility.Score(17, 15, 15, false, false, 0, 0, 0)
              > BotPositionUtility.Score(17, 19, 15, false, false, 0, 0, 0),
            "combat planner prefers movement toward useful weapon range");
        Check(BotPositionUtility.Score(12, 16, 12, true, true, 0, 0, 0)
              > BotPositionUtility.Score(12, 10, 12, true, false, 0, 0, 0),
            "wounded bot prefers breaking sight and making space");
        Check(BotPositionUtility.Score(12, 12, 12, false, false, -0.8f, 0, 0)
              > BotPositionUtility.Score(12, 12, 12, false, false, 0.8f, 0, 0),
            "healthy flanking bot prefers leaving the enemy aim cone");
        Check(BotPositionUtility.Score(12, 12, 12, false, false, 0, 7, 0)
              < BotPositionUtility.Score(12, 12, 12, false, false, 0, 0, 0),
            "combat planner reduces teammate crowding");

        Vector3 filteredVelocity = BotPerceptionMath.SmoothObservedVelocity(
            new(1, 0, 0), new(-1, 0, 0), reacquired: false);
        Check(filteredVelocity.X > -1 && filteredVelocity.X < 1
            && BotPerceptionMath.SmoothObservedVelocity(
                Vector3.Zero, new(-1, 0, 0), reacquired: true).X == -1,
            "bot prediction smooths visible reversal jitter but resets on reacquisition");
        Check(BotReactionPolicy.AcquisitionFrames(3, reacquired: false) <
                BotReactionPolicy.AcquisitionFrames(2, reacquired: false)
            && BotReactionPolicy.AcquisitionFrames(3, reacquired: true) <
                BotReactionPolicy.AcquisitionFrames(3, reacquired: false),
            "elite reaction is fast but not instantaneous; familiar corners reacquire quicker");
        Check(BotReactionPolicy.AcquisitionFrames(-200, false) ==
              BotReactionPolicy.AcquisitionFrames(0, false),
            "reaction difficulty clamps invalid tiers");

        Check(PlayerEntity.PlayerAiData.TacticalAmmoReserveBiasForTest(
                    BeamType.Imperialist, 1) <
              PlayerEntity.PlayerAiData.TacticalAmmoReserveBiasForTest(
                    BeamType.Imperialist, 5),
            "nearly depleted weapons are less valuable than well-supplied alternatives");
        Check(PlayerEntity.PlayerAiData.TacticalAmmoReserveBiasForTest(
                    BeamType.OmegaCannon, 1) == 0
              && PlayerEntity.PlayerAiData.TacticalAmmoReserveBiasForTest(
                    BeamType.PowerBeam, 100) == 0,
            "Omega Cannon and free Power Beam are not penalized for ammo scarcity");

        Check(BotStrategicPolicy.RoleForOrdinal(0, objectiveMode: true)
                == BotSquadRole.Anchor
            && BotStrategicPolicy.RoleForOrdinal(1, objectiveMode: true)
                == BotSquadRole.Vanguard
            && BotStrategicPolicy.RoleForOrdinal(2, objectiveMode: true)
                == BotSquadRole.Flanker,
            "team bots divide objective roles instead of all chasing one target");
        Check(BotStrategicPolicy.TargetBonus(BotSquadRole.Anchor,
                objectiveThreat: true, recentAttacker: false)
                > BotStrategicPolicy.TargetBonus(BotSquadRole.Flanker,
                    objectiveThreat: true, recentAttacker: false),
            "objective defenders prioritize threats to scoring zones");
        Check(BotStrategicPolicy.NodeScore(BotSquadRole.Vanguard, 5,
                owned: false, contested: true, hostileOccupier: true,
                teammateClaims: 0, nearbyTeammates: 0, hardpoint: false)
                > BotStrategicPolicy.NodeScore(BotSquadRole.Vanguard, 5,
                    owned: true, contested: false, hostileOccupier: false,
                    teammateClaims: 0, nearbyTeammates: 0, hardpoint: false),
            "assault roles prioritize contested enemy nodes");
        Check(BotStrategicPolicy.NodeScore(BotSquadRole.Anchor, 5,
                owned: true, contested: false, hostileOccupier: false,
                teammateClaims: 0, nearbyTeammates: 0, hardpoint: false)
                > BotStrategicPolicy.NodeScore(BotSquadRole.Flanker, 5,
                    owned: true, contested: false, hostileOccupier: false,
                    teammateClaims: 0, nearbyTeammates: 0, hardpoint: false),
            "anchor roles protect owned nodes more than flankers");
        Check(BotStrategicPolicy.NodeScore(BotSquadRole.Anchor, 5,
                owned: false, contested: false, hostileOccupier: false,
                teammateClaims: 2, nearbyTeammates: 1, hardpoint: false)
                < BotStrategicPolicy.NodeScore(BotSquadRole.Anchor, 5,
                    owned: false, contested: false, hostileOccupier: false,
                    teammateClaims: 0, nearbyTeammates: 0, hardpoint: false),
            "objective claims spread bots rather than clustering them");
        Check(BotStrategicPolicy.PickupScore(ItemType.HealthBig, .15f,
                alreadyOwned: false, uaFraction: 1, missileFraction: 1)
                > BotStrategicPolicy.PickupScore(ItemType.HealthBig, .95f,
                    alreadyOwned: false, uaFraction: 1, missileFraction: 1),
            "wounded bots recognize high-value live health pickups");
        Check(BotStrategicPolicy.PickupScore(ItemType.OmegaCannon, 1,
                alreadyOwned: true, uaFraction: 1, missileFraction: 1)
                > BotStrategicPolicy.PickupScore(ItemType.UASmall, 1,
                    alreadyOwned: true, uaFraction: 1, missileFraction: 1),
            "omega pickups outrank unnecessary ammo");
        Check(float.IsNegativeInfinity(BotStrategicPolicy.PickupScore(
                ItemType.ArtifactKey, .5f, false, 0, 0)),
            "strategy ignores unrelated or unsupported pickup types");

        Check(BotStrategicPolicy.ShouldDisengage(true, 3, 0, .55f)
            && !BotStrategicPolicy.ShouldDisengage(true, 1, 0, .55f)
            && !BotStrategicPolicy.ShouldDisengage(true, 3, 3, .55f),
            "wounded team bot escapes an unfavorable multi-enemy fight");
        Check(!BotStrategicPolicy.ShouldDisengage(false, 5, 0, .4f),
            "free-for-all duels do not become permanent retreats");

        Check(!BotStrategicPolicy.PickupAllowedForMode(ItemType.Imperialist, weaponLocked: true)
                && BotStrategicPolicy.PickupAllowedForMode(ItemType.HealthBig, weaponLocked: true)
                && BotStrategicPolicy.PickupAllowedForMode(ItemType.Imperialist, weaponLocked: false),
            "locked-weapon modes do not divert bots toward unusable weapon pickups");

        Check(BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Samus, 6, .30f,
                true, true, false, false, false)
            && !BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Samus, 6, .90f,
                true, true, false, false, false),
            "Samus uses Morph Ball defensively rather than random high-health morphing");
        Check(BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Kanden, 4, .75f,
                true, true, false, false, false)
            && BotHunterCombatPolicy.ChooseAltAction(Hunter.Kanden, 4, true, false)
                == BotHunterAltAction.Bomb,
            "Kanden chooses short-range Stinglarva bomb pressure");
        Check(BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Trace, 3, .80f,
                true, true, false, false, false)
            && BotHunterCombatPolicy.ChooseAltAction(Hunter.Trace, 3, true, false)
                == BotHunterAltAction.Lunge,
            "Trace commits to close-range lunge while retaining long-range sniping");
        Check(BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Sylux, 5, .85f,
                true, true, false, false, false)
            && BotHunterCombatPolicy.ChooseAltAction(Hunter.Sylux, 5, true, false)
                == BotHunterAltAction.Bomb,
            "Sylux can set Lockjaw bombs when entering close range");
        Check(!BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Noxus, 3, .8f,
                true, true, false, false, false)
            && BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Noxus, 3, .8f,
                true, true, false, true, false)
            && BotHunterCombatPolicy.ChooseAltAction(Hunter.Noxus, 3, true, true)
                == BotHunterAltAction.Spin,
            "Noxus reserves Vhoscythe spin for genuine frozen-target setup");
        Check(BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Spire, 3, .8f,
                true, true, false, false, false)
            && BotHunterCombatPolicy.ChooseAltAction(Hunter.Spire, 3, true, false)
                == BotHunterAltAction.Slam,
            "Spire performs close-range Dialanche slam");
        Check(BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Weavel, 8, .55f,
                true, true, false, false, false)
            && BotHunterCombatPolicy.ChooseAltAction(Hunter.Weavel, 8, true, false)
                == BotHunterAltAction.HoldTurret,
            "Weavel uses defensive Halfturret without synthetic projectile attacks");
        Check(!BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Kanden, 4, .8f,
                false, true, false, false, false)
            && !BotHunterCombatPolicy.ShouldEnterAlt(Hunter.Weavel, 8, .5f,
                true, true, true, false, true)
            && BotHunterCombatPolicy.ChooseAltAction(Hunter.Trace, 3, false, false)
                == BotHunterAltAction.None,
            "hunter abilities require visible enemies and do not interrupt navigation");
        Check(BotHunterCombatPolicy.ShouldExitAlt(Hunter.Trace, 80, true, 3)
            && !BotHunterCombatPolicy.ShouldExitAlt(Hunter.Sylux, 15, false, 3),
            "hunter form tactics have finite commitment and cannot spam switches");
        Check(BotHunterCombatPolicy.WantsChargedAffinity(Hunter.Kanden,
                BeamType.VoltDriver, 18, false)
            && BotHunterCombatPolicy.WantsChargedAffinity(Hunter.Noxus,
                BeamType.Judicator, 10, false)
            && !BotHunterCombatPolicy.WantsChargedAffinity(Hunter.Noxus,
                BeamType.Judicator, 10, true),
            "charged affinity attacks exploit disruption/freeze without charging frozen targets");

        Check(BotHunterCombatPolicy.ChooseAltAction(
                Hunter.Noxus, 2.5f, targetVisible: true, targetFrozen: true)
                == BotHunterAltAction.Spin
            && BotHunterCombatPolicy.ChooseAltAction(
                Hunter.Noxus, 2.5f, targetVisible: false, targetFrozen: true)
                == BotHunterAltAction.None,
            "Noxus native spin initiation requires a real visible frozen opponent");

        Check(BotHunterCombatPolicy.ShouldExitAlt(Hunter.Weavel, 110,
                targetVisible: false, distance: float.PositiveInfinity)
            && BotHunterCombatPolicy.ShouldExitAlt(Hunter.Noxus, 41,
                targetVisible: false, distance: float.PositiveInfinity),
            "hunter alt combat eventually exits after lost sight or long commitment");

        Vector2 rollRequest = BotMotorMath.AltDesiredAxes(false,
            false, false, false, false, true, false, true, false);
        Vector2 strafeRequest = BotMotorMath.AltDesiredAxes(true,
            true, false, true, false, false, false, false, false);
        Check(rollRequest == new Vector2(1, 1)
            && strafeRequest == new Vector2(1, 1),
            "native roll and strafe alt forms map to the same smooth analog axes");
        Check(BotMotorMath.AltDesiredAxes(false,
                false, false, false, false, true, true, false, false) == Vector2.Zero,
            "opposing alt roll inputs cancel instead of generating abrupt movement");
        float altRate = 0;
        float altFirst = BotMotorMath.StepMoveAxis(altRate, 1, 0.4f, 0.52f);
        float altReverse = BotMotorMath.StepMoveAxis(altFirst, -1, 0.4f, 0.52f);
        Check(altFirst > 0 && altFirst < 1 && altReverse == 0,
            "alt motor brakes on direction reversal without snapping across zero");

        BotOpponentModel repetitiveStrafe = default;
        for (int i = 0; i < 80; i++)
        {
            float sideways = i % 6 < 3 ? .8f : -.8f;
            repetitiveStrafe.Observe(Vector3.Zero, new Vector3(10, 0, 0),
                new Vector3(0, 0, sideways), grounded: true);
        }
        Check(repetitiveStrafe.ReversalHabit > .25f
            && BotOpponentTactics.LeadScale(repetitiveStrafe) < 1,
            "bot adapts to observed strafe reversals by reducing prediction overshoot");
        Check(BotOpponentTactics.WeaponCounterBonus(BeamType.ShockCoil,
                repetitiveStrafe, 8, botLevel: 3)
            > BotOpponentTactics.WeaponCounterBonus(BeamType.Imperialist,
                repetitiveStrafe, 8, botLevel: 3),
            "high-skill bots prefer stable close-range tracking against erratic strafers");
        BotOpponentModel jumper = default;
        for (int i = 0; i < 80; i++)
            jumper.Observe(Vector3.Zero, new Vector3(12, 0, 0),
                new Vector3(0, 0, .1f), grounded: false);
        Check(jumper.AirborneHabit > .5f
            && BotOpponentTactics.WeaponCounterBonus(BeamType.VoltDriver,
                jumper, 14, botLevel: 3)
               > BotOpponentTactics.WeaponCounterBonus(BeamType.Magmaul,
                jumper, 14, botLevel: 3),
            "bots adjust weapon selection against consistently airborne opponents");
        BotOpponentModel rusher = default;
        for (int i = 0; i < 80; i++)
            rusher.Observe(Vector3.Zero, new Vector3(10, 0, 0),
                new Vector3(-.55f, 0, 0), grounded: true);
        Check(rusher.RushHabit > .5f
            && BotOpponentTactics.RangeAdjustment(BeamType.Imperialist, rusher) > 2
            && BotOpponentTactics.RangeAdjustment(BeamType.ShockCoil, rusher) == 0,
            "observed rushers create space for sniper bots without weakening close-range weapons");
        for (int i = 0; i < 320; i++)
            repetitiveStrafe.DecayWithoutSight();
        Check(repetitiveStrafe.ReversalHabit < .01f,
            "opponent habits decay behind walls instead of providing live tracking");
        Check(BotOpponentTactics.WeaponCounterBonus(BeamType.ShockCoil,
                rusher, 10, botLevel: 1) == 0,
            "easy/normal bots do not gain elite opponent adaptation");

        Console.WriteLine($"[bot-ai] {checks} checks passed");
        return 0;
    }

    private static BuiltFace Floor(float minX,float maxX,float minZ,float maxZ,float y,Terrain terrain)
    {
        var face=new BuiltFace(
        [
            new(minX,y,minZ),new(maxX,y,minZ),new(maxX,y,maxZ),new(minX,y,maxZ)
        ],
        [
            Vector2.Zero,Vector2.UnitX,Vector2.One,Vector2.UnitY
        ],Vector3.UnitY,0,1)
        {
            Terrain=terrain,
            Damaging=(int)terrain>=(int)Terrain.Lava
        };
        return face;
    }
}
