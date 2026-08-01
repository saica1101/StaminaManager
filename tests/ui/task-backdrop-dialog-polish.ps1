param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$resultRoot = Join-Path $PSScriptRoot (
    'results\backdrop-dialog-polish-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $resultRoot | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
$pass = 0
$fail = 0

function Invoke-WinApp {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & winapp ui @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output | Out-String)
    }

    return $output
}

function Test-UI {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action)

    try {
        & $Action
        $script:pass++
        $script:results.Add([pscustomobject]@{
            name = $Name
            status = 'PASS'
        })
    }
    catch {
        $script:fail++
        $script:results.Add([pscustomobject]@{
            name = $Name
            status = 'FAIL'
            detail = $_.Exception.Message
        })
    }
}

function Get-UiValue {
    param([Parameter(Mandatory)][string]$Selector)

    $json = Invoke-WinApp @(
        'get-value', $Selector, '-a', "$AppPid", '--json')
    return (($json | Out-String) | ConvertFrom-Json).text
}

function Set-Backdrop {
    param([Parameter(Mandatory)][string]$Value)

    Invoke-WinApp @('invoke', 'BackdropSelector', '-a', "$AppPid") | Out-Null
    $tree = Invoke-WinApp @(
        'inspect', '-a', "$AppPid", '--interactive', '--depth', '8') |
        Out-String
    $itemPattern = '(?m)^\s*(itm-' + $Value.ToLowerInvariant() `
        + '-[^ ]+) ListItem "' + [regex]::Escape($Value) + '"'
    $match = [regex]::Match($tree, $itemPattern)
    $token = $match.Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Backdrop item was not found: $Value"
    }

    Invoke-WinApp @('invoke', $token, '-a', "$AppPid") | Out-Null
    Invoke-WinApp @(
        'wait-for', 'BackdropSelector', '-a', "$AppPid",
        '--value', $Value, '-t', '3000') | Out-Null
}

function Set-Theme {
    param([Parameter(Mandatory)][ValidateSet('On', 'Off')][string]$Value)

    if ((Get-UiValue 'ThemeToggle') -ne $Value) {
        Invoke-WinApp @('invoke', 'ThemeToggle', '-a', "$AppPid") | Out-Null
    }

    Invoke-WinApp @(
        'wait-for', 'ThemeToggle', '-a', "$AppPid",
        '--value', $Value, '-t', '3000') | Out-Null
}

function Assert-ProcessAlive {
    if (-not (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
        throw 'StaminaManager process exited.'
    }
}

function Show-Settings {
    Invoke-WinApp @('invoke', 'NavSettings', '-a', "$AppPid") | Out-Null
    Invoke-WinApp @(
        'wait-for', 'BackdropSelector', '-a', "$AppPid", '-t', '3000') |
        Out-Null
    Assert-ProcessAlive
}

function Show-Overview {
    Invoke-WinApp @('invoke', 'NavOverview', '-a', "$AppPid") | Out-Null
    Invoke-WinApp @(
        'wait-for', 'AddGameCard', '-a', "$AppPid", '-t', '3000') |
        Out-Null
    Assert-ProcessAlive
}

function Open-AddDialogAndCapture {
    param([Parameter(Mandatory)][string]$ScreenshotName)

    Invoke-WinApp @('invoke', 'NavOverview', '-a', "$AppPid") | Out-Null
    Invoke-WinApp @('wait-for', 'AddGameCard', '-a', "$AppPid", '-t', '3000') |
        Out-Null
    Invoke-WinApp @('invoke', 'AddGameCard', '-a', "$AppPid") | Out-Null
    Invoke-WinApp @(
        'wait-for', 'GameEditorDialog', '-a', "$AppPid", '-t', '3000') |
        Out-Null
    Invoke-WinApp @(
        'screenshot', '-a', "$AppPid", '-o',
        (Join-Path $resultRoot $ScreenshotName)) | Out-Null
    Invoke-WinApp @(
        'invoke', 'GameEditorCancelButton', '-a', "$AppPid") | Out-Null
    Invoke-WinApp @(
        'wait-for', 'GameEditorDialog', '-a', "$AppPid",
        '--gone', '-t', '3000') | Out-Null
}

Show-Settings
$originalTheme = Get-UiValue 'ThemeToggle'
$originalBackdrop = Get-UiValue 'BackdropSelector'

try {
    Test-UI 'Backdrop selector shows only Mica, Acrylic, Solid' {
        Invoke-WinApp @('invoke', 'BackdropSelector', '-a', "$AppPid") |
            Out-Null
        $tree = Invoke-WinApp @(
            'inspect', '-a', "$AppPid", '--interactive', '--depth', '8') |
            Out-String
        foreach ($expected in @('Mica', 'Acrylic', 'Solid')) {
            if ($tree -notmatch ('ListItem "' + $expected + '"')) {
                throw "Missing backdrop item: $expected"
            }
        }

        if ($tree -match 'ListItem "(Blur|Transparent)"') {
            throw 'Blur or Transparent is still visible.'
        }

        Invoke-WinApp @('invoke', 'BackdropSelector', '-a', "$AppPid") |
            Out-Null
    }

    Test-UI 'Backdrops survive repeated Settings and Overview round trips' {
        foreach ($cycle in 1..2) {
            foreach ($backdrop in @('Acrylic', 'Solid', 'Mica')) {
                Set-Backdrop $backdrop
                Show-Overview
                Show-Settings
                if ((Get-UiValue 'BackdropSelector') -ne $backdrop) {
                    throw "Backdrop was not retained: $backdrop"
                }
            }
        }
    }

    Test-UI 'Themes survive repeated Settings and Overview round trips' {
        foreach ($theme in @('On', 'Off', 'On', 'Off')) {
            Set-Theme $theme
            Show-Overview
            Show-Settings
            if ((Get-UiValue 'ThemeToggle') -ne $theme) {
                throw "Theme was not retained: $theme"
            }
        }
    }

    Test-UI 'Add dialog follows Light theme' {
        Invoke-WinApp @('invoke', 'NavSettings', '-a', "$AppPid") | Out-Null
        Set-Theme 'Off'
        Open-AddDialogAndCapture '01-add-light.png'
    }

    Test-UI 'Add dialog follows Dark theme' {
        Invoke-WinApp @('invoke', 'NavSettings', '-a', "$AppPid") | Out-Null
        Set-Theme 'On'
        Open-AddDialogAndCapture '02-add-dark.png'
    }

    Test-UI 'Edit and confirmation delete actions are available' {
        $tree = Invoke-WinApp @(
            'inspect', '-a', "$AppPid", '--interactive', '--depth', '8') |
            Out-String
        $gameToken = [regex]::Match(
            $tree,
            '(?m)^\s*(GameCard_[^ ]+) Button').Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($gameToken)) {
            throw 'No existing game card is available for edit verification.'
        }

        Invoke-WinApp @('invoke', $gameToken, '-a', "$AppPid") | Out-Null
        Invoke-WinApp @(
            'wait-for', 'GameEditorDeleteButton', '-a', "$AppPid", '-t', '3000') |
            Out-Null
        Invoke-WinApp @(
            'screenshot', '-a', "$AppPid", '-o',
            (Join-Path $resultRoot '03-edit-delete.png')) | Out-Null
        Invoke-WinApp @(
            'invoke', 'GameEditorDeleteButton', '-a', "$AppPid") | Out-Null
        Invoke-WinApp @(
            'wait-for', 'DeleteConfirmButton', '-a', "$AppPid", '-t', '3000') |
            Out-Null
        Invoke-WinApp @(
            'screenshot', '-a', "$AppPid", '-o',
            (Join-Path $resultRoot '04-delete-confirmation.png')) | Out-Null
        Invoke-WinApp @('invoke', 'DeleteBackButton', '-a', "$AppPid") |
            Out-Null
        Invoke-WinApp @(
            'wait-for', 'GameEditorDeleteButton', '-a', "$AppPid", '-t', '3000') |
            Out-Null
        Invoke-WinApp @(
            'invoke', 'GameEditorCancelButton', '-a', "$AppPid") | Out-Null
    }

    Test-UI 'Process remains alive after appearance changes' {
        Assert-ProcessAlive
    }
}
finally {
    try {
        Show-Settings
        Set-Theme $originalTheme
        if ((Get-UiValue 'BackdropSelector') -ne $originalBackdrop) {
            Set-Backdrop $originalBackdrop
        }
    }
    catch {
        $fail++
        $results.Add([pscustomobject]@{
            name = 'Restore original appearance settings'
            status = 'FAIL'
            detail = $_.Exception.Message
        })
    }
}

$results | ConvertTo-Json -Depth 4 | Set-Content (
    Join-Path $resultRoot 'results.json')
Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object status -eq 'FAIL' | ForEach-Object {
    Write-Host ("FAIL: {0} - {1}" -f $_.name, $_.detail)
}
Write-Host "Results: $resultRoot"
if ($fail -gt 0) {
    exit 1
}
