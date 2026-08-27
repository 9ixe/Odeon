#nullable enable

using Odeon.Core.Helpers;
using Odeon.Core.Models;
using Odeon.Core.Playback;

namespace Odeon.Core.Services;

public interface ICastService
{
    /// <summary>
    /// Create a new renderer watcher for the specified media player
    /// </summary>
    RendererWatcher CreateRendererWatcher(IMediaPlayer player);

    /// <summary>
    /// Set the active renderer for the media player
    /// </summary>
    bool SetActiveRenderer(IMediaPlayer player, Renderer? renderer);
}
