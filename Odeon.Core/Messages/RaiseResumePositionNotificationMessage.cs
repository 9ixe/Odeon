using CommunityToolkit.Mvvm.Messaging.Messages;
using System;

namespace Odeon.Core.Messages
{
    public class RaiseResumePositionNotificationMessage : ValueChangedMessage<TimeSpan>
    {
        public RaiseResumePositionNotificationMessage(TimeSpan value) : base(value)
        {
        }
    }
}
