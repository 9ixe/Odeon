# LibVLC → libmpv Migration Prompt for Odeon

> **Repository Root:** `P:\LibMpvOdeon`
> **Projects:** `Odeon` (UWP/WinUI front-end), `Odeon.Core` (.NET Standard / UWP core logic)
> **Target:** Replace LibVLCSharp/LibVLC.UWP entirely with libmpv (`mpv-2.dll`) using Direct3D 11 rendering into a WinUI `SwapChainPanel`.

---

## Codebase Inventory — Every LibVLC Touchpoint

The following **24 files** reference `LibVLCSharp` or `LibVLC` types. Every single one must be touched during migration:

### Odeon Project (Front-End UWP)

| # | File | LibVLC Role | Migration Action |
|---|------|-------------|------------------|
| 1 | `Odeon/App.xaml.cs` | Calls `LibVLCSharp.Shared.Core.Initialize()` in `OnLaunched`; registers `IVlcDialogService` → `VlcDialogService` in DI | **Remove** `Core.Initialize()`, remove `IVlcDialogService` DI registration, replace with mpv init if needed |
| 2 | `Odeon/Controls/PlayerElement.xaml` | Contains `<vlcSharp:VideoView>` with `Initialized` event and `MediaPlayer="{x:Bind ViewModel.VlcPlayer}"` binding | **Replace** with a `<SwapChainPanel>` for mpv D3D11 rendering |
| 3 | `Odeon/Controls/PlayerElement.xaml.cs` | Uses `LibVLCSharp.Platforms.Windows.InitializedEventArgs` to get `SwapChainOptions`; handles `VlcVideoView_OnInitialized` | **Rewrite** to create D3D11 Device/SwapChain, get `ISwapChainPanelNative`, and pass to mpv render context |
| 4 | `Odeon/Services/VlcDialogService.cs` | Implements `IVlcDialogService` — handles VLC login/question/error/progress dialogs via `LibVLC.SetDialogHandlers()` | **DELETE entirely** — mpv does not use interactive dialog callbacks |
| 5 | `Odeon/Dialogs/VLCLoginDialog.xaml` | XAML UI for VLC network login prompt | **DELETE entirely** |
| 6 | `Odeon/Dialogs/VLCLoginDialog.xaml.cs` | Code-behind for VLC login dialog | **DELETE entirely** |

### Odeon.Core Project (Core Logic)

