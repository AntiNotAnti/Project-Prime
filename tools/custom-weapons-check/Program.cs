using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

if (args.Length == 3 && args[0] == "--rifle-rig-gl") return CustomRifleRigGlChecks.Run(args[1],args[2]);
if (args.Length == 3 && args[0] == "--head-hit-rig-gl") return HeadHitRigGlChecks.Run(args[1],args[2]);
if (args.Length == 3 && args[0] == "--head-hit-damage-gl") return HeadHitDamageGlChecks.Run(args[1],args[2]);
if (args.Length == 3 && args[0] == "--head-hit-replay") return HeadHitReplayChecks.Run(args[1],args[2]);
if (args.Length == 3 && args[0] == "--rifle-assets-gl") return CustomRifleAssetSceneChecks.Run(args[1],args[2]);
if (args.Length == 3 && args[0] == "--wheel-native-hold-gl") return StockPickupGlChecks.Run(args[1],args[2],wheel:true,acquireWhileHeld:true);
if (args.Length == 3 && args[0] == "--wheel-gl") return StockPickupGlChecks.Run(args[1],args[2],wheel:true);
if (args.Length == 2 && args[0] == "--knowledge-input-client-scene") return CustomKnowledgeInputClientSceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--online-pickup-scene") return CustomOnlinePickupSceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--knowledge-authority-scene") return CustomKnowledgeAuthoritySceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--empty-life-scene") return CustomEmptyLifeSceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--cycle-scene") return CustomWeaponCycleSceneChecks.Run(args[1]);
if (args.Length == 3 && args[0] == "--pickup-gl") return StockPickupGlChecks.Run(args[1],args[2]);
if (args.Length == 2 && args[0] == "--scene") return OfflineSceneChecks.Run(args[1]);
if (args.Length == 3 && args[0] == "--stock-pickup-scene") return StockPickupSceneChecks.Run(args[1],args[2]);
if (args.Length is 2 or 3 && args[0] == "--scope-gl") return CustomScopeSceneChecks.Run(args[1],gl:true,output:args.Length==3?args[2]:null);
if (args.Length == 2 && args[0] == "--scope-scene") return CustomScopeSceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--offline-launch-scene") return CustomOfflineLaunchSceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--offline-availability-scene") return CustomOfflineAvailabilitySceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--local-input-scene") return CustomLocalInputSceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--rewind-scene") return CustomRewindSceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--client-projection-scene") return CustomAuthoritySceneChecks.RunClientProjection(args[1]);
if (args.Length == 2 && args[0] == "--public-input-client-scene") return CustomPublicInputClientSceneChecks.Run(args[1]);
if (args.Length == 1 && args[0] == "--gamepad-enhancements")
{
    try
    {
        var counter=typeof(GamepadChecks).GetField("_checks",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        int before=(int)counter.GetValue(null)!;GamepadEnhancementChecks.Run();
        Console.WriteLine($"PASS {(int)counter.GetValue(null)!-before} controller enhancement checks (synthetic input, no physical device or GL).");
        return 0;
    }
    catch (Exception error) {Console.Error.WriteLine("GAMEPAD ENHANCEMENTS FAIL "+error);return 1;}
}
if (args.Length == 2 && args[0] == "--native-selection-scene") return CustomAuthoritySceneChecks.Run(args[1],nativeSelectionOnly:true);
if (args.Length is 2 or 3 && args[0] is "--authority-replica-scene" or "--replica-capture-scene") return CustomAuthoritySceneChecks.Run(args[1],snapshotOnly:true,snapshotCase:args.Length==3 ? args[2]:null);
if (args.Length == 2 && args[0] == "--authority-scene") return CustomAuthoritySceneChecks.Run(args[1]);
if (args.Length == 2 && args[0] == "--engine-source-scene") return CustomAuthoritySceneChecks.Run(args[1],sourceOnly:true);
if (args.Length == 2 && args[0] == "--engine-admission-scene") return CustomAuthoritySceneChecks.Run(args[1],admissionOnly:true);
if (args.Length == 2 && args[0] == "--engine-readiness-scene") return CustomAuthoritySceneChecks.Run(args[1],readinessOnly:true);

int checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new Exception(name); checks++; }
void Reject<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new Exception("Expected rejection: " + name);
}
// Test-only developer identity, never a production registry default or grant.
Guid m7 = Guid.Parse("9a776851-f39f-4a76-8c13-c5d88875ef61");
string revision = new('a', 64);
var identity = WeaponIdentity.Custom(m7, revision);
var rules = MechanicsChecks.Rules();
var definition = WeaponDefinition.Create(m7, "Riftline M7", rules);
var customSource = CustomShotSource.ForOffline(definition,new(7,0x123456789abcdef0UL,3,5,9),13);
Check(customSource.IsValid && customSource.Owner.AuthorityEpoch == 0x123456789abcdef0UL && customSource.Owner.MatchId == 7
    && customSource.Owner.Slot == 3 && customSource.Owner.Generation == 5 && customSource.Owner.Life == 9
    && customSource.Sequence == 13 && customSource.Identity.Weapon == definition.Identity
    && customSource.Identity.Owner == customSource.Owner && customSource.Action == null && customSource.Certificate == null,
    "nominal offline custom source retains full epoch and distinct weapon/owner/sequence fields");
