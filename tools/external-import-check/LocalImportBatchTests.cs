using MphRead.Mods.Import;

internal static class LocalImportBatchTests
{
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-bundle-batch-"+Guid.NewGuid().ToString("N"));
        ImportBundleUpdate Make(string id, byte value) => new(new(id,1,new string('a',64),new string('b',40),new[]{"body.obj"}),
            new Dictionary<string,byte[]>{["body.obj"]=new[]{value}});
        var first=Make("first",1); var second=Make("second",2);
        try
        {
            LocalImportBundle.PublishSequential(root,new[]{first,second});
            var original=LocalImportBundle.Inspect(root,"second").Files.Single();
            using var cancellation=new CancellationTokenSource();
            IEnumerable<ImportBundleUpdate> CancelAfterFirst()
            {
                yield return Make("first",3);
                cancellation.Cancel();
                yield return Make("second",4);
            }
            try { LocalImportBundle.PublishSequential(root,CancelAfterFirst(),cancellation.Token); throw new Exception("Expected partial cancellation"); }
            catch(ImportBundleBatchCanceledException ex)
            { Check(ex.CommittedIds.SequenceEqual(new[]{"first"}) && ex.CancellationToken==cancellation.Token && ex.Message.Contains("Already updated: first"),"exact committed cancellation receipt"); }
            Check(File.ReadAllBytes(LocalImportBundle.Inspect(root,"first").Files.Single())[0]==3,"first committed before cancellation");
            Check(LocalImportBundle.Inspect(root,"second").Files.Single()==original && File.ReadAllBytes(original)[0]==2,"untouched second retains old good generation");
            IEnumerable<ImportBundleUpdate> FailAfterFirst()
            { yield return Make("first",5); throw new IOException("Injected preparation failure"); }
            try { LocalImportBundle.PublishSequential(root,FailAfterFirst()); throw new Exception("Expected partial failure"); }
            catch(ImportBundleBatchException ex)
            { Check(ex.CommittedIds.SequenceEqual(new[]{"first"}) && ex.InnerException is IOException,"exact committed preparation failure receipt"); }
            Check(File.ReadAllBytes(LocalImportBundle.Inspect(root,"first").Files.Single())[0]==5 && LocalImportBundle.Inspect(root,"second").Files.Single()==original,"later failure preserves both committed and untouched generations");
            using var before=new CancellationTokenSource();before.Cancel();
            try { LocalImportBundle.PublishSequential(root,new[]{Make("first",9)},before.Token); throw new Exception("Expected precommit cancellation"); }
            catch(OperationCanceledException ex) { Check(ex is not ImportBundleBatchCanceledException,"no false partial outcome before first commit"); }
            Console.WriteLine("IMPORT BATCH PASS: partial cancellation/failure receipts and untouched-good preservation");
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
}
