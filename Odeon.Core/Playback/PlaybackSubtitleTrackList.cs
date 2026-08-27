#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Windows.ApplicationModel.Core;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI.Core;

namespace Odeon.Core.Playback
{
    public sealed class PlaybackSubtitleTrackList : SingleSelectTrackList<SubtitleTrack>
    {
        private readonly Media _media;
        private readonly PlaybackItem _item;
        private readonly List<LazySubtitleTrack> _pendingSubtitleTracks;
        private VlcMediaPlayer? _player;
        private SubtitleTrack? _expectedExternalTrack;

        private class LazySubtitleTrack
        {
            public SubtitleTrack Track { get; }
            public StorageFile File { get; set; }
            public VlcMediaPlayer Player { get; }

            // VLC SPU id of the native MKV ASS track this rewritten slave replaces (-1 if external).
            public int NativeSpu { get; set; } = -1;

            public LazySubtitleTrack(VlcMediaPlayer player, StorageFile file, Odeon.Core.Helpers.AssTrack? assTrack)
            {
                Player = player;
                File = file;

                string label = file.Name;
                if (assTrack != null)
                {
                    if (!string.IsNullOrEmpty(assTrack.Name))
                        label = assTrack.Name;
                    else if (!string.IsNullOrEmpty(assTrack.Language))
                        label = $"Subtitle ({assTrack.Language})";
                    else
                        label = $"Track {assTrack.TrackNumber}";
                }

                Track = new SubtitleTrack
                {
                    Id = "-1",
                    VlcSpu = -1,
                    Label = label,
                };
            }
        }

        private int _delaySpu = -1;

        public PlaybackSubtitleTrackList(Media media, PlaybackItem item)
        {
            _pendingSubtitleTracks = new List<LazySubtitleTrack>();
            _media = media;
            _item = item;

            if (_media.Tracks.Length > 0)
            {
                AddVlcMediaTracks(_media.Tracks);
            }
            else
            {
                _media.ParsedChanged += Media_ParsedChanged;
            }

            SelectedIndexChanged += OnSelectedIndexChanged;
        }

        private System.Threading.CancellationTokenSource? _extractionCts;
        private static StorageFolder? _cachedSubtitleFolder;



        private static byte[]? _cachedSubtitleFontBytes;
        private static readonly System.Threading.SemaphoreSlim _fontLoadLock = new System.Threading.SemaphoreSlim(1, 1);
        // Bounds how many external subtitle rewrite tasks may run concurrently to prevent
        // thread-pool starvation and VLC dispatcher queue back-pressure.
        private static readonly System.Threading.SemaphoreSlim _externalSubSemaphore = new System.Threading.SemaphoreSlim(2, 2);

        /// <summary>
        /// Reads the "Override subtitle styles" setting directly from the local settings store.
        /// This avoids threading an ISettingsService dependency through the constructor chain.
        /// </summary>
        private static bool IsOverrideSubtitleStylesEnabled()
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            return values.TryGetValue("Player/OverrideSubtitleStyles", out object val) && val is bool b ? b : true;
        }

        private static bool IsSubtitleBackgroundEnabled()
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            return values.TryGetValue("Player/SubtitleBackgroundEnabled", out object val) && val is bool b ? b : false;
        }

        internal static async Task<byte[]?> GetSubtitleFontBytesAsync()
        {
            if (_cachedSubtitleFontBytes != null)
                return _cachedSubtitleFontBytes;

            await _fontLoadLock.WaitAsync();
            try
            {
                if (_cachedSubtitleFontBytes != null)
                    return _cachedSubtitleFontBytes;

                // Resolve the app-package URI once, then read via System.IO for minimal overhead.
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(Odeon.Core.Helpers.SubtitleStyle.FontAssetUri));
                _cachedSubtitleFontBytes = await System.IO.File.ReadAllBytesAsync(file.Path);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"Failed to load font bytes: {ex.Message}");
