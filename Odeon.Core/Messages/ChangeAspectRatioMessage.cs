using CommunityToolkit.Mvvm.Messaging.Messages;
using Windows.Foundation;

namespace Odeon.Core.Messages
{
    public sealed class ChangeAspectRatioMessage : ValueChangedMessage<Size>
    {
        public ChangeAspectRatioMessage(Size value) : base(value)
        {
        }
    }
}
