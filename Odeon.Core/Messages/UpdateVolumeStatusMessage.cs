using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Odeon.Core.Messages
{
    public sealed class UpdateVolumeStatusMessage : ValueChangedMessage<int>
    {
        public UpdateVolumeStatusMessage(int value) : base(value)
        {
        }
    }
}
