#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Settings;

internal sealed partial class NativeFullSettingsPage
{
    private RmlUiDocumentToken _popup;
    private int _popupKind;
    // Settings remains the logical input owner while its native modal traps
    // pointer, Tab and keyboard focus above the retained settings document.
    private RmlUiDocumentToken ForegroundDocument => _popup != default && _host.IsAlive(_popup) ? _popup : _document;

    private void PresentWithPopup(Dictionary<string,RmlUiBindingValue> fields)
    {
        int kind = _controller.ConfirmingVideo ? 1 : _controller.Capturing ? 2 : _controller.Confirmation.Length > 0 ? 3 : 0;
        var popupFields = new Dictionary<string,RmlUiBindingValue>();
        foreach (string id in new[] { "native_settings_video", "native_settings_video_countdown", "native_settings_keep_video",
            "native_settings_capture", "native_settings_capture_text", "native_settings_capture_modifier",
            "native_settings_confirmation", "native_settings_confirmation_text", "native_settings_confirm",
            "native_settings_conflict_replace", "native_settings_conflict_keep" })
        {
            foreach (string prefix in new[] { "", "visible:", "disabled:" })
            {
                string key = prefix + id;
                if (fields.Remove(key, out var value)) popupFields[key] = value;
            }
        }
        _pages.Present(_document, ++_revision, fields);
        if (_popup != default && !_host.IsAlive(_popup)) { _popup = default; _popupKind = 0; }
        if (kind == 0)
        {
            // Another modal may temporarily cover Settings. Retire only our own
            // topmost popup, preserving the real foreground document's lifetime.
            if (_popup != default && _pages.Top == _popup) ClosePopup();
            return;
        }
        if (_popup == default)
        {
            if (_pages.Top != _document || !_host.IsVisible(_document)) return;
            _popup = _pages.OpenModal(new("native-settings-popup", "pages/settings/native-popup.rml"));
        }
        if (_pages.Top != _popup) return;
        popupFields["native_settings_popup_title"] = RmlUiBindingValue.FromText(kind == 1 ? "KEEP DISPLAY MODE?" : kind == 2 ? "BIND CONTROL" : _controller.BindingConflict ? "BINDING ALREADY IN USE" : "CONFIRM CHANGE");
        _pages.Present(_popup, ++_revision, popupFields);
        if (_popupKind != kind)
        {
            _popupKind = kind;
            _host.FocusDocument(_popup, kind == 1 ? "native_settings_revert_video" : kind == 2 ? "native_settings_capture_cancel" : "native_settings_confirmation_cancel");
        }
    }

    private void ClosePopup()
    {
        if (_popup == default) return;
        if (_host.IsAlive(_popup) && (_pages.Top != _popup || !_pages.CloseModal()))
            throw new InvalidOperationException("Settings popup is covered or could not close.");
        _popup = default; _popupKind = 0;
    }
}
#endif
