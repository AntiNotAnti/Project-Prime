using System.Collections.Concurrent;
using System.Diagnostics;
using MphRead.Mods.Launcher;

int assertions = 0;
var failures = new List<string>();
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    assertions++;
}
void Test(string name, Action work)
{
    try { work(); Console.WriteLine("PRESENCE PASS " + name); }
    catch (Exception error) { failures.Add(name + ": " + error.Message); Console.Error.WriteLine("PRESENCE FAIL " + failures[^1]); }
}
static string Id(int n) => $"PP-AAAA-BBBB-CCCC-DDDD-{n:X4}";
static NativeSocialPrivacy Privacy() => new("everyone", "friends", "friends", false);
static NativePresenceDirectory DirectoryAt(DateTimeOffset now, int self = 1) => new(Id(self), Privacy(),
    new[] { new NativePresencePerson(Id(2), "SYNTHETIC ONLINE HUNTER", "in_match", "SYNTHETIC ROOM", "", false, now),
        new NativePresencePerson(Id(3), "SYNTHETIC MENU HUNTER", "menu", "", "", false, now) });
static NativePresenceView Await(NativePresenceController controller, Func<NativePresenceView, bool> ready,
    string activity = "menu", string room = "")
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(3))
    {
        var state = controller.Tick(activity, room);
        if (ready(state)) return state;
        Thread.Sleep(5);
    }
    throw new TimeoutException("Presence worker fixture did not settle");
}
static NativePresenceView Load(NativePresenceController controller, Service service, NativePresenceDirectory data)
{
    controller.Refresh(); service.TakeRead().Completion.SetResult(data);
    return Await(controller, state => !state.Busy);
}
static Service.Heartbeat Establish(NativePresenceController controller, Service service, Clock clock)
{
    clock.Advance(TimeSpan.FromSeconds(15)); controller.Tick();
    var heartbeat = service.TakeHeartbeat();
    heartbeat.Completion.SetResult(DirectoryAt(clock.GetUtcNow()));
    Await(controller, state => !state.Busy);
    return heartbeat;
}

Test("owner thread and snapshot identity", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Exception? foreign = Task.Run(() => { try { controller.Refresh(); return null; } catch (Exception error) { return error; } }).GetAwaiter().GetResult();
    Check(foreign is InvalidOperationException && service.Reads.Count == 0, "foreign thread cannot start service work");
    var source = DirectoryAt(clock.GetUtcNow());
    var loaded = Load(controller, service, source);
    Check(loaded.Directory?.SelfPrimeId == Id(1), "verified self published");
    Check(service.LastCallThread != Environment.CurrentManagedThreadId, "service work runs on worker rather than owner thread");
    source.People[0] = source.People[0] with { DisplayName = "MUTATED SERVICE ARRAY" };
    Check(controller.Current().Directory!.People[0].DisplayName != "MUTATED SERVICE ARRAY", "service array copied before publication");
});

Test("initial snapshot advertises immediately then waits15seconds", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    controller.Tick();
    var first = service.TakeHeartbeat();
    Check(controller.Current().Busy && first.Self == Id(1) && first.Activity == "menu" && first.Session != Guid.Empty,
        "first verified snapshot advertises current state immediately");
    first.Completion.SetResult(DirectoryAt(clock.GetUtcNow()));
    Await(controller, state => !state.Busy);
    clock.Advance(TimeSpan.FromMilliseconds(14999)); controller.Tick();
    Check(!controller.Current().Busy && service.Heartbeats.Count == 0, "unchanged heartbeat remains deferred until15seconds");
    clock.Advance(TimeSpan.FromMilliseconds(1)); controller.Tick();
    var heartbeat = service.TakeHeartbeat();
    Check(heartbeat.Self == Id(1) && heartbeat.Activity == "menu" && heartbeat.Room == "" && heartbeat.Session != Guid.Empty,
        "subsequent heartbeat at15seconds binds exact self and process-local session");
    heartbeat.Completion.SetResult(DirectoryAt(clock.GetUtcNow()));
    Await(controller, state => !state.Busy);
});

