using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Odeon.Core.Messages;

public class PlayerControlsVisibilityChangedMessage : ValueChangedMessage<bool>
{
    public PlayerControlsVisibilityChangedMessage(bool value) : base(value)
    {
    }
}
