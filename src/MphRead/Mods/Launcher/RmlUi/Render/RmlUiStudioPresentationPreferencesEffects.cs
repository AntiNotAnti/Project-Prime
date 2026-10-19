#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed record StudioPreferenceLoad(ReplayStudioPresentationPreferencesReview Review,string Facts);
    private sealed class StudioPreferenceOwner
    {
        internal readonly NativeReplayPageLease Lease;
        internal readonly object Transport,PlaybackOwner;
        internal readonly string SourceFacts;
        internal readonly TheatrePlaybackPagePresenter Page;
        internal readonly ITheatreStudioPresentationPreferencesOwner Editor;
        internal readonly CancellationTokenSource Cancellation=new();
        internal readonly CancellationToken Token;
        internal Task<StudioPreferenceLoad> Read=null!;
        internal Task<ReplayStudioPresentationPreferencesReview>? Save;
        internal StudioPreferenceLoad? Loaded;
        internal Scene? InitialScene;
        internal ReplayStudioPresentationPreferences? InitialView;
        internal long InitialPicture,Seek;
        internal readonly bool ResetDefaults;
        internal bool ReadHandled,RestoreHandled,UserChanged,PendingSave;
        internal string? ReadError;
        internal StudioPreferenceOwner(NativeReplayPageLease lease,TheatrePlaybackPagePresenter page,ITheatreStudioPresentationPreferencesOwner editor,bool restoreSavedView)
        {SourceFacts=ReplayNativeFileWitness.Facts(lease.LogicalPath,lease.PlaybackPath);ResetDefaults=restoreSavedView;RestoreHandled=!restoreSavedView;Lease=lease;Page=page;Editor=editor;Transport=DemoPlayback.Session.Transport;PlaybackOwner=DemoPlayback.PlaybackOwner;Token=Cancellation.Token;}
    }
    // Publication and retirement cross a worker boundary; the worker never
    // calls native UI/GL or mutates a camera, transport, editor or Scene.
    private static volatile StudioPreferenceOwner? _studioPreferenceOwner;
    private static (NativeReplayPageLease Lease,TheatrePlaybackPagePresenter Page,ITheatreStudioPresentationPreferencesOwner Editor,bool RestoreSavedView)? _studioPreferenceAfterExport;

    private static bool StudioPreferenceScopeCurrent(StudioPreferenceOwner owner)
        =>!owner.Token.IsCancellationRequested&&ReferenceEquals(_studioPreferenceOwner,owner)
            &&ReferenceEquals(_nativeReplayLease,owner.Lease)&&DemoPlayback.IsActive
            &&ReferenceEquals(DemoPlayback.PlaybackOwner,owner.PlaybackOwner)
            &&ReferenceEquals(_playback,owner.Page)
            &&DemoPlayback.RequestCurrent(owner.Lease.Request,owner.Lease.CanonicalScene)
            &&DemoPlayback.LogicalPath==owner.Lease.LogicalPath&&DemoPlayback.PlaybackPath==owner.Lease.PlaybackPath;

    private static bool StudioPreferenceCurrent(StudioPreferenceOwner owner)
        =>StudioPreferenceScopeCurrent(owner)&&ReferenceEquals(DemoPlayback.Session.Transport,owner.Transport)&&!ReplayVideoExporter.Rendering;

    /// <summary>Call after the actual Studio presenter and native replay lease are installed.</summary>
    private static void BeginNativeStudioPresentationPreferences(RenderWindow window,ITheatreStudioPresentationPreferencesOwner editor,bool restoreSavedView=true)
    {
        RetireNativeStudioPresentationPreferences();
        if(!Shell.StudioWindow||_playback is not{} page||_nativeReplayLease is not{} lease||!NativeReplayLeaseCurrent(window))return;
        // The shell installs this presenter before the first replay update creates
        // its private player. Do not bind jobs to the temporary prepared owner;
        // wait for the same request's actual player and presentation scene.
        if(!DemoPlayback.RequestReady(lease.Request,lease.CanonicalScene))
        {_studioPreferenceAfterExport=(lease,page,editor,restoreSavedView);return;}
        var owner=new StudioPreferenceOwner(lease,page,editor,restoreSavedView);_studioPreferenceOwner=owner;
        owner.Read=ReplayStorageJobs.Run(()=>
        {
            owner.Token.ThrowIfCancellationRequested();
            if(ReplayNativeFileWitness.Facts(lease.LogicalPath,lease.PlaybackPath)!=owner.SourceFacts)
                throw new IOException("The recording changed before its saved view could be reviewed.");
            string source=ReplayNativeFileWitness.Source(lease.LogicalPath,lease.PlaybackPath);
            var review=ReplayStudioPresentationPreferencesStore.Read(lease.LogicalPath,source,owner.Token);
            if(ReplayNativeFileWitness.Source(lease.LogicalPath,lease.PlaybackPath)!=source)
                throw new IOException("The recording changed while loading its saved view.");
            owner.Token.ThrowIfCancellationRequested();
            return new StudioPreferenceLoad(review,ReplayNativeFileWitness.Facts(lease.LogicalPath,lease.PlaybackPath));
        },owner.Token);
        page.ReportStudioPresentationPreferences("Loading this recording's saved view. Save View stores presentation preferences only.");
    }

    /// <summary>Optional action hook: suppress late automatic restore after a user changes presentation.</summary>
    private static void NoteNativeStudioPresentationPreferencesEdited()
    {if(_studioPreferenceOwner is{} owner)owner.UserChanged=true;}

    /// <summary>Owner-thread completion pump. Read-only between explicit open/save events; never writes every frame.</summary>
    private static void PumpNativeStudioPresentationPreferences(RenderWindow window)
    {
        if(_studioPreferenceOwner is not{} owner)
        {
            if(_studioPreferenceAfterExport is{} resume&&!ReplayVideoExporter.Active)
            {
                if(ReferenceEquals(_nativeReplayLease,resume.Lease)&&ReferenceEquals(_playback,resume.Page)&&NativeReplayLeaseCurrent(window))
                {
                    if(DemoPlayback.RequestReady(resume.Lease.Request,resume.Lease.CanonicalScene))
                        BeginNativeStudioPresentationPreferences(window,resume.Editor,resume.RestoreSavedView);
                }
                else _studioPreferenceAfterExport=null;
            }
            return;
        }
        if(!Shell.StudioWindow||!NativeReplayLeaseCurrent(window)||!StudioPreferenceScopeCurrent(owner))
        {RetireNativeStudioPresentationPreferences();return;}
        if(ReplayVideoExporter.Active)
        {
            var resume=(owner.Lease,owner.Page,owner.Editor,RestoreSavedView:false);
            RetireNativeStudioPresentationPreferences();_studioPreferenceAfterExport=resume;return;
        }
        if(!ReferenceEquals(DemoPlayback.Session.Transport,owner.Transport))
        {
            // Staged seeks replace the replica/session, not this request, page,
            // source or player owner. Retire old transport jobs and re-review
            // metadata; never restore an old saved camera over the user's seek.
            try
            {
                if(ReplayNativeFileWitness.Facts(owner.Lease.LogicalPath,owner.Lease.PlaybackPath)!=owner.SourceFacts)
                    throw new IOException("The recording changed while replacing its seek presentation.");
                bool pending=owner.PendingSave;
                BeginNativeStudioPresentationPreferences(window,owner.Editor,restoreSavedView:false);
                if(_studioPreferenceOwner is{} adopted){adopted.PendingSave=pending;adopted.UserChanged=true;}
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {owner.Page.ReportStudioPresentationPreferences(error.Message);RetireNativeStudioPresentationPreferences();}
            return;
        }
        try
        {
            bool saveRequested=owner.Page.TryTakeStudioSaveViewRequest();
            if(saveRequested){owner.PendingSave=true;owner.UserChanged=true;owner.RestoreHandled=true;}
            var scene=DemoPlayback.PresentationScene;
            if(scene==null||!DemoPlayback.Owns(scene))
            {if(saveRequested)throw new InvalidOperationException("Wait for the recording's presentation scene before saving its view.");return;}
            if(owner.InitialScene==null)
            {
                owner.InitialScene=scene;owner.Seek=DemoPlayback.Session.Transport.SeekGeneration;
                if(scene.TryCaptureNativeReplayCamera(ReplayController.CurrentFrame,out _,out long picture))owner.InitialPicture=picture;
                var defaults=new ReplayStudioPresentationPreferences(ReplayCameraMode.FirstPerson,scene.Players.Main.SlotIndex,80,true,false,true,true,true);
                if(owner.ResetDefaults)
                {
                    owner.Editor.WhatShooterSaw=scene.ApplyNativeStudioPreferences(defaults);
                    scene.SetNativeStudioPreview(true,false,false);
                }
                if(owner.ResetDefaults)owner.InitialView=scene.CaptureNativeStudioPreferences(owner.Editor.WhatShooterSaw);
            }
            if(!owner.ReadHandled&&owner.Read.IsCompleted)
            {
                owner.ReadHandled=true;owner.Loaded=owner.Read.GetAwaiter().GetResult();
                if(owner.Loaded.Review.Preferences==null)
                {
                    owner.RestoreHandled=true;
                    owner.Page.ReportStudioPresentationPreferences("No saved view for this recording. Use Save View to store camera, HUD and combat presentation choices.");
                }
                else if(owner.RestoreHandled)
                    owner.Page.ReportStudioPresentationPreferences("Saved-view metadata ready. Save View updates your current presentation choices.");
            }
            if(owner.Loaded is{} loaded&&!owner.RestoreHandled)
            {
                if(owner.UserChanged||!ReferenceEquals(scene,owner.InitialScene)||DemoPlayback.Session.Transport.SeekGeneration!=owner.Seek
                    ||(ReplayController.IsPaused&&scene.CaptureNativeStudioPreferences(owner.Editor.WhatShooterSaw)!=owner.InitialView))
                {
                    owner.RestoreHandled=true;
                    owner.Page.ReportStudioPresentationPreferences("Saved view was not applied because you changed the current view. Save View keeps your current choices.");
                }
                else if(scene.TryCaptureNativeReplayCamera(ReplayController.CurrentFrame,out _,out long revision)&&revision>owner.InitialPicture)
                {
                    if(ReplayNativeFileWitness.Facts(owner.Lease.LogicalPath,owner.Lease.PlaybackPath)!=loaded.Facts)
                        throw new IOException("The recording changed before its saved view could be restored.");
                    owner.Editor.WhatShooterSaw=scene.ApplyNativeStudioPreferences(loaded.Review.Preferences!);
                    owner.RestoreHandled=true;
                    owner.Page.ReportStudioPresentationPreferences("Restored this recording's saved view. Save View updates it without changing recording bytes.");
                }
            }
            if(owner.Save is {IsCompleted:true} completedSave)
            {
                owner.Save=null;var review=completedSave.GetAwaiter().GetResult();
                if(!StudioPreferenceCurrent(owner))return;
                owner.Loaded=new(review,owner.Loaded!.Facts);
                owner.Page.ReportStudioPresentationPreferences("Saved this recording's camera, player, FOV, HUD, collision and shooter-view preferences.");
            }
            if(owner.PendingSave)
            {
                owner.UserChanged=true;owner.RestoreHandled=true;
                if(owner.Save is {IsCompleted:false})return;
                if(owner.Loaded is not{} reviewed)
                {
                    if(owner.ReadError!=null){owner.PendingSave=false;throw new InvalidOperationException(owner.ReadError);}
                    return; // Retain the one-shot request through asynchronous seek re-review.
                }
                if(!scene.TryCaptureNativeReplayCamera(ReplayController.CurrentFrame,out _,out _))
                {if(saveRequested)owner.Page.ReportStudioPresentationPreferences("Pause and wait for the current picture; Save View is queued for that reviewed presentation.");return;}
                owner.PendingSave=false;
                var snapshot=scene.CaptureNativeStudioPreferences(owner.Editor.WhatShooterSaw);
                owner.Save=ReplayStudioPresentationPreferencesStore.SaveAsync(reviewed.Review,snapshot,()=>
                {
                    owner.Token.ThrowIfCancellationRequested();
                    if(ReplayNativeFileWitness.Source(owner.Lease.LogicalPath,owner.Lease.PlaybackPath)!=reviewed.Review.SourceRevision)
                        throw new IOException("The recording changed before its view preferences could be saved.");
                    owner.Token.ThrowIfCancellationRequested();
                    if(!StudioPreferenceCurrent(owner))throw new InvalidOperationException("The original Studio replay owner changed before view publication.");
                },owner.Token);
                owner.Page.ReportStudioPresentationPreferences("Saving this recording's view preferences…");
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            if(owner.Read.IsFaulted||owner.Read.IsCanceled){owner.ReadHandled=true;owner.ReadError=error.Message;}
            if(StudioPreferenceCurrent(owner))owner.Page.ReportStudioPresentationPreferences(error.Message);
        }
    }

    /// <summary>Call before clearing replay lease/presenter or giving export ownership of presentation.</summary>
    private static void RetireNativeStudioPresentationPreferences()
    {
        _studioPreferenceAfterExport=null;
        var owner=_studioPreferenceOwner;_studioPreferenceOwner=null;if(owner==null)return;
        owner.Cancellation.Cancel();
        // Observe failures and release cancellation storage only after workers
        // retire; no close-time save can publish against a disposed owner.
        _=Task.WhenAll(owner.Read,owner.Save??Task.CompletedTask).ContinueWith(task=>
        {if(task.IsFaulted)_=task.Exception;owner.Cancellation.Dispose();},CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
}
#endif
