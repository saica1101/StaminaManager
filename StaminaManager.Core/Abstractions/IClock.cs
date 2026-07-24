namespace StaminaManager.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
