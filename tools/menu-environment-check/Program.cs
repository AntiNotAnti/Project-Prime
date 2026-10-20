using MphRead;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++; Console.WriteLine("MENU ENVIRONMENT PASS " + name);
}
void Near(double actual, double expected, double tolerance, string name) => Check(Math.Abs(actual - expected) <= tolerance, $"{name}: {actual:G9} vs {expected:G9}");
void Fixture(byte occupied = 255, byte ready = 85)
{
    LauncherLobbyVisuals.Reset(); LauncherLobbyVisuals.Active = true;
    LauncherLobbyVisuals.SetSession(7, 101); LauncherLobbyVisuals.OccupiedMask = occupied; LauncherLobbyVisuals.ReadyMask = ready;
    for (int i = 0; i < 8; i++) { LauncherLobbyVisuals.SetIdentity(i, 100 + i); LauncherLobbyVisuals.SetHunter(i, (Hunter)(i % 7)); LauncherLobbyVisuals.SetTeam(i, 0); }
}
MenuMotionState Run(int hz, double seconds = 2)
{
    Fixture(0, 0); LauncherLobbyVisuals.Active = false; var motion = new MenuMotionState();
    motion.Advance(0, true, false, LauncherActivityAmbience.QuickPlay, Hunter.Samus);
    LauncherLobbyVisuals.Active = true; LauncherLobbyVisuals.OccupiedMask = 255; LauncherLobbyVisuals.ReadyMask = 85;
    for (int i = 1; i <= (int)(hz * seconds); i++)
        motion.Advance(i / (double)hz, true, false, LauncherActivityAmbience.Lobby, Hunter.Sylux);
    return motion;
}

Check(LauncherLobbyFormation.Validate(), "all eight platform/hunter bounds valid");
int priorParticles = -1, priorDetail = -1;
foreach (CosmeticEffectQuality quality in Enum.GetValues<CosmeticEffectQuality>())
{
    var budget = MenuEnvironmentBudget.For(quality, false);
    var reduced = MenuEnvironmentBudget.For(quality, true);
    Check(budget.ParticleCount >= priorParticles && budget.Detail >= priorDetail, quality + " scales work monotonically");
    Check(!reduced.Animate, quality + " respects reduced motion");
    var android = MenuEnvironmentBudget.For(quality, false, android: true);
    Check(android.Detail <= MenuEnvironmentBudget.For(CosmeticEffectQuality.Low, false).Detail, quality + " Android detail bounded by Low");
    Check(android.ParticleCount <= MenuEnvironmentBudget.For(CosmeticEffectQuality.Low, false).ParticleCount, quality + " Android particles bounded by Low");
    priorDetail = budget.Detail; priorParticles = budget.ParticleCount;
}
var off = MenuEnvironmentBudget.For(CosmeticEffectQuality.Off, false);
Check(off.ParticleCount == 0 && off.TelemetryRows == 0 && off.ServiceLamps == 0 && off.Atmosphere == 0 && !off.Animate && !off.HunterMotifs, "Off eliminates optional atmospheric work");
var at120 = Run(120); var at144 = Run(144); var at240 = Run(240);
foreach (var pair in new[] { (120, at120), (144, at144), (240, at240) })
{
    Near(pair.Item2.Time, 2, 1e-5, pair.Item1 + "Hz timeline");
    Near(pair.Item2.HunterWeights[(int)Hunter.Sylux], at240.HunterWeights[(int)Hunter.Sylux], 2e-5, pair.Item1 + "Hz theme convergence");
    Near(pair.Item2.Occupancy[0], at240.Occupancy[0], 2e-5, pair.Item1 + "Hz occupancy convergence");
    Near(pair.Item2.SceneBlend, at240.SceneBlend, 2e-5, pair.Item1 + "Hz scene convergence");
}

var transient240 = Run(240, .25);
foreach (int hz in new[] { 120, 144 })
{
    var transient = Run(hz, .25);
    Near(transient.HunterWeights[(int)Hunter.Sylux], transient240.HunterWeights[(int)Hunter.Sylux], 2e-5, hz + "Hz quarter-second theme transition");
    Near(transient.Occupancy[0], transient240.Occupancy[0], 2e-5, hz + "Hz quarter-second occupancy transition");
    Near(transient.SceneBlend, transient240.SceneBlend, 2e-5, hz + "Hz quarter-second chamber/lobby blend");
    Near(transient.ReadyPulse[0], transient240.ReadyPulse[0], 2e-5, hz + "Hz quarter-second ready signal decay");
}

