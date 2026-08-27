#nullable enable

using CommunityToolkit.Diagnostics;
using LibVLCSharp.Shared;

namespace Odeon.Core.Playback;

public sealed class VideoTrack : MediaTrack
{
    internal int VlcTrackId { get; }

    public string Name { get; }

    public uint Width { get; }

    public uint Height { get; }

    public VideoTrack(LibVLCSharp.Shared.MediaTrack videoTrack) : base(videoTrack)
    {
        Guard.IsTrue(videoTrack.TrackType == TrackType.Video, nameof(videoTrack.TrackType));
        VlcTrackId = videoTrack.Id;
        Name = videoTrack.Description ?? videoTrack.Language ?? videoTrack.Id.ToString();
        Width = videoTrack.Data.Video.Width;
        Height = videoTrack.Data.Video.Height;
    }

    public VideoTrack(Windows.Media.Core.VideoTrack videoTrack) : base(videoTrack)
    {
        Name = videoTrack.Name;
        var props = videoTrack.GetEncodingProperties();
        if (props != null)
        {
            Width = props.Width;
            Height = props.Height;
        }
    }
}
