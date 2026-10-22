using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Mods.Campaign;

/// <summary>
/// Bounded, detached admission of original credits/results frontend data.
/// This performs no file IO, model loading, callback execution or presentation.
/// </summary>
public static class CampaignCreditsSource
{
    public const int MaxInputBytes = 4 * 1024 * 1024, MaxPages = 128, MaxWidgets = 8192, MaxStrings = 2048;
    public const int MaxItems = 64, MaxStates = 16, MaxActions = 16, MaxTimers = 8, MaxCalls = 16;
    public const int MaxReferencedWidgets = 128, MaxStringBytes = 4096, MaxRetainedTextBytes = 1024 * 1024;
    public const int MaxPathBytes = 512, MaxVisits = 32768;
    private static readonly int[] Selected = { 6, 7, 8, 9, 10, 11, 12, 49, 50 };

    public sealed class Document
    {
        public string MarmSha256 { get; }
        public string TextSha256 { get; }
        public ushort HeaderField4 { get; }
        public byte HeaderField6 { get; }
        public byte HeaderField7 { get; }
        public uint HeaderField16 { get; }
        public int GlobalPageCount { get; }
        public int GlobalWidgetCount { get; }
        public int GlobalStringCount { get; }
        public int PointerAndRecordVisits { get; }
        public IReadOnlyList<Page> Pages { get; }
        public IReadOnlyDictionary<int, Widget> Widgets { get; }
        public IReadOnlyDictionary<int, Text> Strings { get; }
        internal Document(string marm, string text, ushort field4, byte field6, byte field7, uint field16,
            int pages, int widgets, int strings, int visits, Page[] selected,
            Dictionary<int, Widget> retainedWidgets, Dictionary<int, Text> retainedText)
        {
            MarmSha256 = marm; TextSha256 = text; HeaderField4 = field4; HeaderField6 = field6;
            HeaderField7 = field7; HeaderField16 = field16; GlobalPageCount = pages;
            GlobalWidgetCount = widgets; GlobalStringCount = strings; PointerAndRecordVisits = visits;
            Pages = Array.AsReadOnly(selected);
            Widgets = new ReadOnlyDictionary<int, Widget>(retainedWidgets);
            Strings = new ReadOnlyDictionary<int, Text>(retainedText);
        }
    }
    public sealed record Page(int Index, uint Field0, ushort InitialFocus, ushort BackgroundA,
        ushort BackgroundB, byte Field29, byte Field30, byte Flags,
        IReadOnlyList<Item> Items, IReadOnlyList<Action> Actions, IReadOnlyList<Timer> Timers);
    public sealed record Item(int Index, uint Field0, uint RuntimeField16, int DelayFx, int Field24,
        uint Field28, int XFx, int YFx, int DepthFx, uint Field48, byte InitialState,
        byte Field53, byte Flags, byte Field57, ushort Field58,
        IReadOnlyList<State> States, IReadOnlyList<Action> Actions);
    public sealed record State(byte Kind, byte Code, ushort Flags, int? WidgetId, ushort WidgetFlags, TextStyle? Text);
    public sealed record TextStyle(int StringId, ushort StringFlags, uint StartColor, uint EndColor,
        ushort WrapWidth, ushort Field10, int DurationFx, byte Size, byte Format1, byte Format2,
        byte Alignment, byte Flags, byte Field21, ushort Field22);
    public sealed record Call(int A, int B);
    public sealed record Action(ushort Kind, byte ArrivalCode, byte Flags, uint RectangleMode,
        short X, short Y, short Width, short Height, IReadOnlyList<Call> Calls,
        ushort TargetItem, byte TargetPage, byte TargetCode);
    public sealed record Timer(int DelayFx, Action Action, byte Flags, byte Field29, ushort Field30);
    public sealed record Widget(int Id, uint Flags, uint RuntimeField4, IReadOnlyList<WidgetFile> Files);
    public sealed record WidgetFile(byte Kind, byte DeclaredLength, ushort Field2, uint Field4, string? Path);
    /// <summary>Latin1 preserves source bytes, including glyph escape pairs and the declared NUL/padding span.</summary>
    public sealed record Text(int Id, ushort DeclaredLength, string DeclaredLatin1)
    {
        public string ContentLatin1 => DeclaredLatin1.IndexOf('\0') is int end && end >= 0 ? DeclaredLatin1[..end] : DeclaredLatin1;
    }

