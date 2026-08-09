using System.Text.RegularExpressions;
using System.Xml.Linq;
using StaminaManager.Infrastructure.Resources;

namespace StaminaManager.Tests.Resources;

[TestClass]
public sealed class ResourceLocalizationContractTests
{
    private static readonly Regex FormatTokenPattern = new(
        @"\{[^{}]+\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [TestMethod]
    public void ResourceFiles_HaveUniqueNonEmptyParityAndMatchingPlaceholders()
    {
        Dictionary<string, string> japanese = LoadResources("ja-JP");
        Dictionary<string, string> english = LoadResources("en-US");

        CollectionAssert.AreEquivalent(
            japanese.Keys.ToArray(),
            english.Keys.ToArray());

        foreach (string key in japanese.Keys)
        {
            CollectionAssert.AreEqual(
                FormatTokens(japanese[key]),
                FormatTokens(english[key]),
                key);
        }
    }

    [TestMethod]
    public void EnglishResources_DoNotContainJapaneseUserFacingCharacters()
    {
        Dictionary<string, string> english = LoadResources("en-US");

        string[] failures = english
            .Where(pair => pair.Value.Any(IsJapaneseCharacter))
            .Select(pair => pair.Key)
            .ToArray();

        Assert.IsEmpty(
            failures,
            "en-USに日本語文字を含むリソースがあります: "
            + string.Join(", ", failures));
    }

    [TestMethod]
    public void Manifest_UserFacingStringsResolveInBothLocales()
    {
        Dictionary<string, string> japanese = LoadResources("ja-JP");
        Dictionary<string, string> english = LoadResources("en-US");
        XDocument manifest = XDocument.Load(GetManifestPath());

        IEnumerable<string> references = manifest
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is
                "DisplayName" or "Description")
            .Select(attribute => attribute.Value)
            .Concat(manifest.Descendants()
                .Where(element => element.Name.LocalName == "DisplayName"
                    && element.Parent?.Name.LocalName == "Properties")
                .Select(element => element.Value));

        foreach (string value in references)
        {
            StringAssert.StartsWith(value, "ms-resource:");
            string resourceId = value["ms-resource:".Length..]
                .TrimStart('/');

            Assert.IsTrue(
                japanese.ContainsKey(resourceId),
                $"ja-JPにmanifestリソース {resourceId} がありません。");
            Assert.IsTrue(
                english.ContainsKey(resourceId),
                $"en-USにmanifestリソース {resourceId} がありません。");
        }
    }

    [TestMethod]
    public void Manifest_DoesNotContainJapaneseUserFacingText()
    {
        string manifest = File.ReadAllText(GetManifestPath());

        Assert.IsFalse(
            manifest.Any(IsJapaneseCharacter),
            "manifestに固定日本語が残っています。");
    }

    [TestMethod]
    public void AppResourceService_GetStringAndFormatResolveValues()
    {
        AppResourceService service = new(resourceId => resourceId switch
        {
            "Greeting" => "Hello",
            "CountFormat" => "Count: {0:00}",
            _ => string.Empty,
        });

        Assert.AreEqual("Hello", service.GetString("Greeting"));
        Assert.AreEqual(
            "Count: 07",
            service.Format("CountFormat", 7));
    }

    [TestMethod]
    public void AppResourceService_UsesSafeResourceIdFallbackForMissingOrInvalidResources()
    {
        AppResourceService missing = new(_ => string.Empty);
        AppResourceService loaderFailure = new(_ =>
            throw new InvalidOperationException("secret loader detail"));
        AppResourceService formatFailure = new(_ => "Broken {0");

        Assert.AreEqual(
            "MissingResource",
            missing.GetString("MissingResource"));
        Assert.AreEqual(
            "MissingResource",
            loaderFailure.GetString("MissingResource"));
        Assert.AreEqual(
            "BrokenFormat",
            loaderFailure.Format("BrokenFormat", 1));
        Assert.AreEqual(
            "BrokenFormat",
            formatFailure.Format("BrokenFormat", 1));
    }

    private static Dictionary<string, string> LoadResources(string language)
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Resources",
            "Strings",
            language,
            "Resources.resw");
        XDocument document = XDocument.Load(path);
        XElement[] entries = document.Root?.Elements("data").ToArray()
            ?? throw new AssertFailedException(
                $"{language}のResources.reswが不正です。");

        string[] duplicateKeys = entries
            .GroupBy(
                entry => (string?)entry.Attribute("name") ?? string.Empty,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.IsEmpty(
            duplicateKeys,
            $"{language}に重複キーがあります: "
            + string.Join(", ", duplicateKeys));

        Dictionary<string, string> values = entries.ToDictionary(
            entry => (string?)entry.Attribute("name")
                ?? throw new AssertFailedException(
                    $"{language}に名前のないdataがあります。"),
            entry => entry.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);

        string[] emptyKeys = values
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();
        Assert.IsEmpty(
            emptyKeys,
            $"{language}に空値があります: "
            + string.Join(", ", emptyKeys));
        return values;
    }

    private static string[] FormatTokens(string value) => FormatTokenPattern
        .Matches(value)
        .Select(match => match.Value)
        .OrderBy(token => token, StringComparer.Ordinal)
        .ToArray();

    private static bool IsJapaneseCharacter(char value) =>
        value is >= '\u3040' and <= '\u30ff'
            or >= '\u3400' and <= '\u4dbf'
            or >= '\u4e00' and <= '\u9fff';

    private static string GetManifestPath() => Path.Combine(
        FindRepositoryRoot(),
        "StaminaManager",
        "Package.appxmanifest");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "StaminaManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new AssertFailedException(
            "リポジトリ ルートを検出できません。");
    }
}
