using System;
using MphRead.Mods.Network;
namespace MphRead.NetTest;
internal static class PredictedKillVisualTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
        try
        {
            var key=new ShotKey(1,2,0,3,4,5);var t=new PredictedKillTicket();
            Check(t.Start(key,1,6,7,8,100,250),"lethal ticket");
            Check(t.Pose(100)>0 && t.Pose(110)==1,"immediate bounded fall");
            Check(!t.Start(key with{ShotId=6},1,6,7,9,101,250),"double-hit cannot reset active fall");
            Check(!t.Verdict(key with{LifeId=8},1,6,7,8,true,111),"old shooter life");
            Check(!t.Verdict(key,1,6,8,8,true,111),"new victim life");
            Check(!t.Verdict(key,1,6,7,9,true,111),"wrong claim");
            Check(t.Verdict(key,1,6,7,8,true,111) && !t.Verdict(key,1,6,7,8,true,112),"idempotent confirmation");
            Check(t.Pose(111)==1 && t.Pose(135)==0,"missing death state never holds forever");
            Check(t.Start(key,1,6,7,8,200,250),"reuse after bounded finish");
            Check(t.Verdict(key,1,6,7,8,false,210) && t.Pose(213)>.0f && t.Pose(216)==0,"denial restores smoothly");
            t.Start(key,1,6,7,8,300,250);Check(t.Pose(339)==0,"loss expires within RTT plus margin and recovery");
            t.Start(key,1,6,7,8,uint.MaxValue-5,0);Check(t.Pose(30)==0,"expiry wraps safely");
            t.Clear();Check(t.State==PredictedKillState.None,"reset");
            Check(!t.Start(key with{ShotId=0},1,6,7,8,1,0),"anonymous signal cannot hide a player");
            HealthShotTests.Session(); NetPredictedKillPresentation.Enabled=true;
            var state=NetSession.RemoteStates[1];
            NetPredictedKillPresentation.Start(ShotKey.For(0,55),1,99);
            Check(NetPredictedKillPresentation.DrawPose(1)!=OpenTK.Mathematics.Matrix4.Identity,"local-only render basis active");
            Check(NetSession.RemoteStates[1].Health==state.Health && NetSession.RemoteStates[1].Position==state.Position
                && NetSession.RemoteStates[1].Flags==state.Flags,"render basis cannot mutate health, position or life flags");
            HealthShotTests.Snapshot(2,HealthShotTests.State(8));
            Check(NetPredictedKillPresentation.DrawPose(1)==OpenTK.Mathematics.Matrix4.Identity,"respawn restores immediately");
            NetPredictedKillPresentation.Enabled=false;NetSession.Stop();
            Check(!NetHitPrediction.DeathEnabled,"actual gameplay death stays disabled");
            Console.WriteLine($"PASS: {checks} predicted kill visual assertions");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{NetPredictedKillPresentation.Enabled=false;NetPredictedKillPresentation.Reset();NetSession.Stop();}
    }
}
