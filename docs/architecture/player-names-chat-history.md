# Player names and optional chat history

Player identities remain Unicode in preferences, launcher controls, Hunter License requests and replay metadata. `PlayerNameCodec` normalizes NFC, validates against `MphGlyphMap`, counts supported glyphs and converts only at network/native HUD boundaries. Names have at most 24 glyphs / 48 native bytes. Identify validation rejects malformed, oversized, empty, control, invisible, surrogate and unsupported input. Preferences sanitize older saved values; interactive editors report invalid input.

Protocol **27** carries the new identity widths. Protocols 25 and 26 were already used by this working tree for map identities and bots, so this change does not reuse 25. Roster generation/team/bot offsets, chat, vote and match report names all use the shared capacity; unrelated server/map names keep their existing formats.

The glyph table is the native repertoire, not general Unicode support. Japanese long-vowel and wave-dash entries use `ー` and `〜`. Undefined/blank table entries, including symbols without a known mapping, remain unavailable. The launcher picker inserts Unicode at the selection and displays validation/counts and a native font preview when extracted game files are configured. Native HUD names share measured fitting with a minimum 75% of their preferred scale and presentation-only truncation.

Chat keeps three compact transient rows with the existing timing. During composition, Tab toggles a 12-row history over 128 retained messages. Up/Down, Page Up/Down, Home/End and wheel navigate; incoming messages preserve the scrolled viewport until its oldest message is evicted. Controller Y toggles, D-pad scrolls, bumpers page, A submits and B/Start cancels. Android adds history/newest/send/cancel touch controls and swipe scrolling while consuming game touches. Session entry/exit clears history. Names in join/leave/vote announcements are structured separately from ASCII message text.

Replay packet adapters decode protocol-24/25/26 ASCII identity layouts before passing packets into current readers. Replica checkpoints accept the corresponding legacy roster/session layouts. New chat presentation facts and checkpoint chat state use the recording frame for expiration, so paused playback does not age messages by wall time. Cached library compatibility is refreshed. Unrelated incompatible engine/world schemas remain subject to existing replay validation.

## Verification

- `dotnet run --project tools/nettest -- --player-names-chat`: glyph-table round trips, rejection corpus, 8-slot packets, legacy packet conversion/generated replay opening, chat checkpoints, history retention/navigation and expiration.
- `node --experimental-strip-types supabase/tests/player-name.test.mjs`: backend validation and exact C#/TypeScript repertoire parity.
- Existing protocol, architecture, lobby and replay format checks.
- Desktop, Windows/Linux cross-builds, Android and dedicated-server compile checks.

Manual device/resolution QA (IME, controller navigation in the picker, visual clipping across HUD scales, actual Android touch/keyboard interaction) is still needed. Backend changes are source changes only and require normal deployment. Existing database text/24-character storage already accommodates these names; no schema migration is required.
