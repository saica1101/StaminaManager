using StaminaManager.Core.Abstractions;

namespace StaminaManager.Tests.TestDoubles;

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
