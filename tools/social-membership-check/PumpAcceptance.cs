using System.Collections.Concurrent;
using MphRead.Mods.Network;

internal static class PumpAcceptance
{
    private sealed record Sent(SocialLobbyMembershipReport Report, TaskCompletionSource<SocialLobbyMembershipReceipt> Reply);
    internal static void Run(Action<bool,string> check)
    {
        var requests = new BlockingCollection<Sent>();
        using var pump = new SocialLobbyMembershipPump((report, token) =>
        {
            var reply = new TaskCompletionSource<SocialLobbyMembershipReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add(new(report,reply)); return reply.Task;
        });
        Sent Take() => requests.TryTake(out var sent, 2000) ? sent : throw new TimeoutException("Missing reporter request");
        void Finish(Sent sent, bool verified = true)
        { sent.Reply.SetResult(new(verified, Guid.NewGuid())); Thread.Sleep(25); }
        var identity = new SocialLobbyMembershipIdentity(new object(), 100, 2, "pp1."+new string('a',30), true);
        pump.Update(new[]{identity},0); var first=Take();
        check(first.Report.ReporterSequence==1 && first.Report.LobbyEligible, "initial owner heartbeat is positive and eligible");
        var changed=identity with { Witness=new object(), Eligible=false };
        pump.Update(new[]{changed},1);
        check(requests.Count==0,"phase change coalesces while old worker is pending");
        Finish(first); pump.Update(new[]{changed},2); var second=Take();
        check(!second.Report.LobbyEligible && second.Report.ReporterSequence>first.Report.ReporterSequence,"late old success cannot postpone new ineligible generation");
        Finish(second);
        double now=3;
        for(int rotation=0;rotation<8;++rotation)
        {
            changed=changed with { Witness=new object(), Eligible=rotation%2==0 };
            pump.Update(new[]{changed},now++); var sent=Take();
            check(sent.Report.ReporterSequence==rotation+3 && sent.Report.LobbyEligible==changed.Eligible,"rotation/rejoin witness sends current generation "+rotation);
            Finish(sent);
        }
        var replacement=changed with { Witness=new object(), Ticket="pp1."+new string('b',30), Eligible=true };
        pump.Update(new[]{replacement},now++); var leave=Take();
        check(leave.Report.Leave && leave.Report.CareerTicket==changed.Ticket && requests.Count==0,"ticket refresh retires old ticket before publishing replacement");
        Finish(leave); pump.Update(new[]{replacement},now++); var fresh=Take();
        check(!fresh.Report.Leave && fresh.Report.CareerTicket==replacement.Ticket && fresh.Report.ReporterSequence>leave.Report.ReporterSequence,"replacement heartbeat follows old leave with newer floor");
        Finish(fresh); pump.Update(Array.Empty<SocialLobbyMembershipIdentity>(),now++); var departing=Take();
        check(departing.Report.Leave,"absent peer emits verified-ticket leave");
        Finish(departing,false); pump.Update(Array.Empty<SocialLobbyMembershipIdentity>(),now);
        check(requests.Count==0,"failed leave obeys bounded retry delay");
        pump.Update(Array.Empty<SocialLobbyMembershipIdentity>(),now+5); var retry=Take();
        check(retry.Report.Leave && retry.Report.ReporterSequence>departing.Report.ReporterSequence,"failed leave retries with greater sequence");
        Finish(retry); pump.Update(Array.Empty<SocialLobbyMembershipIdentity>(),now+6);
        check(pump.Sequence==retry.Report.ReporterSequence,"sequence floor retained after tombstone leaves active scheduler");
        using var restarted = new SocialLobbyMembershipPump((report,token)=>{ requests.Add(new(report,new())); return Task.FromResult(default(SocialLobbyMembershipReceipt)); }, pump.Sequence);
        restarted.Update(new[]{identity},0); var resumed=Take();
        check(resumed.Report.ReporterSequence>retry.Report.ReporterSequence,"same owner incarnation restart resumes sequence floor");

        var blocked=new BlockingCollection<SocialLobbyMembershipReport>();
        using var bounded=new SocialLobbyMembershipPump((report,token)=>{blocked.Add(report);return new TaskCompletionSource<SocialLobbyMembershipReceipt>().Task;});
        var crowded=Enumerable.Range(1,100).Select(i=>new SocialLobbyMembershipIdentity(new object(),200,(uint)i,"pp1."+new string('x',30),true)).ToArray();
        bounded.Update(crowded,0); Thread.Sleep(50);
        check(blocked.Count==8,"reporter admits at most eight concurrent HTTP workers under peer churn");

        NetSession.EvidenceTransport=new();NetSession.IsClient=true;var ownership=new NetClientJoinOwnership();ownership.Capture();
        check(ownership.IsCurrent,"candidate captures exact existing transport");
        NetSession.EvidenceTransport=new();int stops=NetSession.Stops;
        check(!ownership.TryStopCurrent() && NetSession.Stops==stops,"late cleanup cannot stop replacement transport");
        var failed=new NetClientJoinOwnership();NetSession.IsClient=false;failed.Capture();
        check(!failed.IsCurrent && failed.TryStopCurrent() && NetSession.Stops==stops+1,"failed/offline candidate still disposes only its own transport");
    }
}