Test("single-flight heartbeat cadence and early activity transition", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    var first = Establish(controller, service, clock);
    clock.Advance(TimeSpan.FromMilliseconds(14999)); controller.Tick();
    Check(!controller.Current().Busy && service.Heartbeats.Count == 0, "later heartbeat waits full15seconds");
    clock.Advance(TimeSpan.FromMilliseconds(1)); controller.Tick();
    var next = service.TakeHeartbeat();
    for (int i = 0; i < 10; i++) controller.Tick("in_match", "NEXT ROOM");
    Check(controller.Current().Busy && service.Heartbeats.Count == 0, "busy heartbeat cannot overlap another heartbeat");
    Check(next.Session == first.Session && next.Self == first.Self, "same owner session reused across heartbeats");
    next.Completion.SetResult(DirectoryAt(clock.GetUtcNow()));
    Await(controller, state => !state.Busy, "in_match", "NEXT ROOM");
    controller.Tick("in_match", "NEXT ROOM");
    var early = service.TakeHeartbeat();
    Check(early.Activity == "in_match" && early.Room == "NEXT ROOM" && early.Session == first.Session,
        "observed transition schedules early exact activity and room heartbeat");
    early.Completion.SetResult(DirectoryAt(clock.GetUtcNow()));
    Await(controller, state => !state.Busy, "in_match", "NEXT ROOM");
});

Test("refresh retires stale generation and drops private data", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    controller.Refresh(); var old = service.TakeRead();
    Check(controller.Current().Directory == null && controller.Current().Busy, "refresh drops previous private directory immediately");
    controller.Refresh(); var current = service.TakeRead();
    Check(old.Token.IsCancellationRequested, "superseded read cancelled");
    old.Completion.SetResult(DirectoryAt(clock.GetUtcNow()));
    current.Completion.SetResult(DirectoryAt(clock.GetUtcNow(), 9));
    Check(Await(controller, state => !state.Busy).Directory?.SelfPrimeId == Id(9), "stale generation cannot restore another account");
});

Test("directoryTTL45andindividualLastSeen expiry", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    var source = DirectoryAt(clock.GetUtcNow());
    source.People[0] = source.People[0] with { LastSeen = clock.GetUtcNow().AddSeconds(-44) };
    var loaded = Load(controller, service, source);
    Check(loaded.Directory?.People.Length == 2, "fresh and44second-old presence initially visible");
    var pending = Establish(controller, service, clock);
    // Establish refreshes individual rows. Inject an older row while keeping a
    // verified response, then let an unresolved heartbeat span local TTL expiry.
    controller.Refresh(); var read = service.TakeRead();
    var mixed = DirectoryAt(clock.GetUtcNow());
    mixed.People[0] = mixed.People[0] with { LastSeen = clock.GetUtcNow().AddSeconds(-44) };
    read.Completion.SetResult(mixed); Await(controller, state => !state.Busy);
    clock.Advance(TimeSpan.FromSeconds(1)); controller.Tick();
    Check(controller.Current().Directory?.People.Length == 1, "individual row disappears exactly45seconds after LastSeen");
    var held = service.TakeHeartbeat();
    clock.Advance(TimeSpan.FromSeconds(43)); controller.Tick();
    Check(controller.Current().Directory != null, "verified directory retained before45second localTTL");
    clock.Advance(TimeSpan.FromSeconds(1)); controller.Tick();
    Check(controller.Current().Directory == null && controller.Current().Status == "PRESENCE EXPIRED",
        "local directory expires despite unresolved heartbeat and fabricates no offline list");
    held.Completion.SetResult(DirectoryAt(clock.GetUtcNow()));
    Await(controller, state => !state.Busy);
});

