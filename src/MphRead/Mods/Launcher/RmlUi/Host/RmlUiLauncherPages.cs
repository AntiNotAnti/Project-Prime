using System;
using System.Collections.Generic;
using System.Globalization;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    /// <summary>Adapts the shipped Home, Play, Lobby and Rules publishers to composed documents.
    /// Network/rules controllers retain authority. Editable values are copied only on explicit field writes or document retirement.</summary>
    public sealed class RmlUiLauncherPages : IDisposable
    {
        private static readonly string[] ActivityNames = { "quick", "browser", "offline", "adventure", "training" };
        private static readonly string[][] ActivityCopy =
        {
            new[] { "MULTIPLAYER", "QUICK PLAY", "Find the best compatible public hunt.", "PUBLIC MATCHMAKING", "DEPLOY" },
            new[] { "MULTIPLAYER", "LOBBY BROWSER", "Browse live public and private sessions.", "LIVE DIRECTORY", "BROWSE LOBBIES" },
            new[] { "LOCAL PLAY", "OFFLINE BATTLE", "Bots and custom match rules.", "LOCAL SESSION", "CONFIGURE MATCH" },
            new[] { "SOLO", "ADVENTURE", "Continue or load a solo save.", "SAVE DATA", "CONTINUE" },
            new[] { "TRAINING", "AIM LAB", "Practice your aim with dedicated drills.", "PERSONAL TRAINING", "CHOOSE DRILL" }
        };
        private static readonly string[] PlayFields =
            { "play_player_name", "play_join_address", "play_create_name" };
        private static readonly string[] RulesFields = { "rules_time", "rules_goal" };
        private readonly RmlUiHost _host;
        private readonly bool _ownsManager;
        private readonly Dictionary<string, RmlUiBindingValue> _model = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);
        private RmlUiDocumentToken _pageDocument, _rulesDocument;
        private RmlUiMenuPage _page = RmlUiMenuPage.Home;
        private long _revision;
        private ulong _lastSequence;
        private int _activity;
        private bool _dirty = true, _suspended, _disposed, _resumeRequested;
        private string _pendingFocus = "";

        public RmlUiPageManager Manager { get; }
        public RmlUiMenuPage Page => _page;
        public int SelectedActivityIndex => _activity;
        public bool Suspended => _suspended;
        public RmlUiDocumentToken Document => _pageDocument;

        public RmlUiLauncherPages(RmlUiHost host, RmlUiPageManager? manager = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _ownsManager = manager == null;
            Manager = manager ?? new(host);
            Put("home_mode", true);
            Put("multiplayer_mode", false);
            Put("lobby_mode", false);
            Put("play_browser_mode", false);
            Put("play_create_mode", false);
            Put("lobby_rules_open", false);
            Put("home_party_state", RmlUiBindingValue.FromText("FRIENDS AND PARTY"));
            Put("home_social_summary", RmlUiBindingValue.FromText("SOCIAL LINK STANDING BY"));
            Put("home_friends_online", RmlUiBindingValue.FromText("0"));
            Put("home_invites_count", RmlUiBindingValue.FromText("0"));
            Put("home_requests_count", RmlUiBindingValue.FromText("0"));
            Put("home_session_state", RmlUiBindingValue.FromText("SOLO"));
            Put("home_feature_available", false);
            Put("home_feature_category", RmlUiBindingValue.FromText(""));
            Put("home_feature_title", RmlUiBindingValue.FromText(""));
            Put("home_feature_summary", RmlUiBindingValue.FromText(""));
            Put("home_social_rail_visible", false);
            Put("home_social_alert_visible", false);
            Put("home_social_count", RmlUiBindingValue.FromText("0 ONLINE"));
            Put("home_social_alert_text", RmlUiBindingValue.FromText(""));
            Put("home_party_preview_visible", false);
            Put("home_party_preview_name", RmlUiBindingValue.FromText(""));
            Put("home_party_preview_role", RmlUiBindingValue.FromText(""));
            Put("home_friends_visible", false);
            Put("home_friend_count", RmlUiBindingValue.FromText("0 ONLINE"));
            for (int i = 0; i < 3; i++)
            {
                Put($"home_friend{i}_visible", false);
                Put($"home_friend{i}_joinable", false);
                Put($"home_friend{i}_name", RmlUiBindingValue.FromText(""));
                Put($"home_friend{i}_activity", RmlUiBindingValue.FromText(""));
                Put($"home_friend{i}_status", RmlUiBindingValue.FromText(""));
            }
            SelectActivity(1);
        }

        public void SetText(string name, string value)
        {
            Verify();
            // The old multiplayer publisher used this shortened text name.
            if (name == "play_create_mode") name = "play_create_mode_name";
            Put(name, RmlUiBindingValue.FromText(value));
        }

        public void SetBool(string name, bool value)
        {
            Verify();
            bool wasLobby = Bool("lobby_mode");
            Put(name, value);
            if (name == "lobby_mode")
            {
                Put("home_mode", !value);
                if (value)
                {
                    Put("multiplayer_mode", false);
                    Put("play_create_mode", false);
                    Put("play_browser_mode", false);
                    if (!wasLobby) Put("lobby_rules_open", false);
                }
            }
            else if (name == "multiplayer_mode")
            {
                Put("home_mode", !value && !Bool("lobby_mode"));
                if (value) Put("lobby_mode", false);
            }
        }

        public RmlUiDocumentToken ShowBaseline(RmlUiMenuPage? page = null)
        {
            Verify();
            _suspended = false;
            _resumeRequested = true;
            if (page is RmlUiMenuPage.Rules) throw new ArgumentException("Rules is a modal, not a route.", nameof(page));
            if (page is { } selected) SwitchMode(selected);
            Flush();
            Manager.SetVisible(true);
            return _pageDocument;
        }

        /// <summary>Other route presenters use the same manager; periodic baseline publishers continue to update the cache.</summary>
        public void Suspend() { Verify(); CaptureFields(); _suspended = true; }
        public void Resume() => ShowBaseline();
        public void SetVisible(bool visible) { Verify(); Manager.SetVisible(visible); }

        /// <summary>Hydrate a route that uses prime-shell, without competing with its presenter revision.</summary>
        public void PresentChrome(RmlUiDocumentToken document)
        {
            Verify();
            if (!_host.IsAlive(document)) return;
            foreach (var binding in RmlUiMenuPageBindings.ProjectShell(_model))
            {
                if (binding.Value.Kind == RmlUiBindingKind.Text) _host.SetText(document, binding.Key, binding.Value.Text);
                else _host.SetBool(document, binding.Key, binding.Value.Boolean);
            }
        }

        public void SetField(string id, string value)
        {
            Verify();
            _fields[id] = value ?? "";
            var target = FieldDocument(id);
            if (target != default && _host.IsAlive(target)) _host.SetField(target, id, value ?? "");
        }

        public string ReadField(string id)
        {
            Verify();
            var target = FieldDocument(id);
            if (target != default && _host.IsAlive(target))
                return _fields[id] = _host.ReadField(target, id);
            return _fields.GetValueOrDefault(id, "");
        }

        /// <summary>Call before the host Update. Render-thread publication batches all controller changes into one revision.</summary>
        public void Flush()
        {
            Verify();
            if (!_host.Active) return;
            var currentPage = Manager.Page;
            if (!_resumeRequested && currentPage != default && currentPage != _pageDocument)
                _suspended = true;
            _resumeRequested = false;
            if (_suspended) return;
            RmlUiMenuPage desired = Bool("lobby_mode") ? RmlUiMenuPage.Lobby
                : Bool("multiplayer_mode") ? RmlUiMenuPage.Play : RmlUiMenuPage.Home;
            if (!_host.IsAlive(_pageDocument) || _page != desired)
            {
                CaptureFields();
                _rulesDocument = default;
                _pageDocument = Manager.OpenPage(RmlUiMenuPages.Spec(desired));
                _page = desired;
                SeedFields(desired == RmlUiMenuPage.Play ? PlayFields : Array.Empty<string>(), _pageDocument);
                _dirty = true;
                _pendingFocus = desired == RmlUiMenuPage.Lobby && !Bool("lobby_require_ready")
                    ? "lobby_hunter" : RmlUiMenuPages.Spec(desired).InitialFocus;
            }
            bool rules = _page == RmlUiMenuPage.Lobby && Bool("lobby_rules_open");
            if (rules && !_host.IsAlive(_rulesDocument))
            {
                _rulesDocument = Manager.OpenModal(RmlUiMenuPages.Rules);
                SeedFields(RulesFields, _rulesDocument);
                _pendingFocus = "rules_close_top";
                _dirty = true;
            }
            else if (!rules && _host.IsAlive(_rulesDocument))
            {
                Capture(RulesFields, _rulesDocument);
                Manager.CloseModal();
                _rulesDocument = default;
            }
            if (!_dirty) return;
            _dirty = false;
            long revision = ++_revision;
            Manager.Present(_pageDocument, revision, RmlUiMenuPageBindings.Project(_page, _model));
            if (_host.IsAlive(_rulesDocument))
                Manager.Present(_rulesDocument, revision, RmlUiMenuPageBindings.Project(RmlUiMenuPage.Rules, _model));
        }

        /// <summary>Conditional display changes must be laid out before focus is restored.</summary>
        public void AfterUpdate()
        {
            Verify();
            if (_suspended || String.IsNullOrEmpty(_pendingFocus)) return;
            var target = _host.IsAlive(_rulesDocument) ? _rulesDocument : _pageDocument;
            if (_host.FocusDocument(target, _pendingFocus)) _pendingFocus = "";
        }

        /// <summary>True means this adapter owned the event. A zero Kind means it was consumed locally.</summary>
        public bool HandleIntent(in RmlUiIntent intent, out RmlUiIntent forwarded)
        {
            Verify();
            forwarded = default;
            if (_suspended || (intent.Document != _pageDocument && intent.Document != _rulesDocument)) return false;
            if (!Manager.Accept(intent)) return true;
            _lastSequence = Math.Max(_lastSequence, intent.Sequence);
            string action = RmlUiIntentRegistry.ToLegacy(intent);
            switch (action)
            {
                case "home:drawer-open":
                    bool wasOpen = Bool("activity_selector_open");
                    Put("activity_selector_open", !wasOpen);
                    _pendingFocus = !wasOpen ? "drawer_" + ActivityNames[_activity] : "activity_selector";
                    Flush();
                    if (wasOpen) forwarded = Forward("stage:" + ActivityNames[_activity], intent.Sequence);
                    return true;
                case "home:drawer-close":
                    Put("activity_selector_open", false);
                    _pendingFocus = "activity_selector";
                    Flush();
                    forwarded = Forward("stage:" + ActivityNames[_activity], intent.Sequence);
                    return true;
                case "home:deploy":
                    if (_activity < 2)
                    {
                        SwitchMode(RmlUiMenuPage.Play);
                        Put("play_create_mode", false);
                        Put("play_browser_mode", true);
                        Flush();
                    }
                    forwarded = Forward(_activity switch
                    {
                        0 => "play:quick", 1 => "play:browse", 2 => "route:offline", 4 => "route:training", _ => "route:adventure"
                    }, intent.Sequence);
                    return true;
                case "play:create-open":
                    Put("play_create_mode", true);
                    Put("play_browser_mode", false);
                    _pendingFocus = "play_create_name";
                    Flush();
                    break;
                case "play:cancel":
                    if (Bool("play_create_mode"))
                    {
                        Put("play_create_mode", false);
                        Put("play_browser_mode", true);
                        _pendingFocus = "play_quick";
                        Flush();
                        forwarded = Forward("play:browse", intent.Sequence);
                        return true;
                    }
                    SwitchMode(RmlUiMenuPage.Home);
                    Put("play_browser_mode", false);
                    Flush();
                    break;
            }
            if (intent.Kind == RmlUiIntentKind.StageSelect)
            {
                SelectActivity(intent.Argument);
                Put("activity_selector_open", false);
                _pendingFocus = "activity_selector";
                Flush();
            }
            forwarded = intent with { Document = _host.IsAlive(_rulesDocument) ? _rulesDocument : _pageDocument };
            return true;
        }

        public bool Back(out RmlUiIntent forwarded)
        {
            Verify();
            forwarded = default;
            if (_suspended) return false;
            if (_host.IsAlive(_rulesDocument))
            {
                Put("lobby_rules_open", false);
                Flush();
                forwarded = Forward("lobby:rules-close", ++_lastSequence);
                return true;
            }
            if (_page == RmlUiMenuPage.Lobby)
            {
                forwarded = Forward("lobby:leave", ++_lastSequence);
                return true;
            }
            if (_page == RmlUiMenuPage.Play)
            {
                bool creating = Bool("play_create_mode");
                Put("play_create_mode", false);
                Put("play_browser_mode", creating);
                if (!creating) SwitchMode(RmlUiMenuPage.Home);
                _pendingFocus = creating ? "play_quick" : "activity_selector";
                Flush();
                forwarded = Forward(creating ? "play:browse" : "play:cancel", ++_lastSequence);
                return true;
            }
            if (!Bool("activity_selector_open")) return false;
            Put("activity_selector_open", false);
            _pendingFocus = "activity_selector";
            Flush();
            forwarded = Forward("stage:" + ActivityNames[_activity], ++_lastSequence);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_ownsManager) Manager.Dispose();
            _disposed = true;
            _model.Clear();
            _fields.Clear();
        }

        private void SelectActivity(int index)
        {
            if (index < 0 || index >= ActivityNames.Length) throw new ArgumentOutOfRangeException(nameof(index));
            _activity = index;
            Put("activity_index", RmlUiBindingValue.FromText(index.ToString(CultureInfo.InvariantCulture)));
            string[] keys = { "activity_group", "activity_title", "activity_description", "activity_hint", "activity_action" };
            for (int i = 0; i < keys.Length; i++) Put(keys[i], RmlUiBindingValue.FromText(ActivityCopy[index][i]));
        }

        private void SwitchMode(RmlUiMenuPage page)
        {
            Put("home_mode", page == RmlUiMenuPage.Home);
            Put("multiplayer_mode", page == RmlUiMenuPage.Play);
            Put("lobby_mode", page == RmlUiMenuPage.Lobby);
            if (page != RmlUiMenuPage.Lobby) Put("lobby_rules_open", false);
            if (page != RmlUiMenuPage.Home) Put("activity_selector_open", false);
        }

        private RmlUiIntent Forward(string action, ulong sequence)
        {
            var target = _host.IsAlive(_rulesDocument) ? _rulesDocument : _pageDocument;
            if (!RmlUiIntentRegistry.TryParseLegacy(action, target, sequence, out var intent))
                throw new InvalidOperationException($"The action registry is missing '{action}'.");
            return intent;
        }

        private RmlUiDocumentToken FieldDocument(string id) => id.StartsWith("rules_", StringComparison.Ordinal)
            ? _rulesDocument : _page == RmlUiMenuPage.Play && !_suspended ? _pageDocument : default;

        private void CaptureFields()
        {
            if (_page == RmlUiMenuPage.Play && _host.IsAlive(_pageDocument)) Capture(PlayFields, _pageDocument);
            if (_host.IsAlive(_rulesDocument)) Capture(RulesFields, _rulesDocument);
        }

        private void Capture(string[] fields, RmlUiDocumentToken document)
        {
            foreach (string field in fields) _fields[field] = _host.ReadField(document, field);
        }

        private void SeedFields(string[] fields, RmlUiDocumentToken document)
        {
            foreach (string field in fields)
                if (_fields.TryGetValue(field, out string? value)) _host.SetField(document, field, value);
        }

        private bool Bool(string name) => _model.TryGetValue(name, out var value) && value.Boolean;
        private void Put(string name, bool value) => Put(name, RmlUiBindingValue.FromBoolean(value));
        private void Put(string name, RmlUiBindingValue value)
        {
            if (_model.TryGetValue(name, out var previous) && previous == value) return;
            _model[name] = value;
            _dirty = true;
        }

        private void Verify()
        {
            _host.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(RmlUiLauncherPages));
        }
    }
}
