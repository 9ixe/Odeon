using Windows.Storage;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Odeon.Core.Messages
{
    public sealed class RaiseFrameSavedNotificationMessage : ValueChangedMessage<StorageFile>
    {
        public RaiseFrameSavedNotificationMessage(StorageFile value) : base(value)
        {
        }
    }
}
