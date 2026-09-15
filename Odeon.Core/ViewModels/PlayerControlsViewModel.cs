#nullable enable

using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Odeon.Core.Contexts;
using Odeon.Core.Coordinators;
using Odeon.Core.Enums;
using Odeon.Core.Helpers;
using Odeon.Core.Messages;
using Odeon.Core.Playback;
using Odeon.Core.Services;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Odeon.Core.ViewModels;

public sealed partial class PlayerControlsViewModel : ObservableRecipient,
    IRecipient<PropertyChangedMessage<IMediaPlayer?>>,
    IRecipient<SettingsChangedMessage>,
    IRecipient<TogglePlayPauseMessage>,
    IRecipient<ChangePlaybackRateRequestMessage>,
    IRecipient<PropertyChangedMessage<PlayerVisibilityState>>,
    IRecipient<PropertyChangedMessage<WindowViewMode>>
{
    /// <summary>
    /// The observable play queue state. Bind to this for <c>Items</c>,
    /// <c>CurrentItem</c>, <c>ShuffleMode</c>, and <c>RepeatMode</c>.
    /// </summary>
    public PlayQueueContext PlayQueue { get; }

    public bool ShouldBeAdaptive => !IsCompact && SystemInformation.IsDesktop;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private string? _titleName; // TODO: Handle mpv title name
    [ObservableProperty] private string? _chapterName;
    [ObservableProperty] private double _playbackRate;
    [ObservableProperty] private double _audioTimingOffset;
    [ObservableProperty] private double _subtitleTimingOffset;
    [ObservableProperty] private bool _isMinimal;
    [ObservableProperty] private bool _playerShowChapters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldBeAdaptive))]
    private bool _isCompact;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSnapshotCommand))]
    private bool _hasVideo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    private bool _hasActiveItem;

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IWindowService _windowService;
    private readonly ISettingsService _settingsService;
    private readonly PlayerContext _playerContext;
    private readonly IPlayQueueCoordinator _coordinator;
    private Size _aspectRatio;
    private int _subtitleFontSize;
    private readonly DispatcherQueueTimer _subtitleFontSizeDebounceTimer;
    private int _subtitlePosition;
    private readonly DispatcherQueueTimer _subtitlePositionDebounceTimer;
    private bool _subtitleBackgroundEnabled;
    private int _subtitleBackgroundOpacity;
    private bool _subtitleOutlineEnabled;
    private readonly DispatcherQueueTimer _subtitleBackgroundOpacityDebounceTimer;
    private readonly DispatcherQueueTimer _audioTimingOffsetDebounceTimer;
    private readonly DispatcherQueueTimer _subtitleTimingOffsetDebounceTimer;

    public PlayerControlsViewModel(
        PlayQueueContext playQueue,
        IPlayQueueCoordinator coordinator,
        ISettingsService settingsService,
        IWindowService windowService,
        PlayerContext playerContext)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _windowService = windowService;
        _settingsService = settingsService;
        _playerContext = playerContext;
        _coordinator = coordinator;
        _playbackRate = 1.0;
        _audioTimingOffset = 0.0;
        _subtitleTimingOffset = 0.0;
        _isMinimal = true;
        _playerShowChapters = settingsService.PlayerShowChapters;
        _subtitleFontSize = settingsService.SubtitleFontSize;
        _subtitleFontSizeDebounceTimer = _dispatcherQueue.CreateTimer();
        _subtitleFontSizeDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _subtitleFontSizeDebounceTimer.IsRepeating = false;
        _subtitleFontSizeDebounceTimer.Tick += (_, _) =>
        {
            if (_settingsService.SubtitleFontSize != _subtitleFontSize)
            {
                _settingsService.SubtitleFontSize = _subtitleFontSize;
            }
        };
        _subtitlePosition = settingsService.SubtitlePosition;
        if (_subtitlePosition < 50 || _subtitlePosition > 115) _subtitlePosition = 100;
        _subtitlePositionDebounceTimer = _dispatcherQueue.CreateTimer();
        _subtitlePositionDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _subtitlePositionDebounceTimer.IsRepeating = false;
        _subtitlePositionDebounceTimer.Tick += (_, _) =>
        {
            if (_settingsService.SubtitlePosition != _subtitlePosition)
            {
                _settingsService.SubtitlePosition = _subtitlePosition;
            }
        };
        _subtitleBackgroundEnabled = settingsService.SubtitleBackgroundEnabled;
        _subtitleOutlineEnabled = settingsService.SubtitleOutlineEnabled;
        _subtitleBackgroundOpacity = settingsService.SubtitleBackgroundOpacity;
        if (_subtitleBackgroundOpacity < 10 || _subtitleBackgroundOpacity > 100) _subtitleBackgroundOpacity = 75;
        _subtitleBackgroundOpacityDebounceTimer = _dispatcherQueue.CreateTimer();
        _subtitleBackgroundOpacityDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _subtitleBackgroundOpacityDebounceTimer.IsRepeating = false;
        _subtitleBackgroundOpacityDebounceTimer.Tick += (_, _) =>
        {
            if (_settingsService.SubtitleBackgroundOpacity != _subtitleBackgroundOpacity)
            {
                _settingsService.SubtitleBackgroundOpacity = _subtitleBackgroundOpacity;
            }
        };

        _audioTimingOffsetDebounceTimer = _dispatcherQueue.CreateTimer();
        _audioTimingOffsetDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _audioTimingOffsetDebounceTimer.IsRepeating = false;
        _audioTimingOffsetDebounceTimer.Tick += (_, _) =>
        {
            string mediaKey = (MediaPlayer as MpvMediaPlayer)?.PlaybackItem?.FilePath ?? string.Empty;
            if (!string.IsNullOrEmpty(mediaKey))
            {
                try
                {
                    string aKey = $"MediaAudioDelay_{mediaKey.GetHashCode():X8}";
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[aKey] = _audioTimingOffset;
                }
                catch { }
            }
        };

        _subtitleTimingOffsetDebounceTimer = _dispatcherQueue.CreateTimer();
        _subtitleTimingOffsetDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _subtitleTimingOffsetDebounceTimer.IsRepeating = false;
        _subtitleTimingOffsetDebounceTimer.Tick += (_, _) =>
        {
            string mediaKey = (MediaPlayer as MpvMediaPlayer)?.PlaybackItem?.FilePath ?? string.Empty;
            if (!string.IsNullOrEmpty(mediaKey))
            {
                try
                {
                    string sKey = $"MediaSubDelay_{mediaKey.GetHashCode():X8}";
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[sKey] = _subtitleTimingOffset;
                }
                catch { }
            }
        };
        PlayQueue = playQueue;
        PlayQueue.PropertyChanged += PlayQueueOnPropertyChanged;
        _coordinator.CanNavigateChanged += OnCoordinatorCanNavigateChanged;

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            MediaPlayer.ChapterChanged += OnChapterChanged;
            MediaPlayer.ChaptersLoaded += OnChaptersLoaded;
            MediaPlayer.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
            ChapterName = MediaPlayer.Chapter?.Title;
            if (MediaPlayer is MpvMediaPlayer mpvPlayer)
            {
                mpvPlayer.SetSubtitleFontSize(_subtitleFontSize);
                try
                {
                    mpvPlayer.SetSubtitlePosition(_subtitlePosition);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting subtitle position: {ex.Message}");
                }
                mpvPlayer.SetSubtitleBackgroundOpacity(_subtitleBackgroundOpacity);
                mpvPlayer.SetSubtitleBackground(_subtitleBackgroundEnabled);
                mpvPlayer.SetSubtitleOutline(_subtitleOutlineEnabled);
            }
        }

        IsActive = true;
    }

    public bool OverrideSubtitleStyles
    {
        get => _settingsService.OverrideSubtitleStyles;
        set
        {
            if (_settingsService.OverrideSubtitleStyles != value)
            {
                _settingsService.OverrideSubtitleStyles = value;
                OnPropertyChanged();
                if (MediaPlayer is MpvMediaPlayer mpvPlayer)
                {
                    mpvPlayer.SetSubtitleAssOverride(value);
                }
                MediaPlayer?.PlaybackItem?.SubtitleTracks.RefreshOverrideState(MediaPlayer);
            }
        }
    }

    public int SubtitleFontSize
    {
        get => _subtitleFontSize;
        set
        {
            if (_subtitleFontSize != value)
            {
                _subtitleFontSize = value;
                OnPropertyChanged();

                // 1. Immediately apply to active player for 60fps real-time visual feedback
                if (MediaPlayer is MpvMediaPlayer mpvPlayer)
                {
                    mpvPlayer.SetSubtitleFontSize(value);
                }

                // 2. Debounce persistent disk I/O to LocalSettings (300ms idle)
                _subtitleFontSizeDebounceTimer.Stop();
                _subtitleFontSizeDebounceTimer.Start();
            }
        }
    }

    public int SubtitlePosition
    {
        get => _subtitlePosition;
        set
        {
            value = Math.Clamp(value, 50, 115);
            if (_subtitlePosition != value)
            {
                _subtitlePosition = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SubtitlePositionDisplay));
                OnPropertyChanged(nameof(SubtitlePositionOffset));

                if (MediaPlayer is MpvMediaPlayer mpvPlayer)
                {
                    try
                    {
                        mpvPlayer.SetSubtitlePosition(value);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error setting subtitle position: {ex.Message}");
                    }
                }

                _subtitlePositionDebounceTimer.Stop();
                _subtitlePositionDebounceTimer.Start();
            }
        }
    }

    public int SubtitlePositionOffset
    {
        get => 100 - _subtitlePosition;
        set
        {
            int clampedOffset = Math.Clamp(value, -15, 50);
            SubtitlePosition = 100 - clampedOffset;
        }
    }

    public string SubtitlePositionDisplay
    {
        get
        {
            int offset = 100 - _subtitlePosition;
            if (offset == 0) return "Default";
            return offset > 0 ? $"+{offset}%" : $"{offset}%";
        }
    }

    [RelayCommand]
    private void MoveSubtitleUp()
    {
        if (SubtitlePosition > 50)
        {
            SubtitlePosition = Math.Max(50, SubtitlePosition - 2);
        }
    }

    [RelayCommand]
    private void MoveSubtitleDown()
    {
        if (SubtitlePosition < 115)
        {
            SubtitlePosition = Math.Min(115, SubtitlePosition + 2);
        }
    }

    [RelayCommand]
    private void ResetSubtitlePosition()
    {
        SubtitlePosition = 100;
    }

    public bool SubtitleBackgroundEnabled
    {
        get => _subtitleBackgroundEnabled;
        set
        {
            if (_subtitleBackgroundEnabled != value)
            {
                _subtitleBackgroundEnabled = value;
                OnPropertyChanged();
                _settingsService.SubtitleBackgroundEnabled = value;

                if (MediaPlayer is MpvMediaPlayer mpvPlayer)
                {
                    mpvPlayer.SetSubtitleBackground(value);
                }
            }
        }
    }

    public int SubtitleBackgroundOpacity
    {
        get => _subtitleBackgroundOpacity;
        set
        {
            if (_subtitleBackgroundOpacity != value)
            {
                _subtitleBackgroundOpacity = value;
                OnPropertyChanged();

                if (MediaPlayer is MpvMediaPlayer mpvPlayer)
                {
                    mpvPlayer.SetSubtitleBackgroundOpacity(value);
                }

                _subtitleBackgroundOpacityDebounceTimer.Stop();
                _subtitleBackgroundOpacityDebounceTimer.Start();
            }
        }
    }

    public bool SubtitleOutlineEnabled
    {
        get => _subtitleOutlineEnabled;
        set
        {
            if (_subtitleOutlineEnabled != value)
            {
                _subtitleOutlineEnabled = value;
                OnPropertyChanged();
                _settingsService.SubtitleOutlineEnabled = value;

                if (MediaPlayer is MpvMediaPlayer mpvPlayer)
                {
                    mpvPlayer.SetSubtitleOutline(value);
                }
            }
        }
    }

    public void Receive(SettingsChangedMessage message)
    {
        switch (message.SettingsName)
        {
            case nameof(SettingsPageViewModel.PlayerShowChapters):
                PlayerShowChapters = _settingsService.PlayerShowChapters;
                break;
            case nameof(ISettingsService.OverrideSubtitleStyles):
                OnPropertyChanged(nameof(OverrideSubtitleStyles));
                if (MediaPlayer is MpvMediaPlayer mpv)
                {
                    mpv.SetSubtitleAssOverride(_settingsService.OverrideSubtitleStyles);
                }
                MediaPlayer?.PlaybackItem?.SubtitleTracks.RefreshOverrideState(MediaPlayer);
                break;
            case nameof(ISettingsService.SubtitleFontSize):
                if (_subtitleFontSize != _settingsService.SubtitleFontSize)
                {
                    _subtitleFontSize = _settingsService.SubtitleFontSize;
                    OnPropertyChanged(nameof(SubtitleFontSize));
                    if (MediaPlayer is MpvMediaPlayer mpvSize)
                    {
                        mpvSize.SetSubtitleFontSize(_subtitleFontSize);
                    }
                }
                break;
            case nameof(ISettingsService.SubtitlePosition):
                if (_subtitlePosition != _settingsService.SubtitlePosition)
                {
                    _subtitlePosition = _settingsService.SubtitlePosition;
                    OnPropertyChanged(nameof(SubtitlePosition));
                    OnPropertyChanged(nameof(SubtitlePositionDisplay));
                    OnPropertyChanged(nameof(SubtitlePositionOffset));
                    if (MediaPlayer is MpvMediaPlayer mpvPos)
                    {
                        try
                        {
                            mpvPos.SetSubtitlePosition(_subtitlePosition);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error setting subtitle position: {ex.Message}");
                        }
                    }
                }
                break;
            case nameof(ISettingsService.SubtitleBackgroundEnabled):
                if (_subtitleBackgroundEnabled != _settingsService.SubtitleBackgroundEnabled)
                {
                    _subtitleBackgroundEnabled = _settingsService.SubtitleBackgroundEnabled;
                    OnPropertyChanged(nameof(SubtitleBackgroundEnabled));
                    if (MediaPlayer is MpvMediaPlayer mpvBg)
                    {
                        mpvBg.SetSubtitleBackground(_subtitleBackgroundEnabled);
                    }
                }
                break;
            case nameof(ISettingsService.SubtitleBackgroundOpacity):
                if (_subtitleBackgroundOpacity != _settingsService.SubtitleBackgroundOpacity)
                {
                    _subtitleBackgroundOpacity = _settingsService.SubtitleBackgroundOpacity;
                    OnPropertyChanged(nameof(SubtitleBackgroundOpacity));
                    if (MediaPlayer is MpvMediaPlayer mpvBo)
                    {
                        mpvBo.SetSubtitleBackgroundOpacity(_subtitleBackgroundOpacity);
                    }
                }
                break;
            case nameof(ISettingsService.SubtitleOutlineEnabled):
                if (_subtitleOutlineEnabled != _settingsService.SubtitleOutlineEnabled)
                {
                    _subtitleOutlineEnabled = _settingsService.SubtitleOutlineEnabled;
                    OnPropertyChanged(nameof(SubtitleOutlineEnabled));
                    if (MediaPlayer is MpvMediaPlayer mpvOut)
                    {
                        mpvOut.SetSubtitleOutline(_subtitleOutlineEnabled);
                    }
                }
                break;
        }
    }

    public void Receive(PropertyChangedMessage<IMediaPlayer?> message)
    {
        if (message.Sender is not PlayerContext) return;
        if (message.OldValue is { } oldPlayer)
        {
            oldPlayer.PlaybackStateChanged -= OnPlaybackStateChanged;
            oldPlayer.ChapterChanged -= OnChapterChanged;
            oldPlayer.ChaptersLoaded -= OnChaptersLoaded;
            oldPlayer.NaturalVideoSizeChanged -= OnNaturalVideoSizeChanged;
        }

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            MediaPlayer.ChapterChanged += OnChapterChanged;
            MediaPlayer.ChaptersLoaded += OnChaptersLoaded;
            MediaPlayer.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
            ChapterName = MediaPlayer.Chapter?.Title;
            if (MediaPlayer is MpvMediaPlayer mpvPlayer)
            {
                mpvPlayer.SetSubtitleFontSize(_subtitleFontSize);
                try
                {
                    mpvPlayer.SetSubtitlePosition(_subtitlePosition);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting subtitle position: {ex.Message}");
                }
                mpvPlayer.SetSubtitleBackgroundOpacity(_subtitleBackgroundOpacity);
                mpvPlayer.SetSubtitleBackground(_subtitleBackgroundEnabled);
                mpvPlayer.SetSubtitleOutline(_subtitleOutlineEnabled);
            }
        }
    }

    public void Receive(TogglePlayPauseMessage message)
    {
        if (!HasActiveItem || MediaPlayer == null) return;
        if (message.ShowBadge)
        {
            PlayPauseWithBadge();
        }
        else
        {
            PlayPauseInternal();
        }
    }

    public void Receive(ChangePlaybackRateRequestMessage message)
    {
        SetPlaybackRate(message.Value);
        message.Reply(PlaybackRate);
    }

    public void Receive(PropertyChangedMessage<PlayerVisibilityState> message)
    {
        IsMinimal = message.NewValue != PlayerVisibilityState.Visible;
    }

    /// <summary>
    /// Toggles the playback state of the active item and displays a badge indicating the new state.
    /// </summary>
    public void PlayPauseWithBadge()
    {
        if (!HasActiveItem) return;
        Messenger.Send(new ShowPlayPauseBadgeMessage(!IsPlaying));
        PlayPauseInternal();
    }

    /// <summary>
    /// Handles toggling the subtitle track during media playback based on keyboard input.
    /// </summary>
    /// <remarks>
    /// The following modifiers determine the toggle action:
    /// <list type="bullet">
    /// <item><description><see cref="VirtualKeyModifiers.None"/> toggles the only subtitle track on or off.</description></item>
    /// <item><description><see cref="VirtualKeyModifiers.Control"/> cycles forward through the available subtitle tracks.</description></item>
    /// <item><description><see cref="VirtualKeyModifiers.Control"/> + <see cref="VirtualKeyModifiers.Shift"/> cycles backward through the available subtitle tracks.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="modifiers">The modifier keys held during the key press.</param>
    /// <returns>
    /// A tuple where <c>Handled</c> is <see langword="true"/> if the toggle succeeded, and
    /// <c>TrackLabel</c> is the label of the newly selected track, or <see langword="null"/> if subtitles were disabled.
    /// </returns>
    public (bool Handled, string? TrackLabel) ProcessToggleSubtitleKeyDown(VirtualKeyModifiers modifiers)
    {
        if (MediaPlayer?.PlaybackItem is null)
        {
            return (false, null);
        }

        PlaybackSubtitleTrackList subtitleTracks = MediaPlayer.PlaybackItem.SubtitleTracks;
        if (subtitleTracks.Count == 0)
        {
            return (false, null);
        }

        switch (modifiers)
        {
            case VirtualKeyModifiers.None when subtitleTracks.Count == 1:
                if (subtitleTracks.SelectedIndex >= 0)
                {
                    subtitleTracks.SelectedIndex = -1;
                }
                else
                {
                    subtitleTracks.SelectedIndex = 0;
                }

                break;
            case VirtualKeyModifiers.Control:
                if (subtitleTracks.SelectedIndex == subtitleTracks.Count - 1)
                {
                    subtitleTracks.SelectedIndex = -1;
                }
                else
                {
                    subtitleTracks.SelectedIndex++;
                }

                break;
            case VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift:
                if (subtitleTracks.SelectedIndex == -1)
                {
                    subtitleTracks.SelectedIndex = subtitleTracks.Count - 1;
                }
                else
                {
                    subtitleTracks.SelectedIndex--;
                }

                break;
            default:
                return (false, null);
        }

        string? label = subtitleTracks.SelectedIndex == -1
            ? null
            : subtitleTracks[subtitleTracks.SelectedIndex].Label;

        return (true, label);
    }

    /// <summary>
    /// Sends a status message via the messenger.
    /// The view layer should call this after formatting a localized status string.
    /// </summary>
    /// <param name="message">The formatted status message to display.</param>
    public void SendStatusMessage(string? message)
    {
        Messenger.Send(new UpdateStatusMessage(message));
    }

    partial void OnPlaybackRateChanged(double value)
    {
        if (MediaPlayer == null) return;
        MediaPlayer.PlaybackRate = value;
    }

    partial void OnAudioTimingOffsetChanged(double value)
    {
        if (MediaPlayer == null) return;

        if (MediaPlayer is MpvMediaPlayer mpvMediaPlayer)
        {
            mpvMediaPlayer.AudioDelay = value;
        }

        _audioTimingOffsetDebounceTimer.Stop();
        _audioTimingOffsetDebounceTimer.Start();
    }

    partial void OnSubtitleTimingOffsetChanged(double value)
    {
        if (MediaPlayer == null) return;

        if (MediaPlayer is MpvMediaPlayer mpvMediaPlayer)
        {
            mpvMediaPlayer.SubtitleDelay = value;
        }

        _subtitleTimingOffsetDebounceTimer.Stop();
        _subtitleTimingOffsetDebounceTimer.Start();
    }

    private void PlayQueueOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayQueueContext.CurrentItem):
                HasActiveItem = PlayQueue.CurrentItem is not null;
                ChapterName = MediaPlayer?.Chapter?.Title;
                double savedAudioDelay = 0.0;
                double savedSubDelay = 0.0;
                string itemKey = PlayQueue.CurrentItem?.Item.Value?.FilePath ?? PlayQueue.CurrentItem?.Location ?? string.Empty;
                if (!string.IsNullOrEmpty(itemKey))
                {
                    try
                    {
                        var vals = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                        string aKey = $"MediaAudioDelay_{itemKey.GetHashCode():X8}";
                        if (vals.TryGetValue(aKey, out object aVal))
                        {
                            savedAudioDelay = aVal is double ad ? ad : (aVal is int ai ? (double)ai : 0.0);
                        }

                        string sKey = $"MediaSubDelay_{itemKey.GetHashCode():X8}";
                        if (vals.TryGetValue(sKey, out object sVal))
                        {
                            savedSubDelay = sVal is double sd ? sd : (sVal is int si ? (double)si : 0.0);
                        }
                    }
                    catch { }
                }
                AudioTimingOffset = savedAudioDelay;
                SubtitleTimingOffset = savedSubDelay;
                break;
        }
    }

    private void OnNaturalVideoSizeChanged(IMediaPlayer sender, object? args)
    {
        _dispatcherQueue.TryEnqueue(() => HasVideo = MediaPlayer?.NaturalVideoHeight > 0);
    }

    private void OnPlaybackStateChanged(IMediaPlayer sender, object? args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsPlaying = sender.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Opening;
        });
    }

    private void OnChapterChanged(IMediaPlayer sender, object? args)
    {
        _dispatcherQueue.TryEnqueue(() => ChapterName = sender.Chapter?.Title);
    }

    private void OnChaptersLoaded(IMediaPlayer sender, EventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (string.IsNullOrEmpty(ChapterName))
            {
                ChapterName = sender.Chapter?.Title;
            }
        });
    }

    public void Receive(PropertyChangedMessage<WindowViewMode> message)
    {
        if (message.Sender is not WindowContext) return;
        switch (message.NewValue)
        {
            case WindowViewMode.Default:
                IsFullscreen = false;
                IsCompact = false;
                break;
            case WindowViewMode.Compact:
                IsCompact = true;
                IsFullscreen = false;
                break;
            case WindowViewMode.FullScreen:
                IsFullscreen = true;
                IsCompact = false;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void OnCoordinatorCanNavigateChanged(object? sender, EventArgs e)
    {
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task Next() => await _coordinator.NextAsync();

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task Previous() => await _coordinator.PreviousAsync();

    private bool CanGoNext() => _coordinator.CanNext();

    private bool CanGoPrevious() => _coordinator.CanPrevious();

    [RelayCommand]
    private void ResetMediaPlayback()
    {
        if (MediaPlayer is null) return;
        TimeSpan pos = MediaPlayer.Position;
        _coordinator.ResetCurrentItem();
        _dispatcherQueue.TryEnqueue(() =>
        {
            MediaPlayer.Play();
            MediaPlayer.Position = pos;
        });
    }

    [RelayCommand]
    private void SetPlaybackRate(double rate)
    {
        PlaybackRate = rate;
    }

    partial void OnPlayerShowChaptersChanged(bool value)
    {
        _settingsService.PlayerShowChapters = value;
        Messenger.Send(new SettingsChangedMessage(nameof(ISettingsService.PlayerShowChapters), typeof(PlayerControlsViewModel)));
    }

    [RelayCommand]
    private void ToggleShowChapters()
    {
        PlayerShowChapters = !PlayerShowChapters;
    }

    [RelayCommand]
    private void SetAspectRatio(string aspect)
    {
        switch (aspect)
        {
            case "Fit":
                _aspectRatio = new Size(0, 0);
                break;
            case "Fill":
                _aspectRatio = new Size(double.NaN, double.NaN);
                break;
            default:
                string[] values = aspect.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != 2) return;
                if (!double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)) return;
                if (!double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)) return;
                _aspectRatio = new Size(width, height);
                break;
        }

        Messenger.Send(new ChangeAspectRatioMessage(_aspectRatio));
    }

    [RelayCommand]
    private async Task ToggleCompactLayoutAsync()
    {
        if (IsCompact)
        {
            await _windowService.TryExitCompactLayoutAsync();
        }
        else if (MediaPlayer?.NaturalVideoHeight > 0)
        {
            double aspectRatio = MediaPlayer.NaturalVideoWidth / (double)MediaPlayer.NaturalVideoHeight;
            await _windowService.TryEnterCompactLayoutAsync(new Size(240 * aspectRatio, 240));
        }
        else
        {
            await _windowService.TryEnterCompactLayoutAsync(new Size(240, 240));
        }
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        if (IsCompact) return;
        if (IsFullscreen)
        {
            _windowService.ExitFullScreen();
        }
        else
        {
            _windowService.TryEnterFullScreen();
        }
    }

    [RelayCommand]
    private void GoBackToMiniplayer()
    {
        Messenger.Send(new TogglePlayerVisibilityMessage());
    }

    [RelayCommand(CanExecute = nameof(HasActiveItem))]
    private void PlayPause()
    {
        PlayPauseWithBadge();
    }

    private void PlayPauseInternal()
    {
        if (IsPlaying)
        {
            MediaPlayer?.Pause();
        }
        else
        {
            MediaPlayer?.Play();
        }
    }

    /// <summary>
    /// Saves a snapshot of the current video frame to the Pictures library.
    /// Sends a <see cref="FailedToSaveFrameNotificationMessage"/> on failure.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasVideo))]
    private async Task SaveSnapshotAsync()
    {
        if (MediaPlayer?.PlaybackState is not (MediaPlaybackState.Paused or MediaPlaybackState.Playing)) return;
        try
        {
            StorageFile file = await SaveSnapshotInternalAsync(MediaPlayer);
            Messenger.Send(new RaiseFrameSavedNotificationMessage(file));
        }
        catch (UnauthorizedAccessException)
        {
            Messenger.Send(new RaiseLibraryAccessDeniedNotificationMessage(KnownLibraryId.Pictures));
        }
        catch (Exception e)
        {
            Messenger.Send(new FailedToSaveFrameNotificationMessage(e.Message));
        }
    }

    private static async Task<StorageFile> SaveSnapshotInternalAsync(IMediaPlayer mediaPlayer)
    {
        if (mediaPlayer is not MpvMediaPlayer player)
        {
            throw new NotImplementedException("Not supported on non mpv players");
        }

        StorageFolder tempFolder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(
            $"snapshot_{DateTimeOffset.Now.Ticks}",
            CreationCollisionOption.FailIfExists);

        try
        {
            string snapshotPath = System.IO.Path.Combine(tempFolder.Path, "snapshot.png");
            if (!player.TakeSnapshot(snapshotPath))
                throw new Exception("mpv failed to save snapshot");

            StorageFile file = await StorageFile.GetFileFromPathAsync(snapshotPath);
            StorageLibrary pictureLibrary = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures);
            StorageFolder defaultSaveFolder = pictureLibrary.SaveFolder;
            StorageFolder destFolder =
                await defaultSaveFolder.CreateFolderAsync("Odeon",
                    CreationCollisionOption.OpenIfExists);
            return await file.CopyAsync(destFolder, $"Odeon_{DateTimeOffset.Now:yyyyMMdd_HHmmss}{file.FileType}",
                NameCollisionOption.GenerateUniqueName);
        }
        finally
        {
            await tempFolder.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
    }
}
