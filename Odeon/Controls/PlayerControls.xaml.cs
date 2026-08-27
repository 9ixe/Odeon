#nullable enable

using System;
using System.Linq;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Controls;
using Odeon.Core.ViewModels;
using Odeon.Helpers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

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

    private readonly Windows.System.DispatcherQueueTimer _delayFlyoutOpenTimer;

    public PlayerControls()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<PlayerControlsViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();
        _delayFlyoutOpenTimer = Windows.System.DispatcherQueue.GetForCurrentThread().CreateTimer();
        AudioTrackPicker.ShowSubtitleOptionsCommand = new RelayCommand(ShowSubtitleOptions);
        AudioTrackPicker.ShowAudioOptionsCommand = new RelayCommand(ShowAudioOptions);
        AudioTrackPicker.ShowVideoSection = false;
        AudioTrackPicker.ShowSubtitleSection = false;
        SubtitleTrackPicker.ShowSubtitleOptionsCommand = new RelayCommand(ShowSubtitleOptions);
        SubtitleTrackPicker.ShowAudioOptionsCommand = new RelayCommand(ShowAudioOptions);
        SubtitleTrackPicker.ShowVideoSection = false;
        SubtitleTrackPicker.ShowAudioSection = false;
    }

    public Flyout GetPlayQueueFlyout() => PlayQueueFlyout;

    private void PlayQueueFlyout_OnOpening(object sender, object e)
    {
        FindName(nameof(PlayQueue));
    }
    
    private async void PlayQueueFlyout_OnOpened(object sender, object e)
    {
        if (PlayQueue == null) return;
        await PlayQueue.SmoothScrollActiveItemIntoViewAsync();
    }

    private void ShowSubtitleOptions()
    {
        CaptionsFlyout.Hide();
        Flyout flyout = (Flyout)Resources["SubtitleOptionsFlyout"];
        flyout.ShowAt(CaptionsButton);
    }

    private void ShowAudioOptions()
    {
        AudioFlyout.Hide();
        Flyout flyout = (Flyout)Resources["AudioOptionsFlyout"];
        flyout.ShowAt(AudioButton);
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
        AudioFlyout.ShowAt(AudioButton);
    }

    private void SubtitleKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CaptionsFlyout.ShowAt(CaptionsButton);
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

