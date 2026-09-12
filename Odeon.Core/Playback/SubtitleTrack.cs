#nullable enable

using System;
using Windows.Media.Core;

namespace Odeon.Core.Playback
{
    public sealed class SubtitleTrack : MediaTrack
    {
        public int TrackId { get; set; }

        public string? CodecName { get; }
        public bool IsExternal { get; }
        public bool IsSelected { get; }
        public string Title => Label;

        public bool IsAss => CodecName?.StartsWith("ass", StringComparison.OrdinalIgnoreCase) == true ||
                             CodecName?.StartsWith("ssa", StringComparison.OrdinalIgnoreCase) == true ||
                             CodecName?.StartsWith("substation", StringComparison.OrdinalIgnoreCase) == true;

        public SubtitleTrack(long id, string? title, string? language, string? codec, bool isExternal = false, bool selected = false)
            : base(MediaTrackKind.TimedMetadata, id.ToString(), title, language)
        {
            TrackId = (int)id;
            CodecName = codec;
            IsExternal = isExternal;
            IsSelected = selected;
        }

        public SubtitleTrack(string language = "") : base(MediaTrackKind.TimedMetadata, language)
        {
        }

        public SubtitleTrack(TimedMetadataTrack track) : base(track)
        {
        }
    }
}