CustomShotSource? absentSource = default;
Check(!default(CustomProjectileIdentity).IsValid && absentSource == null,
    "default custom projectile identity is invalid and absent source is null");
Reject<ArgumentException>(() => CustomShotSource.ForOffline(definition,default,13),"offline source needs complete owner");
Reject<ArgumentException>(() => CustomShotSource.ForOffline(definition,customSource.Owner,0),"offline source needs nonzero sequence");
Reject<ArgumentNullException>(() => CustomShotSource.ForOffline(null!,customSource.Owner,13),"offline source needs exact definition");
Reject<ArgumentException>(() => new CustomProjectileIdentity(WeaponIdentity.Native(BeamType.Imperialist),customSource.Owner,13),
    "custom projectile identity cannot carry native weapon");
var provenanceBootstrap = new WorldBootstrapIdentity(new(7,0x123456789abcdef0UL,1),1,5,100,1,1);
var provenanceManifest = new CustomWeaponManifest(new[] { definition });
var provenanceAction = new CustomWeaponAction(provenanceBootstrap,provenanceManifest.Digest,3,9,new(10),13,100,90,0,
    CustomWeaponActionKind.Fire,0,new(0,.5f,0),Vector3.UnitZ);
var provenanceCertificate = new CustomSourceCertificate(provenanceAction,100,90,0,Vector3.Zero,Vector3.UnitZ,true,false,false);
var provenanceState = new WeaponRuntimeState(definition,customSource.Owner,100); provenanceState.SetEquipped(100,true);
var provenanceBefore = provenanceState.Snapshot(); provenanceState.TryFire(100,13,true);
var provenanceLaunch = new CustomAcceptedLaunch(definition,provenanceCertificate,provenanceBefore,provenanceState.Snapshot());
var launchSource = CustomShotSource.FromLaunch(provenanceLaunch);
Console.WriteLine($"Custom source field storage: {System.Runtime.CompilerServices.Unsafe.SizeOf<CustomShotSource?>()} bytes.");
Check(System.Runtime.CompilerServices.Unsafe.SizeOf<CustomShotSource?>() == IntPtr.Size,
    "native carriers store only a nullable custom source reference");
Check(launchSource.Identity == customSource.Identity && launchSource.Action == CustomActionIdentity.For(provenanceAction)
    && launchSource.Certificate == provenanceCertificate && launchSource.MatchesLaunch(provenanceLaunch)
    && !customSource.MatchesLaunch(provenanceLaunch),"launch provenance pairs exact action/certificate without converting to native shot key");
Check(!launchSource.MatchesLaunch(provenanceLaunch with { Source = provenanceCertificate with { TargetWorldFrame = 89 } })
    && launchSource.Certificate == provenanceCertificate && !launchSource.MatchesLaunch(default)
    && absentSource?.MatchesLaunch(default) != true,
    "launch match rejects altered immutable certificate and default launch without throwing");
