#nullable enable

using System;
using CommunityToolkit.Mvvm.DependencyInjection;
using Odeon.Core.Playback;
using Odeon.Core.Rendering;
using Odeon.Core.ViewModels;
using Windows.Graphics.Display;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Odeon.Controls;

public sealed partial class PlayerElement : UserControl
{
    public static readonly DependencyProperty ButtonMarginProperty = DependencyProperty.Register(
        nameof(ButtonMargin),
        typeof(Thickness),
        typeof(PlayerElement),
        new PropertyMetadata(default(Thickness)));

    public Thickness ButtonMargin
    {
        get => (Thickness)GetValue(ButtonMarginProperty);
        set => SetValue(ButtonMarginProperty, value);
    }

    private readonly GestureRecognizer _gestureRecognizer;
    private bool _shouldSuppressNextClick;
    private D3D11SwapChainManager? _swapChainManager;
    private DisplayInformation? _displayInfo;

    public event RoutedEventHandler? Click;

    internal PlayerElementViewModel ViewModel => (PlayerElementViewModel)DataContext;

    public PlayerElement()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<PlayerElementViewModel>();

        _gestureRecognizer = new GestureRecognizer
        {
            GestureSettings = GestureSettings.Hold | GestureSettings.HoldWithMouse,
        };

        _gestureRecognizer.Holding += GestureRecognizer_OnHolding;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _swapChainManager = new D3D11SwapChainManager();
        _swapChainManager.Initialize(VideoSurface);

        if (VideoSurface.ActualWidth > 0 && VideoSurface.ActualHeight > 0)
        {
            float scaleX = (float)VideoSurface.CompositionScaleX;
            float scaleY = (float)VideoSurface.CompositionScaleY;
            int pixelWidth = (int)Math.Max(1, Math.Round(VideoSurface.ActualWidth * scaleX));
            int pixelHeight = (int)Math.Max(1, Math.Round(VideoSurface.ActualHeight * scaleY));
            _swapChainManager.Resize(pixelWidth, pixelHeight, scaleX, scaleY);
        }

        VideoSurface.CompositionScaleChanged += VideoSurface_OnCompositionScaleChanged;

        ViewModel.MediaPlayerReady += ViewModel_OnMediaPlayerReady;
        ViewModel.ClearViewRequested += ViewModel_OnClearViewRequested;

        // Subscribe to Windows HDR state changes so the DXGI swap chain color space
        // is updated whenever the user toggles HDR in Windows Settings.
        try
        {
            _displayInfo = DisplayInformation.GetForCurrentView();
            _displayInfo.AdvancedColorInfoChanged += DisplayInfo_AdvancedColorInfoChanged;
            // Apply current HDR state immediately on load
            var aci = _displayInfo.GetAdvancedColorInfo();
            _swapChainManager?.NotifyHdrChanged(aci.CurrentAdvancedColorKind == AdvancedColorKind.HighDynamicRange);
        }
        catch
        {
            // Not fatal — fallback to SDR (sRGB) color space which was set during Initialize()
        }

        if (ViewModel.MpvPlayer != null)
        {
            _swapChainManager.AttachPlayer(ViewModel.MpvPlayer);
        }

        ViewModel.Initialize();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        VideoSurface.CompositionScaleChanged -= VideoSurface_OnCompositionScaleChanged;
        ViewModel.MediaPlayerReady -= ViewModel_OnMediaPlayerReady;
        ViewModel.ClearViewRequested -= ViewModel_OnClearViewRequested;

        if (_displayInfo != null)
        {
            _displayInfo.AdvancedColorInfoChanged -= DisplayInfo_AdvancedColorInfoChanged;
            _displayInfo = null;
        }

        if (_gestureRecognizer is not null)
        {
            _gestureRecognizer.CompleteGesture();
            _gestureRecognizer.GestureSettings = GestureSettings.None;
            _gestureRecognizer.Holding -= GestureRecognizer_OnHolding;
        }

