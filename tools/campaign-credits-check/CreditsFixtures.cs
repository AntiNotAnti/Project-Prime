using System.Buffers.Binary;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Campaign;
using Source = MphRead.Mods.Campaign.CampaignCreditsSource;

// Invented pointer graphs/ASCII labels only. No source frontend/text/model
// bytes are copied into these fixtures or written as generated payloads.
internal static class CreditsFixtures
{
    private static int checks;
    private static readonly int[] Selected = { 6,7,8,9,10,11,12,49,50 };
    internal static void Run()
    {
        checks = 0;
        Positive();
        NestedBounds();
        Semantics();
        PathsAndText();
        Budgets();
        Console.WriteLine($"CREDITS SOURCE BOUNDS PASS {checks} generated checks before current-page action regressions.");
        CombinedActions();
        Console.WriteLine($"CREDITS SOURCE PASS {checks} generated checks; detached source-data admission only, no frontend execution/presentation/save/audio authority.");
    }

    private static void Positive()
    {
        var fixture = Build(); var document = Source.Read(fixture.Marm, fixture.Text);
        Check(document.Pages.Select(p => p.Index).SequenceEqual(Selected) && document.GlobalPageCount == 51
            && document.GlobalWidgetCount == 4699 && document.GlobalStringCount == 3,
            "only nine requested pages retained while global widget table exceeds4096");
        Check(document.Widgets.Keys.SequenceEqual(new[] {4698}) && document.Strings.Keys.SequenceEqual(new[] {1,2}),
            "sparse widget/text dictionaries retain original source IDs without reindexing");
        Check(document.MarmSha256 == Hash(fixture.Marm) && document.TextSha256 == Hash(fixture.Text)
            && document.HeaderField4 == 0x1616 && document.HeaderField6 == 12 && document.HeaderField7 == 51
            && document.HeaderField16 == 0xfeedbeef && document.PointerAndRecordVisits is > 4700 and <= 32768,
            "source hashes, opaque header fields and finite traversal evidence survive admission");
        var page = document.Pages[0]; var item = page.Items[0];
        Check(page.Field0 == 0x11112222 && page.InitialFocus == ushort.MaxValue && page.BackgroundA == ushort.MaxValue
            && page.BackgroundB == ushort.MaxValue && page.Field29 == 0xbb && page.Field30 == 0xbb && page.Flags == 3,
            "selected page opaque fields and source page identity preserved");
        Check(item.Index == 0 && item.DelayFx == 14336 && item.XFx == -126976 && item.YFx == 782336 && item.DepthFx == -10240
            && item.Field0 == 0x11223344 && item.RuntimeField16 == 0x55667788 && item.Field24 == -123
            && item.Field28 == 0x12345678 && item.Field48 == 0x76543210 && item.InitialState == 2
            && item.Field53 == 0xbb && item.Flags == 2 && item.Field57 == 0xbb && item.Field58 == 0xbbbb,
            "signed20.12 geometry/delay and item opaque fields retain exact source integers");
        Check(item.States.Select(s => s.Code).SequenceEqual(new byte[] {2,17,10})
            && item.States.All(s => s.Flags == 0xbbbb) && item.States[0].WidgetId == 4698 && item.States[0].WidgetFlags == 0xabcd,
            "widget steady state and text transitions preserve source codes/flags");
        var style = item.States[1].Text!;
        Check(style.StringId == 2 && style.StringFlags == 0xeeee && style.StartColor == 0x12345678 && style.EndColor == 0x87654321
            && style.WrapWidth == 123 && style.Field10 == 0xbbbb && style.DurationFx == 10240
            && style.Size == 8 && style.Format1 == 4 && style.Format2 == 255 && style.Alignment == 2
            && style.Flags == 0 && style.Field21 == 0xbb && style.Field22 == 0xbbbb,
            "text colors, fractional duration, formats/alignment andBB padding preserved");
        Check(item.Actions.Single() is { Kind:0, ArrivalCode:2, TargetItem:0, TargetPage:255, TargetCode:10 },
            "on-arrival transition-to-Hidden code remains exact rather than rejected as a nonsteady code");
        Check(document.Pages.Take(7).Select(p => p.Timers.Single().DelayFx).SequenceEqual(new[] {140,140,140,140,170,140,60}.Select(x=>x*4096))
            && document.Pages.Skip(7).All(p => p.Timers.Count == 0), "authored credits timers and timer-free results are detached source facts");
        Check(document.Pages[6].Timers.Single().Action.Calls.Single() == new Source.Call(0,12)
            && document.Pages[7].Actions.Single().Calls.Single() == new Source.Call(0,206)
            && page.Actions.Single().Calls.Single() == new Source.Call(0,11), "all three admitted callback pairs remain data without execution");
        var file = document.Widgets[4698].Files[0];
        Check(file.Path == "logo_screen/MAYA/invented_Model.bin" && file.Kind == 7 && file.Field2 == 0xbbbb && file.Field4 == 0x11112222
            && file.DeclaredLength > file.Path.Length && document.Widgets[4698].Files[1].Path == null,
            "backslash path normalized once, padded declared span retained, optional animation absent");
        Check(document.Strings[1].DeclaredLatin1 == "TEST\u0080*\u00e9" && document.Strings[1].ContentLatin1 == "TEST\u0080*\u00e9"
            && document.Strings[2].DeclaredLatin1 == "A\u0080!\u00e9\0\u00bbB" && document.Strings[2].ContentLatin1 == "A\u0080!\u00e9",
            "Latin1/two-byte glyph escapes remain exact with nonNUL and in-spanNUL/padding policies");
        Check(document.Pages is IList<Source.Page> {IsReadOnly:true} && page.Items is IList<Source.Item> {IsReadOnly:true}
            && item.States is IList<Source.State> {IsReadOnly:true} && item.Actions is IList<Source.Action> {IsReadOnly:true}
            && page.Timers is IList<Source.Timer> {IsReadOnly:true} && page.Actions is IList<Source.Action> {IsReadOnly:true}, "all nested source list interfaces read-only");
        Expect<NotSupportedException>(() => ((IList<Source.Page>)document.Pages).Clear(), "page list immutable");
        Expect<NotSupportedException>(() => ((IList<Source.Item>)page.Items)[0] = item, "item replacement immutable");
        Expect<NotSupportedException>(() => ((IList<Source.State>)item.States).Add(item.States[0]), "state addition immutable");
        Expect<NotSupportedException>(() => ((IList<Source.Action>)item.Actions).Clear(), "action clearing immutable");
        Expect<NotSupportedException>(() => ((IList<Source.Call>)page.Actions[0].Calls).Clear(), "callback list immutable");
        Expect<NotSupportedException>(() => ((IList<Source.WidgetFile>)document.Widgets[4698].Files).Clear(), "file list immutable");
        Expect<NotSupportedException>(() => ((IDictionary<int,Source.Widget>)document.Widgets).Clear(), "widget dictionary immutable");
        Expect<NotSupportedException>(() => ((IDictionary<int,Source.Text>)document.Strings)[1] = document.Strings[1], "text dictionary immutable");
        string hash = document.MarmSha256; Array.Fill(fixture.Marm,(byte)0); Array.Fill(fixture.Text,(byte)0);
        Check(document.MarmSha256 == hash && document.Pages[0].Items[0].States[0].WidgetId == 4698
            && document.Strings[1].DeclaredLatin1 == "TEST\u0080*\u00e9" && document.Widgets[4698].Files[0].Path!.EndsWith("invented_Model.bin"),
            "all returned records/strings detach from both caller input arrays");
        var fresh = Build(); Check(Source.Read(fresh.Marm,fresh.Text).MarmSha256 == hash, "repeat numeric fixture yields deterministic admission hash");
    }