Reject<ArgumentException>(() => CustomShotSource.FromLaunch(default),"default launch cannot create provenance");
Reject<ArgumentException>(() => CustomShotSource.FromLaunch(provenanceLaunch with
    { After = provenanceLaunch.After with { Owner = new(7,0x123456789abcdef0UL,3,5,10) } }),"launch resource owner must match action life");
Reject<ArgumentException>(() => CustomShotSource.FromLaunch(provenanceLaunch with
    { After = provenanceLaunch.After with { Magazine = provenanceBefore.Magazine } }),"launch provenance requires consistent one-round transition");
bool provenanceMatches = true;
for (int i=0; i<10000; i++) provenanceMatches &= launchSource.MatchesLaunch(provenanceLaunch);
long provenanceBytes = GC.GetAllocatedBytesForCurrentThread();
for (int i=0; i<100000; i++) provenanceMatches &= launchSource.MatchesLaunch(provenanceLaunch);
provenanceBytes = GC.GetAllocatedBytesForCurrentThread()-provenanceBytes;
Check(provenanceMatches && provenanceBytes == 0,"100000 warmed typed provenance matches allocate zero bytes");
var retainedSources = new CustomShotSource[1024];
for (int i=0; i<retainedSources.Length; i++) retainedSources[i] = CustomShotSource.FromLaunch(provenanceLaunch);
long sourceConstructionBytes = GC.GetAllocatedBytesForCurrentThread();
for (int i=0; i<retainedSources.Length; i++) retainedSources[i] = CustomShotSource.FromLaunch(provenanceLaunch);
sourceConstructionBytes = GC.GetAllocatedBytesForCurrentThread()-sourceConstructionBytes;
Console.WriteLine($"Accepted custom source construction: {sourceConstructionBytes/(double)retainedSources.Length} bytes/source ({retainedSources.Length} retained instances).");
GC.KeepAlive(retainedSources);
Check(!default(WeaponIdentity).IsValid && !default(WeaponRuntimeId).IsValid, "default is invalid");
Check(identity != WeaponIdentity.Native(BeamType.Imperialist), "M7 is never Imperialist");
Check(identity == WeaponIdentity.Custom(m7, revision), "stable equality");
Check(identity.GetHashCode() == -1766204125
    && WeaponIdentity.Native(BeamType.Imperialist).GetHashCode() == -843313896, "frozen cross-process hash vectors");
Check(identity != WeaponIdentity.Custom(m7, new string('b', 64)), "revision is identity");
Check(identity != WeaponIdentity.Custom(Guid.NewGuid(), revision), "content is identity");
foreach (int beam in Enumerable.Range(0, 9))
{
    var native = WeaponIdentity.Native((BeamType)beam);
    Check(WeaponIdentity.TryParse(native.ToString(), out var copy) && native == copy, "native codec");
}
Check(WeaponIdentity.TryParse(identity.ToString(), out var decoded) && decoded == identity, "custom codec");
foreach (string? bad in new[] { null, "", "native/-1", "native/9", "native/04", "Native/4", "native/4 ",
    "custom/" + Guid.Empty.ToString("N") + "/" + revision, identity.ToString().ToUpperInvariant(),
    identity.ToString()[..103], identity.ToString() + "0", "custom/" + m7.ToString("N") + "/" + new string('g',64) })
    Check(!WeaponIdentity.TryParse(bad, out var invalid) && !invalid.IsValid, "corrupt codec refuses with invalid output");