        _swapChainManager?.Dispose();
        _swapChainManager = null;
    }

    /// <summary>
    /// Fired by Windows whenever the display's HDR mode changes (user toggle in Settings → System → Display).
    /// Updates the DXGI swap chain color space so DWM composites the video correctly.
    /// </summary>
    private void DisplayInfo_AdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        try
        {
            var aci = sender.GetAdvancedColorInfo();
            bool hdr = aci.CurrentAdvancedColorKind == AdvancedColorKind.HighDynamicRange;
            // Color space must be set on the render thread or any thread; IDXGISwapChain3::SetColorSpace1 is thread-safe.
            _swapChainManager?.NotifyHdrChanged(hdr);
        }
        catch { /* non-fatal */ }
    }

    private void ViewModel_OnMediaPlayerReady(object? sender, IMediaPlayer? player)
    {
        if (player is MpvMediaPlayer mpvPlayer && _swapChainManager != null)
        {
            if (VideoSurface.ActualWidth > 0 && VideoSurface.ActualHeight > 0)
            {
                float scaleX = (float)VideoSurface.CompositionScaleX;
                float scaleY = (float)VideoSurface.CompositionScaleY;
                int pixelWidth = (int)Math.Max(1, Math.Round(VideoSurface.ActualWidth * scaleX));
                int pixelHeight = (int)Math.Max(1, Math.Round(VideoSurface.ActualHeight * scaleY));
                _swapChainManager.Resize(pixelWidth, pixelHeight, scaleX, scaleY);
            }

            _swapChainManager.AttachPlayer(mpvPlayer);
        }
    }

    private void ViewModel_OnClearViewRequested(object? sender, EventArgs e)
    {
        _swapChainManager?.Clear();
    }

    private void VideoSurface_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdatePlayerViewSize(e.NewSize);

        if (_swapChainManager != null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            float scaleX = (float)VideoSurface.CompositionScaleX;
            float scaleY = (float)VideoSurface.CompositionScaleY;
            int pixelWidth = (int)Math.Max(1, Math.Round(e.NewSize.Width * scaleX));
            int pixelHeight = (int)Math.Max(1, Math.Round(e.NewSize.Height * scaleY));
            _swapChainManager.Resize(pixelWidth, pixelHeight, scaleX, scaleY);
        }
    }

    private void VideoSurface_OnCompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_swapChainManager != null && sender.ActualWidth > 0 && sender.ActualHeight > 0)
        {
            float scaleX = (float)sender.CompositionScaleX;
            float scaleY = (float)sender.CompositionScaleY;
            int pixelWidth = (int)Math.Max(1, Math.Round(sender.ActualWidth * scaleX));
            int pixelHeight = (int)Math.Max(1, Math.Round(sender.ActualHeight * scaleY));
            _swapChainManager.Resize(pixelWidth, pixelHeight, scaleX, scaleY);
        }
    }

    private void VideoViewButton_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (!IsEnabled) return;
        if (_shouldSuppressNextClick)
        {
            _shouldSuppressNextClick = false;
            return;
        }

        ViewModel.OnClick();
        Click?.Invoke(sender, e);
    }

    private void VideoViewButton_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!IsEnabled) return;
        ViewModel.OnClick();
        Click?.Invoke(sender, e);
    }

    private void VideoViewButton_OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _gestureRecognizer.CompleteGesture();
        VideoViewButton.ReleasePointerCapture(e.Pointer);
        e.Handled = IsEnabled;
    }

    private void VideoViewButton_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled || ViewModel.IsHolding) return;

        _gestureRecognizer.ProcessMoveEvents(e.GetIntermediatePoints(this));
    }

    private void VideoViewButton_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) return;

        _gestureRecognizer.ProcessDownEvent(e.GetCurrentPoint(this));
        VideoViewButton.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void VideoViewButton_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) return;

        _gestureRecognizer.ProcessUpEvent(e.GetCurrentPoint(this));
        VideoViewButton.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void VideoViewButton_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) return;

        var pointer = e.GetCurrentPoint(VideoViewButton);
        var properties = pointer.Properties;
        ViewModel.ProcessPointerWheelInput(properties.MouseWheelDelta, properties.IsHorizontalMouseWheel);
        ViewModel.OnManipulationCompleted();
        e.Handled = true;
    }

    private void VideoViewButton_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!IsEnabled) return;

        ViewModel.ProcessSlideGesture(e.Delta.Translation, e.Cumulative.Translation);
    }

    private void VideoViewButton_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (!IsEnabled) return;

        ViewModel.ProcessSwipeGesture(e.Cumulative.Translation);
        ViewModel.OnManipulationCompleted();
    }

    private void GestureRecognizer_OnHolding(GestureRecognizer sender, HoldingEventArgs args)
    {
        if (args.PointerDeviceType is not (Windows.Devices.Input.PointerDeviceType.Touch or Windows.Devices.Input.PointerDeviceType.Pen)
            && args.HoldingState is HoldingState.Completed or HoldingState.Canceled)
            _shouldSuppressNextClick = true;
        ViewModel.ProcessHoldingGesture(args.HoldingState);
    }
}
