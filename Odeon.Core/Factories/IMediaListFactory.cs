#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Odeon.Core.Models;
using Odeon.Core.ViewModels;
using Windows.Storage;

namespace Odeon.Core.Factories;

public interface IMediaListFactory
{
    /// <summary>
    /// Create a playlist from storage items
    /// </summary>
    Task<NextMediaList?> TryParseMediaListAsync(IReadOnlyList<IStorageItem> storageItems, StorageFile? playNext = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a playlist from a single media item
    /// </summary>
    Task<NextMediaList> ParseMediaListAsync(MediaViewModel media, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a playlist from a storage file
    /// </summary>
    Task<NextMediaList> ParseMediaListAsync(StorageFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a playlist from a URI
    /// </summary>
    Task<NextMediaList> ParseMediaListAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse sub-media items recursively (for playlist files)
    /// </summary>
    Task<List<MediaViewModel>> ParseSubMediaRecursiveAsync(MediaViewModel source, CancellationToken cancellationToken = default);
}