| # | File | LibVLC Role | Migration Action |
|---|------|-------------|------------------|
| 7 | `Odeon.Core/Playback/VlcMediaPlayer.cs` (562 lines) | The **central player implementation** — wraps `LibVLCSharp.Shared.MediaPlayer`, fires all IMediaPlayer events from VLC callbacks (`TimeChanged`, `Playing`, `EndReached`, `Buffering`, `ESAdded`, `ESSelected`, `ChapterChanged`, `LengthChanged`, `SeekableChanged`, `Muted/Unmuted`, `VolumeChanged`, `Paused`, `Stopped`), manages `CropGeometry`, `AudioDevice`, `SubtitleDelay`, `AudioDelay`, `PlaybackRate`, `NextFrame()`, `AddSlave()` | **Rewrite as `MpvMediaPlayer.cs`** — use `mpv_create()`, `mpv_set_property()`, `mpv_command()`, observe properties via `mpv_observe_property()` to fire equivalent events |
| 8 | `Odeon.Core/Playback/IMediaPlayer.cs` | Interface with 15 events + 20 properties/methods. Backend-agnostic except `AddSubtitle(IStorageFile)` | **Keep** mostly as-is. The interface is already clean |
| 9 | `Odeon.Core/Playback/PlaybackItem.cs` | Holds `LibVLCSharp.Shared.Media`, creates track lists from VLC `Media.Tracks` | **Rewrite** — remove `Media` property; track lists populated from mpv's `track-list` property instead |
| 10 | `Odeon.Core/Playback/MediaTrack.cs` | Base class — has constructor `MediaTrack(LibVLCSharp.Shared.MediaTrack track)` that reads `TrackType`, `Language`, `Id`, `Description` | **Rewrite VLC constructor** — replace with constructor from mpv track-list node data |
| 11 | `Odeon.Core/Playback/AudioTrack.cs` | Has `VlcTrackId` property; constructor from `LibVLCSharp.Shared.MediaTrack` | **Replace** `VlcTrackId` with mpv track ID (int64); update constructor |
| 12 | `Odeon.Core/Playback/VideoTrack.cs` | Has `VlcTrackId`; reads `Data.Video.Width/Height` from VLC MediaTrack | **Replace** with mpv track data (`demux-w`, `demux-h` from track-list) |
| 13 | `Odeon.Core/Playback/SubtitleTrack.cs` | Has `VlcSpu` property and `Codec` (FourCC); `IsAss` check via FourCC bytes | **Replace** `VlcSpu` with mpv track ID; `IsAss`/codec detection from mpv's `track-list/N/codec` string |
| 14 | `Odeon.Core/Playback/PlaybackAudioTrackList.cs` | Constructed from `LibVLCSharp.Shared.Media`; listens to `Media.ParsedChanged`; filters `TrackType.Audio` | **Rewrite** — populate from mpv `track-list` property (filter `type == "audio"`) |
| 15 | `Odeon.Core/Playback/PlaybackVideoTrackList.cs` | Same pattern as audio — `Media.ParsedChanged`, filter `TrackType.Video` | **Rewrite** — populate from mpv `track-list` (filter `type == "video"`) |
| 16 | `Odeon.Core/Playback/PlaybackSubtitleTrackList.cs` (719 lines) | **The most complex file.** MKV ASS extraction via `MkvSubtitleExtractor`, ASS style rewriting via `AssStyleRewriter`, lazy subtitle loading via VLC `AddSlave()`, `SetSpu()`, track pair management (native/overridden/bg variants), `SelectVlcSpu()`, font embedding | **Major simplification possible** — see Section below on subtitle strategy |
| 17 | `Odeon.Core/Playback/PlaybackChapterList.cs` | Uses `VlcPlayer.ChapterCount`, `VlcPlayer.TitleCount`, `VlcPlayer.FullChapterDescriptions()` | **Rewrite** — use mpv's `chapter-list` property (returns MPV_FORMAT_NODE array of `{title, time}`) |
| 18 | `Odeon.Core/Services/PlayerService.cs` | Creates `LibVLC` with options (`--freetype-font`, `--freetype-rel-fontsize`, `--no-osd`, `--sub-margin`, `--verbose`), creates `VlcMediaPlayer`, creates `Media` objects from `IStorageFile`/`Uri`/`string`, manages FutureAccessList `winrt://` tokens | **Rewrite as `MpvPlayerService.cs`** — create mpv handle with equivalent options (`sub-font`, `sub-font-size`, etc.), handle file paths directly (mpv supports native Win32 paths) |
| 19 | `Odeon.Core/Services/IPlayerService.cs` | Backend-agnostic interface | **Keep** — adjust `Initialize()` signature if SwapChainOptions are no longer needed |
| 20 | `Odeon.Core/Services/IVlcDialogService.cs` | Interface for `SetVlcDialogHandlers(LibVLC)` | **DELETE entirely** |
| 21 | `Odeon.Core/Services/LogService.cs` | Has `RegisterLibVlcLogging(LibVLC libVlc)` that subscribes to `libVlc.Log` | **Rewrite** — use mpv's `mpv_request_log_messages()` or `log-handler` |
| 22 | `Odeon.Core/Services/CastService.cs` | Creates `RendererWatcher(VlcMediaPlayer)`, calls `VlcPlayer.SetRenderer()` | **DELETE or stub** — mpv has no built-in casting/renderer discovery |
| 23 | `Odeon.Core/Helpers/RendererWatcher.cs` | Uses `RendererDiscoverer(LibVLC)` for Chromecast/DLNA discovery | **DELETE entirely** — mpv has no renderer discovery API |
| 24 | `Odeon.Core/Models/Renderer.cs` | Wraps `LibVLCSharp.Shared.RendererItem` | **DELETE entirely** |
| 25 | `Odeon.Core/Helpers/VlcMediaExtensions.cs` | Extension methods `ParseAsync()`, `WaitForParsed()`, `CheckParsed()` on `LibVLCSharp.Shared.Media` | **DELETE entirely** — mpv handles parsing internally when loading a file |
| 26 | `Odeon.Core/Factories/MediaViewModelFactory.cs` | Has `Create(Media media)` overload that uses `LibVLCSharp.Shared.Media.Mrl` and `MetadataType.Title` | **Remove** the `Media` overload or replace with URI-based creation |
| 27 | `Odeon.Core/Factories/MediaListFactory.cs` | Uses `Media.SubItems`, `Media.ParsedStatus`, `MediaParsedStatus.Done`, VLC media parsing for playlist expansion | **Rewrite** playlist expansion — mpv can parse playlists natively via `loadlist` or you parse M3U/M3U8 manually (already partially done) |
| 28 | `Odeon.Core/ViewModels/MediaViewModel.cs` | Uses `LibVLCSharp.Shared.Media` as a `Source` type, references `Media.ParsedStatus`, `Media.IsParsed`, `VLCState` | **Remove** all `Media`-typed paths; use file path/URI as the only source |
| 29 | `Odeon.Core/ViewModels/PlayerElementViewModel.cs` | Casts `IMediaPlayer` to `VlcMediaPlayer` for `VlcPlayer` property binding, catches `VLCException`, passes `--d3d11-upscale-mode` as VLC option | **Remove** VLC-specific casts; property binding should go through `IMediaPlayer` interface; pass upscale via mpv `scale` property |
| 30 | `Odeon.Core/ViewModels/PlayerPageViewModel.cs` | Casts to `VlcMediaPlayer` for external subtitle drop handling | **Replace** cast with `IMediaPlayer` interface method |

