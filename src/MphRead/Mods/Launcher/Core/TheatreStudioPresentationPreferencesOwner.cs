#if !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.Core;

/// <summary>Optional owner for the per-record combat-view choice; contains no world or I/O ownership.</summary>
public interface ITheatreStudioPresentationPreferencesOwner
{
    bool WhatShooterSaw { get; set; }
}
#endif
