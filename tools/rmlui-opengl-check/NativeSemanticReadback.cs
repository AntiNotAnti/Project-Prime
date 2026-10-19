using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Launcher.RmlUi.Host;

/// <summary>Test-only readback of the actual native DOM's existing bounded accessibility projection.</summary>
internal sealed class NativeSemanticReadback
{
    private readonly (string Id, string Label)[] _nodes;
    internal string RawJson { get; }
    private NativeSemanticReadback((string Id, string Label)[] nodes, string raw) { _nodes = nodes; RawJson = raw; }

    internal static NativeSemanticReadback Read(RmlUiHost host, RmlUiDocumentToken document)
    {
        host.VerifyOwnerThread();
        if (!host.IsAlive(document) || !host.IsVisible(document)) throw new InvalidOperationException("Semantic document is not visible/alive.");
        var buffer = new byte[1024 * 1024 + 1];
        int count = Snapshot(document.DocumentId, buffer, buffer.Length);
        if (count <= 0 || count >= buffer.Length) throw new InvalidOperationException("Native semantic snapshot is missing or exceeds its bound.");
        string raw = Encoding.UTF8.GetString(buffer, 0, count);
        using var json = JsonDocument.Parse(raw);
        return new(json.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(node => (node.GetProperty("id").GetString() ?? "", node.GetProperty("label").GetString() ?? "")).ToArray(), raw);
    }

    internal string Label(string id) => _nodes.FirstOrDefault(node => node.Id == id).Label ?? "";
    internal bool ContainsText(string text)
    {
        string expected = Normalize(text);
        return expected.Length > 0 && _nodes.Any(node => node.Label.Contains(expected, StringComparison.Ordinal));
    }
    internal static string Normalize(string text) => String.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [DllImport("ProjectPrime.RmlUi.Native", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "pp_rmlui_document_accessibility_snapshot")]
    private static extern int Snapshot(ulong document, [Out] byte[] buffer, int capacity);
}
