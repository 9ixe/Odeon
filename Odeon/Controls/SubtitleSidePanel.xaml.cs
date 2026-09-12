#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.WinUI;
using CommunityToolkit.Mvvm.DependencyInjection;
using Odeon.Core.Helpers;
using Odeon.Core.ViewModels;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace Odeon.Controls;

public sealed partial class SubtitleSidePanel : UserControl
{
    private int _lastSelectedIndex = -1;

    public event EventHandler? CloseRequested;

    public PlayerControlsViewModel? PlayerControlsViewModel
    {
        get => (PlayerControlsViewModel?)GetValue(PlayerControlsViewModelProperty);
        set => SetValue(PlayerControlsViewModelProperty, value);
    }

    public static readonly DependencyProperty PlayerControlsViewModelProperty =
        DependencyProperty.Register(
            nameof(PlayerControlsViewModel),
            typeof(PlayerControlsViewModel),
            typeof(SubtitleSidePanel),
            new PropertyMetadata(null, OnPlayerControlsViewModelChanged));

    public ObservableCollection<string> SubtitleDisplayList { get; } = new();

    internal CompositeTrackPickerViewModel ViewModel => (CompositeTrackPickerViewModel)DataContext;

    public SubtitleSidePanel()
    {
        PlayerControlsViewModel = Ioc.Default.GetRequiredService<PlayerControlsViewModel>();
        DataContext = Ioc.Default.GetRequiredService<CompositeTrackPickerViewModel>();
        this.InitializeComponent();

        // SettingsSection only exists after InitializeComponent, so the value assigned above
        // (which raised no change notification for an already-set property) is re-applied here.
        ApplySettingsDataContext(PlayerControlsViewModel);

        ViewModel.SubtitleTracks.CollectionChanged += (_, _) => RebuildSubtitleDisplayList();
    }

