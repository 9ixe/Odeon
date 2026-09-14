#nullable enable

using System;
using System.Collections.Generic;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Odeon.Core.Playback
{
    public sealed class PlaybackAudioTrackList : SingleSelectTrackList<AudioTrack>
    {
        private readonly MediaPlaybackAudioTrackList? _source;
        private readonly HashSet<string> _loadedExternalPaths = new(StringComparer.OrdinalIgnoreCase);
        private IMediaPlayer? _player;

        public PlaybackAudioTrackList()
        {
        }

        public PlaybackAudioTrackList(object? unused)
        {
        }

        public PlaybackAudioTrackList(MediaPlaybackAudioTrackList source)
        {
            _source = source;
            SelectedIndex = source.SelectedIndex;
            source.SelectedIndexChanged += (sender, args) => SelectedIndex = sender.SelectedIndex;
            foreach (Windows.Media.Core.AudioTrack audioTrack in source)
            {
                TrackList.Add(new AudioTrack(audioTrack));
            }

            SelectedIndexChanged += OnSelectedIndexChanged;
        }

        public void AddExternalAudio(IMediaPlayer player, StorageFile file, bool select = true)
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
                mpvPlayer.AddAudioTrack(file, select);
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

        public void UpdateTracks(IReadOnlyList<AudioTrack> tracks, int selectedIndex)
        {
            TrackList.Clear();
            TrackList.AddRange(tracks);
            SetSelectedIndexSilently(selectedIndex);
            NotifyTrackListChanged();
        }

        public void Refresh()
        {
            if (_source != null)
            {
                TrackList.Clear();
                foreach (Windows.Media.Core.AudioTrack audioTrack in _source)
                {
                    TrackList.Add(new AudioTrack(audioTrack));
                }
            }
        }

        private void OnSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
        {
            if (_source == null || _source.SelectedIndex == sender.SelectedIndex) return;
            _source.SelectedIndex = sender.SelectedIndex;
        }
    }
}