    public static Document Read(ReadOnlySpan<byte> marm, ReadOnlySpan<byte> localizedText)
    {
        Require(marm.Length is >= 20 and <= MaxInputBytes && localizedText.Length is >= 4 and <= MaxInputBytes,
            "Credits source inputs exceed the supported extent.");
        Require(marm[..4].SequenceEqual("MARM"u8), "Credits source is not a MARM document.");
        var budget = new WorkBudget(); var r = new Reader(marm, budget); var text = new Reader(localizedText, budget);
        uint[] pages = r.Offsets(U32(marm, 8), MaxPages, 32);
        uint[] widgets = r.Offsets(U32(marm, 12), MaxWidgets, 12);
        uint[] strings = text.Offsets(0, MaxStrings, 12, zeroIsEmpty: false);
        Require(pages.Length > Selected[^1], "Credits/results pages are absent.");
        var widgetIds = new HashSet<int>(); var stringIds = new HashSet<int>();
        var selected = new Page[Selected.Length];
        for (int i = 0; i < Selected.Length; i++)
            selected[i] = ReadPage(ref r, Selected[i], pages, widgets.Length, strings.Length, widgetIds, stringIds);
        Require(widgetIds.Count <= MaxReferencedWidgets, "Credits referenced-widget budget exceeded.");
        var retainedWidgets = new Dictionary<int, Widget>();
        foreach (int id in widgetIds.Order()) retainedWidgets.Add(id, ReadWidget(ref r, id, widgets[id]));
        var retainedText = new Dictionary<int, Text>(); int textBytes = 0;
        foreach (int id in stringIds.Order())
        {
            var entry = text.Record(strings[id], 12);
            uint offset = U32(entry, 0); ushort length = U16(entry, 8);
            Require(offset == U32(entry, 4) && length == U16(entry, 10) && length <= MaxStringBytes,
                "Credits text entry has inconsistent offsets/lengths or exceeds its budget.");
            textBytes = checked(textBytes + length);
            Require(textBytes <= MaxRetainedTextBytes, "Credits retained-text budget exceeded.");
            var encoded = text.Bytes(offset, length);
            retainedText.Add(id, new(id, length, Encoding.Latin1.GetString(encoded)));
        }
        return new(Hash(marm), Hash(localizedText), U16(marm, 4), marm[6], marm[7], U32(marm, 16),
            pages.Length, widgets.Length, strings.Length, budget.Visits, selected, retainedWidgets, retainedText);
    }

    private static Page ReadPage(ref Reader r, int index, uint[] pages, int widgetCount, int stringCount,
        HashSet<int> widgetIds, HashSet<int> stringIds)
    {
        var page = r.Record(pages[index], 32);
        Require(page[28] == index && U32(page, 4) == 0, "Credits page identity or extension is unsupported.");
        uint[] items = r.Offsets(U32(page, 8), MaxItems, 60);
        Require(items.Length > 0 && U16(page, 26) == items.Length, "Credits page item count is inconsistent.");
        var retained = new Item[items.Length];
        for (int i = 0; i < items.Length; i++)
            retained[i] = ReadItem(ref r, items[i], i, index, pages, items.Length, widgetCount, stringCount, widgetIds, stringIds);
        Action[] actions = ReadActions(ref r, U32(page, 12), index, pages, items.Length);
        uint[] timers = r.Offsets(U32(page, 16), MaxTimers, 32); var retainedTimers = new Timer[timers.Length];
        for (int i = 0; i < timers.Length; i++)
        {
            var timer = r.Record(timers[i], 32); int delay = I32(timer, 0);
            Require(delay >= 0, "Credits timer has a negative delay.");
            retainedTimers[i] = new(delay, ReadAction(ref r, checked(timers[i] + 4), index, pages, items.Length),
                timer[28], timer[29], U16(timer, 30));
        }
        return new(index, U32(page, 0), U16(page, 20), U16(page, 22), U16(page, 24), page[29], page[30], page[31],
            Array.AsReadOnly(retained), Array.AsReadOnly(actions), Array.AsReadOnly(retainedTimers));
    }

