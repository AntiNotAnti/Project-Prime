using System;
using System.IO;
using MphRead.Mods.Launcher;
using Xunit;

namespace MphRead.Tests;

public sealed class GameFileSetupTests
{
    [Fact]
    public void SetupLockAllowsOnlyOneOwnerAndReleasesOnDispose()
    {
        string directory = Directory.CreateTempSubdirectory("prime-setup-lock-").FullName;
        string path = Path.Combine(directory, "setup.lock");
        try
        {
            IDisposable? first = GameFiles.TryAcquireSetupLock(path);
            Assert.NotNull(first);
            try
            {
                Assert.Null(GameFiles.TryAcquireSetupLock(path));
            }
            finally
            {
                first?.Dispose();
            }

            using IDisposable? next = GameFiles.TryAcquireSetupLock(path);
            Assert.NotNull(next);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
