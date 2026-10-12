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

        // Bot motor: smooth acceleration, reversal braking, deterministic aim limits.
        float move = 0;
        for (int i = 0; i < 8; i++)
            move = BotMotorMath.StepMoveAxis(move, 1, .25f, .40f);
        Check(move == 1, "motor reaches full native movement input");
        float reverseFirst = BotMotorMath.StepMoveAxis(move, -1, .25f, .40f);
        Check(reverseFirst > 0 && reverseFirst < 1,
            "direction reversal brakes instead of snapping");
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
        Check(reversingTurn < previousTurn && reversingTurn > -16,
            "opposite aim request decelerates before reversing");
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
