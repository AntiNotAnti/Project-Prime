using System;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.NetTest;

internal static class ReplayWorldCompatibilityTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Fingerprints frozen from shipped recordings, rather than recomputed
        // with today's field reflection. Keep these when the next layout changes.
        (string contract, string build)[] historical = [
            ("C2BCA9D8B8AA7FAA1EF366D7D3C45DE5A81597A4BC7ECF77DEBA46B4814BD38D", "local+566406796d687da1b25e8238c57773e997ddc1a5"),
            ("AE73AED7F4914368B7C9E85CA7F6D42C0D5912045A0679FBA76B08B6B1D38D66", "local+a96ab5b65805ec3531e9d0c64dc9fb04710020b5"),
            ("E912D88F0C61E2A46425C60EF262ECBDE54CF5B65EB9EAE31B9BF5C392D509F2", "0.1.16+55e78f424978b6b9fa6e7da7368384db3854ea40"),
            ("AA27DAF18BCF432B06C5E01B31FAEECE4CE6BC0B07320B86E3808BEFE2085B20", "0.1.17+3cac4dcaf10ce5a19c88ca510619cc9f8677bac8"),
            ("8415530C276FD2BD8136C66F456451EBF6877D531C0BCC6AC1D31E187D629EF5", "0.1.20+d375d9c147da7cd49488d0e956be7fe5eeb4f45b"),
            ("0C38317D9D737B28C2B38AF4B0CB95030DE680732C2283D310F0270659B35126", "0.1.23+0e39c9d59e5cd660a1038fc6216a47e91c407c20"),
            ("F217FB8D58EE135065578D7642349EBC356E5E771F883EDAFE2D000CF746BDDF", "0.1.28+7ee42326906310862608ddf49339a619401fb694"),
            ("F82EBD7690FD586BF518327581EAA435574651BCB789EA128A278EF2AC8DB3D6", "0.1.32+2bbf161aa7c48f8da84576056d38058c83043aa7"),
            ("6913713B22015A40A23B32E4976318D47D4D2ED19F4804E0F8C1C96DD025C254", "0.1.33+cb04e43ef5fcf92f27943262df76ab3b6199deaa"),
            ("455910DAF1D71841346B1B2692A3ACC3A4B742660CC34A485CCFF66999C2E273", "0.1.34+f0e01e09e08467974b8a5ea359d690ebe0a7e3a4"),
            ("6BA032575224B8B54015F73287A77F624D38C9B0854C4B54DBB9FEBE948D2937", "0.1.43+d306068381dfffc9fb82fef5d47fe3b93a07f91b"),
            ("C83CB9EB96D1107EA81CADE65C0ACD9CFF42B2AE161082B676CE1504F6B637E2", "local+829d67b0a16e670ecf8bef3424af194538176f06"),
            ("0B6BC004AAE8FB1ADA0AAC8DBD708EEAFF7CABE555B8B265120118FF46F177D1", "main-eab6f234+eab6f2345bf9e9b0aef8a950619de7e8fc92ced6")
        ];
        foreach (var (contract, build) in historical)
            check(ReplayWorldCheckpoint.SupportsContract(contract, build), "released world layout " + build);
        check(!ReplayWorldCheckpoint.SupportsContract(new string('A', 64), historical[0].build), "unknown saved-world layout remains rejected");
        check(!ReplayWorldCheckpoint.SupportsContract(historical[2].contract, "0.9.99"), "legacy assembly-version fingerprint stays fenced");
        var old = ReplayWorldLayout.Find(historical[1].contract, historical[1].build)!;
        var recent = ReplayWorldLayout.Find(historical[^1].contract, historical[^1].build)!;
        check(old.ObjectTypes[53] == typeof(PlayerReplicationBridge) && recent.ObjectTypes[59] == typeof(PlayerReplicationBridge),
            "historical object IDs retain their original table when new types shift indices");
        var input = new PlayerEntity.PlayerInput(); var touch = input.MorphTouch;
        // Protocol-17 PlayerInput: two deltas, capture flag, two click coords,
        // stylus flag and input flag. No MorphTouch graph reference existed.
        using (var reader = Reader("0000A03F000020C00100004842000096420101"))
        {
            old.ReadFields(reader, input, (_, _) => throw new Exception("Unexpected graph reference"), new ReplayReplicaState());
            check(input.MouseDeltaX == 1.25f && input.MouseDeltaY == -2.5f && input.ClickX == 50 && input.ClickY == 75
                && input.StylusWeaponMenuDown && input.HasInput && ReferenceEquals(input.MorphTouch, touch)
                && reader.BaseStream.Position == reader.BaseStream.Length, "old positional input fields restore without overwriting new touch defaults");
        }
        using (var reader = Reader("090000000000000007000200000003000400D2040000"))
        {
            var key = (ShotKey)old.ReadValue(reader, typeof(ShotKey).ToString(), (_, _) => null)!;
            check(key.AuthorityEpoch == 9 && key.MatchId == 7 && key.ShooterSlot == 2 && key.Generation == 3
                && key.LifeId == 4 && key.ShotId == 1234 && reader.BaseStream.Position == reader.BaseStream.Length,
                "historical LaunchFrame migrates to ShotId without losing shot identity");
        }
        using (var reader = Reader("1300000000000000140000000000000001"))
        {
            var clock = (ContinuousWeaponPhase.Clock)old.ReadValue(reader, typeof(ContinuousWeaponPhase.Clock).ToString(), (_, _) => null)!;
            check(clock.Phase == 19 && clock.SceneFrame == 20 && clock.Valid && clock.SourceTick == 0 && !clock.Explicit,
                "historical struct field order retains firing phase and defaults new tick fields");
        }
        using (var reader = Reader("0000000000000000000000000000000000000000000000000000000000000000"))
            check(old.ReadValue(reader, "MphRead.Mods.Network.PressHistoryBuffer", (_, _) => null) == null
                && reader.BaseStream.Position == 32, "retired input history is consumed at its original width");
        bool truncated = false;
        try { using var reader = Reader("00"); old.ReadValue(reader, typeof(ShotKey).ToString(), (_, _) => null); }
        catch (EndOfStreamException) { truncated = true; }
        check(truncated, "truncated historical fields remain rejected");
    }
    private static BinaryReader Reader(string hex) => new(new MemoryStream(Convert.FromHexString(hex), writable: false));
}
