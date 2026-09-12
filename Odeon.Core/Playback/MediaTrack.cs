#nullable enable

using System;
using Odeon.Core.Helpers;
using Windows.Globalization;
using Windows.Media.Core;

namespace Odeon.Core.Playback;

public abstract class MediaTrack : IMediaTrack
{
    public string Id { get; internal set; }

    public string Label { get; set; }

    public string Language => _language?.DisplayName ?? _languageStr;

    public string LanguageTag => _language?.LanguageTag ?? string.Empty;

    private readonly Language? _language;
    private readonly string _languageStr;

    public MediaTrackKind TrackKind { get; }

    internal MediaTrack(MediaTrackKind trackKind, string language = "")
    {
        TrackKind = trackKind;
        _languageStr = language;
        Id = string.Empty;
        Label = string.Empty;
    }

    internal MediaTrack(MediaTrackKind trackKind, string id, string? title, string? language)
    {
        TrackKind = trackKind;
        Id = id;
        _languageStr = language ?? string.Empty;
        if (!string.IsNullOrEmpty(_languageStr) && Windows.Globalization.Language.IsWellFormed(_languageStr))
        {
            if (LanguageHelper.TryConvertISO6392ToISO6391(_languageStr, out string bc47Tag))
                _languageStr = bc47Tag;
            try
            {
                _language = new Language(_languageStr);
            }
            catch
            {
                // Fallback if tag cannot be parsed by WinRT Language
            }
        }

        Label = GetFullLabel(title, Language, id);
    }

    protected MediaTrack(IMediaTrack track)
    {
        _languageStr = track.Language;
        if (Windows.Globalization.Language.IsWellFormed(_languageStr))
        {
            _language = new Language(_languageStr);
        }

        Id = track.Id;
        Label = GetFullLabel(track.Label, Language, Id);
    }

    private static string GetFullLabel(string? label, string language, string? fallbackId = null)
    {
        if (string.IsNullOrEmpty(label))
        {
            label = !string.IsNullOrEmpty(language) ? language : (fallbackId != null ? $"Track {fallbackId}" : string.Empty);
        }
        else if (!string.IsNullOrEmpty(language) && language != label)
        {
            label = $"{label} ({language})";
        }

        return label ?? string.Empty;
    }
}
