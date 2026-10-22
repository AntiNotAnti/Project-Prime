using MphRead.Mods.Import;
using MphRead.Mods.Platform;

// Run once in the tool's isolated process: Android root configuration is a
// process authority and intentionally has no reset/reconfiguration test seam.
internal static class CharacterImportPathsChecks
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string why){if(!value)throw new InvalidOperationException("Character import root: "+why);checks++;}
        void Refuse<T>(Action work,string why)where T:Exception
        {try{work();}catch(T){checks++;return;}throw new InvalidOperationException("Expected root refusal: "+why);}
        string temporary=Path.Combine(OperatingSystem.IsMacOS()?"/private/tmp":Path.GetTempPath(),"prime-character-root-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string? previous=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        try
        {
            string root=Path.Combine(temporary,"android-data"),other=Path.Combine(temporary,"other-data");
            Directory.CreateDirectory(root);Directory.CreateDirectory(other);
            string file=Path.Combine(temporary,"ordinary-file");File.WriteAllText(file,"own fixture");
            Refuse<InvalidOperationException>(()=>CharacterImportPaths.Resolve(true),"unconfigured Android cannot target executable data");
            Refuse<ArgumentNullException>(()=>CharacterImportPaths.ConfigureAndroidRoot(null!),"null");
            Refuse<InvalidDataException>(()=>CharacterImportPaths.ConfigureAndroidRoot(""),"empty");
            Refuse<InvalidDataException>(()=>CharacterImportPaths.ConfigureAndroidRoot("relative-data"),"relative");
            Refuse<InvalidDataException>(()=>CharacterImportPaths.ConfigureAndroidRoot(Path.Combine(temporary,"missing")),"missing directory");
            Refuse<InvalidDataException>(()=>CharacterImportPaths.ConfigureAndroidRoot(file),"file instead of directory");
            Refuse<InvalidDataException>(()=>CharacterImportPaths.ConfigureAndroidRoot(root+Path.DirectorySeparatorChar+"."),"noncanonical dot path");
            if(!OperatingSystem.IsWindows())
            {
                string link=Path.Combine(temporary,"linked-root");Directory.CreateSymbolicLink(link,root);
                string ancestor=Path.Combine(temporary,"linked-parent");Directory.CreateSymbolicLink(ancestor,temporary);
                Refuse<InvalidDataException>(()=>CharacterImportPaths.ConfigureAndroidRoot(link),"symlink root");
                Refuse<InvalidDataException>(()=>CharacterImportPaths.ConfigureAndroidRoot(Path.Combine(ancestor,"android-data")),"symlink ancestor");
                Directory.Delete(link);Directory.Delete(ancestor);
            }
            Refuse<InvalidOperationException>(()=>CharacterImportPaths.Resolve(true),"invalid configurations cannot latch root");
            string expected=Path.Combine(root,"source-character-previews");
            CharacterImportPaths.ConfigureAndroidRoot(root);
            CharacterImportPaths.ConfigureAndroidRoot(root+Path.DirectorySeparatorChar);
            Check(CharacterImportPaths.Resolve(true)==expected,"configured inventory uses writable Android data root");
            Parallel.For(0,16,_=>CharacterImportPaths.ConfigureAndroidRoot(root));
            Check(CharacterImportPaths.Resolve(true)==expected&&!Directory.Exists(expected),"same-root calls are idempotent and authority does not create inventory");
            Refuse<InvalidOperationException>(()=>CharacterImportPaths.ConfigureAndroidRoot(other),"a different valid directory cannot retarget Android root");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",other);
            Check(CharacterImportPaths.Resolve(true)==expected,"later desktop environment changes cannot retarget captured Android root");
            Check(CharacterImportPaths.Resolve(false)==Path.Combine(AppPaths.UserDataDirectory,"source-character-previews")
                &&CharacterImportPaths.Resolve(false)==Path.Combine(other,"source-character-previews"),"desktop resolution retains AppPaths semantics");
            if(!OperatingSystem.IsAndroid())Check(RetroCharacterPreviewExporter.DefaultRoot==CharacterImportPaths.Resolve(false),"ordinary exporter retains desktop root after Android fixture configuration");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",previous);
            Directory.Delete(temporary,true);
        }
        Console.WriteLine("CHARACTER IMPORT ROOT PASS "+checks+" synthetic checks; Android root authority only, no Android provider/device execution.");
    }
}
