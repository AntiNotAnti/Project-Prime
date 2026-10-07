using ProjectPrime.Studio.Rendering;
using MphRead.Mods.StudioRendering;

internal static partial class Program
{
    private static int RunNativeMapReadinessControls()
    {
        Check(!StudioGraphicsHost.HasDevice,"pure readiness controls begin without a graphics device");
        var metrics=new StudioRenderMetrics(3,1,0,0,2,1,0,1);
        var views=Enumerable.Range(0,4).Select(index=>new NativeMapPresentation(index,true,false,null,metrics)).ToArray();
        var four=new NativeSnapshot(0,[],4,metrics,1,4,4,null,null,11,[],0,1,"pure fixture","pure fixture",views);
        const long baseline=10;
        Check(IsNativeMapPresentationReady(four,4,baseline),"four own returned-Present metrics with fresh revision admit");
        Check(!IsNativeMapPresentationReady(four with{NativeSurfaces=1,ViewportTargets=1,MetricsRevision=baseline},4,baseline),
            "prior single native view with a newly allocated visual grid rejects");
        Check(!IsNativeMapPresentationReady(four with{MetricsRevision=baseline},4,baseline),"all four allocated with old global revision rejects");
        Check(!IsNativeMapPresentationReady(four with{MetricsRevision=baseline-1},4,baseline),"older global revision rejects");
        Check(!IsNativeMapPresentationReady(four with{MapViewports=3},4,baseline),"incomplete visible grid rejects");
        Check(!IsNativeMapPresentationReady(four with{NativeSurfaces=3},4,baseline),"incomplete native admission rejects");
        Check(!IsNativeMapPresentationReady(four with{ViewportTargets=3},4,baseline),"incomplete native target preparation rejects");
        Check(!IsNativeMapPresentationReady(four with{NativeSurfaces=5},4,baseline),"unexpected additional native ownership rejects");
        Check(!IsNativeMapPresentationReady(four with{ViewportTargets=5},4,baseline),"unexpected additional viewport ownership rejects");
        Check(!IsNativeMapPresentationReady(four with{Worlds=0},4,baseline),"unprepared shared world rejects");
        Check(!IsNativeMapPresentationReady(four with{Worlds=2},4,baseline),"multiple resident worlds reject");
        Check(!IsNativeMapPresentationReady(four with{MapMetrics=null},4,baseline),"missing global metrics reject");
        Check(!IsNativeMapPresentationReady(four with{MapMetrics=metrics with{DrawCalls=0}},4,baseline),"nonpositive global draw metrics reject");
        Check(!IsNativeMapPresentationReady(four with{MapPresentations=null},4,baseline),"missing per-view observation rejects");
        Check(!IsNativeMapPresentationReady(four with{MapPresentations=views[..3]},4,baseline),"missing one own viewport metric rejects");
        for(int index=0;index<4;index++)
        {
            NativeSnapshot Change(Func<NativeMapPresentation,NativeMapPresentation> change)
                => four with{MapPresentations=views.Select((view,current)=>current==index?change(view):view).ToArray()};
            Check(!IsNativeMapPresentationReady(Change(view=>view with{Metrics=null}),4,baseline),$"own viewport {index} metric missing rejects");
            Check(!IsNativeMapPresentationReady(Change(view=>view with{Active=false}),4,baseline),$"own viewport {index} admission inactive rejects");
            Check(!IsNativeMapPresentationReady(Change(view=>view with{Failed=true,Error="fixture failure"}),4,baseline),$"own viewport {index} fallback rejects");
            Check(!IsNativeMapPresentationReady(Change(view=>view with{Metrics=metrics with{DrawCalls=0}}),4,baseline),$"own viewport {index} no completed draw rejects");
        }
        // Readiness establishes presentation, not the separate retained-resource
        // contract. The original caller still rejects these invalid metrics.
        Check(IsNativeMapPresentationReady(four with{MapMetrics=metrics with{MeshUploads=4}},4,baseline),
            "changed upload count remains observable to the unchanged original assertion");
        Check(IsNativeMapPresentationReady(four with{MapMetrics=metrics with{ReadbackBytes=4}},4,baseline),
            "readback violation remains observable to the unchanged original assertion");
        var one=four with{MapViewports=1,NativeSurfaces=1,ViewportTargets=1,MapPresentations=views[..1]};
        Check(IsNativeMapPresentationReady(one,1,-1),"one own returned-Present metrics admit initial viewport");
        Check(!StudioGraphicsHost.HasDevice,"pure readiness controls do not initialize graphics");
        Console.WriteLine($"Map returned-Present readiness controls PASS {_checks}; CPU-only fixture controls, no native surface or GPU observation inferred.");
        return 0;
    }
}
