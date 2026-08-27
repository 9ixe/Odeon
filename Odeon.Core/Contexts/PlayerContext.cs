#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using Odeon.Core.Playback;

namespace Odeon.Core.Contexts;

public sealed partial class PlayerContext : ObservableRecipient
{
    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private IMediaPlayer? _mediaPlayer;
}
