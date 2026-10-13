using System;
using System.Threading;
namespace MphRead.Mods.Render;
internal static class OpenGlFailureInjectionCheck
{
    internal static int Run()
    {
        int failures=0;
        void Check(bool value,string name){Console.WriteLine($"[glfailurecheck] {(value?"PASS":"FAIL")}: {name}");if(!value)failures++;}
        var previous=OpenGlFailureInjection.Callback;
        try
        {
            OpenGlFailureInjection.Callback=null;
            OpenGlFailureInjection.Hit("normal-client");
            Check(OpenGlFailureInjection.Callback==null,"ordinary client has no active allocation injection");
            int hits=0;
            OpenGlFailureInjection.Callback=stage=>{if(stage=="test-stage"){hits++;throw new OpenGlInjectedFailureException(stage);}};
            OpenGlFailureInjection.Hit("other-stage");
            try{OpenGlFailureInjection.Hit("test-stage");}catch(OpenGlInjectedFailureException){}
            Check(hits==1,"only explicit requested stage throws dedicated diagnostic failure");
            bool isolated=false;
            var thread=new Thread(()=>{isolated=OpenGlFailureInjection.Callback==null;OpenGlFailureInjection.Hit("test-stage");});
            thread.Start();thread.Join();
            Check(isolated && hits==1,"failure injection is isolated to harness GL thread");
        }
        finally{OpenGlFailureInjection.Callback=previous;}
        return failures;
    }
}
