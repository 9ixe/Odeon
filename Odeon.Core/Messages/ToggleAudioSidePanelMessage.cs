#nullable enable

namespace Odeon.Core.Messages;

/// <summary>
/// Message sent to toggle the audio side panel open/closed or set to a specific state.
/// </summary>
public sealed class ToggleAudioSidePanelMessage
{
    public bool? ForceState { get; }

    public ToggleAudioSidePanelMessage(bool? forceState = null)
    {
        ForceState = forceState;
    }
}
