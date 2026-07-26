param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$AppOutputDirectory,
    [string]$DataFilePath,
    [string]$ResultPath = "$env:TEMP\staminamanager-task10-result.json"
)

$ErrorActionPreference = 'Stop'
$trayBehavior = 'タスクトレイへ格納'
$exitBehavior = 'アプリを終了'
$fixtureGameName = 'Task10 Integration Game'

if (-not ('Task10WindowNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Task10WindowNative
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    public static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);
}
'@
}

function Invoke-WinApp {
    $output = & winapp @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }

    return $output
}

function Search-Ui {
    param(
        [string]$Query,
        [string]$App)
    $output = & winapp ui search $Query -a $App --json 2>&1
    try {
        return ($output -join [Environment]::NewLine) |
            ConvertFrom-Json
    }
    catch {
        throw "UI search returned invalid JSON: $output"
    }
}

function Test-AppProcess {
    param([int]$ProcessId)
    return $null -ne (Get-Process -Id $ProcessId `
        -ErrorAction SilentlyContinue)
}

function Wait-AppProcessExit {
    param([int]$ProcessId)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if (-not (Test-AppProcess $ProcessId)) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Process $ProcessId did not exit."
}

function Wait-MainWindow {
    param(
        [int]$ProcessId,
        [bool]$Visible)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $process = Get-Process -Id $ProcessId `
            -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            throw "Process $ProcessId exited unexpectedly."
        }

        if (($process.MainWindowHandle -ne 0) -eq $Visible) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Main window visibility did not become '$Visible'."
}

function Start-PackagedApp {
    $launch = Invoke-WinApp run $AppOutputDirectory `
        --detach --json | ConvertFrom-Json
    $processId = [int]$launch.ProcessId
    Wait-MainWindow $processId $true
    return $processId
}

function Get-ControlValue {
    param(
        [int]$ProcessId,
        [string]$AutomationId)
    return (
        Invoke-WinApp ui get-value $AutomationId `
            -a $ProcessId --json | ConvertFrom-Json
    ).text
}

