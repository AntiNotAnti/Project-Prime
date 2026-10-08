using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.News;

public sealed class NewsPagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NewsController _controller;
    private RmlUiDocumentToken _page,_modal;
    private long _revision;
    private NewsDialog _dialog;
    private bool _disposed;
    public RmlUiDocumentToken Document => _page;
    public NewsPagePresenter(RmlUiHost host,RmlUiPageManager pages,NewsController controller) { _host=host;_pages=pages;_controller=controller; }
    public void Open() { ObjectDisposedException.ThrowIf(_disposed,this);_page=_pages.OpenPage(new("news","pages/news/news.rml","news_filter_0"));Refresh(); }
    public bool HandleAction(in RmlUiIntent intent)
    {
        if (_disposed || intent.Kind != RmlUiIntentKind.NewsAction || !_pages.Accept(intent)) return false;
        if (intent.Document == _modal) {
            if (intent.Argument is not (9 or 11)) return false;
            _controller.CloseDialog();Refresh();return true;
        }
        if (intent.Document != _page || _pages.Top != _page) return false;
        bool result = intent.Argument switch { >=0 and <=3 => _controller.Filter((NewsFilter)intent.Argument),
            >=4 and <=7 => _controller.Select(intent.Argument-4),8=>_controller.OpenTransmission(),10=>_controller.OpenDiscord(),_=>false };
        if (result) Refresh();return result;
    }
    public bool Back() { if (_dialog==NewsDialog.None)return false;_controller.CloseDialog();Refresh();return true; }
    public void Refresh()
    {
        if (_disposed || !_host.IsAlive(_page)) return;
        if (_modal != default && (!_host.IsAlive(_modal) || _pages.Top != _modal)) { _modal=default;_dialog=NewsDialog.None;_controller.CloseDialog(); }
        var snapshot=_controller.Snapshot();
        if(snapshot.Dialog!=_dialog) {
            if(_modal!=default) {_pages.CloseModal();_modal=default;}
            _dialog=snapshot.Dialog;
            if(_dialog!=NewsDialog.None) _modal=_pages.OpenModal(new("news-detail","pages/news/detail.rml","news_detail_close"));
        }
        var bindings=new Dictionary<string,RmlUiBindingValue>();
        void Text(string id,string value)=>bindings[id]=RmlUiBindingValue.FromText(value);
        void Flag(string id,bool value)=>bindings[id]=RmlUiBindingValue.FromBoolean(value);
        Text("news_category",snapshot.Selected?.Category??"ALL");Text("news_headline",snapshot.Selected?.Title??"NO DISPATCHES YET");
        Text("news_summary",snapshot.Selected?.Summary??"Check back for project news and announcements.");
        Text("news_error",snapshot.Error);Flag("disabled:news_read",snapshot.Selected==null);
        for(int i=0;i<4;i++) {
            Flag("class:news_filter_"+i+":selected",(int)snapshot.Filter==i);
            bool visible=i<snapshot.Rows.Length;Flag("visible:news_article_"+i,visible);
            Text("news_article_title_"+i,visible?snapshot.Rows[i].Article.Title:"");
            Text("news_article_summary_"+i,visible?snapshot.Rows[i].Article.Category+" // "+snapshot.Rows[i].Article.Summary:"");
            Flag("class:news_article_"+i+":selected",visible&&snapshot.SelectedIndex==i);
        }
        _pages.Present(_page,++_revision,bindings);
        if(_modal!=default) {
            bindings=new();Text("news_detail_category",_dialog==NewsDialog.Transmission?snapshot.Selected?.Category??"":"DISCORD");
            Text("news_detail_title",_dialog==NewsDialog.Transmission?snapshot.Selected?.Title??"":"JOIN THE DISCORD");
            Text("news_detail_body",_dialog==NewsDialog.Transmission?snapshot.Selected?.Detail??"":"Open this invite in your browser:");
            Text("news_discord_uri",_dialog==NewsDialog.DiscordFallback?NewsController.DiscordUrl:"");
            Text("action:news_detail_close",_dialog==NewsDialog.Transmission?"news:action:9":"news:action:11");
            _pages.Present(_modal,++_revision,bindings);
        }
    }
    public void Dispose() { if(_disposed)return;_disposed=true;if(_modal!=default&&_pages.Top==_modal)_pages.CloseModal();if(_pages.Page==_page)_pages.ClosePage();_page=_modal=default; }
}
