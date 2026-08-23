# Audio

The WAVs live here but are not committed, since the source material isn't mine to redistribute.

Convert your own:

```powershell
ffmpeg -i "your-track.mp3" -vn -ac 1 -ar 48000 -c:a pcm_s16le blind_rage.wav
```

- `blind_rage.wav` is required.
- `blind_rage_bass.wav` is optional. If present, players get a "Bass" tick box to switch to it. If absent, the
  option is simply not offered.

Must be 48 kHz; mono or stereo, 16/24/32-bit PCM or 32-bit float. Set the `timeline` markers in the config to
match your track.

Leave some headroom. `AudioTransmitter` applies a fixed 1.414x gain before Opus encoding, so anything peaking
much above 0.7 will clip. `tests\run-tests.ps1` checks this and will tell you.