Test("privacy captures immutable expected/next and exact self", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    var expected = controller.Current().Directory!.Privacy;
    var next = new NativeSocialPrivacy("hidden", "private", "nobody", true);
    Check(!controller.SetPrivacy(expected) && !controller.SetPrivacy(next with { Presence = "invalid" }), "unchanged and invalid privacy rejected before service");
    Check(controller.SetPrivacy(next), "valid privacy change starts");
    var write = service.TakePrivacy();
    Check(write.Self == Id(1) && write.Expected == expected && write.Next == next && write.Token.CanBeCanceled,
        "privacy write captures exact immutable expected/next/self witness");
    Check(controller.Current().Directory == null && !controller.SetPrivacy(expected), "in-flight privacy hides private data and remains single-flight");
    write.Completion.SetResult(DirectoryAt(clock.GetUtcNow()) with { Privacy = next });
    Check(Await(controller, state => !state.Busy).Directory?.Privacy == next, "only exact verified next settings publish");
});

foreach (bool wrongAccount in new[] { true, false }) Test("privacy rejects wrong " + (wrongAccount ? "account" : "settings"), () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    var next = new NativeSocialPrivacy("hidden", "private", "nobody", true);
    controller.SetPrivacy(next); var write = service.TakePrivacy();
    write.Completion.SetResult(DirectoryAt(clock.GetUtcNow(), wrongAccount ? 9 : 1) with { Privacy = wrongAccount ? next : Privacy() });
    var failed = Await(controller, state => !state.Busy);
    Check(failed.Directory == null && !failed.Status.Contains(Id(9)), "mismatched privacy response never publishes directory");
});

Test("heartbeat rejects changed account", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    clock.Advance(TimeSpan.FromSeconds(15)); controller.Tick(); var sent = service.TakeHeartbeat();
    sent.Completion.SetResult(DirectoryAt(clock.GetUtcNow(), 9));
    Check(Await(controller, state => !state.Busy).Directory == null, "heartbeat readback cannot replace captured account");
});
Test("superseded privacy completion cannot restore old account", () =>
{
    var service = new Service(); var clock = new Clock();
    using var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    var next = new NativeSocialPrivacy("hidden", "private", "nobody", true);
    controller.SetPrivacy(next); var old = service.TakePrivacy();
    controller.Refresh(); var current = service.TakeRead();
    Check(old.Token.IsCancellationRequested && controller.Current().Directory == null, "refresh retires pending privacy lifetime and data");
    old.Completion.SetResult(DirectoryAt(clock.GetUtcNow()) with { Privacy = next });
    current.Completion.SetResult(DirectoryAt(clock.GetUtcNow(), 9));
    Check(Await(controller, state => !state.Busy).Directory?.SelfPrimeId == Id(9), "late accepted privacy result cannot replace latest read");
});

var stamp = new Clock().GetUtcNow(); var valid = DirectoryAt(stamp);
var malformed = new (string Label, NativePresenceDirectory? Data)[]
{
    ("null",null), ("bad self",valid with { SelfPrimeId="private account UUID" }),
    ("null privacy",valid with { Privacy=null! }), ("bad privacy",valid with { Privacy=Privacy() with { Activity="invalid" } }),
    ("null people",valid with { People=null! }), ("null row",valid with { People=new NativePresencePerson[] { null! } }),
    ("self row",valid with { People=new[] { valid.People[0] with { PrimeId=Id(1) } } }),
    ("duplicate identity",valid with { People=new[] { valid.People[0],valid.People[0] with { PrimeId=Id(2).ToLowerInvariant() } } }),
    ("bad person ID",valid with { People=new[] { valid.People[0] with { PrimeId="bad" } } }),
    ("empty name",valid with { People=new[] { valid.People[0] with { DisplayName=" " } } }),
    ("oversized name",valid with { People=new[] { valid.People[0] with { DisplayName=new string('x',129) } } }),
    ("fabricated offline",valid with { People=new[] { valid.People[0] with { Activity="offline" } } }),
    ("bad room",valid with { People=new[] { valid.People[0] with { RoomKey=new string('x',129) } } }),
    ("null room",valid with { People=new[] { valid.People[0] with { RoomKey=null! } } }),
    ("bad lobby",valid with { People=new[] { valid.People[0] with { LobbyId="invalid" } } }),
    ("joinable match",valid with { People=new[] { valid.People[0] with { Joinable=true } } }),
    ("joinable no locator",valid with { People=new[] { valid.People[0] with { Activity="lobby",Joinable=true } } }),
    ("default LastSeen",valid with { People=new[] { valid.People[0] with { LastSeen=default } } }),
    ("overflow LastSeen",valid with { People=new[] { valid.People[0] with { LastSeen=DateTimeOffset.MaxValue } } }),
    ("oversized directory",valid with { People=Enumerable.Repeat(valid.People[0],1025).ToArray() })
};
Test("malformed service responses and exception redaction", () =>
{
    Check(NativePresenceController.ValidDirectory(valid), "realistic directory validates");
    foreach (var bad in malformed)
    {
        Check(!NativePresenceController.ValidDirectory(bad.Data), "invalid directory rejects " + bad.Label);
        if (bad.Data == null) continue;
        var service = new Service(); var clock = new Clock();
        using var controller = new NativePresenceController(service, clock);
        var response = Load(controller, service, bad.Data);
        Check(response.Directory == null && !response.Busy, "malformed service never publishes " + bad.Label);
    }
    var failing = new Service(); using var failed = new NativePresenceController(failing, new Clock());
    failed.Refresh(); failing.TakeRead().Completion.SetException(new InvalidOperationException("secret bearer data"));
    var result = Await(failed, state => !state.Busy);
    Check(result.Directory == null && !result.Status.Contains("secret"), "transport errors expose no raw bearer detail");
});

