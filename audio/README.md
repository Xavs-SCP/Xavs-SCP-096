# Audio

- `blind_rage.wav` is required.
- `blind_rage_bass.wav` is optional. If present, players get a "Bass" tick box to switch to it. If absent, the
  option is simply not offered.

Both are 48 kHz mono 16-bit PCM, peak-normalised to 0.672 so that after the fixed 1.414x gain
`AudioTransmitter` applies before Opus encoding they land at 0.95, just under clipping.

To use a different track:

```powershell
ffmpeg -i "your-track.mp3" -vn -ac 1 -ar 48000 -c:a pcm_s16le blind_rage.wav
```

Must be 48 kHz; mono or stereo, 16/24/32-bit PCM or 32-bit float. Set the `timeline` markers in the config to
match, and leave headroom for that 1.414x gain. `tests\run-tests.ps1` checks both and will tell you if a section
would clip.