Reject<ArgumentOutOfRangeException>(() => WeaponIdentity.Native(BeamType.Platform), "platform is not a player weapon");
Reject<ArgumentOutOfRangeException>(() => WeaponIdentity.Native(BeamType.None), "none is not a weapon");
Reject<ArgumentException>(() => WeaponIdentity.Custom(Guid.Empty, revision), "zero content UUID");
Reject<ArgumentException>(() => WeaponIdentity.Custom(m7, new string('A', 64)), "noncanonical hash");
Reject<InvalidOperationException>(() => _ = identity.NativeBeam, "custom native cast");
Reject<ArgumentOutOfRangeException>(() => new WeaponRuntimeId(0), "zero runtime ID");
Reject<ArgumentException>(() => new WeaponDefinition(WeaponIdentity.Native(BeamType.Imperialist), "M7", rules), "native definition");
Reject<ArgumentException>(() => WeaponDefinition.Create(m7, "bad\nname", rules), "control characters");
identity = definition.Identity;
using var registry = new WeaponRegistry(true, new[] { definition });
using var another = new WeaponRegistry(false, Array.Empty<WeaponDefinition>());
Check(registry.Count == 10 && another.Count == 9, "session isolation");
Check(registry.TryResolve(identity, out var resolved) && resolved!.Custom == definition
    && resolved.Native == null && resolved.NativeAffinity == null, "custom discriminant resolution");
Check(registry.TryResolve(resolved!.RuntimeId, out var local) && ReferenceEquals(local, resolved), "frozen runtime mapping");
Check(!another.TryResolve(identity, out _) && !registry.TryResolve(default(WeaponIdentity), out _)
    && !registry.TryResolve(default(WeaponRuntimeId), out _), "missing/default cannot fall back to native");
Check(!registry.TryResolve(WeaponIdentity.Custom(m7, new string('b',64)), out _), "missing revision cannot upgrade");
for (int i=0; i<9; i++)
{
    Check(registry.TryResolve(WeaponIdentity.Native((BeamType)i), out var native)
        && native!.Custom == null && native.Native!.BodyDamage == Weapons.WeaponsMP[i].UnchargedDamage
        && native.NativeAffinity!.BodyDamage == Weapons.WeaponsMP[i+9].UnchargedDamage
        && native.Native.CooldownTicks == Weapons.WeaponsMP[i].ShotCooldown * 2,
        "native and affinity read adapters");
}
var otherDefinition = WeaponDefinition.Create(m7, "M7 revision two", MechanicsChecks.Rules(MechanicsChecks.Json.Replace("\"body\":65","\"body\":66")));
using (var a = new WeaponRegistry(true, new[] { definition, otherDefinition }))
using (var b = new WeaponRegistry(true, new[] { otherDefinition, definition }))
{
    a.TryResolve(identity,out var ar); b.TryResolve(identity,out var br);
    Check(ar!.RuntimeId == br!.RuntimeId, "canonical mapping independent of enumeration");
}
Reject<ArgumentException>(() => new WeaponRegistry(true, new[] { definition, definition }), "duplicate revisions");
Reject<ArgumentException>(() => new WeaponRegistry(true, Enumerable.Repeat(definition,65)), "bounded enumeration");
Check(!registry.TryResolve(new WeaponRuntimeId(ushort.MaxValue), out _), "unknown nonzero runtime ID");
var limitDefinitions = Enumerable.Range(1, 65).Select(i => WeaponDefinition.Create(m7,
    "Bounded " + i, MechanicsChecks.Rules(MechanicsChecks.Json.Replace("\"body\":65","\"body\":" + i)))).ToArray();
using (var full = new WeaponRegistry(true, limitDefinitions.Take(64)))
    Check(full.Count == 73, "exact 64-custom manifest limit accepted");
Reject<ArgumentException>(() => new WeaponRegistry(true, limitDefinitions), "65 distinct customs refused");
Parallel.For(0, 32, i =>
{
    using var isolated = new WeaponRegistry(i % 2 == 0, new[] { definition });
    if (!isolated.TryResolve(identity, out var value) || value!.RuntimeId.Value != 10)
        throw new Exception("Concurrent session isolation");
});
Check(true, "concurrent independent session construction");
var inventory = registry.Inventory(0);
Check(inventory.BindLife(new(1,1,0,1,1),0), "inventory binds owner/life before grants");
Check(inventory.Grant(identity,0) && !inventory.Grant(identity,0) && inventory.Contains(identity), "bounded separate grants");
Check(!inventory.Grant(WeaponIdentity.Native(BeamType.PowerBeam),0), "custom inventory refuses native grants");
Check(!registry.Inventory(1).Contains(identity), "player isolation");
Reject<ArgumentOutOfRangeException>(() => registry.Inventory(8), "slot bounds");
registry.Dispose(); registry.Dispose();
Reject<ObjectDisposedException>(() => registry.TryResolve(identity,out _), "teardown resolver");
Reject<ObjectDisposedException>(() => inventory.Contains(identity), "escaped inventory invalidated");

