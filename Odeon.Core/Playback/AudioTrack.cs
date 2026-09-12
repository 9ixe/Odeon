#nullable enable

namespace Odeon.Core.Playback;

public sealed class AudioTrack : MediaTrack
{
    public int TrackId { get; }

    public string Name { get; }
    public string Title => Name;
    public string? Codec { get; }
    public bool IsSelected { get; }

    public AudioTrack(long id, string? title, string? language, string? codec, bool selected = false)
        : base(Windows.Media.Core.MediaTrackKind.Audio, id.ToString(), title, language)
    {
        TrackId = (int)id;
        Name = !string.IsNullOrEmpty(title) ? title! : (Language ?? id.ToString());
        Codec = codec;
        IsSelected = selected;
    }

    public AudioTrack(Windows.Media.Core.AudioTrack audioTrack) : base(audioTrack)
    {
        Name = audioTrack.Name ?? string.Empty;
    }
}
