#if MPHREAD_RMLUI_POC
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.News;
internal static class NativeNewsCheck
{
    internal static void Run(string library,string assets)
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;Console.WriteLine("PASS "+name);}
        nint module=NativeLibrary.Load(Path.GetFullPath(library));NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
        foreach(var viewport in new[]{(1280,720,1f),(1920,1080,1f),(640,320,1f),(1280,640,2f)}) {
            using var host=new RmlUiHost();Check(host.Initialize(viewport.Item1,viewport.Item2,viewport.Item3,Path.GetFullPath(assets),RmlUiRenderBackend.DrawList),"real News native initialization");
            using var pages=new RmlUiPageManager(host);var links=new Links();using var controller=new NewsController(new BundledNewsProvider(),links);using var presenter=new NewsPagePresenter(host,pages,controller);
            presenter.Open();host.Update();Check(pages.PageKey=="news"&&host.IsAlive(presenter.Document),"real composed News page opens");
            bool heroBounds=host.TryGetElementBounds(presenter.Document,"news_hero",out float heroX,out float heroY,out float heroWidth,out float heroHeight);
            bool feedBounds=host.TryGetElementBounds(presenter.Document,"news_feed",out float feedX,out float feedY,out float feedWidth,out float feedHeight);
            Check(heroBounds&&feedBounds&&heroWidth>0&&heroHeight>0&&feedWidth>0&&feedHeight>0,
                "News authored hero and feed have positive real geometry");
            // RmlUi requires whitespace after @media. Checking the resulting
            // geometry catches a parser failure that leaks compact overrides.
            if(viewport.Item1/viewport.Item3>800)
                Check(heroWidth<viewport.Item1*.6f&&feedX>=heroX+heroWidth-1&&Math.Abs(feedY-heroY)<=1,
                    "desktop News stylesheet retains independent side-by-side hero and feed");
            else
                Check(heroWidth>viewport.Item1*.8f&&feedWidth>viewport.Item1*.8f&&feedY>=heroY+heroHeight-1,
                    "compact News media query stacks full-width hero and feed");
            Check(host.TryGetElementBounds(presenter.Document,"news_back",out _,out float backY,out float backWidth,out float backHeight)
                &&backWidth>0&&backHeight>=44*viewport.Item3&&backY>=Math.Max(heroY+heroHeight,feedY+feedHeight),
                "News Back has a separate44dp flow below both panels");
            void Press(string id,int argument) {
                Check(host.FocusDocument(pages.Top,id),"News real focus "+id);host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
                Check(host.TryTakeIntent(out var intent)&&intent.Kind==RmlUiIntentKind.NewsAction&&intent.Argument==argument,"News real typed action "+id);
                Check(presenter.HandleAction(intent),"News presenter accepts "+id);host.Update();
            }
            foreach(int filter in new[]{1,2,3,0}){Press("news_filter_"+filter,filter);Check(controller.Snapshot().Rows.Length==(filter==0?4:filter==1?2:1),"native exact filter count "+filter);}
            for(int row=0;row<4;row++) {
                Press("news_article_"+row,row+4);var article=controller.Snapshot().Selected!;Press("news_read",8);
                Check(pages.ModalCount==1&&controller.Snapshot().Dialog==NewsDialog.Transmission,"native detail modal preserves selected dispatch");
                Check(host.TryGetElementBounds(pages.Top,"news_detail_body",out float dx,out float dy,out float dw,out float dh)&&dw>viewport.Item1*.6f&&dh>0,"native exact article detail positive readable bounds");
                var stale=host.CreateIntent(RmlUiIntentKind.NewsAction,9);Press("news_detail_close",9);
                Check(!presenter.HandleAction(stale)&&controller.Snapshot().Selected==article,"retired detail rejects late close and retains hero");
            }
            links.Success=false;Press("news_discord",10);Check(links.Uri==NewsController.DiscordUrl&&pages.ModalCount==1,"actual browser boundary failure opens exact URI fallback");
            Check(host.TryGetElementBounds(pages.Top,"news_discord_uri",out _,out _,out float uw,out float uh)&&uw>viewport.Item1*.6f&&uh>0,"fallback displays readable actual Discord URI");Press("news_detail_close",11);
            links.Success=true;Press("news_discord",10);Check(pages.ModalCount==0,"successful browser command keeps native News page");
            Check(host.TryGetElementBounds(presenter.Document,"news_page",out _,out _,out float pw,out float ph)&&pw>=viewport.Item1*.98f&&ph>0,"native News route fills real stage");
            Check(host.TryGetElementBounds(presenter.Document,"news_article_0",out _,out _,out float aw,out float ah)&&aw>viewport.Item1*.32f&&ah>=48*viewport.Item3,"native feed cards positive width and readable height");
            Check(host.TryGetElementBounds(presenter.Document,"news_headline",out _,out float hy,out float hw,out float hh)&&host.TryGetElementBounds(presenter.Document,"news_summary",out _,out float sy,out float sw,out _)&&hw>viewport.Item1*.32f&&sw>viewport.Item1*.32f&&sy>=hy+hh,"headline and summary have independent flow");
            Check(host.FocusDocument(presenter.Document,"news_back"),"native News Back focus");host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
            Check(host.TryTakeIntent(out var back)&&RmlUiIntentRegistry.ToLegacy(back)=="route:home","native News Back uses shared canonical route");
            var doc=presenter.Document;presenter.Dispose();Check(!host.IsAlive(doc),"News retirement closes authored page");
        }
        Console.WriteLine($"PASS {checks} actual native News assertions");
    }
}
#endif
