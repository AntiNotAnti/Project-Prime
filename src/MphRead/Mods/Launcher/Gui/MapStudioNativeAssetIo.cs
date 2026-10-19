using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal sealed partial class NativeStudioEditorSession : INativeStudioEditorAssetIo
    {
        private sealed class AssetIoWork
        {
            public required NativeStudioAssetIoSnapshot Snapshot;
            public required NativeStudioAssetIoSpec Spec;
            public required MapDefinition Definition;
            public NativeStudioFileVersion? ProjectFile,SourceFile,Target;
            public string? DocumentPath,OriginalBaseDirectory,OriginalBundlePath;
            public string OriginalRootPath="";
            public MapStudioPreparedTexture? Texture;
            public MapStudioPreparedAssetExport? Export;
            public Task<AssetIoResult>? Task;
            public CancellationTokenSource? Cancellation;
            public Task<string?>? Picker;
            public int Retired;
            public bool Applying,Committing;
            public Action<string,CancellationToken>? Checkpoint;
        }
        private sealed class AssetIoResult
        {
            public MapStudioPreparedTexture? Texture;
            public MapStudioPreparedAssetExport? Export;
            public NativeStudioFileVersion? Target;
            public MapStudioAssetPublication? Publication;
            public readonly List<string> Generated=new();
            public Exception? Error;
        }
        private AssetIoWork? _assetIo;
        private long _assetIoGeneration;
        private readonly bool _assetPickerAvailable=NativeFilePicker.Available;
        internal Func<Task<string?>>? AssetPickerOverride=null;
        internal Action<string,CancellationToken>? AssetIoWorkerCheckpoint=null;
        public bool AssetPickerAvailable=>AssetPickerOverride!=null||_assetPickerAvailable;
        private static bool AssetRecoverable(Exception error)=>error is not OutOfMemoryException and not StackOverflowException;
        private static string IoError(Exception error)=>error is OperationCanceledException?"ASSET WORK CANCELED // CURRENT PROJECT RETAINED"
            :error is IOException or InvalidDataException or ArgumentException or InvalidOperationException?error.Message:"ASSET WORK FAILED // CURRENT PROJECT RETAINED";
        private bool AssetIoCurrent(AssetIoWork work)
        {
            var ticket=work.Snapshot.Ticket.Resource;
            if(!Alive||_owner._detached||_document.FilePath!=work.DocumentPath||_document.Project.Definition.BaseDirectory!=work.OriginalBaseDirectory||_document.Project.Definition.BundlePath!=work.OriginalBundlePath||work.Snapshot.Ticket.Operation==Guid.Empty||Volatile.Read(ref work.Retired)!=0||ticket.Owner!=Ticket||ticket.SourceNotices!=ResourceNotices)return false;
            if(work.Spec.Kind==NativeStudioAssetIoKind.ImportTexture)return true;
            var matches=_document.Project.Definition.Assets.Where(a=>a.Path==ticket.AssetPath).ToArray();
            return matches.Length==1&&matches[0].Kind=="texture"&&MapStudioResourceCommands.Fingerprint(matches[0])==ticket.AssetFingerprint;
        }
        private static string CanonicalRoot(string path)
        {
            string root=Path.GetFullPath(path);if(!Directory.Exists(root))throw new IOException("Save the project in an existing folder before importing textures.");
            return Path.GetDirectoryName(NativeStudioFileVersion.Capture(Path.Combine(root,".native-studio-root-check-"+Guid.NewGuid().ToString("N")),allowMissing:true).Path)!;
        }
        private static bool InsideRoot(string path,string root)=>path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        private static void VerifyFiles(AssetIoWork work)
        {
            work.Cancellation!.Token.ThrowIfCancellationRequested();if(Volatile.Read(ref work.Retired)!=0)throw new OperationCanceledException();
            string root=CanonicalRoot(work.Snapshot.Ticket.ProjectRoot);
            if(root!=work.Snapshot.Ticket.ProjectRoot||CanonicalRoot(work.OriginalRootPath)!=root||work.ProjectFile is {} project&&!project.Matches()||work.SourceFile is {} source&&!MapStudioReviewedAssetFiles.Matches(source))
                throw new IOException("The reviewed project/source path or bytes changed. Review the asset again.");
        }
        private void UpdateAssetIo(AssetIoWork work,NativeStudioAssetIoStage stage,string notice="")
        {work.Snapshot=work.Snapshot with{Revision=work.Snapshot.Revision+1,Stage=stage,Notice=notice};}
        private void StartAssetWork(AssetIoWork work,Func<AssetIoResult> action)
        {
            if(_owner._work!=null)throw new InvalidOperationException("Finish the current editor job first.");
            var cancellation=new CancellationTokenSource();work.Cancellation=cancellation;work.Checkpoint=AssetIoWorkerCheckpoint;_owner._work=cancellation;
            _owner._jobDocument=_document;_owner._jobState=_document.CurrentStateId;_owner._jobGeneration=_owner._editorGeneration;_owner.SetBusy(true);
            // The worker never awaits or posts to the UI thread. Forced retirement
            // can drain it without waiting for a UI continuation or OS dialog.
            work.Task=Task.Run(()=>{try{return action();}catch(Exception error)when(AssetRecoverable(error)){return new AssetIoResult{Error=error};}});
        }
        private void ReleaseAssetWork(AssetIoWork work)
        {
            if(work.Cancellation is {} cancellation)
            {if(_owner._work==cancellation){_owner._work=null;_owner.SetBusy(false);}cancellation.Dispose();work.Cancellation=null;}
            work.Task=null;
        }
        private void ObserveAssetEffects(AssetIoWork work,AssetIoResult result)
        {
            // Always first, including canceled, stale and retired completions.
            // No publication result can strand an untracked generated file.
            foreach(string relative in result.Generated)_document.RegisterGeneratedAsset(relative,work.Snapshot.Ticket.ProjectRoot);
        }
        private void PumpAssetIo()
        {
            var work=_assetIo;if(work==null)return;
            if(work.Picker is {IsCompleted:true} picker)
            {
                work.Picker=null;string? selected=null;Exception? failure=null;
                try{selected=picker.GetAwaiter().GetResult();}catch(Exception error)when(AssetRecoverable(error)){failure=error;}
                if(!AssetIoCurrent(work)){UpdateAssetIo(work,NativeStudioAssetIoStage.Failed,"EDITOR CHANGED // CHOOSE OR REVIEW THE SOURCE AGAIN");return;}
                if(failure!=null||string.IsNullOrEmpty(selected)){UpdateAssetIo(work,NativeStudioAssetIoStage.Failed,failure==null?"FILE SELECTION CANCELED // ENTER A PATH OR CHOOSE AGAIN":IoError(failure));return;}
                work.Spec=work.Spec with{Source=selected};work.Snapshot=work.Snapshot with{Source=selected};
                UpdateAssetIo(work,NativeStudioAssetIoStage.Complete,"SOURCE SELECTED // REVIEW THE TEXTURE BEFORE APPLYING");
            }
            if(work.Task is not {IsCompleted:true} task)return;
            AssetIoResult result=task.GetAwaiter().GetResult();ObserveAssetEffects(work,result);ReleaseAssetWork(work);
            if(result.Publication is {} publication)
            {
                work.Snapshot=work.Snapshot with{RecoveryPath=publication.RecoveryPath};
                UpdateAssetIo(work,publication.Verified?NativeStudioAssetIoStage.Complete:NativeStudioAssetIoStage.Failed,publication.Notice);
                return;
            }
            if(result.Error!=null||!AssetIoCurrent(work))
            {UpdateAssetIo(work,NativeStudioAssetIoStage.Failed,result.Error!=null?IoError(result.Error):"EDITOR OR SOURCE CHANGED // GENERATED FILES TRACKED; REVIEW AGAIN");return;}
            if(!work.Applying)
            {
                work.Texture=result.Texture;work.Export=result.Export;work.Target=result.Target;
                work.SourceFile=result.Texture?.Source??result.Export?.LooseSource;
                work.Snapshot=work.Snapshot with{Width=result.Texture?.Width??0,Height=result.Texture?.Height??0,
                    PortableBytes=result.Texture?.PortableBytes.Length??result.Export?.Bytes.Length??0,NativeBytes=result.Texture?.NativeBytes.Length??0,
                    Source=result.Texture?.Source.Path??result.Export?.Relative??work.Snapshot.Source,SourceSha256=result.Texture?.Source.Sha256??result.Export?.Sha256??"",
                    Destination=result.Target?.Path??"",DestinationSha256=result.Target?.Sha256??""};
                UpdateAssetIo(work,result.Target?.Exists==true?NativeStudioAssetIoStage.NeedsOverwrite:NativeStudioAssetIoStage.Ready);return;
            }
            try
            {
                work.Committing=true;
                if(work.Spec.Kind==NativeStudioAssetIoKind.ImportTexture)
                    MapStudioAssetReferenceCommands.ImportTexture(_document,new(){Path=result.Generated[0],Kind="texture",SourcePath=work.SourceFile!.Path},
                        new(){Path=result.Generated[1],Kind="texture",SourcePath=work.SourceFile.Path},work.Spec.MaterialName,work.Snapshot.Ticket.ProjectRoot);
                else MapStudioAssetReferenceCommands.ReplaceReference(_document,work.Snapshot.Ticket.Resource.AssetPath,new(){Path=result.Generated.Single(),Kind="texture",SourcePath=work.SourceFile!.Path});
                ResourceChanged();UpdateAssetIo(work,NativeStudioAssetIoStage.Complete,"TEXTURE APPLIED // ONE UNDO STEP; GENERATED FILES RETAINED");
            }
            catch(Exception error)when(AssetRecoverable(error)){UpdateAssetIo(work,NativeStudioAssetIoStage.Failed,IoError(error));}
            finally{work.Committing=false;}
        }
        public NativeStudioAssetIoSnapshot? CaptureAssetIo(){CheckThread();PumpAssetIo();return _assetIo?.Snapshot;}
        public NativeStudioResult ChooseAssetSource(NativeStudioResourceTicket ticket)
        {
            CheckThread();PumpAssetIo();if(!ResourceCurrent(ticket)||_assetIo?.Snapshot.Busy==true)return Stale();
            if(!AssetPickerAvailable)return new(false,"SYSTEM PICKER UNAVAILABLE // ENTER A TEXTURE SOURCE PATH");
            RetireAssetIo(false);
            var stamp=new NativeStudioAssetIoTicket(Guid.NewGuid(),++_assetIoGeneration,ticket,"");
            var work=new AssetIoWork{Snapshot=new(1,stamp,NativeStudioAssetIoKind.ImportTexture,NativeStudioAssetIoStage.ChoosingSource,"","","",0,0,0,0,"","","",null),
                Spec=new(NativeStudioAssetIoKind.ImportTexture,"","",""),Definition=_document.Project.ToDefinition(),DocumentPath=_document.FilePath,OriginalBaseDirectory=_document.Project.Definition.BaseDirectory,OriginalBundlePath=_document.Project.Definition.BundlePath};
            _assetIo=work;
            try{work.Picker=AssetPickerOverride?.Invoke()??NativeFilePicker.OpenFile("Choose PNG texture (other formats may be entered by path)","PNG texture","png");return new(true,"CHOOSE A TEXTURE SOURCE");}
            catch(Exception error)when(AssetRecoverable(error)){UpdateAssetIo(work,NativeStudioAssetIoStage.Failed,IoError(error));return new(false,IoError(error));}
        }
        public NativeStudioResult ReviewAssetIo(NativeStudioResourceTicket ticket,NativeStudioAssetIoSpec specification)
        {
            CheckThread();PumpAssetIo();if(!Enum.IsDefined(specification.Kind)||!ResourceCurrent(ticket,asset:specification.Kind!=NativeStudioAssetIoKind.ImportTexture)||_assetIo?.Snapshot.Busy==true)return Stale();
            try
            {
                if(specification.Source.Length>32767||specification.Destination.Length>32767||specification.MaterialName.Length>256)throw new ArgumentException("PATHS ARE LIMITED TO32767 CHARACTERS; MATERIAL NAME TO256");
                if(specification.Kind!=NativeStudioAssetIoKind.ExportOriginal&&(_document.Project.Definition.BundlePath!=null||_document.FilePath==null))throw new InvalidOperationException("SAVE AN EDITABLE JSON PROJECT BEFORE IMPORTING OR REPLACING ASSETS");
                string rawRoot=_document.Project.Definition.BaseDirectory??Path.GetDirectoryName(Path.GetFullPath(_document.FilePath??throw new InvalidOperationException("SAVE A PROJECT OR OPEN A PACKAGE BEFORE EXPORTING ASSETS")))!;
                string root=CanonicalRoot(rawRoot);
                if(specification.Kind!=NativeStudioAssetIoKind.ImportTexture&&MapStudioResourceCommands.Asset(_document.Project.Definition,ticket.AssetPath).Kind!="texture")throw new InvalidOperationException("SELECT A DECLARED TEXTURE ASSET");
                if(specification.Kind==NativeStudioAssetIoKind.ImportTexture&&_document.Project.Definition.Materials.Any(m=>string.Equals(m.Name,specification.MaterialName,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("CHOOSE A NEW MATERIAL NAME");
                if(specification.Kind==NativeStudioAssetIoKind.ImportTexture&&string.IsNullOrWhiteSpace(specification.MaterialName))throw new ArgumentException("ENTER A MATERIAL NAME BEFORE REVIEW");
                RetireAssetIo(false);var stamp=new NativeStudioAssetIoTicket(Guid.NewGuid(),++_assetIoGeneration,ticket,root);
                var detached=_document.CaptureBuildSnapshot().CreateDefinition();detached.BaseDirectory=root;
                if(detached.Import is {} import)import.BaseDirectory=root;
                if(detached.Collision is {} collision)collision.BaseDirectory=root;
                var work=new AssetIoWork{Snapshot=new(1,stamp,specification.Kind,NativeStudioAssetIoStage.Reading,specification.Source,specification.Destination,specification.MaterialName,0,0,0,0,"","","",null),
                    Spec=specification,Definition=detached,DocumentPath=_document.FilePath,OriginalBaseDirectory=_document.Project.Definition.BaseDirectory,OriginalBundlePath=_document.Project.Definition.BundlePath,OriginalRootPath=rawRoot,ProjectFile=_document.FilePath==null?null:NativeStudioFileVersion.Capture(_document.FilePath)};
                _assetIo=work;StartAssetWork(work,()=>ReadAssetIo(work));return new(true,"PREPARING ASSET REVIEW");
            }
            catch(Exception error)when(AssetRecoverable(error)){return new(false,IoError(error));}
        }
        private static AssetIoResult ReadAssetIo(AssetIoWork work)
        {
            VerifyFiles(work);var token=work.Cancellation!.Token;work.Checkpoint?.Invoke("read-start",token);
            if(work.Spec.Kind==NativeStudioAssetIoKind.ExportOriginal)
            {
                var export=MapStudioTextureIoPreparation.PrepareExport(work.Definition,work.Snapshot.Ticket.Resource.AssetPath,token);
                var target=MapStudioReviewedAssetFiles.ReviewTarget(work.Spec.Destination);
                if(!Directory.Exists(Path.GetDirectoryName(target.Path)))throw new IOException("CHOOSE AN EXISTING EXPORT DESTINATION FOLDER");
                if(InsideRoot(target.Path,work.Snapshot.Ticket.ProjectRoot))throw new InvalidOperationException("EXPORT OUTSIDE THE CURRENT PROJECT SO AUTHORING ASSETS REMAIN UNCHANGED");
                if(!string.Equals(Path.GetExtension(target.Path),Path.GetExtension(export.Relative),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("EXPORT DESTINATION MUST KEEP THE ORIGINAL ASSET EXTENSION");
                VerifyFiles(work);return new(){Export=export,Target=target};
            }
            var texture=MapStudioTextureIoPreparation.PrepareTexture(work.Spec.Source,token);VerifyFiles(work);return new(){Texture=texture};
        }
        public NativeStudioResult ApplyAssetIo(Guid operation,bool overwrite)
        {
            CheckThread();PumpAssetIo();var work=_assetIo;
            if(work==null||work.Snapshot.Ticket.Operation!=operation||!AssetIoCurrent(work)||_owner._work!=null
                ||work.Snapshot.Stage is not(NativeStudioAssetIoStage.Ready or NativeStudioAssetIoStage.NeedsOverwrite))return Stale();
            if(work.Snapshot.Kind==NativeStudioAssetIoKind.ExportOriginal&&(work.Target==null||work.Target.Exists!=overwrite))return new(false,"EXPLICITLY CONFIRM THE REVIEWED DESTINATION AND OVERWRITE STATE");
            work.Applying=true;UpdateAssetIo(work,NativeStudioAssetIoStage.Publishing);
            try{StartAssetWork(work,()=>PublishAssetIo(work));return new(true,"PUBLISHING THE REVIEWED ASSET");}
            catch(Exception error)when(AssetRecoverable(error)){UpdateAssetIo(work,NativeStudioAssetIoStage.Failed,IoError(error));return new(false,IoError(error));}
        }
        private static AssetIoResult PublishAssetIo(AssetIoWork work)
        {
            var result=new AssetIoResult();
            try
            {
                VerifyFiles(work);
                if(work.Spec.Kind==NativeStudioAssetIoKind.ExportOriginal)
                {
                    var current=MapStudioTextureIoPreparation.PrepareExport(work.Definition,work.Snapshot.Ticket.Resource.AssetPath,work.Cancellation!.Token);
                    if(current.Sha256!=work.Export!.Sha256)throw new IOException("THE REVIEWED ASSET BYTES CHANGED; REVIEW EXPORT AGAIN");
                    result.Publication=MapStudioReviewedAssetFiles.Publish(work.Target!,work.Export.Bytes,()=>VerifyFiles(work),()=>work.Checkpoint?.Invoke("before-export-publication",work.Cancellation!.Token));return result;
                }
                var texture=work.Texture??throw new InvalidOperationException("Texture review is unavailable.");
                if(work.Spec.Kind==NativeStudioAssetIoKind.ImportTexture)
                {PublishGenerated(".tex",texture.NativeBytes);PublishGenerated(texture.PortableExtension,texture.PortableBytes);}
                else
                {bool native=work.Snapshot.Ticket.Resource.AssetPath.EndsWith(".tex",StringComparison.OrdinalIgnoreCase);PublishGenerated(native?".tex":texture.PortableExtension,native?texture.NativeBytes:texture.PortableBytes);}
                VerifyFiles(work);
            }
            catch(Exception error)when(AssetRecoverable(error)){result.Error=error;}
            return result;
            void PublishGenerated(string extension,ReadOnlyMemory<byte> bytes)
            {
                VerifyFiles(work);string root=work.Snapshot.Ticket.ProjectRoot;string relative="textures/native-"+Guid.NewGuid().ToString("N")+extension;
                string directory=Path.Combine(root,"textures");Directory.CreateDirectory(directory);
                var target=MapStudioReviewedAssetFiles.ReviewTarget(Path.Combine(root,relative));
                if(target.Exists||!InsideRoot(target.Path,root))throw new IOException("GENERATED ASSET DESTINATION CHANGED OR ESCAPES THE PROJECT");
                var publication=MapStudioReviewedAssetFiles.Publish(target,bytes,()=>VerifyFiles(work));
                // A successful exclusive move has an effect even if later verification failed.
                result.Generated.Add(relative);work.Checkpoint?.Invoke("generated-published",work.Cancellation!.Token);
                if(!publication.Verified)throw new IOException(publication.Notice);
            }
        }
        public void CancelAssetIo(Guid operation)
        {
            CheckThread();var work=_assetIo;if(work==null||work.Snapshot.Ticket.Operation!=operation)return;
            Volatile.Write(ref work.Retired,1);work.Cancellation?.Cancel();
            UpdateAssetIo(work,work.Task==null?NativeStudioAssetIoStage.Failed:NativeStudioAssetIoStage.Canceling,"ASSET WORK CANCELED // CURRENT PROJECT RETAINED");
            // OS dialog task may finish later. Forget it without awaiting or editing the document.
            work.Picker=null;
        }
        internal void AssetIoOwnerInvalidated()
        {if(_assetIo is {} work&&!work.Committing&&work.Snapshot.Stage is not(NativeStudioAssetIoStage.Complete or NativeStudioAssetIoStage.Failed)){Volatile.Write(ref work.Retired,1);work.Cancellation?.Cancel();
            if(work.Task==null){work.Picker=null;UpdateAssetIo(work,NativeStudioAssetIoStage.Failed,"EDITOR OR SOURCE CHANGED // REVIEW THE ASSET AGAIN");}}}
        private bool TryLeaveAssetIo()
        {
            PumpAssetIo();if(_assetIo is not {} work)return true;
            if(work.Task!=null){CancelAssetIo(work.Snapshot.Ticket.Operation);return false;}
            RetireAssetIo(false);return true;
        }
        private void RetireAssetIo(bool drain)
        {
            var work=_assetIo;if(work==null)return;
            Volatile.Write(ref work.Retired,1);work.Cancellation?.Cancel();work.Picker=null;
            if(work.Task is {} task)
            {
                if(!drain)throw new InvalidOperationException("Cancel and drain the current asset job before another operation.");
                // Task.Run worker contains only bounded IO/CPU and never needs a UI continuation.
                AssetIoResult result=task.GetAwaiter().GetResult();ObserveAssetEffects(work,result);ReleaseAssetWork(work);
            }
            _assetIo=null;
        }
    }
}
