using System;
using System.Collections.Immutable;
using MphRead.Combat;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class AssistAttributionTests
    {
        private static CombatActor Actor(byte slot, uint life = 1) => new(slot, (ulong)slot + 100, life);
        private static int Collect(DamageContributionLedger ledger, uint tick, out CombatActor[] actors)
        {
            actors = new CombatActor[8];
            return ledger.Collect(Actor(1), tick, 20, 300, actors);
        }
        [Fact]
        public void EmptyAndResetLedgersNeverExposeStackContents()
        {
            var ledger = new DamageContributionLedger();
            for (int i = 0; i < 100; i++) Assert.Equal(0, Collect(ledger, (uint)i, out _));
            ledger.Add(Actor(0), Actor(2), 10, 100, true);
            Assert.Equal(1, Collect(ledger, 10, out _));
            ledger.Reset();
            Assert.Equal(0, Collect(ledger, 10, out _));
        }
        [Fact]
        public void ExactRecentWindowAndUnhealedDamageAreIndependent()
        {
            var ledger = new DamageContributionLedger();
            ledger.Add(Actor(0), Actor(2), 100, 20, true);
            ledger.Heal(Actor(0), 20);
            Assert.Equal(1, Collect(ledger, 400, out _));
            Assert.Equal(0, Collect(ledger, 401, out _));
            ledger.Add(Actor(0), Actor(2), 402, 1, true);
            Assert.Equal(0, Collect(ledger, 402, out _)); // chip cannot refresh old healed damage
            ledger.Add(Actor(0), Actor(2), 403, 19, true);
            Assert.Equal(1, Collect(ledger, 1000, out _)); // twenty still unhealed
            ledger.Heal(Actor(0), 1);
            Assert.Equal(0, Collect(ledger, 1000, out _));
        }
        [Fact]
        public void HealingConsumesOldestContributionAndNeverAnotherLife()
        {
            var ledger = new DamageContributionLedger();
            ledger.Add(Actor(0), Actor(2), 100, 30, true);
            ledger.Add(Actor(0), Actor(3), 101, 30, true);
            ledger.Heal(Actor(0, 2), 100); // wrong life
            Assert.Equal(2, Collect(ledger, 1000, out _));
            ledger.Heal(Actor(0), 40);
            Assert.Equal(1, Collect(ledger, 1000, out var actors));
            Assert.Equal(Actor(3), actors[0]);
            ledger.Add(Actor(0, 2), Actor(4), 1001, 20, true);
            Assert.Equal(1, Collect(ledger, 1001, out actors));
            Assert.Equal(Actor(4), actors[0]);
        }
        [Fact]
        public void TeamSelfKillerAndReplacementContributionsCannotEarnAssists()
        {
            var ledger = new DamageContributionLedger();
            ledger.Add(Actor(0), Actor(0), 1, 50, true);
            ledger.Add(Actor(0), Actor(1), 1, 50, true);
            ledger.Add(Actor(0), Actor(2), 1, 50, false);
            ledger.Add(Actor(0), Actor(3), 1, 30, true);
            ledger.Add(Actor(0), new(3, 900, 1), 2, 1, true);
            Assert.Equal(0, Collect(ledger, 2, out _));
            var output = new CombatActor[8];
            Assert.Equal(0, ledger.Collect(CombatActor.None, 2, 1, 300, output));
            Assert.Equal(0, ledger.Collect(Actor(0), 2, 1, 300, output));
        }
        [Fact]
        public void TickWrapAndBoundedOverflowStayDeterministic()
        {
            var ledger = new DamageContributionLedger();
            ledger.Add(Actor(0), Actor(2), uint.MaxValue - 10, 20, true);
            ledger.Heal(Actor(0), 20);
            Assert.Equal(1, Collect(ledger, 10, out _));
            for (uint i = 0; i < 64; i++) ledger.Add(Actor(0), Actor(1), i, 1, true);
            Assert.Equal(0, Collect(ledger, 64, out _));
        }
        [Fact]
        public void KillWireGoldenAndMalformedActorsAreStrict()
        {
            var kill = new KillEvent(10, 20, 30, 40, Actor(1), Actor(0), 4,
                KillEventFlags.Headshot, ImmutableArray.Create(Actor(2), Actor(3)));
            byte[] bytes = new byte[KillEvent.Size];
            kill.Write(bytes);
            Assert.Equal(137, bytes.Length);
            Assert.Equal(1, bytes[16]); Assert.Equal(0, bytes[29]);
            Assert.Equal(2, bytes[44]); Assert.Equal(255, bytes[72]);
            Assert.True(KillEvent.TryRead(bytes, out var read));
            Assert.Equal(kill.Id, read.Id); Assert.True(kill.Assists.AsSpan().SequenceEqual(read.Assists.AsSpan()));
            bytes[45] = 4; Assert.False(KillEvent.TryRead(bytes, out _)); bytes[45] = 0;
            bytes[46] = 1; Assert.False(KillEvent.TryRead(bytes, out _)); bytes[46] = 2;
            bytes[72] = 0; Assert.False(KillEvent.TryRead(bytes, out _));
            Assert.False(KillEvent.TryRead(bytes.AsSpan(0, 136), out _));
        }
        [Fact]
        public void AssistRuleFieldsRoundTripAndRejectZeroThreshold()
        {
            var rules = MatchRules.CreateDefault(MatchMode.Battle, "ROOM").With(assistMinimumDamage: 25, assistWindowTicks: 600);
            byte[] bytes = new byte[MatchRulesWire.Size]; MatchRulesWire.Write(bytes, rules);
            Assert.Equal(25, bytes[74]);
            Assert.True(MatchRulesWire.TryRead(bytes, out var read));
            Assert.Equal(25, read.AssistMinimumDamage); Assert.Equal(600, read.AssistWindowTicks);
            bytes[74] = bytes[75] = 0; Assert.False(MatchRulesWire.TryRead(bytes, out _));
        }
    }
}
