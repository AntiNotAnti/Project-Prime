# Prime multiplayer regression probes

QZ0-A keeps deterministic xUnit regressions under `tests/Tests/Regression/MultiplayerHistory/`
and `tests/Server.Node.Tests/Regression/`. This directory is reserved for
content-free `nettest` probes that need a process boundary or real UDP framing;
QZ0-A does not add a foreign binary, capture, or live-network dependency.

Any future probe added here must use a fixed seed and report the Prime invariant
it checks. A successful static or loopback probe is not evidence of WAN or
rendered-client behavior.
