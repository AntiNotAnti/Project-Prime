using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PrimeBrandMarkTests
{
    [AvaloniaFact]
    public void LoadsTheEmbeddedTransparentMarkWithUniformBoundedLayout()
    {
        using var mark = new PrimeBrandMark(96);

        Assert.True(mark.HasImage);
        Assert.True(mark.UsesSharedBitmap);
        Assert.False(mark.IsFallbackVisible);
        Assert.Equal(Stretch.Uniform, mark.ImageStretch);
        Assert.Equal(96, mark.Width);
        Assert.Equal(96, mark.Height);
        Assert.Equal(PrimeBrandMark.MaximumSize, mark.MaxWidth);
        Assert.Equal(PrimeBrandMark.MaximumSize, mark.MaxHeight);
        Assert.Equal("avares", PrimeBrandMark.AssetUri.Scheme);
        Assert.Equal("avares://ProjectPrime.Client.Presentation/Assets/project-prime-mark.png",
            PrimeBrandMark.AssetUri.ToString());
    }

    [AvaloniaFact]
    public void DecodeFailureUsesTheNonTextFallbackWithoutThrowing()
    {
        using var mark = new PrimeBrandMark(() =>
            throw new FileNotFoundException("fixture"));

        Assert.False(mark.HasImage);
        Assert.False(mark.UsesSharedBitmap);
        Assert.True(mark.IsFallbackVisible);
        Assert.Null(mark.ImageSource);
    }

    [AvaloniaFact]
    public void SharedImageSurvivesDetachAndReattach()
    {
        using var mark = new PrimeBrandMark(48);
        IImage? source = mark.ImageSource;
        var window = new Window { Width = 200, Height = 200, Content = mark };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Same(source, mark.ImageSource);

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(source, mark.ImageSource);

            window.Content = mark;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(source, mark.ImageSource);
            Assert.True(mark.HasImage);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15.99)]
    [InlineData(256.01)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsUnboundedSizes(double size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PrimeBrandMark(size));
}