    private static void NestedBounds()
    {
        var f = Build();
        foreach (var field in f.PointerFields)
        {
            foreach (uint value in new uint[] {uint.MaxValue, (uint)f.Marm.Length-1})
            { var bad=(byte[])f.Marm.Clone();Put(bad,field.Value,value);Reject(bad,f.Text,"nested record/list pointer range "+field.Key); }
        }
        foreach (var field in f.TextPointerFields)
        { var bad=(byte[])f.Text.Clone();Put(bad,field.Value,uint.MaxValue);Reject(f.Marm,bad,"localized nested pointer range "+field.Key); }
        foreach (var list in f.ListFields)
        {
            byte[] bad = f.Marm.Concat(new byte[4]).ToArray(); Put(bad,f.Marm.Length,(uint)list.RecordOffset);Put(bad,list.PointerField,(uint)f.Marm.Length);
            Reject(bad,f.Text,"unterminated "+list.Name+" list cannot scan beyond supplied input");
        }
        foreach (int size in new[] {0,1,3,19}) Reject(f.Marm[..size],f.Text,"MARM header extent "+size);
        foreach (int size in new[] {0,1,3}) Reject(f.Marm,f.Text[..size],"localized pointer-table extent "+size);
        foreach (string key in new[] {"page","item","stateWidget","stateText17","style","binding2","actionSkip","timer","call11","widget","fileModel"})
        {
            byte[] bad=(byte[])f.Marm.Clone(); int pointer = f.PointerFields[key]; Put(bad,pointer,(uint)f.Marm.Length-1);
            Reject(bad,f.Text,"truncated fixed record "+key);
        }
        // Unselected nested records contain deliberately invalid pointers and
        // state/index bytes from construction; only table-record spans matter.
        Check(Source.Read(f.Marm,f.Text).Widgets.Count==1,"unselected page/widget/text nested garbage is never parsed");
    }

