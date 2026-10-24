#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ProjectPrime.CaptureZone;

namespace ProjectPrime.CaptureZone.Ui;

/// <summary>
/// Real Avalonia view using the shared roster/catalog; a shared component used by the custom CTZ network lobby and respawn menu.
/// The integration adapter must forward authenticated host snapshots/commands.
/// </summary>
public sealed class CaptureZoneLobbyView : UserControl
{
    private readonly Func<RosterSnapshot> snapshot;
    private readonly Func<ZoneHunter, string> select;
    private readonly Func<ZoneHunter, string>? weaponDetails;
    private readonly Func<Guid> localId;
    private readonly Dictionary<ZoneHunter,Button> cards=new();
    private readonly StackPanel teamPanels=new(){Orientation=Orientation.Vertical,Spacing=10};
    private readonly StackPanel detail=new(){Spacing=12};
    private readonly TextBlock status=Text("Hover or focus a hunter. Select to reserve your team's slot.",13,"#BAC9DB");
    public ZoneHunter FocusedHunter { get; private set; }
    public IReadOnlyDictionary<ZoneHunter,Button> Cards => cards;
    public string StatusText => status.Text ?? "";
    public CaptureZoneLobbyView(CaptureZoneRoster roster, Guid localId)
        : this(roster.Snapshot, localId, h => roster.Select(localId, h).Message, "CAPTURE THE ZONE") { }
    public CaptureZoneLobbyView(Func<RosterSnapshot> snapshot, Guid localId, Func<ZoneHunter,string> select,
        string modeName, Func<ZoneHunter,string>? weaponDetails = null, Func<Guid>? currentLocalId = null)
    {
        this.snapshot=snapshot; this.select=select; this.localId=currentLocalId ?? (()=>localId); this.weaponDetails=weaponDetails;
        Background=Paint("#0A111D");
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto")};
        var header=new StackPanel{Spacing=3,Margin=new Thickness(0,0,0,15)};
        header.Children.Add(Text("PROJECT PRIME   /   HERO DEPLOYMENT",11,"#7D9AB9"));
        header.Children.Add(Text(modeName,24,"#F4F8FF",FontWeight.Bold));
        header.Children.Add(Text("1v1–4v4  •  Unique hunters per team  •  Affinity primary only",13,"#BAC9DB"));
        root.Children.Add(header);
        Grid.SetRow(teamPanels,1);teamPanels.Margin=new Thickness(0,0,0,15);root.Children.Add(teamPanels);
        var body=new Grid{ColumnDefinitions=new ColumnDefinitions("160,10,*")};Grid.SetRow(body,2);root.Children.Add(body);
        var choices=new StackPanel{Spacing=6};
        foreach(var profile in HeroCatalog.All)
        {
            var hunter=profile.Hunter;
            var card=new Button{Height=48,Padding=new Thickness(12,4),HorizontalAlignment=HorizontalAlignment.Stretch,
                HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=Paint("#142238"),Foreground=Brushes.White,Focusable=true};
            card.PointerEntered+=(_,_)=>Show(hunter);card.GotFocus+=(_,_)=>Show(hunter);
            card.Click+=(_,_)=>{status.Text=select(hunter);Refresh();Show(hunter);};
            cards.Add(hunter,card);choices.Children.Add(card);
        }
        var list=new ScrollViewer{Content=choices,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};body.Children.Add(list);
        var details=new Border{Background=Paint("#101C2B"),CornerRadius=new CornerRadius(8),Padding=new Thickness(12),
            Child=new ScrollViewer{Content=detail,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        Grid.SetColumn(details,2);body.Children.Add(details);
        var foot=new StackPanel{Spacing=5,Margin=new Thickness(0,14,0,0)};foot.Children.Add(status);
        foot.Children.Add(Text("Team-safe spawn rooms. Support fire and hunter-specific respawn timers. Host-validated hunter selections.",11,"#D6B576"));
        Grid.SetRow(foot,3);root.Children.Add(foot);Content=root;Refresh();Show(ZoneHunter.Trace);
    }
    public void Refresh()
    {
        Guid localId=this.localId();
        var snapshot=this.snapshot();var me=snapshot.Slots.FirstOrDefault(p=>p.PlayerId==localId);
        foreach(var hero in HeroCatalog.All)
        {
            var owner=snapshot.Slots.FirstOrDefault(p=>p.Team==me?.Team&&p.Hunter==hero.Hunter);
            var content=new StackPanel{Spacing=2};content.Children.Add(Text(hero.Hunter.ToString().ToUpperInvariant(),16,"#E9F1FE",FontWeight.Bold));
            content.Children.Add(Text(owner==null?hero.Role:owner.PlayerId==localId?"YOUR SELECTION":$"TAKEN — {owner.Name}",10,owner==null?"#8CA6C5":owner.PlayerId==localId?"#7EE1D2":"#DEA68D"));
            cards[hero.Hunter].Content=content;
            // Occupied cards stay focusable for accessible character inspection.
            // Clicks are still rejected by the shared reservation authority.
            cards[hero.Hunter].Background=Paint(owner?.PlayerId==localId?"#173C48":owner!=null?"#252430":"#142238");
        }
        teamPanels.Children.Clear();
        for(int team=0;team<2;team++)
        {
            var pane=new StackPanel{Spacing=5};pane.Children.Add(Text($"TEAM {team+1}",11,team==0?"#78B4ED":"#EFAB82",FontWeight.Bold));
            var members=new WrapPanel{Orientation=Orientation.Horizontal};
            var slots=snapshot.Slots.Where(p=>p.Team==team).ToArray();
            for(int i=0;i<snapshot.TeamCapacity;i++)
            {
                var text=new StackPanel{Spacing=2};var slot=i<slots.Length?slots[i]:null;
                text.Children.Add(Text(slot?.Name??"OPEN SLOT",11,"#D8E4F6"));
                text.Children.Add(Text((slot?.Hunter?.ToString()??"Select hunter") + (slot?.Ready == true ? " • READY" : ""),10,"#91A9C5"));
                members.Children.Add(new Border{Width=104,Margin=new Thickness(0,0,4,4),Padding=new Thickness(6),Background=Paint("#142031"),CornerRadius=new CornerRadius(5),Child=text});
            }
            pane.Children.Add(members);teamPanels.Children.Add(pane);
        }
    }
    private void Show(ZoneHunter hunter)
    {
        FocusedHunter=hunter;var profile=HeroCatalog.For(hunter);detail.Children.Clear();
        detail.Children.Add(Text(profile.Role,12,"#80D1E7",FontWeight.Bold));
        detail.Children.Add(Text(hunter.ToString().ToUpperInvariant(),28,"#F5F8FF",FontWeight.Bold));
        detail.Children.Add(Text(profile.Identity,16,"#C1CEE1"));
        detail.Children.Add(Text("SHARED GAMEPLAY DEFINITIONS — INITIAL BALANCE",10,"#D6B576"));
        var stats=new WrapPanel{Orientation=Orientation.Horizontal};
        foreach(var pair in new[]{("MAX HEALTH",profile.MaxHealth.ToString()),("DAMAGE REDUCTION",profile.DamageReduction.ToString("P0")),("EFFECTIVE HEALTH",profile.EffectiveHealth.ToString("0.#"))})
        {
            var s=new StackPanel{Spacing=3};s.Children.Add(Text(pair.Item1,10,"#91A9C5"));s.Children.Add(Text(pair.Item2,25,"#F5F8FF",FontWeight.Bold));
            stats.Children.Add(new Border{Padding=new Thickness(12),MinWidth=110,Margin=new Thickness(0,0,6,6),Background=Paint("#192A40"),CornerRadius=new CornerRadius(5),Child=s});
        }
        detail.Children.Add(stats);
        detail.Children.Add(Text("PRIMARY WEAPON",10,"#80D1E7",FontWeight.Bold));detail.Children.Add(Text(profile.Primary,20,"#F5F8FF",FontWeight.SemiBold));
        detail.Children.Add(Text(weaponDetails?.Invoke(hunter) ?? profile.WeaponStatus,13,"#B2C3DA"));
        detail.Children.Add(Text("ALTERNATE FORM",10,"#80D1E7",FontWeight.Bold));detail.Children.Add(Text(profile.Alternate,13,"#B2C3DA"));
        detail.Children.Add(Text("MODE LOADOUT POLICY",10,"#80D1E7",FontWeight.Bold));
        detail.Children.Add(Text("Assigned primary + alternate form. Unlimited primary ammunition with normal cooldowns. Samus uses the mode-exclusive Aether Pulse.",13,"#B2C3DA"));
    }
    private static IBrush Paint(string color)=>new SolidColorBrush(Color.Parse(color));
    private static TextBlock Text(string text,double size,string color,FontWeight? weight=null)=>new(){Text=text,FontSize=size,Foreground=Paint(color),FontWeight=weight??FontWeight.Normal,TextWrapping=TextWrapping.Wrap};
}

#endif
