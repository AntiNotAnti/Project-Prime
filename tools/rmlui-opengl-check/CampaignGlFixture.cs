using System;
using System.Linq;
using System.Runtime.CompilerServices;
using MphRead;

/// <summary>
/// Synthetic save values without the real new-expedition constructor, which
/// reads the cartridge ScanLog. No game files or user saves are touched.
/// </summary>
internal static class CampaignGlFixture
{
    internal static StorySave Save()
    {
        var save = (StorySave)RuntimeHelpers.GetUninitializedObject(typeof(StorySave));
        void Set(string property, object value) => typeof(StorySave).GetProperty(property)!.SetValue(save, value);
        Set(nameof(StorySave.RoomState), Enumerable.Range(0, 66).Select(_ => new byte[60]).ToArray());
        Set(nameof(StorySave.EnemyEncounters), Enumerable.Range(0, 8).Select(_ => new byte[8]).ToArray());
        Set(nameof(StorySave.VisitedRooms), new byte[9]);
        Set(nameof(StorySave.VisitedConnectors), new int[9]);
        Set(nameof(StorySave.TriggerState), new byte[4]);
        Set(nameof(StorySave.Logbook), new byte[68]);
        Set(nameof(StorySave.AreaHunters), new byte[4]);
        Set(nameof(StorySave.Ammo), new int[2]);
        Set(nameof(StorySave.AmmoMax), new int[2]);
        Set(nameof(StorySave.WeaponSlots), new int[3]);
        Set(nameof(StorySave.Stats), new StorySave.SaveStats());
        save.Areas = 0xC;
        save.CheckpointEntityId = save.CheckpointRoomId = -1;
        return save;
    }
}
