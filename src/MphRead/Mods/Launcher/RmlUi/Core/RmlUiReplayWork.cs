using System;

namespace MphRead.Mods.Launcher.RmlUi.Core;

public enum RmlUiReplayWork { Validate, Recover, Export, Rename, Organize, Delete, Import }
public readonly record struct RmlUiReplayInputs(string Name = "", string Tags = "",
    string Collections = "", string ImportPath = "");
public readonly record struct RmlUiReplayWorkRequest(RmlUiReplayWork Work,
    RmlUiReplayEntry? Entry, RmlUiReplayInputs Inputs);
public sealed record RmlUiReplayWorkResult(bool Success, string Status, RmlUiReplayEntry[] Entries);