### Files to also modify but that DON'T directly reference LibVLC

| File | Action |
|------|--------|
| `Odeon.Core/Common/ServiceHelpers.cs` | Update DI: remove `IVlcDialogService` → `VlcDialogService`, keep `IPlayerService` → `MpvPlayerService` |
| `Odeon.Core/Helpers/SubtitleStyle.cs` | **Keep** — values are backend-agnostic. Update `ForceStyleOption` property to emit mpv `--sub-ass-force-style` format instead of VLC's |
| `Odeon.Core/Helpers/PrivateFontRegistration.cs` | **Keep** — still needed to register font with GDI so libass (used by mpv) can find it |
| `Odeon.Core/Helpers/MkvSubtitleExtractor.cs` (903 lines) | **Evaluate for removal** — see subtitle section below |
| `Odeon.Core/Helpers/AssStyleRewriter.cs` (900 lines) | **Evaluate for removal** — see subtitle section below |

---

## NuGet Package Changes

### Remove from `Odeon.Core.csproj`:
```xml
<PackageReference Include="LibVLCSharp" Version="3.7.0" />
```

### Remove from `Odeon.csproj`:
```xml
<PackageReference Include="LibVLC.UWP" Version="3.0.21-2505100334" />
```

### Add (or vendor manually):
- **mpv-2.dll** — Pre-built Windows x64 binary (from mpv/shinchiro builds or compile from source). Place in app package assets.
- **Vortice.Windows** (recommended) — For `IDXGISwapChain1`, `ID3D11Device` COM interop needed by mpv's D3D11 render API. Alternatively use raw `[DllImport]` / `[LibraryImport]`.

---

## Step-by-Step Migration Plan

### Phase 0: Create mpv P/Invoke Bindings [DONE]

Create `Odeon.Core/Interop/MpvInterop.cs` with the critical libmpv C API functions:

```
mpv_create, mpv_initialize, mpv_terminate_destroy,
mpv_set_option_string, mpv_set_property, mpv_set_property_string,
mpv_get_property, mpv_get_property_string,
mpv_command, mpv_command_async,
mpv_observe_property, mpv_wait_event, mpv_request_log_messages,
mpv_render_context_create, mpv_render_context_render,
mpv_render_context_report_update, mpv_render_context_free,
mpv_render_context_set_update_callback
```

Key constants: `MPV_RENDER_API_TYPE_D3D11`, `MPV_RENDER_PARAM_D3D11_INIT_PARAMS`, `MPV_RENDER_PARAM_D3D11_FBO`, `MPV_EVENT_*`, `MPV_FORMAT_*`.

### Phase 1: Delete Obsolete VLC-Only Files [DONE]

Delete these files completely — they have zero value in an mpv world:

```
Odeon/Services/VlcDialogService.cs
Odeon/Dialogs/VLCLoginDialog.xaml
Odeon/Dialogs/VLCLoginDialog.xaml.cs
Odeon.Core/Services/IVlcDialogService.cs
Odeon.Core/Helpers/RendererWatcher.cs
Odeon.Core/Models/Renderer.cs
Odeon.Core/Helpers/VlcMediaExtensions.cs
Odeon.Core/Services/CastService.cs       (or stub to no-op)
```

