using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.Gui;

// Updates only on control creation or settings publication, never in the input loop.
// Animation-priority values are disposable overlays: removing them reveals each
// control's original local/style/inherited values without destroying bindings.
internal static class GameTypography
{
    public static readonly AttachedProperty<bool> KeepTypefaceProperty =
        AvaloniaProperty.RegisterAttached<Control,bool>("KeepTypeface",typeof(GameTypography),false,true);
    public static void SetKeepTypeface(Control control,bool value)=>control.SetValue(KeepTypefaceProperty,value);
    private sealed class State
    {
        public string Role="ui.body";
        public IDisposable? Family,Style,Weight;
        public void Clear(){Family?.Dispose();Style?.Dispose();Weight?.Dispose();Family=Style=Weight=null;}
    }
    private static readonly ConditionalWeakTable<Control,State> States=new();
    private static readonly List<WeakReference<Control>> Controls=new();
    private static bool _installed;
    public static int Revision { get; private set; }
    public static void Install()
    {
        if(_installed)return;_installed=true;
        Control.LoadedEvent.AddClassHandler<Control>((control,_)=>Track(control));
        HudProfiles.Published+=Refresh;
    }
    private static void Track(Control control)
    {
        if(!States.TryGetValue(control,out var state))
        {
            state=new() { Role=Role(new Typeface(control.GetValue(TextBlock.FontFamilyProperty),
                control.GetValue(TextBlock.FontStyleProperty),control.GetValue(TextBlock.FontWeightProperty))) };
            States.Add(control,state);Controls.Add(new(control));
        }
        Apply(control,state);
    }
    private static void Refresh()
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(Refresh);return;}
        Revision++;DeckText.Forget();
        for(int i=Controls.Count-1;i>=0;i--)
        {
            if(!Controls[i].TryGetTarget(out var control)){Controls.RemoveAt(i);continue;}
            Apply(control,States.GetValue(control,_=>new()));
            control.InvalidateMeasure();control.InvalidateVisual();
        }
    }
    private static void Apply(Control control,State state)
    {
        state.Clear();
        if(control.GetValue(KeepTypefaceProperty) || control is not (TextBlock or TextBox))return;
        var selected=Selected(state.Role);if(selected==null)return;
        state.Family=control.SetValue(TextBlock.FontFamilyProperty,FamilyFor(selected),BindingPriority.Animation);
        state.Style=control.SetValue(TextBlock.FontStyleProperty,selected.Italic?FontStyle.Italic:FontStyle.Normal,BindingPriority.Animation);
        state.Weight=control.SetValue(TextBlock.FontWeightProperty,(FontWeight)selected.Weight,BindingPriority.Animation);
    }
    internal static HudFonts.Entry? Selected(string role)
    {
        var typography=HudProfiles.Runtime.Typography;
        var style=typography.Get(role);
        return style.Enabled?HudFonts.All[Math.Max(0,HudFonts.Index(style.Font))]:null;
    }
    private static readonly FontFamily?[] Families=new FontFamily?[HudFonts.All.Length];
    internal static FontFamily FamilyFor(HudFonts.Entry font)
    {
        int index=Math.Max(0,HudFonts.Index(font.Id));
        return Families[index]??=new("avares://ProjectPrime/Assets/HudFonts/"+font.Id+".ttf#"+font.Family);
    }
    private static string Role(Typeface face)
    {
        string family=face.FontFamily.ToString();
        if(family.Contains("mono",StringComparison.OrdinalIgnoreCase))return "ui.data";
        return (int)face.Weight>=600 || family.Contains("Rajdhani",StringComparison.OrdinalIgnoreCase)
            || family.Contains("November",StringComparison.OrdinalIgnoreCase)?"ui.headings":"ui.body";
    }
    public static Typeface Resolve(Typeface original)
    {
        var selected=Selected(Role(original));
        return selected==null?original:new(FamilyFor(selected),selected.Italic?FontStyle.Italic:FontStyle.Normal,(FontWeight)selected.Weight);
    }
}
