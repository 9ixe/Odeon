#nullable enable

namespace Odeon.Core.Playback;

public sealed class VideoTrack : MediaTrack
{
    public int TrackId { get; }

    public string Name { get; }
    public string? Codec { get; }
    public uint Width { get; }
    public uint Height { get; }
    public bool IsSelected { get; }

    public VideoTrack(long id, string? title, string? language, string? codec, uint width, uint height, bool selected = false)
        : base(Windows.Media.Core.MediaTrackKind.Video, id.ToString(), title, language)
    {
        TrackId = (int)id;
        Name = !string.IsNullOrEmpty(title) ? title! : (Language ?? id.ToString());
        Codec = codec;
        Width = width;
        Height = height;
        IsSelected = selected;
    }

    public VideoTrack(Windows.Media.Core.VideoTrack videoTrack) : base(videoTrack)
    {
        Name = videoTrack.Name ?? string.Empty;
        var props = videoTrack.GetEncodingProperties();
        if (props != null)
        {
            Width = props.Width;
            Height = props.Height;
        }
    }
}
