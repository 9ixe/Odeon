#nullable enable

using System;
using Odeon.Core.Helpers;
using Odeon.Core.Models;
using Odeon.Core.Playback;

namespace Odeon.Core.Services;

public sealed class CastService : ICastService
{
    public RendererWatcher CreateRendererWatcher(IMediaPlayer player)
    {
        if (player is not VlcMediaPlayer vlcMediaPlayer)
            throw new NotSupportedException("RendererWatcher only supports VlcMediaPlayer.");

        return new RendererWatcher(vlcMediaPlayer);
    }

    public bool SetActiveRenderer(IMediaPlayer player, Renderer? renderer)
    {
        if (player is not VlcMediaPlayer vlcMediaPlayer) return false;
        return vlcMediaPlayer.VlcPlayer.SetRenderer(renderer?.Target);
    }
}
