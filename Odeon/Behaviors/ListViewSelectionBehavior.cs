#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Xaml.Interactivity;
using Odeon.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Odeon.Behaviors;

/// <summary>
/// Provides a behavior that synchronizes an external collection of selected items
/// with the <see cref="ListViewBase.SelectedItems"/> collection.
/// </summary>
/// <remarks>
/// The <see cref="ListViewBase.SelectedItems"/> property is a read-only, non-bindable collection,
/// which prevents direct data binding. This behavior enables two-way synchronization of selected
/// items between a <see cref="ListViewBase"/> and an <see cref="IEnumerable"/> collection,
/// such as <see cref="ObservableCollection{object}"/>.
/// </remarks>
/// <example>
/// <code lang="xml"><![CDATA[
/// <ListView ItemsSource="{x:Bind Contacts}">
///     <interactivity:Interaction.Behaviors>
///         <local:ListViewSelectionBehavior SelectedItems="{x:Bind SelectedContacts}" />
///     </interactivity:Interaction.Behaviors>
/// </ListView>
/// ]]></code>
/// </example>
internal sealed class ListViewSelectionBehavior : Behavior<ListViewBase>
{
    /// <summary>
    /// Identifies the <see cref="SelectedItems"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
        nameof(SelectedItems),
        typeof(IEnumerable),
        typeof(ListViewSelectionBehavior),
        new PropertyMetadata(null, OnSelectedItemsChanged));

    /// <summary>
    /// Gets or sets the collection that is synchronized with the selected items
    /// of the associated <see cref="ListViewBase"/>.
    /// </summary>
    /// <value>
    /// An <see cref="IEnumerable"/> collection that reflects the selected items.
    /// The default is <see langword="null"/>.
    /// </value>
    public IEnumerable? SelectedItems
    {
        get => (IEnumerable?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    private bool _isUpdating;

    protected override void OnAttached()
    {
        base.OnAttached();

        AssociatedObject.SelectionChanged += ListViewBase_OnSelectionChanged;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        AssociatedObject.SelectionChanged -= ListViewBase_OnSelectionChanged;

        if (SelectedItems is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnCollectionChanged;
        }
    }

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListViewSelectionBehavior behavior)
        {
            if (e.OldValue is INotifyCollectionChanged oldValue)
            {
                oldValue.CollectionChanged -= behavior.OnCollectionChanged;
            }

            if (e.NewValue is INotifyCollectionChanged newValue)
            {
                newValue.CollectionChanged += behavior.OnCollectionChanged;
            }
        }
    }

    private void ListViewBase_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || SelectedItems is not IList collection)
        {
            return;
        }

        // Only synchronize SelectedItems when the associated control is in Multiple or Extended selection mode.
        // In Single or None mode, mutating or synchronizing the multi-select collection is invalid.
        if (AssociatedObject is null ||
            (AssociatedObject.SelectionMode != ListViewSelectionMode.Multiple &&
             AssociatedObject.SelectionMode != ListViewSelectionMode.Extended))
        {
            return;
        }

        _isUpdating = true;
        try
        {
            foreach (var item in e.RemovedItems)
            {
                collection.Remove(item);
            }

            foreach (var item in e.AddedItems)
            {
                if (!collection.Contains(item))
                {
                    collection.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            Odeon.Core.Services.LogService.Log(ex);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdating || AssociatedObject is not { } listViewBase)
        {
            return;
        }

        // In Single or None selection mode, ListViewBase.SelectedItems is read-only and mutating it
        // throws E_UNEXPECTED (0x8000FFFF). Only synchronize when in Multiple or Extended mode.
        if (listViewBase.SelectionMode != ListViewSelectionMode.Multiple &&
            listViewBase.SelectionMode != ListViewSelectionMode.Extended)
        {
            return;
        }

        _isUpdating = true;
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                        {
                            if (!listViewBase.SelectedItems.Contains(item))
                            {
                                listViewBase.SelectedItems.Add(item);
                            }
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems)
                        {
                            listViewBase.SelectedItems.Remove(item);
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null)
                    {
                        foreach (var oldItem in e.OldItems)
                        {
                            listViewBase.SelectedItems.Remove(oldItem);
                        }
                    }
                    if (e.NewItems != null)
                    {
                        foreach (var newItem in e.NewItems)
                        {
                            if (!listViewBase.SelectedItems.Contains(newItem))
                            {
                                listViewBase.SelectedItems.Add(newItem);
                            }
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Move:
                    // Move doesn't affect selection state, items stay selected if they were previously.
                    break;
                case NotifyCollectionChangedAction.Reset:
                    listViewBase.SelectedItems.Clear();
                    break;
            }
        }
        catch (Exception ex)
        {
            Odeon.Core.Services.LogService.Log(ex);
        }
        finally
        {
            _isUpdating = false;
        }
    }
}
