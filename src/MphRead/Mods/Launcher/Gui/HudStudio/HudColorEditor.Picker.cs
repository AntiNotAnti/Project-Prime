using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
namespace MphRead.Mods.Launcher.Gui;
internal sealed partial class HudColorEditor
{
    private DecimalSliderRow[] _channels=Array.Empty<DecimalSliderRow>();
    private bool _syncPicker;
    private static readonly List<string> Recent=new();
    private static readonly List<string> Favorites=LoadFavorites();
    private static string FavoritesPath=>Path.Combine(Mods.Platform.AppPaths.UserDataDirectory,"hud-color-favorites.json");
    private static List<string> LoadFavorites()
    { try {return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesPath))??new()).Where(s=>TryNormalize(s,out _)).Take(48).ToList();}catch{return new();} }
    private void AddFullPicker()
    {
        var panel=new StackPanel { Spacing=3 };
        string[] names={"Hue (degrees)","Saturation (%)","Brightness (%)","Red (0–255)","Green (0–255)","Blue (0–255)"};
        _channels=names.Select((n,i)=>new DecimalSliderRow(n,0,0,i==0?360:i<3?100:255)).ToArray();
        for(int i=0;i<6;i++)
        {
            int index=i;panel.Children.Add(_channels[i]);_channels[i].ValueChanged+=(_,_)=>
            {
                if(_syncPicker)return;
                Color c;
                if(index<3)
                {
                    double h=_channels[0].Value/60,s=_channels[1].Value/100,v=_channels[2].Value/100;
                    double chroma=v*s,x=chroma*(1-Math.Abs(h%2-1)),m=v-chroma;
                    (double r,double g,double b)=h switch {<1=>(chroma,x,0d),<2=>(x,chroma,0d),<3=>(0d,chroma,x),<4=>(0d,x,chroma),<5=>(x,0d,chroma),_=>(chroma,0d,x)};
                    c=Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
                }
                else c=Color.FromRgb((byte)_channels[3].Value,(byte)_channels[4].Value,(byte)_channels[5].Value);
                Apply($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            };
        }
        Children.Add(new Expander { Header="Full color • HSV / RGB",Content=panel });
        var colors=new WrapPanel();
        void RefreshColors()
        {
            colors.Children.Clear();
            foreach(var color in Favorites.Concat(Recent).Distinct().Take(48))
            {
                var b=new Avalonia.Controls.Button { Width=26,Height=26,MinWidth=0,Padding=new Avalonia.Thickness(0),Margin=new Avalonia.Thickness(1),Background=Brush.Parse(color) };
                ToolTip.SetTip(b,color);b.Click+=(_,_)=>Apply(color);colors.Children.Add(b);
            }
        }
        var save=new Avalonia.Controls.Button { Content="Save favorite color" };
        save.Click+=(_,_)=> { if(!Favorites.Contains(_value)){Favorites.Insert(0,_value);if(Favorites.Count>48)Favorites.RemoveAt(48);}try{Directory.CreateDirectory(Path.GetDirectoryName(FavoritesPath)!);File.WriteAllText(FavoritesPath,JsonSerializer.Serialize(Favorites));RefreshColors();}catch(Exception ex){_message.Text=ex.Message;} };
        Children.Add(save);Children.Add(new Expander { Header="Favorites / recent colors",Content=colors });
        DetachedFromVisualTree+=(_,_)=> { if(!Recent.Contains(_value))Recent.Insert(0,_value);if(Recent.Count>24)Recent.RemoveAt(24); };
        AttachedToVisualTree+=(_,_)=>RefreshColors();
        SyncPicker(_value);
    }
    private void SyncPicker(string value)
    {
        if(_channels.Length!=6)return;
        _syncPicker=true;
        var c=Color.Parse(value);double r=c.R/255.0,g=c.G/255.0,b=c.B/255.0;
        double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        double hue=delta==0?0:max==r?60*((g-b)/delta%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);
        if(hue<0)hue+=360;
        _channels[0].Value=(float)hue;_channels[1].Value=(float)(max==0?0:delta/max*100);_channels[2].Value=(float)(max*100);
        _channels[3].Value=c.R;_channels[4].Value=c.G;_channels[5].Value=c.B;
        _syncPicker=false;
    }
}