#endif
            }
            finally
            {
                _fontLoadLock.Release();
            }

            return _cachedSubtitleFontBytes;
        }

        private volatile bool _extractionRunning;

        internal void SetMediaPlayer(VlcMediaPlayer player)
        {
            _player = player;
            if (_delaySpu >= 0)
            {
                SelectVlcSpu(_delaySpu);
            }
            if (!_extractionRunning)
            {
                StartExtraction();
            }
        }

        private void StartExtraction()
        {
            if (_extractionRunning) return;
            _extractionRunning = true;
            _extractionCts?.Cancel();
            _extractionCts?.Dispose();
            _extractionCts = new System.Threading.CancellationTokenSource();
            _ = RunExtractionAsync(_extractionCts.Token);
        }

        private async Task RunExtractionAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                await TriggerMkvExtractionAsync(ct);
            }
            finally
            {
                _extractionRunning = false;
            }
        }

        private void ResetExtractionCts()
        {
            _extractionCts?.Cancel();
            _extractionCts?.Dispose();
            _extractionCts = new System.Threading.CancellationTokenSource();
        }

        private async Task TriggerMkvExtractionAsync(System.Threading.CancellationToken ct)
        {
            // Always extract all three ASS variants (native + styled-no-bg + styled-with-bg)
            // regardless of the current OverrideSubtitleStyles setting.  AddTrackPair reads
            // the live setting to choose the active variant, and RefreshOverrideState can then
            // switch between them without needing to re-extract.
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[SubtitleExtraction] TriggerMkvExtractionAsync called. Player is null: {_player == null}");
#endif
            if (_player == null) {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[SubtitleExtraction] Aborted: player is null.");
#endif
                return;
            }
            try
            {
                StorageFile? file = null;

                if (_item.OriginalSource is StorageFile sf)
                {
                    file = sf;
                }
                else if (_item.OriginalSource is string str)
                {
                    try { file = await StorageFile.GetFileFromPathAsync(str); } catch { }
                }

                if (file == null && _media.Mrl != null && _media.Mrl.StartsWith("winrt://", StringComparison.OrdinalIgnoreCase))
                {
                    string token = _media.Mrl.Substring(8);
                    try
                    {
                        file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(token);
                    }
                    catch { }
                }

                if (file == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SubtitleExtraction] Failed to resolve StorageFile from OriginalSource '{_item.OriginalSource?.GetType()?.Name}' or Mrl '{_media.Mrl}'.");
                    return;
                }



                if (file.FileType.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
                {
                    var props = await file.GetBasicPropertiesAsync();
                    string fileHash = ComputeDeterministicHash(file.Name, props.Size);

                    // Cache the folder reference so subsequent extractions skip the async lookup
                    if (_cachedSubtitleFolder == null)
                    {
                        _cachedSubtitleFolder = await ApplicationData.Current.LocalCacheFolder
                            .CreateFolderAsync("ExtractedSubtitles", CreationCollisionOption.OpenIfExists);
                    }
                    var cacheFolder = _cachedSubtitleFolder;

                    System.Diagnostics.Debug.WriteLine($"[SubtitleExtraction] Calling ExtractAssTracksAsync for '{file.Name}'");

                    var tracks = await Task.Run(() => Odeon.Core.Helpers.MkvSubtitleExtractor.ExtractAssTracksAsync(file, ct), ct);
                    System.Diagnostics.Debug.WriteLine($"[SubtitleExtraction] Found {tracks.Count} ASS track(s) in '{file.Name}'");

                    if (tracks.Count > 0)
                    {
                        // Pre-warm font cache once before the loop — avoids hitting the
                        // semaphore and doing I/O on every per-track Task.Run lambda.
                        int videoHeight = GetVideoHeight();
                        byte[]? fontBytes = await GetSubtitleFontBytesAsync();

                        foreach (var track in tracks)
                        {
                            if (ct.IsCancellationRequested) break;
                            try
                            {
                                string assFileName    = $"{fileHash}_track_{track.TrackNumber}_v76_native.ass";
                                string srtBg0FileName = $"{fileHash}_track_{track.TrackNumber}_v76_overridden_bg0.ass";
                                string srtBg1FileName = $"{fileHash}_track_{track.TrackNumber}_v76_overridden_bg1.ass";

                                string rawAssContent = Odeon.Core.Helpers.MkvSubtitleExtractor.AssembleAssFile(track);

                                StorageFile? assFile = await Task.Run(async () =>
                                {
                                    var cached = await cacheFolder.TryGetItemAsync(assFileName) as StorageFile;
                                    if (cached != null) return cached;

                                    var newFile = await cacheFolder.CreateFileAsync(assFileName, CreationCollisionOption.ReplaceExisting);
                                    await System.IO.File.WriteAllTextAsync(newFile.Path, rawAssContent, System.Text.Encoding.UTF8);
                                    return newFile;
                                });

                                // Generate bg=OFF (no box) overridden file — use pre-warmed fontBytes
                                StorageFile? srtFile = await Task.Run(async () =>
                                {
                                    var cached = await cacheFolder.TryGetItemAsync(srtBg0FileName) as StorageFile;
                                    if (cached != null) return cached;

                                    var newFile = await cacheFolder.CreateFileAsync(srtBg0FileName, CreationCollisionOption.ReplaceExisting);
                                    string overriddenAssContent = Odeon.Core.Helpers.AssStyleRewriter.RewriteAssStyles(rawAssContent, videoHeight, fontBytes);
                                    await System.IO.File.WriteAllTextAsync(newFile.Path, overriddenAssContent, System.Text.Encoding.UTF8);
                                    return newFile;
                                });

                                // Generate bg=ON (with box) overridden file — use pre-warmed fontBytes
                                StorageFile? srtBgFile = await Task.Run(async () =>
                                {
                                    var cached = await cacheFolder.TryGetItemAsync(srtBg1FileName) as StorageFile;
                                    if (cached != null) return cached;

                                    var newFile = await cacheFolder.CreateFileAsync(srtBg1FileName, CreationCollisionOption.ReplaceExisting);
                                    string overriddenAssContent = Odeon.Core.Helpers.AssStyleRewriter.RewriteAssStyles(rawAssContent, videoHeight, fontBytes);
                                    await System.IO.File.WriteAllTextAsync(newFile.Path, overriddenAssContent, System.Text.Encoding.UTF8);
                                    return newFile;
                                });

                                if (assFile != null && srtFile != null && srtBgFile != null && CoreApplication.MainView != null)
                                {
                                    bool selectThis = false; // Do not force selection change on background extraction
                                    var capturedTrack = track;
                                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                    {
                                        if (!ct.IsCancellationRequested && _player != null)
                                        {
                                            AddTrackPair(_player, assFile, srtFile, srtBgFile, capturedTrack, selectThis);
                                        }
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error processing track {track.TrackNumber}: {ex}");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when playback is closed during extraction
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to extract MKV subtitles: {ex.Message}");
            }
        }

        private class SubtitleTrackPair
        {
            public Odeon.Core.Helpers.AssTrack AssTrack { get; set; } = null!;
            public StorageFile NativeAssFile { get; set; } = null!;
            public StorageFile OverriddenSrtFile { get; set; } = null!;
            public StorageFile OverriddenBgSrtFile { get; set; } = null!;
            public LazySubtitleTrack LazyAss { get; set; } = null!;
            public LazySubtitleTrack LazySrt { get; set; } = null!;
            public LazySubtitleTrack LazyBgSrt { get; set; } = null!;
            public SubtitleTrack PrimaryTrack { get; set; } = null!;
        }

        private readonly List<SubtitleTrackPair> _trackPairs = new List<SubtitleTrackPair>();

        private void AddTrackPair(VlcMediaPlayer player, StorageFile assFile, StorageFile srtFile, StorageFile srtBgFile, Odeon.Core.Helpers.AssTrack? assTrack, bool select)
        {
            var lazyAss = new LazySubtitleTrack(player, assFile, assTrack);
            var lazySrt = new LazySubtitleTrack(player, srtFile, assTrack);
            var lazyBgSrt = new LazySubtitleTrack(player, srtBgFile, assTrack);

            bool isOverride = IsOverrideSubtitleStylesEnabled();
            bool isBgEnabled = IsSubtitleBackgroundEnabled();
            var activeLazy = isOverride ? (isBgEnabled ? lazyBgSrt : lazySrt) : lazyAss;

            var pair = new SubtitleTrackPair
            {
                AssTrack = assTrack!,
                NativeAssFile = assFile,
                OverriddenSrtFile = srtFile,
                OverriddenBgSrtFile = srtBgFile,
                LazyAss = lazyAss,
                LazySrt = lazySrt,
                LazyBgSrt = lazyBgSrt,
                PrimaryTrack = activeLazy.Track
            };
            _trackPairs.Add(pair);

            var nativeTrack = assTrack != null ? TrackList.FirstOrDefault(t => t.Id != "-1" &&
                !_pendingSubtitleTracks.Any(p => p.Track == t)) : null;

            if (nativeTrack != null)
            {
                lazyAss.Track.Label = nativeTrack.Label;
                lazySrt.Track.Label = nativeTrack.Label;
                lazyBgSrt.Track.Label = nativeTrack.Label;
                lazyAss.NativeSpu = nativeTrack.VlcSpu;
                lazySrt.NativeSpu = nativeTrack.VlcSpu;
                lazyBgSrt.NativeSpu = nativeTrack.VlcSpu;

                int index = TrackList.IndexOf(nativeTrack);
                if (index >= 0)
                {
                    TrackList[index] = activeLazy.Track;
                    _pendingSubtitleTracks.Add(lazyAss);
                    _pendingSubtitleTracks.Add(lazySrt);
                    _pendingSubtitleTracks.Add(lazyBgSrt);

                    if (SelectedIndex == index)
                    {
                        if (!isOverride && activeLazy.NativeSpu != -1)
                        {
                            activeLazy.Player.VlcPlayer.SetSpu(activeLazy.NativeSpu);
                        }
                        else
                        {
                            if (activeLazy.NativeSpu != -1)
                            {
                                activeLazy.Player.VlcPlayer.SetSpu(-1);
                            }
                            _expectedExternalTrack = activeLazy.Track;
                            activeLazy.Player.AddSubtitle(activeLazy.File, true);
                        }
                    }
                    return;
                }
            }

            _pendingSubtitleTracks.Add(lazyAss);
            _pendingSubtitleTracks.Add(lazySrt);
            _pendingSubtitleTracks.Add(lazyBgSrt);
            TrackList.Add(activeLazy.Track);

            if (select)
            {
                SelectedIndex = TrackList.Count - 1;
            }
        }

        public void RefreshOverrideState()
        {
            bool overrideEnabled = IsOverrideSubtitleStylesEnabled();
            bool bgEnabled = IsSubtitleBackgroundEnabled();

            foreach (var pair in _trackPairs)
            {
                LazySubtitleTrack targetLazy = !overrideEnabled ? pair.LazyAss : (bgEnabled ? pair.LazyBgSrt : pair.LazySrt);

                int idx = TrackList.IndexOf(pair.PrimaryTrack);
                if (idx < 0) idx = TrackList.IndexOf(pair.LazyAss.Track);
                if (idx < 0) idx = TrackList.IndexOf(pair.LazySrt.Track);
                if (idx < 0) idx = TrackList.IndexOf(pair.LazyBgSrt.Track);

                if (idx >= 0)
                {
                    TrackList[idx] = targetLazy.Track;
                    pair.PrimaryTrack = targetLazy.Track;
                }
            }

            if (SelectedIndex < 0 || SelectedIndex >= TrackList.Count || _player == null)
            {
                if (SelectedIndex < 0)
                    _player?.VlcPlayer.SetSpu(-1);
                return;
            }

            var currentTrack = TrackList[SelectedIndex];
            if (_pendingSubtitleTracks.FirstOrDefault(p => p.Track == currentTrack) is { } activeLazy)
            {
                if (!overrideEnabled && activeLazy.NativeSpu != -1)
                {
                    _player.VlcPlayer.SetSpu(activeLazy.NativeSpu);
                }
                else
                {
                    if (activeLazy.Track.VlcSpu != -1)
                    {
                        _player.VlcPlayer.SetSpu(activeLazy.Track.VlcSpu);
                    }
                    else
                    {
                        if (activeLazy.NativeSpu != -1)
                        {
                            _player.VlcPlayer.SetSpu(-1);
                        }
                        _expectedExternalTrack = activeLazy.Track;
                        _player.AddSubtitle(activeLazy.File, true);
                    }
                }
            }
        }

        /// <summary>
        /// FNV-1a hash. Deterministic across process restarts (unlike string.GetHashCode
        /// in .NET Core), so cached subtitle files survive app restarts.
        /// </summary>
        private static string ComputeDeterministicHash(string fileName, ulong fileSize)
        {
            unchecked
            {
                uint hash = 2166136261u; // FNV offset basis
                foreach (char c in fileName)
                {
                    hash ^= (uint)c;
                    hash *= 16777619u; // FNV prime
                }
                // Mix in file size (8 bytes)
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (uint)((fileSize >> (i * 8)) & 0xFF);
                    hash *= 16777619u;
                }
                return hash.ToString("X8");
            }
        }

        internal void SelectVlcSpu(int spu)
        {
            if (spu < 0)
            {
                SelectedIndex = -1;
                return;
            }

            if (Count == 0)
            {
                _delaySpu = spu;
                return;
            }

            for (int i = 0; i < Count; i++)
            {
                if (this[i].VlcSpu == spu)
                {
                    SelectedIndex = i;
                    return;
                }
            }

            // The native MKV ASS track for this spu was replaced by a rewritten slave whose
            // VlcSpu is now -1. Select that slave instead so the unified style is used.
            for (int i = 0; i < Count; i++)
            {
                if (_pendingSubtitleTracks.FirstOrDefault(x => ReferenceEquals(x.Track, this[i])) is { } lazy && lazy.NativeSpu == spu)
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }

        private void OnSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
        {
            if (SelectedIndex < 0)
            {
                _player?.VlcPlayer.SetSpu(-1);
                return;
            }

            if (SelectedIndex >= 0 && SelectedIndex < TrackList.Count && TrackList[SelectedIndex] is { } selectedTrack)
            {
                bool overrideEnabled = IsOverrideSubtitleStylesEnabled();

                if (_pendingSubtitleTracks.FirstOrDefault(x => ReferenceEquals(x.Track, selectedTrack)) is { } lazyTrack)
                {
                    if (!overrideEnabled && lazyTrack.NativeSpu != -1)
                    {
                        _player?.VlcPlayer.SetSpu(lazyTrack.NativeSpu);
                    }
                    else
                    {
                        if (lazyTrack.Track.VlcSpu != -1)
                        {
                            _player?.VlcPlayer.SetSpu(lazyTrack.Track.VlcSpu);
                        }
                        else
                        {
                            if (lazyTrack.NativeSpu != -1)
                            {
                                _player?.VlcPlayer.SetSpu(-1);
                            }
                            _expectedExternalTrack = lazyTrack.Track;
                            lazyTrack.Player.AddSubtitle(lazyTrack.File, true);
                        }
                    }
                }
                else
                {
                    _player?.VlcPlayer.SetSpu(selectedTrack.VlcSpu);
                }
            }
        }

        private int GetVideoHeight()
        {
            try
            {
                foreach (var track in _media.Tracks)
                {
                    if (track.TrackType == TrackType.Video)
                    {
                        return (int)track.Data.Video.Height;
                    }
                }
            }
            catch { }
            return _player != null ? (int)_player.NaturalVideoHeight : 0;
        }


        public void AddExternalSubtitle(VlcMediaPlayer player, StorageFile file, Odeon.Core.Helpers.AssTrack? assTrack, bool select)
        {
            bool isAss = file.FileType.Equals(".ass", StringComparison.OrdinalIgnoreCase) || file.FileType.Equals(".ssa", StringComparison.OrdinalIgnoreCase);

            if (isAss)
            {
                _ = Task.Run(async () => {
                    await _externalSubSemaphore.WaitAsync();
                    try
                    {
                        string assContent = await System.IO.File.ReadAllTextAsync(file.Path, System.Text.Encoding.UTF8);

                        int videoHeight = GetVideoHeight();
                        byte[]? fontBytes = await GetSubtitleFontBytesAsync();
                        string overriddenAss = Odeon.Core.Helpers.AssStyleRewriter.RewriteAssStyles(assContent, videoHeight, fontBytes);

                        var cacheFolder = Windows.Storage.ApplicationData.Current.LocalCacheFolder;
                        var srtFile = await cacheFolder.CreateFileAsync(
                            System.IO.Path.GetFileNameWithoutExtension(file.Name) + "_overridden_bg0.ass",
                            Windows.Storage.CreationCollisionOption.ReplaceExisting);
                        await System.IO.File.WriteAllTextAsync(srtFile.Path, overriddenAss, System.Text.Encoding.UTF8);

                        var srtBgFile = await cacheFolder.CreateFileAsync(
                            System.IO.Path.GetFileNameWithoutExtension(file.Name) + "_overridden_bg1.ass",
                            Windows.Storage.CreationCollisionOption.ReplaceExisting);
                        await System.IO.File.WriteAllTextAsync(srtBgFile.Path, overriddenAss, System.Text.Encoding.UTF8);

                        await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                            Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            AddTrackPair(player, file, srtFile, srtBgFile, assTrack, select));
                    }
                    catch
                    {
                        await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                            Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            AddExternalSubtitleInternal(player, file, assTrack, select));
                    }
                    finally
                    {
                        _externalSubSemaphore.Release();
                    }
                });
                return;
            }
            AddExternalSubtitleInternal(player, file, assTrack, select);
        }

        private void AddExternalSubtitleInternal(VlcMediaPlayer player, StorageFile file, Odeon.Core.Helpers.AssTrack? assTrack, bool select)
        {
            var lazySub = new LazySubtitleTrack(player, file, assTrack);

            if (assTrack != null)
            {
                var nativeTrack = TrackList.FirstOrDefault(t => t.Id != "-1" && t.IsAss &&
                    !_pendingSubtitleTracks.Any(p => p.Track == t));

                if (nativeTrack != null)
                {
                    lazySub.Track.Label = nativeTrack.Label;
                    lazySub.NativeSpu = nativeTrack.VlcSpu;
                    int index = TrackList.IndexOf(nativeTrack);
                    if (index >= 0)
                    {
                        TrackList[index] = lazySub.Track;
                        _pendingSubtitleTracks.Add(lazySub);
                        if (select || SelectedIndex == index)
                        {
                            if (SelectedIndex == index)
                            {
                                OnSelectedIndexChanged(this, null);
                            }
                            else
                            {
                                SelectedIndex = index;
                            }
                        }
                        return;
                    }
                }
            }

            _pendingSubtitleTracks.Add(lazySub);
            TrackList.Add(lazySub.Track);

            if (select)
            {
                SelectedIndex = TrackList.Count - 1;
            }
        }

        internal void NotifyTrackAdded(int trackId)
        {
            if (_expectedExternalTrack != null)
            {
                _expectedExternalTrack.VlcSpu = trackId;
                _expectedExternalTrack.Id = trackId.ToString();
                _expectedExternalTrack = null;
            }
            else if (SelectedIndex >= 0 && TrackList[SelectedIndex] is { VlcSpu: -1 } selectedTrack)
            {
                selectedTrack.VlcSpu = trackId;
                selectedTrack.Id = trackId.ToString();
            }
        }

        private void Media_ParsedChanged(object sender, MediaParsedChangedEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[SubtitleExtraction] Media_ParsedChanged: ParsedStatus is {e.ParsedStatus}");
#endif
            if (e.ParsedStatus != MediaParsedStatus.Done) return;
            _media.ParsedChanged -= Media_ParsedChanged;
            AddVlcMediaTracks(_media.Tracks);
            if (_delaySpu >= 0)
                SelectVlcSpu(_delaySpu);

            if (_player != null)
            {
                StartExtraction();
            }
        }

        private void AddVlcMediaTracks(LibVLCSharp.Shared.MediaTrack[] tracks)
        {
            foreach (LibVLCSharp.Shared.MediaTrack track in tracks)
            {
                if (track.TrackType == TrackType.Text)
                {
                    TrackList.Add(new SubtitleTrack(track));
                }
            }
        }
    }
}
