# Frequency Customizer

Retunes **everything your computer plays** (Spotify, YouTube, Apple Music, games…) from standard A = 440 Hz tuning to
**432 Hz**, **528 Hz**, **639 Hz**, or any custom frequency, live. Only the pitch changes; the speed stays the same.
Works on **Mac** and **Windows**.

## ⬇️ Download

Go to the **[Releases page](../../releases/latest)** and download:

| Computer | File | Needs |
|---|---|---|
| Mac | `FrequencyCustomizer-mac.zip` | macOS 14.2 Sonoma or newer (Apple Silicon or Intel). Nothing else to install. |
| Windows | `FrequencyCustomizer-windows.zip` | Windows 10/11, the free [VB-CABLE](https://vb-audio.com/Cable/) driver, and the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

## 🍎 Mac: install and play

1. Unzip `FrequencyCustomizer-mac.zip` and drag **Frequency Customizer** into your **Applications** folder.
2. Double-click it. Because the app isn't from the App Store, macOS shows *"Apple could not verify…"*. Click **Done**.
3. Open **System Settings › Privacy & Security**, scroll down to *"Frequency Customizer was blocked…"*, click
   **Open Anyway**, and confirm with your password or Touch ID. This is only needed the first time.
   *(On macOS 14 Sonoma you can instead right-click the app › **Open** › **Open**.)*
4. Click **432 Hz** (or 528 / 639 / Custom), then click **Start**.
5. When macOS asks whether Frequency Customizer may **capture system audio**, click **Allow**.
   If you missed that prompt, turn the app on under **System Settings › Privacy & Security › Screen & System Audio
   Recording**, then click Stop and Start again.

Play anything and you'll hear it retuned. **Bypass** switches back to the original sound instantly so you can compare.
The waveform icon in the menu bar lets you change tuning or stop retuning after you close the window.
When you click Stop or quit, the Mac's sound goes back to normal.

## 🪟 Windows: install and play

1. Install **VB-CABLE** (free) from https://vb-audio.com/Cable/. Run `VBCABLE_Setup_x64.exe` as administrator, then restart the PC.
2. Install the **.NET 8 Desktop Runtime** if Windows asks for it.
3. Unzip `FrequencyCustomizer-windows.zip` and run `FrequencyCustomizer.exe`.
4. Set **Play to** to your speakers or headphones, pick a preset, and click **Start**. The app sends Windows sound through the
   cable while it runs and switches back when you click Stop or close it.

## Tunings

| Preset | What it does | Shift |
|---|---|---|
| 440 Hz | Off (sound passes through unchanged) | 0 cents |
| 432 Hz | A4 440 → 432 Hz | −31.8 cents (98.18 %) |
| 528 Hz | C5 523.25 → 528 Hz (≈ A = 444) | +15.6 cents |
| 639 Hz | D#5 622.25 → 639 Hz (nearest note) | +46.0 cents |
| Custom | Any Hz value + reference note, or a ±100-cent slider | — |

Live pitch shifting adds roughly 50–120 ms of delay, so video lip-sync can be very slightly late.

## How it works

**Mac** (`mac/`, Swift / SwiftUI):
```
all apps ─► Core Audio process tap (originals muted) ─► AVAudioUnitTimePitch ─► your speakers
```
It uses the system-audio tap built into macOS 14.2+, so no driver is needed. The app excludes its own output from the tap so it never retunes itself.

**Windows** (`src/`, C# / WinForms):
```
all apps ─► CABLE Input ══(VB-CABLE)══► CABLE Output ─► SoundTouch pitch shift ─► your speakers
```

## Build it yourself

GitHub Actions (`.github/workflows/build.yml`) builds both apps on every push. Pushing a tag such as `v1.0.1`
publishes a new Release with both zips attached.

```bash
git tag v1.0.1
```

```bash
git push origin v1.0.1
```

Locally:
- Mac (needs Xcode or the Command Line Tools): `bash mac/scripts/make-app.sh`, then run `swift test` inside `mac/`
- Windows (needs the .NET 8 SDK): `dotnet test`, then `dotnet publish src/FrequencyCustomizer -c Release -o publish`

License notes: SoundTouch.Net is LGPL-2.1, and NAudio is MIT.
