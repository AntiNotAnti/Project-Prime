#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Startup;

/// <summary>One native startup gate. The application owner alone decides what follows completion.</summary>
internal sealed class NativeStartupPage : IDisposable
{
    internal const int ArtworkWidth = 1672, ArtworkHeight = 941;
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly bool _studio;
    private RmlUiDocumentToken _page;
    private CancellationTokenRegistration _retirement;
    private bool _disposed, _closed;
    public RmlUiDocumentToken Document => _page;
    public bool Closed => _closed;
    public NativeStartupPage(RmlUiHost host,RmlUiPageManager pages,bool studio)
    { _host=host??throw new ArgumentNullException(nameof(host));_pages=pages??throw new ArgumentNullException(nameof(pages));_studio=studio; }
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        _page=_pages.OpenPage(new("startup","pages/startup/startup.rml","native_startup_continue"));
        _retirement=_pages.Lifetime(_page).Register(()=>{_disposed=_closed=true;});
        _host.SetText(_page,"native_startup_identity",_studio?"PROJECT PRIME STUDIO":"PROJECT PRIME");
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if(_disposed||_closed||intent.Document!=_page||intent.Kind!=RmlUiIntentKind.Navigate
            ||intent.Argument!=(int)RmlUiRouteArgument.Home||_pages.Top!=_page||_host.CurrentInputDocument!=_page
            ||!_host.IsAlive(_page)||!_pages.Accept(intent))return false;
        _closed=true;_host.SetBool(_page,"disabled:native_startup_continue",true);return true;
    }
    public void Refresh(int width,int height,bool reduceMotion)
    {
        if(_disposed||_closed||width<=0||height<=0||!_host.IsAlive(_page))return;
        _host.SetBool(_page,"class:native_startup:wide",(long)width*ArtworkHeight>(long)height*ArtworkWidth);
        _host.SetBool(_page,"class:native_startup:reduce-motion",reduceMotion);
        // Static prompt intentionally stays readable for all motion preferences.
    }
    public void Dispose()
    {
        _retirement.Dispose();if(_disposed)return;_disposed=_closed=true;
        if(_page!=default&&_pages.Page==_page)_pages.ClosePage();
    }
}
#endif
