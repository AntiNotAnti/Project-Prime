using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace ProjectPrime.Studio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Running the ordinary entry point retains crash handling, headless
        // diagnostics/update dispatch, and shutdown cleanup on this STA thread.
        string[] studioArgs = new string[args.Length + 1];
        studioArgs[0] = "--studio";
        Array.Copy(args, 0, studioArgs, 1, args.Length);

        MethodInfo entryPoint = typeof(MphRead.Mods.ModEntry).Assembly.EntryPoint
            ?? throw new InvalidOperationException("The game assembly has no entry point.");
        try
        {
            entryPoint.Invoke(null, new object[] { studioArgs });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        return Environment.ExitCode;
    }
}
