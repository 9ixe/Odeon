#nullable enable

using System.Collections.Generic;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Odeon.Core.Playback
{
    public sealed class PlaybackVideoTrackList : SingleSelectTrackList<VideoTrack>
    {
        private readonly MediaPlaybackVideoTrackList? _source;

        public PlaybackVideoTrackList()
        {
        }

        public PlaybackVideoTrackList(object? unused)
        {
        }

        public PlaybackVideoTrackList(MediaPlaybackVideoTrackList source)
        {
            _source = source;
            SelectedIndex = source.SelectedIndex;
            source.SelectedIndexChanged += (sender, args) => SelectedIndex = sender.SelectedIndex;
            foreach (Windows.Media.Core.VideoTrack videoTrack in source)
            {
                TrackList.Add(new VideoTrack(videoTrack));
            }

            SelectedIndexChanged += OnSelectedIndexChanged;
        }

        public void UpdateTracks(IReadOnlyList<VideoTrack> tracks, int selectedIndex)
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
                foreach (Windows.Media.Core.VideoTrack videoTrack in _source)
                {
                    TrackList.Add(new VideoTrack(videoTrack));
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
