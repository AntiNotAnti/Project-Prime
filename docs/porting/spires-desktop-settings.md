# PORT-09: Desktop local Echoes import entry point

On Windows, Linux and macOS, Settings > Profile > Additional local arenas now shows a user-facing **IMPORT ECHOES: SPIRES (PREVIEW)** action.

- Opens the native desktop picker for a user-owned GameCube ISO.
- Delegates ISO verification and the in-memory Spires compiler to a background task, without blocking UI or mutating the current match.
- Reports failures inline, re-enables the control and requires a restart to register the generated local-only map.
- Does not change default hunters, matchmaking, map rotation, Android UI, network protocol or replay decoding.
- The resulting map is only a collision-backed visual graybox; the real Echoes lighting, models, sound and interactable entities are not implemented.

Android game-source selection and RVZ/WIA/Wii support remain intentionally out of scope. A command-line `.gcm` import also works through PORT-08. This PR does not include any proprietary source assets.
