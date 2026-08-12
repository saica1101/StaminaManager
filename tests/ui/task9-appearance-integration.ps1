param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [string]$OutputDirectory =
        "$PSScriptRoot\results\task9-appearance",
    [switch]$InjectFailureAfterBackdrop
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

function Invoke-WinApp {
    $output = & winapp @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }

    return $output
}

function Assert-NonStoreTestPackage {
    param([Parameter(Mandatory)][object]$Package)

    $installLocation = [IO.Path]::GetFullPath($Package.InstallLocation)
    if ([string]$Package.SignatureKind -eq 'Store' -or
        $installLocation -match '(?i)\\WindowsApps(?:\\|$)') {
        throw 'Store版のPIDはUIテスト対象外です。' +
            '開発用packageをBuildAndRun.ps1で起動してください。'
    }
}

function Get-VerifiedTestPackage {
    $process = Get-Process -Id $AppPid -ErrorAction Stop
    if ($process.ProcessName -ne 'StaminaManager') {
        throw "PID $AppPid is not StaminaManager."
    }

    $processPath = [IO.Path]::GetFullPath($process.Path)
    $package = Get-AppxPackage | Where-Object {
        if ([string]::IsNullOrWhiteSpace($_.InstallLocation)) {
            return $false
        }

        $installLocation = [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\')
        $processPath.StartsWith(
            "$installLocation\",
            [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if ($null -eq $package) {
        throw 'The running executable does not belong to a registered package.'
    }

    Assert-NonStoreTestPackage $package
    return $package
}

function Select-ComboItem {
    param(
        [Parameter(Mandatory)]
        [string]$ComboId,
        [Parameter(Mandatory)]
        [string]$ItemName)

    Invoke-WinApp ui invoke $ComboId -a $AppPid | Out-Null
    $search = Invoke-WinApp ui search $ItemName -a $AppPid --json |
        ConvertFrom-Json
    $item = $search.matches |
        Where-Object { $_.type -eq 'ListItem' } |
        Select-Object -First 1
    if ($null -eq $item) {
        throw "ComboBox item not found: $ItemName"
    }

    Invoke-WinApp ui invoke $item.selector -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for $ComboId -a $AppPid `
        --value $ItemName -t 3000 | Out-Null
}

function Assert-ActualBackdrop {
    param(
        [Parameter(Mandatory)]
        [string]$Selection,
        [string]$ExpectedDiagnostic = '')

    Select-ComboItem BackdropSelector $Selection
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $actual = Get-RawBackdropDiagnostic
        $isAcrylicDiagnostic = $Selection -eq 'Acrylic' -and
            $actual -match '^Acrylic\|TintOpacity=(0\.\d{2}|1\.00)' +
                '\|SolidSurface=Collapsed$'
        if ($actual -eq $ExpectedDiagnostic -or $isAcrylicDiagnostic) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Expected '$ExpectedDiagnostic', actual '$actual'."
}

function Get-RawBackdropDiagnostic {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition =
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::
                ProcessIdProperty,
            $AppPid)
    $window = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $processCondition)
    if ($null -eq $window) {
        throw "Window not found for PID $AppPid."
    }

    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $elements =
        [System.Collections.Generic.Queue[
            System.Windows.Automation.AutomationElement]]::new()
    $elements.Enqueue($window)
    while ($elements.Count -gt 0) {
        $element = $elements.Dequeue()
        if ($element.Current.AutomationId -eq
            'ActualBackdropDiagnostic') {
            return $element.Current.Name
        }

        $child = $walker.GetFirstChild($element)
        while ($null -ne $child) {
            $elements.Enqueue($child)
            $child = $walker.GetNextSibling($child)
        }
    }

    throw 'ActualBackdropDiagnostic was not found in UIA Raw view.'
}

function Get-ControlValue {
    param([Parameter(Mandatory)][string]$AutomationId)

    return (
        Invoke-WinApp ui get-value $AutomationId -a $AppPid --json |
            ConvertFrom-Json
    ).text
}

function Restore-InitialSettings {
    param(
        [Parameter(Mandatory)]
        [string]$ThemeValue,
        [Parameter(Mandatory)]
        [string]$BackdropValue)

    if ((Get-ControlValue ThemeToggle) -ne $ThemeValue) {
        Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
    }

    Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
        --value $ThemeValue -t 3000 | Out-Null
    Select-ComboItem BackdropSelector $BackdropValue
    Invoke-WinApp ui wait-for BackdropSelector -a $AppPid `
        --value $BackdropValue -t 3000 | Out-Null
}

New-Item -ItemType Directory -Force -Path $OutputDirectory |
    Out-Null

$package = Get-VerifiedTestPackage
Assert-NonStoreTestPackage $package

Invoke-WinApp ui wait-for NavSettings -a $AppPid -t 5000 |
    Out-Null
Invoke-WinApp ui invoke NavSettings -a $AppPid |
    Out-Null
Invoke-WinApp ui wait-for BackdropSelector -a $AppPid -t 5000 |
    Out-Null

$initialTheme = Get-ControlValue ThemeToggle
$initialBackdrop = Get-ControlValue BackdropSelector
$testError = $null
$restoreError = $null

try {
    if ($initialTheme -ne 'On') {
        Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
            --value On -t 3000 | Out-Null
    }

    Assert-ActualBackdrop Mica 'Mica|SolidSurface=Collapsed'
    Assert-ActualBackdrop Acrylic

    if ($InjectFailureAfterBackdrop) {
        throw 'Injected failure after backdrop assertions.'
    }

    Invoke-WinApp ui screenshot -a $AppPid `
        -o (Join-Path $OutputDirectory 'acrylic-dark.png') |
        Out-Null

    Invoke-WinApp ui focus NavSettings -a $AppPid | Out-Null
    $focused = Invoke-WinApp ui get-focused -a $AppPid --json |
        ConvertFrom-Json
    if ($focused.element.automationId -ne 'NavSettings') {
        throw "Unexpected focused element: "
            + $focused.element.automationId
    }

    Invoke-WinApp ui hover NavSettings -a $AppPid | Out-Null
    Invoke-WinApp ui screenshot -a $AppPid `
        -o (Join-Path $OutputDirectory 'acrylic-dark-hover.png') |
        Out-Null

    Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
        --value Off -t 3000 | Out-Null
    $acrylicDiagnostic = Get-RawBackdropDiagnostic
    if ($acrylicDiagnostic -notmatch
        '^Acrylic\|TintOpacity=(0\.\d{2}|1\.00)' +
        '\|SolidSurface=Collapsed$') {
        throw "Unexpected acrylic diagnostic: " + $acrylicDiagnostic
    }

    Invoke-WinApp ui screenshot -a $AppPid `
        -o (Join-Path $OutputDirectory 'acrylic-light.png') |
        Out-Null
    Assert-ActualBackdrop Solid 'Solid|SolidSurface=Visible'
}
catch {
    $testError = $_
}
finally {
    try {
        Restore-InitialSettings $initialTheme $initialBackdrop
        Write-Host 'Task9 appearance integration restore: PASS'
    }
    catch {
        $restoreError = $_
    }
}

if ($null -ne $restoreError) {
    throw "Settings restoration failed: $restoreError"
}

if ($null -ne $testError) {
    throw $testError
}

Write-Host 'Task9 appearance integration: PASS'
