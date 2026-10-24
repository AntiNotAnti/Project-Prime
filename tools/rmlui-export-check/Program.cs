using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
int checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
var backend = new MemoryExports();
using var page = new TheatreExportController(backend);
Check(page.Snapshot().Options.Source == TheatreExportSource.Selection && page.Snapshot().Current, "captures existing marks without another transport");
Check(page.Change("fps") && page.Change("game-hud") && page.Change("encoder"), "native draft options can be edited");
var rates = new HashSet<int>();
int originalRate = page.Snapshot().Options.Fps;
for (int i = 0; i < 8; i++) { rates.Add(page.Snapshot().Options.Fps); Check(page.Change("fps"), "cycle supported native export rate"); }
Check(rates.Contains(240) && page.Snapshot().Options.Fps == originalRate,
    "native export rate cycle includes 240 FPS and returns to the reviewed draft");
var options = page.Snapshot().Options;
backend.FailWrite = true;
Check(!page.Enqueue() && page.Snapshot().Options == options && backend.Accepted.Count == 0, "backend failure keeps exact draft options");
backend.FailWrite = false;
Check(page.Enqueue() && backend.Accepted.Single().Options == options && backend.Accepted[0].Context.MarkIn == 60, "same reviewed options/range retry without re-resolving current source");
Check(page.Request(TheatreExportConfirmationKind.ClearPending), "pending clear captures exact queue");
backend.AddPending();
Check(!page.Confirm() && backend.Queue().Pending.Length == 2, "stale clear never removes newly queued work");
page.CancelConfirmation();
Check(page.Request(TheatreExportConfirmationKind.ClearPending) && page.Confirm() && backend.Queue().Pending.IsEmpty, "fresh clear removes exact reviewed set");
backend.Active = backend.NewJob(); backend.Revision++;
Check(page.Request(TheatreExportConfirmationKind.CancelActive), "active cancel freezes original receipt");
backend.CompletedOutput = "original-completed.mp4"; backend.Active = null; backend.Revision++;
Check(!page.Confirm() && backend.Cancellations == 0 && backend.CompletedOutput == "original-completed.mp4", "completion queued before cancel preserves original result");
page.CancelConfirmation(); backend.Active = backend.NewJob(); backend.Revision++;
Check(page.Request(TheatreExportConfirmationKind.CancelActive) && page.Confirm() && backend.Cancellations == 1, "fresh exact active receipt can request cancellation");
backend.Retry = backend.NewJob(); backend.Revision++;
Check(page.Request(TheatreExportConfirmationKind.Retry), "retry freezes original failed job");
backend.Retry = backend.NewJob(); backend.Revision++;
Check(!page.Confirm(), "stale retry cannot switch to new failure"); page.CancelConfirmation();
Check(page.Request(TheatreExportConfirmationKind.Retry) && page.Confirm(), "fresh retry preserves original manifest token");
int captures = backend.Captures; page.Snapshot(); page.Snapshot();
Check(backend.Captures == captures, "readonly progress polls no source capture or file witness");
backend.Current = false;
Check(!page.Snapshot().Current && !page.Enqueue(), "changed camera/replay clears new-enqueue authority");
backend.Current = true;
Check(page.Refresh() && page.Enqueue(), "explicit same-owner review restores enqueue authority");
backend.Context = backend.Context with { PlaybackOwner = new object() };
Check(!page.Refresh(), "same-path restart cannot rebind old export review");
Check(Task.Run(() => { try { page.Enqueue(); return false; } catch (InvalidOperationException) { return true; } }).Result, "wrong thread cannot mutate export presentation");
int pending = backend.Queue().Pending.Length;
page.Dispose();
Check(!page.Enqueue() && !page.Refresh() && backend.Queue().Pending.Length == pending, "page close retires UI without cancelling accepted queue work");
Console.WriteLine($"PASS {checks} native export controller identity/confirmation/retry/lifetime checks");
PresenterChecks.Run();

sealed class MemoryExports : ITheatreExportBackend
{
    public TheatreExportContext Context = new(1,new object(),"synthetic.ppdemo","synthetic.ppdemo","source","camera","annotations","reel",600,60,180,0,new object());
    public bool Current = true, FailWrite;
    public int Captures, Cancellations;
    public long Revision;
    public TheatreExportJob? Active, Retry;
    public string CompletedOutput = "";
    public List<(TheatreExportContext Context, TheatreExportOptions Options)> Accepted = new();
    public ImmutableArray<TheatreExportJob> Pending = ImmutableArray<TheatreExportJob>.Empty;
    public ImmutableArray<TheatreExportPreset> Presets => ImmutableArray.Create(new TheatreExportPreset("Balanced",1,60,false,false));
    public ImmutableArray<string> Encoders() => ImmutableArray.Create("libx264","h264_videotoolbox");
    public TheatreExportContext Capture() { Captures++; return Context; }
    public bool IsCurrent(TheatreExportContext context) => Current && ReferenceEquals(context.PlaybackOwner,Context.PlaybackOwner);
    public TheatreExportQueueState Queue() => new(Revision,Pending,Active,Retry,"Idle","Synthetic",0,ImmutableArray<string>.Empty,CompletedOutput);
    public TheatreExportJob NewJob() => new(Guid.NewGuid(),new object(),"synthetic.ppdemo","synthetic-output.mp4","Synthetic reviewed range");
    public void AddPending() { Pending = Pending.Add(NewJob()); Revision++; }
    public void Enqueue(TheatreExportContext c, TheatreExportOptions o) { if (FailWrite) throw new IOException("locked manifest"); Accepted.Add((c,o)); AddPending(); }
    public bool CancelExact(TheatreExportQueueState c) { if (c.Revision != Revision || c.Active != Active) return false; Cancellations++; Active = null; Revision++; return true; }
    public bool RetryExact(TheatreExportQueueState c) { if (c.Revision != Revision || c.Retry != Retry) return false; Pending = Pending.Add(c.Retry!); Revision++; return true; }
    public bool ClearExact(TheatreExportQueueState c) { if(c.Revision != Revision || !c.Pending.SequenceEqual(Pending)) return false; Pending = ImmutableArray<TheatreExportJob>.Empty; Revision++; return true; }
    public void OpenOutput(string output) { }
}
