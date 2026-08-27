#nullable enable

using Odeon.Core.Playback;

namespace Odeon.Core.Services;

public interface IPlayerService
{
    IMediaPlayer Initialize(string[] swapChainOptions);

    PlaybackItem CreatePlaybackItem(IMediaPlayer player, object source, params string[] options);

    void DisposePlaybackItem(PlaybackItem item);

    void DisposePlayer(IMediaPlayer player);
}