foreach (string phase in new[] { "idle", "refresh", "privacy", "heartbeat" }) Test("Dispose leaves captured session during " + phase, () =>
{
    var service = new Service(); var clock = new Clock();
    var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    var session = Establish(controller, service, clock);
    CancellationToken active = default;
    if (phase == "refresh") { controller.Refresh(); active = service.TakeRead().Token; }
    if (phase == "privacy") { controller.SetPrivacy(new("hidden","private","nobody",true)); active = service.TakePrivacy().Token; }
    if (phase == "heartbeat") { clock.Advance(TimeSpan.FromSeconds(15));controller.Tick(); active = service.TakeHeartbeat().Token; }
    controller.Dispose(); controller.Dispose();
    if (active.CanBeCanceled) Check(active.IsCancellationRequested, "Dispose cancels in-flight work");
    var leave = service.TakeLeave();
    Check(leave.Self == Id(1) && leave.Session == session.Session && leave.Token.CanBeCanceled,
        "Dispose only leaves previously captured verified account/session with bounded cancellation");
    Check(service.Leaves.Count == 0, "Dispose issues leave exactly once");
});
Test("never verified Dispose sends no leave", () =>
{
    var service = new Service(); var controller = new NativePresenceController(service, new Clock());
    controller.Refresh(); var pending = service.TakeRead(); controller.Dispose();
    Check(pending.Token.IsCancellationRequested && service.Leaves.Count == 0, "unverified account cannot issue leave");
    pending.Completion.SetResult(DirectoryAt(DateTimeOffset.UtcNow));
});
Test("Dispose cannot substitute newly read account for advertised owner", () =>
{
    var service = new Service(); var clock = new Clock();
    var controller = new NativePresenceController(service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow()));
    var advertised = Establish(controller, service, clock);
    Load(controller, service, DirectoryAt(clock.GetUtcNow(), 9));
    controller.Dispose();
    var leave = service.TakeLeave();
    Check(leave.Self == Id(1) && leave.Session == advertised.Session, "cleanup retains exact previously advertised account rather than replacement directory owner");
});