    private static void Semantics()
    {
        var f=Build();
        var bytes = new (string Label,int Offset,byte Value)[] {
            ("page index",f.At["page"]+28,5), ("initial state",f.At["item"]+52,4),
            ("state kind",f.At["stateWidget"],2), ("state code",f.At["stateWidget"]+1,4),
            ("duplicate state",f.At["stateText17"]+1,2), ("text alignment",f.At["style"]+19,3),
            ("text format1",f.At["style"]+17,1), ("text format2",f.At["style"]+18,1),
            ("action kind",f.At["actionSkip"],2), ("arrival code",f.At["actionSkip"]+2,4),
            ("target code",f.At["actionArrival"]+23,4), ("target page",f.At["actionSkip"]+22,51)
        };
        foreach(var change in bytes){var bad=(byte[])f.Marm.Clone();bad[change.Offset]=change.Value;Reject(bad,f.Text,"unsupported "+change.Label);}
        foreach(var change in new (string Label,int Offset,uint Value)[] {
            ("page extension",f.At["page"]+4,1), ("negative item delay",f.At["item"]+20,uint.MaxValue),
            ("extra object",f.At["item"]+44,(uint)f.At["item"]), ("negative text duration",f.At["style"]+12,uint.MaxValue),
            ("negative timer",f.At["timer"],uint.MaxValue), ("widget ID",f.At["stateWidget"]+4,4699),
            ("string ID",f.At["binding2"],3), ("callback A",f.At["call11"],1), ("callback B",f.At["call11"]+4,999)
        }){var bad=(byte[])f.Marm.Clone();Put(bad,change.Offset,change.Value);Reject(bad,f.Text,"invalid "+change.Label);}
        foreach(var change in new (int Offset,ushort Value)[] { (f.At["page"]+26,2),(f.At["item"]+54,1),
            (f.At["actionArrival"]+20,1),(f.At["actionArrival"]+20,ushort.MaxValue) })
        {var bad=(byte[])f.Marm.Clone();Half(bad,change.Offset,change.Value);Reject(bad,f.Text,"declared count/ordinal/active target mismatch");}
        byte[] unusedTarget=(byte[])f.Marm.Clone();Half(unusedTarget,f.At["actionSkip"]+20,64000);
        Check(Source.Read(unusedTarget,f.Text).Pages[0].Actions[0].TargetItem==64000,"unused targetItem remains opaque when targetCode is0");
        byte[] noWidget=(byte[])f.Marm.Clone();Put(noWidget,f.At["stateWidget"]+4,0xabcdffff);
        Check(Source.Read(noWidget,f.Text).Widgets.Count==0,"FFFF widget sentinel does not retain another source widget");
        byte[] nonemptyLinks=f.Marm.Concat(new byte[12]).ToArray();int link=f.Marm.Length;Put(nonemptyLinks,link,(uint)(link+8));Put(nonemptyLinks,f.At["item"]+8,(uint)link);
        Reject(nonemptyLinks,f.Text,"links outside narrow credits profile refused explicitly");
    }

