param(
    [int]$AppPid = 0,
    [switch]$ContractOnly,
    [string]$ResultPath =
        "$PSScriptRoot\results\task11-notification-result.json"
)

$ErrorActionPreference = 'Stop'
$fixtureEnabledGameName = 'Task11 Notification Enabled Game'
$fixtureDisabledGameName = 'Task11 Notification Disabled Game'
$fixtureEnabledGameId = [Guid]::NewGuid()
$fixtureDisabledGameId = [Guid]::NewGuid()
$testError = $null
$restoreError = $null
$activeProcessId = $AppPid
$executionMutex = $null
$executionMutexName = $null
$isExecutionMutexAcquired = $false
$notificationTimeToleranceTicks = [TimeSpan]::FromSeconds(1).Ticks

if (-not $ContractOnly -and $AppPid -le 0) {
    throw 'AppPid must be a positive StaminaManager process ID.'
}

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
    $launch = Invoke-WinApp run $resolvedOutput `
        --detach --json | ConvertFrom-Json
    $processId = [int]$launch.ProcessId
    Invoke-WinApp ui wait-for NavOverview -a $processId -t 15000 |
        Out-Null
    return $processId
}

function Wait-LedgerState {
    param(
        [string]$LedgerPath,
        [Guid]$GameId,
        [string]$ExpectedState,
        [int]$ExpectedLeadMinutes)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if (Test-Path -LiteralPath $LedgerPath -PathType Leaf) {
            try {
                $ledger = [IO.File]::ReadAllText($LedgerPath) |
                    ConvertFrom-Json
                $entry = @($ledger.entries) |
                    Where-Object { [Guid]$_.gameId -eq $GameId } |
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

    throw "Ledger did not reach $ExpectedState/$ExpectedLeadMinutes for $GameId."
}

function Write-FixtureData {
    param(
        [string]$DataPath,
        [DateTimeOffset]$RecordedAtUtc,
        [int]$RecoveryMinutes,
        [int]$RecoverySeconds,
        [int]$LeadMinutes)

    $fixture = [pscustomobject]@{
        schemaVersion = 2
        games = @(
            [pscustomobject]@{
                id = $fixtureEnabledGameId
                name = $fixtureEnabledGameName
                baseStamina = 99
                maxStamina = 100
                recoveryMinutes = $RecoveryMinutes
                recoverySeconds = $RecoverySeconds
                recordedAtUtc = $RecordedAtUtc.ToString('O')
                imageAssetId = $null
                sortOrder = 0
                isNotificationEnabled = $true
            },
            [pscustomobject]@{
                id = $fixtureDisabledGameId
                name = $fixtureDisabledGameName
                baseStamina = 99
                maxStamina = 100
                recoveryMinutes = $RecoveryMinutes
                recoverySeconds = $RecoverySeconds
                recordedAtUtc = $RecordedAtUtc.ToString('O')
                imageAssetId = $null
                sortOrder = 1
                isNotificationEnabled = $false
            })
        settings = [pscustomobject]@{
            theme = 'Light'
            backdrop = 'Solid'
            notificationsEnabled = $true
            notificationLeadMinutes = $LeadMinutes
            closeBehavior = 'Exit'
            startupEnabled = $false
            lastDisplayMode = 'Standard'
            selectedCompactGameId = $fixtureEnabledGameId
        }
    }
    [IO.File]::WriteAllText(
        $DataPath,
        ($fixture | ConvertTo-Json -Depth 8 -Compress),
        [Text.UTF8Encoding]::new($false))
}

function Get-ScheduledNotifications {
    return @([Task11ToastProbe]::GetScheduledNotifications(
        $appUserModelId))
}

function Get-HistoryGameIds {
    return @([Task11ToastProbe]::GetHistoryGameIds($appUserModelId) |
        ForEach-Object { [Guid]$_ })
}

function Test-ScheduledLedgerConsistency {
    param(
        [object]$Ledger,
        [object[]]$ScheduledNotifications,
        [long]$TimeToleranceTicks =
            [TimeSpan]::FromSeconds(1).Ticks)

    $scheduledEntries = @($Ledger.entries |
        Where-Object { $_.state -eq 'Scheduled' })
    $staminaSchedules = @($ScheduledNotifications |
        Where-Object { $_.Group -eq 'stamina' })
    if ($scheduledEntries.Count -ne $staminaSchedules.Count) {
        return $false
    }

    $parsedSchedules = foreach ($notification in $staminaSchedules) {
        $gameId = [Guid]::Empty
        if (-not [Guid]::TryParseExact(
                [string]$notification.Tag,
                'N',
                [ref]$gameId)) {
            return $false
        }

        [pscustomobject]@{
            GameId = $gameId
            DeliveryTimeUtcTicks =
                [long]$notification.DeliveryTimeUtcTicks
        }
    }

    foreach ($entry in $scheduledEntries) {
        $matches = @($parsedSchedules |
            Where-Object { $_.GameId -eq [Guid]$entry.gameId })
        if ($matches.Count -ne 1) {
            return $false
        }

        $difference = $matches[0].DeliveryTimeUtcTicks `
            - [long]$entry.notificationAtUtcTicks
        if ($difference -lt -$TimeToleranceTicks `
            -or $difference -gt $TimeToleranceTicks) {
            return $false
        }
    }

    return $true
}

function Test-NotificationStateSnapshot {
    param(
        [object]$Ledger,
        [object[]]$ScheduledNotifications,
        [hashtable]$LedgerStates,
        [long]$TimeToleranceTicks =
            [TimeSpan]::FromSeconds(1).Ticks)

    if (-not (Test-ScheduledLedgerConsistency `
            $Ledger $ScheduledNotifications $TimeToleranceTicks)) {
        return $false
    }

    $staminaSchedules = @($ScheduledNotifications |
        Where-Object { $_.Group -eq 'stamina' })
    foreach ($expected in $LedgerStates.GetEnumerator()) {
        $entries = @($Ledger.entries | Where-Object {
            [Guid]$_.gameId -eq [Guid]$expected.Key -and
            $_.state -eq [string]$expected.Value
        })
        if ($entries.Count -ne 1) {
            return $false
        }

        if ([string]$expected.Value -ne 'Scheduled') {
            $scheduledForGame = @($staminaSchedules |
                Where-Object {
                    $gameId = [Guid]::Empty
                    [Guid]::TryParseExact(
                        [string]$_.Tag,
                        'N',
                        [ref]$gameId) -and
                    $gameId -eq [Guid]$expected.Key
                })
            if ($scheduledForGame.Count -ne 0) {
                return $false
            }
        }
    }

    return $true
}

