#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Odeon.Core.Playback;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace Odeon.Core.Services;

public sealed class PlayerService : IPlayerService
{
    private readonly IVlcDialogService _vlcDialogService;
    private readonly bool _useFal;
    private readonly Dictionary<string, int> _tokenReferences = new();

    public PlayerService(IVlcDialogService vlcDialogService)
    {
        _vlcDialogService = vlcDialogService;

        // FutureAccessList is preferred because it can handle network StorageFiles
        // If FutureAccessList is somehow unavailable, SharedStorageAccessManager will be the fallback
        _useFal = true;

        try
        {
            // Clear FA periodically because of 1000 items limit
            // Delete any entries with "media" metadata to avoid hitting the limit with stale entries
            var tokensToRemove = StorageApplicationPermissions.FutureAccessList.Entries
                .Where(entry => entry.Metadata == "media")
                .Select(entry => entry.Token)
                .ToList();
            foreach (var token in tokensToRemove)
            {
                StorageApplicationPermissions.FutureAccessList.Remove(token);
            }
        }
        catch (Exception)   // FileNotFoundException
        {
            // FutureAccessList is not available
            _useFal = false;
        }
    }

    public IMediaPlayer Initialize(string[] swapChainOptions)
    {
        // Register the bundled font with Windows BEFORE LibVLC starts.
        // This ensures libass (which builds its font cache at initialization) sees the font.
        Odeon.Core.Helpers.PrivateFontRegistration.EnsureRegisteredAsync().GetAwaiter().GetResult();

        // Pre-warm font bytes into memory so subtitle rewriting doesn't wait on file I/O
        _ = Odeon.Core.Playback.PlaybackSubtitleTrackList.GetSubtitleFontBytesAsync();

        LibVLC lib = InitializeLibVlc(swapChainOptions);
        VlcMediaPlayer mediaPlayer = new(lib);
        return mediaPlayer;
    }


    public PlaybackItem CreatePlaybackItem(IMediaPlayer player, object source, params string[] options)
    {
        if (player is not VlcMediaPlayer vlcMediaPlayer)
            throw new NotSupportedException("Only VlcMediaPlayer is supported");
        Media media = CreateMedia(vlcMediaPlayer, source, options);
        return new PlaybackItem(source, media);
    }

    public void DisposePlaybackItem(PlaybackItem item)
    {
        DisposeMedia(item.Media);
    }

    public void DisposePlayer(IMediaPlayer player)
    {
        if (player is VlcMediaPlayer vlcMediaPlayer)
        {
            vlcMediaPlayer.VlcPlayer.Dispose();
            vlcMediaPlayer.LibVlc.Dispose();
        }
    }

    private Media CreateMedia(VlcMediaPlayer player, object source, params string[] options)
    {
        return source switch
        {
            IStorageFile file => CreateMedia(player, file, options),
            string str => CreateMedia(player, str, options),
            Uri uri => CreateMedia(player, uri, options),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    private Media CreateMedia(VlcMediaPlayer player, string str, params string[] options)
    {
        if (Uri.TryCreate(str, UriKind.Absolute, out Uri uri))
        {
            return CreateMedia(player, uri, options);
        }

        return new Media(player.LibVlc, str, FromType.FromPath, options);
    }

    private Media CreateMedia(VlcMediaPlayer player, IStorageFile file, params string[] options)
    {
        // NOTE: There have been reports of network locations not working when using the URI approach.
        // Optimization is disable until we can confirm that the issue is resolved in newer versions of LibVLC and/or Windows.
        if (file is StorageFile storageFile
            && storageFile.Provider.Id.Equals("network", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(storageFile.Path)
            && !_useFal
            && Uri.TryCreate(storageFile.Path, UriKind.Absolute, out var uri))
        {
            // Optimization for network files. Avoid having to deal with WinRT quirks.
            return CreateMedia(player, uri, options);
        }

        string token = IncrementRefCount(file);
        string mrl = "winrt://" + token;
        return new Media(player.LibVlc, mrl, FromType.FromLocation, options);
    }

    private Media CreateMedia(VlcMediaPlayer player, Uri uri, params string[] options)
    {
        return new Media(player.LibVlc, uri, options);
    }

    private void DisposeMedia(Media media)
    {
        string mrl = media.Mrl;
        if (mrl.StartsWith("winrt://"))
        {
            string token = mrl.Substring(8);
            try
            {
                DecrementRefCount(token);
            }
            catch (Exception e)
            {
                LogService.Log(e);
            }
        }

        media.Dispose();
    }

    private string IncrementRefCount(IStorageFile file)
    {
        string token = _useFal
            ? StorageApplicationPermissions.FutureAccessList.Add(file, "media")
            : SharedStorageAccessManager.AddFile(file);

        lock (_tokenReferences)
        {
            if (_tokenReferences.TryGetValue(token, out int refCount))
                _tokenReferences[token] = refCount + 1;
            else
                _tokenReferences[token] = 1;
        }

        return token;
    }

    private void DecrementRefCount(string token)
    {
        lock (_tokenReferences)
        {
            if (_tokenReferences.TryGetValue(token, out int refCount) && refCount > 1)
                _tokenReferences[token] = refCount - 1;
            else
            {
                _tokenReferences.Remove(token);

                if (_useFal)
                {
                    StorageApplicationPermissions.FutureAccessList.Remove(token);
                }
                else
                {
                    SharedStorageAccessManager.RemoveFile(token);
                }
            }
        }
    }

    private LibVLC InitializeLibVlc(string[] swapChainOptions)
    {
        // Pass the font FILE PATH directly to --freetype-font — VLC's freetype module
        // can load TTF files by absolute path, bypassing fontconfig which cannot see
        // GDI-registered private fonts (AddFontResourceEx).
        // EnsureRegisteredAsync() always runs before this so FontFilePath is populated.
        string userFontPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Fonts", "FuturaCyrillicMedium.ttf");

        string freetypeFont = File.Exists(userFontPath)
            ? userFontPath
            : "Futura PT Medium";

        List<string> options = new(swapChainOptions.Length + 7)
        {
#if DEBUG
            "--verbose=3",
#else
            "--verbose=0",
#endif
            "--no-osd",
            $"--freetype-font={freetypeFont}",
            "--freetype-rel-fontsize=28",
            "--freetype-outline-thickness=1",
            "--freetype-shadow-opacity=0",
            "--sub-margin=36",
        };
        options.AddRange(swapChainOptions);
#if DEBUG
        LibVLC libVlc = new(true, options.ToArray());
#else
        LibVLC libVlc = new(false, options.ToArray());
#endif
        LogService.RegisterLibVlcLogging(libVlc);
        _vlcDialogService.SetVlcDialogHandlers(libVlc);
        return libVlc;
    }
}
