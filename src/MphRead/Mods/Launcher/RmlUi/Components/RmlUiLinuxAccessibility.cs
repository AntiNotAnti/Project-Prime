#if !ANDROID
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MphRead.Mods.Launcher.RmlUi.Components;

/// <summary>A real AT-SPI application on the optional Linux accessibility bus.
/// A private GLib worker exports the standard D-Bus interfaces. It reads only
/// immutable published metadata and queues owner-validated native actions.</summary>
public sealed partial class RmlUiLinuxAccessibility : IDisposable
{
    internal const string RootPath = "/org/a11y/atspi/accessible/root", NodePath = "/org/a11y/atspi/accessible";
    private const string NullPath = "/org/a11y/atspi/null", CachePath = "/org/a11y/atspi/cache";
    private const string Accessible = "org.a11y.atspi.Accessible", Application = "org.a11y.atspi.Application",
        Component = "org.a11y.atspi.Component", Action = "org.a11y.atspi.Action", Editable = "org.a11y.atspi.EditableText",
        Cache = "org.a11y.atspi.Cache", Registry = "org.a11y.atspi.Registry";
    internal sealed record Tree(RmlUiAccessibilitySnapshot Snapshot, RmlUiUiaRect Bounds, bool ScreenCoordinates)
    {
        public static Tree Empty { get; } = new(RmlUiAccessibilitySnapshot.Empty, default, false);
        public string Prefix => $"n_{Snapshot.Document.Generation}_{Snapshot.Document.DocumentId}_{Snapshot.Revision}_";
        public string Path(int index) => index < 0 ? RootPath : NodePath + "/" + Prefix + index.ToString(CultureInfo.InvariantCulture);
        public int Index(string node)
        {
            if (node == "root") return -1;
            return node.StartsWith(Prefix, StringComparison.Ordinal)
                && int.TryParse(node.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int i)
                && i >= 0 && i < Snapshot.Nodes.Count ? i : -2;
        }
        public RmlUiUiaRect Rectangle(int index, uint coordinates)
        {
            if (coordinates > 2 || coordinates == 0 && !ScreenCoordinates) return new(-1,-1,0,0);
            double originX = coordinates == 0 ? Bounds.Left : 0, originY = coordinates == 0 ? Bounds.Top : 0;
            if (index < 0) return new(originX, originY, Bounds.Width, Bounds.Height);
            var node = Snapshot.Nodes[index];
            double sx = Snapshot.FramebufferWidth > 0 ? Bounds.Width / Snapshot.FramebufferWidth : 1;
            double sy = Snapshot.FramebufferHeight > 0 ? Bounds.Height / Snapshot.FramebufferHeight : 1;
            return new(originX + node.X*sx,originY + node.Y*sy,node.Width*sx,node.Height*sy);
        }
    }
    private readonly RmlUiAccessibilityService _service;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread? _worker;
    private Tree _published = Tree.Empty, _exported = Tree.Empty;
    private volatile bool _disposed;
    private int _available, _applicationId;
    private nint _context, _connection, _info, _interfaceTable, _subtreeTable, _cancellable;
    private uint _subtreeId, _cacheId;
    private string _unique = "", _parentName = "", _parentPath = NullPath;
    private Async? _pending;
    private long _deadline, _retry;
    private readonly List<Delegate> _callbacks = new();
    public bool Available => Volatile.Read(ref _available) != 0;
    public RmlUiLinuxAccessibility(RmlUiAccessibilityService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if (!OperatingSystem.IsLinux()) return;
        _worker = new Thread(Run) { IsBackground=true, Name="Prime RmlUi AT-SPI" }; _worker.Start();
    }
    public void Publish(RmlUiAccessibilitySnapshot snapshot, RmlUiUiaRect contentBounds, bool screenCoordinates = true)
    {
        if (_disposed) return;
        var old = Volatile.Read(ref _published);
        if (ReferenceEquals(old.Snapshot,snapshot) && old.Bounds==contentBounds && old.ScreenCoordinates==screenCoordinates) return;
        Volatile.Write(ref _published,new(snapshot,contentBounds,screenCoordinates));
        try { _wake.Set(); } catch(ObjectDisposedException) { }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed=true; Volatile.Write(ref _available,0);Volatile.Write(ref _published,Tree.Empty);
        try { _wake.Set(); } catch(ObjectDisposedException) { }
        // The worker owns its private bus and native callback roots. A cancelled
        // in-flight registration is pumped to completion before those are freed.
        if (_worker != null && Thread.CurrentThread!=_worker) _worker.Join(120);
    }
    internal static string[] Interfaces(RmlUiAccessibilityNode? node) => node == null
        ? new[]{Accessible,Application,Component}
        : new[]{Accessible,Component}.Concat(Actions(node).Length>0?new[]{Action}:System.Array.Empty<string>())
            .Concat((node.Actions & RmlUiAccessibilityActions.SetText)!=0?new[]{Editable}:System.Array.Empty<string>()).ToArray();
    internal static RmlUiAccessibilityAction[] Actions(RmlUiAccessibilityNode node)
    {
        var actions=new List<RmlUiAccessibilityAction>(4);
        if((node.Actions&RmlUiAccessibilityActions.Press)!=0)actions.Add(RmlUiAccessibilityAction.Press);
        if((node.Actions&RmlUiAccessibilityActions.Focus)!=0)actions.Add(RmlUiAccessibilityAction.Focus);
        if((node.Actions&RmlUiAccessibilityActions.Scroll)!=0) { actions.Add(RmlUiAccessibilityAction.ScrollForward);actions.Add(RmlUiAccessibilityAction.ScrollBackward); }
        return actions.ToArray();
    }
    internal static uint Role(RmlUiAccessibilityNode? n) => n==null?75u:n.Protected?40u:n.Role switch {
        "button"=>43,"checkbox" or "switch"=>7,"combobox"=>11,"dialog"=>16,"img"=>27,"list"=>31,"listitem"=>32,
        "tab"=>37,"tablist"=>38,"radio"=>44,"slider"=>51,"status"=>54,"alert"=>2,"textbox"=>79,"heading"=>83,"link"=>88,
        "text"=>29,_=>39 };
    internal static uint[] State(RmlUiAccessibilityNode? n)
    {
        uint first=1u<<30; // Visible in the active semantic tree, even when clipped.
        if(n==null)first|=(1u<<8)|(1u<<24)|(1u<<25);
        else {
            if(n.Enabled)first|=(1u<<8)|(1u<<24);
            if(!n.Offscreen)first|=1u<<25;
            if(n.Focused)first|=1u<<12;
            if((n.Actions&RmlUiAccessibilityActions.Focus)!=0)first|=1u<<11;
            if((n.Actions&RmlUiAccessibilityActions.SetText)!=0)first|=1u<<7;
        }
        return new[]{first,0u}; // No invented selection/check/value state.
    }
    private bool Queue(Tree tree,int index,RmlUiAccessibilityAction action,string text="") => !_disposed && index>=0
        && _service.Enqueue(new(tree.Snapshot.Document,tree.Snapshot.Revision,tree.Snapshot.Nodes[index].Key,action,text));
    private void Run()
    {
        try {
            _context=N.g_main_context_new();N.g_main_context_push_thread_default(_context);
            _info=N.g_dbus_node_info_new_for_xml(Introspection,out nint error);FreeError(error);
            if(_info==0)return;
            BuildVtables();
            while(!_disposed || _pending!=null) {
                for(int i=0;i<64 && N.g_main_context_iteration(_context,0)!=0;i++) { }
                if(_disposed) { if(_cancellable!=0)N.g_cancellable_cancel(_cancellable);_wake.WaitOne(8);continue; }
                if(_cancellable!=0 && Environment.TickCount64>=_deadline)N.g_cancellable_cancel(_cancellable);
                if(_connection!=0 && N.g_dbus_connection_is_closed(_connection)!=0)RetireConnection();
                if(_connection==0 && _pending==null && Environment.TickCount64>=_retry)Discover();
                if(Available && !ReferenceEquals(_exported,Volatile.Read(ref _published))) PublishNative();
                _wake.WaitOne(8);
            }
        } catch(Exception error) when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
        catch { /* Optional IPC failures expose no bus details or input payloads. */ }
        finally {
            Volatile.Write(ref _available,0);
            try { RetireConnection(); } catch { }
            if(_info!=0)N.g_dbus_node_info_unref(_info);
            if(_subtreeTable!=0)Marshal.FreeHGlobal(_subtreeTable);
            if(_interfaceTable!=0)Marshal.FreeHGlobal(_interfaceTable);
            if(_context!=0){N.g_main_context_pop_thread_default(_context);N.g_main_context_unref(_context);}
            _callbacks.Clear();_wake.Dispose();
        }
    }
    private void Discover()
    {
        _retry=Environment.TickCount64+2000;
        string session=Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")??"";
        if(session.Length==0 || session.Length>16384)return;
        Connect(session, connection => {
            string address="";
            try {
                nint reply=N.g_dbus_connection_call_sync(connection,"org.a11y.Bus","/org/a11y/bus","org.a11y.Bus","GetAddress",Tuple(),0,0,500,0,out nint error);
                FreeError(error);
                if(reply!=0){try{address=ChildString(reply,0,16384);}finally{N.g_variant_unref(reply);}}
            } catch { }
            finally{N.g_dbus_connection_close(connection,0,null,0);N.g_object_unref(connection);}
            if(!_disposed && address.Length>0){try{Connect(address,AttachBus);}catch{}}
        });
    }
    private void Connect(string address,Action<nint> completed)
    {
        _cancellable=N.g_cancellable_new();_deadline=Environment.TickCount64+700;
        _pending=(_,result,_)=> {
            nint cancellable=_cancellable;_cancellable=0;_pending=null;
            try {
                nint connection=N.g_dbus_connection_new_for_address_finish(result,out nint error);FreeError(error);
                if(connection==0)return;
                N.g_dbus_connection_set_exit_on_close(connection,0);
                if(_disposed){N.g_dbus_connection_close(connection,0,null,0);N.g_object_unref(connection);return;}
                completed(connection);
            } catch { /* Never unwind through a GLib asynchronous callback. */
            } finally { if(cancellable!=0)N.g_object_unref(cancellable); }
        };
        try{N.g_dbus_connection_new_for_address(address,1|8,0,_cancellable,_pending,0);}
        catch{N.g_object_unref(_cancellable);_cancellable=0;_pending=null;throw;}
    }
    private void AttachBus(nint connection)
    {
        try {
        _connection=connection;_unique=Marshal.PtrToStringUTF8(N.g_dbus_connection_get_unique_name(connection))??"";
        _subtreeId=N.g_dbus_connection_register_subtree(connection,NodePath,_subtreeTable,1,0,0,out nint error);FreeError(error);
        _cacheId=N.g_dbus_connection_register_object(connection,CachePath,Info(Cache),_interfaceTable,0,0,out error);FreeError(error);
        if(_subtreeId==0 || _cacheId==0){RetireConnection();return;}
        _cancellable=N.g_cancellable_new();_deadline=Environment.TickCount64+700;
        _pending=(source,result,_)=> {
            try {
                nint reply=N.g_dbus_connection_call_finish(source,result,out nint err);FreeError(err);
                if(reply==0){RetireConnection();return;}
                try {
                    nint reference=N.g_variant_get_child_value(reply,0);
                    try{_parentName=ChildString(reference,0,16384);_parentPath=ChildString(reference,1,16384);}
                    finally{N.g_variant_unref(reference);}
                    if(!_disposed)Volatile.Write(ref _available,1);
                } finally{N.g_variant_unref(reply);}
            } catch { RetireConnection(); }
            finally{if(_cancellable!=0)N.g_object_unref(_cancellable);_cancellable=0;_pending=null;}
        };
        // Embed invokes our writable Application.Id property before returning.
        // The async call allows the same private GLib context to handle it.
        N.g_dbus_connection_call(connection,Registry,RootPath,"org.a11y.atspi.Socket","Embed",Tuple(Reference(_unique,RootPath)),0,0,500,_cancellable,_pending,0);
        } catch {
            _pending=null;if(_cancellable!=0)N.g_object_unref(_cancellable);_cancellable=0;
            RetireConnection();
        }
    }
    private void RetireConnection()
    {
        Volatile.Write(ref _available,0);
        if(_connection!=0) {
            if(_subtreeId!=0)N.g_dbus_connection_unregister_subtree(_connection,_subtreeId);
            if(_cacheId!=0)N.g_dbus_connection_unregister_object(_connection,_cacheId);
            N.g_dbus_connection_close(_connection,0,null,0);N.g_object_unref(_connection);
        }
        _connection=0;_subtreeId=_cacheId=0;_exported=Tree.Empty;_unique=_parentName="";_parentPath=NullPath;
        _retry=Environment.TickCount64+2000;
    }
    private void BuildVtables()
    {
        Method method=MethodCall;GetProperty getter=Property;SetProperty setter=Set;
        Enumerate enumerate=(_,_,_,_)=> {
            try {
            var tree=Volatile.Read(ref _published);var names=new string[tree.Snapshot.Nodes.Count+1];names[0]="root";
            for(int i=0;i<tree.Snapshot.Nodes.Count;i++)names[i+1]=tree.Prefix+i.ToString(CultureInfo.InvariantCulture);
            nint result=N.g_malloc0((nuint)((names.Length+1)*IntPtr.Size));
            for(int i=0;i<names.Length;i++)Marshal.WriteIntPtr(result,i*IntPtr.Size,N.g_strdup(names[i]));return result;
            }catch{return 0;}
        };
        Introspect introspect=(_,_,_,node,_)=> {
            try {
            string name=Marshal.PtrToStringUTF8(node)??"";var tree=Volatile.Read(ref _published);int index=tree.Index(name);
            if(index==-2)return 0;
            var names=Interfaces(index<0?null:tree.Snapshot.Nodes[index]);nint result=N.g_malloc0((nuint)((names.Length+1)*IntPtr.Size));
            for(int i=0;i<names.Length;i++)Marshal.WriteIntPtr(result,i*IntPtr.Size,N.g_dbus_interface_info_ref(Info(names[i])));return result;
            }catch{return 0;}
        };
        Dispatch dispatch=(nint connection,nint sender,nint path,nint iface,nint node,out nint data,nint user)=> {
            data=0;try{var tree=Volatile.Read(ref _published);int index=tree.Index(Marshal.PtrToStringUTF8(node)??"");
            return index!=-2 && Interfaces(index<0?null:tree.Snapshot.Nodes[index]).Contains(Marshal.PtrToStringUTF8(iface)??"")?_interfaceTable:0;
            }catch{return 0;}
        };
        _callbacks.AddRange(new Delegate[]{method,getter,setter,enumerate,introspect,dispatch});
        _interfaceTable=Vtable(method,getter,setter);_subtreeTable=Vtable(enumerate,introspect,dispatch);
    }
    private static nint Vtable(params Delegate[] callbacks)
    {
        nint pointer=Marshal.AllocHGlobal(11*IntPtr.Size);
        for(int i=0;i<11;i++)Marshal.WriteIntPtr(pointer,i*IntPtr.Size,i<callbacks.Length?Marshal.GetFunctionPointerForDelegate(callbacks[i]):0);
        return pointer;
    }
    private nint Info(string name)=>N.g_dbus_node_info_lookup_interface(_info,name);
    private void MethodCall(nint connection,nint sender,nint objectPath,nint interfaceName,nint methodName,nint parameters,nint invocation,nint data)
    {
        try {
            var tree=Volatile.Read(ref _published);string path=Marshal.PtrToStringUTF8(objectPath)??"";
            string iface=Marshal.PtrToStringUTF8(interfaceName)??"",method=Marshal.PtrToStringUTF8(methodName)??"";
            if(path==CachePath && iface==Cache && method=="GetItems"){Reply(invocation,Array("((so)(so)(so)iiassusau)",Enumerable.Range(-1,tree.Snapshot.Nodes.Count+1).Select(i=>CacheItem(tree,i)).ToArray()));return;}
            int index=tree.Index(path.StartsWith(NodePath+"/",StringComparison.Ordinal)?path[(NodePath.Length+1)..]:"");
            if(index==-2){Error(invocation,"org.a11y.atspi.Error.Defunct","Accessible object is retired");return;}
            var node=index<0?null:tree.Snapshot.Nodes[index];
            if(iface==Accessible) {
                switch(method) {
                    case "GetChildAtIndex": int child=ChildInt(parameters,0);if(index>=0 || child<0 || child>=tree.Snapshot.Nodes.Count){Error(invocation,"org.freedesktop.DBus.Error.InvalidArgs","Child index is unavailable");return;}Reply(invocation,Reference(_unique,tree.Path(child)));return;
                    case "GetChildren": Reply(invocation,Array("(so)",index<0?Enumerable.Range(0,tree.Snapshot.Nodes.Count).Select(i=>Reference(_unique,tree.Path(i))).ToArray():System.Array.Empty<nint>()));return;
                    case "GetIndexInParent":Reply(invocation,Int(index));return;
                    case "GetRelationSet":Reply(invocation,Array("(ua(so))"));return;
                    case "GetRole":Reply(invocation,UInt(Role(node)));return;
                    case "GetRoleName":case "GetLocalizedRoleName":Reply(invocation,String(node==null?"application":node.Protected?"password text":node.Role));return;
                    case "GetState":Reply(invocation,Array("u",State(node).Select(UInt).ToArray()));return;
                    case "GetAttributes":Reply(invocation,Array("{ss}",N.g_variant_new_dict_entry(String("toolkit"),String("RmlUi"))));return;
                    case "GetApplication":Reply(invocation,Reference(_unique,RootPath));return;
                    case "GetInterfaces":Reply(invocation,Array("s",Interfaces(node).Select(String).ToArray()));return;
                }
            } else if(iface==Application && index<0) {
                if(method=="GetLocale"){Reply(invocation,String(CultureInfo.CurrentUICulture.Name.Replace('-','_')));return;}
                if(method=="GetApplicationBusAddress"){Reply(invocation,String(""));return;}
            } else if(iface==Component) {
                uint coordinates=method is "Contains" or "GetAccessibleAtPoint"?(uint)ChildInt(parameters,2):method is "GetExtents" or "GetPosition"?(uint)ChildInt(parameters,0):1;
                var rect=tree.Rectangle(index,coordinates);
                switch(method) {
                    case "Contains":Reply(invocation,Bool(Contains(rect,ChildInt(parameters,0),ChildInt(parameters,1))));return;
                    case "GetAccessibleAtPoint":int x=ChildInt(parameters,0),y=ChildInt(parameters,1);int hit=index;
                        if(index<0)hit=Enumerable.Range(0,tree.Snapshot.Nodes.Count).LastOrDefault(i=>!tree.Snapshot.Nodes[i].Offscreen&&Contains(tree.Rectangle(i,coordinates),x,y),-2);
                        Reply(invocation,Reference(hit==-2?"":_unique,hit==-2?NullPath:tree.Path(hit)));return;
                    case "GetExtents":Reply(invocation,Tuple(Int(Round(rect.Left)),Int(Round(rect.Top)),Int(Round(rect.Width)),Int(Round(rect.Height))));return;
                    case "GetPosition":N.g_dbus_method_invocation_return_value(invocation,Tuple(Int(Round(rect.Left)),Int(Round(rect.Top))));return;
                    case "GetSize":N.g_dbus_method_invocation_return_value(invocation,Tuple(Int(Round(rect.Width)),Int(Round(rect.Height))));return;
                    case "GetLayer":Reply(invocation,UInt(3));return;
                    case "GetMDIZOrder":Reply(invocation,N.g_variant_new_int16(0));return;
                    case "GetAlpha":Reply(invocation,N.g_variant_new_double(1));return;
                    case "GrabFocus":Reply(invocation,Bool(Queue(tree,index,RmlUiAccessibilityAction.Focus)));return;
                    case "ScrollTo":Reply(invocation,Bool(ChildInt(parameters,0)==6 && Queue(tree,index,RmlUiAccessibilityAction.Focus)));return;
                    case "SetExtents":case "SetPosition":case "SetSize":case "ScrollToPoint":Reply(invocation,Bool(false));return;
                }
            } else if(iface==Action && node!=null) {
                var actions=Actions(node);int actionIndex=method=="GetActions"?-1:ChildInt(parameters,0);
                if(method=="GetActions"){Reply(invocation,Array("(sss)",actions.Select(a=>Tuple(String(ActionName(a)),String(""),String(""))).ToArray()));return;}
                if(actionIndex<0||actionIndex>=actions.Length){Error(invocation,"org.freedesktop.DBus.Error.InvalidArgs","Action index is unavailable");return;}
                switch(method) {
                    case "DoAction":Reply(invocation,Bool(Queue(tree,index,actions[actionIndex])));return;
                    case "GetName":case "GetLocalizedName":Reply(invocation,String(ActionName(actions[actionIndex])));return;
                    case "GetDescription":case "GetKeyBinding":Reply(invocation,String(""));return;
                }
            } else if(iface==Editable && node!=null && (node.Actions&RmlUiAccessibilityActions.SetText)!=0) {
                if(method=="SetTextContents"){Reply(invocation,Bool(Queue(tree,index,RmlUiAccessibilityAction.SetText,ChildString(parameters,0,131072))));return;}
                // Partial edits/clipboard require private field contents. Do not
                // claim a Text interface or synthesize empty editable values.
                Error(invocation,"org.freedesktop.DBus.Error.NotSupported","Partial text access is unavailable");return;
            }
            Error(invocation,"org.freedesktop.DBus.Error.UnknownMethod","Accessibility method is unavailable");
        } catch { Error(invocation,"org.freedesktop.DBus.Error.Failed","Accessibility request rejected"); }
    }
    private nint Property(nint connection,nint sender,nint objectPath,nint interfaceName,nint propertyName,nint error,nint data)
    {
        try {
            var tree=Volatile.Read(ref _published);string path=Marshal.PtrToStringUTF8(objectPath)??"";
            string iface=Marshal.PtrToStringUTF8(interfaceName)??"",name=Marshal.PtrToStringUTF8(propertyName)??"";
            if(iface==Cache && path==CachePath && name=="version")return UInt(1);
            int index=tree.Index(path.StartsWith(NodePath+"/",StringComparison.Ordinal)?path[(NodePath.Length+1)..]:"");
            if(index==-2)return 0;var node=index<0?null:tree.Snapshot.Nodes[index];
            if(iface==Accessible)return name switch {
                "version"=>UInt(1),"Name"=>String(node?.Label??"Project Prime"),"Description" or "HelpText"=>String(""),
                "Parent"=>index<0?Reference(_parentName,_parentPath):Reference(_unique,RootPath),"ChildCount"=>Int(index<0?tree.Snapshot.Nodes.Count:0),
                "Locale"=>String(CultureInfo.CurrentUICulture.Name.Replace('-','_')),"AccessibleId"=>String(node==null?"project-prime":node.Id.Length>0?node.Id:node.Key),_=>0 };
            if(iface==Application && index<0)return name switch {
                "ToolkitName"=>String("RmlUi"),"ToolkitVersion" or "Version"=>String("6.3"),"AtspiVersion"=>String("2.1"),"InterfaceVersion"=>UInt(1),"Id"=>Int(_applicationId),_=>0 };
            if(name=="version")return UInt(1);
            if(iface==Action && node!=null && name=="NActions")return Int(Actions(node).Length);
        } catch { }
        return 0;
    }
    private int Set(nint connection,nint sender,nint objectPath,nint interfaceName,nint propertyName,nint value,nint error,nint data)
    {
        try {
            if(Marshal.PtrToStringUTF8(objectPath)==RootPath && Marshal.PtrToStringUTF8(interfaceName)==Application
                && Marshal.PtrToStringUTF8(propertyName)=="Id") {_applicationId=N.g_variant_get_int32(value);return 1;}
        } catch { }
        return 0;
    }
    private nint CacheItem(Tree tree,int index)
    {
        var node=index<0?null:tree.Snapshot.Nodes[index];
        return Tuple(Reference(_unique,tree.Path(index)),Reference(_unique,RootPath),index<0?Reference(_parentName,_parentPath):Reference(_unique,RootPath),
            Int(index),Int(index<0?tree.Snapshot.Nodes.Count:0),Array("s",Interfaces(node).Select(String).ToArray()),String(node?.Label??"Project Prime"),
            UInt(Role(node)),String(""),Array("u",State(node).Select(UInt).ToArray()));
    }
    private void PublishNative()
    {
        var next=Volatile.Read(ref _published);var previous=_exported;_exported=next;
        for(int i=0;i<previous.Snapshot.Nodes.Count;i++)Event(RootPath,"ChildrenChanged","remove",i,Reference(_unique,previous.Path(i)));
        for(int i=0;i<next.Snapshot.Nodes.Count;i++)Event(RootPath,"ChildrenChanged","add",i,Reference(_unique,next.Path(i)));
        for(int i=0;i<next.Snapshot.Nodes.Count;i++)if(next.Snapshot.Nodes[i].Focused)Event(next.Path(i),"StateChanged","focused",1,Int(0));
        Event(RootPath,"VisibleDataChanged","",0,Int(0));
    }
    private void Event(string path,string signal,string detail,int first,nint value)
    {
        N.g_dbus_connection_emit_signal(_connection,null,path,"org.a11y.atspi.Event.Object",signal,
            Tuple(String(detail),Int(first),Int(0),N.g_variant_new_variant(value),Array("{sv}")),out nint error);FreeError(error);
    }
    private static string ActionName(RmlUiAccessibilityAction a)=>a switch { RmlUiAccessibilityAction.Press=>"click",RmlUiAccessibilityAction.Focus=>"focus",RmlUiAccessibilityAction.ScrollForward=>"scroll-forward",_=>"scroll-backward" };
    private static bool Contains(RmlUiUiaRect r,int x,int y)=>r.Width>0 && r.Height>0 && x>=r.Left && y>=r.Top && x<r.Left+r.Width && y<r.Top+r.Height;
    private static int Round(double value)=>(int)Math.Clamp(Math.Round(value),int.MinValue,int.MaxValue);
    private static nint String(string value)=>N.g_variant_new_string(value);
    private static nint Int(int value)=>N.g_variant_new_int32(value);
    private static nint UInt(uint value)=>N.g_variant_new_uint32(value);
    private static nint Bool(bool value)=>N.g_variant_new_boolean(value?1:0);
    private static nint Tuple(params nint[] children)=>N.g_variant_new_tuple(children,(nuint)children.Length);
    private static nint Reference(string bus,string path)=>Tuple(String(bus),N.g_variant_new_object_path(path));
    private static nint Array(string signature,params nint[] children) {
        nint type=N.g_variant_type_new(signature);try{return N.g_variant_new_array(type,children,(nuint)children.Length);}finally{N.g_variant_type_free(type);}
    }
    private static void Reply(nint invocation,nint value)=>N.g_dbus_method_invocation_return_value(invocation,Tuple(value));
    private static void Error(nint invocation,string name,string message)=>N.g_dbus_method_invocation_return_dbus_error(invocation,name,message);
    private static void FreeError(nint error){if(error!=0)N.g_error_free(error);}
    private static int ChildInt(nint tuple,int index) {
        if(tuple==0 || index<0 || (nuint)index>=N.g_variant_n_children(tuple))throw new InvalidOperationException("Invalid accessibility request");
        nint value=N.g_variant_get_child_value(tuple,(nuint)index);
        try {
            byte type=Marshal.ReadByte(N.g_variant_get_type_string(value));
            return type==(byte)'u'?unchecked((int)N.g_variant_get_uint32(value)):type==(byte)'i'?N.g_variant_get_int32(value):throw new InvalidOperationException("Invalid accessibility parameter type");
        }finally{N.g_variant_unref(value);}
    }
    private static string ChildString(nint tuple,int index,int limit) {
        if(tuple==0 || index<0 || (nuint)index>=N.g_variant_n_children(tuple))throw new InvalidOperationException("Invalid accessibility request");
        nint value=N.g_variant_get_child_value(tuple,(nuint)index);
        try {
            byte type=Marshal.ReadByte(N.g_variant_get_type_string(value));
            if(type!=(byte)'s' && type!=(byte)'o')throw new InvalidOperationException("Invalid accessibility parameter type");
            nint text=N.g_variant_get_string(value,out nuint length);if(length>(nuint)limit)throw new InvalidOperationException("Accessibility input exceeds capacity");
            if(length==0)return "";byte[] bytes=new byte[(int)length];Marshal.Copy(text,bytes,0,bytes.Length);return new UTF8Encoding(false,true).GetString(bytes);
        } finally{N.g_variant_unref(value);}
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Async(nint source,nint result,nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Method(nint connection,nint sender,nint path,nint iface,nint method,nint parameters,nint invocation,nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetProperty(nint connection,nint sender,nint path,nint iface,nint property,nint error,nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetProperty(nint connection,nint sender,nint path,nint iface,nint property,nint value,nint error,nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Enumerate(nint connection,nint sender,nint path,nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Introspect(nint connection,nint sender,nint path,nint node,nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Dispatch(nint connection,nint sender,nint path,nint iface,nint node,out nint dispatchedData,nint data);
    private static class N
    {
        private const string Glib="libglib-2.0.so.0",Gio="libgio-2.0.so.0",Gobject="libgobject-2.0.so.0";
        [DllImport(Glib)]internal static extern nint g_main_context_new();
        [DllImport(Glib)]internal static extern void g_main_context_push_thread_default(nint context);
        [DllImport(Glib)]internal static extern void g_main_context_pop_thread_default(nint context);
        [DllImport(Glib)]internal static extern void g_main_context_unref(nint context);
        [DllImport(Glib)]internal static extern int g_main_context_iteration(nint context,int mayBlock);
        [DllImport(Glib)]internal static extern nint g_malloc0(nuint count);
        [DllImport(Glib)]internal static extern nint g_strdup([MarshalAs(UnmanagedType.LPUTF8Str)]string value);
        [DllImport(Glib)]internal static extern void g_error_free(nint error);
        [DllImport(Gobject)]internal static extern void g_object_unref(nint value);
        [DllImport(Gio)]internal static extern nint g_cancellable_new();
        [DllImport(Gio)]internal static extern void g_cancellable_cancel(nint value);
        [DllImport(Gio)]internal static extern void g_dbus_connection_new_for_address([MarshalAs(UnmanagedType.LPUTF8Str)]string address,uint flags,nint observer,nint cancellable,Async callback,nint data);
        [DllImport(Gio)]internal static extern nint g_dbus_connection_new_for_address_finish(nint result,out nint error);
        [DllImport(Gio)]internal static extern void g_dbus_connection_set_exit_on_close(nint connection,int exit);
        [DllImport(Gio)]internal static extern int g_dbus_connection_is_closed(nint connection);
        [DllImport(Gio)]internal static extern nint g_dbus_connection_get_unique_name(nint connection);
        [DllImport(Gio)]internal static extern void g_dbus_connection_close(nint connection,nint cancellable,Async? callback,nint data);
        [DllImport(Gio)]internal static extern nint g_dbus_connection_call_sync(nint connection,[MarshalAs(UnmanagedType.LPUTF8Str)]string bus,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,[MarshalAs(UnmanagedType.LPUTF8Str)]string iface,[MarshalAs(UnmanagedType.LPUTF8Str)]string method,nint parameters,nint resultType,uint flags,int timeout,nint cancellable,out nint error);
        [DllImport(Gio)]internal static extern void g_dbus_connection_call(nint connection,[MarshalAs(UnmanagedType.LPUTF8Str)]string bus,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,[MarshalAs(UnmanagedType.LPUTF8Str)]string iface,[MarshalAs(UnmanagedType.LPUTF8Str)]string method,nint parameters,nint resultType,uint flags,int timeout,nint cancellable,Async callback,nint data);
        [DllImport(Gio)]internal static extern nint g_dbus_connection_call_finish(nint connection,nint result,out nint error);
        [DllImport(Gio)]internal static extern nint g_dbus_node_info_new_for_xml([MarshalAs(UnmanagedType.LPUTF8Str)]string xml,out nint error);
        [DllImport(Gio)]internal static extern void g_dbus_node_info_unref(nint info);
        [DllImport(Gio)]internal static extern nint g_dbus_node_info_lookup_interface(nint info,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
        [DllImport(Gio)]internal static extern nint g_dbus_interface_info_ref(nint info);
        [DllImport(Gio)]internal static extern uint g_dbus_connection_register_subtree(nint connection,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,nint vtable,uint flags,nint data,nint destroy,out nint error);
        [DllImport(Gio)]internal static extern int g_dbus_connection_unregister_subtree(nint connection,uint id);
        [DllImport(Gio)]internal static extern uint g_dbus_connection_register_object(nint connection,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,nint info,nint vtable,nint data,nint destroy,out nint error);
        [DllImport(Gio)]internal static extern int g_dbus_connection_unregister_object(nint connection,uint id);
        [DllImport(Gio)]internal static extern void g_dbus_method_invocation_return_value(nint invocation,nint parameters);
        [DllImport(Gio)]internal static extern void g_dbus_method_invocation_return_dbus_error(nint invocation,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string message);
        [DllImport(Gio)]internal static extern int g_dbus_connection_emit_signal(nint connection,[MarshalAs(UnmanagedType.LPUTF8Str)]string? destination,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,[MarshalAs(UnmanagedType.LPUTF8Str)]string iface,[MarshalAs(UnmanagedType.LPUTF8Str)]string signal,nint parameters,out nint error);
        [DllImport(Glib)]internal static extern nint g_variant_new_string([MarshalAs(UnmanagedType.LPUTF8Str)]string value);
        [DllImport(Glib)]internal static extern nint g_variant_new_object_path([MarshalAs(UnmanagedType.LPUTF8Str)]string value);
        [DllImport(Glib)]internal static extern nint g_variant_new_int32(int value);
        [DllImport(Glib)]internal static extern nint g_variant_new_uint32(uint value);
        [DllImport(Glib)]internal static extern nint g_variant_new_boolean(int value);
        [DllImport(Glib)]internal static extern nint g_variant_new_int16(short value);
        [DllImport(Glib)]internal static extern nint g_variant_new_double(double value);
        [DllImport(Glib)]internal static extern nint g_variant_new_tuple(nint[] children,nuint count);
        [DllImport(Glib)]internal static extern nint g_variant_new_array(nint type,nint[] children,nuint count);
        [DllImport(Glib)]internal static extern nint g_variant_new_variant(nint child);
        [DllImport(Glib)]internal static extern nint g_variant_new_dict_entry(nint key,nint value);
        [DllImport(Glib)]internal static extern nint g_variant_type_new([MarshalAs(UnmanagedType.LPUTF8Str)]string signature);
        [DllImport(Glib)]internal static extern void g_variant_type_free(nint type);
        [DllImport(Glib)]internal static extern nint g_variant_get_type_string(nint value);
        [DllImport(Glib)]internal static extern nuint g_variant_n_children(nint value);
        [DllImport(Glib)]internal static extern nint g_variant_get_child_value(nint value,nuint index);
        [DllImport(Glib)]internal static extern nint g_variant_get_string(nint value,out nuint length);
        [DllImport(Glib)]internal static extern int g_variant_get_int32(nint value);
        [DllImport(Glib)]internal static extern uint g_variant_get_uint32(nint value);
        [DllImport(Glib)]internal static extern void g_variant_unref(nint value);
    }
}
#endif
