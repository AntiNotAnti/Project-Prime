using System.Text;
using MphRead.Mods.Launcher.RmlUi.Host;

sealed class FakeBridge : IRmlUiNativeBridge
{
    public bool Legacy { get; set; }
    public bool RejectOpen;
    public readonly Dictionary<ulong,bool> Visible = new();
    public (ulong Document,string Element) Focus => (_focusedDocument,_focusedElement);
    private ulong _generation, _nextDocument, _home;
    private string _clipboard = "";
    public readonly HashSet<ulong> Documents = new();
    public readonly Dictionary<(ulong, string), string> Texts = new();
    public readonly Dictionary<(ulong, string), bool> Bools = new();
    private readonly Dictionary<(ulong, string), string> _fields = new();
    private readonly Queue<RmlUiNativeIntent> _intents = new();
    public readonly List<uint> Codepoints = new();
    public (int, int) Pointer;
    public int TextWrites, FocusReleaseCount;
    private ulong _focusEpoch = 1, _focusedDocument;
    private string _focusedElement = "name";
    private bool _composing;
    public int CompositionCalls, CompositionCursor;
    public uint ProtocolVersion() => Legacy ? throw new EntryPointNotFoundException() : 1u;
    public ulong Generation() => _generation;
    public ulong HomeDocument() => _home;
    public int Initialize(int width, int height, float density, string root) => InitializeBackend(width, height, density, root, 0);
    public int InitializeBackend(int width, int height, float density, string root, int backend)
    {
        _generation++;
        _home = ++_nextDocument;
        Documents.Add(_home);
        return 1;
    }
    public void Shutdown() { Documents.Clear(); Texts.Clear(); Bools.Clear(); _fields.Clear(); _intents.Clear(); }
    public void Resize(int width, int height, float density) { }
    public void Update() { }
    public void Render(int width, int height) { }
    public int TakeIntent(ref RmlUiNativeIntent packet)
    {
        if (packet.Size != RmlUiIntentRegistry.NativeIntentSize
            || packet.Version != RmlUiIntentRegistry.ProtocolVersion) return 0;
        if (!_intents.TryDequeue(out var queued)) return 0;
        packet = queued;
        return 1;
    }
    public int TakeAction(byte[] buffer, int capacity) => 0;
    public void Queue(RmlUiDocumentToken document, RmlUiIntentKind kind, ulong sequence, int argument = 0) =>
        _intents.Enqueue(new() { Size = 40, Version = 1, Kind = (uint)kind, Argument = argument,
            Generation = document.Generation, DocumentId = document.DocumentId, Sequence = sequence });
    public ulong OpenDocument(string path, int layer) { if (RejectOpen) return 0; ulong id = ++_nextDocument; Documents.Add(id); _focusedDocument=id; _focusedElement="auto"; return id; }
    public int CloseDocument(ulong document) => Documents.Remove(document) ? 1 : 0;
    public int ShowDocument(ulong document, int show) { if (!Documents.Contains(document)) return 0; Visible[document]=show!=0; return 1; }
    public int FocusDocument(ulong document, string element)
    {
        if (!Documents.Contains(document)) return 0;
        _focusedDocument = document; _focusedElement = element; _focusEpoch++; _composing = false;
        return 1;
    }
    public void SetText(string name, string value) => DocumentSetText(_home, name, value);
    public void SetBool(string name, int value) => DocumentSetBool(_home, name, value);
    public void SetField(string id, string value) => DocumentSetField(_home, id, value);
    public int ReadField(string id, byte[] buffer, int capacity) => DocumentReadField(_home, id, buffer, capacity);
    public int DocumentSetText(ulong document, string name, string value) { Texts[(document, name)] = value; TextWrites++; return 1; }
    public int DocumentSetBool(ulong document, string name, int value) { Bools[(document, name)] = value != 0; return 1; }
    public int DocumentSetField(ulong document, string id, string value) { _fields[(document, id)] = value; return 1; }
    public int DocumentReadField(ulong document, string id, byte[] buffer, int capacity) =>
        Copy(_fields.GetValueOrDefault((document, id), ""), buffer, capacity);
    public void SetLobbyAnchor(int slot, float x, float y) { }
    public int Back() => 0;
    public int MouseMove(int x, int y, int modifiers) { Pointer = (x, y); return 1; }
    public int MouseButton(int button, int down, int modifiers) => 1;
    public int MouseWheel(float delta, int modifiers) => 1;
    public int Key(int key, int down, int modifiers) => 1;
    public int Text(uint codepoint) { Codepoints.Add(codepoint); return 1; }
    public void FocusLost() { FocusReleaseCount++; _focusedElement = ""; _focusEpoch++; _composing = false; }
    public int TextInputActive() => _focusedElement == "name" ? 1 : 0;
    public int FocusedElement(byte[] buffer, int capacity) => Copy(_focusedElement, buffer, capacity);
    public int HoveredElement(byte[] buffer, int capacity) => Copy("submit", buffer, capacity);
    public int TextInputState(ref RmlUiNativeTextInputState state)
    {
        if (state.Size != 64 || state.Version != 1 || TextInputActive() == 0) return 0;
        state = new() { Size = 64, Version = 1, Generation = _generation,
            DocumentId = _focusedDocument == 0 ? _home : _focusedDocument, FocusEpoch = _focusEpoch,
            Width = 260, Height = 40, Composing = _composing ? 1 : 0,
            Capabilities = RmlUiTextInputCapabilities.Composition | RmlUiTextInputCapabilities.Bounds };
        return 1;
    }
    public int Composition(ulong generation, ulong document, ulong epoch, int stage, string text, int cursor, int selectionLength)
    {
        if (generation != _generation || !Documents.Contains(document) || epoch != _focusEpoch || TextInputActive() == 0) return 0;
        CompositionCalls++; CompositionCursor = cursor;
        if (stage == 0) _composing = true;
        else if (stage is 2 or 3) _composing = false;
        return 1;
    }
    public void SetClipboard(string text) => _clipboard = text;
    public int ReadClipboard(byte[] buffer, int capacity) => Copy(_clipboard, buffer, capacity);
    private static int Copy(string value, byte[] buffer, int capacity)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, capacity - 1);
        Array.Copy(bytes, buffer, length);
        buffer[length] = 0;
        return length;
    }
}

