using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
internal static class NativeSettingsVisualAcceptance
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,MenuSettings settings,Action restoreCapturedOwners)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException("NATIVE VISUAL SETTINGS: "+label);Console.WriteLine("NATIVE VISUAL SETTINGS PASS "+label);}
        var retained=pages.Page;int retainedModalCount=pages.ModalCount;
        byte[] settingsSnapshot=JsonSerializer.SerializeToUtf8Bytes(settings);ulong sequence=100000;
        for(int kind=0;kind<3;kind++)
        {
            NativeFullSettingsPage? cleanupPage=null;NativeFullSettingsController? cleanupController=null;string[]? engineSnapshot=null;
            Exception? caseFailure=null;var cleanupFailures=new List<Exception>();
            try
            {
            var page=new NativeFullSettingsPage(host,pages,settings,asModal:true);cleanupPage=page;page.Open();host.Update();
            var controller=(NativeFullSettingsController)typeof(NativeFullSettingsPage).GetField("_controller",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
            cleanupController=controller;string[] engine=controller.Rows.Select(r=>r.Read()).ToArray();engineSnapshot=engine;
            var before=controller.Rows.Select(r=>r.Value).ToArray();
            Check(page.Handle(new(RmlUiIntentKind.SettingsAction,220+kind,page.Document,sequence++)),"open retained visual editor "+kind);page.Refresh();host.Update();page.Refresh();host.Update();
            var visual=(NativeSettingsVisualEditor)typeof(NativeFullSettingsPage).GetField("_visual",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
            Check(visual.OwnsForeground&&pages.Page==retained&&host.IsAlive(retained),"visual editor keeps exact underlying workspace");
            var doc=pages.Top;
            if(kind<2)
            {
                Check(host.TryGetElementBounds(doc,"settings_visual_target_0",out float x,out float y,out float w,out float h)&&w>0&&h>0,"actual placement target has native geometry");
                Check(page.VisualPointerDown(x+w/2,y+h/2)&&page.VisualPointerMove(x+w/2-12,y+h/2-12),"actual visual canvas coordinates stage drag");page.VisualPointerUp();
                Check(!controller.Rows.Select(r=>r.Value).SequenceEqual(before)&&controller.Rows.Select(r=>r.Read()).SequenceEqual(engine),"visual drag changes only existing detached rows");
            }
            else
            {
                Check(host.TryGetElementBounds(doc,"settings_visual_wheel",out float wx,out float wy,out float ww,out float wh)&&ww>0&&wh>0,
                    "outline hue/saturation wheel has native geometry");
                Check(page.VisualPointerDown(wx+ww*.9f,wy+wh/2)&&page.VisualPointerMove(wx+ww/2,wy+wh*.9f),
                    "outline wheel accepts continuous hue/saturation drag");page.VisualPointerUp();
                Check(!controller.Rows.Select(r=>r.Value).SequenceEqual(before)&&controller.Rows.Select(r=>r.Read()).SequenceEqual(engine),
                    "outline wheel stages detached colors without publishing the renderer palette");
                Check(host.TryGetElementBounds(doc,"settings_visual_brightness",out float bx,out float by,out float bw,out float bh)&&bw>0&&bh>0
                    &&page.VisualPointerDown(bx,by+bh/2),"outline brightness track accepts pointer input");page.VisualPointerUp();
                Check(controller.Rows.First(r=>r.Group=="Outline colors").Value=="#000000","brightness can reach exact black");
                Check(page.Handle(new(RmlUiIntentKind.SettingsAction,235,doc,sequence++))
                    &&controller.Rows.First(r=>r.Group=="Outline colors").Value!="#000000","brightness button recovers retained hue from black");
                host.SetField(doc,"settings_visual_color","#112233");
                Check(page.Handle(new(RmlUiIntentKind.SettingsAction,229,doc,sequence++)),"outline native temporary preview accepts draft color");
                Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(engine),"temporary outline preview never publishes global palette");
                typeof(NativeSettingsVisualEditor).GetField("_previewUntil",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(visual,DateTime.UtcNow.AddSeconds(-1));visual.Present();
                Check(!(bool)typeof(NativeSettingsVisualEditor).GetField("_preview",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(visual)!,"outline preview times out without changing engine palette");
            }
            if(kind<2)
            {
                var root=typeof(MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke);
                var field=root.GetField("_fullSettings",BindingFlags.NonPublic|BindingFlags.Static)!;object? previous=field.GetValue(null);
                Check(previous==null,"standalone Back fixture has no unrelated full Settings owner");
                Check(page.VisualEditorOpen&&!page.OwnsForeground,"visual modal owns focus while parent Settings does not");
                try
                {
                    field.SetValue(null,page);
                    Check((bool)root.GetMethod("BackFullSettings",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!,"actual root Back gate routes the foreground visual Settings modal");
                }
                finally{field.SetValue(null,previous);}
            }
            else Check(page.Handle(new(RmlUiIntentKind.SettingsAction,224,doc,sequence++)),"Cancel visual settings");
            host.Update();
            string[] saved=controller.Rows.Select(r=>r.Read()).ToArray();
            Check(page.OwnsForeground,"native visual Back returns to its owning Settings page");
            Check(!saved.SequenceEqual(engine),"Back autosaves the visual edit through the native Settings owner ("+controller.Status+")");
            var unsaved=controller.Rows.Select((row,index)=>(row,index,
                expected:row.Choices.FirstOrDefault(choice=>choice.Equals(saved[index],StringComparison.OrdinalIgnoreCase))??saved[index]))
                .Where(item=>item.row.Value!=item.row.Saved||item.row.Saved!=item.expected)
                .Select(item=>$"{item.row.Label}: draft={item.row.Value}, saved={item.row.Saved}, owner={item.expected}").ToArray();
            Check(unsaved.Length==0,"autosaved visual values become the Settings owner's saved rows"
                +(unsaved.Length==0?"":" ("+string.Join("; ",unsaved.Take(8))+")"));
            if(kind==0)Check(false,"forced failure after autosaved visual edit");
            }
            catch(Exception ex){caseFailure=ex;}
            finally
            {
                void Attempt(Action cleanup){try{cleanup();}catch(Exception ex){cleanupFailures.Add(ex);}}
                var page=cleanupPage;var controller=cleanupController;var engine=engineSnapshot;
                if(page!=null)
                {
                    var visual=(NativeSettingsVisualEditor?)typeof(NativeFullSettingsPage)
                        .GetField("_visual",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page);
                    if(visual is {Closed:false,OwnsForeground:true})Attempt(visual.Dispose);
                    int expectedModalCount=retainedModalCount+(page.HasLivePage?1:0);
                    while(pages.ModalCount>expectedModalCount)
                    {
                        int failuresBefore=cleanupFailures.Count;
                        Attempt(()=>{if(!pages.CloseModal())throw new InvalidOperationException("Visual Settings fixture could not retire an owned nested modal.");});
                        if(cleanupFailures.Count!=failuresBefore)break;
                    }
                }
                if(controller!=null&&engine!=null)
                {
                    foreach(var item in controller.Rows.Select((row,index)=>(row,index)))
                        Attempt(()=>{if(!controller.Stage(item.row,engine[item.index]))
                            throw new InvalidOperationException("Visual Settings fixture could not stage original owner value for "+item.row.Label+".");});
                    if(controller.Dirty)Attempt(()=>{if(!controller.FlushAutoApply())
                        throw new InvalidOperationException("Visual Settings fixture could not restore the original owner values through Settings.");});
                }
                if(page!=null)Attempt(page.Dispose);
                Attempt(()=>RestoreMenuSettings(settings,settingsSnapshot));
                Attempt(restoreCapturedOwners);
                if(controller!=null&&engine!=null)
                {
                    var restored=controller.Rows.Select(r=>r.Read()).ToArray();
                    var changed=controller.Rows.Select((row,index)=>(row,index)).Where(item=>restored[item.index]!=engine[item.index])
                        .Select(item=>$"{item.row.Label}: expected={engine[item.index]}, actual={restored[item.index]}").ToArray();
                    Attempt(()=>Check(changed.Length==0&&pages.Page==retained&&pages.Top==retained
                        &&pages.ModalCount==retainedModalCount&&host.IsAlive(retained),
                        "visual autosave fixture restores exact settings owners and preference bytes"
                        +(changed.Length==0?"":" ("+string.Join("; ",changed.Take(8))+")")));
                }
            }
            if(caseFailure!=null)
            {
                if(cleanupFailures.Count>0)throw new AggregateException("Visual Settings case failed and cleanup also failed.",new[]{caseFailure}.Concat(cleanupFailures));
                if(kind==0&&caseFailure.Message=="NATIVE VISUAL SETTINGS: forced failure after autosaved visual edit")
                {Console.WriteLine("NATIVE VISUAL SETTINGS PASS forced assertion failure restores per-case owners and private preferences");continue;}
                ExceptionDispatchInfo.Capture(caseFailure).Throw();
            }
            if(cleanupFailures.Count>0)throw new AggregateException("Visual Settings fixture cleanup failed.",cleanupFailures);
        }
    }

    private static void RestoreMenuSettings(MenuSettings destination,byte[] snapshot)
    {
        var saved=JsonSerializer.Deserialize<MenuSettings>(snapshot)??throw new InvalidOperationException("Could not read the visual Settings fixture snapshot.");
        foreach(var property in typeof(MenuSettings).GetProperties(BindingFlags.Instance|BindingFlags.Public)
            .Where(property=>property.CanRead&&property.SetMethod!=null))
            property.SetValue(destination,property.GetValue(saved));
    }
}
