using System.Reflection;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Components;

static class LinuxWireCheck
{
    private const string Gio="libgio-2.0.so.0",Glib="libglib-2.0.so.0";
    public static void Run(RmlUiAccessibilityService service)
    {
        if(OperatingSystem.IsWindows())return;
        var type=typeof(RmlUiLinuxAccessibility);
        string xml=(string)type.GetField("Introspection",BindingFlags.NonPublic|BindingFlags.Static)!.GetRawConstantValue()!;
        nint info=g_dbus_node_info_new_for_xml(xml,out nint error);
        if(error!=0){g_error_free(error);throw new InvalidOperationException("Actual GLib rejected standard AT-SPI introspection");}
        if(info==0)throw new InvalidOperationException("AT-SPI introspection absent");
        try {
            foreach(string name in new[]{"Accessible","Application","Component","Action","EditableText","Cache"})
                if(g_dbus_node_info_lookup_interface(info,"org.a11y.atspi."+name)==0)throw new InvalidOperationException("Missing standard AT-SPI interface");
            using var provider=new RmlUiLinuxAccessibility(service);
            var tree=new RmlUiLinuxAccessibility.Tree(service.Snapshot,new(200,100,640,360),true);
            var cache=type.GetMethod("CacheItem",BindingFlags.Instance|BindingFlags.NonPublic)!;
            foreach(int index in Enumerable.Range(-1,tree.Snapshot.Nodes.Count+1)) {
                nint item=(nint)cache.Invoke(provider,new object[]{tree,index})!;
                item=g_variant_ref_sink(item);
                try {
                    if(Marshal.PtrToStringUTF8(g_variant_get_type_string(item))!="((so)(so)(so)iiassusau)")throw new InvalidOperationException("AT-SPI actual wire tuple does not match primary Cache XML");
                } finally {g_variant_unref(item);}
            }
            Console.WriteLine("PASS actual GLib AT-SPI introspection and all root/node Cache wire signatures");
        } finally {g_dbus_node_info_unref(info);}
    }
    [DllImport(Gio)]private static extern nint g_dbus_node_info_new_for_xml([MarshalAs(UnmanagedType.LPUTF8Str)]string xml,out nint error);
    [DllImport(Gio)]private static extern nint g_dbus_node_info_lookup_interface(nint info,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [DllImport(Gio)]private static extern void g_dbus_node_info_unref(nint info);
    [DllImport(Glib)]private static extern void g_error_free(nint error);
    [DllImport(Glib)]private static extern nint g_variant_ref_sink(nint value);
    [DllImport(Glib)]private static extern nint g_variant_get_type_string(nint value);
    [DllImport(Glib)]private static extern void g_variant_unref(nint value);
}
