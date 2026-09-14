#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI;
using Odeon.Core.Contexts;
using Odeon.Core.Helpers;
using Odeon.Core.Services;
using Odeon.Core.ViewModels;
using Odeon.Extensions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace Odeon.Controls;

public sealed partial class PlayQueueSidePanel : UserControl
{
    private int _lastSelectedIndex = -1;
    private ListViewItem? _currentlyHoveredItem;
    private bool _isRebuildingList;
    private bool _isSyncingSelection;

    public event EventHandler? CloseRequested;

    public ObservableCollection<MediaViewModel> QueueItems { get; } = new();

    internal PlayQueuePanelViewModel ViewModel { get; }

    private PlayQueueContext Queue { get; }

    public PlayQueueSidePanel()
    {
        ViewModel = Ioc.Default.GetRequiredService<PlayQueuePanelViewModel>();
        Queue = Ioc.Default.GetRequiredService<PlayQueueContext>();
        DataContext = ViewModel;
        this.InitializeComponent();

        Queue.Items.CollectionChanged += (_, _) =>
        {
            // CollectionChanged can fire from mpv's background thread. Marshal to UI thread
            // before touching QueueItems or the ListView, or we get ArgumentException crashes.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RebuildQueueDisplayList);
        };
        Queue.PropertyChanged += Queue_OnPropertyChanged;
        ViewModel.Selection.PropertyChanged += Selection_OnPropertyChanged;
    }