    private static Item ReadItem(ref Reader r, uint offset, int ordinal, int page, uint[] pages, int itemCount,
        int widgetCount, int stringCount, HashSet<int> widgetIds, HashSet<int> stringIds)
    {
        var item = r.Record(offset, 60);
        Require(U16(item, 54) == ordinal && U32(item, 44) == 0 && item[52] is >= 1 and <= 3 && I32(item, 20) >= 0,
            "Credits item identity, initial state, delay or extra data is unsupported.");
        Require(r.Offsets(U32(item, 8), 16, 4).Length == 0, "Credits links are outside the supported profile.");
        uint[] states = r.Offsets(U32(item, 12), MaxStates, 8);
        Require(states.Length > 0, "Credits item states are absent.");
        var retained = new State[states.Length]; var codes = new HashSet<byte>();
        for (int i = 0; i < states.Length; i++)
        {
            var state = r.Record(states[i], 8); byte kind = state[0], code = state[1];
            Require(kind <= 1 && StateCode(code) && codes.Add(code), "Unsupported or duplicate credits visual state.");
            uint value = U32(state, 4);
            if (kind == 0)
            {
                int id = (ushort)value;
                Require(id == ushort.MaxValue || id < widgetCount, "Credits widget reference is outside its source table.");
                if (id != ushort.MaxValue) widgetIds.Add(id);
                Require(widgetIds.Count <= MaxReferencedWidgets, "Credits referenced-widget budget exceeded.");
                retained[i] = new(kind, code, U16(state, 2), id == ushort.MaxValue ? null : id, (ushort)(value >> 16), null);
            }
            else
            {
                var binding = r.Record(value, 8); int id = U16(binding, 0);
                Require(id < stringCount, "Credits string reference is outside its source table."); stringIds.Add(id);
                var style = r.Record(U32(binding, 4), 24);
                Require(I32(style, 12) >= 0 && style[19] <= 2
                    && style[17] is 0 or 2 or 4 && style[18] is 0 or 255, "Unsupported credits text style.");
                retained[i] = new(kind, code, U16(state, 2), null, 0,
                    new(id, U16(binding, 2), U32(style, 0), U32(style, 4), U16(style, 8), U16(style, 10),
                        I32(style, 12), style[16], style[17], style[18], style[19], style[20], style[21], U16(style, 22)));
            }
        }
        return new(ordinal, U32(item, 0), U32(item, 16), I32(item, 20), I32(item, 24), U32(item, 28),
            I32(item, 32), I32(item, 36), I32(item, 40), U32(item, 48), item[52], item[53], item[56], item[57], U16(item, 58),
            Array.AsReadOnly(retained), Array.AsReadOnly(ReadActions(ref r, U32(item, 4), page, pages, itemCount)));
    }

    private static Action[] ReadActions(ref Reader r, uint pointer, int page, uint[] pages, int items)
    {
        uint[] offsets = r.Offsets(pointer, MaxActions, 24); var result = new Action[offsets.Length];
        for (int i = 0; i < offsets.Length; i++) result[i] = ReadAction(ref r, offsets[i], page, pages, items);
        return result;
    }
    private static Action ReadAction(ref Reader r, uint offset, int currentPage, uint[] pages, int items)
    {
        var action = r.Record(offset, 24); ushort kind = U16(action, 0), targetItem = U16(action, 20);
        byte targetPage = action[22], targetCode = action[23];
        Require(kind is 0 or 1 or 3 && (action[2] == 0 || StateCode(action[2]))
            && (targetCode == 0 || StateCode(targetCode)) && (targetPage == 255 || targetPage < pages.Length),
            "Unsupported credits action kind, state or page target.");
        if (targetCode != 0)
        {
            Require(targetItem != ushort.MaxValue, "Credits state mutation has no item target.");
            // The source runtime mutates the current page, then calls, then
            // changes pages. A destination page does not own this item index.
            Require(targetItem < items, "Credits action item target is outside the current page.");
        }
        uint[] calls = r.Offsets(U32(action, 16), MaxCalls, 8); var retained = new Call[calls.Length];
        for (int i = 0; i < calls.Length; i++)
        {
            var call = r.Record(calls[i], 8); int a = I32(call, 0), b = I32(call, 4);
            Require(a == 0 && b is 11 or 12 or 206, "Credits callback is outside the supported data profile.");
            retained[i] = new(a, b);
        }
        return new(kind, action[2], action[3], U32(action, 4), I16(action, 8), I16(action, 10), I16(action, 12), I16(action, 14),
            Array.AsReadOnly(retained), targetItem, targetPage, targetCode);
    }