Test("account lease retirement clears cached presence and late completion", () =>
{
    var service = new Service(); var clock = new Clock();
    object account = new();
    service.IsCurrentDirectory = directory => ReferenceEquals(directory.AccountWitness, account);
    using var controller = new NativePresenceController(service, clock);
    var original = DirectoryAt(clock.GetUtcNow()) with { AccountWitness = account };
    Load(controller, service, original);
    controller.Tick();
    var oldHeartbeat = service.TakeHeartbeat();
    account = new();
    var retired = controller.Current();
    Check(retired.Directory == null && !retired.Busy && oldHeartbeat.Token.IsCancellationRequested,
        "account replacement synchronously retires cached directory and in-flight request");
    Check(!controller.SetPrivacy(Privacy() with { DoNotDisturb = true }) && service.PrivacyWrites.Count == 0,
        "retired account cannot submit cached privacy mutation");
    oldHeartbeat.Completion.SetResult(original);
    controller.Tick();
    var freshRead = service.TakeRead();
    freshRead.Completion.SetResult(DirectoryAt(clock.GetUtcNow(), 9) with { AccountWitness = account });
    var fresh = Await(controller, state => !state.Busy);
    Check(fresh.Directory?.SelfPrimeId == Id(9), "late old-account heartbeat cannot replace fresh directory");
});
Test("completed read with retired account lease never publishes", () =>
{
    var service = new Service(); var clock = new Clock();
    object account = new(); object retired = new();
    service.IsCurrentDirectory = directory => ReferenceEquals(directory.AccountWitness, account);
    using var controller = new NativePresenceController(service, clock);
    controller.Refresh();
    service.TakeRead().Completion.SetResult(DirectoryAt(clock.GetUtcNow()) with { AccountWitness = retired });
    var rejected = Await(controller, state => !state.Busy);
    Check(rejected.Directory == null && !controller.SetPrivacy(Privacy() with { DoNotDisturb = true }),
        "a completed old-account read cannot expose people or authorize privacy");
});

Console.WriteLine($"NATIVE PRESENCE {assertions} assertions; {failures.Count} failing scenarios. Synthetic service and fake clock only.");
if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));

sealed class Clock : TimeProvider
{
    private DateTimeOffset _now = new(2026,10,17,18,0,0,TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan elapsed) => _now += elapsed;
}
sealed class Service : INativePresenceService
{
    public Func<NativePresenceDirectory, bool> IsCurrentDirectory = _ => true;
    public bool IsCurrent(NativePresenceDirectory directory) => IsCurrentDirectory(directory);
    public sealed record Read(CancellationToken Token, TaskCompletionSource<NativePresenceDirectory> Completion);
    public sealed record Heartbeat(string Self,Guid Session,string Activity,string Room,CancellationToken Token,TaskCompletionSource<NativePresenceDirectory> Completion);
    public sealed record Write(string Self,NativeSocialPrivacy Expected,NativeSocialPrivacy Next,CancellationToken Token,TaskCompletionSource<NativePresenceDirectory> Completion);
    public sealed record Leave(string Self,Guid Session,CancellationToken Token);
    public readonly BlockingCollection<Read> Reads = new();
    public readonly BlockingCollection<Heartbeat> Heartbeats = new();
    public readonly BlockingCollection<Write> PrivacyWrites = new();
    public readonly BlockingCollection<Leave> Leaves = new();
    public int LastCallThread;
    private static TaskCompletionSource<NativePresenceDirectory> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<NativePresenceDirectory> ReadAsync(CancellationToken token)
    { LastCallThread=Environment.CurrentManagedThreadId;var response=Completion();Reads.Add(new(token,response));return response.Task; }
    public Task<NativePresenceDirectory> HeartbeatAsync(string self,Guid session,string activity,string room,CancellationToken token)
    { var response=Completion();Heartbeats.Add(new(self,session,activity,room,token,response));return response.Task; }
    public Task<NativePresenceDirectory> PrivacyAsync(string self,NativeSocialPrivacy expected,NativeSocialPrivacy next,CancellationToken token)
    { var response=Completion();PrivacyWrites.Add(new(self,expected,next,token,response));return response.Task; }
    public Task LeaveAsync(string self,Guid session,CancellationToken token)
    { Leaves.Add(new(self,session,token));return Task.CompletedTask; }
    private static T Take<T>(BlockingCollection<T> queue,string label) => queue.TryTake(out var value,TimeSpan.FromSeconds(3))?value:throw new TimeoutException("No fixture "+label);
    public Read TakeRead()=>Take(Reads,"read");
    public Heartbeat TakeHeartbeat()=>Take(Heartbeats,"heartbeat");
    public Write TakePrivacy()=>Take(PrivacyWrites,"privacy write");
    public Leave TakeLeave()=>Take(Leaves,"leave");
}
