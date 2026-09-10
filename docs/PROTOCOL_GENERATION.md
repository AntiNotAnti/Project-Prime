# Protocol generation

Protocol 9 additions may opt into the bounded Roslyn generator in
`src/Protocol.Generator`. The generator is an analyzer-only dependency of
`ProjectPrime.Game`; no Roslyn assembly is loaded by the game at runtime.

```csharp
[NetPacket(NetMessageType.JoinPending, protocol: 9)]
public readonly partial record struct JoinPendingPacket(
    [property: NetUnsignedRange(1, ulong.MaxValue)] ulong Nonce);
```

Generated packets expose `MinimumSize`, `MaximumSize`, `Size`, `EncodedSize`,
`Validate`, `Write`, and strict `TryRead`. The current MVP is deliberately
fixed-width and supports byte/sbyte, ushort/short, uint/int, ulong/long,
bool, defined enums, `Guid`, finite `Vector3`, fixed arrays, and fixed-width
NUL-terminated UTF-8 strings. Arrays have an explicit element count and
strings have an explicit maximum that includes the terminator. Unsupported,
unbounded, contradictory, or non-Protocol-9 schemas fail at compile time with
`NETGEN00x` diagnostics.

`JoinPendingPacket` is the first production use. It replaces the hand-written
eight-byte Protocol 9 bot-admission heartbeat while preserving its exact
little-endian wire bytes. `MatchAwardPacket` and `MatchSemanticEventPacket`
carry the numeric QZ3 award and normalized semantic facts. All three schemas
declare Protocol 9; Protocol 8 packet types are intentionally untouched. The
generator is not applied to Node control DTOs, which remain strict
source-generated `System.Text.Json` contracts in `NodeControlCodec` and
`NodeJsonContext`.

The conformance fixture applies exact-length and model/wire validation through
the same public generated API used in production. An inapplicable category is
recorded as such instead of adding a dummy field and changing the wire schema:

| Generated packet | Round trip | Every truncation | Oversize/trailing | Bad enum | Bad range | Non-finite vector |
| --- | --- | --- | --- | --- | --- | --- |
| `JoinPendingPacket` | yes, including golden bytes | rejected | rejected | N/A: no enum | zero nonce rejected | N/A: no vector |
| `MatchAwardPacket` | yes | rejected | rejected | rejected | zero award ID rejected | N/A: no vector |
| `MatchSemanticEventPacket` | yes | rejected | rejected | rejected | zero event ID and flags above `0x7F` rejected | N/A: no vector |
| test-only all-shapes fixture | yes | rejected | rejected | rejected | rejected | rejected |

Focused round-trip, every-truncation, oversize/trailing, finite-value, enum,
range, UTF-8, golden-byte, and compile-time diagnostic tests live in
`tests/Protocol.Generator.Tests/Protocol.Generator.Tests.csproj`.
