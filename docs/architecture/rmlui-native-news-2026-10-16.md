# RML-08: Native News and shared source data

This slice restores the historical native News page/controller and its
modal flow, while removing the duplicate local article data from the
Avalonia NewsWorkspace. Both launchers now use one `BundledNewsProvider`
and the same four immutable dispatches.

Native Home NEWS opens `pages/news/news.rml`, hides Home through
`RmlUiPageManager`, filters/selects current articles via the versioned
NewsAction intent, opens and dismisses a native detail modal, and handles
the single allowlisted official Discord invite with a text fallback.
The Back button restores Home; navigation to other legacy workspaces
disposes its native page and releases focus before the existing route
runs.

The News controller has explicit owner-thread checks, lifetime/revision
tracking, bounded selections and modal top-document intent validation.
The native presenter is compiled only in the opt-in desktop RmlUi build,
and all runtime markup/assets are provided by the RML-07 resource slice.
The default and Android Avalonia interface are unchanged.

`tools/rmlui-news-check` proves bundled-provider sharing, filter bounds,
revision monotonicity, modal behavior, the HTTPS link fallback and owner
thread safety without game files or network calls.

This is functional News parity on an opt-in native desktop client, not a
claim of Settings/Community/License/in-game/Android completeness.
