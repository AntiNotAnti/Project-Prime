# RML-85 Community library continuation

Date: October 18, 2026.

User scope remains RmlUi exclusively. RML-84 Home/Lobby layout is preserved.
Full Settings remains the final migration slice and must follow SettingsView.

## Implemented

- Active native Community Installed and Favorites tabs, retaining Discover,
  My Uploads and Downloads/History command identities. Managed and native tab
  decoding admit the new bounded arguments without changing the ABI.
- Installed rows come from the existing validated immutable registry under the
  configured-files read capability, with disk/index work off the compositor.
  Local identities show exact archive hash and existing map-picker guidance.
- Favorites reads/mutations use the existing leased Hunter License/Community
  facade. Private rows retire on account change; stale/canceled completions cannot
  repopulate current UI. Mutation completion triggers a fresh catalog read,
  retaining actual service counts rather than optimistic state.
- Exact-package copy links use the configured address policy and native clipboard,
  with an injectable clipboard seam for tests. Local installed-only entries do
  not invent a public download URL.
- Published revision metadata includes size, favorite count, protocol compatibility
  and release notes. Installation rejects packages requiring a newer protocol.
- Selected published previews use one cancellable download and existing bounded
  RGB/RGBA PNG-to-TGA decoder. Payload and decoded dimensions are bounded; late
  selections/disposal cannot publish old images. Decoder failures become visible.
- Community's old FULL COMMUNITY / STUDIO button now opens the existing native
  Studio explicitly. Report, local package import and install/host continuation
  remain separate unfinished migration items.
- Native Hunter customization explains graphics settings that affect cosmetic
  visibility, retaining draft, comparison and preview state.

## Compilation and evidence

Client/server compilation passes with zero warnings/errors. Managed assembly:
`416020d7865cb740199b2a28d2a6999d59499240d7c514eabbdb90a3d2d822cc`.
Matching Mac ARM native bridge:
`84434fef4a5a36549bc8133c76852fa2174d6bdb0699aeba073a6af903c0ffbd`.

Frozen runtime/source acceptance is recorded under
`artifacts/rmlui-community-library-rml85-20261018/build-attempt2/`.
All five CPU suites and the headless real-RGB preview acceptance pass with
unchanged pre/post source/runtime manifests. Preview coverage includes exact
pixels, malformed payloads, size limits, selection cancellation and disposal.

Native library acceptance passes at 640×360. The 960×600 and 1280×720 runs
preserve a failing installed-detail semantic assertion: screenshots show the
expected text, but plain paragraphs lack stable accessibility labels. RML-86
adds explicit paragraph roles, split assertions and raw semantic evidence;
those corrected runs are required before closing library qualification.
They now pass at all three sizes in
[RML-86](rmlui-community-report-rml86-2026-10-18.md), which also delivers the
qualified library in its signed Mac ARM package.
No live Favorites mutation or external report has been performed by acceptance.
This continuation does not mark Community or the full migration complete.
