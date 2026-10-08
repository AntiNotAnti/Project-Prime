using MphRead.Mods.Launcher.Core;
#if MPHREAD_RMLUI_POC
if(args.Length==3&&args[0]=="--native"){NativeNewsCheck.Run(args[1],args[2]);return;}
#endif
int checks=0;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;Console.WriteLine("PASS "+name);}
var links=new Links();using var news=new NewsController(new BundledNewsProvider(),links);
Check(news.Snapshot().Rows.Length==4&&news.Snapshot().SelectedIndex==0,"exact four authoritative bundled dispatches");
var old=news.Snapshot();Check(news.Filter(NewsFilter.News)&&news.Snapshot().Rows.Length==2,"NEWS filter");
Check(news.Filter(NewsFilter.PatchNotes)&&news.Snapshot().Rows.Single().Article.Category=="PATCH NOTES","patch filter");
Check(news.Filter(NewsFilter.Announcements)&&news.Snapshot().Rows.Single().Article.Category=="ANNOUNCEMENTS","announcement filter");
Check(old.Rows.Length==4&&old.Selected!.Title=="PROJECT PRIME COMMUNITY UPDATE","old snapshot unchanged");
Check(!news.Filter((NewsFilter)20)&&!news.Select(-1)&&!news.Select(4),"bounded invalid filters/selection rejected");
news.Filter(NewsFilter.All);for(int i=0;i<4;i++){Check(news.Select(i)&&news.Snapshot().Selected==old.Rows[i].Article,"select exact dispatch "+i);Check(news.OpenTransmission()&&news.Snapshot().Dialog==NewsDialog.Transmission,"read exact dispatch "+i);Check(!news.Select(0),"foreground modal owns selection "+i);Check(news.CloseDialog()&&news.Snapshot().Selected==old.Rows[i].Article,"close preserves selection "+i);}
Check(news.OpenDiscord()&&links.Uri==NewsController.DiscordUrl&&news.Snapshot().Dialog==NewsDialog.None,"successful browser uses exact approved community invite");
links.Success=false;Check(news.OpenDiscord()&&news.Snapshot().Dialog==NewsDialog.DiscordFallback&&news.Snapshot().Error.Length>0,"failed browser offers recoverable URI fallback");
Check(!news.OpenDiscord()&&news.CloseDialog(),"fallback modal blocks duplicate launch and closes");
links.Throw=true;Check(news.OpenDiscord()&&news.Snapshot().Dialog==NewsDialog.DiscordFallback,"browser exception offers same fallback");
Check(Task.Run(()=>{try{news.Snapshot();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"worker cannot read controller");
using var empty=new NewsController(new EmptyNews(),new Links());Check(empty.Snapshot().Rows.IsEmpty&&empty.Snapshot().Selected==null&&!empty.OpenTransmission(),"empty authority feed has genuine empty state");
Console.WriteLine($"PASS {checks} News controller contracts");
internal sealed class Links:INewsLinkLauncher{public string Uri="";public bool Success=true,Throw;public bool Open(string uri){Uri=uri;if(Throw)throw new IOException("browser unavailable");return Success;}}
internal sealed class EmptyNews:INewsProvider{public IReadOnlyList<NewsDispatch> Read()=>Array.Empty<NewsDispatch>();}
