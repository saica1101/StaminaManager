using StaminaManager.Core.Abstractions;

namespace StaminaManager.Infrastructure.Windows;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
