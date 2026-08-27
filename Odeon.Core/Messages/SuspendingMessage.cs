using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Odeon.Core.Messages
{
    public class SuspendingMessage : CollectionRequestMessage<Task>
    {
    }
}
