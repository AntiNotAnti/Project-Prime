using MphRead.Entities;

namespace MphRead
{
    /// <summary>Display identities owned by one scene, independent of other matches.</summary>
    public sealed class MatchRoster
    {
        public string[] Nicknames { get; } = new string[PlayerEntity.SlotCapacity];
        public MatchRoster()
        {
            for (int slot = 0; slot < Nicknames.Length; slot++) Nicknames[slot] = $"Player{slot + 1}";
        }
    }
}
