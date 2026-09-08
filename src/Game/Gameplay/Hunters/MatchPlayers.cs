using System;
using System.Collections;
using System.Collections.Generic;
using MphRead.Formats;

namespace MphRead.Entities
{
    /// <summary>The player slots belonging to one scene. No slot is shared across scenes.</summary>
    public sealed class MatchPlayers : IReadOnlyList<PlayerEntity>
    {
        private readonly Scene _scene;
        private readonly PlayerEntity[] _players = new PlayerEntity[PlayerEntity.SlotCapacity];
        private int _maxPlayers = 4;
        public int Count => _players.Length;
        public PlayerEntity this[int slot] => _players[slot];
        public int ActiveCount { get; set; }
        public int CreatedCount { get; private set; }
        public int MaxPlayers
        {
            get => _maxPlayers;
            set => _maxPlayers = value >= 1 && value <= Count ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
        }
        internal MatchPlayers(Scene scene)
        {
            _scene = scene;
            Reset();
        }
        // Called only during construction or after room transition removes the old entities.
        internal void Reset()
        {
            foreach (EntityBase entity in _scene.Entities)
                if (entity is PlayerEntity) throw new InvalidOperationException("Remove scene players before rebuilding slots.");
            for (int slot = 0; slot < Count; slot++) _players[slot] = new PlayerEntity(slot, _scene);
            ActiveCount = 0;
            CreatedCount = 0;
            _scene.LocalPlayerSlot = _scene.IsHeadless ? -1 : 0;
        }
        public PlayerEntity? Create(Hunter hunter, int recolor)
        {
            if (CreatedCount >= MaxPlayers) return null;
            PlayerEntity player = _players[CreatedCount++];
            player.PrepareSlot(hunter, recolor);
            return player;
        }
        public IEnumerator<PlayerEntity> GetEnumerator() => ((IEnumerable<PlayerEntity>)_players).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
