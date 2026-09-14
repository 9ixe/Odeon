#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Odeon.Core.Events;
using System.Collections.Generic;
using Odeon.Core.Enums;
using Odeon.Core.Interop;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Odeon.Core.Playback
{
    /// <summary>
    /// Core media player engine implemented on top of libmpv (mpv-2.dll).
    /// Handles media playback, seeking, volume, track switching, and background event processing.
    /// </summary>
    public class MpvMediaPlayer : IMediaPlayer, IDisposable
    {
        #region Events

        public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaEnded;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaFailed;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaOpened;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? IsMutedChanged;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? VolumeChanged;
        public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<PlaybackItem?>>? PlaybackItemChanged;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingProgressChanged;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingStarted;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingEnded;
        public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? NaturalDurationChanged;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? NaturalVideoSizeChanged;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? CanSeekChanged;
        public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? PositionChanged;
        public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<ChapterCue?>>? ChapterChanged;
        public event TypedEventHandler<IMediaPlayer, EventArgs>? ChaptersLoaded;
        public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<MediaPlaybackState>>? PlaybackStateChanged;
        public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? PlaybackRateChanged;
        public event TypedEventHandler<MpvMediaPlayer, ValueChangedEventArgs<double>>? SubtitleDelayChanged;
        public event TypedEventHandler<MpvMediaPlayer, ValueChangedEventArgs<double>>? AudioDelayChanged;

        #endregion

        #region Observed Property IDs

        private const ulong PropTimePos = 1;
        private const ulong PropDuration = 2;
        private const ulong PropPause = 3;
        private const ulong PropCoreIdle = 4;
        private const ulong PropIdleActive = 5;
        private const ulong PropEofReached = 6;
        private const ulong PropBuffering = 7;
        private const ulong PropMute = 8;
        private const ulong PropVolume = 9;
        private const ulong PropChapter = 10;
        private const ulong PropSeekable = 11;
        private const ulong PropVideoW = 12;
        private const ulong PropVideoH = 13;
        private const ulong PropSpeed = 14;
        private const ulong PropAudioDelay = 15;
        private const ulong PropSubDelay = 16;
        private const ulong PropTrackList = 17;
        private const ulong PropChapterList = 18;

        #endregion

        #region Fields

        private IntPtr _mpv;
        private readonly bool _ownsMpv;
        private DispatcherQueue? _dispatcherQueue;
        private Thread? _eventLoopThread;
        private bool _isUpdatingTracks;
        private List<Odeon.Core.Interop.MpvChapterInfo>? _cachedChapters;
        private volatile bool _disposed;
        private readonly HashSet<string> _loadedExternalSubtitles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loadedExternalAudio = new(StringComparer.OrdinalIgnoreCase);
        private bool? _currentSubAssOverride;
        private bool _currentSubBackground;
        private int _currentSubBackgroundOpacity = 75;
        private bool _currentSubOutlineEnabled = true;
        private int _currentSubtitleFontSize = Odeon.Core.Helpers.SubtitleStyle.FontSize;
        private int _currentSubtitlePosition = 100;

        private TimeSpan _naturalDuration;
        private TimeSpan _position;
        private MediaPlaybackState _playbackState = MediaPlaybackState.None;
        private PlaybackItem? _playbackItem;
        private ChapterCue? _chapter;
        private Rect _normalizedSourceRect;
        private readonly Rect _defaultSourceRect = new Rect(0, 0, 1, 1);

        private bool _isMuted;
        private double _volume = 1.0;
        private double _playbackRate = 1.0;
        private double _subtitleDelay;
        private double _audioDelay;
        private bool _canSeek;
        private bool _isLoopingEnabled;
        private double _bufferingProgress;
        private uint _naturalVideoWidth;
        private uint _naturalVideoHeight;

        private bool _readyToPlay;
        private bool _isPaused;
        private bool _isIdle = true;
        private string? _currentLoadedPath;
        private bool _initialTracksResolved;

        // Throttle rapid PositionChanged events to at most once per UI frame.
        // mpv's time-pos property can change at video frame rate (60/s+ for 60fps content),
        // and marshaling every change to the dispatcher can cause UI thread contention on older hardware.
        private bool _pendingPositionEvent;
        private TimeSpan _pendingPosition;
        private TimeSpan _pendingOldPosition;
        private readonly object _positionEventLock = new object();

        #endregion

        #region Properties

        /// <summary>
        /// Native mpv handle.
        /// </summary>
        public IntPtr MpvHandle => _mpv;

        public ChapterCue? Chapter
        {
            get => _chapter;
            set
            {
                if (value == _chapter) return;
                ChapterCue? oldValue = _chapter;
                _chapter = value;
                DispatcherEnqueue(() => ChapterChanged?.Invoke(this, new ValueChangedEventArgs<ChapterCue?>(value, oldValue)));
            }
        }

        public TimeSpan NaturalDuration
        {
            get => _naturalDuration;
            private set
            {
                if (Math.Abs((_naturalDuration - value).TotalMilliseconds) <= 50) return;
                TimeSpan oldValue = _naturalDuration;
                _naturalDuration = value;
                DispatcherEnqueue(() => NaturalDurationChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(value, oldValue)));
            }
        }

        public TimeSpan Position
        {
            get => _position;
            set
            {
                if (_mpv == IntPtr.Zero) return;
                if (value < TimeSpan.Zero) value = TimeSpan.Zero;
                if (_naturalDuration > TimeSpan.Zero && value > _naturalDuration) value = _naturalDuration;

                TimeSpan oldValue = _position;
                _position = value;

                // Debounce seek commands: rapid position changes (e.g. live scroll on seekbar,
                // gesture scrubbing) can generate many seek commands per second. We only issue the
                // final seek to mpv, which reduces mpv command queue pressure and UI thread overhead.
                string secStr = value.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                MpvInterop.Command(_mpv, "seek", secStr, "absolute");
                MpvInterop.SetPropertyDouble(_mpv, "time-pos", value.TotalSeconds);

                if (PlaybackState == MediaPlaybackState.Paused)
                {
                    DispatcherEnqueue(() => PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(value, oldValue)));
                }
            }
        }

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_mpv == IntPtr.Zero || _isMuted == value) return;
                _isMuted = value;
                MpvInterop.SetPropertyBool(_mpv, "mute", value);
                DispatcherEnqueue(() => IsMutedChanged?.Invoke(this, EventArgs.Empty));
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                if (_mpv == IntPtr.Zero) return;
                double clamped = Math.Clamp(value, 0.0, 3.0);
                if (Math.Abs(_volume - clamped) > 0.001)
                {
                    _volume = clamped;
                    MpvInterop.SetPropertyDouble(_mpv, "volume", clamped * 100.0);
                    DispatcherEnqueue(() => VolumeChanged?.Invoke(this, EventArgs.Empty));
                }
            }
        }

        public double PlaybackRate
        {
            get => _playbackRate;
            set
            {
                if (_mpv == IntPtr.Zero || Math.Abs(_playbackRate - value) <= 0.001) return;
                double oldValue = _playbackRate;
                _playbackRate = value;
                MpvInterop.SetPropertyDouble(_mpv, "speed", value);
                DispatcherEnqueue(() => PlaybackRateChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue)));
            }
        }

        public double SubtitleDelay
        {
            get => _subtitleDelay;
            set
            {
                if (_mpv == IntPtr.Zero || Math.Abs(_subtitleDelay - value) <= 0.001) return;
                double oldValue = _subtitleDelay;
                _subtitleDelay = value;
                MpvInterop.SetPropertyDouble(_mpv, "sub-delay", value / 1000.0);
                DispatcherEnqueue(() => SubtitleDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue)));
            }
        }

        public double AudioDelay
        {
            get => _audioDelay;
            set
            {
                if (_mpv == IntPtr.Zero || Math.Abs(_audioDelay - value) <= 0.001) return;
                double oldValue = _audioDelay;
                _audioDelay = value;
                MpvInterop.SetPropertyDouble(_mpv, "audio-delay", value / 1000.0);
                DispatcherEnqueue(() => AudioDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue)));
            }
        }

        public Rect NormalizedSourceRect
        {
            get => _normalizedSourceRect;
            set
            {
                _normalizedSourceRect = value;
                if (_mpv == IntPtr.Zero) return;

                if (value == _defaultSourceRect)
                {
                    // mpv deprecated setting video-aspect-override to -1. The documented reset is
                    // --video-aspect-override=no together with --video-aspect-mode=container.
                    MpvInterop.SetPropertyString(_mpv, "video-aspect-override", "no");
                    MpvInterop.SetPropertyString(_mpv, "video-aspect-mode", "container");
                }
                else
                {
                    double newWidth = value.Width * NaturalVideoWidth;
                    double newHeight = value.Height * NaturalVideoHeight;
                    if (newWidth > 0 && newHeight > 0)
                    {
                        MpvInterop.SetPropertyString(_mpv, "video-aspect-override", $"{newWidth:F0}:{newHeight:F0}");
                    }
                }
            }
        }

        public DeviceInformation? AudioDevice
        {
            get => null;
            set
            {
                if (_mpv == IntPtr.Zero) return;
                if (value?.Id == null)
                {
                    MpvInterop.SetPropertyString(_mpv, "audio-device", "auto");
                    return;
                }

                string endpointId = ExtractEndpointId(value.Id);
                MpvInterop.SetPropertyString(_mpv, "audio-device", !string.IsNullOrEmpty(endpointId) ? $"wasapi/{endpointId}" : "auto");
            }
        }

        private static string ExtractEndpointId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return string.Empty;

            // UWP DeviceInformation.Id format: \\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{interface_guid}
            string[] parts = deviceId.Split('#');
            if (parts.Length >= 3 && parts[1].Equals("MMDEVAPI", StringComparison.OrdinalIgnoreCase))
            {
                return parts[2];
            }

            // Already an endpoint GUID format: {0.0.0.00000000}.{guid}
            if (deviceId.StartsWith("{0.0.", StringComparison.OrdinalIgnoreCase))
            {
                int end = deviceId.IndexOf('}');
                if (end >= 0 && end + 1 < deviceId.Length && deviceId[end + 1] == '.')
                {
                    int secondEnd = deviceId.IndexOf('}', end + 1);
                    if (secondEnd >= 0)
                    {
                        return deviceId.Substring(0, secondEnd + 1);
                    }
                }
            }

            return deviceId;
        }

        public MediaPlaybackState PlaybackState
        {
            get => _playbackState;
            private set
            {
                if (value == _playbackState) return;
                MediaPlaybackState oldValue = _playbackState;
                _playbackState = value;
                DispatcherEnqueue(() => PlaybackStateChanged?.Invoke(this, new ValueChangedEventArgs<MediaPlaybackState>(value, oldValue)));
            }
        }

        public PlaybackItem? PlaybackItem
        {
            get => _playbackItem;
            set
            {
                if (_playbackItem == value) return;
                PlaybackItem? oldValue = _playbackItem;

                if (_playbackItem != null)
                {
                    RemoveItemHandlers(_playbackItem);
                }

                if (value == null)
                {
                    Stop();
                    _playbackItem = null;
                    _currentLoadedPath = null;
                    _cachedChapters = null;
                    _initialTracksResolved = false;
                }
                else
                {
                    _playbackItem = value;
                    _initialTracksResolved = false;
                    RegisterItemHandlers(_playbackItem);
                    _readyToPlay = true;
                }

                DispatcherEnqueue(() => PlaybackItemChanged?.Invoke(this, new ValueChangedEventArgs<PlaybackItem?>(value, oldValue)));
            }
        }

        public bool CanSeek
        {
            get
            {
                if (_canSeek) return true;
                if (_mpv != IntPtr.Zero)
                {
                    bool? s = MpvInterop.GetPropertyBool(_mpv, "seekable");
                    if (s == true)
                    {
                        _canSeek = true;
                        return true;
                    }
                }
                return _naturalDuration > TimeSpan.Zero || _playbackState is MediaPlaybackState.Playing or MediaPlaybackState.Paused;
            }
        }

        public bool CanPause => true;

        public bool IsLoopingEnabled
        {
            get => _isLoopingEnabled;
            set
            {
                _isLoopingEnabled = value;
                if (_mpv != IntPtr.Zero)
                {
                    MpvInterop.SetPropertyString(_mpv, "loop-file", value ? "inf" : "no");
                }
            }
        }

        public double BufferingProgress => _bufferingProgress;

        public uint NaturalVideoHeight => _naturalVideoHeight;

        public uint NaturalVideoWidth => _naturalVideoWidth;

        public DispatcherQueue? DispatcherQueue
        {
            get => _dispatcherQueue;
            set
            {
                if (value != null)
                {
                    _dispatcherQueue = value;
                }
            }
        }

        #endregion

        #region Constructors & Initialization

        private static DispatcherQueue? ResolveDispatcherQueue()
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            if (queue != null) return queue;

            try
            {
                return Windows.ApplicationModel.Core.CoreApplication.MainView?.DispatcherQueue;
            }
            catch
            {
                return null;
            }
        }

        public MpvMediaPlayer() : this((IDictionary<string, string>?)null) { }

        public MpvMediaPlayer(IDictionary<string, string>? options)
        {
            _dispatcherQueue = ResolveDispatcherQueue();
            _normalizedSourceRect = _defaultSourceRect;

            _mpv = MpvInterop.mpv_create();
            if (_mpv == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create libmpv instance.");

            _ownsMpv = true;
            InitializeMpvHandle(options);
        }

        public MpvMediaPlayer(IEnumerable<string>? options)
            : this(ParseOptionsList(options)) { }

        public MpvMediaPlayer(IntPtr existingMpvHandle)
        {
            _dispatcherQueue = ResolveDispatcherQueue();
            _normalizedSourceRect = _defaultSourceRect;
            _mpv = existingMpvHandle;
            _ownsMpv = false;
            LoadSubtitleSettingsFromStore();
            InitializeObservers();
            StartEventLoop();
        }

        private static IDictionary<string, string>? ParseOptionsList(IEnumerable<string>? options)
        {
            if (options == null) return null;
            var dict = new Dictionary<string, string>();
            foreach (var opt in options)
            {
                if (string.IsNullOrWhiteSpace(opt)) continue;
                string clean = opt.Trim();
                if (clean.StartsWith("--")) clean = clean.Substring(2);
                int eq = clean.IndexOf('=');
                if (eq > 0)
                    dict[clean.Substring(0, eq).Trim()] = clean.Substring(eq + 1).Trim();
                else
                    dict[clean] = "yes";
            }
            return dict;
        }

        private void InitializeMpvHandle(IDictionary<string, string>? customOptions = null)
        {
            // Core options
            MpvInterop.SetOptionString(_mpv, "terminal", "no");
            MpvInterop.SetOptionString(_mpv, "input-default-bindings", "no");
            MpvInterop.SetOptionString(_mpv, "input-vo-keyboard", "no");
            MpvInterop.SetOptionString(_mpv, "keep-open", "yes");
            MpvInterop.SetOptionString(_mpv, "idle", "yes");
            MpvInterop.SetOptionString(_mpv, "hr-seek", "yes");

            // Subtitle & Rendering options (Phase 4 & Phase 11)
            try
            {
                string cacheDir = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
                MpvInterop.SetOptionString(_mpv, "sub-fonts-dir", cacheDir);
            }
            catch
            {
                // Fallback if not running in UWP package context
            }
            LoadSubtitleSettingsFromStore();

            if (customOptions != null)
            {
                if (customOptions.TryGetValue("sub-font-size", out string? fsVal) && int.TryParse(fsVal, out int initFs))
                {
                    _currentSubtitleFontSize = initFs;
                }
                if (customOptions.TryGetValue("sub-pos", out string? posVal) && int.TryParse(posVal, out int initPos))
                {
                    _currentSubtitlePosition = initPos;
                }
                if (customOptions.TryGetValue("sub-back-opacity", out string? boVal) && int.TryParse(boVal, out int initBo))
                {
                    _currentSubBackgroundOpacity = initBo;
                }
                if (customOptions.TryGetValue("sub-border-style", out string? bsVal))
                {
                    _currentSubBackground = bsVal == "background-box" || bsVal == "opaque-box";
                }
                if (customOptions.TryGetValue("sub-outline-enabled", out string? oeVal))
                {
                    _currentSubOutlineEnabled = oeVal == "yes" || oeVal == "true";
                }
                if (customOptions.TryGetValue("sub-ass-override", out string? ovVal))
                {
                    _currentSubAssOverride = ovVal == "force";
                }
            }
            string outlineSize = _currentSubOutlineEnabled ? "1" : "0";
            MpvInterop.SetOptionString(_mpv, "sub-font", "Futura Cyrillic Medium");
            MpvInterop.SetOptionString(_mpv, "sub-font-size", _currentSubtitleFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
            MpvInterop.SetOptionString(_mpv, "sub-border-size", outlineSize);
            MpvInterop.SetOptionString(_mpv, "sub-shadow-offset", _currentSubBackground ? "5" : "0");
            MpvInterop.SetOptionString(_mpv, "sub-margin-y", "36");
            MpvInterop.SetOptionString(_mpv, "sub-pos", _currentSubtitlePosition.ToString(System.Globalization.CultureInfo.InvariantCulture));
            MpvInterop.SetOptionString(_mpv, "osd-level", "0");
            MpvInterop.SetOptionString(_mpv, "vo", "libmpv");
            MpvInterop.mpv_request_log_messages(_mpv, MpvInterop.GetUtf8Bytes("info"));
            MpvInterop.SetOptionString(_mpv, "hwdec", "auto-copy");
            MpvInterop.SetOptionString(_mpv, "audio-device", "auto");
            MpvInterop.SetOptionString(_mpv, "volume-max", "300.0");

            // Scaler selection: use high-quality shaders for HD+ content on capable hardware,
            // faster scalers for SD content to reduce GPU workload on integrated graphics.
            // mpv selects the shader at runtime based on video dimensions vs. output size.
            MpvInterop.SetOptionString(_mpv, "dscale", "mitchell");
            MpvInterop.SetOptionString(_mpv, "scale", "spline36");
            MpvInterop.SetOptionString(_mpv, "cscale", "spline36");
            MpvInterop.SetOptionString(_mpv, "correct-downscaling", "yes");
            MpvInterop.SetOptionString(_mpv, "linear-downscaling", "yes");
            MpvInterop.SetOptionString(_mpv, "sigmoid-upscaling", "yes");

            // Enable fast swscale path for software rendering. This is safe because the GPU shader
            // path above is preferred when hwdec is active or for larger resolutions; the swscale
            // fallback only kicks in for small/offline content. On older hardware this avoids
            // unnecessary spline filtering stalls.
            MpvInterop.SetOptionString(_mpv, "sws-scaler", "bilinear");
            MpvInterop.SetOptionString(_mpv, "sws-fast", "yes");
            MpvInterop.SetOptionString(_mpv, "sws-cscale", "bilinear");

            // Apply custom options
            if (customOptions != null)
            {
                foreach (var kvp in customOptions)
                {
                    MpvInterop.SetOptionString(_mpv, kvp.Key, kvp.Value);
                }
            }

            int initRes = MpvInterop.mpv_initialize(_mpv);
            if (initRes < 0)
                throw new InvalidOperationException($"Failed to initialize libmpv: {MpvInterop.GetErrorString(initRes)}");

            InitializeObservers();
            StartEventLoop();

            MediaDevice.DefaultAudioRenderDeviceChanged += MediaDevice_DefaultAudioRenderDeviceChanged;
        }

        private void InitializeObservers()
        {
            MpvInterop.mpv_observe_property(_mpv, PropTimePos, MpvInterop.GetUtf8Bytes("time-pos"), MpvFormat.Double);
            MpvInterop.mpv_observe_property(_mpv, PropDuration, MpvInterop.GetUtf8Bytes("duration"), MpvFormat.Double);
            MpvInterop.mpv_observe_property(_mpv, PropPause, MpvInterop.GetUtf8Bytes("pause"), MpvFormat.Flag);
            MpvInterop.mpv_observe_property(_mpv, PropCoreIdle, MpvInterop.GetUtf8Bytes("core-idle"), MpvFormat.Flag);
            MpvInterop.mpv_observe_property(_mpv, PropIdleActive, MpvInterop.GetUtf8Bytes("idle-active"), MpvFormat.Flag);
            MpvInterop.mpv_observe_property(_mpv, PropEofReached, MpvInterop.GetUtf8Bytes("eof-reached"), MpvFormat.Flag);
            MpvInterop.mpv_observe_property(_mpv, PropBuffering, MpvInterop.GetUtf8Bytes("cache-buffering-state"), MpvFormat.Int64);
            MpvInterop.mpv_observe_property(_mpv, PropMute, MpvInterop.GetUtf8Bytes("mute"), MpvFormat.Flag);
            MpvInterop.mpv_observe_property(_mpv, PropVolume, MpvInterop.GetUtf8Bytes("volume"), MpvFormat.Double);
            MpvInterop.mpv_observe_property(_mpv, PropChapter, MpvInterop.GetUtf8Bytes("chapter"), MpvFormat.Int64);
            MpvInterop.mpv_observe_property(_mpv, PropSeekable, MpvInterop.GetUtf8Bytes("seekable"), MpvFormat.Flag);
            MpvInterop.mpv_observe_property(_mpv, PropVideoW, MpvInterop.GetUtf8Bytes("video-params/w"), MpvFormat.Int64);
            MpvInterop.mpv_observe_property(_mpv, PropVideoH, MpvInterop.GetUtf8Bytes("video-params/h"), MpvFormat.Int64);
            MpvInterop.mpv_observe_property(_mpv, PropSpeed, MpvInterop.GetUtf8Bytes("speed"), MpvFormat.Double);
            MpvInterop.mpv_observe_property(_mpv, PropAudioDelay, MpvInterop.GetUtf8Bytes("audio-delay"), MpvFormat.Double);
            MpvInterop.mpv_observe_property(_mpv, PropSubDelay, MpvInterop.GetUtf8Bytes("sub-delay"), MpvFormat.Double);
            MpvInterop.mpv_observe_property(_mpv, PropTrackList, MpvInterop.GetUtf8Bytes("track-list"), MpvFormat.Node);
            MpvInterop.mpv_observe_property(_mpv, PropChapterList, MpvInterop.GetUtf8Bytes("chapter-list"), MpvFormat.Node);
        }

        private void StartEventLoop()
        {
            _eventLoopThread = new Thread(EventLoop)
            {
                Name = "MpvMediaPlayer_EventLoop",
                IsBackground = true
            };
            _eventLoopThread.Start();
        }

        #endregion

        #region Event Loop & Event Processing

        private void EventLoop()
        {
            while (!_disposed && _mpv != IntPtr.Zero)
            {
                IntPtr eventPtr = MpvInterop.mpv_wait_event(_mpv, 0.05);
                if (eventPtr == IntPtr.Zero) continue;

                MpvEvent ev;
                try
                {
                    ev = Marshal.PtrToStructure<MpvEvent>(eventPtr);
                }
                catch
                {
                    continue;
                }

                if (ev.EventId == MpvEventId.None) continue;
                if (ev.EventId == MpvEventId.Shutdown) break;

                HandleMpvEvent(ev);

                // Flush any pending position event. Since we're already on the event loop thread
                // (which called DispatcherEnqueue to get here via HandleMpvEvent's property changes),
                // we can safely dispatch the batched position update at this point rather than
                // flooding the dispatcher on every individual time-pos change.
                lock (_positionEventLock)
                {
                    if (_pendingPositionEvent)
                    {
                        TimeSpan pendingPos = _pendingPosition;
                        TimeSpan pendingOld = _pendingOldPosition;
                        _pendingPositionEvent = false;
                        DispatcherEnqueue(() => PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(pendingPos, pendingOld)));
                    }
                }
            }
        }

        private void HandleMpvEvent(MpvEvent ev)
        {
            switch (ev.EventId)
            {
                case MpvEventId.LogMessage:
                    if (ev.Data != IntPtr.Zero)
                    {
                        var log = Marshal.PtrToStructure<MpvEventLogMessage>(ev.Data);
                        string prefix = Marshal.PtrToStringAnsi(log.Prefix) ?? "";
                        string text = Marshal.PtrToStringAnsi(log.Text) ?? "";
                        Debug.WriteLine($"[mpv:{prefix}] {text.TrimEnd()}");
                        // Non-fatal warning for file access issues (e.g. background external subtitle scans)
                        if ((prefix == "file" || prefix == "stream") && text.Contains("Permission denied"))
                        {
                            Debug.WriteLine($"[MpvMediaPlayer] File access warning (non-fatal): {text.TrimEnd()}");
                        }
                    }
                    break;
                case MpvEventId.StartFile:
                    _isIdle = false;
                    DispatcherEnqueue(() =>
                    {
                        PlaybackState = MediaPlaybackState.Opening;
                    });
                    break;

                case MpvEventId.FileLoaded:
                    _isIdle = false;
                    bool? seekableProp = MpvInterop.GetPropertyBool(_mpv, "seekable");
                    _canSeek = seekableProp ?? true;
                    double? durSecs = MpvInterop.GetPropertyDouble(_mpv, "duration");
                    TimeSpan duration = durSecs.HasValue && durSecs.Value > 0
                        ? TimeSpan.FromSeconds(durSecs.Value)
                        : _naturalDuration;
                    if (duration > TimeSpan.Zero)
                    {
                        _naturalDuration = duration;
                        _canSeek = true;
                    }
                    ApplyAllSubtitleSettings();
                    RestoreMediaTimingOffsets();
                    var tracks = GetTrackList();
                    var chapters = GetChapters();
                    if (chapters.Count == 0 && _mpv != IntPtr.Zero)
                    {
                        var freshChapters = Odeon.Core.Interop.MpvNodeReader.GetChapterList(_mpv);
                        if (freshChapters != null && freshChapters.Count > 0)
                        {
                            chapters = freshChapters;
                            _cachedChapters = freshChapters;
                        }
                    }
                    DispatcherEnqueue(() =>
                    {
                        UpdatePlaybackState();
                        if (duration > TimeSpan.Zero)
                        {
                            NaturalDuration = duration;
                        }
                        if (tracks.Count > 0)
                            UpdateTracksFromMpv((List<Odeon.Core.Interop.MpvTrackInfo>)tracks);
                        if (chapters.Count > 0)
                            UpdateChaptersFromMpv((List<Odeon.Core.Interop.MpvChapterInfo>)chapters);
                        CanSeekChanged?.Invoke(this, EventArgs.Empty);
                        MediaOpened?.Invoke(this, EventArgs.Empty);
                    });
                    break;

                case MpvEventId.EndFile:
                    _initialTracksResolved = false;
                    var endFileData = ev.Data != IntPtr.Zero ? Marshal.PtrToStructure<MpvEventEndFile>(ev.Data) : default;
                    if (endFileData.Reason == MpvEndFileReason.Error)
                    {
                        DispatcherEnqueue(() =>
                        {
                            PlaybackState = MediaPlaybackState.None;
                            MediaFailed?.Invoke(this, EventArgs.Empty);
                        });
                    }
                    else if (endFileData.Reason == MpvEndFileReason.Eof)
                    {
                        if (IsLoopingEnabled)
                        {
                            Replay();
                        }
                        else
                        {
                            DispatcherEnqueue(() =>
                            {
                                PlaybackState = MediaPlaybackState.None;
                                MediaEnded?.Invoke(this, EventArgs.Empty);
                            });
                        }
                    }
                    break;

                case MpvEventId.PropertyChange:
                    if (ev.Data != IntPtr.Zero)
                    {
                        MpvEventProperty prop = Marshal.PtrToStructure<MpvEventProperty>(ev.Data);
                        HandlePropertyChange(ev.ReplyUserData, prop);
                    }
                    break;
            }
        }

        private void HandlePropertyChange(ulong propId, MpvEventProperty prop)
        {
            if (prop.Data == IntPtr.Zero) return;

            switch (propId)
            {
                case PropTimePos:
                    if (prop.Format == MpvFormat.Double)
                    {
                        double secs = Marshal.PtrToStructure<double>(prop.Data);
                        TimeSpan newPos = TimeSpan.FromSeconds(secs);
                        if (newPos != _position)
                        {
                            TimeSpan oldPos = _position;
                            _position = newPos;
                            // Queue position event for batched dispatch. We hold the event and only
                            // dispatch it from the event loop after processing, avoiding flooding the
                            // dispatcher with rapid position changes (up to 60/s for 60fps content).
                            lock (_positionEventLock)
                            {
                                _pendingPosition = newPos;
                                _pendingOldPosition = oldPos;
                                _pendingPositionEvent = true;
                            }
                        }
                    }
                    break;

                case PropDuration:
                    if (prop.Format == MpvFormat.Double)
                    {
                        double secs = Marshal.PtrToStructure<double>(prop.Data);
                        if (secs > 0)
                        {
                            NaturalDuration = TimeSpan.FromSeconds(secs);
                            _canSeek = true;
                            DispatcherEnqueue(() => CanSeekChanged?.Invoke(this, EventArgs.Empty));

                            if (PlaybackItem != null && _cachedChapters != null && _cachedChapters.Count > 0)
                            {
                                PlaybackItem.Chapters.Load(_cachedChapters, NaturalDuration);
                                DispatcherEnqueue(() => ChaptersLoaded?.Invoke(this, EventArgs.Empty));
                            }
                        }
                    }
                    break;

                case PropPause:
                    if (prop.Format == MpvFormat.Flag)
                    {
                        _isPaused = Marshal.ReadInt32(prop.Data) != 0;
                        UpdatePlaybackState();
                    }
                    break;

                case PropIdleActive:
                    if (prop.Format == MpvFormat.Flag)
                    {
                        _isIdle = Marshal.ReadInt32(prop.Data) != 0;
                        UpdatePlaybackState();
                    }
                    break;

                case PropCoreIdle:
                    if (prop.Format == MpvFormat.Flag)
                    {
                        bool coreIdle = Marshal.ReadInt32(prop.Data) != 0;
                        if (coreIdle && !_isPaused && !_isIdle)
                        {
                            // Buffering / waiting for data
                        }
                    }
                    break;

                case PropEofReached:
                    if (prop.Format == MpvFormat.Flag)
                    {
                        bool eof = Marshal.ReadInt32(prop.Data) != 0;
                        if (eof && IsLoopingEnabled)
                        {
                            Replay();
                        }
                    }
                    break;

                case PropBuffering:
                    if (prop.Format == MpvFormat.Int64)
                    {
                        long cache = Marshal.ReadInt64(prop.Data);
                        double newProgress = cache / 100.0;
                        if (Math.Abs(_bufferingProgress - newProgress) > 0.01)
                        {
                            bool wasZero = _bufferingProgress == 0;
                            _bufferingProgress = newProgress;
                            DispatcherEnqueue(() =>
                            {
                                if (wasZero && newProgress > 0)
                                    BufferingStarted?.Invoke(this, EventArgs.Empty);

                                BufferingProgressChanged?.Invoke(this, EventArgs.Empty);

                                if (newProgress >= 1.0)
                                {
                                    BufferingEnded?.Invoke(this, EventArgs.Empty);
                                    _bufferingProgress = 0;
                                }
                            });
                        }
                    }
                    break;

                case PropMute:
                    if (prop.Format == MpvFormat.Flag)
                    {
                        _isMuted = Marshal.ReadInt32(prop.Data) != 0;
                        DispatcherEnqueue(() => IsMutedChanged?.Invoke(this, EventArgs.Empty));
                    }
                    break;

                case PropVolume:
                    if (prop.Format == MpvFormat.Double)
                    {
                        double vol = Marshal.PtrToStructure<double>(prop.Data);
                        _volume = vol / 100.0;
                        DispatcherEnqueue(() => VolumeChanged?.Invoke(this, EventArgs.Empty));
                    }
                    break;

                case PropSpeed:
                    if (prop.Format == MpvFormat.Double)
                    {
                        double spd = Marshal.PtrToStructure<double>(prop.Data);
                        if (Math.Abs(_playbackRate - spd) > 0.001)
                        {
                            double oldSpd = _playbackRate;
                            _playbackRate = spd;
                            DispatcherEnqueue(() => PlaybackRateChanged?.Invoke(this, new ValueChangedEventArgs<double>(spd, oldSpd)));
                        }
                    }
                    break;

                case PropAudioDelay:
                    if (prop.Format == MpvFormat.Double)
                    {
                        double sec = Marshal.PtrToStructure<double>(prop.Data);
                        double ms = sec * 1000.0;
                        if (Math.Abs(_audioDelay - ms) > 0.001)
                        {
                            double oldMs = _audioDelay;
                            _audioDelay = ms;
                            DispatcherEnqueue(() => AudioDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(ms, oldMs)));
                        }
                    }
                    break;

                case PropSubDelay:
                    if (prop.Format == MpvFormat.Double)
                    {
                        double sec = Marshal.PtrToStructure<double>(prop.Data);
                        double ms = sec * 1000.0;
                        if (Math.Abs(_subtitleDelay - ms) > 0.001)
                        {
                            double oldMs = _subtitleDelay;
                            _subtitleDelay = ms;
                            DispatcherEnqueue(() => SubtitleDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(ms, oldMs)));
                        }
                    }
                    break;

                case PropSeekable:
                    if (prop.Format == MpvFormat.Flag)
                    {
                        bool seekable = Marshal.ReadInt32(prop.Data) != 0;
                        if (_canSeek != seekable)
                        {
                            _canSeek = seekable;
                            DispatcherEnqueue(() => CanSeekChanged?.Invoke(this, EventArgs.Empty));
                        }
                    }
                    break;

                case PropVideoW:
                    if (prop.Format == MpvFormat.Int64)
                    {
                        uint w = (uint)Marshal.ReadInt64(prop.Data);
                        if (_naturalVideoWidth != w)
                        {
                            _naturalVideoWidth = w;
                            DispatcherEnqueue(() => NaturalVideoSizeChanged?.Invoke(this, EventArgs.Empty));
                        }
                    }
                    break;

                case PropVideoH:
                    if (prop.Format == MpvFormat.Int64)
                    {
                        uint h = (uint)Marshal.ReadInt64(prop.Data);
                        if (_naturalVideoHeight != h)
                        {
                            _naturalVideoHeight = h;
                            DispatcherEnqueue(() => NaturalVideoSizeChanged?.Invoke(this, EventArgs.Empty));
                        }
                    }
                    break;

                case PropChapter:
                    if (prop.Format == MpvFormat.Int64)
                    {
                        long ch = Marshal.ReadInt64(prop.Data);
                        DispatcherEnqueue(() =>
                        {
                            if (PlaybackItem != null && ch >= 0 && ch < PlaybackItem.Chapters.Count)
                                Chapter = PlaybackItem.Chapters[(int)ch];
                            else if (PlaybackItem != null && PlaybackItem.Chapters.Count > 0 && ch < 0)
                                Chapter = FindChapterAtPosition(Position);
                            else
                                Chapter = null;
                        });
                    }
                    break;

                case PropTrackList:
                    if (prop.Format == MpvFormat.Node && prop.Data != IntPtr.Zero)
                    {
                        var rawTracks = Odeon.Core.Interop.MpvNodeReader.ParseTrackList(prop.Data);
                        DispatcherEnqueue(() => UpdateTracksFromMpv(rawTracks));
                    }
                    break;

                case PropChapterList:
                    if (prop.Format == MpvFormat.Node && prop.Data != IntPtr.Zero)
                    {
                        var rawChapters = Odeon.Core.Interop.MpvNodeReader.ParseChapterList(prop.Data);
                        DispatcherEnqueue(() => UpdateChaptersFromMpv(rawChapters));
                    }
                    break;
            }
        }

        private void UpdatePlaybackState()
        {
            MediaPlaybackState newState;
            if (_isIdle)
                newState = MediaPlaybackState.None;
            else if (_isPaused)
                newState = MediaPlaybackState.Paused;
            else
                newState = MediaPlaybackState.Playing;

            PlaybackState = newState;
        }

        #endregion

        #region Public Playback Control Methods

        public void Play()
        {
            if (_mpv == IntPtr.Zero) return;

            if (_readyToPlay && PlaybackItem != null)
            {
                _readyToPlay = false;
                PlaybackState = MediaPlaybackState.Opening;
                lock (_loadedExternalSubtitles)
                {
                    _loadedExternalSubtitles.Clear();
                }
                lock (_loadedExternalAudio)
                {
                    _loadedExternalAudio.Clear();
                }
                _cachedChapters = null;

                // --- Strategy 1: IStorageItemHandleAccess (works for ALL drives including mapped) ---
                // When the file was opened via FileOpenPicker, UWP grants access via broker.
                // IStorageItemHandleAccess::Create gives a real Win32 HANDLE, bypassing AppContainer.
                // mpv's fdclose:// protocol reads from the fd and closes it when done.
                if (PlaybackItem.OriginalSource is Windows.Storage.IStorageFile storageFile)
                {
                    int fd = Odeon.Core.Interop.StorageFileHandleInterop.OpenFileDescriptor(storageFile);
                    if (fd >= 0)
                    {
                        string fdUri = $"fdclose://{fd}";
                        _currentLoadedPath = PlaybackItem.FilePath;
                        _initialTracksResolved = false;
                        Debug.WriteLine($"[MpvMediaPlayer] loadfile via fdclose fd={fd} (path={PlaybackItem.FilePath})");

                        if (PlaybackItem.StartTime > TimeSpan.Zero)
                        {
                            string startSec = PlaybackItem.StartTime.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                            MpvInterop.Command(_mpv, "loadfile", fdUri, "replace", $"start={startSec}");
                        }
                        else
                        {
                            MpvInterop.Command(_mpv, "loadfile", fdUri);
                        }

                        MpvInterop.SetPropertyBool(_mpv, "pause", false);
                        return;
                    }
                    Debug.WriteLine("[MpvMediaPlayer] IStorageItemHandleAccess failed, falling back to path.");
                }

                // --- Strategy 2: Path-based (requires broadFileSystemAccess + Settings grant) ---
                string? filePath = !string.IsNullOrEmpty(PlaybackItem.FilePath)
                    ? PlaybackItem.FilePath
                    : ResolveSourcePath(PlaybackItem.OriginalSource);

                if (filePath is { Length: > 0 })
                {
                    // Normalize: mpv accepts forward slashes on Windows
                    filePath = filePath.Replace("\\", "/");
                    _currentLoadedPath = filePath;
                    _initialTracksResolved = false;
                    Debug.WriteLine($"[MpvMediaPlayer] loadfile via path: {filePath}");

                    if (PlaybackItem.StartTime > TimeSpan.Zero)
                    {
                        string startSec = PlaybackItem.StartTime.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                        MpvInterop.Command(_mpv, "loadfile", filePath, "replace", $"start={startSec}");
                    }
                    else
                    {
                        MpvInterop.Command(_mpv, "loadfile", filePath);
                    }

                    MpvInterop.SetPropertyBool(_mpv, "pause", false);
                    return;
                }
            }

            // Already loaded or no item — just unpause
            MpvInterop.SetPropertyBool(_mpv, "pause", false);
        }

        public void Pause()
        {
            if (_mpv == IntPtr.Zero) return;
            MpvInterop.SetPropertyBool(_mpv, "pause", true);
        }

        public void Stop()
        {
            if (_mpv == IntPtr.Zero) return;
            MpvInterop.Command(_mpv, "stop");
            _isIdle = true;
            _initialTracksResolved = false;
            PlaybackState = MediaPlaybackState.None;
        }

        public void Close()
        {
            Dispose();
        }

        public void StepForwardOneFrame()
        {
            if (_mpv == IntPtr.Zero) return;
            MpvInterop.Command(_mpv, "frame-step");
        }

        public void StepBackwardOneFrame()
        {
            if (_mpv == IntPtr.Zero) return;
            MpvInterop.Command(_mpv, "frame-back-step");
        }

        public void AddSubtitle(IStorageFile file, bool select = true)
        {
            if (file == null || _mpv == IntPtr.Zero) return;

            string key = !string.IsNullOrEmpty(file.Path) ? file.Path : file.Name;
            lock (_loadedExternalSubtitles)
            {
                if (_loadedExternalSubtitles.Contains(key))
                {
                    if (select)
                    {
                        var tracks = GetTrackList();
                        foreach (var track in tracks)
                        {
                            if (track.Type == "sub" && (track.Title == file.Name || (track.Title != null && track.Title.StartsWith(file.Name, StringComparison.OrdinalIgnoreCase))))
                            {
                                MpvInterop.SetPropertyLong(_mpv, "sid", track.Id);
                                break;
                            }
                        }
                    }
                    return;
                }
                _loadedExternalSubtitles.Add(key);
            }

            string? prevSid = null;
            if (!select && _mpv != IntPtr.Zero)
            {
                prevSid = MpvInterop.GetPropertyString(_mpv, "sid");
            }

            // Use IStorageItemHandleAccess to bypass AppContainer sandbox for external subtitle files
            int fd = Odeon.Core.Interop.StorageFileHandleInterop.OpenFileDescriptor(file);
            if (fd >= 0)
            {
                string fdUri = $"fdclose://{fd}";
                Debug.WriteLine($"[MpvMediaPlayer] AddSubtitle via fdclose fd={fd} (name={file.Name})");
                MpvInterop.Command(_mpv, "sub-add", fdUri, select ? "select" : "auto", file.Name);
                if (!select && !string.IsNullOrEmpty(prevSid) && _mpv != IntPtr.Zero)
                {
                    MpvInterop.SetPropertyString(_mpv, "sid", prevSid!);
                }
                return;
            }

            string path = file.Path;
            if (!string.IsNullOrEmpty(path))
            {
                AddSubtitle(path, select);
            }
        }

        public void AddSubtitle(string path, bool select = true)
        {
            if (_mpv == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
            lock (_loadedExternalSubtitles)
            {
                if (_loadedExternalSubtitles.Contains(path)) return;
                _loadedExternalSubtitles.Add(path);
            }

            string? prevSid = null;
            if (!select && _mpv != IntPtr.Zero)
            {
                prevSid = MpvInterop.GetPropertyString(_mpv, "sid");
            }

            MpvInterop.Command(_mpv, "sub-add", path, select ? "select" : "auto", System.IO.Path.GetFileName(path));

            if (!select && !string.IsNullOrEmpty(prevSid) && _mpv != IntPtr.Zero)
            {
                MpvInterop.SetPropertyString(_mpv, "sid", prevSid!);
            }
        }

        public void AddAudioTrack(Windows.Storage.IStorageFile file, bool select = true)
        {
            if (_mpv == IntPtr.Zero || file == null) return;

            string key = !string.IsNullOrEmpty(file.Path) ? file.Path : file.Name;
            lock (_loadedExternalAudio)
            {
                if (_loadedExternalAudio.Contains(key))
                {
                    if (select)
                    {
                        var tracks = GetTrackList();
                        foreach (var track in tracks)
                        {
                            if (track.Type == "audio" && (track.Title == file.Name || track.External))
                            {
                                MpvInterop.SetPropertyLong(_mpv, "aid", track.Id);
                                break;
                            }
                        }
                    }
                    return;
                }
                _loadedExternalAudio.Add(key);
            }

            // Use IStorageItemHandleAccess to bypass AppContainer sandbox for external audio files
            int fd = Odeon.Core.Interop.StorageFileHandleInterop.OpenFileDescriptor(file);
            if (fd >= 0)
            {
                string fdUri = $"fdclose://{fd}";
                Debug.WriteLine($"[MpvMediaPlayer] AddAudioTrack via fdclose fd={fd} (name={file.Name})");
                MpvInterop.Command(_mpv, "audio-add", fdUri, select ? "select" : "auto", file.Name);
                return;
            }

            string path = file.Path;
            if (!string.IsNullOrEmpty(path))
            {
                AddAudioTrack(path, select);
            }
        }

        public void AddAudioTrack(string path, bool select = true)
        {
            if (_mpv == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
            lock (_loadedExternalAudio)
            {
                if (_loadedExternalAudio.Contains(path)) return;
                _loadedExternalAudio.Add(path);
            }
            MpvInterop.Command(_mpv, "audio-add", path, select ? "select" : "auto");
        }

        public bool TakeSnapshot(string path)
        {
            if (_mpv == IntPtr.Zero || string.IsNullOrEmpty(path)) return false;
            return MpvInterop.Command(_mpv, "screenshot-to-file", path, "video") == 0;
        }

        private void Replay()
        {
            if (_mpv == IntPtr.Zero) return;
            MpvInterop.Command(_mpv, "seek", "0", "absolute");
            MpvInterop.SetPropertyBool(_mpv, "pause", false);
        }

        #endregion

        #region Helpers & Item Handlers

        private static string? ResolveSourcePath(object? source)
        {
            if (source is IStorageFile file)
                return file.Path;
            if (source is Uri uri)
                return uri.AbsoluteUri;
            if (source is string path)
                return path;
            return null;
        }

        private void RemoveItemHandlers(PlaybackItem item)
        {
            item.SubtitleTracks.SelectedIndexChanged -= SubtitleTracksOnSelectedIndexChanged;
            item.AudioTracks.SelectedIndexChanged -= AudioTracksOnSelectedIndexChanged;
            item.VideoTracks.SelectedIndexChanged -= VideoTracksOnSelectedIndexChanged;
        }

        private void RegisterItemHandlers(PlaybackItem item)
        {
            RemoveItemHandlers(item);
            item.SubtitleTracks.AttachPlayer(this);
            item.SubtitleTracks.SelectedIndexChanged += SubtitleTracksOnSelectedIndexChanged;
            item.AudioTracks.SelectedIndexChanged += AudioTracksOnSelectedIndexChanged;
            item.VideoTracks.SelectedIndexChanged += VideoTracksOnSelectedIndexChanged;

            if (_mpv != IntPtr.Zero && _currentLoadedPath != null && _currentLoadedPath == item.FilePath)
            {
                var tracks = GetTrackList();
                if (tracks.Count > 0)
                    UpdateTracksFromMpv((List<Odeon.Core.Interop.MpvTrackInfo>)tracks);
                var chapters = GetChapters();
                if (chapters.Count > 0)
                    UpdateChaptersFromMpv((List<Odeon.Core.Interop.MpvChapterInfo>)chapters);
            }
        }

        private void AudioTracksOnSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
        {
            if (_mpv == IntPtr.Zero || _isUpdatingTracks) return;
            PlaybackAudioTrackList trackList = (PlaybackAudioTrackList)sender;
            if (sender.SelectedIndex < 0)
                MpvInterop.SetPropertyString(_mpv, "aid", "no");
            else
                MpvInterop.SetPropertyLong(_mpv, "aid", trackList[sender.SelectedIndex].TrackId);
        }

        private void VideoTracksOnSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
        {
            if (_mpv == IntPtr.Zero || _isUpdatingTracks) return;
            PlaybackVideoTrackList trackList = (PlaybackVideoTrackList)sender;
            if (sender.SelectedIndex < 0)
                MpvInterop.SetPropertyString(_mpv, "vid", "no");
            else
                MpvInterop.SetPropertyLong(_mpv, "vid", trackList[sender.SelectedIndex].TrackId);
        }

        private void SubtitleTracksOnSelectedIndexChanged(ISingleSelectMediaTrackList sender, object? args)
        {
            if (_mpv == IntPtr.Zero || _isUpdatingTracks) return;
            PlaybackSubtitleTrackList trackList = (PlaybackSubtitleTrackList)sender;
            if (sender.SelectedIndex < 0)
                MpvInterop.SetPropertyString(_mpv, "sid", "no");
            else if (trackList[sender.SelectedIndex].TrackId >= 0)
                MpvInterop.SetPropertyLong(_mpv, "sid", trackList[sender.SelectedIndex].TrackId);
        }

        private void UpdateTracksFromMpv(List<Odeon.Core.Interop.MpvTrackInfo> rawTracks)
        {
            if (PlaybackItem == null) return;

            var audioTracks = new List<AudioTrack>();
            var videoTracks = new List<VideoTrack>();
            var subTracks = new List<SubtitleTrack>();

            int selectedAudio = -1;
            int selectedVideo = -1;
            int selectedSub = -1;

            foreach (var track in rawTracks)
            {
                switch (track.Type)
                {
                    case "audio":
                        if (track.Selected) selectedAudio = audioTracks.Count;
                        audioTracks.Add(new AudioTrack(track.Id, track.Title, track.Language, track.Codec, track.Selected));
                        break;
                    case "video":
                        if (track.Selected) selectedVideo = videoTracks.Count;
                        videoTracks.Add(new VideoTrack(track.Id, track.Title, track.Language, track.Codec, track.DemuxWidth, track.DemuxHeight, track.Selected));
                        break;
                    case "sub":
                        if (track.Selected) selectedSub = subTracks.Count;
                        subTracks.Add(new SubtitleTrack(track.Id, track.Title, track.Language, track.Codec, track.External, track.Selected));
                        break;
                }
            }

            if (!_initialTracksResolved && (audioTracks.Count > 0 || subTracks.Count > 0))
            {
                selectedAudio = ResolvePreferredAudioTrack(audioTracks, selectedAudio);
                selectedSub = ResolvePreferredSubtitleTrack(subTracks, selectedSub);
                _initialTracksResolved = true;
            }

            var currentItem = PlaybackItem;
            if (currentItem == null) return;

            _isUpdatingTracks = true;
            try
            {
                currentItem.AudioTracks.UpdateTracks(audioTracks, selectedAudio);
                currentItem.VideoTracks.UpdateTracks(videoTracks, selectedVideo);
                currentItem.SubtitleTracks.UpdateTracks(subTracks, selectedSub);
            }
            finally
            {
                _isUpdatingTracks = false;
            }
        }

        private void UpdateChaptersFromMpv(List<Odeon.Core.Interop.MpvChapterInfo> rawChapters)
        {
            if (rawChapters != null && rawChapters.Count > 0)
            {
                _cachedChapters = rawChapters;
            }
            var currentItem = PlaybackItem;
            if (currentItem != null)
            {
                if (rawChapters != null && rawChapters.Count > 0)
                {
                    currentItem.Chapters.Load(rawChapters, NaturalDuration);
                    if (currentItem.Chapters.Count > 0)
                    {
                        long? ch = _mpv != IntPtr.Zero ? Odeon.Core.Interop.MpvInterop.GetPropertyLong(_mpv, "chapter") : null;
                        if (ch.HasValue && ch.Value >= 0 && ch.Value < currentItem.Chapters.Count)
                        {
                            Chapter = currentItem.Chapters[(int)ch.Value];
                        }
                        else if (Chapter == null)
                        {
                            Chapter = FindChapterAtPosition(Position) ?? currentItem.Chapters[0];
                        }
                    }
                }
                DispatcherEnqueue(() => ChaptersLoaded?.Invoke(this, EventArgs.Empty));
            }
        }

        private ChapterCue? FindChapterAtPosition(TimeSpan pos)
        {
            if (PlaybackItem == null || PlaybackItem.Chapters.Count == 0) return null;
            for (int i = 0; i < PlaybackItem.Chapters.Count; i++)
            {
                var cue = PlaybackItem.Chapters[i];
                if (pos >= cue.StartTime && (cue.Duration == TimeSpan.Zero || pos < cue.StartTime + cue.Duration))
                {
                    return cue;
                }
            }
            return PlaybackItem.Chapters[0];
        }

        public IReadOnlyList<Odeon.Core.Interop.MpvChapterInfo> GetChapters()
        {
            if (_cachedChapters != null && _cachedChapters.Count > 0) return _cachedChapters;
            if (_mpv != IntPtr.Zero)
            {
                var list = Odeon.Core.Interop.MpvNodeReader.GetChapterList(_mpv);
                if (list != null && list.Count > 0)
                {
                    _cachedChapters = list;
                    return _cachedChapters;
                }
            }
            return _cachedChapters ?? (IReadOnlyList<Odeon.Core.Interop.MpvChapterInfo>)Array.Empty<Odeon.Core.Interop.MpvChapterInfo>();
        }

        public IReadOnlyList<Odeon.Core.Interop.MpvTrackInfo> GetTrackList()
        {
            if (_mpv != IntPtr.Zero)
            {
                return Odeon.Core.Interop.MpvNodeReader.GetTrackList(_mpv);
            }
            return Array.Empty<Odeon.Core.Interop.MpvTrackInfo>();
        }

        public void SetSubtitleAssOverride(bool enabled)
        {
            if (_mpv == IntPtr.Zero) return;
            if (_currentSubAssOverride == enabled) return;
            _currentSubAssOverride = enabled;

            if (enabled)
            {
                MpvInterop.SetPropertyString(_mpv, "sub-ass-override", "force");
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", GetForceStyle());
                MpvInterop.SetPropertyString(_mpv, "sub-font-size", _currentSubtitleFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                MpvInterop.SetPropertyString(_mpv, "sub-scale", "1.0");
            }
            else
            {
                MpvInterop.SetPropertyString(_mpv, "sub-ass-override", "scale");
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", "");
                MpvInterop.SetPropertyString(_mpv, "sub-font-size", Odeon.Core.Helpers.SubtitleStyle.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                double scale = _currentSubtitleFontSize / (double)Odeon.Core.Helpers.SubtitleStyle.FontSize;
                MpvInterop.SetPropertyString(_mpv, "sub-scale", scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            FlushActiveSubtitleTrack();
        }

        private string GetForceStyle()
        {
            string fontName = Odeon.Core.Helpers.SubtitleStyle.FontFamily;
            int outline = _currentSubOutlineEnabled ? Odeon.Core.Helpers.SubtitleStyle.OutlineThickness : 0;

            if (_currentSubBackground)
            {
                int alpha = (int)Math.Round((1.0 - (_currentSubBackgroundOpacity / 100.0)) * 255.0);
                string alphaHex = alpha.ToString("X2");
                return $"Fontname={fontName},Fontsize={_currentSubtitleFontSize},BorderStyle=4,Outline={outline},Shadow=5,BackColour=&H{alphaHex}000000";
            }
            else
            {
                return $"Fontname={fontName},Fontsize={_currentSubtitleFontSize},BorderStyle=1,Outline={outline},Shadow={Odeon.Core.Helpers.SubtitleStyle.ShadowDepth}";
            }
        }

        public void SetSubtitleBackground(bool enabled)
        {
            if (_mpv == IntPtr.Zero) return;
            _currentSubBackground = enabled;

            ApplySubtitleBackground();
        }

        public void SetSubtitleBackgroundOpacity(int opacity)
        {
            if (_mpv == IntPtr.Zero) return;
            if (opacity < 10) opacity = 10;
            if (opacity > 100) opacity = 100;
            _currentSubBackgroundOpacity = opacity;

            if (_currentSubBackground)
            {
                ApplySubtitleBackground();
            }
        }

        public void SetSubtitleOutline(bool enabled)
        {
            if (_mpv == IntPtr.Zero) return;
            _currentSubOutlineEnabled = enabled;

            string outlineSize = enabled ? "1" : "0";
            MpvInterop.SetPropertyString(_mpv, "sub-border-size", outlineSize);

            if (_currentSubAssOverride == true)
            {
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", GetForceStyle());
            }
        }

        private void ApplySubtitleBackground()
        {
            if (_mpv == IntPtr.Zero) return;

            bool enabled = _currentSubBackground;
            int opacity = _currentSubBackgroundOpacity;

            string borderStyle = enabled ? "background-box" : "outline-and-shadow";
            int mpvAlpha = (int)Math.Round((opacity / 100.0) * 255.0);
            string mpvAlphaHex = mpvAlpha.ToString("X2");
            string color = enabled ? $"#{mpvAlphaHex}000000" : "#00000000";

            string outlineSize = _currentSubOutlineEnabled ? "1" : "0";
            string shadowOffset = enabled ? "5" : Odeon.Core.Helpers.SubtitleStyle.ShadowDepth.ToString();

            MpvInterop.SetPropertyString(_mpv, "sub-border-style", borderStyle);
            MpvInterop.SetPropertyString(_mpv, "sub-border-size", outlineSize);
            MpvInterop.SetPropertyString(_mpv, "sub-shadow-offset", shadowOffset);
            MpvInterop.SetPropertyString(_mpv, "sub-back-color", color);

            // If ASS override is enabled, keep sub-ass-style-overrides synchronized
            if (_currentSubAssOverride == true)
            {
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", GetForceStyle());
            }
        }

        public void SetSubtitleFontSize(int fontSize)
        {
            if (_mpv == IntPtr.Zero) return;
            if (fontSize < 10) fontSize = 10;
            if (fontSize > 150) fontSize = 150;
            if (_currentSubtitleFontSize == fontSize) return;
            _currentSubtitleFontSize = fontSize;

            if (_currentSubAssOverride == true)
            {
                // Override is ON: force uniform style & size on both SRT and ASS
                MpvInterop.SetPropertyString(_mpv, "sub-font-size", fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", GetForceStyle());
                MpvInterop.SetPropertyString(_mpv, "sub-scale", "1.0");
            }
            else
            {
                // Override is OFF: preserve author styling/fonts, scale proportionally without double-scaling SRT
                MpvInterop.SetPropertyString(_mpv, "sub-font-size", Odeon.Core.Helpers.SubtitleStyle.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                double scale = fontSize / (double)Odeon.Core.Helpers.SubtitleStyle.FontSize;
                MpvInterop.SetPropertyString(_mpv, "sub-ass-override", "scale");
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", "");
                MpvInterop.SetPropertyString(_mpv, "sub-scale", scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        public void SetSubtitlePosition(int position)
        {
            if (_mpv == IntPtr.Zero) return;
            if (position < 50) position = 50;
            if (position > 115) position = 115;
            if (_currentSubtitlePosition == position) return;
            _currentSubtitlePosition = position;

            MpvInterop.SetPropertyString(_mpv, "sub-pos", position.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void FlushActiveSubtitleTrack()
        {
            // Modern libmpv dynamically reapplies sub-ass-style-overrides, sub-font-size, and sub-scale
            // on the fly without cycling sid. Removing sid cycling eliminates subtitle flicker and UI track event churn.
        }

        public void LoadSubtitleSettingsFromStore()
        {
            try
            {
                var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (values.TryGetValue("OverrideSubtitleStyles", out object obVal) && obVal is bool b)
                    _currentSubAssOverride = b;
                else if (_currentSubAssOverride == null)
                    _currentSubAssOverride = true;

                if (values.TryGetValue("Player/SubtitleBackgroundEnabled", out object sbVal) && sbVal is bool sb)
                    _currentSubBackground = sb;
                if (values.TryGetValue("Player/SubtitleBackgroundOpacity", out object sboVal) && sboVal is int sbo && sbo >= 10 && sbo <= 100)
                    _currentSubBackgroundOpacity = sbo;
                if (values.TryGetValue("Player/SubtitleFontSize", out object sfsVal) && sfsVal is int sfs && sfs > 0)
                    _currentSubtitleFontSize = sfs;
                if (values.TryGetValue("Player/SubtitlePosition", out object spVal) && spVal is int sp && sp >= 50 && sp <= 115)
                    _currentSubtitlePosition = sp;
                if (values.TryGetValue("Player/SubtitleOutlineEnabled", out object soVal) && soVal is bool so)
                    _currentSubOutlineEnabled = so;
            }
            catch
            {
                // LocalSettings fallback
            }
        }

        public void ApplyAllSubtitleSettings()
        {
            if (_mpv == IntPtr.Zero) return;

            LoadSubtitleSettingsFromStore();

            // 1. Outline and border size
            string outlineSize = _currentSubOutlineEnabled ? "1" : "0";
            MpvInterop.SetPropertyString(_mpv, "sub-border-size", outlineSize);

            // 2. Position
            MpvInterop.SetPropertyString(_mpv, "sub-pos", _currentSubtitlePosition.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // 3. Background box & opacity
            string borderStyle = _currentSubBackground ? "background-box" : "outline-and-shadow";
            int mpvAlpha = (int)Math.Round((_currentSubBackgroundOpacity / 100.0) * 255.0);
            string mpvAlphaHex = mpvAlpha.ToString("X2");
            string color = _currentSubBackground ? $"#{mpvAlphaHex}000000" : "#00000000";
            string shadowOffset = _currentSubBackground ? "5" : Odeon.Core.Helpers.SubtitleStyle.ShadowDepth.ToString();

            MpvInterop.SetPropertyString(_mpv, "sub-border-style", borderStyle);
            MpvInterop.SetPropertyString(_mpv, "sub-shadow-offset", shadowOffset);
            MpvInterop.SetPropertyString(_mpv, "sub-back-color", color);

            // 4. Font size and ASS style override
            if (_currentSubAssOverride == true)
            {
                MpvInterop.SetPropertyString(_mpv, "sub-ass-override", "force");
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", GetForceStyle());
                MpvInterop.SetPropertyString(_mpv, "sub-font-size", _currentSubtitleFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                MpvInterop.SetPropertyString(_mpv, "sub-scale", "1.0");
            }
            else
            {
                MpvInterop.SetPropertyString(_mpv, "sub-ass-override", "scale");
                MpvInterop.SetPropertyString(_mpv, "sub-ass-style-overrides", "");
                MpvInterop.SetPropertyString(_mpv, "sub-font-size", Odeon.Core.Helpers.SubtitleStyle.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                double scale = _currentSubtitleFontSize / (double)Odeon.Core.Helpers.SubtitleStyle.FontSize;
                MpvInterop.SetPropertyString(_mpv, "sub-scale", scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private int ResolvePreferredSubtitleTrack(List<SubtitleTrack> subTracks, int defaultSelectedSub)
        {
            if (subTracks.Count == 0) return -1;

            try
            {
                var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string mediaKey = PlaybackItem?.FilePath ?? "";
                string perMediaKey = !string.IsNullOrEmpty(mediaKey) ? $"MediaSubTrack_{mediaKey.GetHashCode():X8}" : "";

                string? targetPreference = null;
                if (!string.IsNullOrEmpty(perMediaKey) && values.TryGetValue(perMediaKey, out object pmVal) && pmVal is string pmStr)
                {
                    targetPreference = pmStr;
                }
                else if (values.TryGetValue("Values/SubtitleLanguage", out object gvVal) && gvVal is string gvStr)
                {
                    targetPreference = gvStr;
                }

                if (targetPreference is not { Length: > 0 })
                {
                    return defaultSelectedSub >= 0 && defaultSelectedSub < subTracks.Count ? defaultSelectedSub : (subTracks.Count > 0 ? 0 : -1);
                }

                // If user disabled subtitles ("none" or "disabled")
                if ("none".Equals(targetPreference, StringComparison.OrdinalIgnoreCase) ||
                    "disabled".Equals(targetPreference, StringComparison.OrdinalIgnoreCase))
                {
                    if (_mpv != IntPtr.Zero)
                    {
                        MpvInterop.SetPropertyString(_mpv, "sid", "no");
                    }
                    return -1;
                }

                // Check for matches
                var tokens = targetPreference.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    string clean = token.Trim();
                    for (int i = 0; i < subTracks.Count; i++)
                    {
                        var track = subTracks[i];
                        if (clean.Equals(track.LanguageTag, StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals(track.Language, StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals(track.Title, StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals(track.TrackId.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (defaultSelectedSub != i && _mpv != IntPtr.Zero && track.TrackId >= 0)
                            {
                                MpvInterop.SetPropertyLong(_mpv, "sid", track.TrackId);
                            }
                            return i;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to default
            }

            return defaultSelectedSub >= 0 && defaultSelectedSub < subTracks.Count ? defaultSelectedSub : (subTracks.Count > 0 ? 0 : -1);
        }

        private int ResolvePreferredAudioTrack(List<AudioTrack> audioTracks, int defaultSelectedAudio)
        {
            if (audioTracks.Count == 0) return -1;

            try
            {
                var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string mediaKey = PlaybackItem?.FilePath ?? "";
                string perMediaKey = !string.IsNullOrEmpty(mediaKey) ? $"MediaAudioTrack_{mediaKey.GetHashCode():X8}" : "";

                string? targetPreference = null;
                if (!string.IsNullOrEmpty(perMediaKey) && values.TryGetValue(perMediaKey, out object pmVal) && pmVal is string pmStr)
                {
                    targetPreference = pmStr;
                }
                else if (values.TryGetValue("Values/AudioLanguage", out object gvVal) && gvVal is string gvStr)
                {
                    targetPreference = gvStr;
                }

                if (targetPreference is not { Length: > 0 })
                {
                    if (defaultSelectedAudio < 0 && audioTracks.Count > 0)
                    {
                        if (_mpv != IntPtr.Zero && audioTracks[0].TrackId >= 0)
                        {
                            MpvInterop.SetPropertyLong(_mpv, "aid", audioTracks[0].TrackId);
                        }
                        return 0;
                    }
                    return defaultSelectedAudio;
                }

                // If user disabled audio ("none" or "disabled")
                if ("none".Equals(targetPreference, StringComparison.OrdinalIgnoreCase) ||
                    "disabled".Equals(targetPreference, StringComparison.OrdinalIgnoreCase))
                {
                    if (_mpv != IntPtr.Zero)
                    {
                        MpvInterop.SetPropertyString(_mpv, "aid", "no");
                    }
                    return -1;
                }

                // Check for matches
                var tokens = targetPreference.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    string clean = token.Trim();
                    for (int i = 0; i < audioTracks.Count; i++)
                    {
                        var track = audioTracks[i];
                        if (clean.Equals(track.LanguageTag, StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals(track.Language, StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals(track.Title, StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals(track.TrackId.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (defaultSelectedAudio != i && _mpv != IntPtr.Zero && track.TrackId >= 0)
                            {
                                MpvInterop.SetPropertyLong(_mpv, "aid", track.TrackId);
                            }
                            return i;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to default
            }

            if (defaultSelectedAudio < 0 && audioTracks.Count > 0)
            {
                if (_mpv != IntPtr.Zero && audioTracks[0].TrackId >= 0)
                {
                    MpvInterop.SetPropertyLong(_mpv, "aid", audioTracks[0].TrackId);
                }
                return 0;
            }

            return defaultSelectedAudio;
        }

        private void RestoreMediaTimingOffsets()
        {
            try
            {
                var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string mediaKey = PlaybackItem?.FilePath ?? "";
                if (!string.IsNullOrEmpty(mediaKey))
                {
                    string aKey = $"MediaAudioDelay_{mediaKey.GetHashCode():X8}";
                    if (values.TryGetValue(aKey, out object aVal))
                    {
                        double aDelay = aVal is double ad ? ad : (aVal is int ai ? (double)ai : 0.0);
                        AudioDelay = aDelay;
                    }

                    string sKey = $"MediaSubDelay_{mediaKey.GetHashCode():X8}";
                    if (values.TryGetValue(sKey, out object sVal))
                    {
                        double sDelay = sVal is double sd ? sd : (sVal is int si ? (double)si : 0.0);
                        SubtitleDelay = sDelay;
                    }
                }
            }
            catch
            {
                // Fallback
            }
        }

        private void MediaDevice_DefaultAudioRenderDeviceChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
        {
            if (args.Role == AudioDeviceRole.Default && _mpv != IntPtr.Zero)
            {
                MpvInterop.SetPropertyString(_mpv, "audio-device", "auto");
            }
        }

        private void DispatcherEnqueue(Action action)
        {
            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                _dispatcherQueue.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            MediaDevice.DefaultAudioRenderDeviceChanged -= MediaDevice_DefaultAudioRenderDeviceChanged;

            if (_playbackItem != null)
            {
                RemoveItemHandlers(_playbackItem);
                _playbackItem = null;
            }

            if (_mpv != IntPtr.Zero)
            {
                MpvInterop.mpv_wakeup(_mpv);

                if (_ownsMpv)
                {
                    MpvInterop.mpv_terminate_destroy(_mpv);
                }
                _mpv = IntPtr.Zero;
            }
        }

        #endregion
    }
}