Remove corresponding entries from `.csproj` `<Compile>` items and DI registrations.

### Phase 2: Rewrite the Core Player — `MpvMediaPlayer.cs` [DONE]

Replace `VlcMediaPlayer.cs` (562 lines) with `MpvMediaPlayer.cs` implementing `IMediaPlayer`.

**VLC Event → mpv Property Mapping:**

| VLC Callback | mpv Observed Property | Notes |
|---|---|---|
| `TimeChanged` → `PositionChanged` | `time-pos` (double, seconds) | Convert to `TimeSpan` |
| `LengthChanged` → `NaturalDurationChanged` | `duration` (double, seconds) | |
| `Playing` → `PlaybackState = Playing` | `pause` (bool) = false + `core-idle` = false | |
| `Paused` → `PlaybackState = Paused` | `pause` (bool) = true | |
| `Stopped` → `PlaybackState = None` | `idle-active` (bool) = true | |
| `EndReached` → `MediaEnded` | `eof-reached` (bool) = true | |
| `Buffering` → `BufferingProgress` | `cache-buffering-state` (int, 0-100) | |
| `Opening` → `MediaOpened` | `file-loaded` event | |
| `EncounteredError` → `MediaFailed` | Check `mpv_event.error` field | |
| `Muted/Unmuted` → `IsMutedChanged` | `ao-mute` (bool) | |
| `VolumeChanged` → `VolumeChanged` | `ao-volume` (double, 0-100) | |
| `ChapterChanged` → `ChapterChanged` | `chapter` (int64) | |
| `SeekableChanged` → `CanSeekChanged` | `seekable` (bool) | |
| `ESAdded/ESSelected` | `track-list` (node) — observe once | No per-track events; track list updates atomically |
| `VlcPlayer.Size(0, ref px, ref py)` | `video-params/w`, `video-params/h` | |

**VLC Action → mpv Command Mapping:**

| VLC Call | mpv Equivalent |
|---|---|
| `VlcPlayer.Play(media)` | `mpv_command("loadfile", filePath)` |
| `VlcPlayer.Play()` | `mpv_set_property("pause", false)` |
| `VlcPlayer.Pause()` | `mpv_set_property("pause", true)` |
| `VlcPlayer.Stop()` | `mpv_command("stop")` |
| `VlcPlayer.Time = ms` | `mpv_set_property("time-pos", seconds)` |
| `VlcPlayer.Mute = val` | `mpv_set_property("ao-mute", val)` |
| `VlcPlayer.Volume = v` | `mpv_set_property("ao-volume", v)` (0–100 scale) |
| `VlcPlayer.Rate = r` | `mpv_set_property("speed", r)` |
| `VlcPlayer.NextFrame()` | `mpv_command("frame-step")` |
| `VlcPlayer.SetAudioTrack(id)` | `mpv_set_property("aid", id)` |
| `VlcPlayer.SetVideoTrack(id)` | `mpv_set_property("vid", id)` |
| `VlcPlayer.SetSpu(id)` | `mpv_set_property("sid", id)` |
| `VlcPlayer.AddSlave(Subtitle, mrl)` | `mpv_command("sub-add", path)` |
| `VlcPlayer.CropGeometry = "W:H"` | `mpv_set_property_string("video-aspect-override", "W:H")` or use `video-crop` |
| `VlcPlayer.SetOutputDevice(id)` | `mpv_set_property("audio-device", "wasapi/" + id)` |
| `VlcPlayer.SetRenderer(renderer)` | ❌ Not supported — remove casting feature |
| `VlcPlayer.AudioDelay = μs` | `mpv_set_property("audio-delay", seconds)` |
| `VlcPlayer.SpuDelay = μs` | `mpv_set_property("sub-delay", seconds)` |

**Threading Model:**
- mpv's `mpv_wait_event()` loop runs on a dedicated background thread
- Property changes fire on that thread → must `DispatcherQueue.TryEnqueue()` to UI thread
- mpv render callback (`mpv_render_context_set_update_callback`) fires from a render thread → use `mpv_render_context_render()` in the D3D11 present loop

