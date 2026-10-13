# PNG callback ownership check

This standalone project links the production PNG helper and the same pinned
ReFuel.StbImage package. It does not reference the game, create GL windows, load
game assets or initialize audio. Run each mode in its own process.

```sh
dotnet run --project tools/png-callback-check -c Release -- candidate /tmp/png-candidate
DOTNET_TieredCompilation=0 dotnet run --project tools/png-callback-check -c Release -- candidate /tmp/png-optimized
```

The candidate writes eight deterministic RGB/RGBA images while a separate thread
forces collections during native compression. It decodes them from memory and
requires exact bytes, including alpha and bottom-up orientation. Four formats
and both global flip policies must also produce exactly the package encoder's
PNG bytes. The small package reference encodes inside a temporary no-GC region
so the known callback bug cannot invalidate the parity control. Stream failures
must retain exception identity and caller ownership, and a subsequent write must
work. The helper does not alter flip/filter/compression options.

`baseline` calls the unmodified package convenience writer under the same
pressure. **It intentionally reproduces a process-fatal collected native callback
on affected runtimes; run only as an isolated diagnostic child.** It is excluded
from CI. CI runs the candidate with optimized JIT on Linux, Windows, macOS ARM
and macOS Intel, exercising package-provided native asset resolution.

No forced collections or runtime compilation settings are added to production.
This fixture verifies PNG ownership and encoding, not rendering determinism,
gameplay, performance, physical Android encoding or driver memory boundedness.
