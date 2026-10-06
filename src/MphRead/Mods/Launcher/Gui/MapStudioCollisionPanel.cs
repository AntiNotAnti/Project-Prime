using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.MapEditor;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class MapStudioScreen
    {
        private sealed record RepairReviewRow(string Key,MapViewportRepair Repair,bool Reviewed)
        {
            public override string ToString()
            {
                string state=Reviewed?"✓ reviewed":"• review";
                return $"{state} · {Repair.Kind} · {Repair.Confidence*100:0}% · {Repair.Detail}";
            }
        }

        private static string RepairKey(MapViewportRepair repair)
        {
            System.Numerics.Vector3 center=repair.Points.Length==0?System.Numerics.Vector3.Zero
                :repair.Points.Aggregate(System.Numerics.Vector3.Zero,(a,b)=>a+b)/repair.Points.Length;
            return $"{repair.Kind}|{MathF.Round(center.X*4)/4:0.##},{MathF.Round(center.Y*4)/4:0.##},{MathF.Round(center.Z*4)/4:0.##}|{repair.Detail}";
        }

        private void CollisionRepairInspector()
        {
            _inspector.Children.Clear();if(_document==null||_viewport==null)return;
            _viewport.Collision=true;_viewport.CollisionRepairsOverlay=true;_viewport.InvalidateVisual();
            _inspector.Children.Add(Text("COLLISION AUTO-HEAL REVIEW"));
            if(_document.Project.Definition.Import==null)
            {
                _inspector.Children.Add(Text("Collision repair review is available for BSP/PK3 imports."));
                return;
            }
            MapViewportRepair[] repairs=_viewport.Cache.CollisionRepairs.ToArray();
            var filter=new ComboBox
            {
                ItemsSource=new[]{"Unreviewed","All","Added","Restored","Removed","Low confidence",
                    "Movement risks","Contact density","Jump pads","Topology","Reviewed"},
                SelectedIndex=0
            };
            var list=new ListBox{MaxHeight=360};var radius=new TextBox{Text="2"};
            _inspector.Children.Add(filter);_inspector.Children.Add(list);
            _inspector.Children.Add(Text("Disable-region radius"));_inspector.Children.Add(radius);

            bool Match(MapViewportRepair repair,string value,bool reviewed)
                => value switch
                {
                    "Unreviewed"=>!reviewed,
                    "Reviewed"=>reviewed,
                    "Added"=>repair.Kind==MapCollisionRepairKind.FloorProxyAdded,
                    "Restored"=>repair.Kind==MapCollisionRepairKind.BuriedRestored,
                    "Removed"=>repair.Kind==MapCollisionRepairKind.PhantomRemoved&&repair.Confidence>=.9f,
                    "Low confidence"=>repair.Confidence<.9f,
                    "Movement risks"=>repair.Kind is MapCollisionRepairKind.ProbeFailure
                        or MapCollisionRepairKind.MovementSweepFailure or MapCollisionRepairKind.ContactOverflowRisk
                        or MapCollisionRepairKind.JumpPadFailure or MapCollisionRepairKind.ReachabilityWarning,
                    "Contact density"=>repair.Kind==MapCollisionRepairKind.ContactOverflowRisk,
                    "Jump pads"=>repair.Kind==MapCollisionRepairKind.JumpPadFailure,
                    "Topology"=>repair.Kind is MapCollisionRepairKind.DegenerateRemoved
                        or MapCollisionRepairKind.OverlappingSurface or MapCollisionRepairKind.WindingWarning
                        or MapCollisionRepairKind.OpenBoundary,
                    _=>true
                };
            void Refresh()
            {
                string value=filter.SelectedItem as string??"Unreviewed";
                list.ItemsSource=repairs.Select(repair=>
                {
                    string key=RepairKey(repair);
                    bool reviewed=_studioState.AcceptedCollisionRepairs.Contains(key,StringComparer.Ordinal);
                    return new RepairReviewRow(key,repair,reviewed);
                }).Where(row=>Match(row.Repair,value,row.Reviewed)).ToArray();
            }
            filter.SelectionChanged+=(_,_)=>Refresh();Refresh();

            AddButton(_inspector,"Focus",()=>
            {
                if(list.SelectedItem is RepairReviewRow row)_viewport.FocusWorld(row.Repair.Points);
            });
            AddButton(_inspector,"Accept reviewed",()=>
            {
                if(list.SelectedItem is not RepairReviewRow row)return;
                if(!_studioState.AcceptedCollisionRepairs.Contains(row.Key,StringComparer.Ordinal))
                    _studioState.AcceptedCollisionRepairs.Add(row.Key);
                MapStudioStateStore.Save(_document.Project.Definition,_studioState,_services.UserMapDirectory);Refresh();
            });
            AddButton(_inspector,"Accept all visible",()=>
            {
                foreach(RepairReviewRow row in list.ItemsSource?.OfType<RepairReviewRow>()??Enumerable.Empty<RepairReviewRow>())
                    if(!_studioState.AcceptedCollisionRepairs.Contains(row.Key,StringComparer.Ordinal))
                        _studioState.AcceptedCollisionRepairs.Add(row.Key);
                MapStudioStateStore.Save(_document.Project.Definition,_studioState,_services.UserMapDirectory);Refresh();
            });
            AddButton(_inspector,"Disable heal in region",()=>
            {
                if(list.SelectedItem is not RepairReviewRow row||row.Repair.Points.Length==0)return;
                try
                {
                    float r=Math.Clamp(Number(radius.Text??"2"),.25f,64f);
                    var center=row.Repair.Points.Aggregate(System.Numerics.Vector3.Zero,(a,b)=>a+b)/row.Repair.Points.Length;
                    _document.Edit("Disable collision heal region",d=>d.Import!.CollisionHealExclusions.Add(new()
                    {
                        Center=new[]{center.X,center.Y,center.Z},Radius=r,Note=row.Repair.Kind+" · "+row.Repair.Detail
                    }),MapChangeDomain.Import);
                    _status.Text=$"Auto-Heal disabled within {r:0.##} units of the selected repair.";
                    _=Validate();
                }
                catch(Exception ex){Failure(ex);}
            });
            AddButton(_inspector,"Clear disabled regions",()=>
            {
                _document.Edit("Clear collision heal exclusions",d=>d.Import!.CollisionHealExclusions.Clear(),MapChangeDomain.Import);
                _=Validate();
            });
            int excluded=_document.Project.Definition.Import.CollisionHealExclusions.Count;
            int bodyFailures=repairs.Count(r=>r.Kind==MapCollisionRepairKind.MovementSweepFailure);
            int overflowRisks=repairs.Count(r=>r.Kind==MapCollisionRepairKind.ContactOverflowRisk);
            int jumpPadFailures=repairs.Count(r=>r.Kind==MapCollisionRepairKind.JumpPadFailure);
            var health=_viewport.Cache.CollisionHealth;
            int blockers=_document.Diagnostics.Diagnostics.Count(d=>d.Severity==MapDiagnosticSeverity.Error);
            int warnings=_document.Diagnostics.Diagnostics.Count(d=>d.Severity==MapDiagnosticSeverity.Warning);
            _inspector.Children.Add(Text(
                $"Repairs/risks: {repairs.Length:N0} · disabled regions: {excluded}\n"
                +$"Compile/package: {(blockers==0?"no current blockers":$"{blockers} blocker(s)")} · {warnings} warning(s)\n"
                +(health==null?"Topology/gameplay: run Validate to populate compiled collision health."
                    :$"Gameplay: health {health.Confidence*100:0.0}% · {health.ProbeFailures}/{health.ProbeCount} floor probes · "
                    +$"{health.SweepFailures}/{health.SweepCount} movement/launch sweeps\n"
                    +$"Movement: body {bodyFailures} · contact-buffer {overflowRisks} · jump-pad {jumpPadFailures}\n"
                    +$"Topology: degenerate removed {health.DegenerateFacesRemoved} · overlaps {health.OverlappingFaces} · "
                    +$"winding {health.WindingWarnings} · open edges {health.OpenBoundaryEdges} (open edges may be intentional)")));
        }

    }
}
