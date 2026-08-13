using System.Diagnostics;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class Phase5DUiAutomationContractTests
{
    [TestMethod]
    public void MutatingUiScripts_ExecuteNonStoreGuardForSyntheticPackages()
    {
        string root = FindRepositoryRoot();
        foreach (string fileName in new[]
        {
            "StaminaManager.UiTests.ps1",
            "appearance-navigation-stress.ps1",
            "task9-appearance-integration.ps1",
        })
        {
            string scriptPath = Path.Combine(root, "tests", "ui", fileName);
            RunPowerShell($@"
$scriptPath = $env:STAMINA_UI_SCRIPT
$tokens = $null
$parseErrors = $null
$source = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
$ast = [System.Management.Automation.Language.Parser]::ParseInput(
    $source, $scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {{
    throw ('PowerShell parse failed: ' + $parseErrors[0].Message)
}}
$functions = @($ast.FindAll({{
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Assert-NonStoreTestPackage'
}}, $true))
if ($functions.Count -ne 1) {{
    throw 'Assert-NonStoreTestPackage must have exactly one definition.'
}}
. ([scriptblock]::Create($functions[0].Extent.Text))

function Test-RejectedPackage([object]$package) {{
    try {{
        Assert-NonStoreTestPackage $package
        return $false
    }} catch {{
        return $true
    }}
}}

if (-not (Test-RejectedPackage ([pscustomobject]@{{
    SignatureKind = 'Store'
    InstallLocation = 'D:\dev\StaminaManager\AppX'
}}))) {{
    throw 'Store-signed package was not rejected.'
}}
if (-not (Test-RejectedPackage ([pscustomobject]@{{
    SignatureKind = 'None'
    InstallLocation = 'C:\Program Files\WindowsApps\Fake\AppX'
}}))) {{
    throw 'WindowsApps package was not rejected.'
}}
Assert-NonStoreTestPackage ([pscustomobject]@{{
    SignatureKind = 'None'
    InstallLocation = 'D:\dev\StaminaManager\AppX'
}})
", scriptPath);
        }
    }

    [TestMethod]
    public void UiSuite_EmptyDirectoryFingerprintIsEmptyString()
    {
        string scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1");

        RunPowerShell($@"
$ErrorActionPreference = 'Stop'
$scriptPath = $env:STAMINA_UI_SCRIPT
$tokens = $null
$parseErrors = $null
$source = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
$ast = [System.Management.Automation.Language.Parser]::ParseInput(
    $source, $scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {{
    throw ('PowerShell parse failed: ' + $parseErrors[0].Message)
}}
$functions = @($ast.FindAll({{
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Get-DirectoryFingerprint'
}}, $true))
if ($functions.Count -ne 1) {{
    throw 'Get-DirectoryFingerprint must have exactly one definition.'
}}
. ([scriptblock]::Create($functions[0].Extent.Text))
$directory = Join-Path ([IO.Path]::GetTempPath()) `
    ('stamina-empty-fingerprint-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
try {{
    $value = Get-DirectoryFingerprint $directory
    if ($null -ne $value -and $value -ne '') {{
        throw ('Expected an empty fingerprint, got: ' + $value)
    }}
}}
finally {{
    Remove-Item -LiteralPath $directory -Recurse -Force
}}
'PASS'
", scriptPath);
    }

    [TestMethod]
    public void UiSuite_AcrylicInitialOpacityReadsPersistedData()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1"));

        Assert.DoesNotContain(
            "$initialOpacity = [int](Get-ControlValue AcrylicOpacitySlider)",
            source);
        StringAssert.Contains(source, "acrylicTintOpacityPercent");
    }

    [TestMethod]
    public void UiSuite_SelectComboItemUsesInvokableMatchesOnly()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1"));

        StringAssert.Contains(
            source,
            "Where-Object { $_.isInvokable -eq $true }");
        Assert.DoesNotContain(
            "Where-Object { $_.type -eq 'ListItem' -or $_.isInvokable }",
            source);
    }

    [TestMethod]
    public void UiSuite_AstVerifiesExecutableAppearanceAndLanguageFlows()
    {
        string scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1");

        RunPowerShell($@"
$scriptPath = $env:STAMINA_UI_SCRIPT
$tokens = $null
$parseErrors = $null
$source = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
$ast = [System.Management.Automation.Language.Parser]::ParseInput(
    $source, $scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {{
    throw ('PowerShell parse failed: ' + $parseErrors[0].Message)
}}

function Get-Commands([object]$node) {{
    @($node.FindAll({{
        param($item)
        $item -is [System.Management.Automation.Language.CommandAst]
    }}, $true) | Sort-Object Extent.StartOffset)
}}

function Get-Action([string]$marker) {{
    $invokes = @($ast.FindAll({{
        param($item)
        $item -is [System.Management.Automation.Language.CommandAst] -and
            $item.GetCommandName() -eq 'Invoke-UiTest' -and
            $item.Extent.Text.Contains($marker)
    }}, $true))
    if ($invokes.Count -ne 1) {{
        throw ('Expected one Invoke-UiTest action for ' + $marker)
    }}
    $blocks = @($invokes[0].CommandElements | Where-Object {{
        $_ -is [System.Management.Automation.Language.ScriptBlockExpressionAst]
    }})
    if ($blocks.Count -ne 1) {{
        throw ('Expected one action scriptblock for ' + $marker)
    }}
    return $blocks[0].ScriptBlock
}}

$acrylic = Get-Action 'Acrylic不透明度0/50/100'
if ($null -eq $acrylic) {{ throw 'Acrylic action was not found.' }}
$acrylicCommands = @(Get-Commands $acrylic)
$selectAcrylic = @($acrylicCommands | Where-Object {{
    $_.GetCommandName() -eq 'Select-ComboItem' -and
        $_.Extent.Text -match ""BackdropSelector\s+'Acrylic'""
}} | Select-Object -First 1)
$diagnostic = @($acrylicCommands | Where-Object {{
    $_.GetCommandName() -eq 'Get-RawBackdropDiagnostic'
}} | Select-Object -First 1)
$enabled = @($acrylicCommands | Where-Object {{
    $_.GetCommandName() -eq 'Wait-ControlEnabled' -and
        $_.Extent.Text -match 'AcrylicOpacitySlider\s+\$true'
}} | Select-Object -First 1)
if ($selectAcrylic.Count -ne 1 -or $diagnostic.Count -lt 1 -or
    $enabled.Count -lt 1 -or
    $selectAcrylic[0].Extent.StartOffset -ge
        $diagnostic[0].Extent.StartOffset -or
    $diagnostic[0].Extent.StartOffset -ge
        $enabled[0].Extent.StartOffset) {{
    throw 'Acrylic selection, diagnostic, and enablement order is invalid.'
}}
$restoreThrow = @($acrylic.FindAll({{
    param($item)
    $item -is [System.Management.Automation.Language.ThrowStatementAst] -and
        $item.Extent.Text.Contains('appearanceRestoreError')
}}, $true))
if ($restoreThrow.Count -lt 1) {{
    throw 'Acrylic restore failure is not rethrown to Invoke-UiTest.'
}}
$restoreFailResult = @(Get-Commands $acrylic | Where-Object {{
    $_.GetCommandName() -eq 'Add-Result' -and
        $_.Extent.Text -match ""Acrylic設定のUI復元'.*FAIL""
}})
if ($restoreFailResult.Count -ne 0) {{
    throw 'Acrylic restore failure must not create a child FAIL result.'
}}

$language = Get-Action 'Englishと日本語の再起動反映'
if ($null -eq $language) {{ throw 'Language action was not found.' }}
$languageCommands = @(Get-Commands $language)
foreach ($required in @('Get-LanguageSnapshot', 'Restore-TestLanguage',
        'Restart-TestPackage')) {{
    if (@($languageCommands | Where-Object {{
        $_.GetCommandName() -eq $required
    }}).Count -lt 1) {{
        throw ('Language action does not call ' + $required)
    }}
}}
foreach ($label in @('English', '日本語')) {{
    if (@($languageCommands | Where-Object {{
        $_.GetCommandName() -eq 'Select-ComboItem' -and
            $_.Extent.Text.Contains($label)
    }}).Count -lt 1) {{
        throw ('Language action does not select ' + $label)
    }}
}}
$tryStatements = @($language.FindAll({{
    param($item)
    $item -is [System.Management.Automation.Language.TryStatementAst] -and
        $null -ne $item.Finally
}}, $true))
if ($tryStatements.Count -lt 1) {{
    throw 'Language action has no executable finally restoration.'
}}
$variables = @($ast.FindAll({{
    param($item)
    $item -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $item.VariablePath.UserPath -eq 'languageRestoreError'
}}, $true))
if ($variables.Count -lt 1) {{
    throw 'Language restore error is not represented in the report state.'
}}

$uiDirectory = Split-Path -Parent $scriptPath
foreach ($fileName in @(
        'StaminaManager.UiTests.ps1',
        'appearance-navigation-stress.ps1',
        'task9-appearance-integration.ps1')) {{
    $uiPath = Join-Path $uiDirectory $fileName
    $uiTokens = $null
    $uiParseErrors = $null
    $uiSource = Get-Content -LiteralPath $uiPath -Raw -Encoding UTF8
    $uiAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $uiSource, $uiPath, [ref]$uiTokens, [ref]$uiParseErrors)
    if ($uiParseErrors.Count -gt 0) {{
        throw ('PowerShell parse failed: ' + $uiParseErrors[0].Message)
    }}
    $functionRanges = @($uiAst.FindAll({{
        param($item)
        $item -is [System.Management.Automation.Language.FunctionDefinitionAst]
    }}, $true))
    $topLevelCommands = @($uiAst.FindAll({{
        param($item)
        $item -is [System.Management.Automation.Language.CommandAst]
    }}, $true) | Where-Object {{
        $command = $_
        $insideFunction = @($functionRanges | Where-Object {{
            $command.Extent.StartOffset -ge $_.Extent.StartOffset -and
                $command.Extent.EndOffset -le $_.Extent.EndOffset
        }}).Count -gt 0
        -not $insideFunction
    }} | Sort-Object Extent.StartOffset)
    $guardCalls = @($topLevelCommands | Where-Object {{
        $_.GetCommandName() -eq 'Assert-NonStoreTestPackage'
    }})
    if ($guardCalls.Count -lt 1) {{
        throw ('No top-level package guard call in ' + $fileName)
    }}
    $localStateCalls = @($topLevelCommands | Where-Object {{
        $_.Extent.Text.Contains('LocalState')
    }})
    if ($localStateCalls.Count -gt 0 -and
        $guardCalls[0].Extent.StartOffset -ge
            $localStateCalls[0].Extent.StartOffset) {{
        throw ('Package guard occurs after LocalState access in ' + $fileName)
    }}
}}
'PASS'
", scriptPath);
    }

    private static string RunPowerShell(string command, string scriptPath)
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
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        startInfo.Environment["STAMINA_UI_SCRIPT"] = scriptPath;

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(20_000));
        Assert.AreEqual(0, process.ExitCode, output + error);
        return output + error;
    }

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
