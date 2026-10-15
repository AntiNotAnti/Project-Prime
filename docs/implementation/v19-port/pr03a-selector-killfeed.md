# V19 PR-03A: stacked combat lifetime and WPN selector footprint

**Requires:** PR-02A #570 then PR-02B #573.

## Implemented

* The upper-right native WPN/select control has a configurable relative size. Its center is unchanged, the visible ellipse and actual tap region both resolve through StylusZone.EffectiveButton, and width is corrected for 5:3 stretch.
* The maximum 4:3 size is bounded by the actual screen edge. 5:3 uses the same geometry contract and can safely fit its relative stretch.
* The original standalone pen overlay and the new stacked lower HUD share the same effective control geometry; no parallel hit target.
* The original controls.txt and settings archive grammar store stylus_select_size as a finite invariant numeric value; reset returns to 100%.
* The existing Avalonia Pen Tablet page exposes the WPN size with a compact bounded slider.
* In stacked mode, killfeed entries use their own recorded arrival timestamps and individually expire at five seconds. Other HUD modes retain the profile's configurable lifetime.
* PointerCheck covers center/size parity, settings persistence, reset and stacked killfeed duration.

## Not yet implemented in this slice

* Per-element HUD accent/palette and opacity themes (PR-04).
* Full typography and font effects (PR-07/08).
* Authentic gamepad, pen hardware, OpenGL screenshot, high-DPI controls and multiplayer 8-player acceptance.

## Stability rules

Preserve native weapon choice, damage/ack, kill semantics and replay facts; only draw and local input geometry change. No extra enlarge-each-PR stacking and no new RmlUi route. No CI was awaited before committing these changes.