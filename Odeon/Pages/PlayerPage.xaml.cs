#nullable enable

using System;
using System.ComponentModel;
using System.Threading;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Odeon.Controls;
using Odeon.Core.Enums;
using Odeon.Core.Messages;
using Odeon.Core.Services;
using Odeon.Core.ViewModels;
using Odeon.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Odeon.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PlayerPage : Page
{
    internal PlayerPageViewModel ViewModel => (PlayerPageViewModel)DataContext;

    internal static bool IsAnySidePanelOpen { get; private set; }

    private readonly DispatcherQueueTimer _controlsAutoHideTimer;
    private readonly DispatcherQueueTimer _titleBarHoverTimer;
    private CancellationTokenSource? _animationCancellationTokenSource;
    private bool _startup;
    private bool _isSubtitlePanelOpen;
    private bool _isAudioPanelOpen;
    private bool _isPlayQueuePanelOpen;

    public PlayerPage()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<PlayerPageViewModel>();
        ViewModel.GetVolumeChangeStatusMessage = Odeon.Strings.Resources.VolumeChangeStatusMessage;
        _controlsAutoHideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _titleBarHoverTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();

        RegisterSeekBarPointerHandlers();
        UpdatePreviewType();

        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        AlbumArtImage.RegisterPropertyChangedCallback(ImageBrush.ImageSourceProperty, AlbumArtImageOnSourceChanged);
        LayoutRoot.ActualThemeChanged += OnActualThemeChanged;

        WeakReferenceMessenger.Default.Register<ToggleSubtitleSidePanelMessage>(this, (_, m) => ToggleSubtitleSidePanel(m.ForceState));
        SubtitleSidePanel.CloseRequested += (_, _) => CloseSubtitleSidePanel();

        WeakReferenceMessenger.Default.Register<ToggleAudioSidePanelMessage>(this, (_, m) => ToggleAudioSidePanel(m.ForceState));
        AudioSidePanel.CloseRequested += (_, _) => CloseAudioSidePanel();

        WeakReferenceMessenger.Default.Register<TogglePlayQueueSidePanelMessage>(this, (_, m) => TogglePlayQueueSidePanel(m.ForceState));
        PlayQueueSidePanel.CloseRequested += (_, _) => ClosePlayQueueSidePanel();

        WeakReferenceMessenger.Default.Register<ShowPlayPauseBadgeMessage>(this, (_, _) =>
        {
            PlayPauseBadgeStoryboard.Stop();
            PlayPauseBadgeStoryboard.Begin();
        });

        PreviewKeyDown += PlayerPage_PreviewKeyDown;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        UpdateBackgroundAcrylicOpacity(ActualTheme);

        // DO NOT SET CONTENT VISUAL STATE HERE
        // It will cause element theme to not propagate correctly
        // VisualStateManager.GoToState(this, "Video", false);

        if (e.Parameter is true)
        {
            LayoutRoot.Transitions.Clear();
            ViewModel.PlayerVisibility = PlayerVisibilityState.Visible;
            ViewModel.OnFileLaunched();
            _startup = true;
            UpdateContentState();
            UpdateRootTheme();
            UpdatePreviewType();
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (ViewModel.PlayerVisibility != PlayerVisibilityState.Visible)
        {
            base.OnKeyDown(e);
            return;
        }

        // Handle Tab to prevent focus cycling (Properties is opened via global accelerator)
        if (e.OriginalKey == VirtualKey.Tab || e.Key == VirtualKey.Tab)
        {
            e.Handled = true;
            return;
        }

        bool shouldHideControls = ViewModel is { ControlsHidden: false, ViewMode: WindowViewMode.Default };

        switch (e.Key)
        {
            case VirtualKey.GamepadY when ViewModel.ViewMode != WindowViewMode.Compact:
                ViewModel.ControlsHidden = false;
                TogglePlayQueueSidePanel(true);
                break;
            case VirtualKey.GamepadMenu:
                VideoView.ContextFlyout.ShowAt(PlayerControls,
                    new FlyoutShowOptions { Placement = GlobalizationHelper.MirrorWhenRightToLeft(FlyoutPlacementMode.TopEdgeAlignedRight) });
                break;
            case VirtualKey.GamepadB when shouldHideControls:
                ViewModel.TryHideControls(true);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
    }

    private void PlayerPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab || e.OriginalKey == VirtualKey.Tab)
        {
            e.Handled = true;
        }
    }

    private void AlbumArtImageOnSourceChanged(DependencyObject sender, DependencyProperty dp)
    {
        PlayBackgroundArtChangeCrossFadeAnimation();
    }

    private void OnLoading(FrameworkElement sender, object args)
    {
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden)
            VisualStateManager.GoToState(this, "Hidden", false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateContentState();
        UpdateRootTheme();
        UpdatePreviewType();

        if (LayoutRoot.Transitions.Count == 0)
        {
            LayoutRoot.Transitions.Add(new PaneThemeTransition { Edge = EdgeTransitionLocation.Bottom });
        }

        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible)
        {
            // Focus can fail if player is file activated
            // Controls are disabled by default until playback is ready
            PlayerControls.FocusFirstButton();
        }
    }

    private void BackgroundElementOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double overshoot = 200;   // extra space for animation
        double edgeLength = Math.Max(e.NewSize.Width, e.NewSize.Height) + overshoot;
        BackgroundArt.Width = edgeLength;
        BackgroundArt.Height = edgeLength;
    }

    private void RegisterSeekBarPointerHandlers()
    {
        SeekBar? seekBar = PlayerControls.FindDescendant<SeekBar>();
        Guard.IsNotNull(seekBar, nameof(seekBar));
        seekBar.AddHandler(PointerPressedEvent, (PointerEventHandler)SeekBarPointerPressedOrEnteredEventHandler, true);
        seekBar.AddHandler(PointerReleasedEvent, (PointerEventHandler)SeekBarPointerReleasedEventHandler, true);
        seekBar.AddHandler(PointerCanceledEvent, (PointerEventHandler)SeekBarPointerReleasedEventHandler, true);
        seekBar.AddHandler(PointerEnteredEvent, (PointerEventHandler)SeekBarPointerPressedOrEnteredEventHandler, false);
        seekBar.AddHandler(PointerExitedEvent, (PointerEventHandler)SeekBarPointerExitedEventHandler, false);
    }

    private void SeekBarPointerPressedOrEnteredEventHandler(object s, PointerRoutedEventArgs e)
    {
        ViewModel.SeekBarPointerInteracting = true;
    }

    private void SeekBarPointerReleasedEventHandler(object s, PointerRoutedEventArgs e)
    {
        ViewModel.SeekBarPointerInteracting = false;
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible)
            PlayerControls.FocusFirstButton();
    }

    private void SeekBarPointerExitedEventHandler(object s, PointerRoutedEventArgs e)
    {
        ViewModel.SeekBarPointerInteracting = false;
    }

    private void OnLayoutVisualStateChanged(object _, VisualStateChangedEventArgs args)
    {
        bool expanding = args.OldState?.Name == nameof(MiniPlayer) || (args.OldState?.Name == nameof(Hidden) &&
            (args.NewState == null || args.NewState.Name == nameof(Normal)));

        bool collapsing = args.OldState?.Name == nameof(Normal) && args.NewState?.Name == nameof(MiniPlayer);

        if (expanding || collapsing) PlayerControls.FocusFirstButton();
        UpdateRootTheme();
    }

    private void ViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerPageViewModel.ControlsHidden):
                VisualStateManager.GoToState(this, ViewModel.ControlsHidden ? "ControlsHidden" : "ControlsVisible", true);
                if (!ViewModel.ControlsHidden)
                {
                    PlayerControls.FocusFirstButton();
                }

                break;
            case nameof(PlayerPageViewModel.ViewMode):
                switch (ViewModel.ViewMode)
                {
                    case WindowViewMode.Default:
                        VisualStateManager.GoToState(this, "Normal", true);
                        break;
                    case WindowViewMode.Compact:
                        ViewModel.PlayerVisibility = PlayerVisibilityState.Visible;
                        VisualStateManager.GoToState(this, "CompactOverlay", true);
                        break;
                    case WindowViewMode.FullScreen:
                        ViewModel.PlayerVisibility = PlayerVisibilityState.Visible;
                        VisualStateManager.GoToState(this, "Fullscreen", true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                UpdateContentState();
                break;
            case nameof(PlayerPageViewModel.Media):
            case nameof(PlayerPageViewModel.AudioOnly):
                UpdateContentState();
                UpdateRootTheme();
                UpdatePreviewType();
                break;
            case nameof(PlayerPageViewModel.PlayerVisibility):
                switch (ViewModel.PlayerVisibility)
                {
                    case PlayerVisibilityState.Visible:
                        VisualStateManager.GoToState(this, "NoPreview", true);
                        VisualStateManager.GoToState(this, "Normal", true);
                        break;
                    case PlayerVisibilityState.Minimal:
                        VisualStateManager.GoToState(this, "MiniPlayer", true);
                        break;
                    case PlayerVisibilityState.Hidden:
                        VisualStateManager.GoToState(this, "Hidden", true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                UpdatePreviewType();
                UpdateContentState();
                UpdateRootTheme();
                UpdatePreviewType();
                UpdateMiniPlayerMargin();
                break;
            case nameof(PlayerPageViewModel.NavigationViewDisplayMode) when ViewModel.ViewMode == WindowViewMode.Default:
                UpdateMiniPlayerMargin();
                break;
            case nameof(PlayerPageViewModel.IsPlaying):
                if (ViewModel.IsPlaying)
                {
                    if (_startup)
                    {
                        // Only when the app is file activated
                        // Wait till playback starts then focus the player controls
                        _startup = false;
                        PlayerControls.FocusFirstButton();
                    }

                    BackgroundArtAnimation.Resume();
                }
                else
                {
                    BackgroundArtAnimation.Pause();
                }

                break;
            case nameof(PlayerPageViewModel.ShouldClosePlayQueueFlyout) when ViewModel.ShouldClosePlayQueueFlyout:
                ClosePlayQueueSidePanel();
                ViewModel.ShouldClosePlayQueueFlyout = false;
                break;
        }
    }

    private async void PlayBackgroundArtChangeCrossFadeAnimation()
    {
        // AnimationSet does not throw exception on cancellation
        _animationCancellationTokenSource?.Cancel();
        if (BackgroundElement.Visibility == Visibility.Collapsed ||
        BackgroundArt.Visibility == Visibility.Collapsed)
        {
            BackgroundImage.Source = AlbumArtImage.ImageSource;
            return;
        }

        using CancellationTokenSource cts = _animationCancellationTokenSource = new CancellationTokenSource();
        if (ViewModel.Media == null)
        {
            await BackgroundArtFadeOutAnimation.StartAsync(cts.Token);
            BackgroundImage.Source = null;
        }
        else if (BackgroundImage.Source == null)
        {
            BackgroundImageNext.Visibility = Visibility.Collapsed;
            BackgroundImage.GetVisual().Opacity = 0;
            BackgroundImage.Source = AlbumArtImage.ImageSource;
            await BackgroundArtFadeInAnimation.StartAsync(cts.Token);
        }
        else
        {
            BackgroundImageNext.Visibility = Visibility.Visible;
            await BackgroundArtFadeOutAnimation.StartAsync(cts.Token);
            BackgroundImage.Source = AlbumArtImage.ImageSource;
            await BackgroundArtFadeInAnimation.StartAsync(cts.Token);
            BackgroundImageNext.Visibility = Visibility.Collapsed;
        }

        if (cts == _animationCancellationTokenSource)
            _animationCancellationTokenSource = null;
    }

    private void UpdateContentState()
    {
        var contentVisualStateName = ViewModel.AudioOnly
            ? "AudioOnly"
            : "Video";
        VisualStateManager.GoToState(this, contentVisualStateName, true);
    }

    private void UpdatePreviewType()
    {
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible || ViewModel.ViewMode == WindowViewMode.Compact)
        {
            VisualStateManager.GoToState(this, "NoPreview", true);
        }
        else
        {
            VisualStateManager.GoToState(this, ViewModel.AudioOnly ? "AudioPreview" : "VideoPreview", true);
        }
    }

    private void UpdateMiniPlayerMargin()
    {
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible || ViewModel.ViewMode == WindowViewMode.Compact)
        {
            VisualStateManager.GoToState(this, "NoMargin", false);
        }
        else
        {
            switch (ViewModel.NavigationViewDisplayMode)
            {
                case NavigationViewDisplayMode.Minimal when ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden:
                    VisualStateManager.GoToState(this, "HiddenMinimalMargin", false);
                    break;
                case NavigationViewDisplayMode.Minimal:
                    VisualStateManager.GoToState(this, "MinimalMargin", false);
                    break;
                case NavigationViewDisplayMode.Compact when ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden:
                case NavigationViewDisplayMode.Expanded when ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden:
                    VisualStateManager.GoToState(this, "HiddenNormalMargin", false);
                    break;
                case NavigationViewDisplayMode.Compact:
                case NavigationViewDisplayMode.Expanded:
                    VisualStateManager.GoToState(this, "NormalMargin", false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private void UpdateRootTheme()
    {
        LayoutRoot.RequestedTheme = ElementTheme.Dark;
        App.SetupTitleBarColors(ElementTheme.Dark);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateBackgroundAcrylicOpacity(ElementTheme.Dark);
    }

    private void UpdateBackgroundAcrylicOpacity(ElementTheme theme)
    {
        // Set in code due to XAML compiler not setting it in Release
        BackgroundAcrylicBrush.TintLuminosityOpacity = 0.4;
    }

    private void PlayerControlsBackground_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        PlayerControls.FocusFirstButton(FocusState.Pointer);
        e.Handled = true;
    }

    private void PlayQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePlayQueueSidePanel();
    }

    private void PlayQueueButton_OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
    }

    private void PlayQueueButton_OnDragLeave(object sender, DragEventArgs e)
    {
    }

    private async void VideoView_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        // Reset focus after manipulation
        // Must be queued in Dispatcher or risk losing focus right after
        await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
            () => PlayerControls.FocusFirstButton(FocusState.Programmatic));
    }

    private void ControlsVisibilityStates_OnCurrentStateChanged(object sender, VisualStateChangedEventArgs e)
    {
        if (e.NewState.Name == nameof(ControlsHidden))
        {
            // The side panels are anchored to the controls, so they must
            // not be left floating on screen once the controls have moved out.
            CloseSubtitleSidePanel();
            CloseAudioSidePanel();
            ClosePlayQueueSidePanel();

            // The controls have finished moving out, so the cursor can go with them.
            ViewModel.HideCursor();

            // Handle Space key when the controls are not visible.
            // Also hide tooltip if there is any.
            HiddenButton.Focus(FocusState.Programmatic);
        }
    }

    private void VideoView_OnClick(object sender, RoutedEventArgs e)
    {
        if (SubtitleSidePanel.Visibility == Visibility.Visible)
        {
            CloseSubtitleSidePanel();
            return;
        }

        if (!ViewModel.OnPlayerClick())
        {
            PlayerControls.FocusFirstButton();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Link;
        if (e.DragUIOverride != null) e.DragUIOverride.Caption = Strings.Resources.Play;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        await ViewModel.OnDropAsync(e.DataView);
    }

    private void LayoutRoot_OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter = fullscreen toggle
        if (e.OriginalKey == VirtualKey.Enter && !e.KeyStatus.IsMenuKeyDown)
        {
            PlayerControls.ViewModel.ToggleFullscreenCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Alt+Enter = minimize immersive view
        if (e.OriginalKey == VirtualKey.Enter && e.KeyStatus.IsMenuKeyDown)
        {
            ViewModel.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.OriginalKey == VirtualKey.Space &&
            !KeyboardAcceleratorHelper.IsControlKeyDown &&
            !KeyboardAcceleratorHelper.IsShiftKeyDown &&
            !e.KeyStatus.IsMenuKeyDown)
        {
            // Close any open side panel or flyout on Space instead of toggling play/pause.
            if (_isSubtitlePanelOpen)
            {
                CloseSubtitleSidePanel();
                e.Handled = true;
                return;
            }

            if (_isAudioPanelOpen)
            {
                CloseAudioSidePanel();
                e.Handled = true;
                return;
            }

            if (_isPlayQueuePanelOpen)
            {
                ClosePlayQueueSidePanel();
                e.Handled = true;
                return;
            }

            // Close any open UWP flyout (VolumeControlFlyout, CustomPlaybackSpeedFlyout, etc.)
            var openPopups = VisualTreeHelper.GetOpenPopups(Window.Current);
            if (openPopups.Count > 0)
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            ViewModel.TryHideControls(true);
            ViewModel.ProcessSpaceKeyDown();
        }
    }

    private void LayoutRoot_OnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalKey == VirtualKey.Space &&
            !KeyboardAcceleratorHelper.IsControlKeyDown &&
            !KeyboardAcceleratorHelper.IsShiftKeyDown &&
            !e.KeyStatus.IsMenuKeyDown)
        {
            e.Handled = true;
            ViewModel.ProcessSpaceKeyUp();
        }
    }

    private void ChangeVolumeKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = ViewModel.ProcessChangeVolumeKeyDown(args.KeyboardAccelerator.Key);
    }

    private void SeekKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ProcessSeekKeyDown(args.KeyboardAccelerator.Key, args.KeyboardAccelerator.Modifiers);
        args.Handled = true;
    }

    private void FrameSteppingKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = ViewModel.ProcessFrameSteppingKeyDown(args.KeyboardAccelerator.Key);
    }

    private void PlaybackRateKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = ViewModel.ProcessTogglePlaybackRateKeyDown(args.KeyboardAccelerator.Key, args.KeyboardAccelerator.Modifiers);
    }

    private void WindowResizeKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        double? scale = ViewModel.ProcessResizeKeyDown(args.KeyboardAccelerator.Key, args.KeyboardAccelerator.Modifiers);
        args.Handled = scale.HasValue;
        if (scale.HasValue)
        {
            ViewModel.SendStatusMessage(Odeon.Strings.Resources.ScaleStatus($"{scale.Value * 100:0.##}%"));
        }
    }

    private void SeekToPercentageKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = ViewModel.ProcessPercentJumpKeyDown(args.KeyboardAccelerator.Key);
    }

    private void EscapeKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (SubtitleSidePanel.Visibility == Visibility.Visible)
        {
            CloseSubtitleSidePanel();
            args.Handled = true;
            return;
        }

        if (AudioSidePanel.Visibility == Visibility.Visible)
        {
            CloseAudioSidePanel();
            args.Handled = true;
            return;
        }

        if (PlayQueueSidePanel.Visibility == Visibility.Visible)
        {
            ClosePlayQueueSidePanel();
            args.Handled = true;
            return;
        }

        if (Windows.UI.Xaml.Media.VisualTreeHelper.GetOpenPopups(Window.Current).Count > 0)
        {
            args.Handled = true;
            return;
        }

        switch (ViewModel.ViewMode)
        {
            case WindowViewMode.Compact:
            case WindowViewMode.FullScreen:
                ViewModel.GoBack();
                args.Handled = true;
                break;
            case WindowViewMode.Default:
                ViewModel.TryHideControls();
                args.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Keeps the view model informed about whether a side panel is open, so that the control
    /// auto-hide never leaves a panel floating on screen on its own.
    /// </summary>
    private void UpdateSidePanelState()
    {
        ViewModel.IsSidePanelOpen = _isSubtitlePanelOpen || _isAudioPanelOpen || _isPlayQueuePanelOpen;
        IsAnySidePanelOpen = ViewModel.IsSidePanelOpen;
    }

    private void ToggleSubtitleSidePanel(bool? forceState = null)
    {
        if (!Dispatcher.HasThreadAccess)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ToggleSubtitleSidePanel(forceState));
            return;
        }

        bool targetState = forceState ?? !_isSubtitlePanelOpen;
        if (targetState)
        {
            OpenSubtitleSidePanel();
        }
        else
        {
            CloseSubtitleSidePanel();
        }
    }

    private void OpenSubtitleSidePanel()
    {
        if (_isSubtitlePanelOpen && SubtitleSidePanel.Visibility == Visibility.Visible) return;

        if (_isAudioPanelOpen)
        {
            CloseAudioSidePanel();
        }

        if (_isPlayQueuePanelOpen)
        {
            ClosePlayQueueSidePanel();
        }

        _isSubtitlePanelOpen = true;
        UpdateSidePanelState();
        CloseSubtitlePanelStoryboard.Stop();

        SubtitleSidePanel.Visibility = Visibility.Visible;
        SubtitlePanelBackdrop.IsHitTestVisible = true;
        SubtitleSidePanel.OnOpening();

        OpenSubtitlePanelStoryboard.Begin();
    }

    private void CloseSubtitleSidePanel()
    {
        if (!_isSubtitlePanelOpen && SubtitleSidePanel.Visibility == Visibility.Collapsed) return;

        _isSubtitlePanelOpen = false;
        UpdateSidePanelState();
        if (!_isAudioPanelOpen && !_isPlayQueuePanelOpen)
        {
            SubtitlePanelBackdrop.IsHitTestVisible = false;
        }
        OpenSubtitlePanelStoryboard.Stop();

        CloseSubtitlePanelStoryboard.Begin();
    }

    private void CloseSubtitlePanelStoryboard_OnCompleted(object sender, object e)
    {
        if (!_isSubtitlePanelOpen)
        {
            try
            {
                SubtitleSidePanel.OnClosed();
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
            SubtitleSidePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ToggleAudioSidePanel(bool? forceState = null)
    {
        if (!Dispatcher.HasThreadAccess)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ToggleAudioSidePanel(forceState));
            return;
        }

        bool targetState = forceState ?? !_isAudioPanelOpen;
        if (targetState)
        {
            OpenAudioSidePanel();
        }
        else
        {
            CloseAudioSidePanel();
        }
    }

    private void OpenAudioSidePanel()
    {
        if (_isAudioPanelOpen && AudioSidePanel.Visibility == Visibility.Visible) return;

        if (_isSubtitlePanelOpen)
        {
            CloseSubtitleSidePanel();
        }

        if (_isPlayQueuePanelOpen)
        {
            ClosePlayQueueSidePanel();
        }

        _isAudioPanelOpen = true;
        UpdateSidePanelState();
        CloseAudioPanelStoryboard.Stop();

        AudioSidePanel.Visibility = Visibility.Visible;
        SubtitlePanelBackdrop.IsHitTestVisible = true;
        AudioSidePanel.OnOpening();

        OpenAudioPanelStoryboard.Begin();
    }

    private void CloseAudioSidePanel()
    {
        if (!_isAudioPanelOpen && AudioSidePanel.Visibility == Visibility.Collapsed) return;

        _isAudioPanelOpen = false;
        UpdateSidePanelState();
        if (!_isSubtitlePanelOpen && !_isPlayQueuePanelOpen)
        {
            SubtitlePanelBackdrop.IsHitTestVisible = false;
        }
        OpenAudioPanelStoryboard.Stop();

        CloseAudioPanelStoryboard.Begin();
    }

    private void CloseAudioPanelStoryboard_OnCompleted(object sender, object e)
    {
        if (!_isAudioPanelOpen)
        {
            try
            {
                AudioSidePanel.OnClosed();
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
            AudioSidePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void TogglePlayQueueSidePanel(bool? forceState = null)
    {
        if (!Dispatcher.HasThreadAccess)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => TogglePlayQueueSidePanel(forceState));
            return;
        }

        bool targetState = forceState ?? !_isPlayQueuePanelOpen;
        if (targetState)
        {
            OpenPlayQueueSidePanel();
        }
        else
        {
            ClosePlayQueueSidePanel();
        }
    }

    private void OpenPlayQueueSidePanel()
    {
        if (_isPlayQueuePanelOpen && PlayQueueSidePanel.Visibility == Visibility.Visible) return;

        if (_isSubtitlePanelOpen)
        {
            CloseSubtitleSidePanel();
        }

        if (_isAudioPanelOpen)
        {
            CloseAudioSidePanel();
        }

        _isPlayQueuePanelOpen = true;
        UpdateSidePanelState();
        ClosePlayQueuePanelStoryboard.Stop();

        PlayQueueSidePanel.Visibility = Visibility.Visible;
        SubtitlePanelBackdrop.IsHitTestVisible = true;
        PlayQueueSidePanel.OnOpening();

        OpenPlayQueuePanelStoryboard.Begin();
    }

    private void ClosePlayQueueSidePanel()
    {
        if (!_isPlayQueuePanelOpen && PlayQueueSidePanel.Visibility == Visibility.Collapsed) return;

        _isPlayQueuePanelOpen = false;
        UpdateSidePanelState();
        if (!_isSubtitlePanelOpen && !_isAudioPanelOpen)
        {
            SubtitlePanelBackdrop.IsHitTestVisible = false;
        }
        OpenPlayQueuePanelStoryboard.Stop();

        ClosePlayQueuePanelStoryboard.Begin();
    }

    private void ClosePlayQueuePanelStoryboard_OnCompleted(object sender, object e)
    {
        if (!_isPlayQueuePanelOpen)
        {
            try
            {
                PlayQueueSidePanel.OnClosed();
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
            PlayQueueSidePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void SubtitlePanelBackdrop_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_isSubtitlePanelOpen) CloseSubtitleSidePanel();
        if (_isAudioPanelOpen) CloseAudioSidePanel();
        if (_isPlayQueuePanelOpen) ClosePlayQueueSidePanel();
    }

}
