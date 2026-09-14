#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.WinUI;
using CommunityToolkit.Mvvm.DependencyInjection;
using Odeon.Core.Helpers;
using Odeon.Core.Services;
using Odeon.Core.ViewModels;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace Odeon.Controls;

public sealed partial class SubtitleSidePanel : UserControl
{
    private int _lastSelectedIndex = -1;
    private ListViewItem? _currentlyHoveredItem;
    private bool _isSyncingSelection;

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

        ViewModel.SubtitleTracks.CollectionChanged += (_, _) =>
        {
            RebuildSubtitleDisplayList();
            SyncSelectionAndScroll();
        };

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CompositeTrackPickerViewModel.SubtitleTrackIndex))
            {
                SyncSelectionAndScroll();
            }
        };
    }

    private void SyncSelectionAndScroll()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (Visibility != Visibility.Visible) return;

            if (ViewModel.SubtitleTrackIndex >= 0 && ViewModel.SubtitleTrackIndex < SubtitleDisplayList.Count)
            {
                _isSyncingSelection = true;
                try
                {
                    if (SubtitleTrackListView.SelectedIndex != ViewModel.SubtitleTrackIndex)
                    {
                        SubtitleTrackListView.SelectedIndex = ViewModel.SubtitleTrackIndex;
                    }
                }
                finally
                {
                    _isSyncingSelection = false;
                }

                try
                {
                    SubtitleTrackListView.ScrollIntoView(SubtitleDisplayList[ViewModel.SubtitleTrackIndex]);
                }
                catch (Exception ex)
                {
                    LogService.Log(ex);
                }

                try
                {
                    int targetIndex = ViewModel.SubtitleTrackIndex;
                    for (int i = 0; i < SubtitleTrackListView.Items.Count; i++)
                    {
                        if (SubtitleTrackListView.ContainerFromIndex(i) is ListViewItem container)
                        {
                            if (i == targetIndex)
                            {
                                ApplySingleSelectionVisuals(container);
                            }
                            else
                            {
                                ResetTrackItemVisuals(container);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log(ex);
                }
            }
            else if (ViewModel.SubtitleTrackIndex < 0 || SubtitleDisplayList.Count == 0)
            {
                _isSyncingSelection = true;
                try
                {
                    SubtitleTrackListView.SelectedIndex = -1;
                }
                finally
                {
                    _isSyncingSelection = false;
                }
            }
        });
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
        _lastSelectedIndex = -1;
        _currentlyHoveredItem = null;
        ClearAllHoverStates();

        ViewModel.OnFlyoutOpening();
        RebuildSubtitleDisplayList();
        SyncSelectionAndScroll();

        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (Visibility != Visibility.Visible) return;
            try
            {
                int selectedIndex = ViewModel.SubtitleTrackIndex;
                for (int i = 0; i < SubtitleTrackListView.Items.Count; i++)
                {
                    if (SubtitleTrackListView.ContainerFromIndex(i) is ListViewItem container)
                    {
                        if (i == selectedIndex)
                        {
                            ApplySingleSelectionVisuals(container);
                        }
                        else
                        {
                            ResetTrackItemVisuals(container);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
        });
    }

    private void SubtitleTrackListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;

        if (ViewModel != null && SubtitleTrackListView.SelectedIndex >= 0)
        {
            ViewModel.SubtitleTrackIndex = SubtitleTrackListView.SelectedIndex;
        }

        AnimateSelectionChange(SubtitleTrackListView);
    }

    public void OnClosed()
    {
        _lastSelectedIndex = -1;
        _currentlyHoveredItem = null;
        ClearAllHoverStates();

        for (int i = 0; i < SubtitleTrackListView.Items.Count; i++)
        {
            if (SubtitleTrackListView.ContainerFromIndex(i) is ListViewItem container)
            {
                ResetTrackItemVisuals(container);
            }
        }

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

        var labelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ViewModel.SubtitleTracks.Count; i++)
        {
            string name = GetTrackDisplayName(ViewModel.SubtitleTracks[i], i + 1);
            labelCounts[name] = labelCounts.TryGetValue(name, out int count) ? count + 1 : 1;
        }

        var seenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ViewModel.SubtitleTracks.Count; i++)
        {
            string name = GetTrackDisplayName(ViewModel.SubtitleTracks[i], i + 1);
            if (labelCounts[name] > 1)
            {
                seenCounts[name] = seenCounts.TryGetValue(name, out int count) ? count + 1 : 1;
                newList.Add($"{name} [{seenCounts[name]}]");
            }
            else
            {
                newList.Add(name);
            }
        }

        if (SubtitleDisplayList.SequenceEqual(newList)) return;
        SubtitleDisplayList.SyncItems(newList);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SubtitleTrackListView_OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs e)
    {
        if (e.ItemContainer is ListViewItem container)
        {
            container.PointerEntered -= Container_OnPointerEntered;
            container.PointerExited -= Container_OnPointerExited;
            container.PointerCanceled -= Container_OnPointerCanceled;
            container.PointerCaptureLost -= Container_OnPointerCaptureLost;

            container.PointerEntered += Container_OnPointerEntered;
            container.PointerExited += Container_OnPointerExited;
            container.PointerCanceled += Container_OnPointerCanceled;
            container.PointerCaptureLost += Container_OnPointerCaptureLost;

            int itemIndex = e.ItemIndex;
            bool isSelected = itemIndex >= 0 && itemIndex == ViewModel.SubtitleTrackIndex;
            bool isHovered = container == _currentlyHoveredItem;

            if (isSelected)
            {
                ApplySingleSelectionVisuals(container);
            }
            else
            {
                ResetTrackItemVisuals(container, isHovered);
            }

            if (!isHovered)
            {
                ClearHoverState(container);
            }
        }
    }

    private void Container_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem container)
        {
            SetHoveredItem(container);
        }
    }

    private void Container_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem container && _currentlyHoveredItem == container)
        {
            SetHoveredItem(null);
        }
    }

    private void Container_OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem container && _currentlyHoveredItem == container)
        {
            SetHoveredItem(null);
        }
    }

    private void Container_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem container && _currentlyHoveredItem == container)
        {
            SetHoveredItem(null);
        }
    }

    internal void SubtitleTrackListView_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ClearAllHoverStates();
    }

    private void SetHoveredItem(ListViewItem? container)
    {
        if (_currentlyHoveredItem == container) return;

        ListViewItem? previous = _currentlyHoveredItem;
        _currentlyHoveredItem = container;

        if (previous != null)
            ClearHoverState(previous);

        if (container != null)
            ApplyHoverState(container);
    }

    private static void ApplyHoverState(ListViewItem container)
    {
        if (container.FindDescendant<Border>(b => b.Name == "HoverBackground") is { } hoverBg)
            hoverBg.Opacity = 0.08;
    }

    private static void ClearHoverState(ListViewItem container)
    {
        if (container.FindDescendant<Border>(b => b.Name == "HoverBackground") is { } hoverBg)
        {
            hoverBg.Opacity = 0;
        }
    }

    private void ClearAllHoverStates()
    {
        _currentlyHoveredItem = null;
        for (int i = 0; i < SubtitleTrackListView.Items.Count; i++)
        {
            if (SubtitleTrackListView.ContainerFromIndex(i) is ListViewItem container)
            {
                ClearHoverState(container);
            }
        }
    }

    private void AnimateSelectionChange(ListView listView)
    {
        int selectedIndex = listView.SelectedIndex;
        int previousIndex = _lastSelectedIndex;
        _lastSelectedIndex = selectedIndex;

        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (Visibility != Visibility.Visible) return;
            try
            {
                double travel = previousIndex >= 0 && selectedIndex >= 0
                    ? GetItemExtent(listView) * (selectedIndex - previousIndex)
                    : 0;

                for (int index = 0; index < listView.Items.Count; index++)
                {
                    if (listView.ContainerFromIndex(index) is not ListViewItem itemContainer) continue;

                    bool isHovered = itemContainer == _currentlyHoveredItem;

                    if (itemContainer.IsSelected)
                    {
                        PlayTrackItemSelectionAnimation(itemContainer, travel);
                    }
                    else if (index == previousIndex)
                    {
                        PlayTrackItemDeselectionAnimation(itemContainer, travel);
                        if (!isHovered)
                        {
                            ClearHoverState(itemContainer);
                        }
                    }
                    else
                    {
                        ResetTrackItemVisuals(itemContainer, isHovered);
                    }
                }

                try
                {
                    listView.Focus(FocusState.Programmatic);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
        });
    }

    private static double GetItemExtent(ListView listView)
    {
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

    private static void ApplySingleSelectionVisuals(ListViewItem itemContainer)
    {
        if (itemContainer.FindDescendant<Border>(border => border.Name == "SelectedBackground") is { } selectedBackground)
            selectedBackground.Opacity = 1;

        if (itemContainer.FindDescendant<Rectangle>(rectangle => rectangle.Name == "SelectionIndicator") is { } selectionIndicator)
        {
            selectionIndicator.Opacity = 1;
            if (selectionIndicator.RenderTransform is ScaleTransform indicatorScale)
                indicatorScale.ScaleY = 1;
        }

        if (GetIndicatorTranslate(itemContainer) is { } indicatorTranslate)
            indicatorTranslate.Y = 0;

        if (itemContainer.FindDescendant<ContentPresenter>(presenter => presenter.Name == "ContentPresenter")?.RenderTransform is TranslateTransform contentTransform)
            contentTransform.X = 4;
    }

    private static void ResetTrackItemVisuals(ListViewItem itemContainer, bool isHovered = false)
    {
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

        if (!isHovered)
            ClearHoverState(itemContainer);
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
            indicatorTranslate.Y = 0;
            if (Math.Abs(travel) > 0.1)
            {
                AddAnimation(storyboard, indicatorTranslate, "Y", -travel, 0, TimeSpan.FromMilliseconds(220), new CubicEase { EasingMode = EasingMode.EaseOut });
            }
        }

        if (itemContainer.FindDescendant<ContentPresenter>(presenter => presenter.Name == "ContentPresenter")?.RenderTransform is TranslateTransform contentTransform)
        {
            contentTransform.X = 4;
            AddAnimation(storyboard, contentTransform, "X", 0, 4, TimeSpan.FromMilliseconds(220), new CubicEase { EasingMode = EasingMode.EaseOut });
        }

        try
        {
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }
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
            indicatorTranslate.Y = travel;
            if (Math.Abs(travel) > 0.1)
            {
                AddAnimation(storyboard, indicatorTranslate, "Y", 0, travel, TimeSpan.FromMilliseconds(220), new CubicEase { EasingMode = EasingMode.EaseOut });
            }
        }

        if (itemContainer.FindDescendant<ContentPresenter>(presenter => presenter.Name == "ContentPresenter")?.RenderTransform is TranslateTransform contentTransform)
        {
            contentTransform.X = 0;
            AddAnimation(storyboard, contentTransform, "X", 4, 0, TimeSpan.FromMilliseconds(140), new CubicEase { EasingMode = EasingMode.EaseIn });
        }

        try
        {
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string propertyPath, double from, double to, TimeSpan duration, EasingFunctionBase? easingFunction = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = easingFunction
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        storyboard.Children.Add(animation);
    }
}
