using System.Security.Cryptography;
using System.Text;
using MphRead;
using MphRead.Mods.CustomWeapons;

// Invented source-free profile/admission contracts only. No Scene, input,
// native ammo, burst/mark, collision, accepted damage or rendering proof.
internal static class PrivatePirateDefinitionChecks
{
    // Independent literal profile: changing private mechanics requires an
    // explicit revision here; do not derive expected values from the factory.
    private const string PrivateJson = """
        {"schema":2,"behaviorVersion":1,"behavior":"pirate-ua-burst","damage":{"body":12,"head":16},"burstOffsets":[0,6,12],"ammo":"native-ua","burstAmmoCost":5,"shotCooldownTicks":40,"projectileSpeed4096":8192,"projectileRadius4096":409,"range4096":0,"lifetimeTicks":510,"headshotRange4096":61440,"maxActiveProjectiles":40,"colorsRgb555":[4511,17183],"effectiveness":"native-power-beam","damageInterpolation":0,"fullBurstReservation":true,"splash":false,"piercing":false,"markDistinctBolts":3,"markLifetimeTicks":240}
        """;
    private const string PrivateSha = "e36df634e3024ef31a4f96fd9831bcc17515ef61006d0fede358a30473c20186";

    internal static void Run(Action<bool, string> check, CustomAcceptedLaunch precisionFixture)
    {
        void Reject<T>(Action action, string label) where T : Exception
        {
            try { action(); }
            catch (T) { check(true, "private Pirate: " + label); return; }
            throw new InvalidOperationException("Private Pirate expected refusal: " + label);
        }
        var m7 = RiftlineM7.Create(); var pirate = PirateBlasterDefinition.Definition; var rules = pirate.Mechanics;
        check(m7.Mechanics.ToCanonicalBytes().AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(MechanicsChecks.Json))
            && m7.Mechanics.DefinitionHash == "3ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228"
            && WeaponMechanics.Parse(m7.Mechanics.ToCanonicalBytes()).DefinitionHash == m7.Identity.DefinitionHash,
            "private profile preserves independent original M7 canonical bytes/hash/public parser");
        var precisionManifest = new CustomWeaponManifest(new[] { m7 });
        check(precisionManifest.Digest.ToString() == "a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9",
            "private profile leaves the original CWM1 M7 digest unchanged");
        byte[] canonical = rules.ToCanonicalBytes();
        check(canonical.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(PrivateJson))
            && Convert.ToHexStringLower(SHA256.HashData(canonical)) == PrivateSha && rules.DefinitionHash == PrivateSha,
            "closed private schema2 has independently pinned exact bytes and SHA256");
        check((byte)rules.Behavior == 2 && rules.Behavior == WeaponBehavior.PirateUaBurst
            && WeaponMechanics.SchemaVersion == 1 && WeaponMechanics.BehaviorVersion == 1,
            "private behavior2 never changes the public precision schema1/behavior1 contract");
        check(rules.BodyDamage == 12 && rules.HeadDamage == 16 && rules.ShotCooldownTicks == 40
            && rules.ProjectileSpeed4096 == 8192 && rules.ProjectileRadius4096 == 409
            && rules.Range4096 == 0 && rules.LifetimeTicks == 510 && rules.MaxActiveProjectiles == 40,
            "private scalar rules retain literal damage/cadence/fixed-step projectile units");
        check(rules.MagazineCapacity == 0 && rules.InitialReserve == 0 && rules.MaximumReserve == 0
            && rules.ReloadTicks == 0 && rules.OpticMagnificationsMilli.Count == 0
            && rules.HipSpreadMilliDegrees == 0 && rules.ScopedSpreadMilliDegrees == 0,
            "private UA profile has no synthetic M7 magazine/reserve/reload/optic/spread state");
        Reject<FormatException>(() => WeaponMechanics.Parse(canonical), "public parser refuses its own closed schema2 canonical export");
        Reject<FormatException>(() => WeaponMechanics.Parse(Encoding.UTF8.GetBytes(PrivateJson.Replace("\"schema\":2", "\"schema\":1"))),
            "changing the schema number cannot import a private behavior as precision");
        canonical[0] = 0;
        check(rules.ToCanonicalBytes().AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(PrivateJson))
            && rules.DefinitionHash == PrivateSha, "exported private byte arrays are detached from definition identity");
        check(pirate.Identity.Kind == WeaponIdentityKind.Custom
            && pirate.Identity.ContentId == Guid.Parse("49f3ab6c-bdc0-40ed-bf14-eaaa5ad5aa9f")
            && pirate.Identity != m7.Identity && pirate.Name == "Pirate Blaster",
            "private definition has its own literal custom content identity and name");
        for (int i = 0; i < 9; i++)
            check(pirate.Identity != WeaponIdentity.Native((BeamType)i), "private definition is never native weapon slot" + i);
        var equal = WeaponDefinition.Create(pirate.Identity.ContentId, "Synthetic relisting", rules);
        check(equal.Identity == pirate.Identity && !ReferenceEquals(equal, pirate)
            && PirateBlasterDefinition.Is(pirate) && !PirateBlasterDefinition.Is(equal)
            && !PirateBlasterDefinition.Is(m7) && !PirateBlasterDefinition.Is(null),
            "immutable catalog identity is distinct from exact private runtime definition ownership");

        Adapter(check);
        var owner = precisionFixture.Before.Owner;
        var zero = new WeaponStateSnapshot(owner, pirate.Identity, 100, CustomWeaponPhase.Ready, 0, 0, 0, 0, 0, 0, 0);
        Reject<ArgumentException>(() => new WeaponRuntimeState(pirate, owner, 100), "precision magazine state cannot own private UA profile");
        Reject<ArgumentException>(() => WeaponRuntimeState.Project(zero, rules, 100), "precision projection refuses a zero-valued private snapshot");
        Reject<ArgumentException>(() => new CustomWeaponManifest(new[] { pirate }), "online schema1 manifest refuses private-only profile");
        Reject<ArgumentException>(() => new CustomWeaponManifest(new[] { m7, pirate }), "online manifest refuses mixed precision/private profiles");
        Reject<ArgumentException>(() => new CustomWeaponManifest(new[] { pirate, m7 }), "manifest refusal does not depend on enumeration order");
        Reject<ArgumentException>(() => CustomPrecisionAdapter.Build(rules), "precision native adapter cannot reinterpret private raw fields");
        check(!CustomCombatResults.SnapshotValid(zero, owner, pirate),
            "otherwise-in-range zero private snapshot cannot enter precision receipt/state consumers");
        check(CustomCombatResults.SnapshotValid(precisionFixture.Before, owner, precisionFixture.Definition),
            "precision snapshot positive control still passes the same consumer boundary");
        using (var registry = new WeaponRegistry(true, new[] { pirate, m7 }))
        {
            check(registry.Count == 11 && registry.TryResolve(pirate.Identity, out var resolved)
                && ReferenceEquals(resolved!.Custom, pirate) && resolved.Native == null && resolved.NativeAffinity == null,
                "shared frozen registry may store the private exact definition without native identity aliasing");
            var inventory = registry.Inventory(owner.Slot);
            check(inventory.BindLife(owner, 100), "inventory chronology positive-control life binds");
            check(!inventory.Grant(pirate.Identity, 1000) && !inventory.Contains(pirate.Identity)
                && !inventory.TryGetState(pirate.Identity, out _), "private inventory grant returns false without precision state publication");
            WeaponRuntimeState? retained = null;
            check(inventory.Grant(m7.Identity, 101) && inventory.TryGetState(m7.Identity, out retained)
                && retained!.Snapshot().Tick == 101,
                "refused private grant did not consume time1000: ordinary precision grant still succeeds at101");
            check(!inventory.Grant(pirate.Identity, 2000) && retained!.Advance(102) == WeaponActionResult.Accepted,
                "later private rejection cannot mutate the existing precision state chronology");
        }
        check(Weapons.WeaponsMP.Count == 18 && Weapons.Weapons1P.Count == 18,
            "private registry/adapters leave both native nine+affinity tables unchanged");
        ForgedPrecisionLaunch(check, precisionFixture, pirate, Reject<ArgumentException>);
    }

    private static void Adapter(Action<bool, string> check)
    {
        var a = PirateBlasterDefinition.BuildAdapter(); var b = PirateBlasterDefinition.BuildAdapter();
        check(a.Beam == BeamType.None && a.BeamKind == BeamType.None && a.Flags == WeaponFlags.SurfaceCollision
            && a.Projectiles == 1 && a.MinChargeProjectiles == 1 && a.ChargedProjectiles == 1,
            "private raw adapter uses Beam=None and one native collision projectile, no native accepted-fire identity");
        check(a.UnchargedDamage == 12 && a.MinChargeDamage == 12 && a.ChargedDamage == 12
            && a.HeadshotDamage == 16 && a.MinChargeHeadshotDamage == 16 && a.ChargedHeadshotDamage == 16,
            "private adapter retains literal raw12/16 damage in all native authoring slots");
        check(a.UnchargedSpeed == 16384 && a.MinChargeSpeed == 16384 && a.ChargedSpeed == 16384
            && a.UnchargedCylRadius == 409 && a.MinChargeCylRadius == 409 && a.ChargedCylRadius == 409
            && a.UnchargedLifespan == 255 && a.MinChargeLifespan == 255 && a.ChargedLifespan == 255
            && a.ShotCooldown == 20 && a.AutofireCooldown == 20,
            "raw30Hz adapter values independently pin2units/60Hzstep,255native-life and20native-cadence");
        check(a.AmmoType == 0 && a.AmmoCost == 0 && a.MinChargeCost == 0 && a.ChargeCost == 0
            && a.UnchargedDistance == 0 && a.MinChargeDistance == 0 && a.ChargedDistance == 0,
            "raw adapter does not double-pay privately owned UA or add a precision range cutoff");
        check(a.Colors.SequenceEqual(new ushort[] { 4511, 17183 })
            && a.CollisionEffects.SequenceEqual(new byte[] { 4, 95 }) && a.MuzzleEffects.SequenceEqual(new byte[] { 65, 65 })
            && a.DamageInterpolations.SequenceEqual(new byte[] { 0, 0 }) && a.DmgDirTypes.SequenceEqual(new byte[] { 0, 0 }),
            "private raw colors/effects/interpolation/direction are independently literal, not the M7 adapter");
        check(a.UnchargedDmgDirMag == 0 && a.MinChargeDmgDirMag == 0 && a.ChargedDmgDirMag == 0
            && a.SplashDamage == 0 && a.MinChargeSplashDamage == 0 && a.ChargedSplashDamage == 0
            && a.UnchargedSplashRadius == 0 && a.MinChargeSplashRadius == 0 && a.ChargedSplashRadius == 0
            && a.UnchargedSpread == 0 && a.MinChargeSpread == 0 && a.ChargedSpread == 0
            && a.UnchargedGravity == 0 && a.MinChargeGravity == 0 && a.ChargedGravity == 0
            && a.UnchargedHoming == 0 && a.MinChargeHoming == 0 && a.ChargedHoming == 0,
            "raw adapter carries no added impulse/splash/spread/gravity/homing mechanics");
        var colors = (IList<ushort>)a.Colors; ushort prior = colors[0];
        try
        {
            colors[0] = 0;
            check(b.Colors.SequenceEqual(new ushort[] { 4511, 17183 })
                && PirateBlasterDefinition.BuildAdapter().Colors.SequenceEqual(new ushort[] { 4511, 17183 }),
                "fresh native adapter arrays do not share mutable color storage between emissions");
        }
        finally { colors[0] = prior; }
    }

    private static void ForgedPrecisionLaunch(Action<bool, string> check, CustomAcceptedLaunch valid,
        WeaponDefinition pirate, Action<Action, string> reject)
    {
        check(valid.Before.Magazine > 0 && valid.After.Magazine == valid.Before.Magazine - 1
            && CustomShotSource.IsConsistentLaunch(valid) && CustomShotSource.FromLaunch(valid).MatchesLaunch(valid),
            "reuse existing actual precision state debit/certificate as a non-incidental positive control");
        var forged = valid with { Definition = pirate,
            Before = valid.Before with { Weapon = pirate.Identity }, After = valid.After with { Weapon = pirate.Identity } };
        check(forged.Source == valid.Source && forged.Before.Owner == valid.Before.Owner
            && forged.Before.Magazine == valid.Before.Magazine && forged.After.Magazine == valid.After.Magazine,
            "forgery preserves valid owner/source/debit instead of relying on private zero magazine to fail");
        var offline = CustomShotSource.ForOffline(pirate, valid.Before.Owner, valid.Source.Action.Sequence);
        check(offline.IsValid && offline.Action == null && offline.Certificate == null
            && offline.Identity.Weapon == pirate.Identity, "nominal private offline provenance alone has no online action/certificate");
        check(!CustomShotSource.IsConsistentLaunch(forged) && !offline.MatchesLaunch(forged),
            "closed private behavior cannot masquerade as a precision accepted launch even with coherent debit fields");
        reject(() => CustomShotSource.FromLaunch(forged), "online provenance factory refuses forged private profile");
    }
}
