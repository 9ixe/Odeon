#nullable enable

using System;
using Odeon.Core.Services;

namespace Odeon.Core.Playback
{
    /// <summary>
    /// Represents a playable media item for mpv.
    /// Media properties (duration, tracks, chapters) are observed directly from the player.
    /// </summary>
    public class PlaybackItem
    {
        public object OriginalSource { get; }

        public string FilePath { get; }

        public bool IsDisabledInPlaybackList { get; set; }

        public PlaybackAudioTrackList AudioTracks { get; }

        public PlaybackVideoTrackList VideoTracks { get; }

        public PlaybackSubtitleTrackList SubtitleTracks { get; }

        public PlaybackChapterList Chapters { get; }

        public TimeSpan StartTime { get; set; }

        // Duration comes from the player (IMediaPlayer.NaturalDuration), not the item
        public TimeSpan? Duration => null;

        public PlaybackItem(object source, string? filePath = null)
        {
            OriginalSource = source;
            FilePath = filePath ?? PlayerService.ResolvePath(source) ?? string.Empty;
            AudioTracks = new PlaybackAudioTrackList();
            VideoTracks = new PlaybackVideoTrackList();
            SubtitleTracks = new PlaybackSubtitleTrackList();
            Chapters = new PlaybackChapterList(this);
            StartTime = TimeSpan.Zero;
        }
    }
}
