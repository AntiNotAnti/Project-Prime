using MphRead.Mods.Launcher;

internal static class StudioResourceControllerChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var fake=new Resources();var controls=new NativeStudioResourcesController(fake);
        controls.Show(NativeStudioResourceView.Materials);
        check(controls.Materials.Count==8&&controls.MaterialPages==3&&controls.AssetPages==3,"resource controller exposes bounded eight-row lists");
        controls.MaterialPageBy(1);controls.SelectMaterial(2);
        check(controls.Selected.MaterialIndex==10&&fake.LastRead==controls.Selected,"paged material slot resolves exact frozen row identity");
        controls.SetDraft(0,"front draft");controls.PropertyPageBy(1);controls.SetDraft(0,"back draft");controls.PropertyPageBy(-1);
        check(controls.Properties[0].Value=="front draft","material drafts survive field pagination");
        var ticket=controls.Selected;
        check(controls.Apply()&&fake.LastApply==ticket&&fake.LastPatch.Count==2&&fake.LastPatch["f8"]=="back draft","all material field pages apply through original ticket in one owner call");
        controls.SetDraft(0,"stale");ticket=controls.Selected;fake.Change();controls.Refresh();
        check(!controls.Apply()&&fake.LastApply==ticket&&controls.Properties[0].Value=="stale","external owner refresh does not retarget a frozen material draft");
        controls.ResetDraft();controls.Show(NativeStudioResourceView.Assets);controls.AssetPageBy(2); // invalid step is ignored
        check(controls.AssetPage==0,"resource pagination rejects unbounded direction arguments");
        controls.SelectAsset(0);controls.CycleChannel();
        check(controls.Execute(NativeStudioResourceOperation.BindAssetChannel)&&fake.LastValues?.Channel==NativeStudioMaterialChannel.Albedo
            &&fake.LastExecute.MaterialIndex==10&&fake.LastExecute.AssetPath=="asset/0.tex","asset binding carries selected material, asset and bounded channel together");
        controls.RequestDelete(asset:true);var confirmation=controls.Delete;
        check(confirmation!=null&&controls.ConfirmDelete()&&!controls.ConfirmDelete(),"resource removal confirmation is consumed only once");
        controls.SelectMaterial(0);controls.RequestDelete(asset:false);fake.Change();controls.Refresh();
        check(!controls.ConfirmDelete(),"owner change invalidates pending resource removal confirmation");
        controls.SelectMaterial(0);controls.SetDraft(0,"source stale");fake.Notices="changed-source";controls.Refresh();
        check(!controls.Apply(),"existing source-watch notice changes invalidate frozen material drafts");
        controls.ResetDraft();controls.UseReplacement();var replacement=controls.Selected;controls.SelectMaterial(1);
        check(controls.Execute(NativeStudioResourceOperation.ReplaceMaterialUsages)&&fake.LastValues?.Replacement==replacement,"material replacement carries separately frozen target identity");
        bool wrongThread=false;Task.Run(()=>{try{controls.Show(NativeStudioResourceView.Objects);}catch(InvalidOperationException){wrongThread=true;}}).GetAwaiter().GetResult();
        check(wrongThread,"native resource controller refuses foreign-thread commands");

        var movedMaterial=new Resources();var materialClick=new NativeStudioResourcesController(movedMaterial);
        materialClick.Show(NativeStudioResourceView.Materials);var paintedMaterial=materialClick.Snapshot.Owner;
        movedMaterial.Change();movedMaterial.RowShift=10;materialClick.SelectMaterial(1);
        check(movedMaterial.LastRead.Owner==paintedMaterial&&movedMaterial.LastRead.MaterialFingerprint=="mat1"
            &&materialClick.Snapshot.Owner==paintedMaterial&&!materialClick.Apply(),
            "material click resolves last-painted row and old owner ticket instead of refreshing into a replacement row");
        var movedAsset=new Resources();var assetClick=new NativeStudioResourcesController(movedAsset);
        assetClick.Show(NativeStudioResourceView.Assets);var paintedAsset=assetClick.Snapshot.Owner;
        movedAsset.Change();movedAsset.RowShift=10;assetClick.SelectAsset(1);
        check(assetClick.Selected.Owner==paintedAsset&&assetClick.Selected.AssetPath=="asset/1.tex"
            &&assetClick.Snapshot.Owner==paintedAsset&&!assetClick.Execute(NativeStudioResourceOperation.RenameAsset,"STALE"),
            "asset click resolves last-painted path and old owner ticket instead of refreshing into a replacement row");

    }
    private sealed class Resources:INativeStudioEditorResources
    {
        private NativeStudioEditTicket _owner=new(Guid.NewGuid(),0,0,Guid.Empty);
        public string Notices="none";
        internal int RowShift;
        internal NativeStudioResourceTicket LastRead,LastApply,LastExecute;
        internal NativeStudioResourceValues? LastValues;
        internal Dictionary<string,string> LastPatch=new();
        private NativeStudioResourceDelete? _delete;
        internal void Change()=>_owner=_owner with {State=_owner.State+1,Revision=_owner.Revision+1};
        public NativeStudioResourcesSnapshot CaptureResources()=>new(_owner.Revision,_owner,true,
            Array.AsReadOnly(Enumerable.Range(0,17).Select(i=>new NativeStudioMaterialRow(i,Guid.Empty,"Material "+(i+RowShift),"mat"+(i+RowShift),0,"")).ToArray()),
            Array.AsReadOnly(Enumerable.Range(0,18).Select(i=>new NativeStudioAssetRow("asset/"+(i+RowShift)+".tex","texture","Asset "+(i+RowShift),"","asset"+(i+RowShift),0)).ToArray()),Notices=="none"?0:1,Notices);
        private bool Current(NativeStudioResourceTicket ticket)=>ticket.Owner==_owner&&ticket.SourceNotices==Notices;
        public NativeStudioMaterialDraft ReadMaterial(NativeStudioResourceTicket ticket)
        {LastRead=ticket;return new(ticket,Array.AsReadOnly(Enumerable.Range(0,17).Select(i=>new NativeStudioPropertyField("f"+i,"Field "+i,NativeStudioPropertyKind.Text,"old"+i,256,null,null,null)).ToArray()));}
        public NativeStudioResult ApplyMaterial(NativeStudioResourceTicket ticket,IReadOnlyDictionary<string,string> patch)
        {LastApply=ticket;LastPatch=new(patch);if(!Current(ticket))return new(false,"STALE");Change();return new(true,"APPLIED");}
        public NativeStudioResult ExecuteResource(NativeStudioResourceTicket ticket,NativeStudioResourceOperation operation,NativeStudioResourceValues values)
        {LastExecute=ticket;LastValues=values;return new(Current(ticket),"EXECUTED");}
        public NativeStudioResourceDelete? PrepareResourceDelete(NativeStudioResourceTicket ticket,bool asset)=>Current(ticket)?_delete=new(ticket,Guid.NewGuid(),asset,"resource"):null;
        public NativeStudioResult ConfirmResourceDelete(NativeStudioResourceDelete confirmation)
        {bool result=confirmation==_delete&&Current(confirmation.Ticket);_delete=null;if(result)Change();return new(result,"CONFIRMED");}
        public void CancelResourceDelete()=>_delete=null;
    }
}
