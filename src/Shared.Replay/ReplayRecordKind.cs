namespace MphRead.Mods.Network
{
    // Frozen authoritative record kinds 1..5; format3 checkpoints append bounded presentation and clock records.
    public enum ReplayRecordKind : byte { Match = 1, Snapshot, World, Roster, Event, Presentation, Clock, ChatState, Perspective }
}
