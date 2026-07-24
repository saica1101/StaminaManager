using Windows.Storage;

namespace StaminaManager.Infrastructure.Persistence;

public interface IAppDataPathProvider
{
    string DataRootPath { get; }
}

public sealed class AppDataPathProvider : IAppDataPathProvider
{
    public string DataRootPath => Path.Combine(
        ApplicationData.Current.LocalFolder.Path,
        "Data");
}