    private static void OnPlayerControlsViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SubtitleSidePanel)d).ApplySettingsDataContext(e.NewValue);
    }

    /// <summary>
    /// Gives the settings section its own DataContext. The panel's DataContext stays on
    /// <see cref="CompositeTrackPickerViewModel"/> so the track list keeps working; the slider and
    /// toggle bindings in the settings section belong to <see cref="PlayerControlsViewModel"/>.
    /// </summary>
    private void ApplySettingsDataContext(object? viewModel)
    {
        if (SettingsSection is not null)
        {
            SettingsSection.DataContext = viewModel;
        }
    }

    public void OnOpening()
    {
        // Place the indicator on the current track instead of animating it into view.
        _lastSelectedIndex = -1;
        ViewModel.OnFlyoutOpening();
        RebuildSubtitleDisplayList();
        if (ViewModel.SubtitleTrackIndex >= 0 && ViewModel.SubtitleTrackIndex < SubtitleDisplayList.Count)
        {
            SubtitleTrackListView.SelectedIndex = ViewModel.SubtitleTrackIndex;
            SubtitleTrackListView.ScrollIntoView(SubtitleDisplayList[ViewModel.SubtitleTrackIndex]);
        }
    }

    private void SubtitleTrackListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && SubtitleTrackListView.SelectedIndex >= 0)
        {
            ViewModel.SubtitleTrackIndex = SubtitleTrackListView.SelectedIndex;
        }

        AnimateSelectionChange(SubtitleTrackListView);
    }

    public void OnClosed()
    {
        ViewModel.OnFlyoutClosed();
    }

    private static string GetTrackDisplayName(string trackLabel, int oneBasedIndex) =>
        !string.IsNullOrEmpty(trackLabel)
            ? trackLabel
            : Odeon.Strings.Resources.TrackIndex(oneBasedIndex);

    private void RebuildSubtitleDisplayList()
    {
        var newList = new List<string>();
        newList.Add(Odeon.Strings.Resources.Disable);
        for (int i = 0; i < ViewModel.SubtitleTracks.Count; i++)
        {
            newList.Add(GetTrackDisplayName(ViewModel.SubtitleTracks[i], i + 1));
        }

        if (SubtitleDisplayList.SequenceEqual(newList)) return;
        SubtitleDisplayList.SyncItems(newList);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AnimateSelectionChange(ListView listView)
    {
        int selectedIndex = listView.SelectedIndex;
        int previousIndex = _lastSelectedIndex;
        _lastSelectedIndex = selectedIndex;

        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            // Signed vertical distance the indicator travels between the two tracks.
            double travel = previousIndex >= 0 && selectedIndex >= 0
                ? GetItemExtent(listView) * (selectedIndex - previousIndex)
                : 0;

            for (int index = 0; index < listView.Items.Count; index++)
            {
                if (listView.ContainerFromIndex(index) is not ListViewItem itemContainer) continue;

                if (itemContainer.IsSelected)
                {
                    PlayTrackItemSelectionAnimation(itemContainer, travel);
                }
                else if (index == previousIndex)
                {
                    // Deselecting is animated here instead of being left to the item's visual
                    // states, otherwise the previous track keeps its highlight until the panel
                    // is reopened.
                    PlayTrackItemDeselectionAnimation(itemContainer, travel);
                }
                else
                {
                    ResetTrackItemVisuals(itemContainer);
                }
            }

            listView.Focus(FocusState.Programmatic);
        });
    }

    private static double GetItemExtent(ListView listView)
    {
        // The first realized container is used as a template for the row height, since the
        // topmost track can be virtualized while the list is scrolled.
        for (int index = 0; index < listView.Items.Count; index++)
        {
            if (listView.ContainerFromIndex(index) is ListViewItem container)
            {
                return container.ActualHeight + container.Margin.Top + container.Margin.Bottom;
            }
        }

        return 0;
    }

    private static TranslateTransform? GetIndicatorTranslate(ListViewItem itemContainer)
    {
        return itemContainer.FindDescendant<Grid>(grid => grid.Name == "IndicatorHost")?.RenderTransform as TranslateTransform;
    }

    private static void ResetTrackItemVisuals(ListViewItem itemContainer)
    {
        // Only the selection visuals are cleared here. Hover and pressed are owned by the item's
        // own visual states, so touching them would fight the framework and drop a hover that is
        // still under the pointer.
        if (itemContainer.FindDescendant<Border>(border => border.Name == "SelectedBackground") is { } selectedBackground)
            selectedBackground.Opacity = 0;

        if (itemContainer.FindDescendant<Rectangle>(rectangle => rectangle.Name == "SelectionIndicator") is { } selectionIndicator)
        {
            selectionIndicator.Opacity = 0;
            if (selectionIndicator.RenderTransform is ScaleTransform indicatorScale)
                indicatorScale.ScaleY = 0;
        }

        if (GetIndicatorTranslate(itemContainer) is { } indicatorTranslate)
            indicatorTranslate.Y = 0;

        if (itemContainer.FindDescendant<ContentPresenter>(presenter => presenter.Name == "ContentPresenter")?.RenderTransform is TranslateTransform contentTransform)
            contentTransform.X = 0;
    }

    private static void PlayTrackItemSelectionAnimation(ListViewItem itemContainer, double travel)
    {
        var storyboard = new Storyboard();

        if (itemContainer.FindDescendant<Border>(border => border.Name == "SelectedBackground") is { } selectedBackground)
        {
            selectedBackground.Opacity = 1;
            AddAnimation(storyboard, selectedBackground, "Opacity", 0, 1, TimeSpan.FromMilliseconds(180));
        }

        if (itemContainer.FindDescendant<Rectangle>(rectangle => rectangle.Name == "SelectionIndicator") is { } selectionIndicator)
        {
            selectionIndicator.Opacity = 1;
            AddAnimation(storyboard, selectionIndicator, "Opacity", 0, 1, TimeSpan.FromMilliseconds(120));

            if (selectionIndicator.RenderTransform is ScaleTransform indicatorScale)
            {
                indicatorScale.ScaleY = 1;
                AddAnimation(storyboard, indicatorScale, "ScaleY", 0, 1, TimeSpan.FromMilliseconds(220), new CubicEase { EasingMode = EasingMode.EaseOut });
            }
        }

        if (GetIndicatorTranslate(itemContainer) is { } indicatorTranslate)
        {
            // Start the indicator on the previously selected track and slide it into place.
            indicatorTranslate.Y = 0;
            AddAnimation(storyboard, indicatorTranslate, "Y", -travel, 0, TimeSpan.FromMilliseconds(220), new CubicEase { EasingMode = EasingMode.EaseOut });
        }

        if (itemContainer.FindDescendant<ContentPresenter>(presenter => presenter.Name == "ContentPresenter")?.RenderTransform is TranslateTransform contentTransform)
        {
            contentTransform.X = 4;
            AddAnimation(storyboard, contentTransform, "X", 0, 4, TimeSpan.FromMilliseconds(220), new CubicEase { EasingMode = EasingMode.EaseOut });
        }

        storyboard.Begin();
    }

    private static void PlayTrackItemDeselectionAnimation(ListViewItem itemContainer, double travel)
    {
        var storyboard = new Storyboard();

        if (itemContainer.FindDescendant<Border>(border => border.Name == "SelectedBackground") is { } selectedBackground)
        {
            selectedBackground.Opacity = 0;
            AddAnimation(storyboard, selectedBackground, "Opacity", 1, 0, TimeSpan.FromMilliseconds(140));
        }

        if (itemContainer.FindDescendant<Rectangle>(rectangle => rectangle.Name == "SelectionIndicator") is { } selectionIndicator)
        {
            selectionIndicator.Opacity = 0;
            AddAnimation(storyboard, selectionIndicator, "Opacity", 1, 0, TimeSpan.FromMilliseconds(100));

            if (selectionIndicator.RenderTransform is ScaleTransform indicatorScale)
            {
                indicatorScale.ScaleY = 0;
                AddAnimation(storyboard, indicatorScale, "ScaleY", 1, 0, TimeSpan.FromMilliseconds(140), new CubicEase { EasingMode = EasingMode.EaseIn });
            }
        }

        if (GetIndicatorTranslate(itemContainer) is { } indicatorTranslate)
        {
            // Let the indicator carry on towards the newly selected track before it disappears.
            indicatorTranslate.Y = travel;
            AddAnimation(storyboard, indicatorTranslate, "Y", 0, travel, TimeSpan.FromMilliseconds(220), new CubicEase { EasingMode = EasingMode.EaseOut });
        }

        if (itemContainer.FindDescendant<ContentPresenter>(presenter => presenter.Name == "ContentPresenter")?.RenderTransform is TranslateTransform contentTransform)
        {
            contentTransform.X = 0;
            AddAnimation(storyboard, contentTransform, "X", 4, 0, TimeSpan.FromMilliseconds(140), new CubicEase { EasingMode = EasingMode.EaseIn });
        }

        storyboard.Begin();
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string propertyPath, double from, double to, TimeSpan duration, EasingFunctionBase? easingFunction = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            // Callers apply the final value to the element up front, so the animation must not
            // keep holding its own value once it has completed.
            FillBehavior = FillBehavior.Stop,
            EasingFunction = easingFunction
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        storyboard.Children.Add(animation);
    }
}
