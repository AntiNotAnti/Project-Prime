using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Text;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

internal static class PlayerNameChatTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
        byte[] bytes = new byte[PlayerNameCodec.MaxWireBytes];
        foreach (string name in new[] { "Player", "ABCDEFGHIJKLMNOPQRSTUVWX", new string('あ', 24),
            "JÄRRETT™", "ハンター", "メトロイド", "「PRIME」", "PRIME∞", "e\u0301" })
        {
            Check(PlayerNameCodec.TryEncode(name, bytes, out int length), "encode " + name);
            Check(PlayerNameCodec.Decode(bytes) == PlayerNameCodec.Normalize(name), "padded round trip " + name);
            Check(PlayerNameCodec.TryDecode(bytes.AsSpan(0, length), out string decoded, false)
                && decoded == PlayerNameCodec.Normalize(name), "identify round trip " + name);
        }
        foreach (string glyph in MphGlyphMap.Extended.Where(c => c.Length == 1 && c != " ").Distinct())
        {
            Check(PlayerNameCodec.TryEncode(glyph, bytes, out _), "table encode " + glyph);
            Check(PlayerNameCodec.Decode(bytes) == PlayerNameCodec.Normalize(glyph), "table round trip " + glyph);
        }
        foreach (string name in new[] { "JÄRRETT™", "ハンター", "「PRIME」", "PRIME∞" })
        {
            string canonical = PlayerNameCodec.Normalize(name);
            string native = PlayerNameCodec.ToNative(canonical);
            var expected = new List<int>();
            foreach (Rune rune in canonical.EnumerateRunes())
            {
                Check(PlayerNameCodec.TryMapUnicodeToGlyph(rune, out ushort code),
                    "native glyph source maps " + name);
                expected.Add(code);
            }

            var actual = new List<int>();
            int steps = 0;
            for (int i = 0; i < native.Length; i++)
            {
                Check(PlayerNameLayout.TryReadNativeGlyph(native, ref i, native.Length,
                    out int code, out _), "native glyph read " + name);
                actual.Add(code);
                Check(++steps <= PlayerNameCodec.MaxGlyphs,
                    "native glyph walk terminates " + name);
            }
            Check(actual.SequenceEqual(expected), "native glyph round trip " + name);
        }
        string truncatedNative = PlayerNameCodec.ToNative("™");
        int truncatedIndex = 0;
        Check(PlayerNameLayout.TryReadNativeGlyph(truncatedNative, ref truncatedIndex, 1,
            out int truncatedCode, out _)
            && truncatedCode == '?' && truncatedIndex == 0,
            "truncated native glyph falls back without crossing line boundary");
        foreach (string invalid in new[] { "", " ", new string('A', 25), "A\0B", "A\nB", "A\tB", "😀", "A\u200bB", "A\u202eB", "\ud800", "\ue000" })
            Check(!PlayerNameCodec.TryEncode(invalid, bytes, out _), "reject " + invalid);
        foreach (byte[] bad in new[] { new byte[] { 0xc4 }, new byte[] { 0xc4, 0x41 }, new byte[] { 0xc0, 0xa0 },
            new byte[] { 65, 0, 66 }, new byte[] { 0xff, 0xff }, Enumerable.Repeat((byte)65, 25).ToArray() })
            Check(!PlayerNameCodec.TryDecode(bad, out _), "malformed bytes");
        Check(PlayerNameCodec.Clamp(new string('あ', 25)).Length == 24, "clamp");
        var roster = RosterPacket.Create(); roster.Count = 8;
        for (int i = 0; i < 8; i++) { roster.Slots[i] = (byte)i; roster.Names[i] = "ハンター" + i; roster.Generations[i] = (ushort)(i + 1); roster.Teams[i] = -1; }
        byte[] wire = new byte[RosterPacket.Size]; roster.Write(wire);
        Check(RosterPacket.TryRead(wire, out var decodedRoster), "eight player roster");
        for (int i = 0; i < 8; i++) Check(decodedRoster.Names[i] == roster.Names[i] && decodedRoster.Generations[i] == i + 1, "roster offsets");
        var chat = new ChatPacket { Name = "JÄRRETT™", Text = "hello", Kind = ChatPacket.KindSay };
        wire = new byte[ChatPacket.Size]; chat.Write(wire); Check(ChatPacket.Read(wire).Name == chat.Name, "chat name");
        foreach (int protocol in new[] { 24, 25, 26 })
        {
            int header = protocol == 26 ? 18 : 17, entry = protocol == 26 ? 27 : 25;
            byte[] old = new byte[1 + header + 8 * entry]; old[0] = (byte)PacketType.Roster; old[1] = 1;
            old[1 + header + 5] = (byte)'A'; old[1 + header + 21] = 7; old[1 + header + 23] = 255;
            var converted = ReplayIdentityCompatibility.Convert(old, protocol);
            Check(RosterPacket.TryRead(converted[1..], out var legacy) && legacy.Names[0] == "A" && legacy.Generations[0] == 7, "legacy roster " + protocol);
            string replayPath = Path.Combine(Path.GetTempPath(), $"prime-names-{Guid.NewGuid():N}.ppdemo");
            try
            {
                var match = new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "", Mode = (byte)GameMode.Battle };
                byte[] matchBytes = new byte[1 + MatchStatePacket.Size]; matchBytes[0] = (byte)PacketType.MatchState; match.Write(matchBytes.AsSpan(1));
                using (var writer = new DemoWriter(replayPath))
                { writer.WriteRecord(0, matchBytes); writer.WriteRecord(0, old); writer.WriteRecord(30, old); }
                using (var file = File.OpenWrite(replayPath)) { file.Position = DemoFile.Magic.Length + 1; file.WriteByte((byte)protocol); }
                var host = new PassiveReplaySessionHost();
                using var playback = new ReplayPlaybackSession(host);
                Check(playback.Join(replayPath), "open protocol " + protocol + " replay: " + playback.LastError);
                Check(host.State.Occupant(0).Name == "A", "replay name " + protocol);
            }
            finally { File.Delete(replayPath); }
            old = new byte[1 + 2 + 16 + ChatPacket.MaxTextBytes]; old[0] = (byte)PacketType.Chat;
            old[3] = (byte)'A'; old[19] = (byte)'x';
            var legacyChat = ChatPacket.Read(ReplayIdentityCompatibility.Convert(old, protocol)[1..]);
            Check(legacyChat.Name == "A" && legacyChat.Text == "x", "legacy chat " + protocol);
        }
        var report = PostMatchReportPacket.Create(); report.Count = 8;
        for (int i = 0; i < 8; i++) { report.Slots[i] = (byte)i; report.Names[i] = "ハンター" + i; report.DamageTaken[i] = (uint)(500 + i); }
        wire = new byte[PostMatchReportPacket.Size]; report.Write(wire);
        Check(PostMatchReportPacket.TryRead(wire, out var readReport) && readReport.Names[7] == "ハンター7" && readReport.DamageTaken[7] == 507, "report name/stats offsets");
        var vote = new VoteStatePacket { Proposer = "「PRIME」", RoomKey = "MP1 SANCTORUS", Yes = 4, Seconds = 42 };
        wire = new byte[VoteStatePacket.Size]; vote.Write(wire);
        Check(VoteStatePacket.Read(wire).Proposer == vote.Proposer && VoteStatePacket.Read(wire).Seconds == 42, "vote name/tally offsets");
        var replica = new ReplayReplicaState();
        wire = new byte[1 + ChatPacket.Size]; wire[0] = (byte)PacketType.Chat; chat.Write(wire.AsSpan(1));
        replica.Accept(wire, 5);
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(replica.CaptureCheckpoint());
        Check(restored.ChatLines.Count == 1 && restored.ChatLines[0].Packet.Name == chat.Name && restored.ChatLines[0].Frame == 5, "replay chat checkpoint");
        GameState.Mode = GameMode.Battle;
        var visible = new List<(ChatLine Line, float Alpha)>();
        ChatBox.Clear(); ChatBox.Open(false); ChatBox.ToggleHistory(); ChatBox.CollectHistory(visible);
        Check(visible.Count == 0, "empty history");
        for (int i = 0; i < 128; i++) ChatBox.Add("A", i.ToString(), ChatPacket.KindSay);
        ChatBox.CollectCompact(visible); Check(visible.Count == 3, "compact three");
        ChatBox.CollectHistory(visible); Check(visible.Count == 12 && visible[^1].Line.Text == "127", "newest twelve");
        ChatBox.ScrollHistory(10); ChatBox.CollectHistory(visible); string first = visible[0].Line.Text;
        ChatBox.Add("A", "128", ChatPacket.KindSay); ChatBox.CollectHistory(visible);
        Check(visible[0].Line.Text == first && ChatBox.UnreadWhileScrolled == 1, "stable scrolled window during rollover");
        ChatBox.JumpHistoryOldest(); ChatBox.CollectHistory(visible); Check(visible[0].Line.Text == "1", "retention 128");
        ChatBox.PageHistory(-1); Check(ChatBox.HistoryOffset == 104, "page newer");
        ChatBox.JumpHistoryNewest(); Check(ChatBox.HistoryOffset == 0 && ChatBox.UnreadWhileScrolled == 0, "end newest");
        ChatBox.HandleKeyDown(Keys.Tab, false, false, true); Check(!ChatBox.HistoryOpen, "tab closes");
        ChatBox.ToggleHistory(); ChatBox.Cancel(); Check(!ChatBox.HistoryOpen && !ChatBox.Composing, "cancel closes");
        ChatBox.Open(false); ChatBox.ToggleHistory(); ChatBox.Submit(); Check(!ChatBox.HistoryOpen && !ChatBox.Composing, "submit closes");
        ChatBox.Clear();
        var lines = (List<ChatLine>)typeof(ChatBox).GetField("_lines", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        lines.Add(new ChatLine("A", "old", ChatPacket.KindSay, Environment.TickCount64 - 11000));
        ChatBox.CollectCompact(visible); Check(visible.Count == 0, "compact expires");
        ChatBox.CollectHistory(visible); Check(visible.Count == 1 && visible[0].Alpha == 1, "history never expires");
        ChatBox.Clear(); ChatBox.CollectHistory(visible); Check(visible.Count == 0 && !ChatBox.HistoryOpen, "session reset");
        Console.WriteLine($"Player names/chat: {checks} checks passed.");
        return 0;
    }
}
