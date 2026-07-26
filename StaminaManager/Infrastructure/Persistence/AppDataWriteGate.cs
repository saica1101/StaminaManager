using System.Collections.Concurrent;

namespace StaminaManager.Infrastructure.Persistence;

internal static class AppDataWriteGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        Gates = new(StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim Get(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(pathProvider.DataRootPath));
        return Gates.GetOrAdd(
            root,
            static _ => new SemaphoreSlim(1, 1));
    }
}
