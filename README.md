# yasoundboard

A soundboard you control from your phone or any touch screen device.

The app plays sounds on the output device you choose.

Sounds are loaded once at startup and the connection to the device is kept open, so tapping a pad
plays straight away.

## Requirements

- **Windows**
- **.NET 11**
- **ffmpeg**, available on your PATH. It reads your audio files.
- **An output device to play to.** `dotnet run -- --list-devices` lists them.

## Setup

1. List your output devices:

   ```
   dotnet run -- --list-devices
   ```

2. Put part of the device name in `appsettings.local.json`. Create the file if it is not there yet,
   and copy this in:

   ```json
   {
     "YasoundBoard": {
       "Audio": {
         "DeviceNameMatch": "Speakers"
       }
     }
   }
   ```

   Any part of the name works, and capital letters do not matter. The app will not start until this
   is set, and the error lists the names it can see.

3. Put your audio files in `sounds/`. Create the folder if it is not there yet.

4. Start it, then open the address it prints on your phone:

   ```
   dotnet run
   ```

   The phone has to be on the same network as the PC.

## Settings

`appsettings.json` holds the defaults. Anything you set in `appsettings.local.json` is used instead.
Both files accept `//` comments, and lists are replaced rather than merged.

### Audio

| Setting | Default | Effect |
| --- | --- | --- |
| `DeviceNameMatch` | empty | Part of the name of the device to play to. Empty stops the app from starting. |
| `DeviceNameFallbacks` | empty | A list of other names to try, in order. |
| `SampleRate` | `48000` | The rate every sound is converted to. Has to match the device for low latency. |
| `Channels` | `2` | Stereo. |
| `LatencyMs` | `20` | How much audio is sent at a time. Lower reacts sooner, higher is safer on a slow machine. |
| `ShareMode` | `Shared` | `Shared` lets other programs use the device. `Exclusive` takes it over for lower latency. |
| `FallBackToShared` | `true` | Use shared mode when exclusive mode is refused. |
| `LowLatency` | `true` | Ask Windows for low latency playback. |
| `LowLatencyRequired` | `false` | Stop with an error instead of carrying on without low latency. |
| `UseEventSync` | `true` | Wait for the device instead of checking it over and over. |
| `MmcssThreadPriority` | `Pro Audio` | Windows priority for the audio thread. Empty turns it off. |
| `RawMode` | `false` | Skip the sound effects Windows normally adds. |
| `Category` | `Media` | What Windows thinks the audio is for. Avoid `Communications`, which quiets everything else. |
| `MaxVoices` | `64` | How many sounds can play at once. |
| `MasterGain` | `1.0` | Overall volume. `1.0` is unchanged, `2.0` is twice as loud. |
| `SoftClip` | `true` | Soften a loud mix instead of cutting it off. |
| `HeadroomSeconds` | `1` | Smallest the mixing buffer is allowed to be, in seconds. It already grows to fit `LatencyMs`. |

### Library

| Setting | Default | Effect |
| --- | --- | --- |
| `SoundsDirectory` | `sounds` | Where your audio files are. |
| `Recurse` | `true` | Look inside subfolders. |
| `Extensions` | 14 formats | Which file types count as sounds. |
| `FfmpegPath` | `ffmpeg` | Where ffmpeg is. |
| `MaxDecodeSeconds` | `600` | Stop reading a file after this long. `0` means no limit. |
| `ParallelDecodes` | `4` | How many files are converted at once. |
| `CacheDirectory` | `.cache/pcm` | Where the converted audio is kept. |
| `UseCache` | `true` | Reuse the converted audio so later starts are fast. |
| `PruneCache` | `false` | Delete converted audio that no longer matches a sound. |
| `MetadataFile` | `sounds.json` | File for renaming and recolouring sounds. |
| `DefaultGain` | `1.0` | Volume for sounds with no setting of their own. |
| `Sort` | `name` | Order of the pads: `name`, `order`, `path`, `duration` or `peak`. |

### Server

| Setting | Default | Effect |
| --- | --- | --- |
| `BindAddress` | `0.0.0.0` | Which network connections to accept. `0.0.0.0` accepts all of them. |
| `Port` | `8787` | The port in the address. |
| `AnnounceAddresses` | `true` | Print the addresses at startup. |
| `LogRequests` | `false` | Print every request the phone makes. |

### Ui

Sent to the phone when it connects, so changing one needs the app restarted, not the phone.

| Setting | Default | Effect |
| --- | --- | --- |
| `Title` | `yasoundboard` | Shown at the top of the page. |
| `DisplayMode` | `standalone` | How the page opens from the home screen. |
| `Theme` | `auto` | `auto`, `dark` or `light`. |
| `AccentColor` | `#e24b4a` | Colour of the level meter and the pad flash. |
| `Columns` | `0` | Pads per row. `0` decides from the screen size. |
| `ShowLabels` | `true` | Show the sound names on the pads. |
| `ShowLevels` | `true` | Show the level meter. |
| `ShowStopAll` | `true` | Show the stop all button. |
| `ShowMasterVolume` | `true` | Show the volume slider. |
| `ShowConnectionStatus` | `true` | Show whether the phone is connected. |
| `VibrateOnTap` | `true` | Vibrate when a pad is tapped. |
| `VibrateMs` | `15` | How long the vibration lasts. |
| `FlashOnTap` | `true` | Flash the pad when it is tapped. |
| `KeepAwake` | `true` | Keep the screen on while the page is open. |
| `StateUpdatesPerSecond` | `20` | How often the phone is updated. Lower saves battery. |
| `PadMinWidthPx` | `92` | Smallest a pad can get, which sets how many fit per row. |
| `PadGapPx` | `8` | Space between pads. |
| `PadRadiusPx` | `14` | How rounded the pads are. |

## Per sound overrides

Optional `sounds.json` inside the sounds folder. The key is the path to the file without its
extension, so `sounds/drums/kick.wav` is `drums/kick`.

```json
{
  "drums/kick": { "name": "Kick", "color": "#e24b4a", "gain": 1.2, "order": 1 },
  "airhorn":    { "order": 0 },
  "unused":     { "hidden": true }
}
```

| Field | Effect |
| --- | --- |
| `name` | Label on the pad instead of the file name. |
| `color` | Colour of the pad. |
| `gain` | Volume for that sound, applied before the overall volume. |
| `order` | Where it sorts when `Sort` is `order`. |
| `hidden` | Keep the sound loaded but do not show it. |

## Troubleshooting

**The phone will not connect.** Allow the app through the firewall on private networks. If the
firewall is fine, check the router for client isolation.

**`Audio.DeviceNameMatch is empty`.** Run `--list-devices` and set it in `appsettings.local.json`.

**`No output device matched`.** The name you set matched nothing. The same message lists every
device it can see.

**`No sounds found`.** The line above it says which folder was scanned.

**`Every sound failed to decode`.** ffmpeg is missing, or not where `FfmpegPath` points.

**Low latency requested but not granted.** The reason is printed. Usually `SampleRate` does not
match the device.

**Choppy audio.** Raise `LatencyMs` to 40, then 60. If that does not help, check whether another
program is holding the device.

**New sounds do not appear.** Restart the app.

## Commands

```
dotnet run                     start
dotnet run -- --list-devices   list output devices and exit
dotnet build                   build
```
