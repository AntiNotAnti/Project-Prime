using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    // These numeric values are a wire contract. Add values; never renumber them.
    public enum RmlUiIntentKind : uint
    {
        Navigate = 1, Quit = 2, OpenStudio = 3, StageSelect = 4, StagePreview = 5,
        PlayQuick = 10, PlayBrowse = 11, PlayCreateOpen = 12, PlayCreate = 13,
        PlayJoin = 14, PlayCancel = 15, PlayNextMap = 16, PlayNextMode = 17,
        PlayToggleHost = 18, PlayServer = 19,
        LobbyReady = 30, LobbyStart = 31, LobbyLeave = 32, LobbyNextHunter = 33,
        LobbyNextSuit = 34, LobbyClassic = 35, LobbyRulesOpen = 36,
        LobbyRulesClose = 37, LobbyRulesApply = 38, LobbyRulesMap = 39,
        LobbyRulesMode = 40, LobbyRulesFormat = 41, LobbyRulesToggle = 42
    }

    public enum RmlUiRouteArgument
    {
        Home = 0, Play = 1, Offline = 2, HunterLicense = 3, Community = 4,
        Theatre = 5, Settings = 6, News = 7, Adventure = 8, Social = 9
    }

    public readonly record struct RmlUiDocumentToken(ulong Generation, ulong DocumentId);

    public readonly record struct RmlUiIntent(
        RmlUiIntentKind Kind, int Argument, RmlUiDocumentToken Document, ulong Sequence);

    [StructLayout(LayoutKind.Sequential)]
    public struct RmlUiNativeIntent
    {
        public uint Size;
        public uint Version;
        public uint Kind;
        public int Argument;
        public ulong Generation;
        public ulong DocumentId;
        public ulong Sequence;
    }

    /// <summary>One registry for the versioned ABI and the transitional string buttons.</summary>
    public static class RmlUiIntentRegistry
    {
        public const uint ProtocolVersion = 1;
        public const uint NativeIntentSize = 40;
        private static readonly string[] Routes =
        {
            "home", "play", "offline", "hunter", "forge", "theatre", "settings",
            "news", "adventure", "social"
        };
        private static readonly string[] Stages = { "quick", "browser", "offline", "adventure" };
        private static readonly IReadOnlyDictionary<RmlUiIntentKind, string> LegacyActions =
            new Dictionary<RmlUiIntentKind, string>
            {
                [RmlUiIntentKind.Quit] = "quit",
                [RmlUiIntentKind.OpenStudio] = "studio:open",
                [RmlUiIntentKind.PlayQuick] = "play:quick",
                [RmlUiIntentKind.PlayBrowse] = "play:browse",
                [RmlUiIntentKind.PlayCreateOpen] = "play:create-open",
                [RmlUiIntentKind.PlayCreate] = "play:create",
                [RmlUiIntentKind.PlayJoin] = "play:join",
                [RmlUiIntentKind.PlayCancel] = "play:cancel",
                [RmlUiIntentKind.PlayNextMap] = "play:next-map",
                [RmlUiIntentKind.PlayNextMode] = "play:next-mode",
                [RmlUiIntentKind.PlayToggleHost] = "play:toggle-host",
                [RmlUiIntentKind.LobbyReady] = "lobby:ready",
                [RmlUiIntentKind.LobbyStart] = "lobby:start",
                [RmlUiIntentKind.LobbyLeave] = "lobby:leave",
                [RmlUiIntentKind.LobbyNextHunter] = "lobby:next-hunter",
                [RmlUiIntentKind.LobbyNextSuit] = "lobby:next-suit",
                [RmlUiIntentKind.LobbyClassic] = "lobby:classic",
                [RmlUiIntentKind.LobbyRulesOpen] = "lobby:rules-open",
                [RmlUiIntentKind.LobbyRulesClose] = "lobby:rules-close",
                [RmlUiIntentKind.LobbyRulesApply] = "lobby:rules-apply",
                [RmlUiIntentKind.LobbyRulesMap] = "lobby:rules-map",
                [RmlUiIntentKind.LobbyRulesMode] = "lobby:rules-mode",
                [RmlUiIntentKind.LobbyRulesFormat] = "lobby:rules-format"
            };

        public static bool IsValid(RmlUiIntentKind kind, int argument) => kind switch
        {
            RmlUiIntentKind.Navigate => argument >= 0 && argument < Routes.Length,
            RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview => argument is >= 0 and < 4,
            RmlUiIntentKind.PlayServer => argument is >= 0 and < 8,
            RmlUiIntentKind.LobbyRulesToggle => argument is >= 0 and < 16,
            _ => argument == 0 && LegacyActions.ContainsKey(kind)
        };

        public static bool TryDecode(in RmlUiNativeIntent packet, out RmlUiIntent intent)
        {
            var kind = (RmlUiIntentKind)packet.Kind;
            if (packet.Size != NativeIntentSize || packet.Version != ProtocolVersion
                || packet.Generation == 0 || packet.DocumentId == 0 || packet.Sequence == 0
                || !IsValid(kind, packet.Argument))
            {
                intent = default;
                return false;
            }
            intent = new(kind, packet.Argument,
                new(packet.Generation, packet.DocumentId), packet.Sequence);
            return true;
        }

        public static bool TryParseLegacy(string action, RmlUiDocumentToken document,
            ulong sequence, out RmlUiIntent intent)
        {
            foreach (var pair in LegacyActions)
            {
                if (String.Equals(pair.Value, action, StringComparison.Ordinal))
                {
                    intent = new(pair.Key, 0, document, sequence);
                    return true;
                }
            }
            if (TryNamed(action, "route:", Routes, out int argument))
                intent = new(RmlUiIntentKind.Navigate, argument, document, sequence);
            else if (TryNamed(action, "stage:", Stages, out argument))
                intent = new(RmlUiIntentKind.StageSelect, argument, document, sequence);
            else if (TryNamed(action, "stage-preview:", Stages, out argument))
                intent = new(RmlUiIntentKind.StagePreview, argument, document, sequence);
            else if (TryIndex(action, "play:server:", 8, out argument))
                intent = new(RmlUiIntentKind.PlayServer, argument, document, sequence);
            else if (TryIndex(action, "lobby:rules-toggle:", 16, out argument))
                intent = new(RmlUiIntentKind.LobbyRulesToggle, argument, document, sequence);
            else
            {
                intent = default;
                return false;
            }
            return true;
        }

        public static string ToLegacy(in RmlUiIntent intent)
        {
            if (!IsValid(intent.Kind, intent.Argument))
                throw new ArgumentException("Invalid RmlUi intent.", nameof(intent));
            return intent.Kind switch
            {
                RmlUiIntentKind.Navigate => "route:" + Routes[intent.Argument],
                RmlUiIntentKind.StageSelect => "stage:" + Stages[intent.Argument],
                RmlUiIntentKind.StagePreview => "stage-preview:" + Stages[intent.Argument],
                RmlUiIntentKind.PlayServer => "play:server:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.LobbyRulesToggle => "lobby:rules-toggle:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                _ => LegacyActions[intent.Kind]
            };
        }

        private static bool TryNamed(string action, string prefix, string[] names, out int argument)
        {
            argument = action.StartsWith(prefix, StringComparison.Ordinal)
                ? Array.IndexOf(names, action[prefix.Length..]) : -1;
            return argument >= 0;
        }

        private static bool TryIndex(string action, string prefix, int limit, out int argument)
        {
            argument = -1;
            if (!action.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string number = action[prefix.Length..];
            return Int32.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out argument)
                && argument >= 0 && argument < limit
                && number == argument.ToString(CultureInfo.InvariantCulture);
        }
    }
}
