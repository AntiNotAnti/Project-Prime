using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

internal static class MapSourceRevisionChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        string directory=Path.Combine(root,"source-revisions");Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"map.json");
        new MapDefinition{Name="REVISION_MAP",FormatVersion=2,MapId=Guid.NewGuid()}.Save(path);
        MapDocument Open(string source)=>new(MapProjectSerializer.Load(source),source);
        void Edit(MapDocument document,string value)=>document.Edit("Edit name",definition=>definition.InGameName=value);
        bool Refuses(MapDocument document,string destination)
        {
            try{document.Save(destination);return false;}
            catch(IOException error){return error.Message.Contains("changed outside",StringComparison.Ordinal);}
        }

        var first=Open(path);var stale=Open(path);
        Edit(first,"First tab");Edit(stale,"Stale tab");first.Save(path);
        byte[] firstBytes=File.ReadAllBytes(path);
        check(Refuses(stale,path)&&File.ReadAllBytes(path).SequenceEqual(firstBytes)&&stale.IsDirty,
            "stale second tab save preserves source bytes and unsaved editor changes");
        Edit(first,"First tab again");first.Save(path);
        check(!first.IsDirty&&MapProjectSerializer.Load(path).Definition.InGameName=="First tab again",
            "a successful save advances the source revision for the next edit");

        // The parsed project's baseline must survive the asynchronous handoff to
        // a document, even if another process saves before document construction.
        var parsed=MapProjectSerializer.Load(path);
        byte[] external=System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(path)+Environment.NewLine);
        File.WriteAllBytes(path,external);
        var lateDocument=new MapDocument(parsed,path);Edit(lateDocument,"Late adoption");
        check(Refuses(lateDocument,path)&&File.ReadAllBytes(path).SequenceEqual(external),
            "load baseline belongs to parsed bytes rather than later document construction");

        var upgrade=MapProjectMigrator.Upgrade(MapProjectSerializer.Load(path));
        File.AppendAllText(path," ");var upgradedDocument=new MapDocument(upgrade,path);Edit(upgradedDocument,"Upgrade");
        check(Refuses(upgradedDocument,path),"explicit project upgrade retains its loaded source revision");

        var missing=Open(path);Edit(missing,"Deleted externally");File.Delete(path);
        check(Refuses(missing,path)&&!File.Exists(path),"ordinary save preserves an externally deleted source");
        string copy=Path.Combine(directory,"saved-copy.json");missing.Save(copy);
        check(!missing.IsDirty&&File.Exists(copy),"Save As to a different filename rescues edits from a source conflict");
        string destination=Path.Combine(directory,"overwrite.json");
        new MapDefinition{Name="OVERWRITE",FormatVersion=2,MapId=Guid.NewGuid()}.Save(destination);
        var previousOwner=Open(destination);Edit(previousOwner,"Previous destination owner");
        Edit(missing,"Explicit overwrite");missing.Save(destination);
        byte[] overwritten=File.ReadAllBytes(destination);
        check(MapProjectSerializer.Load(destination).Definition.InGameName=="Explicit overwrite"
            &&Refuses(previousOwner,destination)&&File.ReadAllBytes(destination).SequenceEqual(overwritten),
            "explicit Save As overwrite becomes the new revision and stale destination owner cannot overwrite it");

        string caseAlias=Path.Combine(directory,"OVERWRITE.JSON");
        if((OperatingSystem.IsWindows()||OperatingSystem.IsMacOS())&&File.Exists(caseAlias))
        {
            var caseOwner=Open(destination);var writer=Open(destination);Edit(writer,"Case alias writer");writer.Save(destination);
            byte[] current=File.ReadAllBytes(destination);Edit(caseOwner,"Stale case alias");
            check(Refuses(caseOwner,caseAlias)&&File.ReadAllBytes(destination).SequenceEqual(current),
                "case-only path aliases cannot bypass a stale save check");
        }

        string alias=Path.Combine(directory,"alias.json");bool links=true;
        try{File.CreateSymbolicLink(alias,destination);}
        catch(Exception error)when(error is UnauthorizedAccessException or PlatformNotSupportedException
            or System.ComponentModel.Win32Exception){links=false;}
        if(links)
        {
            var aliasOwner=Open(alias);var physicalOwner=Open(destination);
            Edit(physicalOwner,"Physical writer");physicalOwner.Save(destination);
            byte[] physicalBytes=File.ReadAllBytes(destination);Edit(aliasOwner,"Alias writer");
            check(Refuses(aliasOwner,alias)&&File.ReadAllBytes(destination).SequenceEqual(physicalBytes)
                &&new FileInfo(alias).LinkTarget!=null,"file alias cannot bypass a stale ordinary save check or replace its link");
            var aliasTarget=Open(destination);Edit(aliasTarget,"Save through alias");aliasTarget.Save(alias);
            check(new FileInfo(alias).LinkTarget!=null&&MapProjectSerializer.Load(destination).Definition.InGameName=="Save through alias",
                "saving through an unchanged source alias updates the physical source and preserves the link");
            var retargeted=Open(alias);Edit(retargeted,"Retargeted alias");
            string other=Path.Combine(directory,"other.json");new MapDefinition{Name="OTHER",FormatVersion=2,MapId=Guid.NewGuid()}.Save(other);
            byte[] otherBytes=File.ReadAllBytes(other);File.Delete(alias);File.CreateSymbolicLink(alias,other);
            check(Refuses(retargeted,alias)&&File.ReadAllBytes(other).SequenceEqual(otherBytes),
                "retargeted source alias refuses save and preserves the new target");
        }

        var left=Open(destination);var right=Open(destination);Edit(left,"Concurrent left");Edit(right,"Concurrent right");
        Task<bool>[] saves=new[]{left,right}.Select(document=>Task.Run(()=>
        {try{document.Save(destination);return true;}catch(IOException){return false;}})).ToArray();
        Task.WaitAll(saves);
        check(saves.Count(save=>save.Result)==1,"concurrent documents cannot both commit against one source revision");
        var canonical=Open(destination);Edit(canonical,"Canonical bytes");canonical.Save(destination);
        check(File.ReadAllBytes(destination).SequenceEqual(System.Text.Encoding.UTF8.GetBytes(MapProjectFolder.Serialize(canonical.Project.Definition))),
            "guarded project save retains the canonical JSON wire bytes");
    }
}
