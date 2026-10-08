using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

public enum StudioDocumentKind { Map, Replay, Clip }
public enum StudioOperation { None, PickingMap, Launching }
public readonly record struct StudioActionResult(bool Accepted, string Message = "");
public readonly record struct StudioAvailability(bool DesktopSupported, bool Installed, bool PickerAvailable, string Message);
public readonly record struct StudioPathResult(bool Valid, string Path = "", StudioDocumentKind Kind = StudioDocumentKind.Map, string Error = "");
public readonly record struct StudioPickResult(string? Path = null, string Error = "");

/// <summary>Launch feedback contains no broker token or launch environment.</summary>
public sealed record StudioViewSnapshot
{
    public Guid Lifetime { get; init; }
    public ulong Version { get; init; }
    public StudioAvailability Availability { get; init; }
    public StudioOperation Operation { get; init; }
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
    public string DocumentName { get; init; } = "";
    public StudioDocumentKind? DocumentKind { get; init; }
    public ulong PathEpoch { get; init; }
    public bool Closed { get; init; }
    public bool Busy => Operation != StudioOperation.None;
    public bool CanLaunch => Availability.DesktopSupported && !Busy && !Closed;
    public bool CanPick => CanLaunch && Availability.PickerAvailable;
}

/// <summary>File dialogs and the existing paired application are the only external authorities.</summary>
public interface IStudioEntryBackend
{
    StudioAvailability Availability();
    StudioPathResult ValidatePath(string path);
    StudioActionResult Launch(string? documentPath, bool recover);
    Task<StudioPickResult> PickMapAsync(CancellationToken cancellationToken);
}