    private static void PathsAndText()
    {
        foreach(string path in new[] {"logo_screen\\MAYA\\invented_Model.bin","main menu\\Invented Model.bin","tostart\\invented.bin","gameoptions\\invented_Anim.bin"})
        {var f=Build(path);Check(Source.Read(f.Marm,f.Text).Widgets[4698].Files[0].Path==path.Replace('\\','/'),"observed source directory/case/space path policy "+path);}
        foreach(string path in new[] {"logo_screen/MAYA/COM10.bin","main menu/Interior Space/NULish.bin","gameoptions/COM10/NULish.bin","gameoptions/COM10.bin/invented.bin"})
        {var f=Build(path);Check(Source.Read(f.Marm,f.Text).Widgets[4698].Files[0].Path==path,"nondevice names and valid interior spaces preserved");}
        foreach(string path in new[] {"/logo_screen/file.bin","C:\\logo_screen\\file.bin","\\\\host\\logo_screen\\file.bin","logo_screen/../file.bin",
            "logo_screen/./file.bin","logo_screen//file.bin","logo_screen/file.png","frontend/file.bin","models/file.bin","http://logo_screen/file.bin",
            "gameoptions/file?.bin","gameoptions/é.bin","gameoptions/evil\n.bin"})
        {var f=Build(path);Reject(f.Marm,f.Text,"unsafe/unsupported filename "+path.Replace('\n','_'));}
        var fixture=Build();
        byte[] noNul=(byte[])fixture.Marm.Clone();Array.Fill(noNul,(byte)'x',fixture.At["filename"],fixture.Marm[fixture.At["fileModel"]+1]);
        Reject(noNul,fixture.Text,"filename requires NUL within declared padded span");
        byte[] afterSpan=(byte[])fixture.Marm.Clone();afterSpan[fixture.At["fileModel"]+1]=1;
        Reject(afterSpan,fixture.Text,"NUL after declared filename span cannot terminate filename");
        foreach(int field in new[]{4,10}){var bad=(byte[])fixture.Text.Clone();bad[fixture.At["textEntry2"]+field]^=1;Reject(fixture.Marm,bad,"duplicate localized offset/length disagreement");}
        byte[] empty=(byte[])fixture.Text.Clone();Half(empty,fixture.At["textEntry2"]+8,0);Half(empty,fixture.At["textEntry2"]+10,0);
        Check(Source.Read(fixture.Marm,empty).Strings[2] is {DeclaredLength:0,DeclaredLatin1:"",ContentLatin1:""},"legitimate empty selected text retains known empty span");
        byte[] nullAnimation=(byte[])fixture.Marm.Clone();nullAnimation[fixture.At["fileAnimation"]+1]=1;
        Reject(nullAnimation,fixture.Text,"absent optional animation requires zero declared span");
        // Windows strips trailing spaces/dots and resolves reserved device stems
        // before the first dot, including intermediate directory components.
        foreach(string segment in new[] {"MAYA ","MAYA.",".. ","..."})
        {var f=Build("logo_screen/"+segment+"/invented.bin");Reject(f.Marm,f.Text,"Windows-trimmed intermediate source path refused");}
        foreach(string segment in new[] {"CON","prn","AuX","nUl","CON.extra","con.bin","NUL ","CON .foo"}
            .Concat(Enumerable.Range(1,9).SelectMany(i=>new[]{"cOm"+i,"LpT"+i})))
        {
            foreach(string path in new[]{"logo_screen/"+segment+"/invented.bin","logo_screen/MAYA/"+segment+".bin"})
            {var f=Build(path);Reject(f.Marm,f.Text,"Windows-ambiguous source path refused");}
        }
    }

