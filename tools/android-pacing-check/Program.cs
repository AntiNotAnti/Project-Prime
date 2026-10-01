using MphRead.Droid;

int failures = 0;
void Check(bool result, string name)
{
    Console.WriteLine($"ANDROIDPACING {(result ? "PASS" : "FAIL")} {name}");
    if (!result) failures++;
}

foreach (double hz in new[] { 59.94, 60, 90, 120, 144 })
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    double now = 0, addedWait = 0, elapsed = 0;
    for (int i = 0; i < 600; i++)
    {
        double deadline = pacer.Deadline(now, 0);
        double wait = Math.Max(0, deadline - now);
        now += wait;
        addedWait += wait;
        elapsed += pacer.BeginFrame(now);
        now += 1 / hz; // Work plus blocking presentation already occupies one refresh.
    }
    Check(addedWait < 1e-9 && Math.Abs(elapsed - 599 / hz) < 1e-8,
        $"display {hz}Hz adds no second wait and preserves elapsed time");
}
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    pacer.Deadline(0, 0);
    pacer.BeginFrame(0);
    // A presentation queue can free a buffer between display boundaries.
    // The former 60Hz software timer added ~8.67ms here on top of VSync.
    Check(pacer.Deadline(.008, 0) <= .008,
        "early presentation return does not wait for an unrelated 60Hz software phase");
}
foreach (int cap in new[] { 60, 90, 120, 144 })
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    double now = 0, addedWait = 0, elapsed = 0;
    for (int i = 0; i < 600; i++)
    {
        double deadline = pacer.Deadline(now, cap, displayPaced: true);
        double wait = Math.Max(0, deadline - now);
        now += wait;
        addedWait += wait;
        elapsed += pacer.BeginFrame(now);
        now += 1.0 / cap; // compositor/native refresh owns the cadence
    }
    Check(addedWait < 1e-9 && Math.Abs(elapsed - 599.0 / cap) < 1e-8,
        $"native explicit {cap} cap adds no second software wait");
}
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    pacer.Deadline(0, 120, displayPaced: true);
    pacer.BeginFrame(0);
    Check(pacer.Deadline(.008, 120, displayPaced: true) <= .008,
        "native 120Hz cap follows compositor timing rather than an 8.33ms sleep phase");
}

foreach (int cap in new[] { 30, 60, 90, 120, 144, 500 })
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    double now = 0, elapsed = 0;
    for (int i = 0; i < 600; i++)
    {
        now = Math.Max(now, pacer.Deadline(now, cap));
        elapsed += pacer.BeginFrame(now);
        now += 0.001;
    }
    Check(Math.Abs(elapsed - 599.0 / cap) < 1e-8, $"explicit {cap} cap retains its deadline cadence");
}
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    pacer.Deadline(0, 0);
    pacer.BeginFrame(0);
    Check(Math.Abs(pacer.Deadline(0.0001, 0) - .002) < 1e-9,
        "nonblocking presentation retains 500Hz safety ceiling");
    pacer.Deadline(0.01, 30);
    pacer.BeginFrame(0.01);
    Check(pacer.Deadline(0.015, 120) == 0.015, "cap changes discard the old render deadline");
    pacer.BeginFrame(0.015);
    pacer.Deadline(1, 120);
    double elapsed = pacer.BeginFrame(1);
    Check(Math.Abs(elapsed - .985) < 1e-9 && pacer.Deadline(1, 120) > 1,
        "stall preserves simulation elapsed but creates no render catch-up burst");
    pacer.Reset(100);
    pacer.Deadline(100, 60);
    Check(pacer.BeginFrame(100) == 0, "resume excludes background time");
}
Check(AndroidFramePacer.BudgetRate(120, 60) == 60
    && AndroidFramePacer.BudgetRate(0, 90) == 90
    && AndroidFramePacer.BudgetRate(30, 120) == 30,
    "governor uses active display rate and lower explicit caps");
double budget = 1000.0 / 60;
Check(!AndroidFramePacer.Behind(budget, 4, budget), "vsync-limited frame is not overloaded");
Check(!AndroidFramePacer.HasHeadroom(budget, 4, budget - 4, budget),
    "present back-pressure is not assumed to be GPU headroom");
Check(AndroidFramePacer.Behind(33.4, 4, budget)
    && AndroidFramePacer.Behind(budget, 17, budget), "late frames and expensive CPU work still trigger downscaling");
Check(AndroidFramePacer.HasHeadroom(budget, 4, 1, budget), "genuine headroom allows quality recovery");
return failures == 0 ? 0 : 1;
