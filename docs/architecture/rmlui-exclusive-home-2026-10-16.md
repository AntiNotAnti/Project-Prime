# RML-04: Exclusive native Home composition

## Design

RML-03 provided a live RmlUi input/service bridge over the existing shell. This
slice corrects dual UI composition. The desktop **opt-in** native Home now
replaces the Avalonia chrome for one scene-free frame, instead of painting
two sets of buttons on top of the same photograph.

- The existing OpenGL background and Hunter stand remain visible.
- A native host is initialized **before** the home compositor decides whether
  to suppress the Avalonia bitmap and Studio viewport.
- If initialization fails, the existing UI is presented and retains input.
- When entering setup, modal, editor, other workspace or gameplay, RmlUi
  releases input and the existing client draws normally.
- The old launcher remains the shipping default unless both the compile flag
  and environment opt-in are present.
- F11 and Alt+Enter remain window-owned fullscreen shortcuts with a native
  menu focused.

## Acceptance

The opt-in client compilation, managed Home/Play/Lobby contracts and pinned
native OpenGL software-frame suite run in CI. Screenshots and physical-device
input/resize acceptance remain to be done before making native Home default.

## Following work

Complete current lobby creation, rules editing and Hunter selection with
authoritative session services, then update Community/License/Settings and
cross-platform device support. The compatibility switch remains reversible.
