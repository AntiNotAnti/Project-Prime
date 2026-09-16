using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class LogFixRegressionTests
{
    [Fact]
    public void BroadcastHudSelectsOnlyUsableHudAndFallsBackToReadyRemote()
    {
        using var scene = new Scene();
        scene.Players[0].LoadFlags |= LoadFlags.Active;
        scene.Players[1].LoadFlags |= LoadFlags.Active;
        scene.LocalPlayerSlot = 0;
        using var session = new ReplayPlaybackSession();
        ScenePresentation presentation = new(scene, new Vector2i(640, 480),
            (KeyboardState)Activator.CreateInstance(typeof(KeyboardState), nonPublic: true)!,
            (MouseState)Activator.CreateInstance(typeof(MouseState), nonPublic: true)!,
            static _ => { }, static () => { }, new FrameTiming(),
            session.SceneServices, session);

        try
        {
            PlayerPresentation local = scene.Players[0].GetPresentation();
            PlayerPresentation remote = scene.Players[1].GetPresentation();
            Assert.Null(BroadcastHud.SelectHud(presentation));

            SetHudReady(local, true);
            Assert.Same(local, BroadcastHud.SelectHud(presentation));

            SetHudReady(local, false);
            SetHudReady(remote, true);
            Assert.Same(remote, BroadcastHud.SelectHud(presentation));
        }
        finally
        {
            presentation.DoCleanup(preserveSharedAudio: true);
        }
    }

    [Fact]
    public void NetSlotReinitializationUsesSceneLifecycleBeforeActivationFlag()
    {
        string source = ReadRepositoryFile("src", "Client.Presentation",
            "Networking", "NetSlotManager.cs");
        int activate = source.IndexOf("private static void Activate",
            StringComparison.Ordinal);
        int activateEnd = source.IndexOf("private static int CountActive",
            activate, StringComparison.Ordinal);
        int activationLifecycle = source.IndexOf("scene.InitializeEntity(player);",
            activate, StringComparison.Ordinal);
        int activateFlag = source.IndexOf("_activated[slot] = true;",
            activationLifecycle, StringComparison.Ordinal);

        Assert.True(activate >= 0 && activateEnd > activate);
        Assert.True(activationLifecycle > activate
            && activationLifecycle < activateEnd);
        Assert.True(activateFlag > activationLifecycle
            && activateFlag < activateEnd);
        Assert.Equal(2, source.Split("scene.InitializeEntity(player);",
            StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CelPreflightCachesExpectedFailureBeforePingPongAdvance()
    {
        string source = ReadRepositoryFile("src", "Renderer", "Backends",
            "SdlGpu", "SdlGpuPostResources.cs");
        int celCall = source.IndexOf("TryPrepareCel(_sceneFormat)",
            StringComparison.Ordinal);
        int target = source.IndexOf("SDL_GPUTexture* target = NextIntermediate(current);",
            celCall, StringComparison.Ordinal);
        int preflight = source.IndexOf("private bool TryPrepareCel",
            StringComparison.Ordinal);
        int cacheCheck = source.IndexOf("_failedCelFormat == targetFormat",
            preflight, StringComparison.Ordinal);
        int cacheWrite = source.IndexOf("_failedCelFormat = targetFormat;",
            preflight, StringComparison.Ordinal);
        int expectedErrors = source.IndexOf(
            "or IOException or PlatformNotSupportedException", preflight,
            StringComparison.Ordinal);

        Assert.True(celCall >= 0);
        Assert.True(target > celCall);
        Assert.True(preflight >= 0);
        Assert.True(cacheCheck > preflight);
        Assert.True(cacheWrite > cacheCheck);
        Assert.True(expectedErrors > preflight && expectedErrors < cacheWrite);
        Assert.Contains("driver={_device.Driver}", source,
            StringComparison.Ordinal);
        Assert.Contains("DescribeShaderFormats(_device.ShaderFormats)", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SetupFailureDoesNotReadKeysFromRedirectedChildProcess()
    {
        string source = ReadRepositoryFile("src", "Client", "Program.cs");
        int checkSetup = source.IndexOf("private static bool CheckSetup",
            StringComparison.Ordinal);
        Assert.True(checkSetup >= 0);
        int finishSetup = source.IndexOf("private static void FinishSetupFailure",
            checkSetup, StringComparison.Ordinal);

        Assert.True(finishSetup > checkSetup);
        string setup = source[checkSetup..finishSetup];
        Assert.DoesNotContain("Console.ReadKey", setup, StringComparison.Ordinal);
        Assert.Equal(2, setup.Split("FinishSetupFailure();",
            StringSplitOptions.None).Length - 1);
        Assert.Contains("if (Console.IsInputRedirected) return;", source,
            StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException)", source,
            StringComparison.Ordinal);
    }

    private static void SetHudReady(PlayerPresentation presentation, bool ready)
        => typeof(PlayerPresentation)
            .GetField("<HudReady>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(presentation, ready);

    private static string ReadRepositoryFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }
            .Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
