#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Collections;

namespace Odeon.Core.Helpers;

public static class CollectionExtensions
{
    public static void ClearItems<T>(this ICollection<T> groups) where T : IList
    {
        foreach (T group in groups)
        {
            group.Clear();
        }
    }

    public static void SyncItems<T>(this IList<T> target, IReadOnlyList<T> reference)
    {
        // 1. Trim target if it has more items than reference
        while (target.Count > reference.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        // 2. Update existing elements in place
        for (int i = 0; i < target.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(target[i], reference[i]))
            {
                target[i] = reference[i];
            }
        }

        // 3. Add any new elements at the end
        for (int i = target.Count; i < reference.Count; i++)
        {
            target.Add(reference[i]);
        }
    }

    public static void SyncObservableGroups<TKey, TValue>(this IList<ObservableGroup<TKey, TValue>> target,
        IReadOnlyList<IGrouping<TKey, TValue>> reference) where TKey : notnull
    {
        var refDict = reference.ToDictionary(g => g.Key, g => g.ToList());
        var targetDict = target.ToDictionary(g => g.Key, g => g);
        var keysToSync = targetDict.Keys.Where(refDict.ContainsKey);

        // Add & Remove
        var unifiedGroups = reference.Select(g =>
                targetDict.TryGetValue(g.Key, out var targetGroup)
                    ? targetGroup
                    : g as ObservableGroup<TKey, TValue> ?? new ObservableGroup<TKey, TValue>(g))
            .ToList();
        target.SyncItems(unifiedGroups);

        // Sync
        foreach (TKey key in keysToSync)
        {
            targetDict[key].SyncItems(refDict[key]);
        }
    }
}
