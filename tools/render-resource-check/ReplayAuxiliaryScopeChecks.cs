using System;
using System.Reflection;
using MphRead;
using OpenTK.Graphics.OpenGL;

internal static class ReplayAuxiliaryScopeChecks
{
    internal static void Run(Scene scene, int framebuffer)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type scopeType = typeof(Scene).GetNestedType("ReplayAuxiliaryRenderScope", BindingFlags.NonPublic)!;
        Type passType = typeof(Scene).GetNestedType("MultiViewPass", BindingFlags.NonPublic)!;
        int checks = 0;
        Check(GL.GetError() == ErrorCode.NoError, "clean scope entry");
        foreach (bool failBody in new[] { false, true })
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            var pass = Activator.CreateInstance(passType, nonPublic: true)!;
            var scope = (IDisposable)Activator.CreateInstance(scopeType, flags, binder: null,
                args: new object[] { scene, pass }, culture: null)!;
            // This test isolates GL ownership from retained world draw items.
            scopeType.GetField("RestoreDrawItems", flags)!.SetValue(scope, false);
            bool caught = false;
            try
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.DrawBuffer(DrawBufferMode.Back);
                GL.ReadBuffer(ReadBufferMode.Back);
                if (failBody) throw new InvalidOperationException("synthetic auxiliary picture failure");
            }
            catch (InvalidOperationException) when (failBody) { caught = true; }
            finally { scope.Dispose(); }
            Check(caught == failBody, "normal and failed auxiliary body both leave scope");
            Check(GL.GetInteger(GetPName.DrawFramebufferBinding) == framebuffer
                && GL.GetInteger(GetPName.ReadFramebufferBinding) == framebuffer,
                "saved draw and read framebuffer owners restored");
            Check(GL.GetInteger(GetPName.DrawBuffer) == (int)DrawBufferMode.ColorAttachment0
                && GL.GetInteger(GetPName.ReadBuffer) == (int)ReadBufferMode.ColorAttachment0,
                "saved attachment selectors restored on their owning framebuffer");
            Check(GL.GetError() == ErrorCode.NoError,
                "attribute restoration on changed framebuffer has no native GL error");
            scope.Dispose();
            Check(GL.GetError() == ErrorCode.NoError, "repeated scope disposal does not pop attributes again");
        }
        Console.WriteLine($"REPLAYAUXILIARYSCOPE PASS {checks} actual native framebuffer/attribute restoration assertions");

        void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("Replay auxiliary scope: " + message);
            checks++;
        }
    }
}
