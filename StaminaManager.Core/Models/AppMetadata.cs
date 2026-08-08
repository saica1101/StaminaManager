namespace StaminaManager.Core.Models;

public static class AppMetadata
{
    public static Uri GitHubUri { get; } = new(
        "https://github.com/saica1101/StaminaManager");

    public static Uri ReadmeUri { get; } = new(
        "https://github.com/saica1101/StaminaManager/blob/develop/README.md");

    public static bool IsAllowedExternalUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && (string.Equals(
                    uri.OriginalString,
                    GitHubUri.OriginalString,
                    StringComparison.Ordinal)
                || string.Equals(
                    uri.OriginalString,
                    ReadmeUri.OriginalString,
                    StringComparison.Ordinal));
    }
}
