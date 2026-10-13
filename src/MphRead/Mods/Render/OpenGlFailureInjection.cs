using System;

namespace MphRead.Mods.Render;

// Diagnostic harness opts in explicitly on the GL-owning thread. Never enabled
// by environment variables or ordinary client startup.
internal static class OpenGlFailureInjection
{
    [ThreadStatic] internal static Action<string>? Callback;
    internal static void Hit(string stage) => Callback?.Invoke(stage);
}
internal sealed class OpenGlInjectedFailureException : InvalidOperationException
{
    internal OpenGlInjectedFailureException(string stage) : base("Injected GL allocation failure: " + stage) { }
}
