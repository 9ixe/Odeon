#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace Odeon.Core.Playback
{
    public sealed class PlaybackSubtitleTrackList : SingleSelectTrackList<SubtitleTrack>
    {
        private IMediaPlayer? _player;
        private readonly HashSet<string> _loadedExternalPaths = new(StringComparer.OrdinalIgnoreCase);

        public PlaybackSubtitleTrackList()
        {
        }

        public PlaybackSubtitleTrackList(object? unusedMedia, PlaybackItem? unusedItem = null)
        {
        }

        public void AttachPlayer(IMediaPlayer player)
        {
            _player = player;
        }

        public void UpdateTracks(IReadOnlyList<SubtitleTrack> tracks, int selectedIndex)
        {
            TrackList.Clear();
            TrackList.AddRange(tracks);
            SelectedIndex = selectedIndex;
        }

        public void Refresh()
        {
            // Tracks are automatically updated from mpv track-list property
        }

        public void RefreshOverrideState(IMediaPlayer? player = null)
        {
            if (player != null)
            {
                _player = player;
            }

            // In mpv, subtitle style overrides are handled directly via properties (e.g. sub-ass-override, sub-back-color)
            if ((player ?? _player) is MpvMediaPlayer mpvPlayer)
            {
                try
                {
                    var values = ApplicationData.Current.LocalSettings.Values;
                    bool overrideEnabled = !values.TryGetValue("Player/OverrideSubtitleStyles", out object val) || (val is bool b && b);
                    mpvPlayer.SetSubtitleAssOverride(overrideEnabled);

                    bool subBackEnabled = values.TryGetValue("Player/SubtitleBackgroundEnabled", out object sbVal) && sbVal is bool sb && sb;
                    mpvPlayer.SetSubtitleBackground(subBackEnabled);

                    bool subOutlineEnabled = !values.TryGetValue("Player/SubtitleOutlineEnabled", out object soVal) || (soVal is bool so && so);
                    mpvPlayer.SetSubtitleOutline(subOutlineEnabled);
                }
                catch
                {
                    // Ignore settings read errors
                }
            }
        }

        public void AddExternalSubtitle(IMediaPlayer player, StorageFile file, object? assTrack = null, bool select = true)
        {
            _player = player;
            string key = !string.IsNullOrEmpty(file.Path) ? file.Path : file.Name;
            lock (_loadedExternalPaths)
            {
                if (_loadedExternalPaths.Contains(key))
                {
                    if (select)
                    {
                        for (int i = 0; i < TrackList.Count; i++)
                        {
                            if (TrackList[i].Label.StartsWith(file.Name, StringComparison.OrdinalIgnoreCase) ||
                                TrackList[i].TrackId.ToString() == file.Name)
                            {
                                SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    return;
                }
                _loadedExternalPaths.Add(key);
            }

            if (player is MpvMediaPlayer mpvPlayer)
            {
                mpvPlayer.AddSubtitle(file, select);
            }
        }

        public new void Clear()
        {
            lock (_loadedExternalPaths)
            {
                _loadedExternalPaths.Clear();
            }
            base.Clear();
        }

        internal static Task<byte[]?> GetSubtitleFontBytesAsync()
        {
            return Task.FromResult<byte[]?>(null);
        }
    }
}
