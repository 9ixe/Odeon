using Odeon.Core.Enums;
using Windows.Media;

namespace Odeon.Core.Services;

public interface ISettingsService
{
    PlayerAutoResizeOption PlayerAutoResize { get; set; }
    bool UseIndexer { get; set; }
    bool PlayerShowControls { get; set; }
    bool PlayerShowChapters { get; set; }
    int PlayerControlsHideDelay { get; set; }
    int PersistentVolume { get; set; }
    string PersistentSubtitleLanguage { get; set; }
    string PersistentAudioLanguage { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the application's default
    /// subtitle style should override the styles embedded in ASS/SSA subtitle files.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the application forces its own font, size, and color
    /// settings onto ASS/SSA subtitles; otherwise, <see langword="false"/>.
    /// The default is <c>true</c>.
    /// </value>
    bool OverrideSubtitleStyles { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether a translucent background box
    /// should be rendered behind each subtitle line (YouTube-style).
    /// </summary>
    /// <value>
    /// <see langword="true"/> if a semi-transparent dark rectangle is drawn behind
    /// each subtitle line; otherwise, <see langword="false"/>.
    /// The default is <c>false</c>.
    /// </value>
    bool SubtitleBackgroundEnabled { get; set; }
    bool SubtitleOutlineEnabled { get; set; }

    /// <summary>
    /// Gets or sets the subtitle font size in points.
    /// The default is 50.
    /// </summary>
    int SubtitleFontSize { get; set; }

    /// <summary>
    /// Gets or sets the vertical subtitle position (percent of screen height).
    /// The default is 100 (standard bottom).
    /// </summary>
    int SubtitlePosition { get; set; }

    /// <summary>
    /// Gets or sets the opacity of the subtitle background box as a percentage (10-100).
    /// The default is 75.
    /// </summary>
    int SubtitleBackgroundOpacity { get; set; }

    bool ShowRecent { get; set; }
    ThemeOption Theme { get; set; }
    bool EnqueueAllFilesInFolder { get; set; }
    bool RestorePlaybackPosition { get; set; }
    bool SearchRemovableStorage { get; set; }
    int MaxVolume { get; set; }
    string GlobalArguments { get; set; }
    bool UseMultipleInstances { get; set; }
    MediaPlaybackAutoRepeatMode PersistentRepeatMode { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the playback position should be saved
    /// and restored between sessions.
    /// </summary>
    bool PersistPlaybackPosition { get; set; }

    /// <summary>
    /// Gets or sets the rewind step duration in seconds for the media player.
    /// </summary>
    /// <value>The duration in seconds to rewind the playback. The default is <c>5</c> seconds.</value>
    int PlayerRewindStep { get; set; }

    /// <summary>
    /// Gets or sets the fast forward step duration in seconds for the media player.
    /// </summary>
    /// <value>The duration in seconds to fast forward the playback. The default is <c>5</c> seconds.</value>
    int PlayerFastForwardStep { get; set; }

    /// <summary>
    /// Gets or sets the media command invoked by a tap gesture.
    /// </summary>
    /// <value>
    /// A value of the enumeration that specifies the media command invoked by
    /// a tap gesture.
    /// </value>
    PlaybackActionKind PlayerGestureTap { get; set; }

    /// <summary>
    /// Gets or sets the media command invoked by an upward swipe gesture.
    /// </summary>
    /// <value>
    /// A value of the enumeration that specifies the media command invoked
    /// by an upward swipe gesture.
    /// </value>
    PlaybackActionKind PlayerGestureSwipeUp { get; set; }

    /// <summary>
    /// Gets or sets the media command invoked by a downward swipe gesture.
    /// </summary>
    /// <value>
    /// A value of the enumeration that specifies the media command invoked by
    /// a downward swipe gesture.
    /// </value>
    PlaybackActionKind PlayerGestureSwipeDown { get; set; }

    /// <summary>
    /// Gets or sets the media command invoked by a left swipe gesture.
    /// </summary>
    /// <value>
    /// A value of the enumeration that specifies the media command invoked by
    /// a left swipe gesture.
    /// </value>
    PlaybackActionKind PlayerGestureSwipeLeft { get; set; }

    /// <summary>
    /// Gets or sets the media command invoked by a right swipe gesture.
    /// </summary>
    /// <value>
    /// A value of the enumeration that specifies the media command invoked by
    /// a right swipe gesture.
    /// </value>
    PlaybackActionKind PlayerGestureSwipeRight { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether vertical slide gestures
    /// (up/down) are enabled in the player.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if vertical slide gestures adjust playback volume;
    /// otherwise, <see langword="false"/>.
    /// </value>
    bool PlayerGestureSlideVertical { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether horizontal slide gestures
    /// (left/right) are enabled in the player.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if horizontal slide gestures seek the playback position;
    /// otherwise, <see langword="false"/>.
    /// </value>
    bool PlayerGestureSlideHorizontal { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the press and hold gesture
    /// is enabled in the player.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the press and hold gesture is enabled;
    /// otherwise, <see langword="false"/>.
    /// </value>
    bool PlayerGesturePressAndHold { get; set; }
}
