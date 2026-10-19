using System.Text;
using MphRead.Mods.Launcher.RmlUi.Host;

internal sealed class FakeBridge : IRmlUiNativeBridge
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly HashSet<ulong> _documents = new();
    private ulong _generation, _next, _home, _focused, _focusEpoch = 1;
    private string _field = "", _clipboard = "";
    private bool _inputSinceUpdate;
    internal readonly List<uint> Text = new();
    internal int Buttons, Releases, WrongThreadCalls;
    internal ulong Keys;
    internal bool Composing, InputPrecededUpdate;
    internal bool ThrowOnRelease;
    internal int ButtonsAtResize;
    internal Action? OnResize { get; set; }
    internal (int, int) Pointer;
    private void Owner()
    {
        if (_owner != Environment.CurrentManagedThreadId) { WrongThreadCalls++; throw new InvalidOperationException("Fake native owner violation."); }
    }
    public uint ProtocolVersion() { Owner(); return 1; }
    public ulong Generation() { Owner(); return _generation; }
    public ulong HomeDocument() { Owner(); return _home; }
    public int Initialize(int width, int height, float density, string root) => InitializeBackend(width, height, density, root, 0);
    public int InitializeBackend(int width, int height, float density, string root, int backend)
    { Owner(); _generation++; _home = ++_next; _documents.Add(_home); return 1; }
    public void Shutdown() { Owner(); _documents.Clear(); _field = ""; Buttons = 0; Keys = 0; Composing = false; }
    public void Resize(int width, int height, float density) { Owner(); ButtonsAtResize = Buttons; OnResize?.Invoke(); }
    public void Update() { Owner(); InputPrecededUpdate |= _inputSinceUpdate; _inputSinceUpdate = false; }
    public void Render(int width, int height) { Owner(); }
    public int TakeIntent(ref RmlUiNativeIntent packet) { Owner(); return 0; }
    public int TakeAction(byte[] buffer, int capacity) { Owner(); return 0; }
    public ulong OpenDocument(string path, int layer) { Owner(); ulong id = ++_next; _documents.Add(id); return id; }
    public int CloseDocument(ulong document) { Owner(); return _documents.Remove(document) ? 1 : 0; }
    public int ShowDocument(ulong document, int show) { Owner(); return _documents.Contains(document) ? 1 : 0; }
    public int FocusDocument(ulong document, string element)
    {
        Owner(); if (!_documents.Contains(document)) return 0;
        _focused = document; _field = element; _focusEpoch++; Composing = false; return 1;
    }
    public void SetText(string name, string value) { Owner(); }
    public void SetBool(string name, int value) { Owner(); }
    public void SetField(string id, string value) { Owner(); }
    public int ReadField(string id, byte[] buffer, int capacity) { Owner(); return Copy("", buffer); }
    public int DocumentSetText(ulong document, string name, string value) { Owner(); return 1; }
    public int DocumentSetBool(ulong document, string name, int value) { Owner(); return 1; }
    public int DocumentSetField(ulong document, string id, string value) { Owner(); return 1; }
    public int DocumentReadField(ulong document, string id, byte[] buffer, int capacity) { Owner(); return Copy("", buffer); }
    public void SetLobbyAnchor(int slot, float x, float y) { Owner(); }
    public int Back() { Owner(); return 0; }
    public int MouseMove(int x, int y, int modifiers) { Owner(); Pointer = (x, y); _inputSinceUpdate = true; return 1; }
    public int MouseButton(int button, int down, int modifiers)
    { Owner(); if (down != 0) Buttons |= 1 << button; else Buttons &= ~(1 << button); _inputSinceUpdate = true; return 1; }
    public int MouseWheel(float delta, int modifiers) { Owner(); _inputSinceUpdate = true; return 1; }
    public int Key(int key, int down, int modifiers)
    { Owner(); if (down != 0) Keys |= 1UL << key; else Keys &= ~(1UL << key); _inputSinceUpdate = true; return 1; }
    int IRmlUiNativeBridge.Text(uint codepoint) { Owner(); Text.Add(codepoint); _inputSinceUpdate = true; return 1; }
    public void FocusLost()
    {
        Owner(); Releases++;
        if (ThrowOnRelease) throw new InvalidOperationException("Fake native input release failed.");
        Buttons = 0; Keys = 0; Composing = false; _field = ""; _focusEpoch++;
    }
    public int TextInputActive() { Owner(); return _field.Length > 0 ? 1 : 0; }
    public int FocusedElement(byte[] buffer, int capacity) { Owner(); return Copy(_field, buffer); }
    public int HoveredElement(byte[] buffer, int capacity) { Owner(); return Copy("button", buffer); }
    public void SetClipboard(string text) { Owner(); _clipboard = text; }
    public int ReadClipboard(byte[] buffer, int capacity) { Owner(); return Copy(_clipboard, buffer); }
    public int TextInputState(ref RmlUiNativeTextInputState state)
    {
        Owner(); if (_field.Length == 0) return 0;
        state = new() { Size = 64, Version = 1, Generation = _generation,
            DocumentId = _focused == 0 ? _home : _focused, FocusEpoch = _focusEpoch,
            X = 10, Y = 20, Width = 100, Height = 40, Composing = Composing ? 1 : 0,
            Capabilities = RmlUiTextInputCapabilities.Composition | RmlUiTextInputCapabilities.Bounds };
        return 1;
    }
    public int Composition(ulong generation, ulong document, ulong epoch, int stage, string text, int cursor, int selectionLength)
    {
        Owner(); if (generation != _generation || document != _focused || epoch != _focusEpoch || _field.Length == 0) return 0;
        Composing = stage is 0 or 1; _inputSinceUpdate = true; return 1;
    }
    private static int Copy(string value, byte[] buffer)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value); bytes.CopyTo(buffer, 0); buffer[bytes.Length] = 0; return bytes.Length;
    }
}

// These fakes allow the production session orchestration to compile in an
// asset/window-free fixture. The real GLES implementation is not exercised.
namespace MphRead.Mods.Launcher.RmlUi.Render
{
    internal sealed class RmlUiDrawListReader
    {
        internal long CapturedFrames { get; private set; }
        internal long ReusedFrames => 0;
        internal object Capture() { CapturedFrames++; return new object(); }
        internal void Forget() { }
    }
}
namespace MphRead.Droid
{
    internal sealed class RmlUiAndroidGlesCompositor
    {
        internal static int ReleaseCalls;
        internal long UploadedGeometry => 0;
        internal long UploadedTextures => 0;
        internal void Draw(object frame, int width, int height) { }
        internal void Release() { ReleaseCalls++; }
    }
}
