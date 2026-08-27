#nullable enable

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Odeon.Core;
using Odeon.Core.Enums;
using Odeon.Core.Services;
using Odeon.Core.ViewModels;
using Odeon.Strings;
using Windows.Storage;

namespace Odeon.ViewModels;

public sealed partial class PropertyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _length = string.Empty;

    [ObservableProperty]
    private string _resolution = string.Empty;

    [ObservableProperty]
    private string _bitRate = string.Empty;

    [ObservableProperty]
    private string _fileSize = string.Empty;

    private readonly IFilesService _filesService;

    public PropertyViewModel(IFilesService filesService)
    {
        _filesService = filesService;
    }

    /// <summary>
    /// Loads detailed media information for the given <paramref name="media"/> item when the view loads.
    /// </summary>
    public async void OnLoaded(MediaViewModel media)
    {
        await media.LoadDetailsAsync(_filesService);
        UpdateProperties(media);
    }

    /// <summary>
    /// Populates the property values from the given <paramref name="media"/> item.
    /// </summary>
    public void UpdateProperties(MediaViewModel media)
    {
        bool isVideo = media.MediaType == MediaPlaybackType.Video;

        // Title
        Title = isVideo
            ? (string.IsNullOrEmpty(media.MediaInfo.VideoProperties.Title) ? media.Name : media.MediaInfo.VideoProperties.Title)
            : (string.IsNullOrEmpty(media.MediaInfo.MusicProperties.Title) ? media.Name : media.MediaInfo.MusicProperties.Title);

        // Length
        Length = isVideo
            ? Humanizer.ToDuration(media.MediaInfo.VideoProperties.Duration)
            : Humanizer.ToDuration(media.MediaInfo.MusicProperties.Duration);

        // Resolution (Only for Video and if width/height are valid)
        if (isVideo)
        {
            uint width = media.MediaInfo.VideoProperties.Width;
            uint height = media.MediaInfo.VideoProperties.Height;

            if ((width == 0 || height == 0) && media.Item.IsValueCreated && media.Item.Value?.VideoTracks != null)
            {
                foreach (var track in media.Item.Value.VideoTracks)
                {
                    if (track.Width > 0 && track.Height > 0)
                    {
                        width = track.Width;
                        height = track.Height;
                        break;
                    }
                }
            }

            Resolution = (width > 0 && height > 0) ? $"{width}x{height}" : string.Empty;
        }
        else
        {
            Resolution = string.Empty;
        }

        // Bit rate
        uint bitrate = isVideo
            ? media.MediaInfo.VideoProperties.Bitrate
            : media.MediaInfo.MusicProperties.Bitrate;
        BitRate = bitrate > 0 ? $"{bitrate / 1000} kbps" : string.Empty;

        // File size
        if (media.Source is StorageFile file)
        {
            FileSize = BytesToHumanReadable((long)media.MediaInfo.Size);
        }
        else
        {
            FileSize = string.Empty;
        }
    }

    // https://stackoverflow.com/a/11124118
    private static string BytesToHumanReadable(long byteCount)
    {
        // Get absolute value
        long absCount = byteCount < 0 ? -byteCount : byteCount;
        // Determine the suffix and readable value
        string suffix;
        double readable;
        if (absCount >= 0x1000000000000000) // Exabyte
        {
            suffix = "EB";
            readable = byteCount >> 50;
        }
        else if (absCount >= 0x4000000000000) // Petabyte
        {
            suffix = "PB";
            readable = byteCount >> 40;
        }
        else if (absCount >= 0x10000000000) // Terabyte
        {
            suffix = "TB";
            readable = byteCount >> 30;
        }
        else if (absCount >= 0x40000000) // Gigabyte
        {
            suffix = "GB";
            readable = byteCount >> 20;
        }
        else if (absCount >= 0x100000) // Megabyte
        {
            suffix = "MB";
            readable = byteCount >> 10;
        }
        else if (absCount >= 0x400) // Kilobyte
        {
            suffix = "KB";
            readable = byteCount;
        }
        else
        {
            return byteCount.ToString("0 B"); // Byte
        }
        // Divide by 1024 to get fractional value
        readable = readable / 1024;
        // Return formatted number with suffix
        return readable.ToString("0.## ") + suffix;
    }
}
