using MphRead.Entities;

namespace MphRead;

/// <summary>Destination coordination and rotating visibility scan owned by one scene.</summary>
public sealed class BotRuntimeState
{
    public int GlobalField0 { get; internal set; }
    public int GlobalField2 { get; internal set; }
    public PlayerEntity.PlayerAiData.AiGlobals[] GlobalObjects { get; } = CreateObjects();
    public bool[,] PlayerVisibility { get; } = new bool[PlayerEntity.SlotCapacity, PlayerEntity.SlotCapacity];
    public byte VisibilityIndex1 { get; internal set; } = 1;
    public byte VisibilityIndex2 { get; internal set; }

    private static PlayerEntity.PlayerAiData.AiGlobals[] CreateObjects()
    {
        var result = new PlayerEntity.PlayerAiData.AiGlobals[PlayerEntity.SlotCapacity];
        for (int i = 0; i < result.Length; i++) result[i] = new();
        return result;
    }
}
