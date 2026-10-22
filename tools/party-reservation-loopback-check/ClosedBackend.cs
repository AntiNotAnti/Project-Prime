using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using MphRead.Mods.Network;

// Explicit synthetic server credential and backend boundary. No DNS/socket is
// used by this handler; real Edge HMAC/SQL acceptance is outside its claims.
internal sealed class ClosedBackend(int count):HttpMessageHandler
{
    internal const string Reporter="fixture-only-not-a-real-reporter-key-000000000000";
    internal static string Ticket(uint client)=>"pp1.closed-loopback-fixture-client-"+client;
    internal PartyReservationRequestPacket Identity;
    internal readonly Guid Leader=Guid.NewGuid();internal readonly Guid[] Members=Enumerable.Range(0,count-1).Select(_=>Guid.NewGuid()).ToArray();
    internal readonly ConcurrentQueue<string> Errors=new();internal readonly ConcurrentQueue<string> Actions=new();
    internal string? Reservation;internal bool Cancelled,HoldClaims;
    private DateTimeOffset expires;private bool activationReleased;
    private readonly ConcurrentQueue<TaskCompletionSource<HttpResponseMessage>> activation=new(),claims=new();
    private readonly Dictionary<uint,(int Slot,string Connection,string Nonce,DateTimeOffset Until)> committed=new();
    private readonly object gate=new();
    internal int Count(string action)=>Actions.Count(x=>x==action);
    private static HttpResponseMessage Reply(object body)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(body))};
    private static bool Equal(JsonElement body,string key,string value)=>body.GetProperty(key).GetString()==value;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if(request.RequestUri?.AbsoluteUri!="https://closed.invalid/reservation")throw new InvalidOperationException("external HTTP forbidden");
        if(request.Headers.Authorization?.Parameter!=Reporter)throw new InvalidOperationException("existing reporter credential owner was not used");
        using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var b=doc.RootElement;string action=b.GetProperty("action").GetString()!;
        if(!b.GetProperty("native_v1").GetBoolean())return Reply(new{ok=false});
        if(action=="capability"){Actions.Enqueue(action);return Reply(new{ok=true,native_v1=true,version=1});}
        uint client=b.GetProperty("client_id").GetUInt32();
        bool leader=client==1001;int member=unchecked((int)client-2001);
        bool valid=Equal(b,"request_id",Identity.RequestId)&&Equal(b,"party_id",Identity.PartyId)
            &&b.GetProperty("party_revision").GetUInt64()==Identity.PartyRevision&&Equal(b,"travel_id",Identity.TravelId)
            &&b.GetProperty("travel_revision").GetUInt32()==Identity.TravelRevision&&b.GetProperty("match_id").GetUInt16()==Identity.MatchId
            &&Equal(b,"authority_epoch",Identity.AuthorityEpoch.ToString(CultureInfo.InvariantCulture))&&b.GetProperty("operation_generation").GetInt32()==1
            &&Equal(b,"career_ticket",Ticket(client))&&(leader||member>=0&&member<Members.Length)
            &&Equal(b,"purpose",leader?"leader":"member");
        var deadline=b.GetProperty("operation_deadline").GetDateTimeOffset();
        valid&=deadline>DateTimeOffset.UtcNow&&deadline<=DateTimeOffset.UtcNow.AddSeconds(3.6);
        if(!valid){Errors.Enqueue("invalid "+action+" exact authority proof");return Reply(new{ok=false});}
        if(action=="validate")
        {
            Actions.Enqueue(action);
            if(Cancelled)return Reply(new{ok=false,status="cancelled"});
            return Reply(new{ok=true,status="pending",player_id=leader?Leader:Members[member],leader_id=Leader,members=Members,roster=Members.Prepend(Leader),expires_at=DateTimeOffset.UtcNow.AddSeconds(50)});
        }
        if(action=="activate")
        {
            if(!leader)return Reply(new{ok=false});Reservation=b.GetProperty("reservation_id").GetString();
            expires=b.GetProperty("expires_at").GetDateTimeOffset();
            var assignments=b.GetProperty("assignments").EnumerateArray().ToArray();
            bool assignmentValid=assignments.Length==Members.Length&&assignments.Select(a=>a.GetProperty("slot").GetInt32()).Distinct().Count()==Members.Length
                &&assignments.Select(a=>a.GetProperty("player_id").GetGuid()).Order().SequenceEqual(Members.Order())
                &&assignments.All(a=>a.GetProperty("slot").GetInt32() is >=1 and <=7)&&expires<=DateTimeOffset.UtcNow.AddSeconds(15.1);
            if(!assignmentValid){Errors.Enqueue("invalid actual assignments");return Reply(new{ok=false});}
            Actions.Enqueue(action); // Tests observe commitment, not request entry.
            lock(gate){if(activationReleased)return Activation();}
            var pending=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);activation.Enqueue(pending);
            lock(gate){if(activationReleased)ReleaseActivation();}
            return await pending.Task; // commitment deliberately survives a locally cancelled reply
        }
        if(!Equal(b,"reservation_id",Reservation??"")){Errors.Enqueue("wrong committed reservation");return Reply(new{ok=false});}
        if(action=="cancel"){if(b.GetProperty("cancel_scope").GetString()=="group")Cancelled=true;Actions.Enqueue(action);return Reply(new{ok=true});}
        if(action=="claim_admission")
        {
            if(leader||Cancelled||DateTimeOffset.UtcNow>=expires)return Reply(new{ok=false});
            int slot=b.GetProperty("slot").GetInt32();string connection=b.GetProperty("connection_id").GetString()!,nonce=b.GetProperty("claim_nonce").GetString()!;
            if(slot is <1 or >7||connection=="0"||nonce=="0"||deadline>DateTimeOffset.UtcNow.AddSeconds(2.1)){Errors.Enqueue("invalid short claim");return Reply(new{ok=false});}
            var until=DateTimeOffset.UtcNow.AddSeconds(1.8);if(deadline<until)until=deadline;
            lock(gate)committed[client]=(slot,connection,nonce,until);
            Actions.Enqueue(action); // The original short claim is now committed.
            var response=Reply(new{ok=true,status="admitting",claim_expires_at=until});
            if(!HoldClaims)return response;
            var pending=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);claims.Enqueue(pending);
            return await pending.Task.ContinueWith(_=>response,TaskScheduler.Default);
        }
        if(action=="admitted")
        {
            lock(gate)
            {
                if(!committed.TryGetValue(client,out var claim)||claim.Until<=DateTimeOffset.UtcNow
                    ||b.GetProperty("slot").GetInt32()!=claim.Slot||!Equal(b,"connection_id",claim.Connection)
                    ||!Equal(b,"claim_nonce",claim.Nonce)||!b.GetProperty("peer_created").GetBoolean())
                {Errors.Enqueue("actual-peer acknowledgement lacks current claim proof");return Reply(new{ok=false});}
            }
            Actions.Enqueue(action);
            return Reply(new{ok=true});
        }
        Errors.Enqueue("unknown "+action);return Reply(new{ok=false});
    }
    private HttpResponseMessage Activation()=>Reply(new{ok=true,status="reserved",expires_at=expires});
    internal void ReleaseActivation(){lock(gate)activationReleased=true;while(activation.TryDequeue(out var t))t.TrySetResult(Activation());}
    internal void ReleaseClaims(){while(claims.TryDequeue(out var t))t.TrySetResult(Reply(new{ok=true}));}
    protected override void Dispose(bool disposing){ReleaseActivation();ReleaseClaims();base.Dispose(disposing);}
}
