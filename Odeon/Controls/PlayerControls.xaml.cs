#nullable enable

using System;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Controls;
using Odeon.Core.Messages;
using Odeon.Core.ViewModels;
using Odeon.Helpers;
using Odeon.Pages;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Odeon.Controls;

public sealed partial class PlayerControls : UserControl
{
    public static readonly DependencyProperty BackgroundTransitionProperty = DependencyProperty.Register(
        nameof(BackgroundTransition),
        typeof(BrushTransition),
        typeof(PlayerControls),
        new PropertyMetadata(null));

    public BrushTransition BackgroundTransition
    {
        get => (BrushTransition)GetValue(BackgroundTransitionProperty);
        set => SetValue(BackgroundTransitionProperty, value);
    }

    public MenuFlyout? PlayerContextMenu => (MenuFlyout?)MoreButton.Flyout;

    internal PlayerControlsViewModel ViewModel => (PlayerControlsViewModel)DataContext;

    internal CommonViewModel Common { get; }

    /// <summary>
    /// Fraction of the window height by which the seek bar is lifted in fullscreen.
    /// </summary>
    private const double FullscreenSeekBarOffsetRatio = 0.015;

    private readonly Windows.System.DispatcherQueueTimer _delayFlyoutOpenTimer;
    private Window? _window;

    public PlayerControls()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<PlayerControlsViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();
        _delayFlyoutOpenTimer = Windows.System.DispatcherQueue.GetForCurrentThread().CreateTimer();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The view model is a singleton, so the subscription is tied to the control's lifetime.
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;

        if (_window is null && Window.Current is { } window)
        {
            _window = window;
            window.SizeChanged += Window_OnSizeChanged;
        }

        UpdateSeekBarOffset();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;

        if (_window is null) return;

        _window.SizeChanged -= Window_OnSizeChanged;
        _window = null;
    }

    private void Window_OnSizeChanged(object sender, WindowSizeChangedEventArgs e) => UpdateSeekBarOffset();

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerControlsViewModel.IsFullscreen))
        {
            UpdateSeekBarOffset();
        }
    }

    /// <summary>
    /// Lifts the seek bar off the bottom edge of the screen in fullscreen. The offset is a fraction of
    /// the window height rather than a fixed number of pixels so that it keeps the same visual weight
    /// on any display size.
    /// </summary>
    private void UpdateSeekBarOffset()
    {
        if (ViewModel is not { } viewModel) return;

        double windowHeight = _window?.Bounds.Height ?? 0;

        SeekBarOffset.Y = viewModel.IsFullscreen && windowHeight > 0
            ? -windowHeight * FullscreenSeekBarOffsetRatio
            : 0;
    }

    private void PlayQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new TogglePlayQueueSidePanelMessage());
    }

    private void AudioButton_OnClick(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new ToggleAudioSidePanelMessage());
    }

    private void CaptionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new ToggleSubtitleSidePanelMessage());
    }

    private void ShowSubtitleOptions()
    {
        WeakReferenceMessenger.Default.Send(new ToggleSubtitleSidePanelMessage(true));
    }

    private void ShowAudioOptions()
    {
        WeakReferenceMessenger.Default.Send(new ToggleAudioSidePanelMessage(true));
    }

    public void FocusFirstButton(FocusState value = FocusState.Programmatic)
    {
        PlayPauseButton.Focus(value);
    }

    private void CustomSpeedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Flyout customSpeedFlyout = (Flyout)Resources["CustomPlaybackSpeedFlyout"];
        customSpeedFlyout.ShowAt(MoreButton);
        if (SpeedSlider.Value != ViewModel.PlaybackRate)
        {
            SpeedSlider.Value = ViewModel.PlaybackRate;
        }
        else
        {
            SelectAlternatePlaybackSpeedItem(ViewModel.PlaybackRate);
        }
    }

    private void CustomAspectRatioMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Flyout customAspectFlyout = (Flyout)Resources["CustomAspectRatioFlyout"];
        customAspectFlyout.ShowAt(MoreButton);
    }

    private void SpeedSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        double newValue = Math.Max(e.NewValue, 0.05);
        if (Math.Abs(SpeedSlider.Value - newValue) > 0.0001)
        {
            SpeedSlider.Value = newValue;
        }

        ViewModel.SetPlaybackRateCommand.Execute(newValue);
        SelectAlternatePlaybackSpeedItem(newValue);
    }

    private void SelectAlternatePlaybackSpeedItem(double playbackSpeed)
    {
        bool isMenuValue = (int)(playbackSpeed * 100) % 25 == 0;
        if (isMenuValue &&
            PlaybackSpeedSubMenu.Items?.FirstOrDefault(x =>
                    x.Tag is double predefinedSpeed && Math.Abs(predefinedSpeed - playbackSpeed) < 0.0001) is
                RadioMenuFlyoutItem matchItem)
        {
            matchItem.IsChecked = true;
        }
        else
        {
            CustomPlaybackSpeedMenuItem.IsChecked = true;
        }
    }

    private void AspectRatioTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        string aspectRatio = AspectRatioTextBox.Text;
        if (!aspectRatio.Contains(':')) return;
        if (AspectRatioSubMenu.Items?.FirstOrDefault(x => (string)x.Tag == aspectRatio) is RadioMenuFlyoutItem
            matchItem)
        {
            matchItem.IsChecked = true;
            matchItem.Command?.Execute(matchItem.CommandParameter);
        }
        else
        {
            CustomAspectRatioMenuItem.IsChecked = true;
            ViewModel.SetAspectRatioCommand.Execute(aspectRatio);
        }
    }

    private void PlayPauseKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Ignore the play/pause shortcut when the spacebar is pressed in mini-player visual state.
        if (args.KeyboardAccelerator.Key == VirtualKey.Space && ViewModel.IsMinimal) return;

        // Ignore Space when any side panel or flyout is open — it should close the flyout, not toggle playback.
        if (args.KeyboardAccelerator.Key == VirtualKey.Space &&
            (PlayerPage.IsAnySidePanelOpen || Windows.UI.Xaml.Media.VisualTreeHelper.GetOpenPopups(Window.Current).Count > 0))
        {
            args.Handled = true;
            return;
        }

        // Override default keyboard accelerator to show badge.
        args.Handled = true;
        ViewModel.PlayPauseWithBadge();
    }

    private void ToggleSubtitleKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var result = ViewModel.ProcessToggleSubtitleKeyDown(args.KeyboardAccelerator.Modifiers);
        args.Handled = result.Handled;
        if (result.Handled)
        {
            string label = !string.IsNullOrEmpty(result.TrackLabel)
                ? result.TrackLabel!
                : Odeon.Strings.Resources.None;
            ViewModel.SendStatusMessage(Odeon.Strings.Resources.SubtitleStatus(label));
        }
    }

    private void AudioKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        WeakReferenceMessenger.Default.Send(new ToggleAudioSidePanelMessage());
    }

    private void SubtitleKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        WeakReferenceMessenger.Default.Send(new ToggleSubtitleSidePanelMessage());
    }

    private void PlayQueueButton_OnDragEnter(object sender, DragEventArgs e)
    {
        // Add visual feedback for drag enter if needed
    }

    private void PlayQueueButton_OnDragLeave(object sender, DragEventArgs e)
    {
        // Remove visual feedback for drag leave if needed
    }
}