// Actual scene ownership/cleanup without cartridge data or rendering.
MphRead.Mods.Headless.Enter();
var scene = new Scene(new Vector2i(256,192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => {}, () => {}, initializeRuntime: false);
try
{
    Check(scene.CustomWeapons == null, "native-only scene pays no registry allocation");
    var owned = scene.FreezeCustomWeaponRegistry(new[] { definition });
    var held = owned.Inventory(0); held.BindLife(new(1,1,0,1,1),0); held.Grant(identity,0);
    Reject<InvalidOperationException>(() => scene.FreezeCustomWeaponRegistry(Array.Empty<WeaponDefinition>()), "no mid-match replacement");
    scene.DoCleanup();
    Check(owned.IsDisposed && scene.CustomWeapons == null, "scene cleanup owns registry");
    Reject<ObjectDisposedException>(() => held.Grant(identity,0), "scene invalidates escaped inventory");
    Reject<ObjectDisposedException>(() => scene.FreezeCustomWeaponRegistry(Array.Empty<WeaponDefinition>()), "exited scene cannot rebind");
}
finally { scene.DoCleanup(); }
Check(Weapons.WeaponsMP.Count == 18 && Weapons.Weapons1P.Count == 18
    && (sbyte)BeamType.Imperialist == 4 && (sbyte)BeamType.OmegaCannon == 8, "native shape remains fixed");
MechanicsChecks.Run(Check);
RuntimeStateChecks.Run(Check);
NetworkContractChecks.Run(Check);
SourceJournalChecks.Run(Check);
PickupFundingChecks.Run(Check);
PickupWorldChecks.Run(Check);
EngineSourceHistoryChecks.Run(Check);
SourceModifierChecks.Run(Check);
ReadinessChecks.Run(Check);
SourceBoundaryChecks.Run(Check);
ResultTransportChecks.Run(Check);
ControlRequestChecks.Run(Check);
ClientControlSubmissionChecks.Run(Check);
CustomInputControllerChecks.Run(Check);
CustomScopeChecks.Run(Check);
CustomPresentationResultChecks.Run(Check);
CustomFeedbackChecks.Run(Check);
CustomInputProjectionChecks.Run(Check);
NativeSelectionHistoryChecks.Run(Check);
NativeFireGateChecks.Run(Check);
InputCarrierChecks.Run(Check);
ReplicaDeliveryChecks.Run(Check);
ReplicaConsumerChecks.Run(Check);
ReplicaAudienceChecks.Run(Check);
CustomReplayBoundaryChecks.Run(Check);
CustomMatchPolicyChecks.Run(Check);
CustomOfflineAvailabilityChecks.Run(Check);
StockPickupChecks.Run(Check);
CustomWeaponCycleChecks.Run(Check);
CustomWeaponWheelChecks.Run(Check);
CustomPickupKnowledgeChecks.Run(Check);
CustomEmptyInventoryChecks.Run(Check);
CustomKnowledgeCarriageChecks.Run(Check);
CustomPickupReplicaChecks.Run(Check);
CustomRifleModelChecks.Run(Check);
CustomRifleCueChecks.Run(Check);
CustomRifleTrackChecks.Run(Check);
CustomRifleAudioChecks.Run(Check);
HeadHitTrackChecks.Run(Check);
HeadHitSceneChecks.Run(Check);
checks += CustomPaymentChecks.Run();
Console.WriteLine($"PASS {checks} custom weapon identity/registry/mechanics/state/network-contract/source-journal/engine-source-history/source-modifier/readiness/source-boundary/result-transport/control-request/input-carrier/replica-delivery/payment checks (pure contracts; scene fixture requires --scene)");
return 0;
