#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI;
using Odeon.Core.Contexts;
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

        Queue.Items.CollectionChanged += (_, _) => RebuildQueueDisplayList();
        Queue.PropertyChanged += Queue_OnPropertyChanged;
    }

    private void Queue_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayQueueContext.CurrentItem))
        {
            UpdateCurrentlyPlayingPill();
        }
    }

    public void OnOpening()
    {
        _lastSelectedIndex = -1;
        RebuildQueueDisplayList();
        UpdateEmptyState();

        int currentIndex = Queue.CurrentIndex >= 0 && Queue.CurrentIndex < QueueItems.Count
            ? Queue.CurrentIndex
            : -1;

        if (currentIndex >= 0)
        {
            PlaylistListView.SelectedIndex = currentIndex;
            PlaylistListView.ScrollIntoView(QueueItems[currentIndex]);
        }
    }

    private void PlaylistListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.Selection.IsSelectionModeActive) return;

        if (PlaylistListView.SelectedIndex >= 0 && PlaylistListView.SelectedIndex < QueueItems.Count)
        {
            MediaViewModel selected = QueueItems[PlaylistListView.SelectedIndex];
            if (Queue.CurrentItem != selected)
            {
                ViewModel.PlaySingleCommand.Execute(selected);
            }
        }

        AnimateSelectionChange(PlaylistListView);
    }

    public void OnClosed()
    {
    }

    private void RebuildQueueDisplayList()
    {
        var currentItems = Queue.Items.ToList();
        if (QueueItems.SequenceEqual(currentItems)) return;
        QueueItems.Clear();
        foreach (var item in currentItems)
        {
            QueueItems.Add(item);
        }
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool empty = QueueItems.Count == 0;
        PlaylistListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyMessage.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCurrentlyPlayingPill()
    {
        if (Queue.CurrentItem is null) return;
        int newIndex = QueueItems.IndexOf(Queue.CurrentItem);
        if (newIndex < 0) return;

        int previousIndex = _lastSelectedIndex;
        _lastSelectedIndex = newIndex;

        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            double travel = previousIndex >= 0
                ? GetItemExtent(PlaylistListView) * (newIndex - previousIndex)
                : 0;

            for (int index = 0; index < PlaylistListView.Items.Count; index++)
            {
                if (PlaylistListView.ContainerFromIndex(index) is not ListViewItem itemContainer) continue;

                if (index == newIndex)
                {
                    PlayTrackItemSelectionAnimation(itemContainer, travel);
                }
                else if (index == previousIndex)
                {
                    PlayTrackItemDeselectionAnimation(itemContainer, travel);
                }
                else
                {
                    ResetTrackItemVisuals(itemContainer);
                }
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
            FillBehavior = FillBehavior.Stop,
            EasingFunction = easingFunction
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        storyboard.Children.Add(animation);
    }
}
