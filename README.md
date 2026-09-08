# Prime Hunters

<img src="src/Client/Assets/fruity-prime-intro.png" alt="Prime Hunters" width="100%">

**Metroid Prime Hunters on PC and Android.** Online matches for up to 8 players, widescreen, 60 FPS,
and a launcher that does the setting up for you.

A fork of [NoneGiven/MphRead](https://github.com/NoneGiven/MphRead).

> You bring your own Metroid Prime Hunters cartridge dump. No Nintendo game data ships here or is
> downloaded.

**[Download](https://github.com/liveteklol/Fruity-Prime/releases)** · [Support the project ☕](https://ko-fi.com/livetek)

## Features

- **Ultra Widescreen support**
- **High resolution**
- **Windows / Linux / Android port**
- **240+ FPS support**
- **Online multiplayer** (no WFC support)
- **Up to 8 players**
- **Dedicated Node + Worker servers**
- **Demo recording**
- **Custom maps**
- **12 multiplayer modes**: Battle, Survival, Capture, Bounty, Defender, Nodes, Prime Hunter, and teams
- **Keyboard & mouse**
- **Cel shading**
- **Modern HUD**
- **Auto Update check**

## Support

If you enjoy it: **[ko-fi.com/livetek](https://ko-fi.com/livetek)** ☕

<img width="500" height="300" alt="Prime Hunters" src="https://github.com/user-attachments/assets/ec6a2871-2b67-4de0-8b1a-ac6740c8d388" />

## Getting started

1. **[Download](https://github.com/liveteklol/Fruity-Prime/releases)** the package for your system
   and unzip it.
2. Run it:
   - **Windows** — double-click `FruityPrime.exe`
   - **Linux** — `./FruityPrime -launcher`
   - **macOS** — `xattr -dr com.apple.quarantine .` once, then the same as Linux
3. Click **Game files** and pick your `.nds`. It unpacks itself, once, with a progress bar.
4. Play.

`Escape` opens the menu, `F11` is fullscreen. Your name, hunter, controls and HUD are in
**Settings**, in the launcher or from that menu.

## Playing with other people

| | |
|---|---|
| **Join** | **Join**, sign in, choose a compatible public Node, then choose a listed public lobby |
| **Host** | **Host**, choose a public Node, create a public lobby, configure the match and start it |
| **Self-host** | Run the combined Node + Worker package, publish its HTTPS/WSS endpoint, and have players join its public lobby |

Everybody in a match needs the same version; the launcher checks for a new one and says so.

Online matches use a persistent Server Node for account-backed control, sessions, public lobbies and
match placement. A managed Worker owns each match's 60 Hz authoritative simulation and receives
gameplay UDP directly from clients; the Node connection remains the reliable control path. The client
cutover exposes public lobbies only. Private or unlisted local hosting has been retired, and there is
no direct Worker `--standalone` mode. Servers need extracted game data or a compact baked package;
see the [implementation and validation notes](docs/NETWORK_MODERNIZATION.md) and the
[Server Node guide](src/Server.Node/README.md).

## Custom maps

A map is one file: `something.fpmap`. Put it in the `maps` folder beside the game and it is in the
map list next time you open the launcher, picture and all. **de_dust2** comes with it.

## Not done yet

- **Gamepads**, on any platform.

## Command line

The `-room` and `-model` options still open the multiplayer room and model asset viewer. The old
local-gameplay path no longer accepts `-mode` and `-players`; use `-launcher` to join or host a
multiplayer match.

## Building

```bash
dotnet publish src/Client/Client.csproj -c Release \
  -r win-x64|linux-x64|osx-x64|osx-arm64 --self-contained true -p:PublishSingleFile=true
```

Needs [.NET 10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
`tools/package-server.sh --rid linux-x64 --output publish/server-linux-x64` packages the persistent
Server Node and its bundled Worker; use `win-x64` or `linux-arm64` for the other supported server
targets. Android is `dotnet build src/Android/Android.csproj` with the `android` workload. Every command
line option, and the test harness, are in [`CLAUDE.md`](CLAUDE.md).

Build the desktop solution with `dotnet build Game.sln`; Android remains a separate
workload build. The [project layout](docs/PROJECT_LAYOUT.md) describes the Game,
Client, Server Node, Server Worker, Tools and Android boundaries. Client output no longer
contains a local server executable; hosting is provided by the Node + Worker package.

## Credits

Prime Hunters (formerly Fruity Prime) is Livetek's fork of [MphRead](https://github.com/NoneGiven/MphRead) by **NoneGiven** —
the model viewer, the renderer, the format parsers and the recreation of the game itself are theirs.
That work is in turn built on **dsgraph**, [Chemical](https://gitlab.com/ch-mcl/metroid-prime-hunters-file-document),
[McKay42](https://github.com/McKay42), [Barubary](https://github.com/Barubary/dsdecmp),
[loveemu](https://github.com/loveemu/loveemu-lab), **Gericom**,
[CharlesVanEeckhout](https://github.com/CharlesVanEeckhout/actimagine),
[CyberBotX](https://github.com/CyberBotX/NCSF) and
[hackyourlife](https://github.com/hackyourlife/mph-viewer), with
[OpenTK](https://github.com/opentk/opentk), [OpenAL Soft](https://github.com/kcat/openal-soft) and
[SoundFlow](https://github.com/LSXPrime/SoundFlow) underneath. `FruityPrime -credits` prints the
list with what each one is for, and the Settings screen shows it too.

Metroid Prime Hunters is Nintendo's. No game data is included with this program: it comes from your
own cartridge dump.
