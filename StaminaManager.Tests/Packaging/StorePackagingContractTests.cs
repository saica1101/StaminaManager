using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StaminaManager.Tests.Packaging;

[TestClass]
public sealed class StorePackagingContractTests
{
    private const string ExpectedPackageName = "saica1101.StaminaManager";
    private const string ExpectedPublisher =
        "CN=E42D0651-60BF-47A1-BD3B-ECCF464087D2";
    private const string ExpectedPublisherDisplayName = "saica1101";
    private const string ExpectedMinVersion = "10.0.22000.0";
    private const string LegacyDevelopmentIdentity =
        "35F978A4-DE78-42D1-AA68-26AAB5754821";

    [TestMethod]
    public void Manifest_UsesPartnerCenterIdentity()
    {
        XDocument manifest = LoadManifest();
        XNamespace foundation = manifest.Root!.Name.Namespace;
        XElement identity = manifest.Root.Element(
            foundation + "Identity")!;
        XElement properties = manifest.Root.Element(
            foundation + "Properties")!;

        Assert.AreEqual(
            ExpectedPackageName,
            (string?)identity.Attribute("Name"));
        Assert.AreEqual(
            ExpectedPublisher,
            (string?)identity.Attribute("Publisher"));
        Assert.AreEqual(
            ExpectedPublisherDisplayName,
            properties.Element(foundation + "PublisherDisplayName")?.Value);
    }

    [TestMethod]
    public void Manifest_UsesFourPartNumericVersionAndWindows11Minimum()
    {
        XDocument manifest = LoadManifest();
        XNamespace foundation = manifest.Root!.Name.Namespace;
        XElement identity = manifest.Root.Element(
            foundation + "Identity")!;
        string version = (string?)identity.Attribute("Version") ?? string.Empty;

        StringAssert.Matches(
            version,
            new Regex(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.CultureInvariant));

        foreach (string component in version.Split('.'))
        {
            Assert.IsTrue(
                ushort.TryParse(component, out _),
                $"manifest versionの各要素は0～65535である必要があります: {version}");
        }

        string[] versionComponents = version.Split('.');
        Assert.AreNotEqual(
            (ushort)0,
            ushort.Parse(versionComponents[0]));
        Assert.AreEqual(
            (ushort)0,
            ushort.Parse(versionComponents[3]));

        XElement[] deviceFamilies = manifest
            .Descendants(foundation + "TargetDeviceFamily")
            .ToArray();

        Assert.HasCount(1, deviceFamilies);
        Assert.AreEqual(
            "Windows.Desktop",
            (string?)deviceFamilies[0].Attribute("Name"));
        Assert.AreEqual(
            ExpectedMinVersion,
            (string?)deviceFamilies[0].Attribute("MinVersion"));
    }

    [TestMethod]
    public void Manifest_DoesNotContainLegacyPhoneIdentity()
    {
        string source = File.ReadAllText(GetManifestPath());
        XDocument manifest = XDocument.Parse(source);

        Assert.DoesNotContain(LegacyDevelopmentIdentity, source);
        Assert.IsNull(manifest.Root!.GetNamespaceOfPrefix("mp"));
        Assert.IsFalse(manifest.Descendants().Any(element =>
            element.Name.LocalName == "PhoneIdentity"));
    }

    [TestMethod]
    public void Manifest_StartupTaskReferencesPackagedExecutable()
    {
        XDocument manifest = LoadManifest();
        XElement startupExtension = manifest
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Extension" &&
                (string?)element.Attribute("Category") ==
                    "windows.startupTask");

