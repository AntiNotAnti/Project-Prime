using System;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
internal static class NativeSettingsVisualAcceptance
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,MenuSettings settings)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException("NATIVE VISUAL SETTINGS: "+label);Console.WriteLine("NATIVE VISUAL SETTINGS PASS "+label);}
        var retained=pages.Page;using var page=new NativeFullSettingsPage(host,pages,settings,asModal:true);page.Open();host.Update();
        var controller=(NativeFullSettingsController)typeof(NativeFullSettingsPage).GetField("_controller",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
        string[] engine=controller.Rows.Select(r=>r.Read()).ToArray();ulong sequence=100000;
        for(int kind=0;kind<3;kind++)
        {
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
            Check(controller.Rows.Select(r=>r.Value).SequenceEqual(before)&&controller.Rows.Select(r=>r.Read()).SequenceEqual(engine)&&page.OwnsForeground,"Cancel restores exact prior draft and runtime owners");
        }
    }
}
