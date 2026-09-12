using System;

namespace MphRead.Mods.Update;

public enum UpdatePolicy
{
    Automatic,
    NotifyOnly,
    Off
}

public static class UpdatePolicyCodec
{
    public static bool TryParse(string? text, out UpdatePolicy policy)
    {
        if (Enum.TryParse(text, ignoreCase: true, out policy)
            && Enum.IsDefined(policy)) return true;
        policy = UpdatePolicy.Automatic;
        return false;
    }

    public static string Describe(UpdatePolicy policy) => policy switch
    {
        UpdatePolicy.Automatic => "Download and install updates automatically. Recommended.",
        UpdatePolicy.NotifyOnly => "Tell me when a new version is available.",
        _ => "Do not check automatically."
    };
}
