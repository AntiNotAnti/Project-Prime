using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Hits that land the instant they are fired, on the machine that fired
    /// them.
    ///
    /// Lag compensation put the *authority's* answer where the shooter aimed;
    /// it did nothing about when that answer arrives. A client shoots, the
    /// intent goes upstream, the authority resolves it, the snapshot comes
    /// back: the hit is correct and a full round trip late, so at 150 ms a
    /// player empties a clip into somebody who does not flinch until a fifth
    /// of a second after each shot. The authority itself never had this --
    /// its own gun resolves in the frame it is fired -- which is exactly the
    /// asymmetry this removes. Everyone who is not the authority resolves
    /// their own shots locally, now, and the authority's answer arrives later
    /// to confirm or overrule it.
    ///
    /// <b>Lag compensation is what makes this sound.</b> The authority rewinds
    /// every other player to the snapshot frame the shooter had applied --
    /// which is the world the shooter's own machine is holding when it fires.
    /// The local resolution and the authority's are therefore the same test
    /// against the same positions, and they agree except for a frame of skew
    /// and for the things a client cannot know (invulnerability windows the
    /// authority has already opened, a victim killed by somebody else in the
    /// meantime). Without <see cref="NetUnlagged"/> underneath it this would
    /// mispredict exactly as often as the shots used to miss.
    ///
    /// Three rules keep a prediction from becoming a lie:
    ///
    /// <list type="number">
    /// <item><b>A prediction never owns durable score or match end.</b> The
    /// authority's snapshot owns those outcomes.</item>
    /// <item><b>Only this client's own outgoing attacks are predicted.</b>
    /// Incoming damage from another player waits for authority. Completely
    /// local self-damage/self-death is the exception.</item>
    /// <item><b>A remote predicted hit does not author a remote death.</b>
    /// Locally lethal damage is clamped to leave the victim at one HP; the
    /// flinch/marker/nonlethal health feedback remains immediate while the
    /// body waits for the authoritative result.</item>
    /// </list>
    ///
    /// <para>
    /// <b>What is held, and for how long.</b> Predicting a hit and then
    /// letting the next snapshot assign the authority's health straight over
    /// it is a prediction that lasts one frame: the flinch is instant and the
    /// bar springs back up, which is the thing "it is not registering" is
    /// actually describing. So a prediction is held -- the victim's health is
    /// the authority's number minus whatever this machine has predicted and
    /// not yet had confirmed. Remote victims are not predicted dead in the
    /// current build; lethal remote damage is held at one HP until authority.
    /// The hold lasts one measured round trip and a margin
    /// (<see cref="HoldFrames"/>), never the two seconds a prediction is kept
    /// for the statistics: a mispredicted hit is a wrong health bar, and a
    /// wrong health bar has to expire in the time it takes the authority to
    /// answer rather than in the time it takes to be sure it never will.
    /// </para>
    ///
    /// A CombatAck retires only its named claim and shot. Its damage sequence
    /// identifies the exact authoritative event whose feedback was already shown;
    /// unrelated events still replay through <see cref="NetDamage"/>. Unanswered
    /// debits expire independently. Snapshots own death, score, and match outcomes.
    /// </summary>
    public static class NetHitPrediction
    {
        private const int Slots = PlayerEntity.SlotCapacity;

        /// <summary>
        /// Off with <c>-nohitprediction</c>, which is the control: the same
        /// match measured with hits resolving locally and with them waiting
        /// for the authority is the only way to say what this is worth. On by
        /// default, as <see cref="NetUnlagged.Enabled"/> is.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// The mark over the crosshair. Separate from the prediction because
        /// it is not part of it: the authority draws the same mark for the
        /// same hits, and so does an offline match, where there is nothing to
        /// predict and the confirmation is simply true. Off with
        /// <c>-nohitmarker</c>.
        /// </summary>
        public static bool MarkerEnabled { get; set; } = true;

        /// <summary>
        /// True on a machine whose own damage resolution is a guess rather
        /// than the answer -- a client that is not the authority, outside a
        /// replay of the authority's own hit.
        ///
        /// This is the same condition <see cref="NetDamage.Suppress"/> used to
        /// throw the hit away under, and it is still what decides whether a
        /// hit counts: <see cref="NetDamage.Note"/> records nothing here, so
        /// the diagnostics still describe one machine's resolution and not
        /// eight machines' opinions of it.
        /// </summary>
        public static bool Predicting => NetSession.Active
            && !NetSession.IsHost && !NetSession.IsAuthority && !NetDamage.Replaying;

        /// <summary>
        /// How long a prediction waits for the authority to agree with it.
        ///
        /// Generous on purpose: it has to cover the round trip, the gap
        /// between snapshots, and the rewind depth the authority may have
        /// applied on top. Anything still outstanding after two seconds was
        /// not a slow confirmation, it was a miss.
        /// </summary>
        private const int PendingFrames = 120;

        /// <summary>
        /// Predictions outstanding for one victim at once. A burst of Judicator
        /// shots or a Battlehammer stream can put several in the air before
        /// the first is answered; beyond this the oldest is dropped, since a
        /// prediction nobody has confirmed in two dozen hits is not going to
        /// be.
        ///
        /// Two dozen rather than the eight this started at, because the Shock
        /// Coil resolves a hit every other frame for as long as the trigger is
        /// held: at 300 ms of round trip that is nine outstanding before the
        /// first answer arrives, and an overflow does not merely lose a
        /// statistic any more -- it drops that hit's damage out of the health
        /// the victim is being held at.
        /// </summary>
        private const int PendingCapacity = 24;

        private static readonly uint[,] _pendingFrame = new uint[Slots, PendingCapacity];

        /// <summary>
        /// What each outstanding prediction took off that victim, so the bar
        /// can be drawn at the authority's health minus what this machine has
        /// already landed on them. See <see cref="HealthFor"/>.
        /// </summary>
        private static readonly int[,] _pendingDamage = new int[Slots, PendingCapacity];

        /// <summary>Whether that prediction was the one that killed them here.</summary>
        private static readonly bool[,] _pendingLethal = new bool[Slots, PendingCapacity];

        /// <summary>
        /// Whether this machine resolved that hit as a headshot.
        ///
        /// The question <see cref="Confirmed"/> cannot answer and the one the
        /// Imperialist is played on. A headshot is decided by a single
        /// comparison in <c>BeamProjectileEntity</c> -- the impact point must
        /// be at least 0.8 units above the victim's position, on a body 1.6
        /// units tall -- so the band is 0.3 units, the top eighth of a hunter.
        /// A rewind that lands the victim a frame out vertically, which is
        /// what a jump pad does in a frame, turns a headshot into a body shot
        /// while leaving the *hit* perfectly confirmed. So a client whose
        /// every prediction is confirmed can still be a client whose every
        /// instant kill was refused, and nothing here would have said so.
        /// </summary>
        private static readonly bool[,] _pendingHeadshot = new bool[Slots, PendingCapacity];

        /// <summary>
        /// The weapon behind each outstanding prediction, so the tally can be
        /// read a weapon at a time. <see cref="BeamType.None"/> -- an alt
        /// form's ram, a scythe, a bomb, the void -- is kept as
        /// <see cref="AltBeam"/> rather than dropped: half the complaints
        /// about prediction are about alt form, and a bucket that silently
        /// discarded them could not answer any of them.
        /// </summary>
        private static readonly byte[,] _pendingBeam = new byte[Slots, PendingCapacity];

        /// <summary>
        /// The claim identifying this prediction, or zero for self-damage or
        /// claims disabled. Remote debits retire through their own exact receipt;
        /// snapshot event counts cannot identify which prediction was answered.
        /// </summary>
        private static readonly ushort[,] _pendingClaim = new ushort[Slots, PendingCapacity];
        private static readonly uint[,] _pendingShot = new uint[Slots, PendingCapacity];
        private static readonly PredictionHealthProjection.Pending[,] _pendingProjection = new PredictionHealthProjection.Pending[Slots, PendingCapacity];
        // Exact authoritative event receipts replace victim-only settled credits.
        private const int ReceiptCapacity = 128;
        // Feedback receipts are consumed; projection exclusions must survive that
        // consumption so a paid event cannot subsidize a different pending hit.
        private static readonly PredictionHealthProjection.Accepted[,] _projectionAccepted = new PredictionHealthProjection.Accepted[Slots, ReceiptCapacity];
        private static readonly int[] _projectionAcceptedNext = new int[Slots];
        public static long ProjectionAmbiguousSamples { get; private set; }
        public static long ProjectionOverlapPoints { get; private set; }
        private static readonly ushort[,] _receiptSequence = new ushort[Slots, ReceiptCapacity];
        private static readonly uint[,] _receiptFrame = new uint[Slots, ReceiptCapacity];
        private static readonly int[] _receiptNext = new int[Slots];
        private static readonly Vector3[,] _pendingImpulse = new Vector3[Slots, PendingCapacity];
        private static readonly Vector3[,] _receiptImpulse = new Vector3[Slots, ReceiptCapacity];

        /// <summary>
        /// Whether that entry has already been answered by name -- a verdict
        /// naming its claim. It stays in the ring until the head reaches it,
        /// because the entries in front of it are still outstanding, but it
        /// owes nothing and must not be counted again.
        /// </summary>
        private static readonly bool[,] _pendingSpent = new bool[Slots, PendingCapacity];

        /// <summary>
        /// Whether that entry is a hit on this machine's own player -- its own
        /// splash, a fall. Kept out of the per-weapon tally for the same
        /// reason <see cref="SelfPredicted"/> is kept out of
        /// <see cref="Predicted"/>: it is not the same claim. There is no
        /// other machine's opinion to be wrong about, so counting it beside
        /// the shots that crossed a wire would flatter every weapon that can
        /// splash.
        /// </summary>
        private static readonly bool[,] _pendingSelf = new bool[Slots, PendingCapacity];
        private static readonly bool[,] _pendingDeferred = new bool[Slots, PendingCapacity];

        private static readonly int[] _pendingCount = new int[Slots];
        private static readonly int[] _pendingHead = new int[Slots];

        /// <summary>
        /// Last presentation health and prediction frame. The local self-damage
        /// path may hold a health floor; remote health uses independently aged
        /// debits so fresh predictions cannot prolong an older wrong health value.
        /// </summary>
        private static readonly int[] _shownHealth = new int[Slots];
        private static readonly uint[] _predictedFrame = new uint[Slots];

        /// <summary>
        /// Last authority health, used to measure damage and recognize healing
        /// that releases any local presentation floor.
        /// </summary>
        private static readonly int[] _lastAuthorityHealth = new int[Slots];

        /// <summary>How often a rise in the authority's number lifted the floor.</summary>
        public static long FloorLifted { get; private set; }

        /// <summary>
        /// Points of damage this machine predicted onto each slot, against the
        /// points the authority's own number actually came down by.
        ///
        /// <b>The direct answer to "is it counted twice".</b> Every other line
        /// here counts events -- hits predicted, hits confirmed -- and a hit
        /// counted once but worth twice as much looks perfect in all of them.
        /// These two are points, and with one client shooting (which is what
        /// <c>-hitrig missile</c> arranges) the authority's drop is this
        /// client's damage and nobody else's, so the ratio is the whole
        /// measurement.
        ///
        /// The authority's drop counts everything that hurt that player,
        /// including their own splash and the void, so in an ordinary match
        /// it is an upper bound rather than a comparison. Read it from a rig
        /// run.
        /// </summary>
        private static readonly long[] _predictedPoints = new long[Slots];
        private static readonly long[] _authorityDrop = new long[Slots];

        /// <summary>
        /// How long a prediction is allowed to hold the picture: one measured
        /// round trip, a snapshot's gap, and a margin.
        ///
        /// Not <see cref="PendingFrames"/>, which is how long a prediction is
        /// kept for the *statistics* -- two seconds, deliberately generous,
        /// because a confirmation that arrives late is still a confirmation
        /// and counting it as a miss would flatter nothing. Holding a health
        /// bar for two seconds is a different proposition: a hit that was
        /// wrong is a health bar that is wrong, and it has to right itself in
        /// about the time the authority takes to answer rather than in the
        /// time it takes to be certain it never will.
        ///
        /// The ping is the server's own measurement of this client's round
        /// trip (see <see cref="NetSession.SlotPing"/>), which is zero until
        /// it has one -- and the clamp is what makes that read as "assume a
        /// quarter of a second" rather than as "hold nothing".
        /// </summary>
        private static int HoldFrames
        {
            get
            {
                int slot = NetHooks.LocalSlot;
                int ping = slot >= 0 && slot < NetSession.SlotPing.Length
                    ? NetSession.SlotPing[slot]
                    : 0;
                // 60 Hz of simulation, plus twelve frames for the gap between
                // snapshots and the rewind the authority may have applied.
                //
                // Plus the grace a hit claim waits out on the authority
                // (NetHitClaims.GraceFrames) whenever claims are live at all.
                // A rescued hit is applied at the end of that window rather
                // than on arrival, so its confirmation is one grace later than
                // an ordinary one -- and a hold that expires first is the
                // resurrection the claim exists to stop, reintroduced by the
                // clock rather than by the authority disagreeing.
                int grace = NetHitClaims.Claiming ? NetHitClaims.GraceFrames : 0;
                return Math.Clamp((int)(ping * 0.06f) + 12 + grace, 15, 120);
            }
        }

        /// <summary>
        /// Exact owner continuous-tick healing credits. A credit remains until
        /// the matching owner snapshot includes its paid tick or its deadline ends.
        /// Settled entries retain tick identity briefly to reject local duplicates.
        /// </summary>
        private const int HealCapacity = 128;
        private static readonly uint[] _healFrame = new uint[HealCapacity];
        private static readonly uint[] _healPhase = new uint[HealCapacity];
        private static readonly int[] _healAmount = new int[HealCapacity];
        private static readonly ushort[] _healClaim = new ushort[HealCapacity];
        private static readonly ushort[] _healGeneration = new ushort[HealCapacity];
        private static readonly ushort[] _healLife = new ushort[HealCapacity];
        private static int _healCount;
        private static int _healHead;

        /// <summary>
        /// The bucket everything without a beam behind it is counted in: an
        /// alt form's ram, Weavel's scythe, Spire's spin, a bomb, the void.
        /// One past the last real <see cref="BeamType"/>.
        /// </summary>
        public const int AltBeam = 10;
        private const int BeamBuckets = AltBeam + 1;

        /// <summary>
        /// The same tally as <see cref="Predicted"/> and the rest, a weapon at
        /// a time -- and the reason it exists is that the aggregate cannot
        /// answer the question anybody actually asks. "The prediction is
        /// wrong" is never a statement about prediction; it is a statement
        /// about one weapon, or about alt form, and a 95% confirmed rate
        /// across a match is perfectly compatible with every charged Power
        /// Beam shot in it being resolved at a different number on the
        /// authority. <see cref="DescribeByWeapon"/> prints it.
        /// </summary>
        private static readonly long[] _beamPredicted = new long[BeamBuckets];
        private static readonly long[] _beamConfirmed = new long[BeamBuckets];
        private static readonly long[] _beamDenied = new long[BeamBuckets];
        private static readonly long[] _beamLethal = new long[BeamBuckets];
        private static readonly long[] _beamUndone = new long[BeamBuckets];
        private static readonly long[] _beamDamage = new long[BeamBuckets];

        private static int Bucket(BeamType beam)
        {
            int index = (int)beam;
            return index < 0 || index >= AltBeam ? AltBeam : index;
        }

        private static string BucketName(int bucket)
        {
            return bucket == AltBeam ? "alt/bomb" : ((BeamType)bucket).ToString();
        }

        /// <summary>Hits this machine resolved for itself, before being told.</summary>
        public static long Predicted { get; private set; }

        /// <summary>Predictions the authority went on to agree with.</summary>
        public static long Confirmed { get; private set; }

        /// <summary>
        /// Predictions the authority never confirmed: a shot that connected
        /// here and nowhere else. The number that says whether this is worth
        /// having -- a few per cent is the frame of skew, a large share means
        /// the local world and the rewound one are not the same world.
        /// </summary>
        public static long Denied { get; private set; }

        /// <summary>
        /// Hits the authority credited this machine with that it had not
        /// predicted -- the opposite error, and the one that costs nothing:
        /// it is shown when it arrives, which is what every hit used to do.
        /// </summary>
        public static long Unpredicted { get; private set; }

        /// <summary>
        /// Authority-confirmed hits this client did not predict, split by
        /// whether the local shooter was moving. This preserves upstream's
        /// diagnostic for spotting divergence between local projectile flight
        /// and the authority's rewound simulation.
        /// </summary>
        public static long UnpredictedMoving { get; private set; }
        public static long UnpredictedStill { get; private set; }

        private static bool MovingNow()
        {
            int local = NetHooks.LocalSlot;
            return local >= 0 && local < PlayerEntity.Players.Count
                && PlayerEntity.Players[local].Speed.LengthSquared > 0.0004f;
        }

        /// <summary>
        /// Remote lethal hits held at one health so the authority owns the
        /// actual death. This is the current behavior; the old death-prediction
        /// switches are compatibility no-ops.
        /// </summary>
        public static long LethalHeld { get; private set; }

        /// <summary>Legacy diagnostic retained for report compatibility; remote deaths are not predicted.</summary>
        public static long DeathsPredicted { get; private set; }

        /// <summary>
        /// Deaths this machine's own player died on the frame it died them: a
        /// rocket jump at low health, a fall into the void, a crusher. Counted
        /// apart from <see cref="DeathsPredicted"/> because it is not the same
        /// claim -- there is no other machine's opinion to be wrong about.
        /// </summary>
        public static long SelfDeathsPredicted { get; private set; }

        /// <summary>
        /// Legacy diagnostic retained for old report readers. Remote-player
        /// deaths are authority-owned now, so normal current runs keep this at zero.
        /// </summary>
        public static long DeathsUndone { get; private set; }

        /// <summary>Health drained by this machine's own beam, ahead of the authority.</summary>
        public static long DrainPredicted { get; private set; }
        public static long DrainConfirmed { get; private set; }
        public static long DrainRejected { get; private set; }
        public static long DrainExpired { get; private set; }
        public static int DrainCurrentPoints
        {
            get
            {
                int points = 0;
                int local = NetHooks.LocalSlot;
                for (int i = 0; i < _healCount; i++)
                {
                    int at = (_healHead + i) % HealCapacity;
                    if (NetSession.NetFrame - _healFrame[at] < PendingFrames
                        && NetPlayerLifecycle.Matches(local, _healGeneration[at], _healLife[at]))
                        points += _healAmount[at];
                }
                return points;
            }
        }

        /// <summary>
        /// Headshots this machine resolved on somebody else, and what the
        /// authority said about the same hits when it answered.
        ///
        /// <see cref="HeadshotsDowngraded"/> is the number this whole exercise
        /// is about: the shooter saw a head hit, the authority agreed a hit
        /// landed and called it a body shot. On the Imperialist that is the
        /// difference between an instant kill and half a health bar, and it is
        /// invisible to <see cref="Confirmed"/>, which counts the hit and asks
        /// nothing about where it landed.
        ///
        /// <see cref="HeadshotsUpgraded"/> is the opposite error and is worth
        /// counting for the same reason a mispredicted hit is: it says the two
        /// machines disagree about the victim's height, not which way.
        /// </summary>
        public static long HeadshotsPredicted { get; private set; }
        public static long HeadshotsAgreed { get; private set; }
        public static long HeadshotsDowngraded { get; private set; }
        public static long HeadshotsUpgraded { get; private set; }

        /// <summary>
        /// Your own splash, on you, resolved the frame it went off -- the
        /// rocket jump. Counted apart from <see cref="Predicted"/> because it
        /// is not the same claim: source, target and input are all on this
        /// machine, so it is arithmetic rather than a bet on a rewind, and
        /// mixing the two would flatter the percentage that measures the bet.
        /// </summary>
        public static long SelfPredicted { get; private set; }

        /// <summary>Those of them the authority went on to agree with.</summary>
        public static long SelfConfirmed { get; private set; }

        /// <summary>Compatibility option: remote death always waits for authority.</summary>
        public static bool DeathEnabled { get => false; set { } }
        private static readonly bool[,] _pendingHeld = new bool[Slots, PendingCapacity];
        public static long LethalConfirmed { get; private set; }
        public static long LethalDenied { get; private set; }

        private static void ResolveHeld(int slot, int at, bool confirmed)
        {
            if (!_pendingHeld[slot, at]) return;
            _pendingHeld[slot, at] = false;
            if (confirmed) LethalConfirmed++; else LethalDenied++;
            if (NetLog.Enabled) NetLog.Event($"[predict] lethal {(confirmed ? "confirmed" : "denied")} "
                + $"slot={slot} generation={NetPlayerLifecycle.Generation(slot)} life={NetPlayerLifecycle.Get(slot)} "
                + $"frame={NetSession.NetFrame} {LifecycleDetails(slot)}");
        }

        private static readonly ushort[] _life = new ushort[Slots];
        private static readonly ushort[] _generation = new ushort[Slots];
        private static readonly ushort[,] _pendingLife = new ushort[Slots, PendingCapacity];
        private static readonly ushort[,] _pendingGeneration = new ushort[Slots, PendingCapacity];

        private static void EnsureLife(int slot)
        {
            if (slot < 0 || slot >= Slots) return;
            if (_life[slot] != NetPlayerLifecycle.Get(slot) || _generation[slot] != NetPlayerLifecycle.Generation(slot))
            {
                ForgetSlot(slot);
                _life[slot] = NetPlayerLifecycle.Get(slot);
                _generation[slot] = NetPlayerLifecycle.Generation(slot);
            }
        }

        internal static int Outstanding
        {
            get
            {
                int count = 0;
                for (int slot = 0; slot < Slots; slot++)
                    for (int i = 0; i < _pendingCount[slot]; i++)
                    {
                        int at = (_pendingHead[slot] + i) % PendingCapacity;
                        if (!_pendingSpent[slot, at] && !_pendingSelf[slot, at]) count++;
                    }
                return count;
            }
        }
        internal static int SelfOutstanding
        {
            get
            {
                int count = 0;
                for (int slot = 0; slot < Slots; slot++)
                    for (int i = 0; i < _pendingCount[slot]; i++)
                    {
                        int at = (_pendingHead[slot] + i) % PendingCapacity;
                        if (_pendingSelf[slot, at] && !_pendingSpent[slot, at] && _pendingClaim[slot, at] != 0) count++;
                    }
                return count;
            }
        }
        internal static object Capture() => new { Predicted, Confirmed, Denied, Outstanding,
            SelfPredicted, SelfConfirmed, SelfDeathsPredicted, SelfOutstanding, SelfPendingClaims = SelfOutstanding,
            CombatAcks, LethalHeld, LethalConfirmed, LethalDenied, HealthDisagreed,
            DrainPredicted, DrainConfirmed, DrainRejected, DrainExpired, DrainCurrentPoints };

        public static string LifecycleDetails(int slot) => $"pending={_pendingCount[slot]} "
            + $"debit={Debit(slot)} authorityHealth={_lastAuthorityHealth[slot]} heldDead={HeldDead(slot)}";

        public static string HealthDetails(int slot)
        {
            if (slot < 0 || slot >= Slots) return "invalid slot";
            if (!NetPlayerLifecycle.Matches(slot, _generation[slot], _life[slot]))
                return "predictedDebit=0 shownFloor=0 pending=0 lastAuthorityHP=0";
            return $"predictedDebit={Debit(slot)} shownFloor={_shownHealth[slot]} pending={_pendingCount[slot]} lastAuthorityHP={_lastAuthorityHealth[slot]}";
        }

        // Three frames: beyond this the authority's catch-up commonly
        // resolves the projectile before the local copy reaches its target.

        private static int _markerTimer;

        /// <summary>
        /// Frames the mark stays up. Two tenths of a second: long enough to
        /// read at a glance, short enough that a stream of hits reads as a
        /// stream rather than as one long flash.
        /// </summary>
        private const int MarkerFrames = 12;

        /// <summary>
        /// How solid the mark is drawn, 0 when there is nothing to draw. It
        /// fades over its last few frames rather than blinking out.
        /// </summary>
        public static float MarkerAlpha
        {
            get
            {
                if (!MarkerEnabled || _markerTimer <= 0)
                {
                    return 0;
                }
                const int fade = 6;
                return _markerTimer >= fade ? 1f : _markerTimer / (float)fade;
            }
        }

        public static void Reset()
        {
            Array.Clear(_life);
            Array.Clear(_ackHealthValid);
            Array.Clear(_generation);
            Array.Clear(_pendingLife);
            Array.Clear(_pendingGeneration);
            Array.Clear(_pendingFrame);
            Array.Clear(_pendingDamage);
            Array.Clear(_pendingLethal);
            Array.Clear(_pendingHeadshot);
            Array.Clear(_pendingBeam);
            Array.Clear(_pendingClaim);
            Array.Clear(_pendingShot);
            Array.Clear(_pendingProjection);
            Array.Clear(_projectionAccepted); Array.Clear(_projectionAcceptedNext);
            ProjectionAmbiguousSamples = ProjectionOverlapPoints = 0;
            Array.Clear(_receiptSequence); Array.Clear(_receiptFrame); Array.Clear(_receiptNext);
            Array.Clear(_pendingSpent);
            Array.Clear(_pendingSelf); Array.Clear(_pendingDeferred);
            Array.Clear(_beamPredicted);
            Array.Clear(_beamConfirmed);
            Array.Clear(_beamDenied);
            Array.Clear(_beamLethal);
            Array.Clear(_beamUndone);
            Array.Clear(_beamDamage);
            Array.Clear(_pendingCount);
            Array.Clear(_pendingHead);
            Array.Clear(_healFrame);
            Array.Clear(_healAmount);
            Array.Clear(_healPhase); Array.Clear(_healClaim);
            Array.Clear(_healGeneration); Array.Clear(_healLife);
            _healCount = 0;
            _healHead = 0;
            Predicted = 0;
            Confirmed = 0;
            CombatAcks = DamageCorrections = HealthCorrections = HeadshotCorrections = 0;
            SelfPredicted = 0;
            SelfConfirmed = 0;
            Denied = 0;
            Unpredicted = 0;
            UnpredictedMoving = 0;
            UnpredictedStill = 0;
            LethalHeld = LethalConfirmed = LethalDenied = 0;
            Array.Clear(_pendingHeld);
            DeathsPredicted = 0;
            SelfDeathsPredicted = 0;
            DeathsUndone = 0;
            Array.Clear(_shownHealth);
            Array.Clear(_predictedFrame);
            HealthSamples = 0;
            HealthDisagreed = 0;
            HealthUnderPoints = 0;
            HealthUnderWorst = 0;
            HealthOverPoints = 0;
            HealthOverWorst = 0;
            FloorLifted = 0;
            Array.Clear(_lastAuthorityHealth);
            Array.Clear(_predictedPoints);
            Array.Clear(_authorityDrop);
            FloorHeld = 0;
            FloorHeldPoints = 0;
            FloorWorst = 0;
            DebitPoints = 0;
            DebitWorst = 0;
            DrainPredicted = DrainConfirmed = DrainRejected = DrainExpired = 0;
            HeadshotsPredicted = 0;
            HeadshotsAgreed = 0;
            HeadshotsDowngraded = 0;
            HeadshotsUpgraded = 0;
            _markerTimer = 0;
        }

        /// <summary>
        /// Whether damage this machine has just resolved should be allowed to
        /// land as a prediction rather than thrown away.
        ///
        /// Asked by <see cref="NetDamage.Suppress"/>, which is the top of
        /// <c>TakeDamage</c> -- before any of the engine's feedback has run,
        /// so a "no" here costs exactly what it always cost.
        /// </summary>
        public static bool Predicts(PlayerEntity victim, EntityBase? source, DamageFlags flags)
        {
            if (!Enabled || victim.Health <= 0 || flags.TestFlag(DamageFlags.Burn))
            {
                return false;
            }
            int local = NetHooks.LocalSlot;
            if (local < 0)
            {
                return false;
            }
            // Environment and damage-over-time are exclusively authority resolved.
            if (source == null) return false;
            // Whose shot it is, and nothing about who it lands on.
            //
            // This used to refuse a hit whose victim was this machine's own
            // player -- rule two, incoming damage is not predicted -- and that
            // refusal is already made by the line below: damage arriving from
            // somebody else has an owner who is not this slot. What the extra
            // clause actually excluded was the one hit that is *entirely*
            // local: your own splash, on you. Source, target and input are all
            // on this machine, there is nothing to guess about anybody, and it
            // is the hit whose feedback matters most on the frame it happens,
            // because a rocket jump is not damage that arrives late -- it is a
            // jump that does not happen. See the self-damage section in
            // .claude/multiplayer/NETWORK-PREDICTION.md.
            PlayerEntity? owner = OwnerOf(source);
            if (owner == null || owner.SlotIndex != local)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Whose shot this is. A halfturret's beams are Weavel's, and a bomb
        /// belongs to whoever laid it -- both are hits a player aimed and
        /// both are answered by the authority the same way.
        /// </summary>
        private static PlayerEntity? OwnerOf(EntityBase? source)
        {
            if (source is BeamProjectileEntity beam)
            {
                if (beam.Owner is PlayerEntity player)
                {
                    return player;
                }
                if (beam.Owner is HalfturretEntity turret)
                {
                    return turret.Owner;
                }
                return null;
            }
            if (source is BombEntity bomb)
            {
                return bomb.Owner;
            }
            if (source is PlayerEntity attacker)
            {
                // An alt form's attack -- Weavel's scythe, Spire's spin,
                // Sylux's trail -- is dealt by the player rather than by
                // anything it spawned. These are the hits that feel worst
                // when they arrive late, because they land at arm's length:
                // the whole of the attack is over before the authority's
                // answer to it comes back.
                return attacker;
            }
            return null;
        }

        /// <summary>
        /// A hit that has survived every one of <c>TakeDamage</c>'s refusals
        /// and is about to be applied: the damage is final, the attacker is
        /// known, and the death has not been decided yet. The last moment a
        /// prediction can be recorded, and the only one at which the clamp
        /// that keeps it from killing still works.
        /// </summary>
        /// <param name="beam">
        /// The weapon behind the hit, or <see cref="BeamType.None"/> for
        /// everything that is not a beam -- an alt form's attack, a bomb, the
        /// void. Only a hit claim reads it, and only so that the authority can
        /// bound the damage against what that weapon can actually deal and the
        /// victim's own machine can replay the right hit rather than a
        /// nameless one. <see cref="NetHitClaims"/>.
        /// </param>
        /// <param name="flight">
        /// How long the projectile behind this hit had been in the air, in
        /// seconds. Zero for everything that is not a beam.
        ///
        /// Retained for caller compatibility; all remote lethal hits are held.
        /// </param>
        /// <param name="afflictions">
        /// Afflictions caused by this specific hit. This is carried separately
        /// from the victim's current state so a later hit on somebody who is
        /// already frozen cannot masquerade as a second Judicator freeze.
        /// </param>
        public static void NoteHit(PlayerEntity victim, PlayerEntity? attacker,
            ref DamageFlags flags, ref uint damage, BeamType beam = BeamType.None,
            uint launchFrame = 0, float flight = 0, Vector3? direction = null,
            Affliction afflictions = Affliction.None, uint unsplitDamage = 0, uint continuousPhase = 0, bool direct = false, uint shotId = 0, ShotKey? shotKey = null, uint componentId = 0, bool deferSelf = false)
        {
            int local = NetHooks.LocalSlot;
            if (local < 0)
            {
                return;
            }
            // No attacker at all is the environment, and Predicts only ever
            // lets one of those through for this machine's own player: the
            // void, a kill plane, a crusher. It is a self-kill with nothing
            // holding the trigger.
            bool self;
            if (attacker == null)
            {
                if (victim.SlotIndex != local)
                {
                    return;
                }
                self = true;
            }
            else
            {
                if (attacker.SlotIndex != local)
                {
                    return;
                }
                self = attacker == victim;
            }
            if (Predicting && Enabled)
            {
                // Lethal even at zero damage when the flag says so: a fall
                // into the void is TakeDamage(0, DamageFlags.Death), and
                // reading the number alone would file the one death that is
                // certainly right as a scratch.
                bool lethal = victim.Health > 0
                    && (damage >= (uint)victim.Health || flags.TestFlag(DamageFlags.Death));
                // Keep feedback immediate while reserving remote death for authority.
                uint claimedDamage = damage;
                int weapon = NetShotDiagnostics.Bucket(beam);
                NetShotDiagnostics.LocalHits[weapon]++;
                NetShotDiagnostics.Predictions[weapon]++;
                NetShotDiagnostics.PredictedDamage[weapon] += damage;
                if (flags.TestFlag(DamageFlags.Headshot)) NetShotDiagnostics.LocalHeadshots[weapon]++;
                if (attacker != null && NetLog.Enabled) NetShotDiagnostics.Trace("prediction",
                    ShotKey.For(attacker.SlotIndex, shotId), beam, $"victim={victim.SlotIndex} damage={damage}");
                bool claimedLethal = lethal && !deferSelf;
                if (deferSelf) { damage = 0; lethal = false; }
                bool exactSelf = self && attacker != null && NetSelfSplash.Claimable(beam, flags, direct, shotId, componentId);
                if (lethal && (!self || exactSelf))
                {
                    damage = (uint)Math.Max(0, victim.Health - 1);
                    flags &= ~DamageFlags.Death;
                    LethalHeld++;
                    lethal = false;
                    if (NetLog.Enabled) NetLog.Event($"[predict] lethal held slot={victim.SlotIndex} "
                        + $"life={NetPlayerLifecycle.Get(victim.SlotIndex)} frame={NetSession.NetFrame}");
                }
                if (NetCampaignEvidence.Enabled && attacker != null)
                    NetCampaignEvidence.Timeline("predicted-feedback", shotKey ?? ShotKey.For(attacker.SlotIndex, shotId),
                        componentId, continuousPhase, victim: (byte)victim.SlotIndex,
                        victimGeneration: NetPlayerLifecycle.Generation(victim.SlotIndex),
                        victimLife: NetPlayerLifecycle.Get(victim.SlotIndex), weapon: (byte)beam,
                        value: (int)Math.Min(damage, int.MaxValue));
                bool headshot = flags.TestFlag(DamageFlags.Headshot);
                if (!self)
                {
                    // Capped at what is actually there to take, because that
                    // is all the authority's own number can come down by: a
                    // 32-damage Missile into a victim on 3 health removes 3.
                    // Counting the 32 made every kill look like a hit worth a
                    // third more than it was, which is how this ledger first
                    // read x1.21 on a run whose damage was exact.
                    _predictedPoints[victim.SlotIndex] +=
                        Math.Min((int)damage, Math.Max(0, victim.Health));
                }
                int at = Push(victim.SlotIndex, NetSession.NetFrame, (int)damage,
                    lethal, headshot, beam, self);
                if (at >= 0)
                {
                    var canonical = NetSession.RemoteStates[victim.SlotIndex];
                    bool form = canonical.Form.Phase != FormPhase.Unknown;
                    _pendingProjection[victim.SlotIndex, at] = new((int)damage,
                        canonical.DamageEventId, form ? canonical.Form.DamageEventId : canonical.DamageEventId,
                        canonical.Health, form ? canonical.Form.BodyHealth : canonical.Health,
                        (byte)local, shotKey?.Generation ?? NetPlayerLifecycle.Generation(local), (byte)beam);
                    _pendingHeld[victim.SlotIndex, at] = claimedLethal && (!self || exactSelf);
                    _pendingDeferred[victim.SlotIndex, at] = deferSelf;
                    _pendingImpulse[victim.SlotIndex, at] = deferSelf ? Vector3.Zero : direction ?? Vector3.Zero;
                    if (self)
                    {
                        _shownHealth[victim.SlotIndex] = Math.Max(1, victim.Health - (int)damage);
                        if (_lastAuthorityHealth[victim.SlotIndex] == 0)
                            _lastAuthorityHealth[victim.SlotIndex] = NetSession.RemoteStates[victim.SlotIndex].Health;
                    }
                }
                // And tell the authority, which may not find this hit itself:
                // its rewind has a ceiling, its copy of the trigger pull may
                // be stale, and if this machine's player is killed during the
                // round trip it will not run the shot at all. The position
                // handed over is *this machine's* copy of the victim, which is
                // the one thing about the claim the authority can check --
                // against its own history, at the frame this machine was
                // looking at. NetHitClaims.
                //
                // The id comes back and is stamped onto the prediction just
                // filed, because the verdict for that claim is the only exact
                // answer this machine ever gets about this particular hit.
                // See _pendingClaim.
                if (attacker != null)
                {
                    ushort claimId = NetHitClaims.Declare(victim, attacker, beam, flags.TestFlag(DamageFlags.Halfturret) ? unsplitDamage : claimedDamage,
                        flags, claimedLethal, victim.Position, launchFrame,
                        direction ?? Vector3.Zero, afflictions, predictedBodyDamage: claimedDamage, continuousPhase: continuousPhase, direct: direct, shotId: shotId, shotKey: shotKey, componentId: componentId);
                    StampClaim(victim.SlotIndex, at, claimId);
                    if (at >= 0) _pendingShot[victim.SlotIndex, at] = shotId;
                }
                if (headshot && !self)
                {
                    HeadshotsPredicted++;
                }
                // Counted apart from the rest. The confirmed percentage is a
                // claim about shots aimed at other people over a wire; a hit
                // on yourself, resolved on the machine that fired it, would
                // only flatter it.
                if (self)
                {
                    SelfPredicted++;
                }
                else
                {
                    Predicted++;
                }
                if (lethal)
                {
                    if (self)
                    {
                        SelfDeathsPredicted++;
                    }
                    else
                    {
                        DeathsPredicted++;
                    }
                }
            }
            // Every machine, every mode: on the authority and offline this is
            // a hit that has actually happened, and there is no reason the
            // confirmation a player gets should depend on which machine is
            // running the match. Not in the story, which is the DS's game and
            // has no such mark.
            //
            // Never for your own splash landing on you: the mark answers "did
            // that land on somebody", and a rocket jump is not a hit anybody
            // wants confirming.
            if (!GameState.SinglePlayer && !self)
            {
                _markerTimer = MarkerFrames;
            }
        }

        /// <summary>
        /// Confirm legacy local self-damage that has no outgoing claim identity.
        /// Remote predictions require CombatAck and exact event receipts instead.
        /// </summary>
        public static bool Confirm(int slot, int landed = 1, bool authorityHeadshot = false)
        {
            EnsureLife(slot);
            if (slot < 0 || slot >= Slots)
            {
                return false;
            }
            // Legacy self-damage has no outgoing claim identity. Remote hits
            // require their own ACK; an event count cannot retire a prediction.
            if (slot != NetHooks.LocalSlot || _pendingCount[slot] == 0) return false;
            // Claimed self hits must wait for their exact receipt. A snapshot
            // counter cannot consume the next pending rocket jump by accident.
            if (_pendingClaim[slot, _pendingHead[slot]] != 0) return false;
            bool self = true;
            int take = Math.Clamp(landed, 1, _pendingCount[slot]);
            for (int i = 0; i < take; i++)
            {
                int head = _pendingHead[slot];
                bool predictedHeadshot = _pendingHeadshot[slot, head];
                _pendingHeadshot[slot, head] = false;
                // Already answered by name, by the verdict for its own claim.
                // The snapshot is counting the same hit, so it consumes one of
                // `take` -- it just must not be counted a second time.
                if (RetireHead(slot, confirmed: true) < 0)
                {
                    continue;
                }
                if (self)
                {
                    SelfConfirmed++;
                    continue;
                }
                Confirmed++;
                // Only the last of a batch is described by the flags this
                // snapshot carries; the earlier ones in the same window are
                // hits whose flags were overwritten before they were sent, and
                // scoring them against this one would invent disagreements
                // that the wire never reported either way.
                if (i != take - 1)
                {
                    continue;
                }
                if (predictedHeadshot && authorityHeadshot)
                {
                    HeadshotsAgreed++;
                }
                else if (predictedHeadshot)
                {
                    HeadshotsDowngraded++;
                }
                else if (authorityHeadshot)
                {
                    HeadshotsUpgraded++;
                }
            }
            return true;
        }

        /// <summary>Suppress feedback only when an accepted exact claim named
        /// this authority event. An unidentified snapshot never settles a debit.</summary>
        internal static bool ConfirmEvent(int slot, ushort generation, ushort life, ushort eventId)
        {
            if ((uint)slot >= Slots || eventId == 0 || !NetPlayerLifecycle.Matches(slot, generation, life)) return false;
            EnsureLife(slot);
            for (int i = 0; i < ReceiptCapacity; i++)
                if (_receiptSequence[slot, i] == eventId
                    && unchecked(NetSession.NetFrame - _receiptFrame[slot, i]) < PendingFrames)
                { _receiptSequence[slot, i] = 0; return true; }
            Unpredicted++;
            if (MovingNow()) UnpredictedMoving++; else UnpredictedStill++;
            return false;
        }

        internal static bool HasSelfClaims(byte beam = 255)
        {
            int slot = NetHooks.LocalSlot;
            if ((uint)slot >= Slots) return false;
            for (int i = 0; i < _pendingCount[slot]; i++)
            {
                int at = (_pendingHead[slot] + i) % PendingCapacity;
                if (_pendingSelf[slot, at] && _pendingClaim[slot, at] != 0
                    && !_pendingSpent[slot, at] && (beam == 255 || _pendingBeam[slot, at] == Bucket((BeamType)beam))) return true;
            }
            return false;
        }
        internal static bool HasEventReceipt(int slot, ushort eventId)
        {
            if ((uint)slot >= Slots) return false;
            for (int i = 0; i < ReceiptCapacity; i++)
                if (_receiptSequence[slot, i] == eventId && eventId != 0
                    && NetSession.NetFrame - _receiptFrame[slot, i] < PendingFrames) return true;
            return false;
        }
        internal static bool ConfirmSelfEvent(int slot, ushort generation, ushort life, ushort eventId, Vector3 impulse, bool alreadyApplied = false)
        {
            if ((uint)slot >= Slots || !NetPlayerLifecycle.Matches(slot, generation, life)) return false;
            for (int i = 0; i < ReceiptCapacity; i++)
                if (_receiptSequence[slot, i] == eventId && eventId != 0
                    && NetSession.NetFrame - _receiptFrame[slot, i] < PendingFrames)
                {
                    _receiptSequence[slot, i] = 0;
                    if (PlayerEntity._players[slot] is { Health: > 0 } player)
                    {
                        var before = player.Speed;
                        player.Speed += (alreadyApplied ? Vector3.Zero : impulse) - _receiptImpulse[slot, i];
                        NetSelfSplash.RecordFeedback(slot, generation, life, eventId, _receiptImpulse[slot, i], impulse, before, player.Speed, alreadyApplied);
                    }
                    return true;
                }
            return false;
        }
        private static void RememberEvent(in CombatAckEntry ack, Vector3 predictedImpulse)
        {
            if (!ack.Accepted || (ack.Flags & CombatAckFlags.OutcomePresent) == 0 || ack.DamageSequence == 0) return;
            int slot = ack.VictimSlot;
            RememberAcceptedOutcome(ack);
            int at = _receiptNext[slot]++ % ReceiptCapacity;
            _receiptSequence[slot, at] = (ushort)ack.DamageSequence;
            _receiptFrame[slot, at] = NetSession.NetFrame;
            _receiptImpulse[slot, at] = predictedImpulse;
        }

        /// <summary>
        /// Forget what is outstanding for one slot, because the slot has
        /// changed hands or the room has.
        ///
        /// A prediction describes a hit on a particular player in a particular
        /// room. Kept across either, the worst of it is a lethal one holding
        /// the new occupant of that slot dead on this screen for the length of
        /// the hold -- a player who has just spawned into a fresh map, lying
        /// down because somebody else was shot before the rotation. The
        /// statistics go with it: they are per-match, like
        /// <c>NetDamage.ResetForRoomChange</c>'s tallies.
        /// </summary>
        public static void ForgetSlot(int slot)
        {
            if (slot < 0 || slot >= Slots)
            {
                return;
            }
            for (int i = 0; i < PendingCapacity; i++)
            {
                _pendingFrame[slot, i] = 0;
                _pendingDamage[slot, i] = 0;
                _pendingLethal[slot, i] = false;
                _pendingHeld[slot, i] = false;
                _pendingHeadshot[slot, i] = false;
                _pendingBeam[slot, i] = AltBeam;
                _pendingClaim[slot, i] = 0;
                _pendingShot[slot, i] = 0;
                _pendingSpent[slot, i] = false;
                _pendingSelf[slot, i] = false; _pendingDeferred[slot, i] = false;
            }
            if (slot == NetHooks.LocalSlot) ForgetDrain();
            _ackHealthValid[slot] = false;
            _pendingCount[slot] = 0;
            _pendingHead[slot] = 0;
            _shownHealth[slot] = 0;
            _predictedFrame[slot] = 0;
            _lastAuthorityHealth[slot] = 0;
            for (int i = 0; i < ReceiptCapacity; i++) _receiptSequence[slot, i] = 0;
            _receiptNext[slot] = 0;
            for (int i = 0; i < ReceiptCapacity; i++) _projectionAccepted[slot, i] = default;
            _projectionAcceptedNext[slot] = 0;
        }

        /// <summary>
        /// The authority has put <paramref name="slot"/> back on the map, so
        /// everything this machine predicted about their last life is spent.
        ///
        /// Two jobs at one moment. It counts a kill this machine showed that
        /// the authority never confirmed -- the hold expired, the next
        /// snapshot stood them up, and that is exactly what a mispredicted
        /// kill looks like from here. And it drops the outstanding debit,
        /// because a prediction about the life that just ended must not come
        /// off the health of the one that just started: without this, a
        /// player killed and respawned inside the hold window would come back
        /// with the last twenty points this machine had landed on them
        /// already taken off.
        /// </summary>
        public static void NoteRespawn(int slot)
        {
            if (slot < 0 || slot >= Slots)
            {
                return;
            }
            if (slot == NetSession.LocalSlot) ForgetPending();
            else ForgetSlot(slot);
        }

        /// <summary>
        /// The authority has reported <paramref name="slot"/> dead, so
        /// everything this machine predicted about that life is answered and
        /// the hold is over.
        ///
        /// Without it a predicted self-kill outlived its own confirmation: the
        /// authority's report of the death carries no attacker this machine
        /// can match (a fall names nobody), so nothing retired the pending
        /// lethal entry, and <see cref="HeldDead"/> went on refusing the
        /// respawn that came a moment later -- a player who died in the void
        /// and then lay there for the rest of the hold window.
        ///
        /// <b>It releases the hold and nothing else.</b> Clearing the slot
        /// outright would drop predictions that have not been answered yet,
        /// and they would leave the tally neither confirmed nor denied --
        /// <see cref="Predicted"/> would stop equalling
        /// <see cref="Confirmed"/> plus <see cref="Denied"/>, which is the one
        /// arithmetic that makes those numbers readable. They stay, to be
        /// confirmed or to age out; what they must not do is go on holding a
        /// body down that the authority has already agreed is down.
        /// <see cref="NoteRespawn"/> is what clears the slot, at the respawn,
        /// where the life really has ended.
        /// </summary>
        public static void NoteDeath(int slot)
        {
            if (slot < 0 || slot >= Slots)
            {
                return;
            }
            for (int i = 0; i < PendingCapacity; i++)
            {
                _pendingLethal[slot, i] = false;
                _pendingHeld[slot, i] = false;
            }
        }

        /// <summary>Everything outstanding, for a rotation into a new room.</summary>
        public static void ForgetPending()
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                ForgetSlot(slot);
            }
            ForgetDrain();
        }

        private static void ForgetDrain()
        {
            for (int i = 0; i < _healCount; i++) DrainExpired += _healAmount[(_healHead + i) % HealCapacity];
            _healCount = 0; _healHead = 0;
        }

        /// <summary>
        /// Check exact local tick eligibility before speculative GainHealth.
        /// Authority healing uses its payment transaction rather than this guard.
        /// </summary>
        public static bool CanPredictDrain(PlayerEntity healer, uint phase)
        {
            int local = NetHooks.LocalSlot;
            if (!Enabled || !Predicting || healer.Health <= 0
                || local < 0 || healer.SlotIndex != local) return false;
            EnsureLife(local);
            ReconcileDrain(local);
            ushort generation = NetPlayerLifecycle.Generation(local);
            ushort life = NetPlayerLifecycle.Get(local);
            if (generation == 0 || life == 0) return false;
            for (int i = 0; i < _healCount; i++)
            {
                int at = (_healHead + i) % HealCapacity;
                if (_healPhase[at] == phase && _healGeneration[at] == generation && _healLife[at] == life) return false;
            }
            // A snapshot can arrive before the local physical tick or its ACK.
            // Its matching health already includes the paid tick; add no credit.
            return !DrainIncluded(local, generation, life, phase);
        }

        /// <summary>
        /// Record actual locally granted healing and retain zero-gain tick identity
        /// so a full-health tick cannot heal again after intervening damage.
        /// </summary>
        public static void NoteDrain(PlayerEntity healer, int amount, uint phase, ushort claimId = 0)
        {
            if (amount < 0 || !CanPredictDrain(healer, phase)) return;
            int local = NetHooks.LocalSlot;
            ushort generation = NetPlayerLifecycle.Generation(local);
            ushort life = NetPlayerLifecycle.Get(local);
            if (_healCount == HealCapacity)
            {
                DrainExpired += _healAmount[_healHead];
                _healHead = (_healHead + 1) % HealCapacity;
                _healCount--;
            }
            int tail = (_healHead + _healCount) % HealCapacity;
            _healFrame[tail] = NetSession.NetFrame; _healPhase[tail] = phase;
            _healGeneration[tail] = generation; _healLife[tail] = life;
            _healClaim[tail] = claimId; _healAmount[tail] = amount;
            _healCount++;
            DrainPredicted += amount;
            NetShotDiagnostics.DrainCredit[NetShotDiagnostics.Bucket(BeamType.ShockCoil)] += amount;
        }

        /// <summary>Compatibility overload; live credits require an exact owner tick.</summary>
        public static void NoteDrain(PlayerEntity healer, int amount) { }

        private static bool DrainIncluded(int slot, ushort generation, ushort life, uint phase)
        {
            if ((uint)slot >= Slots || !NetSession.RemoteStateValid[slot]) return false;
            var state = NetSession.RemoteStates[slot];
            if (state.SlotGeneration != generation || state.LifeId != life) return false;
            uint distance = unchecked(state.ContinuousPaidTick - phase);
            if (distance >= 128) return false;
            return distance < 64 ? ((state.ContinuousPaidMaskLow >> (int)distance) & 1) != 0
                : ((state.ContinuousPaidMaskHigh >> (int)(distance - 64)) & 1) != 0;
        }

        private static void ReconcileDrain(int slot)
        {
            uint now = NetSession.NetFrame;
            for (int i = 0; i < _healCount; i++)
            {
                int at = (_healHead + i) % HealCapacity;
                if (_healAmount[at] == 0) continue;
                if (!NetPlayerLifecycle.Matches(slot, _healGeneration[at], _healLife[at])
                    || now - _healFrame[at] >= PendingFrames)
                {
                    DrainExpired += _healAmount[at]; _healAmount[at] = 0;
                }
                else if (DrainIncluded(slot, _healGeneration[at], _healLife[at], _healPhase[at]))
                {
                    DrainConfirmed += _healAmount[at]; _healAmount[at] = 0;
                }
            }
            while (_healCount > 0 && now - _healFrame[_healHead] >= PendingFrames)
            {
                _healHead = (_healHead + 1) % HealCapacity; _healCount--;
            }
        }

        /// <summary>Reject only the named healing claim; acceptance awaits same-state tick proof.</summary>
        public static void SettleDrain(ushort claimId, bool accepted)
        {
            if (claimId == 0) return;
            int local = NetHooks.LocalSlot;
            if ((uint)local >= Slots) return;
            EnsureLife(local);
            // A snapshot that already includes a tick takes precedence over a
            // repeated receipt. Otherwise rejection removes only its own credit.
            ReconcileDrain(local);
            if (accepted) return;
            for (int i = 0; i < _healCount; i++)
            {
                int at = (_healHead + i) % HealCapacity;
                if (_healClaim[at] != claimId || _healAmount[at] == 0) continue;
                DrainRejected += _healAmount[at]; _healAmount[at] = 0;
            }
        }

        /// <summary>
        /// What this machine has taken off <paramref name="slot"/> and not yet
        /// been told about, in points of health.
        /// </summary>
        // Evidence of the exact pending remote prediction debit, not an authority payment.
        internal static int FormPredictionDebit(int slot) => Predicting && slot != NetHooks.LocalSlot ? Debit(slot) : 0;

        private static int Debit(int slot)
        {
            EnsureLife(slot);
            if (!Enabled || slot < 0 || slot >= Slots || _pendingCount[slot] == 0)
            {
                return 0;
            }
            uint now = NetSession.NetFrame;
            int hold = HoldFrames;
            int debit = 0;
            for (int i = 0; i < _pendingCount[slot]; i++)
            {
                int at = (_pendingHead[slot] + i) % PendingCapacity;
                if (NetPlayerLifecycle.Matches(slot, _pendingGeneration[slot, at], _pendingLife[slot, at])
                    && now - _pendingFrame[slot, at] < (uint)hold)
                {
                    debit += _pendingDamage[slot, at];
                }
            }
            return debit;
        }

        /// <summary>
        /// The health to show for a puppet: the authority's number, less what
        /// this machine has already landed on them and not yet had confirmed.
        ///
        /// Never zero on its own account. Assigning zero health is not a
        /// death -- it skips the whole death path -- so a hold that ran the
        /// bar to the bottom would produce a player who is neither alive nor
        /// dead. A predicted kill goes through <c>TakeDamage</c> like any
        /// other and is held by <see cref="HeldDead"/> instead.
        /// </summary>
        private static PredictionHealthProjection.Result ProjectRemoteHealth(int slot, int health, ushort? sampleSequence)
        {
            Span<PredictionHealthProjection.Pending> pending = stackalloc PredictionHealthProjection.Pending[PendingCapacity];
            Span<PredictionHealthProjection.Accepted> accepted = stackalloc PredictionHealthProjection.Accepted[ReceiptCapacity];
            int count = 0, acceptedCount = 0;
            uint now = NetSession.NetFrame;
            int hold = HoldFrames;
            for (int i = 0; i < _pendingCount[slot]; i++)
            {
                int at = (_pendingHead[slot] + i) % PendingCapacity;
                if (!_pendingSelf[slot, at] && !_pendingSpent[slot, at] && _pendingDamage[slot, at] > 0
                    && now - _pendingFrame[slot, at] < (uint)hold
                    && NetPlayerLifecycle.Matches(slot, _pendingGeneration[slot, at], _pendingLife[slot, at]))
                    pending[count++] = _pendingProjection[slot, at] with { Damage = _pendingDamage[slot, at] };
            }
            for (int i = 0; i < ReceiptCapacity; i++)
                if (_projectionAccepted[slot, i].Sequence != 0)
                    accepted[acceptedCount++] = _projectionAccepted[slot, i];
            var state = NetSession.RemoteStates[slot];
            return PredictionHealthProjection.Project(health, sampleSequence ?? state.DamageEventId, state,
                pending[..count], accepted[..acceptedCount], sampleSequence.HasValue && state.Form.Phase != FormPhase.Unknown);
        }

        public static int HealthFor(int slot, int authorityHealth, ushort? sampleSequence = null)
        {
            EnsureLife(slot);
            if (!Enabled || slot < 0 || slot >= Slots)
            {
                return authorityHealth;
            }
            if (_ackHealthValid[slot])
            {
                ushort latest = sampleSequence ?? NetSession.RemoteStates[slot].DamageEventId;
                ushort ackSequence = (ushort)_ackHealth[slot].DamageSequence;
                if (latest == ackSequence || NetLifecycleTracker.Newer(latest, ackSequence))
                {
                    // Body and form samples travel independently. Retain the one
                    // bounded outcome until both have caught up; a fast body ACK
                    // must not expose an older slow turret-health sample again.
                    var form = NetSession.RemoteStates[slot].Form;
                    if (form.Phase != FormPhase.Unknown && (form.DamageEventId == ackSequence
                        || NetLifecycleTracker.Newer(form.DamageEventId, ackSequence))) _ackHealthValid[slot] = false;
                }
                else authorityHealth = authorityHealth > 0 ? Math.Max(1, (int)_ackHealth[slot].HealthAfter) : 0;
            }
            // Authority healing or respawn releases the local presentation floor.
            if (authorityHealth > _lastAuthorityHealth[slot] && _shownHealth[slot] > 0)
            {
                _shownHealth[slot] = 0;
                FloorLifted++;
            }
            if (_lastAuthorityHealth[slot] > authorityHealth && authorityHealth > 0)
            {
                _authorityDrop[slot] += _lastAuthorityHealth[slot] - authorityHealth;
            }
            _lastAuthorityHealth[slot] = authorityHealth;
            // Local health is already reduced by its predicted impact. Until
            // the exact receipt arrives, take the lower presentation/authority
            // value instead of subtracting that impact from a paid snapshot again.
            int debit = Debit(slot);
            int health = authorityHealth;
            if (slot != NetHooks.LocalSlot && debit > 0 && authorityHealth > 0)
            {
                var projection = ProjectRemoteHealth(slot, authorityHealth, sampleSequence);
                health = projection.Health;
                if (projection.Ambiguous) ProjectionAmbiguousSamples++;
                ProjectionOverlapPoints += projection.PossibleOverlap;
            }
            int owed = health;
            // Only local self-damage may hold the last shown floor. Remote
            // presentation recovers as each wrong or unanswered debit retires.
            if (slot == NetHooks.LocalSlot && authorityHealth > 0 && _shownHealth[slot] > 0
                && NetSession.NetFrame - _predictedFrame[slot] < (uint)HoldFrames)
            {
                health = Math.Min(health, _shownHealth[slot]);
                health = Math.Max(1, health);
            }
            // What the two bars actually say, sampled where both numbers are
            // in the same hand. "The client kills him and the server does not"
            // is a statement about this difference and nothing else, and
            // before this there was no line anywhere that printed it.
            //
            // Track independent debit, any local floor, and unexplained health gaps.
            if (authorityHealth > 0)
            {
                HealthSamples++;
                if (health < owed)
                {
                    FloorHeld++;
                    FloorHeldPoints += owed - health;
                    FloorWorst = Math.Max(FloorWorst, owed - health);
                }
                if (debit == 0 && health != authorityHealth)
                {
                    HealthDisagreed++;
                    int gap = authorityHealth - health;
                    if (gap > 0)
                    {
                        HealthUnderPoints += gap;
                        HealthUnderWorst = Math.Max(HealthUnderWorst, gap);
                    }
                    else
                    {
                        HealthOverPoints += -gap;
                        HealthOverWorst = Math.Max(HealthOverWorst, -gap);
                    }
                }
                DebitPoints += debit;
                DebitWorst = Math.Max(DebitWorst, debit);
            }
            _shownHealth[slot] = health;
            return health;
        }

        /// <summary>
        /// The client's health bar against the authority's, sampled on every
        /// snapshot applied to a remote player.
        ///
        /// <see cref="HealthDisagreed"/> is the number that matters: nothing
        /// outstanding and the two still differ. Everything else here is the
        /// mechanism working -- a debit is a hit this machine has landed and
        /// not had answered. Remote health has no persistent floor beyond those
        /// independently aged debits.
        /// </summary>
        public static long HealthSamples { get; private set; }
        public static long HealthDisagreed { get; private set; }
        /// <summary>
        /// Points the drawn bar sat <b>below</b> the authority's with nothing
        /// outstanding -- the direction that predicts kills the authority
        /// refuses -- and above it, which is the harmless one.
        /// </summary>
        public static long HealthUnderPoints { get; private set; }
        public static int HealthUnderWorst { get; private set; }
        public static long HealthOverPoints { get; private set; }
        public static int HealthOverWorst { get; private set; }
        public static long FloorHeld { get; private set; }
        public static long FloorHeldPoints { get; private set; }
        public static int FloorWorst { get; private set; }
        public static long DebitPoints { get; private set; }
        public static int DebitWorst { get; private set; }

        /// <summary>
        /// Points predicted against points the authority actually removed, per
        /// slot. The line that says whether a hit is being counted twice --
        /// see <see cref="_predictedPoints"/> for how to read it.
        /// </summary>
        public static string DescribeDamageLedger()
        {
            var text = new System.Text.StringBuilder();
            text.Append("damage ledger (predicted / authority removed):");
            bool any = false;
            for (int slot = 0; slot < Slots; slot++)
            {
                if (_predictedPoints[slot] == 0 && _authorityDrop[slot] == 0)
                {
                    continue;
                }
                any = true;
                string ratio = _authorityDrop[slot] > 0
                    ? $" x{_predictedPoints[slot] / (double)_authorityDrop[slot]:F2}"
                    : "";
                text.Append($" slot {slot} {_predictedPoints[slot]}/{_authorityDrop[slot]}{ratio};");
            }
            return any ? text.ToString() : "damage ledger: nothing predicted onto anybody";
        }

        /// <summary>One line for the report: the two bars, side by side.</summary>
        public static string DescribeHealth()
        {
            if (HealthSamples == 0)
            {
                return "health bars: nothing drawn for anybody else";
            }
            return $"health bars: {HealthSamples} sample(s); debit mean "
                + $"{DebitPoints / (double)HealthSamples:F2} worst {DebitWorst}; "
                + $"floor held {FloorHeld} ({FloorHeldPoints} point(s), worst {FloorWorst}), "
                + $"lifted {FloorLifted}; disagreed with nothing outstanding "
                + $"{HealthDisagreed} -- drawn low by {HealthUnderPoints} point(s) "
                + $"(worst {HealthUnderWorst}), high by {HealthOverPoints} "
                + $"(worst {HealthOverWorst})";
        }

        /// <summary>
        /// This machine's own health: the authority's number plus whatever its
        /// beam has drained since the authority last spoke.
        /// </summary>
        public static int LocalHealthFor(PlayerEntity player, int authorityHealth, ushort? sampleSequence = null)
        {
            EnsureLife(player.SlotIndex);
            ReconcileDrain(player.SlotIndex);
            if (!Enabled || authorityHealth <= 0)
            {
                return authorityHealth;
            }
            int credit = DrainCurrentPoints;
            authorityHealth = HealthFor(player.SlotIndex, authorityHealth, sampleSequence);
            // A self-kill this machine has already died is not undone by a
            // snapshot that has not heard about it. The branch in ApplyState
            // that spawns a player normally returns before this line while
            // HeldDead is true, so this is the belt to that brace: handing
            // back the authority's health here would stand a corpse up with
            // the death camera still running.
            if (player.Health <= 0 && HeldDead(NetHooks.LocalSlot))
            {
                return 0;
            }
            if (credit == 0)
            {
                return authorityHealth;
            }
            int max = player.HealthMax > 0 ? player.HealthMax : authorityHealth;
            // Floored at 1 for HealthFor's reason: an assignment is not a
            // death. A self-inflicted prediction *can* be lethal now, and the
            // line above is what keeps that one from coming through here.
            return Math.Clamp(authorityHealth + credit, 1, max);
        }

        /// <summary>
        /// Whether this machine has killed <paramref name="slot"/> and is
        /// still waiting to hear whether it was right.
        ///
        /// While this is true the snapshot is not allowed to put that player
        /// back on the map: the authority's copy of them is a round trip
        /// behind and still walking around, and respawning the corpse every
        /// snapshot until the kill is confirmed is worse than either answer.
        /// It stops being true the moment the kill is confirmed -- or, if it
        /// never is, when the hold expires and the next snapshot spawns them
        /// as it always did.
        /// </summary>
        public static bool HeldDead(int slot)
        {
            EnsureLife(slot);
            if (!Enabled || slot < 0 || slot >= Slots)
            {
                return false;
            }
            // Somebody else is only held down when death is predicted for
            // somebody else, which it is not by default. This machine's own
            // player always is: a self-kill is predicted whatever DeathEnabled
            // says, and a snapshot that has not heard about it yet would stand
            // the body straight back up -- the resurrection this whole switch
            // exists to stop, on the one player who is looking at it.
            if (slot != NetHooks.LocalSlot)
            {
                return false;
            }
            uint now = NetSession.NetFrame;
            int hold = HoldFrames;
            for (int i = 0; i < _pendingCount[slot]; i++)
            {
                int at = (_pendingHead[slot] + i) % PendingCapacity;
                if (_pendingLethal[slot, at] && now - _pendingFrame[slot, at] < (uint)hold)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Once a simulation step: age the outstanding predictions out and
        /// count the mark down. In the step and not in the draw, because both
        /// are measured in frames and a picture with no step behind it must
        /// not advance either.
        /// </summary>
        public static void Tick()
        {
            NetDamage.FlushSelfFeedback();
            if (_markerTimer > 0)
            {
                _markerTimer--;
            }
            if (!NetSession.Active)
            {
                return;
            }
            uint now = NetSession.NetFrame;
            for (int slot = 0; slot < Slots; slot++)
            {
                while (_pendingCount[slot] > 0)
                {
                    uint frame = _pendingFrame[slot, _pendingHead[slot]];
                    // Unsigned, so a counter that has been reset underneath us
                    // reads as an enormous age rather than a negative one --
                    // which is the right answer either way: nothing pending
                    // from before a reset can still be confirmed.
                    if (now - frame < PendingFrames)
                    {
                        break;
                    }
                    RetireHead(slot, confirmed: false);
                }
            }
            // Heal credits use a fixed bounded deadline and exact owner snapshot proof.
            if ((uint)NetHooks.LocalSlot < Slots) ReconcileDrain(NetHooks.LocalSlot);
        }

        /// <summary>
        /// File a prediction. Returns where it landed in the ring, so the
        /// caller can stamp the claim id onto it once the claim has been
        /// declared -- the claim is what retires it exactly later.
        /// </summary>
        private static int Push(int slot, uint frame, int damage, bool lethal,
            bool headshot, BeamType beam, bool self)
        {
            EnsureLife(slot);
            if (slot < 0 || slot >= Slots)
            {
                return -1;
            }
            if (_pendingCount[slot] == PendingCapacity)
            {
                RetireHead(slot, confirmed: false);
            }
            _predictedFrame[slot] = frame;
            int tail = (_pendingHead[slot] + _pendingCount[slot]) % PendingCapacity;
            _pendingLife[slot, tail] = NetPlayerLifecycle.Get(slot);
            _pendingGeneration[slot, tail] = NetPlayerLifecycle.Generation(slot);
            _pendingFrame[slot, tail] = frame;
            _pendingDamage[slot, tail] = Math.Max(0, damage);
            _pendingLethal[slot, tail] = lethal;
            _pendingHeadshot[slot, tail] = headshot;
            _pendingBeam[slot, tail] = (byte)Bucket(beam);
            _pendingClaim[slot, tail] = 0;
            _pendingShot[slot, tail] = 0;
            _pendingSpent[slot, tail] = false;
            _pendingSelf[slot, tail] = self; _pendingDeferred[slot, tail] = false;
            _pendingCount[slot]++;
            if (!self)
            {
                int bucket = Bucket(beam);
                _beamPredicted[bucket]++;
                _beamDamage[bucket] += Math.Max(0, damage);
                if (lethal)
                {
                    _beamLethal[bucket]++;
                }
            }
            return tail;
        }

        /// <summary>
        /// Take the oldest outstanding prediction for a slot off the books and
        /// file it under the weapon it was made with. One place, so the
        /// per-weapon tally cannot drift from the aggregate one.
        /// </summary>
        private static int RetireHead(int slot, bool confirmed)
        {
            int head = _pendingHead[slot];
            ResolveHeld(slot, head, confirmed);
            bool spent = _pendingSpent[slot, head] || _pendingSelf[slot, head];
            int bucket = _pendingBeam[slot, head];
            if (!spent && bucket >= 0 && bucket < BeamBuckets)
            {
                if (confirmed)
                {
                    _beamConfirmed[bucket]++;
                }
                else
                {
                    _beamDenied[bucket]++;
                }
            }
            bool answered = _pendingSpent[slot, head];
            // Not a self-hit: SelfPredicted is counted apart from Predicted,
            // so counting its timeouts in Denied breaks the one arithmetic
            // that makes these numbers readable -- Predicted == Confirmed +
            // Denied. A run with three unanswered rocket-jump splashes read
            // "13 predicted, 12 confirmed, 2 denied".
            if (!answered && !confirmed && !_pendingSelf[slot, head])
            {
                Denied++;
            }
            _pendingClaim[slot, head] = 0;
            _pendingSpent[slot, head] = false;
            _pendingSelf[slot, head] = false; _pendingDeferred[slot, head] = false;
            _pendingHead[slot] = (head + 1) % PendingCapacity;
            _pendingCount[slot]--;
            return answered ? -1 : head;
        }

        /// <summary>
        /// Historical accepted outcomes used for presentation until a snapshot
        /// reaches their damage sequence. They never author death or resurrection.
        /// </summary>
        private static readonly CombatAckEntry[] _ackHealth = new CombatAckEntry[Slots];
        private static readonly bool[] _ackHealthValid = new bool[Slots];
        public static long CombatAcks, DamageCorrections, HealthCorrections, HeadshotCorrections;

        internal static int TurretHealthFor(int slot, int health)
            => TurretHealthFor(slot, health, (uint)slot < Slots ? NetSession.RemoteStates[slot].DamageEventId : (ushort)0);

        internal static int TurretHealthFor(int slot, int health, ushort sampleSequence)
        {
            EnsureLife(slot);
            if ((uint)slot >= Slots || !_ackHealthValid[slot]) return health;
            ushort sequence = (ushort)_ackHealth[slot].DamageSequence;
            return sampleSequence == sequence || NetLifecycleTracker.Newer(sampleSequence, sequence)
                ? health : _ackHealth[slot].HalfturretHealthAfter;
        }

        internal static ushort TurretEvidenceSequenceFor(int slot, ushort sampleSequence)
        {
            if ((uint)slot >= Slots || !_ackHealthValid[slot]) return sampleSequence;
            ushort ack = (ushort)_ackHealth[slot].DamageSequence;
            return NetLifecycleTracker.Newer(ack, sampleSequence) ? ack : sampleSequence;
        }

        // Opt-in evidence only: copy the retained receipt without changing its
        // lifetime or reconciliation state. Bind it to the sampled victim life.
        internal static string? FormAckSource(int slot, ushort generation, ushort life)
        {
            if (!NetCampaignEvidence.Enabled || (uint)slot >= Slots || !_ackHealthValid[slot]) return null;
            var ack = _ackHealth[slot];
            if (ack.VictimSlot != slot || ack.VictimGeneration != generation || ack.VictimLife != life) return null;
            Span<byte> bytes = stackalloc byte[CombatAckEntry.Size]; ack.Write(bytes);
            return Convert.ToHexString(bytes);
        }

        /// <summary>Called only after receipt identity validation. An exact paid
        /// event remains unavailable to other predictions even if its own
        /// prediction expired. Retained independently of consumable feedback and
        /// age: fresh predictions can start while snapshots are still stale.</summary>
        internal static void RememberAcceptedOutcome(in CombatAckEntry ack)
        {
            int slot = ack.VictimSlot;
            if ((uint)slot >= Slots || !ack.Accepted || (ack.Flags & CombatAckFlags.OutcomePresent) == 0
                || ack.DamageSequence == 0 || !NetPlayerLifecycle.Matches(slot, ack.VictimGeneration, ack.VictimLife)) return;
            EnsureLife(slot);
            ushort sequence = (ushort)ack.DamageSequence;
            for (int i = 0; i < ReceiptCapacity; i++)
                if (_projectionAccepted[slot, i].Sequence == sequence) return;
            int at = _projectionAcceptedNext[slot];
            _projectionAcceptedNext[slot] = (at + 1) % ReceiptCapacity;
            _projectionAccepted[slot, at] = new(sequence, ack.DamageApplied);
        }

        /// <summary>Retire the exact named prediction and reconcile its presentation outcome.</summary>
        public static bool ApplyCombatAck(in CombatAckEntry ack, int latencyFrames)
        {
            int slot = ack.VictimSlot;
            if ((uint)slot >= Slots || !NetPlayerLifecycle.Matches(slot, ack.VictimGeneration, ack.VictimLife)) return false;
            EnsureLife(slot);
            for (int i = 0; i < _pendingCount[slot]; i++)
            {
                int at = (_pendingHead[slot] + i) % PendingCapacity;
                if (ack.ClaimId == 0 || _pendingClaim[slot, at] != ack.ClaimId
                    || _pendingShot[slot, at] != ack.ShotId || _pendingSpent[slot, at]) continue;
                int predicted = _pendingDamage[slot, at];
                bool head = _pendingHeadshot[slot, at];
                int healthBefore = PlayerEntity._players[slot]?.Health ?? 0;
                CombatAcks++;
                if (predicted != ack.DamageApplied) DamageCorrections++;
                bool authorityHeadshot = (ack.Flags & CombatAckFlags.Headshot) != 0;
                if (head != authorityHeadshot) HeadshotCorrections++;
                if (ack.Accepted)
                {
                    if (head && authorityHeadshot) HeadshotsAgreed++;
                    else if (head) HeadshotsDowngraded++;
                    else if (authorityHeadshot) HeadshotsUpgraded++;
                }
                bool wasSelf = _pendingSelf[slot, at], wasDeferred = _pendingDeferred[slot, at];
                int selfFloor = _shownHealth[slot];
                ResolveHeld(slot, at, ack.Accepted && (ack.Flags & CombatAckFlags.Lethal) != 0);
                RememberEvent(ack, _pendingSelf[slot, at] ? _pendingImpulse[slot, at] : Vector3.Zero);
                Settle(slot, ack.ClaimId, ack.Accepted);
                // Exact acknowledgement retires this debit immediately. Other
                // outstanding claims retain their own independent debits.
                _pendingDamage[slot, at] = 0;
                _pendingLethal[slot, at] = _pendingHeadshot[slot, at] = false;
                _shownHealth[slot] = wasSelf && HasSelfClaims() && selfFloor > 0
                    ? wasDeferred ? selfFloor : Math.Max(1, selfFloor + predicted - (ack.Accepted ? ack.DamageApplied : 0)) : 0;
                if (ack.Accepted && (ack.Flags & CombatAckFlags.OutcomePresent) != 0)
                {
                    ushort sequence = (ushort)ack.DamageSequence;
                    ushort snapshot = NetSession.RemoteStates[slot].DamageEventId;
                    if (NetLifecycleTracker.Newer(sequence, snapshot)
                        && (!_ackHealthValid[slot] || NetLifecycleTracker.Newer(sequence, (ushort)_ackHealth[slot].DamageSequence)))
                    { _ackHealth[slot] = ack; _ackHealthValid[slot] = true; }
                }
                var player = PlayerEntity._players[slot];
                if (player != null && player.Health > 0)
                {
                    if (player.Hunter != Hunter.Weavel || player.OwningScene.PlayerReplication.CanReconcileFormHealth(player))
                        player.Health = Math.Max(1, HealthFor(slot, NetSession.RemoteStates[slot].Health));
                    if (player.IsAltForm && player.Halfturret != null && _ackHealthValid[slot]
                        && player.OwningScene.PlayerReplication.CanReconcileTurretHealth(player)
                        && ((ack.Flags & CombatAckFlags.HalfturretAffected) != 0 || NetSession.RemoteStates[slot].HalfturretActive))
                    {
                        player.Halfturret.Health = TurretHealthFor(slot, player.Halfturret.Health);
                        if (player.Halfturret.Health > 0) player.ModRestoreHalfturretFlag();
                        else player.OnHalfturretDied();
                    }
                }
                int healthCorrection = healthBefore > 0 && player != null ? player.Health - healthBefore : 0;
                if (healthCorrection != 0) HealthCorrections++;
                byte weapon = _pendingBeam[slot, at];
                Telemetry.ProductionTelemetry.Emit(new(Telemetry.TelemetryEventType.CombatAck, NetSession.NetFrame,
                    Player: (byte)NetSession.LocalSlot, Victim: ack.VictimSlot, Weapon: weapon,
                    Id: ack.ClaimId, Result: ack.Result, Flags: (int)ack.Flags,
                    A: latencyFrames * (1000.0 / 60), B: ack.DamageApplied - predicted,
                    C: healthCorrection, D: head == ((ack.Flags & CombatAckFlags.Headshot) != 0) ? 0 : 1));
                CombatStudyReports.Record(ack, latencyFrames, ack.DamageApplied - predicted,
                    healthCorrection, head != ((ack.Flags & CombatAckFlags.Headshot) != 0), weapon);
                return true;
            }
            return false;
        }

        /// <summary>Settle a named claim independently of visible health or prediction enablement.</summary>
        public static void Settle(int slot, ushort claimId, bool confirmed)
        {
            EnsureLife(slot);
            if (slot < 0 || slot >= Slots || claimId == 0
                || _pendingCount[slot] == 0)
            {
                return;
            }
            for (int i = 0; i < _pendingCount[slot]; i++)
            {
                int at = (_pendingHead[slot] + i) % PendingCapacity;
                if (_pendingClaim[slot, at] != claimId)
                {
                    continue;
                }
                ResolveHeld(slot, at, confirmed);
                int bucket = _pendingSelf[slot, at] ? -1 : _pendingBeam[slot, at];
                if (bucket >= 0 && bucket < BeamBuckets)
                {
                    if (confirmed)
                    {
                        _beamConfirmed[bucket]++;
                    }
                    else
                    {
                        _beamDenied[bucket]++;
                    }
                }
                if (confirmed)
                {
                    if (_pendingSelf[slot, at]) SelfConfirmed++;
                    else Confirmed++;
                }
                else
                {
                    Denied++;
                    if (_pendingLethal[slot, at])
                    {
                        DeathsUndone++;
                        if (bucket >= 0 && bucket < BeamBuckets)
                        {
                            _beamUndone[bucket]++;
                        }
                    }
                    // A refused prediction is one whose bar should spring back
                    // up, so the floor under the drawn health goes with it.
                    _shownHealth[slot] = 0;
                }
                // Answered, so nothing may count it again -- but marked, not
                // removed: the entries in front of it are still outstanding
                // and the ones behind it are still owed answers.
                _pendingClaim[slot, at] = 0;
                _pendingSpent[slot, at] = true;
                // A terminal ACK retires the named debit. Its historical
                // health bridges only the exact event/snapshot race above.
                _pendingDamage[slot, at] = 0;
                _pendingLethal[slot, at] = false;
                _pendingHeadshot[slot, at] = false;
                while (_pendingCount[slot] > 0 && _pendingSpent[slot, _pendingHead[slot]]
                    && _pendingDamage[slot, _pendingHead[slot]] == 0)
                {
                    _pendingSpent[slot, _pendingHead[slot]] = false;
                    _pendingHead[slot] = (_pendingHead[slot] + 1) % PendingCapacity;
                    _pendingCount[slot]--;
                }
                return;
            }
        }

        /// <summary>
        /// Stamp the claim a prediction was declared under onto it.
        /// </summary>
        private static void StampClaim(int slot, int at, ushort claimId)
        {
            if (claimId != 0 && slot >= 0 && slot < Slots && at >= 0 && at < PendingCapacity)
            {
                _pendingClaim[slot, at] = claimId;
            }
        }

        /// <summary>One line for the harness reports and the diagnostics screen.</summary>
        public static string Describe()
        {
            if (!Enabled)
            {
                return "hit prediction: off";
            }
            if (Predicted == 0)
            {
                // Self-hits still say so: a run that landed nothing on
                // anybody else can still have rocket-jumped and fallen into
                // the void, and those are predictions too. Every self-kill is
                // a self-hit, so this one test covers both.
                string own = SelfPredicted > 0
                    ? $", {SelfPredicted} self-hits predicted "
                        + $"({SelfConfirmed} confirmed, {SelfDeathsPredicted} of them lethal)"
                    : "";
                return "hit prediction: on, nothing predicted here "
                    + $"({Unpredicted} hits arrived from the authority)" + own;
            }
            double agreed = Confirmed * 100.0 / Predicted;
            string deaths = DeathEnabled
                ? $"{DeathsPredicted} kills predicted, {DeathsUndone} undone"
                : $"{LethalHeld} lethal hits held ({LethalConfirmed} confirmed, {LethalDenied} denied)";
            if (SelfDeathsPredicted > 0)
            {
                deaths += $", {SelfDeathsPredicted} self-kills predicted";
            }
            string drain = DrainPredicted > 0 ? $", {DrainPredicted} health drained ahead" : "";
            string self = SelfPredicted > 0
                ? $", {SelfPredicted} self-hits predicted ({SelfConfirmed} confirmed)"
                : "";
            return $"hit prediction: {Predicted} predicted, {Confirmed} confirmed "
                + $"({agreed:F1}%), {Denied} denied, {Unpredicted} unpredicted, "
                + deaths + drain + self;
        }

        /// <summary>
        /// The tally a weapon at a time, which is the only form of it that can
        /// answer "the prediction is wrong with X".
        ///
        /// One line per weapon that predicted anything, with the damage this
        /// machine took off with it. A weapon whose denied share stands out
        /// from the rest is a weapon whose damage this machine and the
        /// authority compute differently -- the charge tier, a powerup one of
        /// them has and the other does not, a splash one of them resolved as a
        /// direct hit -- and that is invisible in the aggregate line, which is
        /// dominated by whatever was fired most.
        /// </summary>
        public static string DescribeByWeapon()
        {
            var text = new System.Text.StringBuilder();
            text.Append("hit prediction by weapon:");
            bool any = false;
            for (int i = 0; i < BeamBuckets; i++)
            {
                if (_beamPredicted[i] == 0)
                {
                    continue;
                }
                any = true;
                long answered = _beamConfirmed[i] + _beamDenied[i];
                string rate = answered > 0
                    ? $"{_beamConfirmed[i] * 100.0 / answered:F0}%"
                    : "unanswered";
                text.Append($"\n  {BucketName(i),-13} {_beamPredicted[i],5} predicted, "
                    + $"{_beamConfirmed[i],5} confirmed ({rate}), {_beamDenied[i],5} denied, "
                    + $"{_beamDamage[i],6} damage");
                if (_beamLethal[i] > 0 || _beamUndone[i] > 0)
                {
                    text.Append($", {_beamLethal[i]} kills ({_beamUndone[i]} undone)");
                }
            }
            if (!any)
            {
                return "hit prediction by weapon: nothing predicted here";
            }
            return text.ToString();
        }

        /// <summary>
        /// The headshot line, which is a different claim from the one above
        /// and reads as a pass while that one does.
        ///
        /// A hit whose flags disagree is `confirmed` to everything else here:
        /// the authority says a hit landed, the prediction is retired, the
        /// percentage is unmoved. What the shooter saw was an instant kill and
        /// what they got was a body shot, and this is the only line that says
        /// so.
        /// </summary>
        public static string DescribeHeadshots()
        {
            long answered = HeadshotsAgreed + HeadshotsDowngraded;
            if (HeadshotsPredicted == 0 && HeadshotsUpgraded == 0)
            {
                return "headshots: none predicted here";
            }
            string text = $"headshots: {HeadshotsPredicted} predicted";
            if (answered > 0)
            {
                text += $", {HeadshotsAgreed} agreed by the authority "
                    + $"({HeadshotsAgreed * 100.0 / answered:F1}%), "
                    + $"{HeadshotsDowngraded} downgraded to body shots";
            }
            else
            {
                text += ", none answered yet";
            }
            if (HeadshotsUpgraded > 0)
            {
                text += $", {HeadshotsUpgraded} the authority called a headshot and this machine did not";
            }
            return text;
        }
    }
}