    private void Queue_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayQueueContext.CurrentItem))
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!ViewModel.Selection.IsSelectionModeActive)
                {
                    UpdateCurrentlyPlayingPill();
                }
            });
        }
        else if (e.PropertyName == nameof(PlayQueueContext.Items))
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                RebuildQueueDisplayList();
                if (!ViewModel.Selection.IsSelectionModeActive)
                {
                    UpdateCurrentlyPlayingPill();
                }
            });
        }
    }

    private void Selection_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.IsSelectionModeActive))
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (Visibility != Visibility.Visible) return;

                if (ViewModel.Selection.IsSelectionModeActive)
                {
                    OnSelectionModeEntered();
                }
                else
                {
                    OnSelectionModeExited();
                }
            });
        }
    }

    public void OnOpening()
    {
        _lastSelectedIndex = -1;
        _currentlyHoveredItem = null;

        _isSyncingSelection = true;
        try
        {
            // Completely reset multi-select state when opening the flyout
            if (ViewModel.Selection.IsSelectionModeActive)
            {
                ViewModel.Selection.IsSelectionModeActive = false;
            }
            ViewModel.Selection.SelectedItems.Clear();
            PlaylistListView.SelectionMode = ListViewSelectionMode.Single;
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }
        finally
        {
            _isSyncingSelection = false;
        }

        RebuildQueueDisplayList();
        UpdateEmptyState();
        ClearAllHoverStates();

        int currentIndex = Queue.CurrentIndex >= 0 && Queue.CurrentIndex < QueueItems.Count
            ? Queue.CurrentIndex
            : -1;

        if (currentIndex >= 0)
        {
            _isSyncingSelection = true;
            try
            {
                PlaylistListView.SelectedIndex = currentIndex;
            }
            finally
            {
                _isSyncingSelection = false;
            }
            _lastSelectedIndex = currentIndex;
            try
            {
                PlaylistListView.ScrollIntoView(QueueItems[currentIndex]);
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (Visibility != Visibility.Visible) return;
                try
                {
                    for (int i = 0; i < PlaylistListView.Items.Count; i++)
                    {
                        if (PlaylistListView.ContainerFromIndex(i) is ListViewItem container)
                        {
                            if (i == currentIndex)
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
        else
        {
            _isSyncingSelection = true;
            try
            {
                PlaylistListView.SelectedIndex = -1;
            }
            finally
            {
                _isSyncingSelection = false;
            }
            try
            {
                for (int i = 0; i < PlaylistListView.Items.Count; i++)
                {
                    if (PlaylistListView.ContainerFromIndex(i) is ListViewItem container)
                    {
                        ResetTrackItemVisuals(container);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
        }

        UpdateDividerVisibility();
    }

    public void OnClosed()
    {
        _isSyncingSelection = true;
        try
        {
            // Fully reset multi-select and hover states on flyout close
            if (ViewModel.Selection.IsSelectionModeActive)
            {
                ViewModel.Selection.IsSelectionModeActive = false;
            }
            ViewModel.Selection.SelectedItems.Clear();

            ClearAllHoverStates();
            PlaylistListView.SelectionMode = ListViewSelectionMode.Single;
            PlaylistListView.SelectedIndex = -1;
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }
        finally
        {
            _isSyncingSelection = false;
        }

        try
        {
            for (int i = 0; i < PlaylistListView.Items.Count; i++)
            {
                if (PlaylistListView.ContainerFromIndex(i) is ListViewItem container)
                {
                    ResetTrackItemVisuals(container);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }

        _lastSelectedIndex = -1;
        _currentlyHoveredItem = null;
    }

    private void OnSelectionModeEntered()
    {
        if (Visibility != Visibility.Visible) return;

        _isSyncingSelection = true;
        try
        {
            PlaylistListView.SelectionMode = ListViewSelectionMode.Multiple;
        }
        finally
        {
            _isSyncingSelection = false;
        }
        ClearAllHoverStates();

        try
        {
            for (int i = 0; i < PlaylistListView.Items.Count; i++)
            {
                if (PlaylistListView.ContainerFromIndex(i) is ListViewItem container)
                {
                    bool isSelected = i < QueueItems.Count && ViewModel.Selection.SelectedItems.Contains(QueueItems[i]);
                    UpdateMultiSelectContainerVisuals(container, isSelected);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }
    }

    private void OnSelectionModeExited()
    {
        if (Visibility != Visibility.Visible) return;

        _isSyncingSelection = true;
        try
        {
            PlaylistListView.SelectionMode = ListViewSelectionMode.Single;
        }
        finally
        {
            _isSyncingSelection = false;
        }
        ClearAllHoverStates();

        try
        {
            for (int i = 0; i < PlaylistListView.Items.Count; i++)
            {
                if (PlaylistListView.ContainerFromIndex(i) is ListViewItem container)
                {
                    ResetTrackItemVisuals(container);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log(ex);
        }

        _lastSelectedIndex = -1;

        int currentIndex = Queue.CurrentIndex >= 0 && Queue.CurrentIndex < QueueItems.Count
            ? Queue.CurrentIndex
            : -1;

        if (currentIndex >= 0)
        {
            _isSyncingSelection = true;
            try
            {
                PlaylistListView.SelectedIndex = currentIndex;
            }
            finally
            {
                _isSyncingSelection = false;
            }
            _lastSelectedIndex = currentIndex;
            try
            {
                if (PlaylistListView.ContainerFromIndex(currentIndex) is ListViewItem curContainer)
                {
                    ApplySingleSelectionVisuals(curContainer);
                }
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
        }
        else
        {
            _isSyncingSelection = true;
            try
            {
                PlaylistListView.SelectedIndex = -1;
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    private void PlaylistListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRebuildingList || _isSyncingSelection) return;

        if (ViewModel.Selection.IsSelectionModeActive)
        {
            foreach (var item in e.RemovedItems)
            {
                if (PlaylistListView.ContainerFromItem(item) is ListViewItem container)
                {
                    UpdateMultiSelectContainerVisuals(container, isSelected: false);
                }
            }

            foreach (var item in e.AddedItems)
            {
                if (PlaylistListView.ContainerFromItem(item) is ListViewItem container)
                {
                    UpdateMultiSelectContainerVisuals(container, isSelected: true);
                }
            }
            return;
        }

        if (PlaylistListView.SelectedIndex >= 0 && PlaylistListView.SelectedIndex < QueueItems.Count)
        {
            MediaViewModel selected = QueueItems[PlaylistListView.SelectedIndex];
            if (Queue.CurrentItem != selected)
            {
                ViewModel.PlaySingleCommand.Execute(selected);
            }
        }
    }

    private void UpdateMultiSelectContainerVisuals(ListViewItem container, bool isSelected)
    {
        // Direct assignment only — no GoToState, no ClearValue on selection properties.
        if (container.FindDescendant<Border>(border => border.Name == "SelectedBackground") is { } selBg)
            selBg.Opacity = isSelected ? 1 : 0;

        if (container.FindDescendant<Rectangle>(rectangle => rectangle.Name == "SelectionIndicator") is { } ind)
        {
            ind.Opacity = isSelected ? 1 : 0;
            if (ind.RenderTransform is ScaleTransform scale)
                scale.ScaleY = isSelected ? 1 : 0;
        }

        if (container.FindDescendant<ContentPresenter>(presenter => presenter.Name == "ContentPresenter")?.RenderTransform is TranslateTransform ct)
            ct.X = isSelected ? 4 : 0;

        if (GetIndicatorTranslate(container) is { } indTrans)
            indTrans.Y = 0;

        // Hover is completely independent from multi-select
        bool isHovered = container == _currentlyHoveredItem;
        if (!isHovered)
            ClearHoverState(container);
        else
            ApplyHoverState(container);
    }

    private void RebuildQueueDisplayList()
    {
        var currentItems = Queue.Items.ToList();
        if (QueueItems.SequenceEqual(currentItems)) return;

        _isRebuildingList = true;
        try
        {
            QueueItems.SyncItems(currentItems);
        }
        finally
        {
            _isRebuildingList = false;
        }

        UpdateEmptyState();
        UpdateDividerVisibility();
    }

    private void UpdateEmptyState()
    {
        bool empty = QueueItems.Count == 0;
        PlaylistListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyMessage.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDividerVisibility()
    {
        bool show = QueueItems.Count > 1;
        for (int i = 0; i < PlaylistListView.Items.Count; i++)
        {
            if (PlaylistListView.ContainerFromIndex(i) is not ListViewItem container) continue;
            SetDividerVisibility(container, show);
        }
    }

    private static void SetDividerVisibility(ListViewItem container, bool show)
    {
        if (container.FindDescendant<Rectangle>(r => r.Name == "ItemDivider") is { } divider)
            divider.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PlaylistListView_OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs e)
    {
        if (e.ItemContainer is ListViewItem container)
        {
            SetDividerVisibility(container, QueueItems.Count > 1);

            container.PointerEntered -= Container_OnPointerEntered;
            container.PointerExited -= Container_OnPointerExited;
            container.PointerCanceled -= Container_OnPointerCanceled;
            container.PointerCaptureLost -= Container_OnPointerCaptureLost;

            container.PointerEntered += Container_OnPointerEntered;
            container.PointerExited += Container_OnPointerExited;
            container.PointerCanceled += Container_OnPointerCanceled;
            container.PointerCaptureLost += Container_OnPointerCaptureLost;

            bool isHovered = container == _currentlyHoveredItem;

            if (ViewModel.Selection.IsSelectionModeActive)
            {
                bool isSelected = e.ItemIndex >= 0 && e.ItemIndex < QueueItems.Count && ViewModel.Selection.SelectedItems.Contains(QueueItems[e.ItemIndex]);
                UpdateMultiSelectContainerVisuals(container, isSelected);
            }
            else
            {
                int currentIndex = Queue.CurrentIndex >= 0 && Queue.CurrentIndex < QueueItems.Count ? Queue.CurrentIndex : -1;
                if (e.ItemIndex >= 0 && e.ItemIndex == currentIndex)
                {
                    ApplySingleSelectionVisuals(container);
                }
                else
                {
                    ResetTrackItemVisuals(container, isHovered);
                }
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

    internal void PlaylistListView_OnPointerExited(object sender, PointerRoutedEventArgs e)
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
        // Directly set HoverBackground opacity — avoids fighting XAML's internal
        // pointer-state machine which uses GoToState("PointerOver") independently.
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
        for (int i = 0; i < PlaylistListView.Items.Count; i++)
        {
            if (PlaylistListView.ContainerFromIndex(i) is ListViewItem container)
            {
                ClearHoverState(container);
            }
        }
    }

    private void UpdateCurrentlyPlayingPill()
    {
        if (Queue.CurrentItem is null) return;
        int newIndex = QueueItems.IndexOf(Queue.CurrentItem);
        if (newIndex < 0) return;

        int previousIndex = _lastSelectedIndex;
        _lastSelectedIndex = newIndex;

        if (Visibility != Visibility.Visible) return;

        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (Visibility != Visibility.Visible) return;

            try
            {
                // Re-validate indices inside the dispatcher: the collection may have changed
                // between when this was queued and when it runs (e.g. during a video switch).
                int safeNewIndex = newIndex < PlaylistListView.Items.Count ? newIndex : -1;
                if (safeNewIndex < 0) return;

                if (PlaylistListView.SelectedIndex != safeNewIndex)
                {
                    _isSyncingSelection = true;
                    try
                    {
                        PlaylistListView.SelectedIndex = safeNewIndex;
                    }
                    finally
                    {
                        _isSyncingSelection = false;
                    }
                }

                try
                {
                    PlaylistListView.ScrollIntoView(QueueItems[safeNewIndex]);
                }
                catch (Exception ex)
                {
                    LogService.Log(ex);
                }

                int safePreviousIndex = previousIndex >= 0 && previousIndex < PlaylistListView.Items.Count
                    ? previousIndex
                    : -1;

                if (safePreviousIndex == safeNewIndex)
                {
                    if (PlaylistListView.ContainerFromIndex(safeNewIndex) is ListViewItem itemContainer)
                    {
                        ApplySingleSelectionVisuals(itemContainer);
                    }
                    return;
                }

                double travel = safePreviousIndex >= 0
                    ? GetItemExtent(PlaylistListView) * (safeNewIndex - safePreviousIndex)
                    : 0;

                for (int index = 0; index < PlaylistListView.Items.Count; index++)
                {
                    if (PlaylistListView.ContainerFromIndex(index) is not ListViewItem itemContainer) continue;

                    bool isHovered = itemContainer == _currentlyHoveredItem;

                    if (index == safeNewIndex)
                    {
                        PlayTrackItemSelectionAnimation(itemContainer, travel);
                    }
                    else if (index == safePreviousIndex)
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
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
        });
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    internal async void PlaylistListView_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.Handled = true;
        IReadOnlyList<IStorageItem>? items = await e.DataView.GetStorageItemsAsync();
        if (items?.Count > 0)
        {
            int insertIndex = PlaylistListView.GetDropIndex(e);
            await ViewModel.EnqueueDroppedItemsAsync(items, insertIndex);
        }
    }

    internal void PlaylistListView_OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.Caption = Strings.Resources.AddToQueue;
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
                // Re-clamp previousIndex in case the list was rebuilt between this call and now.
                int safePreviousIndex = previousIndex >= 0 && previousIndex < listView.Items.Count
                    ? previousIndex
                    : -1;

                if (safePreviousIndex == selectedIndex && selectedIndex >= 0)
                {
                    if (listView.ContainerFromIndex(selectedIndex) is ListViewItem itemContainer)
                    {
                        ApplySingleSelectionVisuals(itemContainer);
                    }
                    return;
                }

                double travel = safePreviousIndex >= 0 && selectedIndex >= 0
                    ? GetItemExtent(listView) * (selectedIndex - safePreviousIndex)
                    : 0;

                for (int index = 0; index < listView.Items.Count; index++)
                {
                    if (listView.ContainerFromIndex(index) is not ListViewItem itemContainer) continue;

                    bool isHovered = itemContainer == _currentlyHoveredItem;

                    if (itemContainer.IsSelected)
                    {
                        PlayTrackItemSelectionAnimation(itemContainer, travel);
                    }
                    else if (index == safePreviousIndex)
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

                // Programmatic focus omitted to avoid focus ring / keyboard navigation side effects
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
        // Do NOT call GoToState("Selected") here — VSM Setters set properties at a
        // precedence that survives ClearValue, causing indicator to bleed onto other items.
        // Manage all selection visuals exclusively through direct property assignment.
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
        // Assign directly — do NOT call ClearValue on selection properties.
        // ClearValue would reveal any still-active VSM "Selected" setter, making the
        // indicator re-appear on items that were previously selected.
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
        if (!itemContainer.IsLoaded)
        {
            ApplySingleSelectionVisuals(itemContainer);
            return;
        }

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
        if (!itemContainer.IsLoaded)
        {
            ResetTrackItemVisuals(itemContainer);
            return;
        }

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
