using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public enum RmlUiDocumentLayer { Page = 0, Modal = 1 }
    public enum RmlUiRenderBackend { OpenGl = 0, DrawList = 1 }

    /// <summary>The native seam can be exercised without a window or UI toolkit.</summary>
    internal interface IRmlUiNativeBridge
    {
        uint ProtocolVersion();
        ulong Generation();
        ulong HomeDocument();
        int Initialize(int width, int height, float density, string root);
        int InitializeBackend(int width, int height, float density, string root, int backend);
        void Shutdown();
        void Resize(int width, int height, float density);
        void Update();
        int UpdateState(ref RmlUiNativeUpdateState state) => 0;
        void Render(int width, int height);
        int TakeIntent(ref RmlUiNativeIntent packet);
        int TakeAction(byte[] buffer, int capacity);
        ulong OpenDocument(string path, int layer);
        int CloseDocument(ulong document);
        int ShowDocument(ulong document, int show);
        int FocusDocument(ulong document, string element);
        void SetText(string name, string value);
        void SetBool(string name, int value);
        void SetField(string id, string value);
        int ReadField(string id, byte[] buffer, int capacity);
        int DocumentSetText(ulong document, string name, string value);
        int DocumentSetBool(ulong document, string name, int value);
        int DocumentSetField(ulong document, string id, string value);
        int DocumentReadField(ulong document, string id, byte[] buffer, int capacity);
        void SetLobbyAnchor(int slot, float x, float y);
        int Back();
        int MouseMove(int x, int y, int modifiers);
        int MouseButton(int button, int down, int modifiers);
        int MouseWheel(float delta, int modifiers);
        int Key(int key, int down, int modifiers);
        int Text(uint codepoint);
        void FocusLost();
        int TextInputActive();
        int FocusedElement(byte[] buffer, int capacity);
        void SetClipboard(string text);
        int ReadClipboard(byte[] buffer, int capacity);
        int TextInputState(ref RmlUiNativeTextInputState state);
        int TextSelectionUtf16(ulong generation, ulong document, ulong epoch, out int start, out int end)
        { start = end = 0; return 0; }
        int Composition(ulong generation, ulong document, ulong epoch, int stage, string text, int cursor, int selectionLength);
        int HoveredElement(byte[] buffer, int capacity);
        int DocumentElementBounds(ulong document, string element, out float x, out float y, out float width, out float height)
        { x = y = width = height = 0; return 0; }
    }

    internal sealed class RmlUiNativeBridge : IRmlUiNativeBridge
    {
        private const string Library = "ProjectPrime.RmlUi.Native";

        uint IRmlUiNativeBridge.ProtocolVersion() => NativeProtocolVersion();
        ulong IRmlUiNativeBridge.Generation() => NativeGeneration();
        ulong IRmlUiNativeBridge.HomeDocument() => NativeHomeDocument();
        int IRmlUiNativeBridge.Initialize(int width, int height, float density,
            string root) => NativeInitialize(width, height, density, root);
        int IRmlUiNativeBridge.InitializeBackend(int width, int height, float density,
            string root, int backend) => NativeInitializeBackend(width, height, density, root, backend);
        void IRmlUiNativeBridge.Shutdown() => NativeShutdown();
        void IRmlUiNativeBridge.Resize(int width, int height, float density) => NativeResize(width, height, density);
        void IRmlUiNativeBridge.Update() => NativeUpdate();
        int IRmlUiNativeBridge.UpdateState(ref RmlUiNativeUpdateState state) => NativeUpdateState(ref state);
        void IRmlUiNativeBridge.Render(int width, int height) => NativeRender(width, height);
        int IRmlUiNativeBridge.TakeIntent(ref RmlUiNativeIntent packet) => NativeTakeIntent(ref packet);
        int IRmlUiNativeBridge.TakeAction(byte[] buffer, int capacity) => NativeTakeAction(buffer, capacity);
        ulong IRmlUiNativeBridge.OpenDocument(string path, int layer) => NativeOpenDocument(path, layer);
        int IRmlUiNativeBridge.CloseDocument(ulong document) => NativeCloseDocument(document);
        int IRmlUiNativeBridge.ShowDocument(ulong document, int show) => NativeShowDocument(document, show);
        int IRmlUiNativeBridge.FocusDocument(ulong document, string element) => NativeFocusDocument(document, element);
        void IRmlUiNativeBridge.SetText(string name,
            string value) => NativeSetText(name, value);
        void IRmlUiNativeBridge.SetBool(string name, int value) => NativeSetBool(name, value);
        void IRmlUiNativeBridge.SetField(string id,
            string value) => NativeSetField(id, value);
        int IRmlUiNativeBridge.ReadField(string id,
            byte[] buffer, int capacity) => NativeReadField(id, buffer, capacity);
        int IRmlUiNativeBridge.DocumentSetText(ulong document,
            string name,
            string value) => NativeDocumentSetText(document, name, value);
        int IRmlUiNativeBridge.DocumentSetBool(ulong document,
            string name, int value) => NativeDocumentSetBool(document, name, value);
        int IRmlUiNativeBridge.DocumentSetField(ulong document,
            string id,
            string value) => NativeDocumentSetField(document, id, value);
        int IRmlUiNativeBridge.DocumentReadField(ulong document,
            string id, byte[] buffer, int capacity) => NativeDocumentReadField(document, id, buffer, capacity);
        void IRmlUiNativeBridge.SetLobbyAnchor(int slot, float x, float y) => NativeSetLobbyAnchor(slot, x, y);
        int IRmlUiNativeBridge.Back() => NativeBack();
        int IRmlUiNativeBridge.MouseMove(int x, int y, int modifiers) => NativeMouseMove(x, y, modifiers);
        int IRmlUiNativeBridge.MouseButton(int button, int down, int modifiers) => NativeMouseButton(button, down, modifiers);
        int IRmlUiNativeBridge.MouseWheel(float delta, int modifiers) => NativeMouseWheel(delta, modifiers);
        int IRmlUiNativeBridge.Key(int key, int down, int modifiers) => NativeKey(key, down, modifiers);
        int IRmlUiNativeBridge.Text(uint codepoint) => NativeText(codepoint);
        void IRmlUiNativeBridge.FocusLost() => NativeFocusLost();
        int IRmlUiNativeBridge.TextInputActive() => NativeTextInputActive();
        int IRmlUiNativeBridge.FocusedElement(byte[] buffer, int capacity) => NativeFocusedElement(buffer, capacity);
        void IRmlUiNativeBridge.SetClipboard(string text) => NativeSetClipboard(text);
        int IRmlUiNativeBridge.ReadClipboard(byte[] buffer, int capacity) => NativeReadClipboard(buffer, capacity);
        int IRmlUiNativeBridge.TextInputState(ref RmlUiNativeTextInputState state) => NativeTextInputState(ref state);
        int IRmlUiNativeBridge.TextSelectionUtf16(ulong generation, ulong document, ulong epoch, out int start, out int end)
            => NativeTextSelectionUtf16(generation, document, epoch, out start, out end);
        int IRmlUiNativeBridge.Composition(ulong generation, ulong document, ulong epoch, int stage,
            string text, int cursor, int selectionLength) => NativeComposition(generation, document, epoch, stage, text, cursor, selectionLength);
        int IRmlUiNativeBridge.HoveredElement(byte[] buffer, int capacity) => NativeHoveredElement(buffer, capacity);
        int IRmlUiNativeBridge.DocumentElementBounds(ulong document, string element, out float x, out float y, out float width, out float height)
            => NativeDocumentElementBounds(document, element, out x, out y, out width, out height);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_protocol_version")]
        private static extern uint NativeProtocolVersion();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_generation")]
        private static extern ulong NativeGeneration();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_home_document")]
        private static extern ulong NativeHomeDocument();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_initialize")]
        private static extern int NativeInitialize(int width, int height, float density,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string root);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_initialize_backend")]
        private static extern int NativeInitializeBackend(int width, int height, float density,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string root, int backend);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_shutdown")]
        private static extern void NativeShutdown();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_resize")]
        private static extern void NativeResize(int width, int height, float density);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_update")]
        private static extern void NativeUpdate();
        [DllImport(Library, EntryPoint = "pp_rmlui_update_status", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NativeUpdateState(ref RmlUiNativeUpdateState state);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_render")]
        private static extern void NativeRender(int width, int height);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_take_intent")]
        private static extern int NativeTakeIntent(ref RmlUiNativeIntent packet);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_take_action")]
        private static extern int NativeTakeAction([Out] byte[] buffer, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_open")]
        private static extern ulong NativeOpenDocument([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int layer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_close")]
        private static extern int NativeCloseDocument(ulong document);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_show")]
        private static extern int NativeShowDocument(ulong document, int show);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_focus")]
        private static extern int NativeFocusDocument(ulong document, [MarshalAs(UnmanagedType.LPUTF8Str)] string element);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_text")]
        private static extern void NativeSetText([MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_bool")]
        private static extern void NativeSetBool([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_field")]
        private static extern void NativeSetField([MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_read_field")]
        private static extern int NativeReadField([MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [Out] byte[] buffer, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_set_text")]
        private static extern int NativeDocumentSetText(ulong document,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_set_bool")]
        private static extern int NativeDocumentSetBool(ulong document,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_set_field")]
        private static extern int NativeDocumentSetField(ulong document,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_read_field")]
        private static extern int NativeDocumentReadField(ulong document,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id, [Out] byte[] buffer, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_lobby_anchor")]
        private static extern void NativeSetLobbyAnchor(int slot, float x, float y);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_back")]
        private static extern int NativeBack();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_move")]
        private static extern int NativeMouseMove(int x, int y, int modifiers);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_button")]
        private static extern int NativeMouseButton(int button, int down, int modifiers);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_wheel")]
        private static extern int NativeMouseWheel(float delta, int modifiers);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_key")]
        private static extern int NativeKey(int key, int down, int modifiers);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_text")]
        private static extern int NativeText(uint codepoint);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_focus_lost")]
        private static extern void NativeFocusLost();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_text_input_active")]
        private static extern int NativeTextInputActive();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_focused_element")]
        private static extern int NativeFocusedElement([Out] byte[] buffer, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_clipboard")]
        private static extern void NativeSetClipboard([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_read_clipboard")]
        private static extern int NativeReadClipboard([Out] byte[] buffer, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_text_input_state")]
        private static extern int NativeTextInputState(ref RmlUiNativeTextInputState state);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_text_selection_utf16")]
        private static extern int NativeTextSelectionUtf16(ulong generation, ulong document, ulong epoch, out int start, out int end);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_composition")]
        private static extern int NativeComposition(ulong generation, ulong document, ulong epoch, int stage,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int cursor, int selectionLength);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_hovered_element")]
        private static extern int NativeHoveredElement([Out] byte[] buffer, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_document_element_bounds")]
        private static extern int NativeDocumentElementBounds(ulong document, [MarshalAs(UnmanagedType.LPUTF8Str)] string element,
            out float x, out float y, out float width, out float height);
    }
}
