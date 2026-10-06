using System;
using System.Collections.Generic;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    // Frozen during asset preflight. Packet production never reads or hashes
    // extracted map files; clients compare on their preparation worker.
    private readonly Dictionary<string, MapHash256> _stockGameplayHashes = new(StringComparer.OrdinalIgnoreCase);
    private void RememberStockGameplayHash(string room)
        => _stockGameplayHashes[room] = NetworkMapIdentity.StockGameplayHash(room);
}
