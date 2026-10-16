using System;
using System.Collections.Generic;
using System.Threading;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public sealed record RmlUiPageSpec(string Key, string Path, string InitialFocus = "");

    /// <summary>Document composition only. Controllers own routes, services, and editable drafts.</summary>
    public sealed class RmlUiPageManager : IDisposable
    {
        private sealed class Entry
        {
            public readonly RmlUiPageSpec Spec;
            public readonly RmlUiDocumentToken Document;
            public readonly string RestoreFocus;
            public ulong LastSequence;
            public Entry(RmlUiPageSpec spec, RmlUiDocumentToken document, string restoreFocus = "")
                => (Spec, Document, RestoreFocus) = (spec, document, restoreFocus);
        }

        private readonly RmlUiHost _host;
        private readonly List<Entry> _modals = new();
        private Entry? _page;
        private RmlUiDocumentToken _baseline;
        private string _baselineFocus = "";
        private bool _disposed;
        private bool _changingDocuments;
        private bool _visible = true;

        public RmlUiPageManager(RmlUiHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));
        public RmlUiDocumentToken Page { get { Reconcile(); return _page?.Document ?? default; } }
        public RmlUiDocumentToken Top { get { Reconcile(); return _modals.Count > 0 ? _modals[^1].Document : Page; } }
        public string? PageKey { get { Reconcile(); return _page?.Spec.Key; } }
        public int ModalCount { get { Reconcile(); return _modals.Count; } }

        public RmlUiDocumentToken OpenPage(RmlUiPageSpec spec) => Change(() => OpenPageCore(spec));

        private RmlUiDocumentToken OpenPageCore(RmlUiPageSpec spec)
        {
            RequireUsable(spec);
            if (_page?.Spec == spec) return _page.Document;
            if (!CloseModals()) throw new InvalidOperationException("The active modal could not be closed.");
            var previous = _page;
            if (_baseline == default)
            {
                _baseline = _host.HomeDocument;
                _baselineFocus = _host.FocusedElement();
            }
            RmlUiDocumentToken document;
            try { document = _host.OpenDocument(spec.Path, RmlUiDocumentLayer.Page); }
            catch
            {
                if (previous == null) { _baseline = default; _baselineFocus = ""; }
                throw;
            }
            try
            {
                _page = null;
                if (previous != null && !_host.CloseDocument(previous.Document))
                    throw new InvalidOperationException("The previous page could not be closed.");
                if (!_host.ShowDocument(_baseline, false))
                    throw new InvalidOperationException("The launcher shell could not be hidden.");
                _page = new(spec, document);
                _host.ShowDocument(document, _visible);
                Focus(document, spec.InitialFocus);
                return document;
            }
            catch
            {
                _host.CloseDocument(document);
                _page = previous != null && _host.IsAlive(previous.Document) ? previous : null;
                if (_page == null) RestoreBaseline();
                throw;
            }
        }

        public RmlUiDocumentToken OpenModal(RmlUiPageSpec spec) => Change(() => OpenModalCore(spec));

        private RmlUiDocumentToken OpenModalCore(RmlUiPageSpec spec)
        {
            RequireUsable(spec);
            if (_page == null) throw new InvalidOperationException("Open a page before its modal.");
            if (_modals.Count >= 8) throw new InvalidOperationException("Too many launcher modals.");
            string restoreFocus = _host.FocusedElement();
            var document = _host.OpenDocument(spec.Path, RmlUiDocumentLayer.Modal);
            _modals.Add(new(spec, document, restoreFocus));
            _host.ShowDocument(document, _visible);
            Focus(document, spec.InitialFocus);
            return document;
        }

        public bool Present(RmlUiDocumentToken document, long revision,
            IEnumerable<KeyValuePair<string, RmlUiBindingValue>> bindings)
        {
            Reconcile();
            if (Find(document) == null) return false;
            return _host.Present(new(document, revision, bindings));
        }

        /// <summary>Only the foreground document may dispatch; delayed or duplicate clicks are rejected.</summary>
        public bool Accept(in RmlUiIntent intent)
        {
            Reconcile();
            Entry? target = _modals.Count > 0 ? _modals[^1] : _page;
            if (_disposed || !_visible || target == null || target.Document != intent.Document || intent.Sequence == 0
                || intent.Sequence <= target.LastSequence || !RmlUiIntentRegistry.IsValid(intent.Kind, intent.Argument))
                return false;
            target.LastSequence = intent.Sequence;
            return true;
        }

        public CancellationToken Lifetime(RmlUiDocumentToken document)
        {
            Reconcile();
            return Find(document) == null ? new CancellationToken(canceled: true)
                : _host.DocumentCancellation(document);
        }

        public bool CloseModal() => Change(CloseModalCore);

        private bool CloseModalCore()
        {
            Reconcile();
            if (_modals.Count == 0) return false;
            Entry closing = _modals[^1];
            // Remove local ownership before native retirement invokes cancellation callbacks.
            _modals.RemoveAt(_modals.Count - 1);
            if (!_host.CloseDocument(closing.Document))
            {
                _modals.Add(closing);
                return false;
            }
            Focus(Top, closing.RestoreFocus);
            return true;
        }

        /// <summary>Returns true when Back dismissed a modal; otherwise the application router handles Back.</summary>
        public bool Back() => CloseModal();

        public bool ClosePage() => Change(ClosePageCore);

        private bool ClosePageCore()
        {
            Reconcile();
            if (!CloseModals()) return false;
            if (_page != null)
            {
                Entry closing = _page;
                _page = null;
                if (!_host.CloseDocument(closing.Document))
                {
                    _page = closing;
                    return false;
                }
            }
            RestoreBaseline();
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (!ClosePage()) throw new InvalidOperationException("The launcher page could not be closed.");
            _disposed = true;
        }

        private bool CloseModals()
        {
            while (_modals.Count > 0) if (!CloseModalCore()) return false;
            return true;
        }

        public void SetVisible(bool visible)
        {
            Change(() =>
            {
                Reconcile();
                _visible = visible;
                if (_page != null) _host.ShowDocument(_page.Document, visible);
                foreach (var modal in _modals) _host.ShowDocument(modal.Document, visible);
                return true;
            });
        }

        private T Change<T>(Func<T> action)
        {
            _host.VerifyNativeCallAllowed();
            if (_changingDocuments) throw new InvalidOperationException("Document retirement cannot reenter the page manager.");
            if (_disposed) throw new ObjectDisposedException(nameof(RmlUiPageManager));
            _changingDocuments = true;
            try { return action(); }
            finally { _changingDocuments = false; }
        }

        private void RestoreBaseline()
        {
            if (_baseline != default && _host.IsAlive(_baseline))
            {
                _host.ShowDocument(_baseline, true);
                Focus(_baseline, _baselineFocus);
            }
            _baseline = default;
            _baselineFocus = "";
        }

        private Entry? Find(RmlUiDocumentToken document)
        {
            if (_page?.Document == document) return _page;
            return _modals.Find(entry => entry.Document == document);
        }

        private void Focus(RmlUiDocumentToken document, string element)
        {
            if (_visible && document != default && !String.IsNullOrEmpty(element)) _host.FocusDocument(document, element);
        }

        private void RequireUsable(RmlUiPageSpec spec)
        {
            ArgumentNullException.ThrowIfNull(spec);
            if (String.IsNullOrWhiteSpace(spec.Key)) throw new ArgumentException("A page key is required.", nameof(spec));
            Reconcile();
            if (_disposed) throw new ObjectDisposedException(nameof(RmlUiPageManager));
        }

        private void Reconcile()
        {
            _host.VerifyOwnerThread();
            for (int index = _modals.Count - 1; index >= 0; index--)
                if (!_host.IsAlive(_modals[index].Document)) _modals.RemoveAt(index);
            if (_page != null && !_host.IsAlive(_page.Document)) _page = null;
            if (_baseline != default && !_host.IsAlive(_baseline))
            {
                _baseline = default;
                _baselineFocus = "";
            }
        }
    }
}