Fixture(); var state = new MenuMotionState();
state.Advance(0, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
state.Advance(.05, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
double time = state.Time, energy = state.EnergyTime;
state.Advance(1, false, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Near(state.Time, time, 0, "unfocused timeline freezes");
Near(state.EnergyTime, energy, 0, "unfocused energy freezes");
state.Advance(5, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Near(state.Time, time, 0, "suspension gap is not fast-forwarded");
state.Suspend(); state.Advance(50, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Near(state.Time, time, 0, "explicit hidden suspend resumes without jump");
state.Advance(50.1, true, true, LauncherActivityAmbience.Lobby, Hunter.Trace);
Near(state.Time, time, 0, "reduced-motion timeline freezes");
Check(state.HunterWeights[(int)Hunter.Trace] == 1 && state.HunterWeights[(int)Hunter.Samus] == 0, "reduced motion applies correct hunter without animation");

Fixture(1, 0); state.Reset(); state.Advance(0, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
LauncherLobbyVisuals.ReadyMask = 1; state.Advance(.01, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Check(state.Ready[0] && state.ReadyPulse[0] > 0, "authoritative ready edge emits ready pulse");
LauncherLobbyVisuals.ReadyMask = 0; state.Advance(.02, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Check(!state.Ready[0] && state.ReadyPulse[0] == 0, "ready cancellation immediately removes ready signal");
LauncherLobbyVisuals.ReadyMask = 1; state.Advance(.03, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
LauncherLobbyVisuals.OccupiedMask = 0; state.Advance(.04, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Check(!state.Ready[0] && state.ReadyPulse[0] == 0, "leaving slot cannot retain ready signal");
Check(state.LeavePulse[0] > 0, "authoritative leave edge emits leave pulse");
LauncherLobbyVisuals.SetIdentity(0, 999); LauncherLobbyVisuals.OccupiedMask = 1; LauncherLobbyVisuals.ReadyMask = 0;
state.Advance(.05, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Check(!state.Ready[0] && state.ReadyPulse[0] == 0 && state.JoinPulse[0] > 0, "replacement joins without stale ready effect");
uint version = LauncherLobbyVisuals.SessionVersion;
LauncherLobbyVisuals.SetSession(7, 102);
Check(LauncherLobbyVisuals.SessionVersion != version, "different authoritative session creates new visual scope");
state.Advance(.06, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Check(!state.Ready[0] && state.ReadyPulse[0] == 0, "session identity reuse cannot inherit ready signal");

Fixture(3, 1); state.Reset();
state.Advance(0, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
LauncherLobbyVisuals.SetIdentity(0, 101); LauncherLobbyVisuals.SetIdentity(1, 100);
LauncherLobbyVisuals.SetHunter(0, Hunter.Kanden); LauncherLobbyVisuals.SetHunter(1, Hunter.Samus);
LauncherLobbyVisuals.ReadyMask = 2;
state.Advance(.01, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
Check(!state.Ready[0] && state.Ready[1], "presentation reorder carries each actor's authoritative ready state");
Check(state.JoinPulse.All(p => p == 0) && state.LeavePulse.All(p => p == 0)
    && state.ReadyPulse.All(p => p == 0), "stable actors reordered across pads cannot synthesize join, leave or ready edges");

Fixture(1, 0); state.Reset();
LauncherLobbyVisuals.SetTeam(0, 2);
state.Advance(0, true, true, LauncherActivityAmbience.Lobby, Hunter.Samus);
Check(state.SlotColors[0] == new MenuRgb(.161f, .612f, 1f), "authoritative blue team wins over hunter palette");
LauncherLobbyVisuals.SetTeam(0, -1); LauncherLobbyVisuals.SetHunter(0, Hunter.Spire);
state.Advance(.01, true, true, LauncherActivityAmbience.Lobby, Hunter.Spire);
Check(state.SlotColors[0] == LauncherHunterTheme.For(Hunter.Spire).Rim, "unassigned team uses hunter palette");
LauncherLobbyVisuals.SetTeam(0, 99);
Check(LauncherLobbyVisuals.TeamAt(0) == -1, "invalid team cannot fabricate team membership");
LauncherLobbyVisuals.Starting = true;
state.Advance(.02, true, false, LauncherActivityAmbience.Lobby, Hunter.Spire);
Check(state.Launch > 0, "authoritative starting state activates deployment intensity");
LauncherLobbyVisuals.Starting = false;
state.Advance(.03, true, false, LauncherActivityAmbience.Lobby, Hunter.Spire);
Check(state.Launch == 0, "authoritative launch cancellation clears deployment on same frame");
state.Advance(.04, true, false, LauncherActivityAmbience.Lobby, Hunter.Noxus);
Check(state.HeroReveal is > 0 and < 1, "hunter switch starts short stabilization reveal");
for (int i = 5; i < 105; i++) state.Advance(i / 100.0, true, false, LauncherActivityAmbience.Lobby, Hunter.Noxus);
Check(state.HeroReveal > .999f, "hunter stabilization converges without blocking navigation");
state.Advance(1.05, true, true, LauncherActivityAmbience.Lobby, Hunter.Sylux);
Check(state.HeroReveal == 1 && state.JoinPulse.All(p => p == 0) && state.LeavePulse.All(p => p == 0), "reduced motion removes reveal/join/leave transitions");

Fixture(); state.Reset();
for (int i = 0; i < 10000; i++) state.Advance(i / 240.0, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
long allocated = GC.GetAllocatedBytesForCurrentThread();
for (int i = 10000; i < 34000; i++) state.Advance(i / 240.0, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
Check(allocated == 0, "24,000 steady 240Hz presentation updates allocate zero bytes: " + allocated);
state.Reset();
Check(state.Time == 0 && state.EnergyTime == 0 && state.Launch == 0 && state.Ready.All(r => !r) && state.ReadyPulse.All(p => p == 0), "lifecycle reset clears visual history");
Console.WriteLine($"MENU ENVIRONMENT ACCEPTANCE: {checks} checks passed.");