function Select-ComboItem {
    param(
        [int]$ProcessId,
        [string]$AutomationId,
        [string]$ItemName)
    if ((Get-ControlValue $ProcessId $AutomationId) -eq $ItemName) {
        return
    }

    Invoke-WinApp ui invoke $AutomationId -a $ProcessId | Out-Null
    $item = (Invoke-WinApp ui search $ItemName `
        -a $ProcessId --json | ConvertFrom-Json).matches |
        Where-Object { $_.type -eq 'ListItem' } |
        Select-Object -First 1
    if ($null -eq $item) {
        throw "Combo item was not found: $ItemName"
    }

    Invoke-WinApp ui invoke $item.selector -a $ProcessId | Out-Null
    Invoke-WinApp ui wait-for $AutomationId -a $ProcessId `
        --value $ItemName -t 5000 | Out-Null
}

function Open-Settings {
    param([int]$ProcessId)
    Invoke-WinApp ui wait-for NavSettings -a $ProcessId -t 5000 |
        Out-Null
    Invoke-WinApp ui invoke NavSettings -a $ProcessId | Out-Null
    Invoke-WinApp ui wait-for CloseBehaviorSelector `
        -a $ProcessId -t 5000 | Out-Null
}

function Close-MainWindow {
    param([int]$ProcessId)
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    if ($process.MainWindowHandle -eq 0) {
        throw "Main window for process $ProcessId is hidden."
    }

    $posted = [Task10WindowNative]::PostMessage(
        $process.MainWindowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    if (-not $posted) {
        throw "WM_CLOSE could not be posted to process $ProcessId."
    }
}

function Get-TrayIcon {
    $explorerWindows = Invoke-WinApp ui list-windows `
        -a explorer --json | ConvertFrom-Json
    $taskbar = $explorerWindows |
        Where-Object { $_.className -eq 'Shell_TrayWnd' } |
        Select-Object -First 1
    if ($null -eq $taskbar) {
        throw 'Taskbar window was not found.'
    }

    $search = Search-Ui 'Stamina Manager' explorer
    $overflow = $explorerWindows |
        Where-Object { $_.className -eq 'NotifyIconOverflowWindow' } |
        Select-Object -First 1
    if ($search.matchCount -eq 0 -or $null -eq $overflow) {
        Invoke-WinApp ui invoke 1502 -w $taskbar.hwnd | Out-Null
        Start-Sleep -Milliseconds 300
        $explorerWindows = Invoke-WinApp ui list-windows `
            -a explorer --json | ConvertFrom-Json
        $search = Search-Ui 'Stamina Manager' explorer
        $overflow = $explorerWindows |
            Where-Object { $_.className -eq 'NotifyIconOverflowWindow' } |
            Select-Object -First 1
    }

    $icon = $search.matches | Select-Object -First 1
    if ($null -eq $icon -or $null -eq $overflow) {
        throw 'Stamina Manager tray icon was not found.'
    }

    return [pscustomobject]@{
        Icon = $icon
        Overflow = $overflow
    }
}

function Restore-FromTrayByDoubleClick {
    param([int]$ProcessId)
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $tray = Get-TrayIcon
        [Task10WindowNative]::SetForegroundWindow(
            [IntPtr]$tray.Overflow.hwnd) | Out-Null
        $x = [int]($tray.Icon.x + ($tray.Icon.width / 2))
        $y = [int]($tray.Icon.y + ($tray.Icon.height / 2))
        [Task10WindowNative]::SetCursorPos($x, $y) | Out-Null
        for ($click = 0; $click -lt 2; $click++) {
            [Task10WindowNative]::mouse_event(
                0x0002, 0, 0, 0, [UIntPtr]::Zero)
            [Task10WindowNative]::mouse_event(
                0x0004, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 50
        }
        Start-Sleep -Milliseconds 300
        if ((Get-Process -Id $ProcessId).MainWindowHandle -ne 0) {
            break
        }
    }

    Wait-MainWindow $ProcessId $true
    Invoke-WinApp ui wait-for NavOverview -a $ProcessId -t 5000 |
        Out-Null
}

function Exit-FromTray {
    param([int]$ProcessId)
    $tray = Get-TrayIcon
    [Task10WindowNative]::SetForegroundWindow(
        [IntPtr]$tray.Overflow.hwnd) | Out-Null
    $x = [int]($tray.Icon.x + ($tray.Icon.width / 2))
    $y = [int]($tray.Icon.y + ($tray.Icon.height / 2))
    [Task10WindowNative]::SetCursorPos($x, $y) | Out-Null
    [Task10WindowNative]::mouse_event(
        0x0008, 0, 0, 0, [UIntPtr]::Zero)
    [Task10WindowNative]::mouse_event(
        0x0010, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100

    $exitItem = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $exitItem = (Search-Ui '終了' $ProcessId).matches |
            Where-Object { $_.type -eq 'MenuItem' } |
            Select-Object -First 1
        if ($null -ne $exitItem) {
            break
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $exitItem) {
        throw 'Tray exit item was not found.'
    }

    $exitX = [int]($exitItem.x + ($exitItem.width / 2))
    $exitY = [int]($exitItem.y + ($exitItem.height / 2))
    [Task10WindowNative]::SetCursorPos($exitX, $exitY) | Out-Null
    [Task10WindowNative]::mouse_event(
        0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [Task10WindowNative]::mouse_event(
        0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Wait-AppProcessExit $ProcessId
}

function Set-StartupToggle {
    param(
        [int]$ProcessId,
        [string]$ExpectedValue)
    $actual = Get-ControlValue $ProcessId StartupToggle
    if ($actual -eq $ExpectedValue) {
        return $true
    }

    Invoke-WinApp ui invoke StartupToggle -a $ProcessId | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $actual = Get-ControlValue $ProcessId StartupToggle
        if ($actual -eq $ExpectedValue) {
            return $true
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    return $false
}

function Restore-Settings {
    param(
        [int]$ProcessId,
        [string]$CloseBehavior,
        [string]$StartupValue)
    $compact = Search-Ui ReturnOverviewButton $ProcessId
    if ($compact.matchCount -gt 0) {
        Invoke-WinApp ui invoke ReturnOverviewButton `
            -a $ProcessId | Out-Null
    }

    Open-Settings $ProcessId
    Select-ComboItem $ProcessId CloseBehaviorSelector $CloseBehavior
    if (-not (Set-StartupToggle $ProcessId $StartupValue)) {
        throw "StartupTask could not be restored to $StartupValue."
    }
}

$resolvedOutput = (Resolve-Path $AppOutputDirectory).Path
if ([string]::IsNullOrWhiteSpace($DataFilePath)) {
    $appxPath = (Resolve-Path (Join-Path $resolvedOutput 'AppX')).Path
    $package = Get-AppxPackage | Where-Object {
        $_.InstallLocation -eq $appxPath
    } | Select-Object -First 1
    if ($null -eq $package) {
        throw 'Registered debug package was not found.'
    }

    $relativeDataPath = 'Packages\' + $package.PackageFamilyName `
        + '\LocalState\Data\data.json'
    $DataFilePath = Join-Path $env:LOCALAPPDATA $relativeDataPath
}

if (-not (Test-Path -LiteralPath $DataFilePath -PathType Leaf)) {
    throw "Data file was not found: $DataFilePath"
}

$originalData = [IO.File]::ReadAllBytes($DataFilePath)
$fixtureGameId = [Guid]::NewGuid()
$currentPid = $AppPid
$initialCloseBehavior = $null
$initialStartup = $null
$testError = $null
$restoreError = $null

try {
    Wait-MainWindow $currentPid $true
    Open-Settings $currentPid
    $initialCloseBehavior = Get-ControlValue `
        $currentPid CloseBehaviorSelector
    $initialStartup = Get-ControlValue $currentPid StartupToggle

    $secondLaunch = Invoke-WinApp run $AppOutputDirectory `
        --detach --json | ConvertFrom-Json
    Wait-AppProcessExit ([int]$secondLaunch.ProcessId)
    $primaryPath = (Get-Process -Id $currentPid).Path
    $matchingProcesses = Get-Process StaminaManager `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $primaryPath }
    if (@($matchingProcesses).Count -ne 1) {
        throw 'Single-instance process count was not one.'
    }
    Open-Settings $currentPid

    $oppositeStartup = if ($initialStartup -eq 'On') { 'Off' } else { 'On' }
    $startupChanged = Set-StartupToggle $currentPid $oppositeStartup
    if ($startupChanged) {
        if (-not (Set-StartupToggle $currentPid $initialStartup)) {
            throw 'StartupTask enable/disable restoration failed.'
        }
    }
    elseif ($initialStartup -ne 'Off') {
        throw 'StartupTask state changed unexpectedly.'
    }
    else {
        Invoke-WinApp ui wait-for SettingsInfoBar `
            -a $currentPid -t 5000 | Out-Null
    }

    Select-ComboItem $currentPid CloseBehaviorSelector $trayBehavior
    Close-MainWindow $currentPid
    Wait-MainWindow $currentPid $false
    $restoreLaunch = Invoke-WinApp run $AppOutputDirectory `
        --detach --json | ConvertFrom-Json
    Wait-AppProcessExit ([int]$restoreLaunch.ProcessId)
    Wait-MainWindow $currentPid $true
    Invoke-WinApp ui wait-for NavOverview -a $currentPid -t 5000 |
        Out-Null

    Close-MainWindow $currentPid
    Wait-MainWindow $currentPid $false
    Exit-FromTray $currentPid

    $fixture = [Text.Encoding]::UTF8.GetString($originalData) |
        ConvertFrom-Json
    $fixture.games = @([pscustomobject]@{
        id = $fixtureGameId
        name = $fixtureGameName
        baseStamina = 40
        maxStamina = 100
        recoveryMinutes = 5
        recordedAtUtc = '2026-07-25T00:00:00+00:00'
        imageAssetId = $null
        sortOrder = 0
    })
    $fixture.settings.closeBehavior = 'Exit'
    $fixture.settings.lastDisplayMode = 'Standard'
    $fixture.settings.selectedCompactGameId = $fixtureGameId
    [IO.File]::WriteAllText(
        $DataFilePath,
        ($fixture | ConvertTo-Json -Depth 8 -Compress),
        [Text.UTF8Encoding]::new($false))

    $currentPid = Start-PackagedApp
    Invoke-WinApp ui invoke CompactModeButton -a $currentPid | Out-Null
    Invoke-WinApp ui wait-for ReturnOverviewButton `
        -a $currentPid -t 5000 | Out-Null
    Invoke-WinApp ui wait-for CompactGameSelector `
        -a $currentPid --value $fixtureGameName -t 5000 | Out-Null
    $compactBefore = Invoke-WinApp ui list-windows `
        -a $currentPid --json | ConvertFrom-Json |
        Select-Object -First 1

    Close-MainWindow $currentPid
    Wait-AppProcessExit $currentPid

    $currentPid = Start-PackagedApp
    Invoke-WinApp ui wait-for ReturnOverviewButton `
        -a $currentPid -t 5000 | Out-Null
    Invoke-WinApp ui wait-for CompactGameSelector `
        -a $currentPid --value $fixtureGameName -t 5000 | Out-Null
    $compactAfter = Invoke-WinApp ui list-windows `
        -a $currentPid --json | ConvertFrom-Json |
        Select-Object -First 1
    if ($compactBefore.width -ne $compactAfter.width `
        -or $compactBefore.height -ne $compactAfter.height) {
        throw 'Compact window bounds were not restored.'
    }

    Write-Host 'Task10 window lifecycle integration: PASS'
}
catch {
    $testError = $_
}
finally {
    try {
        if (-not (Test-AppProcess $currentPid)) {
            $currentPid = Start-PackagedApp
        }
        elseif ((Get-Process -Id $currentPid).MainWindowHandle -eq 0) {
            $redirected = Invoke-WinApp run $AppOutputDirectory `
                --detach --json | ConvertFrom-Json
            Wait-AppProcessExit ([int]$redirected.ProcessId)
            Wait-MainWindow $currentPid $true
        }

        if ($null -ne $initialStartup) {
            Restore-Settings $currentPid $exitBehavior $initialStartup
        }

        if (Test-AppProcess $currentPid) {
            Close-MainWindow $currentPid
            Wait-AppProcessExit $currentPid
        }
    }
    catch {
        $restoreError = $_
    }

    try {
        $remaining = Get-Process -Id $currentPid `
            -ErrorAction SilentlyContinue
        if ($null -ne $remaining `
            -and $remaining.ProcessName -eq 'StaminaManager') {
            Stop-Process -Id $currentPid -Force
            Wait-AppProcessExit $currentPid
        }

        [IO.File]::WriteAllBytes($DataFilePath, $originalData)
        $currentPid = 0
        Write-Host 'Task10 settings restoration: PASS'
    }
    catch {
        if ($null -eq $restoreError) {
            $restoreError = $_
        }
        else {
            $restoreError = "$restoreError; data restore: $_"
        }
    }
}

if ($null -ne $restoreError) {
    $result = [pscustomobject]@{
        testPassed = $null -eq $testError
        restorePassed = $false
        testError = if ($null -eq $testError) {
            $null
        } else { $testError.ToString() }
        restoreError = $restoreError.ToString()
    }
    [IO.File]::WriteAllText(
        $ResultPath,
        ($result | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
    throw "Settings restoration failed: $restoreError"
}

if ($null -ne $testError) {
    $result = [pscustomobject]@{
        testPassed = $false
        restorePassed = $true
        testError = $testError.ToString()
        restoreError = $null
    }
    [IO.File]::WriteAllText(
        $ResultPath,
        ($result | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
    throw $testError
}

$result = [pscustomobject]@{
    testPassed = $true
    restorePassed = $true
    testError = $null
    restoreError = $null
}
[IO.File]::WriteAllText(
    $ResultPath,
    ($result | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))
Write-Host 'Task10 data restoration: PASS'
