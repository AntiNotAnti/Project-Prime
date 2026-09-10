# Stylus input

Android `MotionEvent` and desktop SDL pen events become `PointerSample` values.
Each sample preserves pointer ID, tool, coordinates, pressure, buttons,
timestamp, and any coordinate-space metadata the platform actually knows.

Contact is pointer-owned and relative. The first contact establishes an anchor
and emits zero look; movement emits relative degrees through
`LookInputCoordinator`; lift releases ownership while preserving already queued
motion; cancellation or proximity loss discards only pending stylus motion.
Recontact always starts a fresh anchor.

Gesture detection is parallel to aim. A fast contact movement always submits
valid look and may independently emit `FlickBoost`; gameplay still gates that
gesture to an eligible alt form. Double tap, pressure-to-fire, primary/secondary
buttons, and flick settings do not own or suppress camera motion.

Stationary Android `ACTION_BUTTON_PRESS` and `ACTION_BUTTON_RELEASE` events use
the button-only router path, so Fire/Zoom edges need no movement. Hover and
contact are separate states. Hover may update proximity and button state but
never claims look ownership, and hover buttons do not activate gameplay binds.

While stylus contact owns aim, finger aim gestures are suppressed. The left
movement stick, visible touch buttons, and weapon UI remain available, so palm
rejection does not disable the whole touch layer.

Direct pens divide deltas by known logical display/DPI scale. Indirect tablets
use a mapped extent only when an adapter supplies one; SDL currently exposes
device type but not a physical tablet extent, so the fallback deliberately
retains density-only scaling rather than inventing dimensions. S Pen, Surface,
Wacom, and Huion measurements remain required before claiming equivalent
physical sensitivity.

Android Settings includes an optional pen-only turn preview. Dragging across
the row reports the logical degrees produced by the current sensitivity; it
does not claim gameplay input or make calibration mandatory.