function Wait-NotificationState {
    param([hashtable]$LedgerStates)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        try {
            $ledger = [IO.File]::ReadAllText($ledgerPath) |
                ConvertFrom-Json
            $scheduledNotifications = @(Get-ScheduledNotifications)
            if (Test-NotificationStateSnapshot `
                    $ledger `
                    $scheduledNotifications `
                    $LedgerStates `
                    $notificationTimeToleranceTicks) {
                return
            }
        }
        catch {
            # ledgerの原子置換またはOS予約更新中は再試行する。
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'Windows scheduled toasts and notification ledger did not converge.'
}

function Assert-RestoredNotificationConsistency {
    $ledger = if (Test-Path -LiteralPath $ledgerPath -PathType Leaf) {
        [IO.File]::ReadAllText($ledgerPath) | ConvertFrom-Json
    }
    else {
        [pscustomobject]@{ schemaVersion = 1; entries = @() }
    }
    $scheduledNotifications = @(Get-ScheduledNotifications)
    if (-not (Test-ScheduledLedgerConsistency `
            $ledger `
            $scheduledNotifications `
            $notificationTimeToleranceTicks)) {
        throw '復元後のWindows予約と通知ledgerが整合しません。'
    }

    $fixtureIds = @($fixtureEnabledGameId, $fixtureDisabledGameId)
    if (@($ledger.entries | Where-Object {
                [Guid]$_.gameId -in $fixtureIds
            }).Count -ne 0) {
        throw '復元後のledgerにfixture IDが残っています。'
    }

    foreach ($notification in @($scheduledNotifications |
            Where-Object { $_.Group -eq 'stamina' })) {
        $gameId = [Guid]::Empty
        if ([Guid]::TryParseExact(
                [string]$notification.Tag,
                'N',
                [ref]$gameId) -and
            $gameId -in $fixtureIds) {
            throw '復元後のWindows予約にfixture IDが残っています。'
        }
    }
}

function Invoke-NotificationStateContract {
    $scheduledGameId = [Guid]::NewGuid()
    $suppressedGameId = [Guid]::NewGuid()
    $notificationTicks = [DateTimeOffset]::UtcNow.AddMinutes(1).UtcTicks
    $ledger = [pscustomobject]@{
        entries = @(
            [pscustomobject]@{
                gameId = $scheduledGameId
                state = 'Scheduled'
                notificationAtUtcTicks = $notificationTicks
            },
            [pscustomobject]@{
                gameId = $suppressedGameId
                state = 'Suppressed'
                notificationAtUtcTicks = $notificationTicks
            })
    }
    $states = @{
        $scheduledGameId = 'Scheduled'
        $suppressedGameId = 'Suppressed'
    }
    $matchingSchedule = [pscustomobject]@{
        Tag = $scheduledGameId.ToString('N')
        Group = 'stamina'
        DeliveryTimeUtcTicks = $notificationTicks +
            [TimeSpan]::FromMilliseconds(500).Ticks
    }
    if (-not (Test-NotificationStateSnapshot `
            $ledger @($matchingSchedule) $states)) {
        throw '時刻許容差内の予約が一致と判定されません。'
    }

    $lateSchedule = $matchingSchedule.PSObject.Copy()
    $lateSchedule.DeliveryTimeUtcTicks = $notificationTicks +
        [TimeSpan]::FromSeconds(2).Ticks
    if (Test-NotificationStateSnapshot $ledger @($lateSchedule) $states) {
        throw '時刻許容差外の予約が一致と判定されました。'
    }

    $extraSchedule = [pscustomobject]@{
        Tag = [Guid]::NewGuid().ToString('N')
        Group = 'stamina'
        DeliveryTimeUtcTicks = $notificationTicks
    }
    if (Test-NotificationStateSnapshot `
            $ledger @($matchingSchedule, $extraSchedule) $states) {
        throw '余分なstamina予約が見逃されました。'
    }

    $suppressedSchedule = [pscustomobject]@{
        Tag = $suppressedGameId.ToString('N')
        Group = 'stamina'
        DeliveryTimeUtcTicks = $notificationTicks
    }
    if (Test-NotificationStateSnapshot `
            $ledger @($matchingSchedule, $suppressedSchedule) $states) {
        throw 'Suppressedエントリの予約が見逃されました。'
    }

    Write-Host 'Task11 notification contract: PASS'
}

function Set-GlobalNotifications {
    param([bool]$IsEnabled)

    Invoke-WinApp ui invoke NavSettings -a $activeProcessId | Out-Null
    Invoke-WinApp ui scroll-into-view NotificationsToggle `
        -a $activeProcessId | Out-Null
    $current = (Invoke-WinApp ui get-value NotificationsToggle `
        -a $activeProcessId --json | ConvertFrom-Json).text
    $expected = if ($IsEnabled) { 'On' } else { 'Off' }
    if ($current -ne $expected) {
        Invoke-WinApp ui invoke NotificationsToggle `
            -a $activeProcessId | Out-Null
    }
    Invoke-WinApp ui wait-for NotificationsToggle -a $activeProcessId `
        --value $expected -t 5000 | Out-Null
}

function Set-GameNotification {
    param(
        [Guid]$GameId,
        [bool]$IsEnabled)

    Invoke-WinApp ui invoke NavOverview -a $activeProcessId | Out-Null
    Invoke-WinApp ui invoke "GameCard_$($GameId.ToString('D'))" `
        -a $activeProcessId | Out-Null
    Invoke-WinApp ui wait-for GameNotificationToggle `
        -a $activeProcessId -t 5000 | Out-Null
    $current = (Invoke-WinApp ui get-value GameNotificationToggle `
        -a $activeProcessId --json | ConvertFrom-Json).text
    $expected = if ($IsEnabled) { 'On' } else { 'Off' }
    if ($current -ne $expected) {
        Invoke-WinApp ui invoke GameNotificationToggle `
            -a $activeProcessId | Out-Null
    }
    Invoke-WinApp ui wait-for GameNotificationToggle `
        -a $activeProcessId --value $expected -t 5000 | Out-Null
    Invoke-WinApp ui invoke GameEditorSaveButton `
        -a $activeProcessId | Out-Null
    Invoke-WinApp ui wait-for GameEditorDialog -a $activeProcessId `
        --gone -t 5000 | Out-Null
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

if ($ContractOnly) {
    Invoke-NotificationStateContract
    return
}

$inputProcess = Get-Process -Id $AppPid -ErrorAction Stop
if ($inputProcess.ProcessName -ne 'StaminaManager') {
    throw "PID $AppPid is not StaminaManager."
}
$processPath = [IO.Path]::GetFullPath($inputProcess.Path)
$appxPath = Split-Path -Parent $processPath
$resolvedOutput = Split-Path -Parent $appxPath
$package = Get-AppxPackage | Where-Object {
    [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\') -eq
        [IO.Path]::GetFullPath($appxPath).TrimEnd('\')
} | Select-Object -First 1
if ($null -eq $package) {
    throw 'Registered debug package was not found.'
}

$mutexToken = $package.PackageFamilyName -replace '[^A-Za-z0-9_.-]', '_'
$executionMutexName = "Local\StaminaManager.UiTests.$mutexToken"
$executionMutex = [Threading.Mutex]::new($false, $executionMutexName)
try {
    try {
        $isExecutionMutexAcquired = $executionMutex.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        $isExecutionMutexAcquired = $true
        throw 'A previous UI test ended without restoring its package data.'
    }
    if (-not $isExecutionMutexAcquired) {
        throw 'Another UI test is already using this package.'
    }

[xml]$manifest = [IO.File]::ReadAllText(
    (Join-Path $appxPath 'AppxManifest.xml'))
$applicationId = [string]$manifest.Package.Applications.Application.Id
$appUserModelId = "$($package.PackageFamilyName)!$applicationId"
$windowsSdkAssembly = Join-Path $appxPath 'Microsoft.Windows.SDK.NET.dll'
if (-not ('Task11ToastProbe' -as [type])) {
    [Reflection.Assembly]::LoadFrom(
        (Join-Path $appxPath 'WinRT.Runtime.dll')) | Out-Null
    [Reflection.Assembly]::LoadFrom($windowsSdkAssembly) | Out-Null
    Add-Type -ReferencedAssemblies $windowsSdkAssembly `
        -CompilerOptions '/nowarn:1701' -TypeDefinition @'
using System;
using Windows.UI.Notifications;

public sealed class Task11ScheduledToastInfo
{
    public Task11ScheduledToastInfo(
        string tag,
        string group,
        long deliveryTimeUtcTicks)
    {
        Tag = tag;
        Group = group;
        DeliveryTimeUtcTicks = deliveryTimeUtcTicks;
    }

    public string Tag { get; }

    public string Group { get; }

    public long DeliveryTimeUtcTicks { get; }
}

public static class Task11ToastProbe
{
    public static Task11ScheduledToastInfo[] GetScheduledNotifications(
        string appUserModelId)
    {
        var source = ToastNotificationManager.CreateToastNotifier(
            appUserModelId).GetScheduledToastNotifications();
        var results = new Task11ScheduledToastInfo[source.Count];
        var count = 0;
        foreach (var notification in source)
        {
            results[count++] = new Task11ScheduledToastInfo(
                notification.Tag ?? string.Empty,
                notification.Group ?? string.Empty,
                notification.DeliveryTime.UtcDateTime.Ticks);
        }
        Array.Resize(ref results, count);
        return results;
    }

    public static string[] GetHistoryGameIds(string appUserModelId)
    {
        var source = ToastNotificationManager.History.GetHistory(
            appUserModelId);
        var results = new string[source.Count];
        var count = 0;
        foreach (var notification in source)
        {
            if (notification.Group == "stamina" && Guid.TryParseExact(
                notification.Tag, "N", out var gameId))
            {
                results[count++] = gameId.ToString("D");
            }
        }
        Array.Resize(ref results, count);
        return results;
    }
}
'@
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
    Stop-StaminaManager $activeProcessId
    $activeProcessId = 0

    $recordedAtUtc = [DateTimeOffset]::UtcNow
    Write-FixtureData $dataPath $recordedAtUtc 2 30 1
    [IO.File]::WriteAllText(
        $ledgerPath,
        '{"schemaVersion":1,"entries":[]}',
        [Text.UTF8Encoding]::new($false))

    $activeProcessId = Start-PackagedApp
    $scheduledStates = @{
        $fixtureEnabledGameId = 'Scheduled'
        $fixtureDisabledGameId = 'Suppressed'
    }
    Wait-NotificationState $scheduledStates

    Set-GlobalNotifications $false
    $suppressedStates = @{
        $fixtureEnabledGameId = 'Suppressed'
        $fixtureDisabledGameId = 'Suppressed'
    }
    Wait-NotificationState $suppressedStates

    Set-GlobalNotifications $true
    Wait-NotificationState $scheduledStates

    Set-GameNotification $fixtureEnabledGameId $false
    Wait-NotificationState $suppressedStates

    Set-GameNotification $fixtureEnabledGameId $true
    Wait-NotificationState $scheduledStates
    $scheduled = Wait-LedgerState $ledgerPath $fixtureEnabledGameId `
        'Scheduled' 1
    $notificationAtUtc = [DateTimeOffset]::new(
        [long]$scheduled.notificationAtUtcTicks,
        [TimeSpan]::Zero)
    if ($notificationAtUtc -le [DateTimeOffset]::UtcNow) {
        throw 'Before-due re-enable did not schedule in the future.'
    }

    Set-GameNotification $fixtureEnabledGameId $false
    Wait-NotificationState $suppressedStates
    while ([DateTimeOffset]::UtcNow -le $notificationAtUtc.AddSeconds(1)) {
        Start-Sleep -Milliseconds 200
    }

    if ($fixtureEnabledGameId -in (Get-HistoryGameIds)) {
        throw 'Fixture notification appeared before after-due re-enable.'
    }
    Set-GameNotification $fixtureEnabledGameId $true
    $consumedStates = @{
        $fixtureEnabledGameId = 'Consumed'
        $fixtureDisabledGameId = 'Suppressed'
    }
    Wait-NotificationState $consumedStates
    if ($fixtureEnabledGameId -in (Get-HistoryGameIds)) {
        throw 'After-due re-enable showed an immediate notification.'
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

        [IO.File]::WriteAllBytes($dataPath, $dataOriginal)
        Restore-OptionalFile $ledgerPath $ledgerDidExist $ledgerOriginal

        $cleanupProcessId = Start-PackagedApp
        Stop-StaminaManager $cleanupProcessId
        [IO.File]::WriteAllBytes($dataPath, $dataOriginal)
        if ([Convert]::ToBase64String(
                [IO.File]::ReadAllBytes($dataPath)) -ne
            [Convert]::ToBase64String($dataOriginal)) {
            throw '復元後のdata.jsonが元のbytesと一致しません。'
        }
        Assert-RestoredNotificationConsistency
        Write-Host 'Task11 notification restoration: PASS'
    }
    catch {
        $restoreError = $_
    }
}

$result = [pscustomobject]@{
    testPassed = $null -eq $testError
    restorePassed = $null -eq $restoreError
    fixtureEnabledGameId = $fixtureEnabledGameId
    fixtureDisabledGameId = $fixtureDisabledGameId
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
}
finally {
    if ($isExecutionMutexAcquired -and $null -ne $executionMutex) {
        $executionMutex.ReleaseMutex()
        $isExecutionMutexAcquired = $false
    }
    if ($null -ne $executionMutex) {
        $executionMutex.Dispose()
    }
}
