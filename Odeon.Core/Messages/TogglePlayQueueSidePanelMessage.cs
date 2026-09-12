#nullable enable

namespace Odeon.Core.Messages;

/// <summary>
/// Message sent to toggle the play queue side panel open/closed or set to a specific state.
/// </summary>
public sealed class TogglePlayQueueSidePanelMessage
{
    public bool? ForceState { get; }

    public TogglePlayQueueSidePanelMessage(bool? forceState = null)
    {
        ForceState = forceState;
    }
}