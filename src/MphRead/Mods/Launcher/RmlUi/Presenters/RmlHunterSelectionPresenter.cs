#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Presenters
{
    /// <summary>Native draft controls over the same identity/cosmetic services as the legacy client.</summary>
    internal sealed class RmlHunterSelectionPresenter : IDisposable
    {
        private readonly RmlUiHost _host;
        private readonly HunterSelectionController _selection;
        private readonly bool _ownsSelection;
        private readonly LobbySessionController? _lobby;
        private RmlUiDocumentToken _document;
        private readonly Guid? _lobbyLifetime;
        private string _previewStatus = "PREPARING HUNTER PREVIEW.";
        private long _revision;
        private bool _disposed;
        public bool Compact { get; }
        public bool Active => !_disposed && _host.IsAlive(_document);
        public RmlUiDocumentToken Document => _document;
        public HunterSelectionSnapshot Snapshot => _selection.Snapshot;
        public event Action<HunterSelectionSnapshot>? PreviewChanged;
        public event Action? Closed;

        public RmlHunterSelectionPresenter(RmlUiHost host, LobbySessionController? lobby = null, IHunterSelectionBackend? backend = null,
            HunterSelectionController? existingSelection = null, bool compact = false)
        {
            Compact = compact; _host = host; _lobby = lobby; _lobbyLifetime = lobby?.Snapshot().Lifetime;
            _ownsSelection = existingSelection == null;
            _selection = existingSelection ?? new(lobby, backend);
        }
        public void Open()
        {
            _host.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(RmlHunterSelectionPresenter));
            if (!Active) _document = _host.OpenDocument(Compact ? "pages/hunters/strip.rml" : "pages/hunters/selection.rml", RmlUiDocumentLayer.Modal);
            Update(); _host.FocusDocument(_document, "hunter_choice" + (int)_selection.Snapshot.Hunter);
        }
        public void SetPreviewStatus(string status)
        {
            _host.VerifyOwnerThread();
            _previewStatus = status ?? "";
        }
        public void Update()
        {
            if (!Active) return;
            LobbySnapshot? lobby = _lobby?.Snapshot();
            if (lobby != null && (lobby.Lifetime != _lobbyLifetime || lobby.Closed || !lobby.Active)) { Dispose(); return; }
            _selection.Pump(); HunterSelectionSnapshot state = _selection.Snapshot;
            var bindings = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
            void Text(string id, string value) => bindings[id] = RmlUiBindingValue.FromText(value);
            void Bool(string id, bool value) => bindings[id] = RmlUiBindingValue.FromBoolean(value);
            void Enabled(string id, bool value) => Bool("disabled:" + id, !value);
            if (Compact)
            {
                Text("hunter_status", state.CommandError.Length > 0 ? state.CommandError : "Choose a Hunter for this lobby.");
                Text("hunter_restriction", "Select a Hunter to equip it. Unavailable Hunters are dimmed.");
                for (int i=0; i<7; i++)
                {
                    Bool($"class:hunter_choice{i}:selected", (int)state.Hunter==i);
                    Enabled("hunter_choice"+i, state.AllowedHunters.Contains((Hunter)i) && state.CanApplyIdentity && !state.IdentityPending);
                }
                _host.Present(new(_document,++_revision,bindings));
                PreviewChanged?.Invoke(state);
                return;
            }
            Text("hunter_name", state.Hunter.ToString().ToUpperInvariant() + " // SUIT " + (state.Color + 1));
            Text("hunter_status", state.CommandError.Length > 0 ? state.CommandError
                : lobby?.CommandError.Length > 0 ? lobby.CommandError
                : state.IdentityPending ? "HUNTER / SUIT REQUEST SENT // WAITING FOR SERVER ACKNOWLEDGEMENT"
                : lobby?.SpectatorPending == true ? lobby.SpectatorMessage : state.Status);
            for (int index = 0; index < 7; index++)
            {
                Bool($"class:hunter_choice{index}:selected", (int)state.Hunter == index);
                Enabled("hunter_choice" + index, state.AllowedHunters.Contains((Hunter)index) && !state.Saving);
            }
            for (int index = 0; index < 4; index++) { Bool($"class:hunter_suit{index}:selected", state.Color == index); Enabled("hunter_suit" + index, !state.Saving); }
            Text("hunter_restriction", Compact ? "Select a Hunter to equip it. Unavailable Hunters are dimmed." : state.AllowedHunters.Length < 7
                ? "LOW-TIER LOBBY // ONLY KANDEN, SPIRE, NOXUS AND WEAVEL ARE AVAILABLE. A DRAFT IS NEVER SILENTLY CHANGED BY SERVER REFRESH."
                : "SELECT A HUNTER AND SUIT, THEN APPLY. COSMETIC DRAFTS EQUIP SEPARATELY.");
            Text("hunter_skin", "SKIN: " + Name(state.Skins, state.Draft.SkinKey));
            Text("hunter_armor", "ARMOR EFFECT: " + Name(state.ArmorEffects, state.Draft.ArmorEffectKey));
            Text("hunter_death", "DEATH: " + Name(state.DeathPresentations, state.Draft.DeathEffectKey));
            Text("hunter_cosmetics_state", (state.Draft != state.Equipped ? "LOCAL DRAFT / NOT EQUIPPED" : state.PendingSync ? "EQUIPPED LOCALLY / ACCOUNT SYNC PENDING" : "EQUIPPED") + "\n" + state.VisibilityMessage);
            Text("hunter_custom_assets", state.CustomModelMessage + "\n" + (state.AvailableCustomModelModes.Length == 0
                ? "NO INSTALLED CUSTOM MODEL MODES" : "INSTALLED CUSTOM MODEL MODES: " + String.Join(", ", state.AvailableCustomModelModes)));
            Text("hunter_preview_status", _previewStatus);
            Text("hunter_acknowledged", lobby == null ? "LOCAL PROFILE // APPLY SAVES HUNTER AND SUIT FOR THE NEXT SESSION"
                : state.AcknowledgedHunter.HasValue ? $"SERVER ACKNOWLEDGED: {state.AcknowledgedHunter.Value} // SUIT {state.AcknowledgedColor.GetValueOrDefault() + 1}\n{(lobby.AcknowledgedSpectator == true ? "SPECTATOR" : "COMBATANT")}{(lobby.SpectatorPending ? " // ROLE REQUEST PENDING" : "")}" : "WAITING FOR SERVER IDENTITY");
            LobbyPresentation? presentation = lobby == null ? null : LobbyPresentation.From(lobby);
            Text("hunter_spectator", lobby?.PreferSpectator == true ? "JOIN AS COMBATANT" : "JOIN AS SPECTATOR");
            Enabled("hunter_spectator", presentation?.CanChangeSpectator == true);
            var local = presentation?.Players.FirstOrDefault(row => row.IsLocal);
            Text("hunter_team_state", lobby == null ? "TEAM AND SPECTATOR CHOICES ARE AVAILABLE IN A LIVE LOBBY."
                : presentation!.Teams.Length == 0 ? "FREE FOR ALL"
                : String.Join(" // ", presentation.Teams.Select(team => $"{team.Name} {team.Occupants}/{team.Capacity}")) + (presentation.TeamsLocked ? " // TEAMS LOCKED" : ""));
            for (int index = 0; index < 4; index++) {
                Bool("visible:hunter_team" + index, presentation != null && index < presentation.Teams.Length);
                Text("hunter_team" + index, presentation != null && index < presentation.Teams.Length
                    ? $"{presentation.Teams[index].Name} // {presentation.Teams[index].Occupants}/{presentation.Teams[index].Capacity}" : "");
                Enabled("hunter_team" + index, local != null && presentation!.CanAssignTeam(local.Player.Slot, (sbyte)index));
                Bool($"class:hunter_team{index}:selected", local?.Player.Team == index);
            }
            Bool("visible:hunter_spectator", lobby != null);
            Bool("visible:hunter_team_next", local?.CanChangeTeam == true);
            Bool("visible:hunter_team_state", lobby != null);
            Enabled("hunter_team_next", local?.CanChangeTeam == true);
            Enabled("hunter_apply", state.CanApplyIdentity && !state.IdentityPending);
            Enabled("hunter_cosmetics_save", state.CanEquip);
            Enabled("hunter_cosmetics_reset", !state.Saving);
            Enabled("hunter_skin", !state.Saving && state.Skins.Any(option => option.Unlocked));
            Enabled("hunter_armor", !state.Saving && state.ArmorEffects.Any(option => option.Unlocked));
            Enabled("hunter_death", !state.Saving && state.DeathPresentations.Any(option => option.Unlocked));
            Enabled("hunter_preview_turret", state.Hunter == Hunter.Weavel);
            Bool("class:hunter_loop_death:selected", state.LoopDeath);
            Bool("class:hunter_compare_native:selected", state.CompareNative);
            foreach (var mode in new[] { (SkinContext.Biped, "hunter_preview_biped"), (SkinContext.ViewModel, "hunter_preview_weapon"), (SkinContext.AltForm, "hunter_preview_alt"), (SkinContext.Halfturret, "hunter_preview_turret") })
                Bool($"class:{mode.Item2}:selected", state.PreviewMode == mode.Item1);
            _host.Present(new(_document, ++_revision, bindings));
            PreviewChanged?.Invoke(state);
        }
        private static string Name(ImmutableArray<HunterCosmeticOption> options, string key) => options.FirstOrDefault(option => option.Key == key).Name ?? key;
        private static string? Next(ImmutableArray<HunterCosmeticOption> options, string key)
        {
            if (options.Length == 0) return null;
            int current = -1; for (int index = 0; index < options.Length; index++) if (options[index].Key == key) current = index;
            for (int step = 1; step <= options.Length; step++) { var option = options[(current + step) % options.Length]; if (option.Unlocked) return option.Key; }
            return null;
        }
        public bool Handle(RmlUiIntent intent)
        {
            if (!Active || intent.Document != _document) return false;
            HunterSelectionSnapshot state = _selection.Snapshot;
            switch (intent.Kind)
            {
                case RmlUiIntentKind.HunterCancel: Dispose(); return true;
                case RmlUiIntentKind.HunterSelect:
                    if (_selection.SelectHunter((Hunter)intent.Argument).Accepted && Compact && _selection.ApplyIdentity().Accepted)
                    { Dispose(); return true; }
                    break;
                case RmlUiIntentKind.HunterSuit: _selection.SelectSuit((byte)intent.Argument); break;
                case RmlUiIntentKind.HunterApply: _selection.ApplyIdentity(); break;
                case RmlUiIntentKind.HunterSkinNext: if (Next(state.Skins, state.Draft.SkinKey) is { } skin) _selection.SelectSkin(skin); break;
                case RmlUiIntentKind.HunterArmorNext: if (Next(state.ArmorEffects, state.Draft.ArmorEffectKey) is { } armor) _selection.SelectArmor(armor); break;
                case RmlUiIntentKind.HunterDeathNext: if (Next(state.DeathPresentations, state.Draft.DeathEffectKey) is { } death) _selection.SelectDeath(death); break;
                case RmlUiIntentKind.HunterCosmeticsReset: _selection.ResetLoadout(); break;
                case RmlUiIntentKind.HunterCosmeticsSave: _selection.Equip(); break;
                case RmlUiIntentKind.HunterPreviewMode: _selection.SetPreviewMode((SkinContext)intent.Argument); break;
                case RmlUiIntentKind.HunterPreviewDeath: _selection.PreviewDeath(); break;
                case RmlUiIntentKind.HunterRotate: _selection.Rotate(intent.Argument == 0 ? -15 : 15); break;
                case RmlUiIntentKind.HunterZoom: _selection.Zoom(intent.Argument == 0 ? -.1f : .1f); break;
                case RmlUiIntentKind.HunterLoopDeath: _selection.SetLoopDeath(!state.LoopDeath); break;
                case RmlUiIntentKind.HunterCompareNative: _selection.CompareNative(!state.CompareNative); break;
                case RmlUiIntentKind.HunterResetPreview: _selection.ResetPreview(); break;
                case RmlUiIntentKind.HunterSpectator:
                    if (_lobby != null) _lobby.Dispatch(_lobby.Intent(LobbyIntentKind.ToggleSpectator)); break;
                case RmlUiIntentKind.LobbyTeamSelect:
                    if (_lobby != null) {
                        var session = _lobby.Snapshot();
                        if (session.LocalSlot >= 0) _lobby.Dispatch(_lobby.Intent(LobbyIntentKind.SetTeam, (byte)session.LocalSlot) with { Team = (sbyte)intent.Argument });
                    } break;
                case RmlUiIntentKind.LobbyTeamNext:
                    if (_lobby != null) {
                        var lobby = _lobby.Snapshot(); var model = LobbyPresentation.From(lobby);
                        var local = lobby.Players.FirstOrDefault(row => row.Slot == lobby.LocalSlot);
                        for (int step = 1; step <= model.Teams.Length; step++) {
                            sbyte next = (sbyte)((Math.Max(0, (int)local.Team) + step) % Math.Max(1, model.Teams.Length));
                            if (model.CanAssignTeam(local.Slot, next)) { _lobby.Dispatch(_lobby.Intent(LobbyIntentKind.SetTeam, local.Slot) with { Team = next }); break; }
                        }
                    } break;
                default: return false;
            }
            Update(); return true;
        }
        public void Rotate(float delta) { if (Active) { _selection.Rotate(delta); Update(); } }
        public void Zoom(float delta) { if (Active) { _selection.Zoom(delta); Update(); } }
        public void Dispose()
        {
            if (_disposed) return;
            _host.VerifyOwnerThread(); _disposed = true;
            if (_ownsSelection) _selection.Dispose();
            if (_host.IsAlive(_document)) _host.CloseDocument(_document);
            Closed?.Invoke();
        }
    }
}
#endif
