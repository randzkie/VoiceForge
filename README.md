# VoiceForge

A **Voicemod-style real-time voice changer + soundboard** for Windows, built as a
native **.NET Framework 4.7 WinForms** desktop application with **NAudio**.

Dark, modern UI with a custom borderless window, sidebar navigation, voice cards,
a soundboard with global hotkeys, a recorder that captures your processed voice,
dark/light themes and JSON settings persistence.

![style](https://img.shields.io/badge/UI-Voicemod--style%20dark-00E5C0)

---

## Features

| Area | What you get |
|---|---|
| **Voice Changer** | 13 preset voices + a Custom "Voice Lab" with 6 live sliders |
| **AI Voices (RVC)** | Real neural voice conversion (RVC v2 .onnx models) via ONNX Runtime - fully local, no Python |
| **Real-time engine** | Mic → DSP chain → output at 48 kHz mono, ~70 ms latency |
| **Soundboard** | Load WAV/MP3 files, click to fire, multiple simultaneous sounds |
| **Global hotkeys** | `Ctrl+Alt+1 … Ctrl+Alt+9` fire the first 9 soundboard slots system-wide |
| **Recorder** | One-click WAV recording of exactly what the engine outputs |
| **Virtual mic** | Output to VB-CABLE and your voice becomes a microphone in Discord/games |
| **Settings** | Remembered voice, devices, volume, theme, soundboard list (`settings.json`) |
| **Themes** | Voicemod-style dark navy + light theme toggle |

### The 13 voices

| Voice | Recipe |
|---|---|
| **Helium** | Pitch +6 st |
| **Chipmunk** | Pitch +8.5 st |
| **Girl** | Pitch +4 st + high-pass 130 Hz (cuts chest rumble) + soft air reverb |
| **Deep Vader** | Pitch −4.5 st + room reverb |
| **Robot** | Ring modulation @ 45 Hz + soft distortion |
| **Alien** | Pitch +3.5 st + ring modulation @ 118 Hz |
| **Ghost** | Pitch −2 st + flanger + lush reverb |
| **Cave** | Long reverb + feedback echo |
| **Echo** | 320 ms slap-back delay |
| **Radio** | Band-pass 1.7 kHz + AM distortion |
| **Telephone** | Narrow band-pass + grit |
| **Megaphone** | Band-pass + heavy drive + boost |
| **Underwater** | Low-pass 550 Hz + slow flanger wobble |
| **Custom** | Pitch / reverb mix / decay / echo delay / echo mix / distortion sliders |

All DSP (pitch shifter, Freeverb-style reverb, echo, ring modulator, flanger,
waveshaper distortion, RBJ biquad filters) is implemented **from scratch in pure C#**
in `Audio/Dsp.cs` — no native DSP dependencies.

---

## AI voices (RVC) — real neural voice conversion

The **AI Voices** page runs [RVC](https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI)
v2 voice models entirely on your machine through **Microsoft.ML.OnnxRuntime** (CPU).
It converts your voice into a trained target voice — a different speaker, not just a
pitched-up version of you. Models are *not* bundled (they are big and licensed per
creator); you drop them into a folder and VoiceForge finds them:

```
%AppData%\VoiceForge\models\
    hubert.onnx          <- REQUIRED feature extractor, shared by all voices
    MyVoice.onnx         <- an RVC v2 generator exported to ONNX
    MyVoice.json         <- optional sidecar (created automatically from the template)
```

The in-app button **OPEN MODELS FOLDER** creates/opens that folder, and
`src/VoiceForge/models/README-AI-MODELS.txt` (also copied to `bin\models\`) walks
through getting `hubert.onnx` and converting `.pth` voices to `.onnx`.

**Where to download models (verified live links):** the guide above starts with a
"QUICK DOWNLOAD LINKS" section. Fastest setup — grab a ready ONNX voice *and* the
matching feature extractor from <https://huggingface.co/ozada/onnx_rvc> (e.g.
`woman_1.onnx` + `vec-256-layer-9.onnx`), rename the extractor to `hubert.onnx`,
drop both into the models folder, done. Free `.pth` voices (Amitaro, Kikoto
Kurage/Mahiro, Tokina Shigure, …) live at <https://huggingface.co/wok000/vcclient_model>;
official checkpoints are at <https://huggingface.co/lj1995/VoiceConversionWebUI>.

**How to use:** open a voice model on the AI Voices page → flip **AI voice conversion**
on → talk. Tuning: *Pitch transpose* shifts the target voice's pitch (−12…+12 st),
*Chunk size* trades stability for delay, *AI output gain* compensates loudness.

**Latency:** AI mode processes 0.5 s chunks (recommended setting), so expect roughly
**0.5–1 s extra delay** on CPU while it is active — this is physics, not a bug. DSP
presets keep their ~70 ms latency and can still be stacked on top of the AI voice.

**How it works inside** (`AI/` folder, pure C#): mic audio is resampled 48 kHz → 16 kHz
→ HuBERT content features → YIN pitch tracking → RVC generator → resampled back to
48 kHz. Inference runs on a worker thread behind lock-free FIFOs, so a slow CPU
degrades into a little extra silence instead of stuttering the whole engine.

**Want GPU speed?** Swap the NuGet package `Microsoft.ML.OnnxRuntime` for
`Microsoft.ML.OnnxRuntime.Gpu` (needs CUDA + cuDNN installed) — no code changes.

---

## Requirements

- **Windows 7 SP1 or newer** (Windows 10/11 recommended)
- **.NET Framework 4.7** runtime (preinstalled on Windows 10 1709+; the app prompts Windows Update to offer it if missing)
- **Visual Studio 2017 or newer** with the **“.NET desktop development”** workload (to build)
- Internet access on first build so NuGet can restore **NAudio 1.10.0** and **Microsoft.ML.OnnxRuntime 1.16.3**

## Build (Visual Studio)

1. Open `VoiceForge.sln`.
2. NuGet packages restore automatically on first build (NAudio).
   - If your VS asks, accept the restore prompt. CLI equivalent: `nuget restore VoiceForge.sln` or `msbuild /t:Restore`.
3. Press **F5** (Debug) or **Ctrl+Shift+B** then run `bin\Debug\VoiceForge.exe`.

## Build (command line)

```bat
:: Developer Command Prompt for VS 2017/2019/2022
msbuild VoiceForge.sln /t:Restore /p:Configuration=Release
msbuild VoiceForge.sln /p:Configuration=Release
:: output: src\VoiceForge\bin\Release\VoiceForge.exe
```

---

## Use your changed voice as a microphone (like Voicemod)

VoiceForge outputs the processed voice to **any** playback device. To make
Discord, games, OBS, etc. see it as a microphone, use a virtual audio cable:

1. Download **VB-CABLE** (free donationware): <https://www.vb-audio.com/Cable/>
2. Unzip → right-click `VBCABLE_Setup_x64.exe` → **Run as administrator** → *Install Driver*.
3. **Reboot** (recommended) — a new playback device **“CABLE Input”** and recording device **“CABLE Output”** appear.
4. In VoiceForge → *Voice Changer* → **Output** → select **CABLE Input (VB-Audio Virtual Cable)**.
   (The app auto-detects VB-CABLE and tells you on the status line.)
5. In Discord → *Voice & Video* → **Input device** → **CABLE Output**.
6. Power the engine ON and talk — everyone hears your changed voice.

> Note: while output goes to VB-CABLE you will not hear yourself (that is the point —
> it goes to the virtual mic). Switch Output back to your speakers anytime to monitor.

### Latency

Settings → *Latency*: **40 ms** (fast PCs), **70 ms** (default, recommended), **120 ms** (older hardware).
The pitch shifter adds ~80 ms of its own latency (inherent to real-time pitch shifting).

## Hotkeys

While VoiceForge is running (even in the background):

- `Ctrl+Alt+1` … `Ctrl+Alt+9` → play soundboard slot 1–9
- Disable/enable them in Settings. They re-register automatically when you add/remove sounds.

## Where files live

| What | Path |
|---|---|
| Settings | `%AppData%\VoiceForge\settings.json` |
| Recordings | `%AppData%\VoiceForge\Recordings\*.wav` |

Delete `settings.json` (or use Settings → *Reset settings*) to start fresh.

---

## Project structure

```
VoiceForge.sln
src/VoiceForge/
├── App/Program.cs                 Entry point + global exception handlers
├── Audio/
│   ├── Dsp.cs                     Pure-C# DSP: pitch shifter, reverb, echo,
│   │                              ring mod, flanger, distortion, biquad filters
│   ├── VoiceEngine.cs             Capture → effects chain → output + recording tap
│   └── SoundboardPlayer.cs        Multi-clip player with auto cleanup
├── Models/
│   ├── VoicePreset.cs             Effect-chain data + the 13-voice catalog
│   └── AppSettings.cs             Persisted settings model
├── Services/
│   ├── SettingsService.cs         JSON load/save (%AppData%)
│   ├── HotkeyService.cs           RegisterHotKey wrapper (Ctrl+Alt+1..9)
│   └── ThemeService.cs            Dark/light palettes
└── UI/
    ├── ChromeForm.cs              Borderless window: drag, resize, custom title bar
    ├── MainForm.cs                Navigation + all wiring
    ├── Controls/                  VoiceCard, SoundButton, ModernSlider,
    │                              ToggleSwitch, SidebarButton, PillButton
    └── Pages/                     VoiceChanger, Soundboard, Recorder, Settings
```

## How the audio pipeline works

```
Microphone (WaveInEvent, 48 kHz mono 16-bit)
   → BufferedWaveProvider
   → PCM16 → float sample provider
   → DSP chain (ring mod → pitch → filter → flanger → drive → reverb → echo → gain)
   → Volume
   → Recording tap (writes WAV while recording)
   → float → PCM16
   → Output device (WaveOutEvent: your speakers or VB-CABLE)
```

Presets are pure data (`VoicePreset`); switching voices mid-stream rebuilds the
effect chain click-free on the next audio buffer.

## Troubleshooting

| Symptom | Fix |
|---|---|
| No sound at all | Check Output device selection; try Output = your speakers first |
| Flat voice / no effect | Effects only apply while the engine is ON and a voice card is selected |
| “Microphone capture stopped” on Windows 10/11 | Settings → Privacy → Microphone → allow desktop apps |
| Robot/Ghost sounds too quiet | They intentionally color the voice; raise Output volume slider |
| Echo preset never decays | Lower Echo mix (feedback is capped at 0.9 for safety) |
| Sounds stutter on soundboard | Increase latency to 120 ms; close heavy browser tabs |
| VB-Cable not listed | Reboot after installing the driver; check it appears in Windows Sound settings |
| Device names look truncated | Windows WMME caps device names at 31 chars — the app matches by prefix, it is harmless |

## Credits & licenses

- **NAudio** (NuGet) — Mark Heath & contributors, Microsoft Public License (Ms-PL)
- **VB-CABLE** — Vincent Burel / VB-Audio (free donationware, not bundled)
- VoiceForge source code — provided as-is for learning and tinkering.

*VoiceForge is an independent hobby project inspired by Voicemod's UX; it is not affiliated with or endorsed by Voicemod.*
