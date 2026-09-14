#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Odeon.Core.Helpers;
using Odeon.Core.Playback;
using Windows.Storage;

namespace Odeon.Core.Services;

public sealed class PlayerService : IPlayerService
{
    public PlayerService()
    {
    }

    public IMediaPlayer Initialize(string[]? swapChainOptions = null)
    {
        // Font registration is handled once at App startup (App.xaml.cs). Not repeated here.

        // 2. Build mpv initialization options
        string cacheDir = ApplicationData.Current.LocalCacheFolder.Path;
        string fontName = GetSubtitleFontName();

        bool overrideEnabled = true;
        bool subBackEnabled = false;
        int subBackOpacity = 75;
        bool subOutlineEnabled = true;
        int subtitleFontSize = SubtitleStyle.FontSize;
        int subtitlePosition = 100;
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue("OverrideSubtitleStyles", out object obVal) && obVal is bool b)
                overrideEnabled = b;
            if (values.TryGetValue("Player/SubtitleBackgroundEnabled", out object sbVal) && sbVal is bool sb)
                subBackEnabled = sb;
            if (values.TryGetValue("Player/SubtitleBackgroundOpacity", out object sboVal) && sboVal is int sbo && sbo >= 10 && sbo <= 100)
                subBackOpacity = sbo;
            if (values.TryGetValue("Player/SubtitleFontSize", out object sfsVal) && sfsVal is int sfs && sfs > 0)
                subtitleFontSize = sfs;
            if (values.TryGetValue("Player/SubtitlePosition", out object spVal) && spVal is int sp && sp >= 50 && sp <= 115)
                subtitlePosition = sp;
            if (values.TryGetValue("Player/SubtitleOutlineEnabled", out object soVal) && soVal is bool so)
                subOutlineEnabled = so;
        }
        catch
        {
            // Fallback if settings store is unreachable
        }

        double initialScale = subtitleFontSize / (double)SubtitleStyle.FontSize;
        int initialAlpha = (int)Math.Round((1.0 - (subBackOpacity / 100.0)) * 255.0);
        string initialAlphaHex = initialAlpha.ToString("X2");

        int mpvAlpha = (int)Math.Round((subBackOpacity / 100.0) * 255.0);
        string mpvAlphaHex = mpvAlpha.ToString("X2");
        string opacityColor = $"#{mpvAlphaHex}000000";

        int outline = subOutlineEnabled ? 1 : 0;
        string forceStyle = subBackEnabled
            ? $"Fontname={fontName},Fontsize={subtitleFontSize},BorderStyle=4,Outline={outline},Shadow=5,BackColour=&H{initialAlphaHex}000000"
            : $"Fontname={fontName},Fontsize={subtitleFontSize},BorderStyle=1,Outline={outline},Shadow={SubtitleStyle.ShadowDepth}";

        var options = new Dictionary<string, string>
        {
            ["sub-font"] = fontName,
            ["sub-font-size"] = overrideEnabled ? subtitleFontSize.ToString() : SubtitleStyle.FontSize.ToString(),
            ["sub-scale"] = overrideEnabled ? "1.0" : initialScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sub-border-size"] = subOutlineEnabled ? "1" : "0",
            ["sub-shadow-offset"] = subBackEnabled ? "5" : SubtitleStyle.ShadowDepth.ToString(),
            ["sub-margin-y"] = "36",
            ["sub-pos"] = subtitlePosition.ToString(),
            ["sub-fonts-dir"] = cacheDir,
            ["sub-ass-override"] = overrideEnabled ? "force" : "scale",
            ["sub-border-style"] = subBackEnabled ? "background-box" : "outline-and-shadow",
            ["sub-back-color"] = subBackEnabled ? opacityColor : "#00000000",
            ["sub-back-opacity"] = subBackOpacity.ToString(),
            ["sub-outline-enabled"] = subOutlineEnabled ? "yes" : "no",
            ["sub-ass-style-overrides"] = overrideEnabled ? forceStyle : "",
            ["osd-level"] = "0",
            ["vo"] = "libmpv",
            ["hwdec"] = "auto-copy",
            ["volume-max"] = "300.0"
        };

        // Add any extra arguments passed from settings or caller
        if (swapChainOptions != null && swapChainOptions.Length > 0)
        {
            foreach (var opt in swapChainOptions)
            {
                if (string.IsNullOrWhiteSpace(opt)) continue;
                string clean = opt.Trim();
                if (clean.StartsWith("--")) clean = clean.Substring(2);
                int eqIdx = clean.IndexOf('=');
                if (eqIdx > 0)
                {
                    string key = clean.Substring(0, eqIdx).Trim();
                    string val = clean.Substring(eqIdx + 1).Trim();
                    options[key] = val;
                }
                else
                {
                    options[clean] = "yes";
                }
            }
        }

        // 3. Create player (mpv_create + mpv_initialize with options)
        MpvMediaPlayer mediaPlayer = new(options);
        return mediaPlayer;
    }

    public PlaybackItem CreatePlaybackItem(IMediaPlayer player, object source, params string[] options)
    {
        // 4. Simplify: mpv accepts plain Win32 file paths; resolve file.Path directly
        string? path = ResolvePath(source);
        return new PlaybackItem(source, path);
    }

    public void DisposePlaybackItem(PlaybackItem item)
    {
        // 5. Simplify: mpv doesn't have separate Media objects to dispose
    }

    public void DisposePlayer(IMediaPlayer player)
    {
        (player as IDisposable)?.Dispose();
    }

    public static string? ResolvePath(object? source)
    {
        return source switch
        {
            IStorageFile file => file.Path,
            Uri uri => uri.IsFile ? uri.LocalPath : uri.AbsoluteUri,
            string str => str,
            _ => source?.ToString()
        };
    }

    private static string GetSubtitleFontName()
    {
        string cacheFontPath = Path.Combine(
            ApplicationData.Current.LocalCacheFolder.Path,
            "FuturaCyrillicMedium.ttf");

        if (File.Exists(cacheFontPath))
            return SubtitleStyle.FontFamily;

        string userFontPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Fonts", "FuturaCyrillicMedium.ttf");

        return File.Exists(userFontPath)
            ? SubtitleStyle.FontFamily
            : "Futura PT Medium";
    }
}
