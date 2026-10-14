# Genuine recording clip diagnostic

This device-free friend assembly invokes the production Studio operation:
`ReplayVirtualClips.Save` → `Materialize` → `ReplayArchive.Extract`.
The existing CLI exposes `-replayclipcheck SOURCE -clip CLIP -start FRAME`,
but does not expose Studio's archive export operation. `-replayexportcheck`
is a separate render/export check and is not substituted here.

The diagnostic uses an existing recording; it does not manufacture packets,
checkpoints or clips as a positive fixture. It verifies the loaded physical
client hash against the supplied frozen directory and expected campaign hash,
the input recording hash, and recording producer BuildId against the loaded
client. All export/playback work runs with `Headless.Enter`, isolated user data
and supplied local asset paths. No GPU/audio device or live network session is
created. No assets are shipped.

It checks production archive validation, a nonempty interior range without
silent end clipping, exact exported duration, every visible gameplay and
presentation frame through `ReplayClipFidelity`, forward/backward private seeks,
original recording clock mapping, a real private world capture/restore at the
range midpoint, private grant state equality where supported, and preservation
of foreground facade owners. A wrong source mapping and a truncated exported
archive must be rejected. Missing historical grant capability remains null.
Raw receipts and clip artifacts persist under the supplied output directory.

Build against the exact frozen client that produced the recording (the tool
never builds the client):

```sh
dotnet build tools/replay-clip-acceptance/replay-clip-acceptance.csproj -c Release \
  -p:AcceptanceBinaryDirectory=/absolute/frozen-client \
  -p:OutDir=/absolute/diagnostic-bin/ -p:UseAppHost=false -p:DebugType=None
dotnet /absolute/diagnostic-bin/nettest.dll \
  --binary-dir /absolute/frozen-client --source /absolute/recording.ppdemo \
  --paths /absolute/campaign/paths.txt --output /absolute/new-proof-directory \
  --start 800 --end 1050 --client-sha256 CAMPAIGN_CLIENT_SHA256 \
  --source-sha256 RECORDING_SHA256
```

Add `--require-a3-facts` for a current protocol56 recording. This requires a
positive grant packet denominator inside the visible selection, and rejects A2
recordings. `PACKET_POSITIVE_ONLY` is deliberately narrower than acceptance of
all three native powerup types, expiry/death/room transitions, or trusted
grant-to-shot damage. Join the final A3 recording's campaign receipt separately;
the diagnostic cannot establish natural contacts or authority provenance from
a filename/hash alone. It reports source and clip metadata, producer/client/tool
BuildIds, physical hashes/MVIDs, positive packet counts and restore/seek details.

The genuine A2 protocol55 eight-peer Fuel Sluice recording from source467d748f
qualifies compatibility only. Zero grant records mean `NOT_VERIFIED` for A3.
Its optional durable firing capsules have a different consumer policy and are
rejected with safe reconstruction fallback. A source memory checkpoint and an
explicit new private world capsule restore are exercised; this does not claim
that those historical durable capsules were accepted. Negative diagnostics print
an expected `replayclipcheck FAIL` before the overall diagnostic PASS.

Limits: this is gameplay/presentation hash fidelity without rendered pixels,
HUD screenshots, sound, native GPU lifetime, real accepted-shot damage, or an
independent natural-contact campaign. It covers Studio archive extraction, not
the live instant-clip `SaveFrozen` operation. A3 remains NOT VERIFIED until the
same final client and a genuine grant-positive A3 recording pass this diagnostic
and the independent A3 acceptance gates.
