# Generated packet foundation

Run content-free checks with:

```
dotnet run --project tools/protocol-generator-check -c Release
```

The incremental Roslyn generator is referenced as an **analyzer only** by the
desktop/server and Android projects. Its Roslyn dependencies belong to build and
test tooling; they must not appear in game runtime dependency manifests. No live
packet uses this foundation yet, and protocol 34 and historical decoders remain
unchanged. Adding production packets requires separate integration and a protocol
bump at that time.

## Schema and wire contract

```csharp
using MphRead.Mods.Network.Generated;

[NetPacket(PacketType.SomeFuturePacket, protocolVersion: 35)]
internal readonly partial record struct ExamplePacket(
    uint MatchId,
    [NetRange(1, 8)] byte Position,
    bool Ready,
    [NetString(96)] string DisplayName);
```

This example does not allocate a live packet ID or protocol version. Packet ID
must fit one byte. The protocol version is required, positive metadata exposed as
`SchemaProtocolVersion`; it does not add bytes to the payload or change NetConfig.

Fields serialize in positional constructor order after the one-byte packet ID.
Integers use little endian, booleans are exactly 0 or 1, enums accept only their
declared values, and strings have an unsigned 16-bit UTF-8 **byte** length prefix.
UTF-8 rejects malformed input and unpaired UTF-16 surrogates. Null strings are
invalid; empty strings are valid. No trailing bytes are accepted by `TryRead`.

Supported types: byte/sbyte, ushort/short, uint/int, ulong/long, bool, non-Flags
enums with those underlying types, and bounded strings. `NetRange` uses inclusive
signed 64-bit bounds and must fit the field type. Unbounded ulong still supports
its full range. Maximum packet size is taken from the consuming
`MphRead.Mods.Network.NetConfig.MaxPacketSize`; isolated consumers without that
symbol use the current 1472-byte control-packet ceiling. Realtime 1200-byte lane
budgets must still be enforced when integrating into their transport.

Schemas must be top-level, non-generic, readonly partial positional record
structs with no additional instance state or properties. Generated members are
reserved. Arrays, hashes, arbitrary objects/custom identity structs, nullable
values and Flags enums are deliberately rejected; `NetFixedArray` is a reserved
marker, not implemented support. Add explicit contracts and tests before expanding
the supported schema. Identity relationships cannot be inferred from primitive
fields and require semantic validation at future production integration.

Generated API:

- `MinimumSize`, `MaximumSize`, and `Size` for fixed packets.
- `Validate()` rejects invalid values; invalid `EncodedSize`/`Write` throws.
- `Write(Span<byte>)` validates values and capacity before mutation; a larger
  destination is allowed and its unused suffix stays untouched.
- `TryRead(ReadOnlySpan<byte>, out Packet)` fails closed with a default output.

Diagnostics NETGEN001–008 cover unsupported schemas, unbounded strings, invalid
ranges, invalid enums, unsupported variable fields/arrays, oversized packets,
duplicate packet IDs, and invalid protocol metadata. Duplicate IDs are rejected
across all generated declarations in one compilation.

The check compiles synthetic schemas through Roslyn, exercises every diagnostic,
then executes golden fixed/variable bytes, round trips, every truncation, trailing
bytes, wrong packet IDs, bad ranges/enums/booleans/string lengths/UTF-8, byte-size
limits, signed and unsigned extremes, failed-write atomicity, and 5,000 seeded
malformed datagrams. It requires no game assets or graphics device.
