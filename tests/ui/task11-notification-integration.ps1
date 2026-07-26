param(
    [Parameter(Mandatory)]
    [string]$AppOutputDirectory,
    [string]$ProcDumpPath,
    [string]$ResultPath =
        "$PSScriptRoot\results\task11-notification-result.json"
)

$ErrorActionPreference = 'Stop'
$fixtureGameName = 'Task11 Notification Integration Game'
$fixtureGameId = [Guid]::NewGuid()
$testError = $null
$restoreError = $null
$activeProcessId = 0
$procDumpProcess = $null

if (-not ('Task11PowerRequest' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Task11PowerRequest
{
    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint executionState);
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

function Wait-ProcessExit {
    param([int]$ProcessId)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($null -eq (Get-Process -Id $ProcessId `
                -ErrorAction SilentlyContinue)) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Process $ProcessId did not exit."
}

function Stop-StaminaManager {
    param([int]$ProcessId)

    if ($ProcessId -eq 0) {
        return
    }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    if ($process.ProcessName -ne 'StaminaManager') {
        throw "Refusing to stop unexpected process $ProcessId."
    }

    Stop-Process -Id $ProcessId -Force
    Wait-ProcessExit $ProcessId
}

function Start-PackagedApp {
    $launch = Invoke-WinApp run $AppOutputDirectory `
        --detach --json | ConvertFrom-Json
    $processId = [int]$launch.ProcessId
    Invoke-WinApp ui wait-for NavOverview -a $processId -t 15000 |
        Out-Null
    return $processId
}

function Wait-LedgerState {
    param(
        [string]$LedgerPath,
        [string]$ExpectedState,
        [int]$ExpectedLeadMinutes)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if (Test-Path -LiteralPath $LedgerPath -PathType Leaf) {
            try {
                $ledger = [IO.File]::ReadAllText($LedgerPath) |
                    ConvertFrom-Json
                $entry = @($ledger.entries) |
                    Where-Object { $_.gameId -eq $fixtureGameId } |
                    Select-Object -First 1
                if ($null -ne $entry `
                    -and $entry.state -eq $ExpectedState `
                    -and $entry.leadMinutes -eq $ExpectedLeadMinutes) {
                    return $entry
                }
            }
            catch {
                # 原子的な置換中は次のポーリングで再読込する。
            }
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Ledger did not reach $ExpectedState/$ExpectedLeadMinutes."
}

function Wait-Notification {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do {
        $output = & winapp ui search $fixtureGameName `
            -a ShellExperienceHost --json 2>$null
        if ($LASTEXITCODE -eq 0) {
            try {
                $search = ($output -join [Environment]::NewLine) |
                    ConvertFrom-Json
                $match = $search.matches |
                    Where-Object { $_.isInvokable } |
                    Select-Object -First 1
                if ($null -ne $match) {
                    $windows = Invoke-WinApp ui list-windows `
                        -a ShellExperienceHost --json | ConvertFrom-Json
                    $toastWindow = $windows |
                        Where-Object {
                            $_.title -eq '新しい通知' `
                                -and $_.height -gt 0
                        } |
                        Select-Object -First 1
                    if ($null -ne $toastWindow) {
                        return [pscustomobject]@{
                            Selector = $match.selector
                            WindowHandle = $toastWindow.hwnd
                        }
                    }
                }
            }
            catch {
                # 通知ホスト更新中の不完全な出力は再試行する。
            }
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'Scheduled notification did not appear within three minutes.'
}

function Wait-ActivatedProcess {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $process = Get-Process StaminaManager `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
        if ($null -ne $process) {
            return [int]$process.Id
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'Notification activation did not launch the app.'
}

function Write-FixtureData {
    param(
        [string]$DataPath,
        [DateTimeOffset]$RecordedAtUtc,
        [int]$RecoveryMinutes,
        [int]$LeadMinutes)

    $fixture = [pscustomobject]@{
        schemaVersion = 1
        games = @([pscustomobject]@{
            id = $fixtureGameId
            name = $fixtureGameName
            baseStamina = 99
            maxStamina = 100
            recoveryMinutes = $RecoveryMinutes
            recordedAtUtc = $RecordedAtUtc.ToString('O')
            imageAssetId = $null
            sortOrder = 0
        })
        settings = [pscustomobject]@{
            theme = 'Light'
            backdrop = 'Solid'
            notificationsEnabled = $true
            notificationLeadMinutes = $LeadMinutes
            closeBehavior = 'Exit'
            startupEnabled = $false
            lastDisplayMode = 'Standard'
            selectedCompactGameId = $fixtureGameId
        }
    }
    [IO.File]::WriteAllText(
        $DataPath,
        ($fixture | ConvertTo-Json -Depth 8 -Compress),
        [Text.UTF8Encoding]::new($false))
}

function Restore-OptionalFile {
    param(
        [string]$Path,
        [bool]$DidExist,
        [byte[]]$Content)

    if ($DidExist) {
        [IO.File]::WriteAllBytes($Path, $Content)
    }
    elseif (Test-Path -LiteralPath $Path -PathType Leaf) {
        $preservedPath = $ResultPath + '.generated-ledger'
        Move-Item -LiteralPath $Path -Destination $preservedPath -Force
    }
}

$resolvedOutput = (Resolve-Path $AppOutputDirectory).Path
$appxPath = (Resolve-Path (Join-Path $resolvedOutput 'AppX')).Path
$package = Get-AppxPackage | Where-Object {
    $_.InstallLocation -eq $appxPath
} | Select-Object -First 1
if ($null -eq $package) {
    throw 'Registered debug package was not found.'
}

$dataRoot = Join-Path $env:LOCALAPPDATA (
    'Packages\' + $package.PackageFamilyName + '\LocalState\Data')
$dataPath = Join-Path $dataRoot 'data.json'
$ledgerPath = Join-Path $dataRoot 'notification-state.json'
if (-not (Test-Path -LiteralPath $dataPath -PathType Leaf)) {
    throw "Data file was not found: $dataPath"
}

$dataOriginal = [IO.File]::ReadAllBytes($dataPath)
$ledgerDidExist = Test-Path -LiteralPath $ledgerPath -PathType Leaf
$ledgerOriginal = if ($ledgerDidExist) {
    [IO.File]::ReadAllBytes($ledgerPath)
} else {
    [byte[]]::new(0)
}

New-Item -ItemType Directory -Force `
    -Path (Split-Path -Parent $ResultPath) | Out-Null
[Task11PowerRequest]::SetThreadExecutionState(2147483649) | Out-Null

try {
    Get-Process StaminaManager -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-StaminaManager ([int]$_.Id) }

    $recordedAtUtc = [DateTimeOffset]::UtcNow.AddSeconds(15)
    Write-FixtureData $dataPath $recordedAtUtc 2 1
    [IO.File]::WriteAllText(
        $ledgerPath,
        '{"schemaVersion":1,"entries":[]}',
        [Text.UTF8Encoding]::new($false))

    $activeProcessId = Start-PackagedApp
    $scheduled = Wait-LedgerState $ledgerPath 'Scheduled' 1
    $expectedNotificationAt = [DateTimeOffset]::new(
        [long]$scheduled.notificationAtUtcTicks,
        [TimeSpan]::Zero)
    if ($expectedNotificationAt -le [DateTimeOffset]::UtcNow) {
        throw 'Fixture notification was not scheduled in the future.'
    }

    Stop-StaminaManager $activeProcessId
    $activeProcessId = 0
    if (-not [string]::IsNullOrWhiteSpace($ProcDumpPath)) {
        $resolvedProcDump = (Resolve-Path $ProcDumpPath).Path
        $dumpDirectory = Join-Path (Split-Path -Parent $ResultPath) `
            'task11-dumps'
        New-Item -ItemType Directory -Path $dumpDirectory -Force |
            Out-Null
        $procDumpProcess = Start-Process `
            -FilePath $resolvedProcDump `
            -ArgumentList @(
                '-accepteula',
                '-ma',
                '-e',
                '-w',
                'StaminaManager.exe',
                $dumpDirectory) `
            -PassThru `
            -WindowStyle Hidden
        Start-Sleep -Seconds 1
        if ($procDumpProcess.HasExited) {
            throw 'ProcDump could not wait for notification activation.'
        }
    }

    $notification = Wait-Notification
    if ($null -ne (Get-Process StaminaManager `
            -ErrorAction SilentlyContinue)) {
        throw 'App was running before notification activation.'
    }

    Invoke-WinApp ui invoke $notification.Selector `
        -w $notification.WindowHandle | Out-Null
    $activeProcessId = Wait-ActivatedProcess
    $cardId = 'GameCard_' + $fixtureGameId.ToString('D')
    Invoke-WinApp ui wait-for $cardId -a $activeProcessId -t 15000 |
        Out-Null

    $focusDeadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $focused = Invoke-WinApp ui get-focused `
            -a $activeProcessId --json | ConvertFrom-Json
        if ($focused.element.automationId -eq $cardId) {
            break
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $focusDeadline)
    if ($focused.element.automationId -ne $cardId) {
        throw "Notification target was not focused: " `
            + $focused.element.automationId
    }

    Stop-StaminaManager $activeProcessId
    $activeProcessId = 0

    Write-FixtureData $dataPath ([DateTimeOffset]::UtcNow) 10 1
    [IO.File]::WriteAllText(
        $ledgerPath,
        '{"schemaVersion":1,"entries":[]}',
        [Text.UTF8Encoding]::new($false))

    $activeProcessId = Start-PackagedApp
    $firstSchedule = Wait-LedgerState $ledgerPath 'Scheduled' 1
    Invoke-WinApp ui invoke NavSettings -a $activeProcessId | Out-Null
    Invoke-WinApp ui scroll-into-view NotificationsToggle `
        -a $activeProcessId | Out-Null
    Invoke-WinApp ui invoke NotificationsToggle `
        -a $activeProcessId | Out-Null
    Wait-LedgerState $ledgerPath 'Suppressed' 1 | Out-Null

    Invoke-WinApp ui invoke NotificationsToggle `
        -a $activeProcessId | Out-Null
    Wait-LedgerState $ledgerPath 'Scheduled' 1 | Out-Null
    Invoke-WinApp ui set-value InputBox 2 -a $activeProcessId | Out-Null
    Invoke-WinApp ui focus NavSettings -a $activeProcessId | Out-Null
    $secondSchedule = Wait-LedgerState $ledgerPath 'Scheduled' 2
    if ($firstSchedule.key -eq $secondSchedule.key) {
        throw 'Lead-minute change did not create a new cycle key.'
    }

    Write-Host 'Task11 notification integration: PASS'
}
catch {
    $testError = $_
}
finally {
    [Task11PowerRequest]::SetThreadExecutionState(2147483648) | Out-Null
    try {
        Stop-StaminaManager $activeProcessId
        $activeProcessId = 0
        if ($null -ne $procDumpProcess `
            -and -not $procDumpProcess.HasExited) {
            Stop-Process -Id $procDumpProcess.Id -Force
        }

        [IO.File]::WriteAllBytes($dataPath, $dataOriginal)
        Restore-OptionalFile $ledgerPath $ledgerDidExist $ledgerOriginal

        $cleanupProcessId = Start-PackagedApp
        Stop-StaminaManager $cleanupProcessId
        [IO.File]::WriteAllBytes($dataPath, $dataOriginal)
        Restore-OptionalFile $ledgerPath $ledgerDidExist $ledgerOriginal
        Write-Host 'Task11 notification restoration: PASS'
    }
    catch {
        $restoreError = $_
    }
}

$result = [pscustomobject]@{
    testPassed = $null -eq $testError
    restorePassed = $null -eq $restoreError
    fixtureGameId = $fixtureGameId
    testError = if ($null -eq $testError) {
        $null
    } else {
        $testError.ToString()
    }
    restoreError = if ($null -eq $restoreError) {
        $null
    } else {
        $restoreError.ToString()
    }
}
[IO.File]::WriteAllText(
    $ResultPath,
    ($result | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))

if ($null -ne $restoreError) {
    throw "Notification data restoration failed: $restoreError"
}

if ($null -ne $testError) {
    throw $testError
}
