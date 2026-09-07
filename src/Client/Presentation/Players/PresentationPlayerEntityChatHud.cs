using System;
using System.Collections.Generic;
using MphRead.Hud;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        /// <summary>
        /// Glyph size, 1 being the font's own 8x8 cell. Caps are six of those
        /// rows, so this draws a 2.7-unit capital in a 192-unit screen --
        /// about 15 pixels at 1080p, and small enough that three lines of it
        /// sit above the energy bar without being in the way.
        /// </summary>
        private const float ChatScale = 0.45f;
        private const float ChatLineHeight = 7 * ChatScale + 1.2f;
        private const float ChatTop = 3;
        /// <summary>
        /// The lowest the log can reach: three lines and the prompt under
        /// them. Reserved whether or not anybody is talking, because a score
        /// that moved when a message arrived would be worse than one sitting
        /// slightly lower than the DS put it.
        /// </summary>
        private const float ChatBottom = ChatTop + (ChatBox.VisibleLines + 1) * ChatLineHeight;
        public float ModChatClearance(float posY)
        {
            return ChatBox.Available ? Math.Max(posY, ChatBottom) : posY;
        }

        /// <summary>
        /// Clear of the top-left corner on Android, which draws its MENU
        /// button over the scene there -- the same reason and the same number
        /// the frame counter used before it moved to the other side.
        /// </summary>
        private static readonly float ChatMargin = OperatingSystem.IsAndroid() ? 30 : 3;
        // All green, with a hierarchy inside it: who said it stands out from
        // what they said, and the game's own notices are dimmer than either.
        private static readonly ColorRgba ChatName = new ColorRgba(110, 255, 130, 255);
        private static readonly ColorRgba ChatInk = new ColorRgba(170, 255, 175, 255);
        private static readonly ColorRgba ChatSystemInk = new ColorRgba(110, 205, 125, 255);
        private static readonly ColorRgba ChatPromptInk = new ColorRgba(110, 205, 125, 255);
        /// <summary>
        /// Two entries, and neither is ever read: <c>DoTexture</c> refuses to
        /// run without palette data, and every glyph here is drawn with an
        /// explicit colour, which takes the palette out of the path. This is
        /// what makes the assertion true.
        /// </summary>
        private static readonly ColorRgba[] _chatPalette =
        {
            new ColorRgba(),
            new ColorRgba(255, 255, 255, 255)
        };
        private HudObjectInstance? _chatInst;
        private readonly List<(ChatLine Line, float Alpha)> _chatVisible = new();
        /// <summary>
        /// What the prompt says. Real lowercase, at last -- the game's font
        /// would have made this SAYS.
        /// </summary>
        private const string ChatPrompt = "Says: ";
        public void ModDrawChat()
        {
            if (!ChatBox.Visible)
            {
                return;
            }

            if (_chatInst == null)
            {
                // Built on first use rather than with the rest of the HUD:
                // this costs a texture binding, and most matches never draw a
                // chat line at all. The palette goes on first, because
                // SetCharacterData only builds the texture once both are
                // there.
                _chatInst = new HudObjectInstance(width: ChatFont.Cell, height: ChatFont.Cell);
                _chatInst.SetPaletteData(_chatPalette, _player._scene);
                _chatInst.SetCharacterData(ChatFont.Pixels, _player._scene);
                _chatInst.Enabled = true;
            }

            ChatBox.CollectVisible(_chatVisible);
            float aspect = HudAspectFix;
            float x = ChatMargin * aspect;
            float y = ChatTop;
            for (int i = 0; i < _chatVisible.Count; i++)
            {
                (ChatLine line, float alpha) = _chatVisible[i];
                bool system = line.Kind == ChatPacket.KindSystem;
                // The trailing space is part of the prefix rather than the
                // message: it is measured with the name, and it survives a
                // message the sender began with one of their own.
                string name = system || line.Name.Length == 0 ? "" : line.Name + ": ";
                float at = ChatDraw(x, y, aspect, alpha, name, system ? ChatSystemInk : ChatName);
                ChatDraw(at, y, aspect, alpha, ChatFit(line.Text, aspect, at - x), system ? ChatSystemInk : ChatInk);
                y += ChatLineHeight;
            }

            if (ChatBox.Composing)
            {
                float at = ChatDraw(x, y, aspect, alpha: 1, ChatPrompt, ChatPromptInk);
                // The tail rather than the head once the line is long: what
                // somebody is typing is what they have just typed, and a
                // prompt that stops moving at the eightieth character looks
                // exactly like a prompt that has stopped taking input.
                //
                // The caret is a plain underscore and does not blink. It comes
                // out of the font like everything else, and something flashing
                // in the corner of a shooter reads as the game trying to tell
                // you about damage.
                ChatDraw(at, y, aspect, alpha: 1, ChatTail(ChatBox.ComposeText + "_", aspect, at - x), ChatInk);
            }
        }

        public float ChatDraw(float x, float y, float aspect, float alpha, ReadOnlySpan<char> text, ColorRgba color)
        {
            HudObjectInstance inst = _chatInst!;
            inst.Alpha = alpha;
            for (int i = 0; i < text.Length; i++)
            {
                int index = ChatFont.Index(text[i]);
                if (index < 0)
                {
                    continue;
                }

                if (text[i] != ' ')
                {
                    inst.PositionX = x / 256f;
                    inst.PositionY = y / 192f;
                    inst.SetData(index, color, _player._scene);
                    Presentation.DrawHudObject(inst, mode: 1, scale: ChatScale);
                }

                x += ChatFont.Widths[index] * ChatScale * aspect;
            }

            return x;
        }

        public static float ChatWidth(ReadOnlySpan<char> text, float aspect)
        {
            return ChatFont.Measure(text) * ChatScale * aspect;
        }

        public static float ChatRoom(float aspect, float used)
        {
            return 256 - ChatMargin * aspect * 2 - used;
        }

        public static string ChatFit(string text, float aspect, float used)
        {
            float room = ChatRoom(aspect, used);
            if (ChatWidth(text, aspect) <= room)
            {
                return text;
            }

            int count = text.Length;
            while (count > 0 && ChatWidth(text.AsSpan(0, count), aspect) > room)
            {
                count--;
            }

            return text[..count];
        }

        public static string ChatTail(string text, float aspect, float used)
        {
            float room = ChatRoom(aspect, used);
            if (ChatWidth(text, aspect) <= room)
            {
                return text;
            }

            int start = 0;
            while (start < text.Length && ChatWidth(text.AsSpan(start), aspect) > room)
            {
                start++;
            }

            return text[start..];
        }

        public void ModForgetInputDeltas()
        {
            _mouseState = null;
            _player.Input.MouseDeltaX = _player.Input.MouseDeltaY = 0;
            _keyboardState = null;
        }
    }
}
