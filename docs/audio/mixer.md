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
