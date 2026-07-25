param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [string]$OutputDirectory =
        "$PSScriptRoot\results\task9-appearance"
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
        [Parameter(Mandatory)]
        [string]$ExpectedDiagnostic)

    Select-ComboItem BackdropSelector $Selection
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $actual = Get-RawBackdropDiagnostic
        if ($actual -eq $ExpectedDiagnostic) {
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

New-Item -ItemType Directory -Force -Path $OutputDirectory |
    Out-Null

Invoke-WinApp ui wait-for NavSettings -a $AppPid -t 5000 |
    Out-Null
Invoke-WinApp ui invoke NavSettings -a $AppPid |
    Out-Null
Invoke-WinApp ui wait-for BackdropSelector -a $AppPid -t 5000 |
    Out-Null

$initialTheme = (
    Invoke-WinApp ui get-value ThemeToggle -a $AppPid --json |
        ConvertFrom-Json
).text
if ($initialTheme -ne 'On') {
    Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
        --value On -t 3000 | Out-Null
}

Assert-ActualBackdrop Mica `
    'Microsoft.UI.Xaml.Media.MicaBackdrop|SolidSurface=Collapsed'
Assert-ActualBackdrop Acrylic `
    'Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop|SolidSurface=Collapsed'
Assert-ActualBackdrop Blur `
    'StaminaManager.Infrastructure.Windows.BlurredBackdrop|SolidSurface=Collapsed'
Assert-ActualBackdrop Transparent `
    'WinUIEx.TransparentTintBackdrop|SolidSurface=Collapsed'

Invoke-WinApp ui screenshot -a $AppPid `
    -o (Join-Path $OutputDirectory 'transparent-dark.png') |
    Out-Null

Invoke-WinApp ui focus NavSettings -a $AppPid | Out-Null
$focused = Invoke-WinApp ui get-focused -a $AppPid --json |
    ConvertFrom-Json
if ($focused.element.automationId -ne 'NavSettings') {
    throw "Unexpected focused element: $($focused.element.automationId)"
}

Invoke-WinApp ui hover NavSettings -a $AppPid | Out-Null
Invoke-WinApp ui screenshot -a $AppPid `
    -o (Join-Path $OutputDirectory 'transparent-dark-hover.png') |
    Out-Null

Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
    --value Off -t 3000 | Out-Null
$transparentDiagnostic = Get-RawBackdropDiagnostic
if ($transparentDiagnostic -ne
    'WinUIEx.TransparentTintBackdrop|SolidSurface=Collapsed') {
    throw "Unexpected transparent diagnostic: $transparentDiagnostic"
}
Invoke-WinApp ui screenshot -a $AppPid `
    -o (Join-Path $OutputDirectory 'transparent-light.png') |
    Out-Null

Assert-ActualBackdrop Solid '<null>|SolidSurface=Visible'

if ($initialTheme -eq 'On') {
    Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
        --value On -t 3000 | Out-Null
}

Select-ComboItem BackdropSelector Mica
Write-Host 'Task9 appearance integration: PASS'
