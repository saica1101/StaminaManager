using StaminaManager.Core.Abstractions;

namespace StaminaManager.Tests.TestDoubles;

internal sealed class RecordingResourceService(
    IReadOnlyDictionary<string, string>? values = null)
    : IAppResourceService
{
    private readonly IReadOnlyDictionary<string, string> _values =
        values ?? new Dictionary<string, string>();

    public List<string> RequestedResourceIds { get; } = [];

    public string GetString(string resourceId)
    {
        RequestedResourceIds.Add(resourceId);
        return _values.TryGetValue(resourceId, out string? value)
            ? value
            : resourceId;
    }

    public string Format(string resourceId, params object?[] args) =>
        string.Format(GetString(resourceId), args);
}
