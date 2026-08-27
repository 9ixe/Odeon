using System;

namespace Odeon.Core.Models;

public record struct PositionChangedResult(TimeSpan OldPosition, TimeSpan NewPosition, TimeSpan OriginalPosition, TimeSpan NaturalDuration)
{
}