        Assert.AreEqual(
            "StaminaManager.exe",
            (string?)startupExtension.Attribute("Executable"));
    }

    [TestMethod]
    public void ProjectAndStoreScript_TargetX64Only()
    {
        XDocument project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "StaminaManager.csproj"));
        XDocument publishProfile = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Properties",
            "PublishProfiles",
            "win-x64.pubxml"));
        string script = LoadStoreScript();

        Assert.AreEqual(
            "x64",
            FindProperty(project, "Platforms"));
        Assert.AreEqual(
            "win-x64",
            FindProperty(project, "RuntimeIdentifier"));
        Assert.AreEqual(
            "x64",
            FindProperty(publishProfile, "Platform"));
        Assert.AreEqual(
            "win-x64",
            FindProperty(publishProfile, "RuntimeIdentifier"));
        StringAssert.Contains(script, "Platform=x64");
        StringAssert.Contains(script, "RuntimeIdentifier=win-x64");
    }

    [TestMethod]
    public void StoreScript_BuildsUnsignedSingleProjectUpload()
    {
        string script = LoadStoreScript();

        StringAssert.Contains(script, "GenerateAppxPackageOnBuild=true");
        StringAssert.Contains(script, "AppxPackageSigningEnabled=false");
        StringAssert.Contains(script, "UapAppxPackageBuildMode=StoreOnly");
        StringAssert.Contains(script, ".msixupload");
        Assert.IsFalse(
            Regex.IsMatch(
                script,
                @"(?i)\.pfx|certificate|cert-password|password|STAMINA_CERT"),
            "Store提出物の生成はPFX、証明書、パスワードを要求してはいけません。");
    }

    [TestMethod]
    public void StoreScript_RebuildsUploadStaging()
    {
        string script = LoadStoreScript();

        StringAssert.Contains(script, "'/t:Rebuild'");
        Assert.DoesNotContain("'/t:Build'", script);
        StringAssert.Contains(
            script,
            "[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')");
        StringAssert.Contains(script, "$PID");
        StringAssert.Contains(
            script,
            "\"artifacts\\store\\$Version\\$runId\"");
        StringAssert.Contains(
            script,
            "\"/p:AppxPackageDir=$packageOutputDirectory\\\"");
    }

    [TestMethod]
    public void StoreScript_ValidateOnlyAcceptsCompleteUnsignedUpload()
    {
        using SyntheticStoreUpload fixture = SyntheticStoreUpload.Create(
            "Valid");

        ScriptResult result = ValidateStoreUpload(fixture);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Store upload:");
        StringAssert.Contains(
            result.Output,
            Path.GetFullPath(fixture.UploadPath));
        Assert.IsEmpty(Directory.GetFileSystemEntries(
            fixture.ValidationTempPath));
    }

    [TestMethod]
    [DataRow("MissingAppxSym", "exactly two root entries")]
    [DataRow("ExtraRootEntry", "exactly two root entries")]
    [DataRow("NestedSignature", "must not contain AppxSignature.p7x")]
    [DataRow("MissingBlockMap", "AppxBlockMap.xml is missing")]
    [DataRow("MissingContentTypes", "[Content_Types].xml is missing")]
    [DataRow("ContentModified", "Block hash does not match")]
    [DataRow("OversizedPackage", "nested MSIX exceeds")]
    [DataRow("MissingExecutable", "StaminaManager.exe is missing")]
    [DataRow("NameMismatch", "Name does not match")]
    [DataRow("PublisherMismatch", "Publisher does not match")]
    [DataRow("VersionMismatch", "Version does not match")]
    [DataRow("PublisherDisplayNameMismatch", "PublisherDisplayName does not match")]
    [DataRow("MinVersionMismatch", "Windows.Desktop MinVersion does not match")]
    [DataRow("UniversalDeviceFamily", "exactly one Windows.Desktop")]
    [DataRow("ArchitectureMismatch", "architecture is not x64")]
    [DataRow("StartupTaskMismatch", "StartupTask Executable does not match")]
    public void StoreScript_ValidateOnlyRejectsInvalidUpload(
        string mutation,
        string expectedError)
    {
        using SyntheticStoreUpload fixture = SyntheticStoreUpload.Create(
            mutation);

        ScriptResult result = ValidateStoreUpload(fixture);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.Output, expectedError);
        Assert.IsEmpty(Directory.GetFileSystemEntries(
            fixture.ValidationTempPath));
    }

    [TestMethod]
    public void StoreScript_BuildAndValidateOnlyShareValidationFunction()
    {
        string script = LoadStoreScript();

        Assert.HasCount(
            3,
            Regex.Matches(script, @"\bAssert-StoreUpload\b"),
            "定義、ValidateOnly、build後の3箇所で同じ検証関数を使います。");
    }

    [TestMethod]
    public void StoreScript_LimitsNestedPackageLengthAndCopiedBytes()
    {
        string script = LoadStoreScript();

        StringAssert.Contains(script, "$maxNestedPackageBytes = 128MB");
        StringAssert.Contains(script, "$copiedBytes");
        StringAssert.Contains(
            script,
            "$copiedBytes -gt $maxNestedPackageBytes");
    }

    [TestMethod]
    public void StoreScript_RemovesOnlyFailedRunDirectory()
    {
        string script = LoadStoreScript();

        StringAssert.Contains(script, "$isStorePackageReady = $false");
        StringAssert.Contains(script, "$isStorePackageReady = $true");
        StringAssert.Matches(
            script,
            new Regex(
                @"(?s)finally\s*\{.*?-not \$isStorePackageReady.*?" +
                @"Remove-Item.*?\$packageOutputDirectory",
                RegexOptions.CultureInvariant));
    }

    [TestMethod]
    [DataRow("1.2.3")]
    [DataRow("0.1.0.0")]
    [DataRow("1.0.0.1")]
    public void StoreScript_RejectsInvalidVersionBeforeBuild(string version)
    {
        string scriptPath = GetStoreScriptPath();
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-Version");
        startInfo.ArgumentList.Add(version);

        using Process process = Process.Start(startInfo)!;
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(10_000));

        Assert.AreNotEqual(0, process.ExitCode);
        StringAssert.Contains(
            standardOutput + standardError,
            "Major 1..65535");
        Assert.DoesNotContain("Build succeeded", standardOutput);
    }

    private static ScriptResult ValidateStoreUpload(
        SyntheticStoreUpload fixture)
    {
        ProcessStartInfo startInfo = CreatePowerShellStartInfo();
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(GetStoreScriptPath());
        startInfo.ArgumentList.Add("-ValidateOnly");
        startInfo.ArgumentList.Add("-UploadPath");
        startInfo.ArgumentList.Add(fixture.UploadPath);
        startInfo.ArgumentList.Add("-Version");
        startInfo.ArgumentList.Add("1.0.0.0");
        startInfo.Environment["TEMP"] = fixture.ValidationTempPath;
        startInfo.Environment["TMP"] = fixture.ValidationTempPath;

        using Process process = Process.Start(startInfo)!;
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(10_000));

        return new ScriptResult(
            process.ExitCode,
            standardOutput + standardError);
    }

    private static ProcessStartInfo CreatePowerShellStartInfo()
    {
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = FindRepositoryRoot(),
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        return startInfo;
    }

    private sealed record ScriptResult(int ExitCode, string Output);

    private sealed class SyntheticStoreUpload : IDisposable
    {
        private const long OversizedNestedPackageBytes =
            (128L * 1024 * 1024) + 1;

        private SyntheticStoreUpload(
            string rootPath,
            string uploadPath,
            string validationTempPath)
        {
            RootPath = rootPath;
            UploadPath = uploadPath;
            ValidationTempPath = validationTempPath;
        }

        public string RootPath { get; }

        public string UploadPath { get; }

        public string ValidationTempPath { get; }

        public static SyntheticStoreUpload Create(string mutation)
        {
            string rootPath = Path.Combine(
                Path.GetTempPath(),
                $"StaminaManager.StorePackagingTests-{Guid.NewGuid():N}");
            string validationTempPath = Path.Combine(
                rootPath,
                "validation-temp");
            Directory.CreateDirectory(validationTempPath);

            string msixPath = Path.Combine(
                rootPath,
                "StaminaManager_1.0.0.0_x64.msix");
            string uploadPath = Path.Combine(
                rootPath,
                "StaminaManager_1.0.0.0_x64.msixupload");

            CreateNestedMsix(msixPath, mutation);
            CreateUpload(uploadPath, msixPath, mutation);
            File.Delete(msixPath);

            return new SyntheticStoreUpload(
                rootPath,
                uploadPath,
                validationTempPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static void CreateNestedMsix(
            string msixPath,
            string mutation)
        {
            string name = mutation == "NameMismatch"
                ? "example.Wrong"
                : ExpectedPackageName;
            string publisher = mutation == "PublisherMismatch"
                ? "CN=Wrong"
                : ExpectedPublisher;
            string version = mutation == "VersionMismatch"
                ? "2.0.0.0"
                : "1.0.0.0";
            string displayName = mutation == "PublisherDisplayNameMismatch"
                ? "Wrong Publisher"
                : ExpectedPublisherDisplayName;
            string desktopMinVersion = mutation == "MinVersionMismatch"
                ? "10.0.17763.0"
                : ExpectedMinVersion;
            string architecture = mutation == "ArchitectureMismatch"
                ? "arm64"
                : "x64";
            string startupExecutable = mutation == "StartupTaskMismatch"
                ? "Wrong.exe"
                : "StaminaManager.exe";

            XNamespace foundation =
                "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            XNamespace desktop =
                "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
            XDocument manifest = new(
                new XElement(
                    foundation + "Package",
                    new XAttribute(XNamespace.Xmlns + "desktop", desktop),
                    new XElement(
                        foundation + "Identity",
                        new XAttribute("Name", name),
                        new XAttribute("Publisher", publisher),
                        new XAttribute("Version", version),
                        new XAttribute("ProcessorArchitecture", architecture)),
                    new XElement(
                        foundation + "Properties",
                        new XElement(
                            foundation + "PublisherDisplayName",
                            displayName)),
                    new XElement(
                        foundation + "Dependencies",
                        CreateDeviceFamily(
                            foundation,
                            "Windows.Desktop",
                            desktopMinVersion),
                        mutation == "UniversalDeviceFamily"
                            ? CreateDeviceFamily(
                                foundation,
                                "Windows.Universal",
                                ExpectedMinVersion)
                            : null),
                    new XElement(
                        foundation + "Applications",
                        new XElement(
                            foundation + "Application",
                            new XElement(
                                foundation + "Extensions",
                                new XElement(
                                    desktop + "Extension",
                                    new XAttribute(
                                        "Category",
                                        "windows.startupTask"),
                                    new XAttribute(
                                        "Executable",
                                        startupExecutable)))))));

            Dictionary<string, byte[]> blockMapPayloads = new(
                StringComparer.OrdinalIgnoreCase)
            {
                ["AppxManifest.xml"] = Encoding.UTF8.GetBytes(
                    manifest.ToString(SaveOptions.DisableFormatting)),
            };

            if (mutation != "MissingExecutable")
            {
                blockMapPayloads["StaminaManager.exe"] =
                    Encoding.UTF8.GetBytes("exe");
            }

            Dictionary<string, byte[]> actualPayloads = blockMapPayloads
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
            if (mutation == "ContentModified")
            {
                actualPayloads["StaminaManager.exe"] =
                    Encoding.UTF8.GetBytes("bad");
            }

            using ZipArchive package = ZipFile.Open(
                msixPath,
                ZipArchiveMode.Create);
            foreach ((string entryName, byte[] content) in actualPayloads)
            {
                AddEntry(package, entryName, content);
            }

            if (mutation != "MissingBlockMap")
            {
                AddEntry(
                    package,
                    "AppxBlockMap.xml",
                    CreateBlockMap(blockMapPayloads));
            }

            if (mutation != "MissingContentTypes")
            {
                AddEntry(
                    package,
                    "[Content_Types].xml",
                    CreateContentTypes());
            }

            if (mutation == "NestedSignature")
            {
                AddEntry(
                    package,
                    "AppxSignature.p7x",
                    Encoding.UTF8.GetBytes("signature"));
            }
        }

        private static byte[] CreateBlockMap(
            IReadOnlyDictionary<string, byte[]> payloads)
        {
            XNamespace blockMapNamespace =
                "http://schemas.microsoft.com/appx/2010/blockmap";
            XDocument blockMap = new(
                new XElement(
                    blockMapNamespace + "BlockMap",
                    new XAttribute(
                        "HashMethod",
                        "http://www.w3.org/2001/04/xmlenc#sha256"),
                    payloads.Select(pair => new XElement(
                        blockMapNamespace + "File",
                        new XAttribute(
                            "Name",
                            pair.Key.Replace('/', '\\')),
                        new XAttribute("Size", pair.Value.LongLength),
                        new XAttribute("LfhSize", 0),
                        CreateBlocks(blockMapNamespace, pair.Value)))));

            return Encoding.UTF8.GetBytes(
                blockMap.ToString(SaveOptions.DisableFormatting));
        }

        private static IEnumerable<XElement> CreateBlocks(
            XNamespace blockMapNamespace,
            byte[] content)
        {
            const int blockSize = 64 * 1024;
            for (int offset = 0; offset < content.Length; offset += blockSize)
            {
                int length = Math.Min(blockSize, content.Length - offset);
                byte[] hash = SHA256.HashData(
                    content.AsSpan(offset, length));
                yield return new XElement(
                    blockMapNamespace + "Block",
                    new XAttribute(
                        "Hash",
                        Convert.ToBase64String(hash)));
            }
        }

        private static byte[] CreateContentTypes() => Encoding.UTF8.GetBytes(
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Default Extension=\"exe\" ContentType=\"application/octet-stream\"/>" +
            "</Types>");

        private static XElement CreateDeviceFamily(
            XNamespace foundation,
            string name,
            string minVersion) => new(
                foundation + "TargetDeviceFamily",
                new XAttribute("Name", name),
                new XAttribute("MinVersion", minVersion));

        private static void CreateUpload(
            string uploadPath,
            string msixPath,
            string mutation)
        {
            using ZipArchive upload = ZipFile.Open(
                uploadPath,
                ZipArchiveMode.Create);
            if (mutation == "OversizedPackage")
            {
                ZipArchiveEntry packageEntry = upload.CreateEntry(
                    Path.GetFileName(msixPath),
                    CompressionLevel.Fastest);
                using Stream packageStream = packageEntry.Open();
                byte[] buffer = new byte[64 * 1024];
                long remaining = OversizedNestedPackageBytes;
                while (remaining > 0)
                {
                    int bytesToWrite = (int)Math.Min(
                        buffer.Length,
                        remaining);
                    packageStream.Write(buffer, 0, bytesToWrite);
                    remaining -= bytesToWrite;
                }
            }
            else
            {
                upload.CreateEntryFromFile(
                    msixPath,
                    Path.GetFileName(msixPath));
            }

            if (mutation != "MissingAppxSym")
            {
                AddEntry(
                    upload,
                    "StaminaManager_1.0.0.0_x64.appxsym",
                    Encoding.UTF8.GetBytes("symbols"));
            }

            if (mutation == "ExtraRootEntry")
            {
                AddEntry(
                    upload,
                    "unexpected.txt",
                    Encoding.UTF8.GetBytes("unexpected"));
            }
        }

        private static void AddEntry(
            ZipArchive archive,
            string name,
            byte[] content)
        {
            using Stream stream = archive.CreateEntry(name).Open();
            stream.Write(content, 0, content.Length);
        }
    }

    private static string? FindProperty(XDocument document, string name) =>
        document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == name)
            ?.Value;

    private static XDocument LoadManifest() => XDocument.Load(
        GetManifestPath(),
        LoadOptions.PreserveWhitespace);

    private static string GetManifestPath() => Path.Combine(
        FindRepositoryRoot(),
        "StaminaManager",
        "Package.appxmanifest");

    private static string LoadStoreScript() => File.ReadAllText(
        GetStoreScriptPath());

    private static string GetStoreScriptPath() => Path.Combine(
        FindRepositoryRoot(),
        "BuildStorePackage.ps1");

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
