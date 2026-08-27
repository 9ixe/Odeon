#nullable enable

using CommunityToolkit.Diagnostics;
using LibVLCSharp.Shared;
using Windows.Media.Core;

namespace Odeon.Core.Playback
{
    public sealed class SubtitleTrack : MediaTrack
    {
        internal int VlcSpu { get; set; }
        internal uint Codec { get; }

        public bool IsAss
        {
            get
            {
                byte[] bytes = System.BitConverter.GetBytes(Codec);
                string fourcc = System.Text.Encoding.ASCII.GetString(bytes);
                return fourcc.StartsWith("ass", System.StringComparison.OrdinalIgnoreCase) || 
                       fourcc.StartsWith("ssa", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        public SubtitleTrack(string language = "") : base(MediaTrackKind.TimedMetadata, language)
        {
        }

        public SubtitleTrack(LibVLCSharp.Shared.MediaTrack textTrack) : base(textTrack)
        {
            Guard.IsTrue(textTrack.TrackType == TrackType.Text, nameof(textTrack.TrackType));
            VlcSpu = textTrack.Id;
            Codec = textTrack.Codec;
        }

        public SubtitleTrack(TimedMetadataTrack track) : base(track)
        {
        }
    }
}

