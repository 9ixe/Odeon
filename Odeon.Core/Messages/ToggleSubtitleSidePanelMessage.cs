#nullable enable

namespace Odeon.Core.Messages;

/// <summary>
/// Message sent to toggle the subtitle side panel open/closed or set to a specific state.
/// </summary>
public sealed class ToggleSubtitleSidePanelMessage
{
    public bool? ForceState { get; }

    public ToggleSubtitleSidePanelMessage(bool? forceState = null)
    {
        ForceState = forceState;
    }
}
