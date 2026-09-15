#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Odeon.Core.Contexts;
using Odeon.Core.Enums;
using Odeon.Core.Helpers;
using Odeon.Core.Messages;
using Odeon.Core.Playback;
using Odeon.Core.Services;
using Windows.Storage;
using Windows.Media.Core;
using Windows.Storage.Search;

namespace Odeon.Core.ViewModels;

public sealed partial class CompositeTrackPickerViewModel : ObservableRecipient,
    IRecipient<QueueCurrentItemChangedMessage>,
    IRecipient<SubtitleAddedNotificationMessage>
{
    public ObservableCollection<string> SubtitleTracks { get; }

    public ObservableCollection<string> AudioTracks { get; }

    public ObservableCollection<string> VideoTracks { get; }

    private PlaybackSubtitleTrackList? ItemSubtitleTrackList => MediaPlayer?.PlaybackItem?.SubtitleTracks;

    private PlaybackAudioTrackList? ItemAudioTrackList => MediaPlayer?.PlaybackItem?.AudioTracks;

    private PlaybackVideoTrackList? ItemVideoTrackList => MediaPlayer?.PlaybackItem?.VideoTracks;

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    /// <summary>
    /// The currently selected subtitle track UI index.
    /// <list type="bullet">
    /// <item><description><c>0</c> = subtitles disabled (corresponds to the prepended "Disable" option in the UI).</description></item>
    /// <item><description><c>1</c> to <c>SubtitleTracks.Count</c> = the <c>SelectedIndex</c> of an enabled subtitle track in the UI; the
    /// actual underlying subtitle track index is typically obtained by subtracting <c>1</c> from this value.</description></item>
    /// </list>
    /// </summary>
    [ObservableProperty] private int _subtitleTrackIndex;

    /// <summary>
    /// The currently selected audio track index. <c>-1</c> means no track is selected.
    /// </summary>
    [ObservableProperty] private int _audioTrackIndex;

    /// <summary>
    /// The currently selected video track index. <c>-1</c> means no track is selected.
    /// </summary>
    [ObservableProperty] private int _videoTrackIndex;

    private readonly IFilesService _filesService;
    private readonly ISettingsService _settingsService;
    private readonly PlayerContext _playerContext;
    private bool _flyoutOpened;
    private string? _lastProcessedMediaKey;
    private IMediaPlayer? _currentHookedPlayer;
    private PlaybackItem? _currentSubscribedPlaybackItem;

    public CompositeTrackPickerViewModel(PlayerContext playerContext, IFilesService filesService,
        ISettingsService settingsService)
    {
        _filesService = filesService;
        _settingsService = settingsService;
        _playerContext = playerContext;
        SubtitleTracks = new ObservableCollection<string>();
        AudioTracks = new ObservableCollection<string>();
        VideoTracks = new ObservableCollection<string>();

        IsActive = true;

        _playerContext.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerContext.MediaPlayer))
            {
                HookMediaPlayer(_playerContext.MediaPlayer);
            }
        };
        HookMediaPlayer(_playerContext.MediaPlayer);
    }

    private void HookMediaPlayer(IMediaPlayer? player)
    {
        if (_currentHookedPlayer == player) return;
        if (_currentHookedPlayer != null)
        {
            _currentHookedPlayer.PlaybackItemChanged -= OnPlaybackItemChanged;
        }
        _currentHookedPlayer = player;
        if (_currentHookedPlayer != null)
        {
            _currentHookedPlayer.PlaybackItemChanged += OnPlaybackItemChanged;
            SubscribeToPlaybackItem(_currentHookedPlayer.PlaybackItem);
        }
        else
        {
            SubscribeToPlaybackItem(null);
        }
    }

    private void OnPlaybackItemChanged(IMediaPlayer sender, Events.ValueChangedEventArgs<PlaybackItem?> args)
    {
        SubscribeToPlaybackItem(args.NewValue);
    }

    private void SubscribeToPlaybackItem(PlaybackItem? item)
    {
        if (_currentSubscribedPlaybackItem == item) return;

        if (_currentSubscribedPlaybackItem != null)
        {
            _currentSubscribedPlaybackItem.SubtitleTracks.TrackListChanged -= OnSubtitleTracksChanged;
            _currentSubscribedPlaybackItem.SubtitleTracks.SelectedIndexChanged -= OnSubtitleSelectedIndexChanged;
            _currentSubscribedPlaybackItem.AudioTracks.TrackListChanged -= OnAudioTracksChanged;
            _currentSubscribedPlaybackItem.AudioTracks.SelectedIndexChanged -= OnAudioSelectedIndexChanged;
            _currentSubscribedPlaybackItem.VideoTracks.TrackListChanged -= OnVideoTracksChanged;
            _currentSubscribedPlaybackItem.VideoTracks.SelectedIndexChanged -= OnVideoSelectedIndexChanged;
        }

        _currentSubscribedPlaybackItem = item;

        if (_currentSubscribedPlaybackItem != null)
        {
            _currentSubscribedPlaybackItem.SubtitleTracks.TrackListChanged += OnSubtitleTracksChanged;
            _currentSubscribedPlaybackItem.SubtitleTracks.SelectedIndexChanged += OnSubtitleSelectedIndexChanged;
            _currentSubscribedPlaybackItem.AudioTracks.TrackListChanged += OnAudioTracksChanged;
            _currentSubscribedPlaybackItem.AudioTracks.SelectedIndexChanged += OnAudioSelectedIndexChanged;
            _currentSubscribedPlaybackItem.VideoTracks.TrackListChanged += OnVideoTracksChanged;
            _currentSubscribedPlaybackItem.VideoTracks.SelectedIndexChanged += OnVideoSelectedIndexChanged;
        }

        UpdateSubtitleTrackList();
        UpdateAudioTrackList();
        UpdateVideoTrackList();
        SubtitleTrackIndex = (_currentSubscribedPlaybackItem?.SubtitleTracks.SelectedIndex + 1) ?? 0;
        AudioTrackIndex = _currentSubscribedPlaybackItem?.AudioTracks.SelectedIndex ?? -1;
        VideoTrackIndex = _currentSubscribedPlaybackItem?.VideoTracks.SelectedIndex ?? -1;
    }

    private void OnSubtitleTracksChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        UpdateSubtitleTrackList();
        SubtitleTrackIndex = (ItemSubtitleTrackList?.SelectedIndex + 1) ?? 0;
    }

    private void OnSubtitleSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        int newIndex = (sender.SelectedIndex + 1);
        if (SubtitleTrackIndex != newIndex)
        {
            SubtitleTrackIndex = newIndex;
        }
    }

    private void OnAudioTracksChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        UpdateAudioTrackList();
        AudioTrackIndex = ItemAudioTrackList?.SelectedIndex ?? -1;
    }

    private void OnAudioSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        int newIndex = sender.SelectedIndex;
        if (AudioTrackIndex != newIndex)
        {
            AudioTrackIndex = newIndex;
        }
    }

    private void OnVideoTracksChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        UpdateVideoTrackList();
        VideoTrackIndex = ItemVideoTrackList?.SelectedIndex ?? -1;
    }

    private void OnVideoSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
    {
        int newIndex = sender.SelectedIndex;
        if (VideoTrackIndex != newIndex)
        {
            VideoTrackIndex = newIndex;
        }
    }

    public void Receive(SubtitleAddedNotificationMessage message)
    {
        UpdateSubtitleTrackList();
        SubtitleTrackIndex = (ItemSubtitleTrackList?.SelectedIndex + 1) ?? 0;
    }

    /// <summary>
    /// Try load a subtitle in the same directory with the same name
    /// </summary>
    public async void Receive(QueueCurrentItemChangedMessage message)
    {
        if (MediaPlayer is not MpvMediaPlayer player) return;
        if (message.Value is not { Source: StorageFile file, MediaType: MediaPlaybackType.Video } media)
            return;

        string fileKey = file.Path ?? file.Name;
        if (_lastProcessedMediaKey == fileKey) return;
        _lastProcessedMediaKey = fileKey;

        var playbackSubtitleTrackList = media.Item.Value?.SubtitleTracks;
        if (playbackSubtitleTrackList == null) return;

        // 1. Auto-discover neighboring subtitles in same folder
        IReadOnlyList<StorageFile> subtitles = await GetSubtitlesForFile(file, message.NeighboringFilesQuery);
        if (player.PlaybackItem != media.Item.Value) return;

        foreach (StorageFile subtitleFile in subtitles)
        {
            playbackSubtitleTrackList.AddExternalSubtitle(player, subtitleFile, null, false);
        }

        // 2. Restore any remembered external subtitle path for this file
        try
        {
            string extSubKey = $"MediaExtSub_{fileKey.GetHashCode():X8}";
            if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue(extSubKey, out object val) && val is string extPath && !string.IsNullOrEmpty(extPath))
            {
                if (System.IO.File.Exists(extPath))
                {
                    StorageFile extFile = await StorageFile.GetFileFromPathAsync(extPath);
                    if (player.PlaybackItem != media.Item.Value) return;

                    if (extFile != null)
                    {
                        playbackSubtitleTrackList.AddExternalSubtitle(player, extFile, null, false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }

        if (player.PlaybackItem != media.Item.Value) return;
        TrySetSubtitleFromLanguage(playbackSubtitleTrackList, _settingsService.PersistentSubtitleLanguage);

        var playbackAudioTrackList = media.Item.Value?.AudioTracks;
        if (playbackAudioTrackList != null)
        {
            // 1. Auto-discover neighboring audio files in same folder
            IReadOnlyList<StorageFile> audioFiles = await GetAudioTracksForFile(file, message.NeighboringFilesQuery);
            if (player.PlaybackItem != media.Item.Value) return;

            foreach (StorageFile audioFile in audioFiles)
            {
                playbackAudioTrackList.AddExternalAudio(player, audioFile, false);
            }

            // 2. Restore any remembered external audio path for this file
            try
            {
                string extAudioKey = $"MediaExtAudio_{fileKey.GetHashCode():X8}";
                if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue(extAudioKey, out object aVal) && aVal is string aExtPath && !string.IsNullOrEmpty(aExtPath))
                {
                    if (System.IO.File.Exists(aExtPath))
                    {
                        StorageFile extAudioFile = await StorageFile.GetFileFromPathAsync(aExtPath);
                        if (player.PlaybackItem != media.Item.Value) return;

                        if (extAudioFile != null)
                        {
                            playbackAudioTrackList.AddExternalAudio(player, extAudioFile, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }

            if (player.PlaybackItem != media.Item.Value) return;
            TrySetAudioFromLanguage(playbackAudioTrackList, _settingsService.PersistentAudioLanguage);
        }
    }

    private static void TrySetSubtitleFromLanguage(PlaybackSubtitleTrackList subtitleTrackList, string persistentLanguage)
    {
        if (!string.IsNullOrEmpty(persistentLanguage))
        {
            if (persistentLanguage.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                persistentLanguage.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                subtitleTrackList.SelectedIndex = -1;
                return;
            }

            if (subtitleTrackList.Count == 1)
            {
                subtitleTrackList.SelectedIndex = 0;
                return;
            }

            var langPreferences = persistentLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string language in langPreferences)
            {
                string cleanLang = language.Trim();
                for (int i = 0; i < subtitleTrackList.Count; i++)
                {
                    var subtitleTrack = subtitleTrackList[i];
                    if (cleanLang.Equals(subtitleTrack.LanguageTag, StringComparison.OrdinalIgnoreCase) ||
                        cleanLang.Equals(subtitleTrack.Language, StringComparison.OrdinalIgnoreCase) ||
                        cleanLang.Equals(subtitleTrack.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        subtitleTrackList.SelectedIndex = i;
                        return;
                    }
                }
            }
        }
    }

    private static void TrySetAudioFromLanguage(PlaybackAudioTrackList audioTrackList, string persistentLanguage)
    {
        if (!string.IsNullOrEmpty(persistentLanguage))
        {
            if (persistentLanguage.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                persistentLanguage.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                audioTrackList.SelectedIndex = -1;
                return;
            }

            if (audioTrackList.Count == 1)
            {
                audioTrackList.SelectedIndex = 0;
                return;
            }

            var langPreferences = persistentLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string language in langPreferences)
            {
                string cleanLang = language.Trim();
                for (int i = 0; i < audioTrackList.Count; i++)
                {
                    var audioTrack = audioTrackList[i];
                    if (cleanLang.Equals(audioTrack.LanguageTag, StringComparison.OrdinalIgnoreCase) ||
                        cleanLang.Equals(audioTrack.Language, StringComparison.OrdinalIgnoreCase) ||
                        cleanLang.Equals(audioTrack.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        audioTrackList.SelectedIndex = i;
                        return;
                    }
                }
            }
        }
    }

    private async Task<IReadOnlyList<StorageFile>> GetSubtitlesForFile(StorageFile sourceFile, StorageFileQueryResult? neighboringFilesQuery = null)
    {
        IReadOnlyList<StorageFile> subtitles = Array.Empty<StorageFile>();
        string rawName = Path.GetFileNameWithoutExtension(sourceFile.Name);

        // 1. Define your separators
        char[] separators = [' ', '.', '_', '-', '[', ']', '(', ')', '{', '}', ',', ';', '"', '\''];

        // 2. Break the name into tokens, removing empty entries to avoid double wildcards (**)
        string[] tokens = rawName.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0) return subtitles;

        // If we have a neighboring files query from the playlist, use it and filter for subtitles
        if (neighboringFilesQuery != null)
        {
            try
            {
                var escapedTokens = tokens.Select(token => Regex.Escape(token)).ToList();

                // STRATEGY A: Strict "Skeleton" Match
                var strictRegexPattern = "^" + string.Join(".*", escapedTokens) + ".*$";
                IReadOnlyList<StorageFile> files = await neighboringFilesQuery.GetFilesAsync(0, 50);
                subtitles = files.Where(f =>
                       f.IsSupportedSubtitle() && Regex.IsMatch(f.Name, strictRegexPattern, RegexOptions.IgnoreCase))
                    .ToArray();
                if (subtitles.Count == 0 && tokens.Length > 1)
                {
                    // STRATEGY B: Fallback (Partial Tokens Match)
                    var fallbackPattern = "^" + string.Join(".*", escapedTokens.Take(Math.Min(escapedTokens.Count - 1, 3))) + ".*$";
                    subtitles = files.Where(f =>
                            f.IsSupportedSubtitle() && Regex.IsMatch(f.Name, fallbackPattern, RegexOptions.IgnoreCase))
                        .ToArray();
                }
            }
            catch (Exception e)
            {
                LogService.Log(e);
            }
        }
        else
        {
            // Fallback to creating a new query with subtitle filter

            // STRATEGY A: Strict "Skeleton" Match
            // "Iron.Man.2008" -> "Iron*Man*2008*"
            string strictPattern = string.Join("*", tokens) + "*";

            QueryOptions options = new(CommonFileQuery.DefaultQuery, FilesHelpers.SupportedSubtitleFormats)
            {
                ApplicationSearchFilter = $"System.FileName:~\"{strictPattern}\""
            };

            var query = await _filesService.GetNeighboringFilesQueryAsync(sourceFile, options);
            if (query != null)
            {
                subtitles = await query.GetFilesAsync(0, 50);

                // STRATEGY B: Fallback (Partial Tokens Match)
                // If "Iron*Man*2008*" fails, try "Iron*Man*"
                if (subtitles.Count == 0 && tokens.Length > 1)
                {
                    string fallbackPattern = string.Join("*", tokens.Take(Math.Min(tokens.Length - 1, 3))) + "*";
                    options.ApplicationSearchFilter = $"System.FileName:~\"{fallbackPattern}\"";
                    query.ApplyNewQueryOptions(options);
                    subtitles = await query.GetFilesAsync(0, 50);
                }
            }
        }

        return subtitles;
    }

    private async Task<IReadOnlyList<StorageFile>> GetAudioTracksForFile(StorageFile sourceFile, StorageFileQueryResult? neighboringFilesQuery = null)
    {
        IReadOnlyList<StorageFile> audioFiles = Array.Empty<StorageFile>();
        string rawName = Path.GetFileNameWithoutExtension(sourceFile.Name);

        char[] separators = [' ', '.', '_', '-', '[', ']', '(', ')', '{', '}', ',', ';', '"', '\''];
        string[] tokens = rawName.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0) return audioFiles;

        if (neighboringFilesQuery != null)
        {
            try
            {
                var escapedTokens = tokens.Select(token => Regex.Escape(token)).ToList();
                var strictRegexPattern = "^" + string.Join(".*", escapedTokens) + ".*$";
                IReadOnlyList<StorageFile> files = await neighboringFilesQuery.GetFilesAsync(0, 50);
                audioFiles = files.Where(f =>
                       f.IsSupportedAudio() && Regex.IsMatch(f.Name, strictRegexPattern, RegexOptions.IgnoreCase))
                    .ToArray();
                if (audioFiles.Count == 0 && tokens.Length > 1)
                {
                    var fallbackPattern = "^" + string.Join(".*", escapedTokens.Take(Math.Min(escapedTokens.Count - 1, 3))) + ".*$";
                    audioFiles = files.Where(f =>
                            f.IsSupportedAudio() && Regex.IsMatch(f.Name, fallbackPattern, RegexOptions.IgnoreCase))
                        .ToArray();
                }
            }
            catch (Exception e)
            {
                LogService.Log(e);
            }
        }
        else
        {
            string strictPattern = string.Join("*", tokens) + "*";
            QueryOptions options = new(CommonFileQuery.DefaultQuery, FilesHelpers.SupportedAudioFormats)
            {
                ApplicationSearchFilter = $"System.FileName:~\"{strictPattern}\""
            };

            var query = await _filesService.GetNeighboringFilesQueryAsync(sourceFile, options);
            if (query != null)
            {
                audioFiles = await query.GetFilesAsync(0, 50);

                if (audioFiles.Count == 0 && tokens.Length > 1)
                {
                    string fallbackPattern = string.Join("*", tokens.Take(Math.Min(tokens.Length - 1, 3))) + "*";
                    options.ApplicationSearchFilter = $"System.FileName:~\"{fallbackPattern}\"";
                    query.ApplyNewQueryOptions(options);
                    audioFiles = await query.GetFilesAsync(0, 50);
                }
            }
        }

        return audioFiles;
    }

    partial void OnSubtitleTrackIndexChanged(int value)
    {
        if (!_flyoutOpened) return;
        if (ItemSubtitleTrackList == null) return;

        // VM index 0 maps to actual track index -1, which is "Disable"
        // Decrement value by 1 to convert from display index to actual subtitle track index
        value = Math.Max(-1, value - 1);
        if (value >= ItemSubtitleTrackList.Count) return;
        if (ItemSubtitleTrackList.SelectedIndex != value)
            ItemSubtitleTrackList.SelectedIndex = value;

        string mediaKey = (MediaPlayer as MpvMediaPlayer)?.PlaybackItem?.FilePath ?? string.Empty;
        string perMediaKey = !string.IsNullOrEmpty(mediaKey) ? $"MediaSubTrack_{mediaKey.GetHashCode():X8}" : string.Empty;

        if (value < 0)
        {
            _settingsService.PersistentSubtitleLanguage = "none";
            if (!string.IsNullOrEmpty(perMediaKey))
            {
                try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[perMediaKey] = "none"; } catch { }
            }
        }
        else if (value < ItemSubtitleTrackList.Count)
        {
            var subtitle = ItemSubtitleTrackList[value];
            string langPref = $"{subtitle.LanguageTag},{subtitle.Language},{LanguageHelper.GetPreferredLanguage().Substring(0, 2)}";
            if (!string.IsNullOrEmpty(subtitle.Title))
            {
                langPref = $"{subtitle.Title},{langPref}";
            }
            _settingsService.PersistentSubtitleLanguage = langPref;
            if (!string.IsNullOrEmpty(perMediaKey))
            {
                try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[perMediaKey] = $"{subtitle.TrackId},{subtitle.Title},{subtitle.LanguageTag},{subtitle.Language}"; } catch { }
            }
        }
    }

    partial void OnAudioTrackIndexChanged(int value)
    {
        if (!_flyoutOpened) return;
        if (ItemAudioTrackList != null && value >= 0 && value < ItemAudioTrackList.Count)
        {
            if (ItemAudioTrackList.SelectedIndex != value)
                ItemAudioTrackList.SelectedIndex = value;
        }

        string mediaKey = (MediaPlayer as MpvMediaPlayer)?.PlaybackItem?.FilePath ?? string.Empty;
        string perMediaKey = !string.IsNullOrEmpty(mediaKey) ? $"MediaAudioTrack_{mediaKey.GetHashCode():X8}" : string.Empty;

        if (value < 0)
        {
            _settingsService.PersistentAudioLanguage = "none";
            if (!string.IsNullOrEmpty(perMediaKey))
            {
                try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[perMediaKey] = "none"; } catch { }
            }
        }
        else if (ItemAudioTrackList != null && value < ItemAudioTrackList.Count)
        {
            var audioTrack = ItemAudioTrackList[value];
            string langPref = $"{audioTrack.LanguageTag},{audioTrack.Language}";
            if (!string.IsNullOrEmpty(audioTrack.Title))
            {
                langPref = $"{audioTrack.Title},{langPref}";
            }
            _settingsService.PersistentAudioLanguage = langPref;
            if (!string.IsNullOrEmpty(perMediaKey))
            {
                try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[perMediaKey] = $"{audioTrack.TrackId},{audioTrack.Title},{audioTrack.LanguageTag},{audioTrack.Language}"; } catch { }
            }
        }
    }

    partial void OnVideoTrackIndexChanged(int value)
    {
        if (!_flyoutOpened) return;
        if (ItemVideoTrackList != null && value >= 0 && value < ItemVideoTrackList.Count)
        {
            if (ItemVideoTrackList.SelectedIndex != value)
                ItemVideoTrackList.SelectedIndex = value;
        }
    }

    /// <summary>
    /// Adds a subtitle file to the current media. Sends a <see cref="Core.Messages.FailedToLoadSubtitleNotificationMessage"/> on failure.
    /// </summary>
    [RelayCommand]
    private async Task AddSubtitleAsync()
    {
        try
        {
            if (ItemSubtitleTrackList == null || MediaPlayer is not MpvMediaPlayer player) return;
            StorageFile? file = await _filesService.PickFileAsync(FilesHelpers.SupportedSubtitleFormats.Add("*").ToArray());
            if (file == null) return;

            ItemSubtitleTrackList.AddExternalSubtitle(player, file, null, true);

            string mediaKey = player.PlaybackItem?.FilePath ?? string.Empty;
            if (!string.IsNullOrEmpty(mediaKey) && !string.IsNullOrEmpty(file.Path))
            {
                try
                {
                    string extSubKey = $"MediaExtSub_{mediaKey.GetHashCode():X8}";
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[extSubKey] = file.Path;
                }
                catch { }
            }

            UpdateSubtitleTrackList();
            SubtitleTrackIndex = (ItemSubtitleTrackList?.SelectedIndex + 1) ?? 0;

            Messenger.Send(new SubtitleAddedNotificationMessage(file));
        }
        catch (Exception e)
        {
            Messenger.Send(new FailedToLoadSubtitleNotificationMessage(e.Message));
        }
    }

    /// <summary>
    /// Adds an external audio file to the current media.
    /// </summary>
    [RelayCommand]
    private async Task AddAudioTrackAsync()
    {
        try
        {
            if (ItemAudioTrackList == null || MediaPlayer is not MpvMediaPlayer player) return;
            StorageFile? file = await _filesService.PickFileAsync(FilesHelpers.SupportedAudioFormats.Add("*").ToArray());
            if (file == null) return;

            ItemAudioTrackList.AddExternalAudio(player, file, true);

            string mediaKey = player.PlaybackItem?.FilePath ?? string.Empty;
            if (!string.IsNullOrEmpty(mediaKey) && !string.IsNullOrEmpty(file.Path))
            {
                try
                {
                    string extAudioKey = $"MediaExtAudio_{mediaKey.GetHashCode():X8}";
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[extAudioKey] = file.Path;
                }
                catch { }
            }

            Messenger.Send(new UpdateStatusMessage($"Audio: {file.Name}"));
        }
        catch (Exception e)
        {
            LogService.Log(e);
        }
    }


    public void OnFlyoutOpening()
    {
        UpdateSubtitleTrackList();
        UpdateAudioTrackList();
        UpdateVideoTrackList();
        SubtitleTrackIndex = (ItemSubtitleTrackList?.SelectedIndex + 1) ?? 0;
        AudioTrackIndex = ItemAudioTrackList?.SelectedIndex ?? -1;
        VideoTrackIndex = ItemVideoTrackList?.SelectedIndex ?? -1;

        _flyoutOpened = true;
    }

    public void OnFlyoutClosed()
    {
        _flyoutOpened = false;
    }

    private void UpdateAudioTrackList()
    {
        if (ItemAudioTrackList == null) return;
        ItemAudioTrackList.Refresh();
        var trackLabels = ItemAudioTrackList.Select(track => track.Label).ToList();
        if (AudioTracks.SequenceEqual(trackLabels)) return;
        AudioTracks.SyncItems(trackLabels);
    }

    private void UpdateVideoTrackList()
    {
        if (ItemVideoTrackList == null) return;
        ItemVideoTrackList.Refresh();
        var trackLabels = ItemVideoTrackList.Select(track => track.Label).ToList();
        if (VideoTracks.SequenceEqual(trackLabels)) return;
        VideoTracks.SyncItems(trackLabels);
    }

    private void UpdateSubtitleTrackList()
    {
        if (ItemSubtitleTrackList == null) return;
        var trackLabels = ItemSubtitleTrackList.Select(track => track.Label).ToList();
        if (SubtitleTracks.SequenceEqual(trackLabels)) return;
        SubtitleTracks.SyncItems(trackLabels);
    }
}
