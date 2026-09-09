namespace FruityPrime.Server.Shared;

/// <summary>Wire-safe tag for a human identity. The tag is part of the key so a
/// guest UUID can never alias a registered account UUID.</summary>
public enum HumanIdentityKind { Registered, Guest }

public readonly record struct HumanIdentityKey(HumanIdentityKind Kind, Guid Value)
{
    public static HumanIdentityKey Registered(Guid playerId) => Create(HumanIdentityKind.Registered, playerId);
    public static HumanIdentityKey Guest(Guid guestSessionId) => Create(HumanIdentityKind.Guest, guestSessionId);

    private static HumanIdentityKey Create(HumanIdentityKind kind, Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("Human identity is required.", nameof(value));
        return new(kind, value);
    }
}

public static class HumanIdentityValidation
{
    public static HumanIdentityKey Require(Guid? playerId, Guid? guestSessionId)
    {
        if (playerId.HasValue == guestSessionId.HasValue
            || playerId is { } player && player == Guid.Empty
            || guestSessionId is { } guest && guest == Guid.Empty)
            throw new ArgumentException("Exactly one human identity is required.");
        return playerId is { } registered
            ? HumanIdentityKey.Registered(registered)
            : HumanIdentityKey.Guest(guestSessionId!.Value);
    }

    public static bool TryGet(Guid? playerId, Guid? guestSessionId, out HumanIdentityKey key)
    {
        try
        {
            key = Require(playerId, guestSessionId);
            return true;
        }
        catch (ArgumentException)
        {
            key = default;
            return false;
        }
    }
}
