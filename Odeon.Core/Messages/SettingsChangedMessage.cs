using System;

namespace Odeon.Core.Messages
{
    public sealed record SettingsChangedMessage(string SettingsName, Type Origin)
    {
        public Type Origin { get; } = Origin;
        public string SettingsName { get; } = SettingsName;
    }
}
