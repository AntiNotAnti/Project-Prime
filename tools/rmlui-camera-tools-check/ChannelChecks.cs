using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;

internal static class ChannelChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var backend=new DocumentMemory();
        using var editor=new TheatreCameraToolsController(backend);
        var original=editor.Snapshot().Context.Camera.Keys;
        check(editor.SelectAll(),"independent curve review selects exact saved keys");
        var changes=new[]{TheatreCameraToolsChange.OrientationCurve,TheatreCameraToolsChange.OrientationEase,
            TheatreCameraToolsChange.FovCurve,TheatreCameraToolsChange.FovEase,TheatreCameraToolsChange.RollCurve,TheatreCameraToolsChange.RollEase};
        int[] values={2,4,3,2,4,1};
        for(int i=0;i<changes.Length;i++)
            check(editor.Apply(changes[i],values[i])&&backend.Commits==0&&editor.Snapshot().Pending==null,
                "independent "+changes[i]+" commits one memory transaction without a modal or disk write");
        var curves=new TheatreCameraChannelCurves(2,4,3,2,4,1);
        var edited=editor.Snapshot().Context.Camera.Keys;
        check(edited.All(k=>k.Pose.Channels==curves)&&edited.Select(k=>k.Pose with{Channels=null}).SequenceEqual(original.Select(k=>k.Pose))
            &&editor.Snapshot().UndoCount==6&&editor.Snapshot().Dirty,
            "all six channels change only curve metadata on the selected document");
        for(int i=0;i<6;i++)check(editor.Undo(),"independent curve undo "+i);
        check(editor.Snapshot().Context.Camera.Keys.SequenceEqual(original)&&!editor.Snapshot().Dirty,
            "independent curve undo restores exact legacy null metadata");
        for(int i=0;i<6;i++)check(editor.Redo(),"independent curve redo "+i);
        check(editor.Snapshot().Context.Camera.Keys.SequenceEqual(edited),"independent curve redo restores exact six-channel document");
        check(!editor.Apply(TheatreCameraToolsChange.OrientationCurve,3)
            &&!editor.Apply(TheatreCameraToolsChange.FovEase,5)&&!editor.Apply(TheatreCameraToolsChange.RollCurve,-1)
            &&editor.Snapshot().Context.Camera.Keys.SequenceEqual(edited),
            "orientation Bezier and unknown curve/ease values reject atomically");
        check(editor.Copy()&&editor.Destination("100")&&editor.Apply(TheatreCameraToolsChange.Paste)
            &&editor.Snapshot().Context.Camera.Keys.All(k=>k.Pose.Channels==curves),
            "copy and translated paste preserve all independent curves");
        check(editor.Undo()&&editor.Transform(new(1,2,3,0,0,0,0,1,1))&&editor.Apply(TheatreCameraToolsChange.Transform)
            &&editor.Snapshot().Context.Camera.Keys.All(k=>k.Pose.Channels==curves),
            "position and lens transforms preserve every independent channel");
        check(editor.Save()&&backend.Commits==1&&!editor.Snapshot().Dirty,
            "explicit save publishes the same independent document exactly once");
        var cycleBackend=new DocumentMemory();using var cycle=new TheatreCameraToolsController(cycleBackend);
        check(cycle.Apply(TheatreCameraToolsChange.OrientationCurve)&&cycle.Apply(TheatreCameraToolsChange.OrientationCurve)
            &&cycle.Apply(TheatreCameraToolsChange.OrientationCurve)&&cycle.Snapshot().Focus!.Pose.Channels!.Orientation==4,
            "orientation cycle skips unsupported Bezier and offers Hold");
    }
}