### Phase 3: Rewrite the XAML Video Surface [DONE]

**Current:** `PlayerElement.xaml` uses `<vlcSharp:VideoView>` which provides SwapChainOptions on its `Initialized` event.

**New:** Replace with a `<SwapChainPanel x:Name="VideoSurface">`:

1. In code-behind, get `ISwapChainPanelNative` via COM interop
2. Create `ID3D11Device` + `IDXGISwapChain1` using `DXGI_SWAP_CHAIN_DESC1` targeting the panel
3. Call `ISwapChainPanelNative.SetSwapChain(swapChain)`
4. Pass the D3D11 device to `mpv_render_context_create()` with `MPV_RENDER_API_TYPE_D3D11`
5. On `mpv_render_context_set_update_callback`, call `mpv_render_context_render()` with the swap chain's back buffer as the FBO parameter
6. Present via `IDXGISwapChain1.Present1()`

**Reference implementations:**
- [Richasy/mpv-winui](https://github.com/Richasy/mpv-winui) — Direct mpv-WinUI integration
- [WangyuHello/HotPotPlayer](https://github.com/WangyuHello/HotPotPlayer) — Full WinUI 3 player with mpv D3D11

### Phase 4: Rewrite PlayerService [DONE]

**Current `PlayerService.cs`** does:
1. Font registration (`PrivateFontRegistration.EnsureRegisteredAsync()`) — **KEEP**
2. Creates `LibVLC` with `--freetype-font`, `--freetype-rel-fontsize`, etc. — **Replace with mpv properties**
3. Creates `VlcMediaPlayer(libVlc)` — **Replace with `mpv_create()` + `mpv_initialize()`**
4. Creates `Media` objects from `IStorageFile` using `winrt://` FutureAccessList tokens — **Simplify**: mpv accepts plain Win32 file paths; for `IStorageFile`, resolve `file.Path` directly. FutureAccessList tokens are LibVLC-specific (`winrt://` scheme)
5. Manages media disposal — **Simplify**: mpv doesn't have separate Media objects to dispose

**mpv initialization options (equivalent to current VLC options):**

```
--sub-font="Futura Cyrillic Medium"       ← replaces --freetype-font
--sub-font-size=28                        ← replaces --freetype-rel-fontsize=28
--sub-border-size=1                       ← replaces --freetype-outline-thickness=1
--sub-shadow-offset=0                     ← replaces --freetype-shadow-opacity=0
--sub-margin-y=36                         ← replaces --sub-margin=36
--osd-level=0                             ← replaces --no-osd
--vo=gpu-next                             ← modern GPU rendering
--gpu-api=d3d11                           ← Direct3D 11
--hwdec=auto-copy                         ← hardware decoding
```

### Phase 5: Simplify Track Lists [DONE]

**Current architecture:** Track lists are populated from `LibVLCSharp.Shared.Media.Tracks[]` (available after `ParsedChanged` event fires) and have fallback constructors from Windows `MediaPlaybackAudioTrackList`.

**New architecture with mpv:** Track info comes from mpv's `track-list` property, which is an MPV_FORMAT_NODE_ARRAY. Each entry has:
- `id` (int64) — the track ID for `aid`/`vid`/`sid` commands
- `type` (string) — `"audio"`, `"video"`, `"sub"`
- `title` (string, optional)
- `lang` (string, optional)
- `codec` (string) — e.g. `"ass"`, `"subrip"`, `"h264"`
- `demux-w`, `demux-h` (int64) — video dimensions
- `external` (bool) — whether this is an external file

**Migration per track list class:**
- `PlaybackAudioTrackList` → Populate from `track-list` entries where `type == "audio"`; no more `ParsedChanged` event
- `PlaybackVideoTrackList` → Same, filter `type == "video"`; read `demux-w`/`demux-h` for dimensions
- `PlaybackChapterList` → Read from mpv's `chapter-list` property (array of `{title, time}`)
- `PlaybackSubtitleTrackList` → See below

### Phase 6: Subtitle System — Critical Decision [DONE]

**Current subtitle pipeline (the most complex part of the codebase):**

1. `MkvSubtitleExtractor.cs` (903 lines) — Custom EBML parser that extracts ASS/SSA subtitle tracks from MKV container byte streams
2. `AssStyleRewriter.cs` (900 lines) — Rewrites ASS `[Styles]` section to force Futura PT Medium font, strips font override tags `{\\fn...}`, embeds font bytes as UU-encoded `[Fonts]` block, generates three variants per track: native, overridden-no-bg, overridden-with-bg
3. `PlaybackSubtitleTrackList.cs` (719 lines) — Orchestrates extraction, manages LazySubtitleTrack objects, uses VLC `AddSlave()` to inject rewritten ASS files, manages `SubtitleTrackPair` with three file variants, handles `RefreshOverrideState` to switch between native/styled on settings change

**Why this existed:** LibVLC's freetype renderer has limited ASS style override capability. VLC's `--sub-ass-force-style` only partially works. So Odeon extracts ASS from MKV containers, rewrites the style blocks in the file, embeds the font bytes, and adds the rewritten file as a subtitle slave — all to ensure Futura PT Medium is used consistently.

**With mpv, this can be dramatically simplified:**

mpv uses **libass** natively with much better `--sub-ass-force-style` support. The following mpv properties give you complete control:

```
--sub-font="Futura Cyrillic Medium"
--sub-font-size=50
--sub-border-size=1
--sub-shadow-offset=0
--sub-margin-y=36
--sub-ass-override=force        ← forces style overrides on ALL ASS/SSA tracks
--sub-ass-force-style=FontName=Futura Cyrillic Medium,FontSize=50,...
```

**With `--sub-ass-override=force`, mpv will:**
- Override all ASS styles in-place without file manipulation
- Apply your chosen font to all subtitle tracks
- Respect `--sub-ass-force-style` for granular control

**Decision point: Can you delete MkvSubtitleExtractor and AssStyleRewriter?**

**YES, if** mpv's `--sub-ass-override=force` + `--sub-ass-force-style` produces acceptable results for your use case. This would:
- Delete ~1,800 lines of complex extraction/rewriting code
- Eliminate the subtitle file caching in `LocalCacheFolder`
- Simplify `PlaybackSubtitleTrackList` from 719 lines to ~100 lines (just reading mpv's `track-list`)
- Remove the LazySubtitleTrack/SubtitleTrackPair complexity entirely

**KEEP MkvSubtitleExtractor + AssStyleRewriter only if** you need the UU-encoded font embedding for edge cases where mpv's `--sub-ass-force-style` with `--sub-fonts-dir` is insufficient. This is unlikely.

**Recommended: DELETE both files and rely on mpv's native ASS override.**

The `PrivateFontRegistration.cs` still copies the TTF to `LocalCacheFolder` — point mpv at that folder:
```
--sub-fonts-dir=<LocalCacheFolder path>
```

### Phase 7: Handle Media Creation Without LibVLC Media Objects [DONE]

**Current flow:** `PlayerService.CreatePlaybackItem()` creates a `LibVLCSharp.Shared.Media` object from `IStorageFile` (via `winrt://` token), `Uri`, or `string` path. The `Media` object is stored in `PlaybackItem` and used for:
- `media.Tracks` (track enumeration)
- `media.Duration`
- `media.ParsedStatus` / `media.IsParsed`
- `media.SubItems` (playlist expansion)
- `media.Mrl` (URI)
- `media.Meta(MetadataType.Title)` (metadata)

**With mpv:** There is no separate "Media" object. You just `mpv_command("loadfile", path)` and observe properties.

**New `PlaybackItem`:**
```csharp
public class PlaybackItem
{
    public object OriginalSource { get; }
    public string FilePath { get; }           // resolved Win32 path or URL
    public PlaybackAudioTrackList AudioTracks { get; }
    public PlaybackVideoTrackList VideoTracks { get; }
    public PlaybackSubtitleTrackList SubtitleTracks { get; }
    public PlaybackChapterList Chapters { get; }
    public TimeSpan StartTime { get; set; }
    // Duration comes from the player, not the item
}
```

Track lists are populated lazily when mpv fires `file-loaded` → read `track-list` property.

### Phase 8: Update MediaViewModel [DONE]

`MediaViewModel.cs` has a constructor `MediaViewModel(PlayerContext, IPlayerService, Media media)` that accepts a VLC `Media` object. This is used by `MediaViewModelFactory.Create(Media)` for playlist sub-items.

**Migration:** Remove the `Media`-typed constructor. Playlist items should be created via URI. The `Media.Mrl` → `Uri` conversion is already done in `MediaViewModelFactory.Create(Media)`.

Also remove:
- `Source is Media` checks in `CreatePlaybackItem()` and `CleanUpItem()`
- `Media.ParsedStatus` references in `UpdateMediaInfo()`

### Phase 9: Update Playlist Expansion (MediaListFactory) [DONE]

**Current:** `MediaListFactory.ParseSubMediaRecursiveAsync()` uses VLC `Media.Parse()` and `Media.SubItems` to recursively expand playlists.

**With mpv:** Two options:
1. **mpv native:** Use `mpv_command("loadlist", url)` — mpv handles playlist parsing internally
2. **Keep manual M3U parsing:** Already implemented in `MediaListFactory.ParseM3uAsync()` — extend to cover all playlist formats

**Recommended:** Keep M3U parsing (already done), remove VLC `Media.SubItems` recursion, and for non-M3U playlists let mpv handle them.

### Phase 10: Update DI Registration [DONE]

In `Odeon/App.xaml.cs` `ConfigureServices()`:

```diff
- services.AddSingleton<IVlcDialogService, VlcDialogService>();
```

In `Odeon.Core/Common/ServiceHelpers.cs` `PopulateCoreServices()`:

```diff
  services.AddSingleton<IPlayerService, PlayerService>();
+ // PlayerService is already rewritten to use mpv internally
- services.AddSingleton<ICastService, CastService>();
+ // Cast feature removed — mpv has no renderer discovery
```

In `App.xaml.cs` `OnLaunched()`:

```diff
- LibVLCSharp.Shared.Core.Initialize();
+ // mpv initialization happens in PlayerService.Initialize()
```

### Phase 11: Font Integration via mpv [DONE]

**Current font pipeline:**
1. `PrivateFontRegistration.cs` copies TTF from app package to `LocalCacheFolder`, registers with `AddFontResourceExW(FR_PRIVATE)`
2. `PlayerService` passes `--freetype-font=<path>` to LibVLC
3. `AssStyleRewriter` embeds font bytes as UU-encoded `[Fonts]` block in rewritten ASS files

**New font pipeline:**
1. `PrivateFontRegistration.cs` — **KEEP AS-IS**. `AddFontResourceExW(FR_PRIVATE)` makes the font visible to libass (which mpv uses)
2. In `MpvPlayerService.Initialize()`, set:
   ```
   mpv_set_option_string(ctx, "sub-font", "Futura Cyrillic Medium");
   mpv_set_option_string(ctx, "sub-fonts-dir", localCacheFolderPath);
   ```
3. **DELETE** font embedding in `AssStyleRewriter` — mpv's libass will find the font via GDI registration + `--sub-fonts-dir`

### Phase 12: Settings Mapping [DONE]

| Setting | Current (VLC) | New (mpv) |
|---------|---------------|-----------|
| `GlobalArguments` | Passed as LibVLC options (`--key=value`) | Pass via `mpv_set_option_string()` — mpv uses same `--key=value` syntax |
| `VideoUpscale` | `--d3d11-upscale-mode=<value>` (VLC-specific) | `mpv_set_property("scale", "bilinear"/"lanczos"/"ewa_lanczos")` |
| `OverrideSubtitleStyles` | Controls MKV extraction → native vs overridden file | `mpv_set_property("sub-ass-override", "force"/"no")` — live toggle without re-extraction |
| `SubtitleBackgroundEnabled` | Controls which ASS variant file is used | Use mpv's `--sub-back-color` property |

---

## Files to DELETE (Complete List)

```
Odeon/Services/VlcDialogService.cs
Odeon/Dialogs/VLCLoginDialog.xaml
Odeon/Dialogs/VLCLoginDialog.xaml.cs
Odeon.Core/Services/IVlcDialogService.cs
Odeon.Core/Helpers/RendererWatcher.cs
Odeon.Core/Models/Renderer.cs
Odeon.Core/Helpers/VlcMediaExtensions.cs
Odeon.Core/Services/CastService.cs           ← or stub to no-op
Odeon.Core/Helpers/MkvSubtitleExtractor.cs   ← if using mpv's native --sub-ass-override
Odeon.Core/Helpers/AssStyleRewriter.cs       ← if using mpv's native --sub-ass-override
Odeon.Core/Playback/VlcMediaPlayer.cs        ← replaced by MpvMediaPlayer.cs
```

## Files to CREATE

```
Odeon.Core/Interop/MpvInterop.cs             ← P/Invoke bindings for libmpv C API
Odeon.Core/Interop/MpvRenderInterop.cs       ← P/Invoke for mpv_render_* functions
Odeon.Core/Playback/MpvMediaPlayer.cs        ← IMediaPlayer implementation
Odeon.Core/Playback/MpvEventLoop.cs          ← Background thread mpv_wait_event() loop
Odeon.Core/Rendering/D3D11SwapChainManager.cs ← SwapChainPanel ↔ D3D11 ↔ mpv render
```

## Files to HEAVILY MODIFY

```
Odeon/Controls/PlayerElement.xaml             ← Replace <vlcSharp:VideoView> with <SwapChainPanel>
Odeon/Controls/PlayerElement.xaml.cs          ← D3D11 initialization instead of VLC VideoView
Odeon/App.xaml.cs                             ← Remove Core.Initialize(), update DI
Odeon.Core/Common/ServiceHelpers.cs           ← Update DI registrations
Odeon.Core/Services/PlayerService.cs          ← Full rewrite for mpv
Odeon.Core/Services/LogService.cs             ← mpv log handler
Odeon.Core/Playback/PlaybackItem.cs           ← Remove Media property
Odeon.Core/Playback/MediaTrack.cs             ← New constructor from mpv track data
Odeon.Core/Playback/AudioTrack.cs             ← Replace VlcTrackId
Odeon.Core/Playback/VideoTrack.cs             ← Replace VlcTrackId
Odeon.Core/Playback/SubtitleTrack.cs          ← Replace VlcSpu, new codec detection
Odeon.Core/Playback/PlaybackAudioTrackList.cs ← Populate from mpv track-list
Odeon.Core/Playback/PlaybackVideoTrackList.cs ← Populate from mpv track-list
Odeon.Core/Playback/PlaybackSubtitleTrackList.cs ← Major simplification
Odeon.Core/Playback/PlaybackChapterList.cs    ← Use mpv chapter-list
Odeon.Core/ViewModels/PlayerElementViewModel.cs ← Remove VLC casts
Odeon.Core/ViewModels/PlayerPageViewModel.cs   ← Remove VlcMediaPlayer cast
Odeon.Core/ViewModels/MediaViewModel.cs        ← Remove Media-typed paths
Odeon.Core/Factories/MediaViewModelFactory.cs  ← Remove Create(Media) overload
Odeon.Core/Factories/MediaListFactory.cs       ← Remove VLC media parsing
Odeon.Core/Helpers/SubtitleStyle.cs             ← Update ForceStyleOption for mpv syntax
```

---

## Verification Checklist

After migration, verify:

- [ ] Video renders correctly in the `SwapChainPanel` (no black frames, no airspace issues)
- [ ] Play/Pause/Stop/Seek all work
- [ ] Volume and mute work
- [ ] Playback rate change works
- [ ] Frame stepping (forward/backward) works
- [ ] Audio/Video/Subtitle track switching works
- [ ] Chapter navigation works
- [ ] External subtitle file loading works
- [ ] Subtitle font renders as Futura PT Medium
- [ ] `--sub-ass-override=force` correctly overrides ASS styles
- [ ] Aspect ratio / crop geometry works
- [ ] Audio device switching works
- [ ] Loop mode works
- [ ] Playlist (M3U/M3U8) expansion works
- [ ] Network streaming (HTTP URLs) works
- [ ] System Media Transport Controls (play/pause from keyboard/taskbar) still work
- [ ] Display request (prevent sleep during video playback) still works
- [ ] DPI/scaling changes don't break rendering
- [ ] Window resize re-creates swap chain correctly
- [ ] App terminates cleanly (mpv context destroyed, D3D11 resources released)

---

## mpv Binary Distribution

Ship `mpv-2.dll` (and its dependencies if any) alongside the app:

1. Download from [mpv shinchiro builds](https://github.com/shinchiro/mpv-winbuild-cmake/releases) — get the `dev` package which includes `mpv-2.dll` and the C headers
2. Place `mpv-2.dll` in the app's output directory (configure `.csproj` to copy to output)
3. For UWP packaging: add as `<Content Include="mpv-2.dll" />` with `CopyToOutputDirectory=PreserveNewest`
