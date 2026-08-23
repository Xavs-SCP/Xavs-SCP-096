# Xavs-SCP-096

SCP: Secret Laboratory only plays chase music to humans, so if you're playing SCP-096 you hear nothing while
everyone running away from you gets a soundtrack. This LabAPI plugin gives it back to you.

It doesn't modify, inject into, or read memory from the game client.

## Why it has to work this way

The decision is made client side, and the client refuses to play a chase theme for anyone who isn't human:

```csharp
// FacilitySoundtrack.ChaseThemes.ChaseThemeSoundtrack.UpdateVolume
bool flag = ReferenceHub.TryGetLocalHub(out hub) && hub.IsHuman();
if (flag)
    FindNewChases();

UpdateActiveThemes(!flag, out var totalDominance, out var highestPriority);  // forceDisable when not human
```

A server plugin can't change that, and the client is an IL2CPP build shipping an anti-cheat, so patching it
there isn't an option either.

Instead the plugin streams the track to SCP-096 down the speaker toy audio path. It spawns a non-spatial
`SpeakerToy` per SCP-096 and uses `AudioTransmitter.ValidPlayers` so the frames only ever reach that one player.
Everyone else keeps hearing the normal client-side theme.

## Sections

`blind_rage.wav` is cut at these markers, which are also the config defaults:

| Time | Section | Rage state | |
|---|---|---|---|
| `0:00` | Encounter / chase, non-target | `Docile` | loops |
| `0:48` | Enrage build-up, non-target | `Distressed` | once, then `0:54` |
| `0:54` | Chase, non-target | `Enraged` | loops |
| `1:50` | Calming down, non-target | `Calming` | once, then silence |
| `1:59` | Face seen / chase, target | `Docile` | loops |
| `2:38` | Enrage build-up, target | `Distressed` | once, then `2:44` |
| `2:44` | Chase, target | `Enraged` | loops |
| `3:40` | Calming down, target | `Calming` | once, then silence |

Each section runs until the next marker. `variant` picks which half you get, defaulting to `NonTarget` since
SCP-096 is never their own target.

A few details that matter:

- Music starts when SCP-096 gets their first target, not on spawning. Set `docile_requires_targets: false` to
  loop the docile section all round instead.
- `Distressed` lasts 6.1s in game (`Scp096RageCycleAbility.EnragingTime`) and the build-up section is 6.0s. Both
  clips are handed to the transmitter together, so the enraged loop starts on the exact sample the build-up ends
  on rather than waiting for the state to flip.
- The sections are cut from one continuous recording, so the calm-down doesn't resolve into silence on its own.
  It's faded out over its last couple of seconds instead.

## Installing

Put both files in your LabAPI plugins folder:

```
%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\global\
    Scp096ChaseMusic.dll
    blind_rage.wav
```

`build.ps1 -Install` does it for you. Start the server once and it writes
`%APPDATA%\SCP Secret Laboratory\LabAPI\configs\<port>\Scp096ChaseMusic\config.yml`.

You should see:

```
[INFO] [Scp096ChaseMusic] Loaded blind_rage.wav (NonTarget mix): Docile 48.00s, BuildUp 6.00s, Enraged 56.00s, Calming 9.00s.
```

## Player settings

Players get their own entries under Settings > Server-specific:

- **Chase music volume**, 0-300%. Per player.
- **Bass**, if a second mix is present. Swaps to it at the next section.

The game's own "Chase Themes" slider can't drive this, and it's worth knowing why: it isn't an audio mixer
group. The five mixer sliders are Master, VoiceChat, SoundEffects, MenuMusic and MenuUI, and Chase Themes is a
plain local `UserSetting<float>` that the chase theme system applies in code. It's never sent to the server, so
a plugin has no way to read it. The volume slider above is the stand-in.

## Audio

The WAVs aren't in the repo. Convert your own from whatever source you have:

```powershell
ffmpeg -i "your-track.mp3" -vn -ac 1 -ar 48000 -c:a pcm_s16le audio\blind_rage.wav
```

Must be 48 kHz. Mono or stereo, 16/24/32-bit PCM or 32-bit float. Set the eight `timeline` markers to match
whatever you use; they accept `m:ss`, `m:ss.fff`, `h:mm:ss` or plain seconds.

Worth leaving headroom: `AudioTransmitter` applies a fixed 1.414x gain before Opus encoding, so a track peaking
much above 0.7 will clip. The test suite checks this.

## Building

Needs a dedicated server install for the reference assemblies (`steamcmd +app_update 996560`) and Roslyn from
any Visual Studio or MSBuild install. No .NET SDK or NuGet restore.

```powershell
.\build.ps1 -ServerPath 'C:\path\to\sl-dedicated-server' -Install
```

Compiling against the server's own `mscorlib` and `Assembly-CSharp` keeps the output compatible with the Unity
Mono runtime the server actually runs.

## Tests

```powershell
.\tests\run-tests.ps1
```

Covers what doesn't need a running game: timestamp parsing, the sample offsets each section resolves to, that
real audio sits at those offsets, that nothing clips under the transmitter's gain, that the config survives a
YAML round trip, and that hostile volume values from a client are rejected.

## Notes

- Speaker toy audio rides the voice path, so the client's Sound Effects slider affects it. That's expected.
- One speaker controller id per live SCP-096, starting at `controller_id_base` (96). Move it if another audio
  plugin on your server claims that range.
- Only the configured variant is loaded, roughly 22 MB per mix.
- Tested against SCP:SL 14.2.7 / LabAPI 1.1.