    private static Widget ReadWidget(ref Reader r, int id, uint offset)
    {
        var widget = r.Record(offset, 12); uint[] files = r.Offsets(U32(widget, 8), 2, 12);
        Require(files.Length is >= 1 and <= 2, "Credits widget requires a model and optional animation reference.");
        var retained = new WidgetFile[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            var file = r.Record(files[i], 12); uint nameOffset = U32(file, 8); int length = file[1]; string? path = null;
            if (nameOffset == 0) Require(i == 1 && length == 0, "Credits model filename is absent.");
            else
            {
                Require(length is >= 1 and <= MaxPathBytes, "Credits filename exceeds its declared extent.");
                var bytes = r.Bytes(nameOffset, length); int end = bytes.IndexOf((byte)0);
                Require(end > 0, "Credits filename has no in-span terminator.");
                path = CanonicalPath(Encoding.Latin1.GetString(bytes[..end]));
            }
            retained[i] = new(file[0], file[1], U16(file, 2), U32(file, 4), path);
        }
        return new(id, U32(widget, 0), U32(widget, 4), Array.AsReadOnly(retained));
    }
    private static string CanonicalPath(string source)
    {
        string path = source.Replace('\\', '/');
        Require(path.Length <= MaxPathBytes && !path.StartsWith('/') && !path.Contains(':')
            && path.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-' or '.' or '/'), "Credits filename is not a supported relative source path.");
        string[] parts = path.Split('/');
        Require(parts.Length >= 2 && parts.All(SafePathSegment)
            && (parts[0].Equals("logo_screen", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("main menu", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("tostart", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("gameoptions", StringComparison.OrdinalIgnoreCase))
            && parts[^1].EndsWith(".bin", StringComparison.OrdinalIgnoreCase), "Credits filename has an unsupported source root or extension.");
        return path;
    }
    private static bool SafePathSegment(string part)
    {
        if (part.Length == 0 || part[^1] is ' ' or '.') return false;
        ReadOnlySpan<char> stem = part.AsSpan();
        int dot = stem.IndexOf('.');
        if (dot > 0 && stem[dot - 1] == ' ') return false;
        if (dot >= 0) stem = stem[..dot];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return false;
        return !(stem.Length == 4 && stem[3] is >= '1' and <= '9'
            && (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase)
                || stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase)));
    }
    private static bool StateCode(byte code) => code is 1 or 2 or 3 or 10 or 11 or 17;
    private sealed class WorkBudget
    {
        internal int Visits;
        internal void Spend() { Require(++Visits <= MaxVisits, "Credits source aggregate work budget exceeded."); }
    }
    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private readonly WorkBudget _budget;
        internal Reader(ReadOnlySpan<byte> bytes, WorkBudget budget) { _bytes = bytes; _budget = budget; }
        internal ReadOnlySpan<byte> Bytes(uint offset, int length)
        {
            Require(length >= 0 && (ulong)offset + (uint)length <= (ulong)_bytes.Length, "Credits source record range is invalid.");
            return _bytes.Slice((int)offset, length);
        }
        internal ReadOnlySpan<byte> Record(uint offset, int length)
        { _budget.Spend(); Require(offset != 0, "Credits source record pointer is null."); return Bytes(offset, length); }
        internal uint[] Offsets(uint pointer, int maximum, int recordBytes, bool zeroIsEmpty = true)
        {
            if (pointer == 0 && zeroIsEmpty) return Array.Empty<uint>();
            var result = new List<uint>();
            for (int i = 0; i <= maximum; i++)
            {
                _budget.Spend(); ulong at = (ulong)pointer + (uint)i * 4;
                Require(at + 4 <= (ulong)_bytes.Length, "Credits source pointer list is unterminated or truncated.");
                uint value = U32(_bytes, (int)at);
                if (value == 0) return result.ToArray();
                Require(result.Count < maximum, "Credits source pointer-list budget exceeded.");
                Require(value != 0, "Credits source pointer is null."); Bytes(value, recordBytes); result.Add(value);
            }
            throw new InvalidDataException("Credits source pointer list is unterminated.");
        }
    }
    private static uint U32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
    private static ushort U16(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);
    private static int I32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);
    private static short I16(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadInt16LittleEndian(bytes[at..]);
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