    private static void Budgets()
    {
        var f=Build();
        Reject(new byte[4*1024*1024+1],f.Text,"MARM input byte cap before parsing/hash retention");
        Reject(f.Marm,new byte[4*1024*1024+1],"text input byte cap before parsing/hash retention");
        foreach(var list in f.ListFields)
        {
            byte[] bad=f.Marm.Concat(new byte[(list.Maximum+2)*4]).ToArray();int at=f.Marm.Length;
            for(int i=0;i<=list.Maximum;i++)Put(bad,at+4*i,(uint)list.RecordOffset);
            Put(bad,list.PointerField,(uint)at);Reject(bad,f.Text,"nested pointer-list cap "+list.Name);
        }
        var amplified=Build(items:64,actions:16,calls:16);
        var budget=Expect<InvalidDataException>(()=>Source.Read(amplified.Marm,amplified.Text),"shared aliases count toward aggregate visits even when unique bytes are small");
        Check(budget.Message.Contains("aggregate work budget",StringComparison.Ordinal),"legal bounded alias graphs reach aggregate guard itself");
        var widgets=Build(items:64);var wm=new Blob();wm.Add(widgets.Marm);
        int itemTable=(int)BinaryPrimitives.ReadUInt32LittleEndian(widgets.Marm.AsSpan(widgets.At["page"]+8));
        for(int i=0;i<64;i++)
        {
            int item=(int)BinaryPrimitives.ReadUInt32LittleEndian(widgets.Marm.AsSpan(itemTable+4*i));
            int[] states=new int[3];
            for(int j=0;j<3;j++){states[j]=wm.Reserve(8);wm.Byte(states[j]+1,new byte[]{2,17,10}[j]);wm.Word(states[j]+4,(uint)(100+i*3+j));}
            wm.Word(item+12,(uint)wm.List(states));
        }
        var widgetBudget=Expect<InvalidDataException>(()=>Source.Read(wm.Bytes.ToArray(),widgets.Text),"many valid widget IDs cannot amplify retained source objects");
        Check(widgetBudget.Message.Contains("referenced-widget budget",StringComparison.Ordinal),"distinct widget guard precedes parsing unselected widget payloads");
        var texts=Build(items:64);var tm=new Blob();tm.Add(texts.Marm);var tt=new Blob();
        int table=tt.Reserve(385*4),payload=tt.Reserve(4096,(byte)'A');
        for(int id=0;id<384;id++){int entry=tt.Reserve(12);tt.Word(table+id*4,(uint)entry);tt.Word(entry,(uint)payload);tt.Word(entry+4,(uint)payload);tt.U16(entry+8,4096);tt.U16(entry+10,4096);}
        int textItems=(int)BinaryPrimitives.ReadUInt32LittleEndian(texts.Marm.AsSpan(texts.At["page"]+8));
        byte[] codes={1,2,3,10,11,17};
        for(int i=0;i<64;i++)
        {
            int item=(int)BinaryPrimitives.ReadUInt32LittleEndian(texts.Marm.AsSpan(textItems+4*i));int[] states=new int[6];
            for(int j=0;j<6;j++){int binding=tm.Reserve(8);tm.Word(binding,(uint)(i*6+j));tm.Word(binding+4,(uint)texts.At["style"]);states[j]=tm.Reserve(8);tm.Byte(states[j],1);tm.Byte(states[j]+1,codes[j]);tm.Word(states[j]+4,(uint)binding);}
            tm.Word(item+12,(uint)tm.List(states));
        }
        var retained=Expect<InvalidDataException>(()=>Source.Read(tm.Bytes.ToArray(),tt.Bytes.ToArray()),"aliased encoded text still counts each retained declared span");
        Check(retained.Message.Contains("retained-text budget",StringComparison.Ordinal),"retained text guard reached independently of encoded input and work limits");
        byte[] longText=new byte[2050*4+12];int record=2050*4;
        for(int i=0;i<2049;i++)Put(longText,i*4,(uint)record);
        Reject(f.Marm,longText,"global localized pointer table exceeds bounded string count");
        byte[] oversized=f.Text.Concat(new byte[4097]).ToArray();int span=f.Text.Length;
        Put(oversized,f.At["textEntry2"],(uint)span);Put(oversized,f.At["textEntry2"]+4,(uint)span);
        Half(oversized,f.At["textEntry2"]+8,4097);Half(oversized,f.At["textEntry2"]+10,4097);
        Reject(f.Marm,oversized,"single selected declared text span exceeds4096 despite available source bytes");
        Put(oversized,f.At["textEntry2"],uint.MaxValue);Put(oversized,f.At["textEntry2"]+4,uint.MaxValue);
        Half(oversized,f.At["textEntry2"]+8,7);Half(oversized,f.At["textEntry2"]+10,7);
        Reject(f.Marm,oversized,"matching duplicate text pointers cannot bypass bounded span arithmetic");
        var tiny=new byte[20];"MARM"u8.CopyTo(tiny);Put(tiny,8,uint.MaxValue);
        for(int i=0;i<8;i++)Reject(tiny,f.Text,"warm tiny impossible global pointer");
        long before=GC.GetAllocatedBytesForCurrentThread();Reject(tiny,f.Text,"tiny malicious input refuses without declared arrays");
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated<1024*1024,"tiny impossible pointer allocates less than1MiB warmed refusal allowance");
        Console.WriteLine($"CREDITS SOURCE ALLOCATION PASS: tiny invalid graph allocated {allocated} bytes before refusal.");
    }

    private static void CombinedActions()
    {
        // The source executor mutates the CURRENT item before invoking callbacks
        // and changing page. Destination cardinality must never authorize that
        // earlier access. Make the two page lists independent, not aliased.
        var invalid = CombinedFixture(currentItems:1, destinationItems:2);
        Reject(invalid.Marm,invalid.Text,"combined action must bound active target against current page, not longer destination");
        var valid = CombinedFixture(currentItems:2, destinationItems:1);
        var document = Source.Read(valid.Marm,valid.Text);
        Check(document.Pages[0].Items.Count==2 && document.Pages.Single(p=>p.Index==49).Items.Count==1
            && document.Pages[0].Actions.Single() is {TargetItem:1,TargetCode:1,TargetPage:49},
            "combined action retains valid current item even when destination has fewer items");
    }

    private static Fixture CombinedFixture(int currentItems,int destinationItems)
    {
        var f=Build();var m=new Blob();m.Add(f.Marm);
        int[] Items(int count)
        {
            var offsets=new int[count];
            for(int i=0;i<count;i++)
            { offsets[i]=m.Add(f.Marm.AsSpan(f.At["item"],60).ToArray());m.U16(offsets[i]+54,(ushort)i); }
            return offsets;
        }
        int current=m.List(Items(currentItems)),destination=m.List(Items(destinationItems));
        m.Word(f.At["page6"]+8,(uint)current);m.U16(f.At["page6"]+26,(ushort)currentItems);
        m.Word(f.At["page49"]+8,(uint)destination);m.U16(f.At["page49"]+26,(ushort)destinationItems);
        int action=m.Add(f.Marm.AsSpan(f.At["actionSkip"],24).ToArray());
        m.U16(action+20,1);m.Byte(action+22,49);m.Byte(action+23,1);
        m.Word(f.At["page6"]+12,(uint)m.List(action));
        return f with {Marm=m.Bytes.ToArray()};
    }

    private sealed record ListSite(string Name,int PointerField,int RecordOffset,int Maximum);
    private sealed record Fixture(byte[] Marm,byte[] Text,Dictionary<string,int> At,Dictionary<string,int> PointerFields,
        Dictionary<string,int> TextPointerFields,List<ListSite> ListFields);
    private sealed class Blob
    {
        internal readonly List<byte> Bytes=new();
        internal int Reserve(int n,byte fill=0){int at=Bytes.Count;Bytes.AddRange(Enumerable.Repeat(fill,n));return at;}
        internal int Add(byte[] b){int at=Bytes.Count;Bytes.AddRange(b);return at;}
        internal void Byte(int at,byte value)=>Bytes[at]=value;
        internal void Word(int at,uint value){byte[] b=new byte[4];Put(b,0,value);for(int i=0;i<4;i++)Bytes[at+i]=b[i];}
        internal void U16(int at,ushort value){Bytes[at]=(byte)value;Bytes[at+1]=(byte)(value>>8);}
        internal int List(params int[] values){int at=Reserve((values.Length+1)*4);for(int i=0;i<values.Length;i++)Word(at+4*i,(uint)values[i]);return at;}
    }
    private static Fixture Build(string filename="logo_screen\\MAYA\\invented_Model.bin",int items=1,int actions=1,int calls=1)
    {
        var m=new Blob();var t=new Blob();var at=new Dictionary<string,int>();var pointers=new Dictionary<string,int>();var textPointers=new Dictionary<string,int>();var lists=new List<ListSite>();
        m.Reserve(20);for(int i=0;i<4;i++)m.Byte(i,"MARM"u8[i]);
        m.U16(4,0x1616);m.Byte(6,12);m.Byte(7,51);m.Word(16,0xfeedbeef);
        int pageTable=m.Reserve(52*4),widgetTable=m.Reserve(4700*4);m.Word(8,(uint)pageTable);m.Word(12,(uint)widgetTable);
        int ignoredPage=m.Reserve(32);m.Word(ignoredPage+8,uint.MaxValue);
        for(int i=0;i<51;i++)m.Word(pageTable+4*i,(uint)ignoredPage);
        int ignoredWidget=m.Reserve(12);m.Word(ignoredWidget+8,uint.MaxValue);for(int i=0;i<4699;i++)m.Word(widgetTable+4*i,(uint)ignoredWidget);
        int textTable=t.Reserve(16),ignoredText=t.Reserve(12);t.Word(ignoredText,uint.MaxValue);t.U16(ignoredText+8,ushort.MaxValue);t.Word(0,(uint)ignoredText);
        int textEntry1=t.Reserve(12),textEntry2=t.Reserve(12);int text1=t.Add(new byte[]{84,69,83,84,0x80,42,0xe9}),text2=t.Add(new byte[]{65,0x80,33,0xe9,0,0xbb,66});
        t.Word(4,(uint)textEntry1);t.Word(8,(uint)textEntry2);
        foreach(var pair in new[]{(textEntry1,text1),(textEntry2,text2)}){t.Word(pair.Item1,(uint)pair.Item2);t.Word(pair.Item1+4,(uint)pair.Item2);t.U16(pair.Item1+8,7);t.U16(pair.Item1+10,7);}
        at["textEntry2"]=textEntry2;textPointers["entry"]=8;textPointers["textSpan"]=textEntry2;textPointers["duplicateSpan"]=textEntry2+4;
        int style=m.Reserve(24,0xbb);m.Word(style,0x12345678);m.Word(style+4,0x87654321);m.U16(style+8,123);m.Word(style+12,10240);m.Byte(style+16,8);m.Byte(style+17,4);m.Byte(style+18,255);m.Byte(style+19,2);m.Byte(style+20,0);at["style"]=style;
        int binding1=m.Reserve(8),binding2=m.Reserve(8);m.Word(binding1,0xeeee0001);m.Word(binding2,0xeeee0002);m.Word(binding1+4,(uint)style);m.Word(binding2+4,(uint)style);at["binding2"]=binding2;pointers["style"]=binding2+4;
        int widgetState=m.Reserve(8),text17=m.Reserve(8),text10=m.Reserve(8);
        foreach(var s in new[]{widgetState,text17,text10})m.U16(s+2,0xbbbb);
        m.Byte(widgetState+1,2);m.Word(widgetState+4,0xabcd0000|4698);m.Byte(text17,1);m.Byte(text17+1,17);m.Word(text17+4,(uint)binding2);m.Byte(text10,1);m.Byte(text10+1,10);m.Word(text10+4,(uint)binding1);
        at["stateWidget"]=widgetState;at["stateText17"]=text17;pointers["binding2"]=text17+4;
        int states=m.List(widgetState,text17,text10);pointers["stateWidget"]=states;pointers["stateText17"]=states+4;
        int Call(int code){int c=m.Reserve(8);m.Word(c+4,(uint)code);return c;}
        int call11=Call(11),call12=Call(12),call206=Call(206);at["call11"]=call11;
        int calls11=m.List(Enumerable.Repeat(call11,calls).ToArray()),calls12=m.List(call12),calls206=m.List(call206);pointers["call11"]=calls11;
        int Action(ushort kind,int callPointer,byte page=255,byte target=0,ushort item=ushort.MaxValue,byte arrive=0)
        {int a=m.Reserve(24);m.U16(a,kind);m.Byte(a+2,arrive);m.Byte(a+3,0xbb);m.U16(a+8,unchecked((ushort)-10));m.U16(a+10,20);m.U16(a+12,30);m.U16(a+14,40);m.Word(a+16,(uint)callPointer);m.U16(a+20,item);m.Byte(a+22,page);m.Byte(a+23,target);return a;}
        int skip=Action(3,calls11),ok=Action(1,calls206),arrival=Action(0,0,target:10,item:0,arrive:2);at["actionSkip"]=skip;at["actionArrival"]=arrival;
        int skipList=m.List(skip),okList=m.List(ok),arrivalList=m.List(Enumerable.Repeat(actions>1?skip:arrival,actions).ToArray());pointers["actionSkip"]=skipList;
        int[] itemOffsets=new int[items];
        for(int i=0;i<items;i++)
        {int it=m.Reserve(60);itemOffsets[i]=it;m.Word(it,0x11223344);m.Word(it+4,(uint)arrivalList);m.Word(it+12,(uint)states);m.Word(it+16,0x55667788);m.Word(it+20,14336);m.Word(it+24,unchecked((uint)-123));m.Word(it+28,0x12345678);m.Word(it+32,unchecked((uint)-126976));m.Word(it+36,782336);m.Word(it+40,unchecked((uint)-10240));m.Word(it+48,0x76543210);m.Byte(it+52,2);m.Byte(it+53,0xbb);m.U16(it+54,(ushort)i);m.Byte(it+56,2);m.Byte(it+57,0xbb);m.U16(it+58,0xbbbb);}
        int itemList=m.List(itemOffsets);at["item"]=itemOffsets[0];pointers["item"]=itemList;
        int firstPage=0,firstTimer=0,firstTimerList=0;
        foreach(int index in Selected)
        {int p=m.Reserve(32);at["page"+index]=p;m.Word(pageTable+index*4,(uint)p);m.Word(p,0x11112222);m.Word(p+8,(uint)itemList);m.Word(p+12,(uint)(index<49?skipList:okList));m.U16(p+20,ushort.MaxValue);m.U16(p+22,ushort.MaxValue);m.U16(p+24,ushort.MaxValue);m.U16(p+26,(ushort)items);m.Byte(p+28,(byte)index);m.Byte(p+29,0xbb);m.Byte(p+30,0xbb);m.Byte(p+31,(byte)(index<49?3:0));
         if(index<49){int timer=m.Reserve(32);m.Word(timer,(uint)((index==10?170:index==12?60:140)*4096));int a=Action(0,index==12?calls12:0,page:(byte)(index==12?255:index+1));for(int b=0;b<24;b++)m.Byte(timer+4+b,m.Bytes[a+b]);m.Byte(timer+28,0xbb);m.Byte(timer+29,0xbb);m.U16(timer+30,0xbbbb);int tl=m.List(timer);m.Word(p+16,(uint)tl);if(index==6){firstTimer=timer;firstTimerList=tl;}}
         if(index==6)firstPage=p;}
        at["page"]=firstPage;at["timer"]=firstTimer;pointers["page"]=pageTable+6*4;pointers["timer"]=firstTimerList;
        int nameBytes=Encoding.Latin1.GetByteCount(filename);int span=(nameBytes+1+3)/4*4;int name=m.Reserve(span,0xbb);byte[] encoded=Encoding.Latin1.GetBytes(filename);for(int i=0;i<encoded.Length;i++)m.Byte(name+i,encoded[i]);m.Byte(name+nameBytes,0);at["filename"]=name;
        int file=m.Reserve(12),anim=m.Reserve(12);m.Byte(file,7);m.Byte(file+1,(byte)span);m.U16(file+2,0xbbbb);m.Word(file+4,0x11112222);m.Word(file+8,(uint)name);m.Byte(anim,8);at["fileModel"]=file;at["fileAnimation"]=anim;
        int files=m.List(file,anim),widget=m.Reserve(12);m.Word(widget,0x20000);m.Word(widget+4,0x44444444);m.Word(widget+8,(uint)files);m.Word(widgetTable+4698*4,(uint)widget);at["widget"]=widget;pointers["widget"]=widgetTable+4698*4;pointers["fileModel"]=files;pointers["filename"]=file+8;
        lists.AddRange(new[]{new ListSite("pages",8,ignoredPage,128),new ListSite("widgets",12,ignoredWidget,8192),new ListSite("items",firstPage+8,itemOffsets[0],64),new ListSite("states",itemOffsets[0]+12,widgetState,16),new ListSite("pageActions",firstPage+12,skip,16),new ListSite("itemActions",itemOffsets[0]+4,arrival,16),new ListSite("timers",firstPage+16,firstTimer,8),new ListSite("calls",skip+16,call11,16),new ListSite("files",widget+8,file,2)});
        foreach(var list in lists)pointers["list-"+list.Name]=list.PointerField;
        return new(m.Bytes.ToArray(),t.Bytes.ToArray(),at,pointers,textPointers,lists);
    }
    private static void Put(byte[] bytes,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at),value);
    private static void Half(byte[] bytes,int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at),value);
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Reject(byte[] marm,byte[] text,string why)=>Expect<InvalidDataException>(()=>Source.Read(marm,text),why);
    private static void Check(bool value,string why){checks++;if(!value)throw new InvalidOperationException("Credits fixture: "+why);}
    private static T Expect<T>(System.Action action,string why)where T:Exception
    {checks++;try{action();}catch(T error){return error;}throw new InvalidOperationException("Credits expected "+typeof(T).Name+": "+why);}
}
