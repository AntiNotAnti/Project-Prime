using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using MphRead.AvaloniaShared;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Settings;

namespace ProjectPrime.Studio.Diagnostics;

public sealed class StudioAboutWindow : Window
{
    public static string StudioVersion => Version(typeof(Program).Assembly);
    public static string EngineVersion => Version(typeof(AvaloniaMapStudioHost).Assembly);
    private static string Version(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    public static string LocalSummary => $"Project Prime Studio: {StudioVersion}\nShared Project Prime engine: {EngineVersion}\nStudio IPC: {StudioProtocol.StudioIpcVersion}\nGraphics: {StudioGraphicsHost.Backend ?? (StudioGraphicsHost.SafeMode ? "Safe mode" : "No device created")}\nAdapter: {StudioGraphicsHost.Adapter ?? "Unavailable"}";

    public StudioAboutWindow(StudioPaths paths)
    {
        Title="About Project Prime Studio"; Width=640; MinWidth=480; SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel { Margin=new Thickness(24),Spacing=16 };
        body.Children.Add(new TextBlock { Text="Project Prime Studio",FontSize=24 });
        body.Children.Add(new TextBlock { Name="StudioVersionSummary",Text=LocalSummary,TextWrapping=Avalonia.Media.TextWrapping.Wrap });
        var connection=new TextBlock { Name="StudioVersionConnection",Text="Checking the running game…",TextWrapping=Avalonia.Media.TextWrapping.Wrap };
        body.Children.Add(connection);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=12 };
        var copy=new Button { Content="Copy diagnostics" };
        copy.Click+=async (_,_)=> { if(Clipboard is { } clipboard) await clipboard.SetTextAsync(LocalSummary+"\n"+connection.Text); };
        var close=new Button { Content="Close" }; close.Click+=(_,_)=>Close();
        actions.Children.Add(copy);actions.Children.Add(close);body.Children.Add(actions);Content=body;
        var lifetime=new CancellationTokenSource();
        Closed+=(_,_)=>{ lifetime.Cancel();lifetime.Dispose(); };
        Opened+=async (_,_)=>
        {
            try
            {
                using var broker=new StudioGameBrokerClient(paths.InstallationDirectory,paths.UserDataDirectory);
                var result=await broker.GetDiagnosticsAsync(lifetime.Token);
                if(lifetime.IsCancellationRequested)return;
                connection.Text=result.GameVersion is { } version
                    ? $"Running Project Prime: {version}\nConnection: "+(result.Accepted ? "Compatible" : result.Error ?? "Unavailable")
                    : "Project Prime connection: "+(result.Error ?? "Unavailable");
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { if(!lifetime.IsCancellationRequested)connection.Text="Project Prime connection: "+ex.Message; }
        };
    }
}
