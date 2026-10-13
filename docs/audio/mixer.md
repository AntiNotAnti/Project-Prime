# Audio mixer and voice ownership

Audio → Volume exposes Music, Player, Weapons, Notifications, and Sound effects.
Game audio is the existing SfxVolume master for all non-music categories.
New category gains default to 100%, preserving existing settings. Combat
notification volume remains an additional per-feature gain.

AudioMixer owns category gains and sound-ID routes. The cartridge hunter and
beam tables register player and weapon routes when loaded; unregistered sounds
use SoundSource.Bus (SoundEffects by default). Player sources default to Player.
SoundSource.PlayFreeSfx discards spatial source ownership and uses the registered
ID route or SoundEffects. Announcer streams and custom combat cues use Notifications.
Music retains its existing independent native/custom streaming implementation.
New sounds can use AudioMixer.Register or a sourced bus; scripts and DGNs capture
their parent sound's route, keeping their component samples together.

Pool recycling must Stop the previous instance before assigning its next sound.
Every failed sample allocation must release partial ownership, including sample
references acquired before a channel was available. A fully referenced buffer
cache drops the pending request instead of stopping arbitrary active owners.
Temporary saturation can still drop a sound; it must not permanently leak voices.

Run: ~/.dotnet/dotnet run --project tools/audio-check
The check exercises saturated instance recycling, repeated channel exhaustion,
buffer exhaustion, category routing, independent gains, and compatible defaults
without requiring extracted game data or an audio device.

Long-session audible verification on a real audio device remains necessary to
confirm whether these ownership bugs fully explain the reported audio dropout.

## Native resource ownership

Each foreground SFX instance owns one device/context, 64 cached sample buffers,
one stream buffer, 128 physical sources, and one stream source. IDs are recorded
at acquisition, including partial batch allocation. A failed load releases its
acquisitions before installing the existing silent fallback. Null device/context
handles and failed context activation also take that fallback.

Shutdown stops voice ownership and deletes every source/buffer while the scene's
context is current, then detaches it. Cleanup rebinds its owner context if a
capability probe changed the current context. Desktop destruction/close runs on
one ordered worker lane. The next scene waits up to two seconds for the prior
close during loading, then uses the silent fallback if it is still pending.
It opens no overlapping device and queues no empty cleanup behind a stalled close.
Normal playback gains no waiting or worker allocation. Shutdown is idempotent and
continues native cleanup when music/feedback cleanup throws.

Android uses the same individual ID release because its context adapter does not
reclaim the shared mixer's dictionaries. Android device close completes on the
scene owner: it only stops mixer voices, so an old close cannot later stop a newly
loaded scene. The process-wide music output remains available for the next match.

`tools/audio-check` additionally links the actual Android mixer source and verifies
120 acquire/fill/release cycles, 120 desktop worker-close cycles, 120 headless
cycles, null handles, failed context activation, partial buffer/source allocation,
cleanup errors, repeated teardown, context/probe interference, retained fallback
cleanup completion, and blocked native destruction with a readiness deadline. These are
synthetic ownership checks, not Android hardware or native-memory measurements.
Real-device reload timing, audio playback and long-run process RSS still require
separate validation; this correction does not attribute prior macOS RSS growth.
