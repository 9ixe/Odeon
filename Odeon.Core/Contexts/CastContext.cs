#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using Odeon.Core.Helpers;
using Odeon.Core.Models;

namespace Odeon.Core.Contexts;

public sealed partial class CastContext : ObservableObject
{
    [ObservableProperty]
    private RendererWatcher? _rendererWatcher;

    [ObservableProperty]
    private Renderer? _activeRenderer;
}
