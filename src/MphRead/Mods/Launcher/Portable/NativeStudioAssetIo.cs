using System;
using System.IO;

namespace MphRead.Mods.Launcher;

public enum NativeStudioAssetIoKind { ImportTexture, ReplaceTexture, ExportOriginal }
public enum NativeStudioAssetIoStage { ChoosingSource, Reading, Ready, NeedsOverwrite, Publishing, Canceling, Complete, Failed }
public sealed record NativeStudioAssetIoSpec(NativeStudioAssetIoKind Kind,string Source,string Destination,string MaterialName);
public sealed record NativeStudioAssetIoTicket(Guid Operation,long Generation,NativeStudioResourceTicket Resource,string ProjectRoot);
public sealed record NativeStudioAssetIoSnapshot(long Revision,NativeStudioAssetIoTicket Ticket,NativeStudioAssetIoKind Kind,
    NativeStudioAssetIoStage Stage,string Source,string Destination,string MaterialName,int Width,int Height,long PortableBytes,
    long NativeBytes,string SourceSha256,string DestinationSha256,string Notice,string? RecoveryPath)
{
    public bool Busy=>Stage is NativeStudioAssetIoStage.ChoosingSource or NativeStudioAssetIoStage.Reading or NativeStudioAssetIoStage.Publishing or NativeStudioAssetIoStage.Canceling;
    public string SourceName=>Path.GetFileName(Source);
    public string DestinationName=>Path.GetFileName(Destination);
    public string Status=>Notice.Length>0?Notice:Stage switch{
        NativeStudioAssetIoStage.ChoosingSource=>"CHOOSE A TEXTURE SOURCE // CURRENT PROJECT REMAINS OPEN",
        NativeStudioAssetIoStage.Reading=>"PREPARING REVIEWED TEXTURE BYTES // CURRENT PROJECT REMAINS OPEN",
        NativeStudioAssetIoStage.Ready=>"REVIEW THE SOURCE AND RESULT BEFORE APPLYING",
        NativeStudioAssetIoStage.NeedsOverwrite=>"REVIEW THE FILENAME AND COMPLETE DESTINATION BEFORE OVERWRITING",
        NativeStudioAssetIoStage.Publishing=>"PUBLISHING THE REVIEWED ASSET // KEEP THE EDITOR OPEN",
        NativeStudioAssetIoStage.Canceling=>"CANCELING ASSET WORK // CURRENT PROJECT RETAINED",
        NativeStudioAssetIoStage.Complete=>"ASSET OPERATION COMPLETE",
        _=>"ASSET OPERATION COULD NOT COMPLETE // CURRENT PROJECT RETAINED"};
}

/// <summary>Optional retained owner service. Capture may drain completed work but never begins source/target IO.</summary>
public interface INativeStudioEditorAssetIo
{
    NativeStudioAssetIoSnapshot? CaptureAssetIo();
    bool AssetPickerAvailable { get; }
    NativeStudioResult ChooseAssetSource(NativeStudioResourceTicket ticket);
    NativeStudioResult ReviewAssetIo(NativeStudioResourceTicket ticket,NativeStudioAssetIoSpec specification);
    NativeStudioResult ApplyAssetIo(Guid operation,bool overwrite);
    void CancelAssetIo(Guid operation);
}
