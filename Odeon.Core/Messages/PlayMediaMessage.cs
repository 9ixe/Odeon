using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Odeon.Core.Messages
{
    public sealed class PlayMediaMessage : ValueChangedMessage<object>
    {
        public bool Existing { get; }

        public PlayMediaMessage(object value, bool existing = false) : base(value)
        {
            Existing = existing;
        }
    }
}
