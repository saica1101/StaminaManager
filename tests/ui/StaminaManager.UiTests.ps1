param(
    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$AppPid,
    [string]$OutputDirectory = "$PSScriptRoot\results"
)

$ErrorActionPreference = 'Stop'
$runSuffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runId = "{0}-{1}" -f (
    [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss-fff')),
    $runSuffix
$screenshotDirectory = Join-Path $OutputDirectory "screenshots\$runId"
$resultPath = Join-Path $OutputDirectory "ui-results-$runId.json"
$results = [Collections.Generic.List[object]]::new()
$auditElements = [Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::Now
$expectedProcessPath = $null
$packageFamilyName = $null
$dataDirectory = $null
$settingsDirectory = $null
$appOutputDirectory = $null
$temporaryRoot = $null
$dataBackupDirectory = $null
$settingsBackupDirectory = $null
$generatedResidueDirectory = $null
$originalDataFingerprint = $null
$originalSettingsFingerprint = $null
$executionMutex = $null
$executionMutexName = $null
$isExecutionMutexAcquired = $false
$isIdentityVerified = $false
$isBackupVerified = $false
$restoreError = $null
$testGameId = $null
$notificationTestGameId = $null
$testGameName = "Codex UI $runId"
$editedGameName = "$testGameName edited"
$secondGameName = "$testGameName second"
$thirdGameName = "$testGameName third"
$testGameIds = [Collections.Generic.List[Guid]]::new()
$originalWindowBounds = $null
$originalDisplayMode = $null
$threeColumnMinimumWidth = 720

if (-not ('StaminaManagerUiTestNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class StaminaManagerUiTestNative
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(
        IntPtr hWnd,
        int x,
        int y,
        int width,
        int height,
        bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int command);
}
'@
}
Add-Type -AssemblyName UIAutomationClient

$requiredAutomationIds = @(
    'ShellContentFrame',
    'AboutPageRoot',
    'AboutVersionText',
    'OpenGitHubButton',
    'OpenReadmeButton',
    'AboutReadmeHeading',
    'AboutReadmeDescription',
    'NavOverview',
    'NavSettings',
    'OverviewScrollViewer',
    'SettingsScrollViewer',
    'OverviewItems',
    'AddGameCard',
    'CompactModeButton',
    'GameEditorSaveButton',
    'GameEditorCancelButton',
    'GameNameInput',
    'CurrentStaminaInput',
    'MaxStaminaInput',
    'RecoveryMinutesInput',
    'RecoverySecondsInput',
    'RecoverySecondsErrorText',
    'RecoveryIntervalErrorText',
    'GameNotificationToggle',
    'ChooseGameImageButton',
    'GameEditorDeleteButton',
    'DeleteConfirmButton',
    'DeleteBackButton',
    'CompactGameSelector',
    'ReturnOverviewButton',
    'CompactUpdateButton',
    'CompactEditButton',
    'ThemeToggle',
    'BackdropSelector',
    'AcrylicOpacitySlider',
    'AcrylicOpacityValue',
    'CloseBehaviorSelector',
    'StartupToggle',
    'NotificationsToggle',
    'NotificationLeadInput',
    'ExportBackupButton',
    'ImportBackupButton'
)
$conditionalAutomationIds = [ordered]@{
    SettingsInfoBar = '設定操作の結果メッセージが開いた場合のみ表示'
    SettingsLoadingStatus = '設定読込中のみ表示'
    SettingsFailedStatus = '設定読込失敗時のみ表示'
    OpenWindowsNotificationSettingsButton =
        'Windows通知が無効または利用不可の場合のみ表示'
    OverviewRecoveryInfoBar = 'data.recovery.jsonから回復した場合のみ表示'
    OverviewStartupErrorInfoBar = '起動処理の失敗時のみ表示'
    OverviewInfoBar = 'Overview操作の失敗時のみ表示'
    CompactErrorInfoBar = 'Compact操作の失敗時のみ表示'
    CompactAddGameButton = 'ゲーム0件のCompact表示でのみ表示'
    GameEditorElapsedInfoBar = '編集開始後に自然回復した場合のみ表示'
    GameEditorErrorInfoBar = 'ゲーム保存の失敗時のみ表示'
    GameEditorDeleteErrorInfoBar = 'ゲーム削除の失敗時のみ表示'
    RestoreBackupConfirmButton = '有効なbackup選択後の確認時のみ表示'
    RestoreBackupCancelButton = '有効なbackup選択後の確認時のみ表示'
}

function Invoke-WinApp {
    $output = & winapp @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }

    return $output
}

function Add-Result {
    param(
        [string]$Category,
        [string]$Name,
        [ValidateSet('PASS', 'FAIL', 'SKIP')]
        [string]$Status,
        [string]$Detail = '')

    $script:results.Add([ordered]@{
        category = $Category
        name = $Name
        status = $Status
        detail = $Detail
    })
}

function Invoke-UiTest {
    param(
        [string]$Category,
        [string]$Name,
        [scriptblock]$Action)

    try {
        & $Action
        Add-Result $Category $Name PASS
    }
    catch {
        Add-Result $Category $Name FAIL $_.Exception.Message
    }
}

function Get-DirectoryFingerprint {
    param([string]$Path)

    $root = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $entries = Get-ChildItem -LiteralPath $root -Force -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($root.Length).TrimStart('\')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "F|$relativePath|$($_.Length)|$hash"
        }
    return [string]::Join("`n", $entries)
}

function Get-DirectoryContentSnapshot {
    param([string]$Path)

    $root = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $files = @(Get-ChildItem -LiteralPath $root -Force -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            [pscustomobject]@{
                RelativePath = $_.FullName.Substring(
                    $root.Length).TrimStart('\')
                Bytes = [IO.File]::ReadAllBytes($_.FullName)
            }
        })
    return [pscustomobject]@{ Files = $files }
}

function Move-GeneratedFiles {
    param(
        [string]$Path,
        [Collections.Generic.HashSet[string]]$ExpectedRelativePaths,
        [string]$ResidueCategory)

    $root = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    foreach ($file in @(Get-ChildItem -LiteralPath $root `
            -Force -File -Recurse)) {
        $relativePath = $file.FullName.Substring($root.Length).TrimStart('\')
        if ($ExpectedRelativePaths.Contains($relativePath)) {
            continue
        }

        $residueRoot = Join-Path $generatedResidueDirectory $ResidueCategory
        $destination = Join-Path $residueRoot $relativePath
        New-Item -ItemType Directory -Force `
            -Path (Split-Path -Parent $destination) | Out-Null
        Move-Item -LiteralPath $file.FullName `
            -Destination $destination -Force
    }
}

function Wait-DirectoryFilesWritable {
    param(
        [string]$Path,
        [int]$TimeoutSeconds = 30)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $lockedFile = $null
        foreach ($file in @(Get-ChildItem -LiteralPath $Path `
                -Force -File -Recurse)) {
            $stream = $null
            try {
                $stream = [IO.File]::Open(
                    $file.FullName,
                    [IO.FileMode]::Open,
                    [IO.FileAccess]::ReadWrite,
                    [IO.FileShare]::None)
            }
            catch {
                $lockedFile = $file.FullName
                break
            }
            finally {
                if ($null -ne $stream) {
                    $stream.Dispose()
                }
            }
        }

        if ($null -eq $lockedFile) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "ApplicationData file remained locked: $lockedFile"
}

function Copy-DirectoryFromBackup {
    param(
        [string]$BackupPath,
        [string]$DestinationPath,
        [string]$ResidueCategory)

    $backupRoot = [IO.Path]::GetFullPath($BackupPath).TrimEnd('\')
    $destinationRoot = [IO.Path]::GetFullPath(
        $DestinationPath).TrimEnd('\')
    $backupFiles = @(Get-ChildItem -LiteralPath $backupRoot `
        -Force -File -Recurse)
    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $backupFiles) {
        [void]$expected.Add(
            $file.FullName.Substring($backupRoot.Length).TrimStart('\'))
    }

    Move-GeneratedFiles $destinationRoot $expected $ResidueCategory
    foreach ($file in $backupFiles) {
        $relativePath = $file.FullName.Substring(
            $backupRoot.Length).TrimStart('\')
        $destination = Join-Path $destinationRoot $relativePath
        New-Item -ItemType Directory -Force `
            -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName `
            -Destination $destination -Force
    }
}

function Restore-DirectoryFromBackup {
    param(
        [string]$BackupPath,
        [string]$DestinationPath,
        [string]$ResidueCategory)

    $backupRoot = [IO.Path]::GetFullPath($BackupPath).TrimEnd('\')
    $destinationRoot = [IO.Path]::GetFullPath(
        $DestinationPath).TrimEnd('\')
    $backupFiles = @(Get-ChildItem -LiteralPath $backupRoot `
        -Force -File -Recurse)
    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $backupFiles) {
        [void]$expected.Add(
            $file.FullName.Substring($backupRoot.Length).TrimStart('\'))
    }

    Move-GeneratedFiles $destinationRoot $expected $ResidueCategory
    foreach ($file in $backupFiles) {
        $relativePath = $file.FullName.Substring(
            $backupRoot.Length).TrimStart('\')
        $destination = Join-Path $destinationRoot $relativePath
        New-Item -ItemType Directory -Force `
            -Path (Split-Path -Parent $destination) | Out-Null
        Move-Item -LiteralPath $file.FullName `
            -Destination $destination -Force
    }
}

function Restore-DirectoryContentSnapshot {
    param(
        [string]$Path,
        [object]$Snapshot,
        [string]$ResidueCategory)

    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($Snapshot.Files)) {
        [void]$expected.Add([string]$file.RelativePath)
    }

    Move-GeneratedFiles $Path $expected $ResidueCategory
    foreach ($file in @($Snapshot.Files)) {
        $destination = Join-Path $Path $file.RelativePath
        New-Item -ItemType Directory -Force `
            -Path (Split-Path -Parent $destination) | Out-Null
        [IO.File]::WriteAllBytes($destination, $file.Bytes)
    }
}

function Get-VerifiedAppIdentity {
    param([int]$ProcessId)

    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    if ($process.ProcessName -ne 'StaminaManager') {
        throw "PID $ProcessId is not StaminaManager."
    }

    $processPath = [IO.Path]::GetFullPath($process.Path)
    if ([IO.Path]::GetFileName($processPath) -ne 'StaminaManager.exe') {
        throw "Unexpected executable path: $processPath"
    }

    $package = Get-AppxPackage | Where-Object {
        if ([string]::IsNullOrWhiteSpace($_.InstallLocation)) {
            return $false
        }

        $installPath = [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\')
        $processPath.StartsWith(
            "$installPath\",
            [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if ($null -eq $package) {
        throw 'The running executable does not belong to a registered package.'
    }

    $appxDirectory = Split-Path -Parent $processPath
    if ((Split-Path -Leaf $appxDirectory) -ne 'AppX') {
        throw "The executable parent is not AppX: $appxDirectory"
    }

    return [pscustomobject]@{
        ProcessPath = $processPath
        PackageFamilyName = $package.PackageFamilyName
        AppOutputDirectory = Split-Path -Parent $appxDirectory
    }
}

function Stop-VerifiedAppProcess {
    param(
        [int]$ProcessId,
        [string]$ExpectedPath)

    if ($ProcessId -le 0) {
        return
    }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    $actualPath = [IO.Path]::GetFullPath($process.Path)
    if ($process.ProcessName -ne 'StaminaManager' -or
        -not $actualPath.Equals(
            $ExpectedPath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to stop reused or unexpected PID $ProcessId."
    }

    Stop-Process -Id $ProcessId -Force
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($null -eq (Get-Process -Id $ProcessId `
                -ErrorAction SilentlyContinue)) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Process $ProcessId did not exit within 15 seconds."
}

function Test-UiElement {
    param(
        [int]$ProcessId,
        [string]$AutomationId,
        [int]$Timeout = 500)

    & winapp ui wait-for $AutomationId -a $ProcessId `
        -t $Timeout 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Wait-UiElementNameEmpty {
    param(
        [int]$ProcessId,
        [string]$AutomationId,
        [int]$Timeout = 3000)

    $deadline = [DateTime]::UtcNow.AddMilliseconds($Timeout)
    do {
        $output = & winapp ui get-property $AutomationId -a $ProcessId `
            -p Name --json 2>$null
        if ($LASTEXITCODE -ne 0) {
            return
        }
        $name = (($output -join [Environment]::NewLine) |
            ConvertFrom-Json).properties.Name
        if ([string]::IsNullOrWhiteSpace([string]$name)) {
            return
        }

        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Element retained an error name: $AutomationId"
}

function Wait-WindowsNotificationSettings {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $hosts = @(Get-Process ApplicationFrameHost -ErrorAction SilentlyContinue)
        foreach ($hostProcess in $hosts) {
            if (Test-UiElement $hostProcess.Id `
                    SystemSettings_Notifications_ShowAppNotifications_ToggleSwitch `
                    500) {
                return $hostProcess.Id
            }
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'Windows notification settings did not open within 10 seconds.'
}

function Wait-AppReady {
    param([int]$ProcessId)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if (Test-UiElement $ProcessId NavOverview 500) {
            return
        }

        if (Test-UiElement $ProcessId ReturnOverviewButton 500) {
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Process $ProcessId did not expose a ready page."
}

function Start-PackagedApp {
    $launch = Invoke-WinApp run $appOutputDirectory `
        --detach --json | ConvertFrom-Json
    $script:AppPid = [int]$launch.ProcessId
    $identity = Get-VerifiedAppIdentity $AppPid
    if (-not $identity.ProcessPath.Equals(
            $expectedProcessPath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Relaunched process path changed: $($identity.ProcessPath)"
    }

    Wait-AppReady $AppPid
    return $AppPid
}

function Write-EmptyFixture {
    $dataPath = Join-Path $dataDirectory 'data.json'
    if (-not (Test-Path -LiteralPath $dataPath -PathType Leaf)) {
        throw "Data file was not found: $dataPath"
    }

    $original = [IO.File]::ReadAllText($dataPath) | ConvertFrom-Json
    $script:originalDisplayMode = [string]$original.settings.lastDisplayMode
    $fixtureSettings = $original.settings | Select-Object *
    $fixtureSettings | Add-Member -MemberType NoteProperty `
        -Name selectedCompactGameId -Value $null -Force
    $fixture = [ordered]@{
        schemaVersion = [int]$original.schemaVersion
        games = @()
        settings = $fixtureSettings
    }
    [IO.File]::WriteAllText(
        $dataPath,
        ($fixture | ConvertTo-Json -Depth 8 -Compress),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $dataDirectory 'notification-state.json'),
        '{"schemaVersion":1,"entries":[]}',
        [Text.UTF8Encoding]::new($false))
}

function Get-EmptyFixtureState {
    $dataPath = Join-Path $dataDirectory 'data.json'
    $data = [IO.File]::ReadAllText($dataPath) | ConvertFrom-Json
    return [pscustomobject]@{
        Fingerprint = (Get-FileHash -LiteralPath $dataPath `
            -Algorithm SHA256).Hash
        GameCount = @($data.games).Count
        SelectedCompactGameId = $data.settings.selectedCompactGameId
    }
}

function Wait-EmptyFixtureApplied {
    param(
        [string]$ExpectedFingerprint,
        [string]$Stage,
        [int]$Timeout = 5000)

    $deadline = [DateTime]::UtcNow.AddMilliseconds($Timeout)
    $stableMatches = 0
    $lastState = $null
    do {
        try {
            $lastState = Get-EmptyFixtureState
            if ($lastState.Fingerprint -eq $ExpectedFingerprint -and
                $lastState.GameCount -eq 0 -and
                $null -eq $lastState.SelectedCompactGameId) {
                $stableMatches++
                if ($stableMatches -ge 3) {
                    return $lastState
                }
            }
            else {
                $stableMatches = 0
            }
        }
        catch {
            $stableMatches = 0
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    $detail = if ($null -eq $lastState) {
        'data.jsonを読み取れませんでした。'
    }
    else {
        "Hash=$($lastState.Fingerprint); Games=$($lastState.GameCount); " +
            "SelectedCompactGameId=$($lastState.SelectedCompactGameId)"
    }
    throw "$Stage の空fixtureが安定しませんでした。$detail"
}

function Format-EmptyFixtureState {
    param([object]$State)

    $selected = if ($null -eq $State.SelectedCompactGameId) {
        'null'
    }
    else {
        [string]$State.SelectedCompactGameId
    }
    return "Hash=$($State.Fingerprint); Games=$($State.GameCount); " +
        "SelectedCompactGameId=$selected"
}

function Add-TestGame {
    param([string]$Name)

    Invoke-WinApp ui invoke AddGameCard -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for GameNameInput -a $AppPid -t 5000 |
        Out-Null
    Invoke-WinApp ui set-value GameNameInput $Name -a $AppPid |
        Out-Null
    Invoke-WinApp ui set-value CurrentStaminaInput 98 `
        -a $AppPid | Out-Null
    Invoke-WinApp ui set-value MaxStaminaInput 100 `
        -a $AppPid | Out-Null
    Invoke-WinApp ui set-value RecoveryMinutesInput 525600 `
        -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for GameEditorSaveButton -a $AppPid `
        -p IsEnabled --value True -t 3000 | Out-Null
    Invoke-WinApp ui invoke GameEditorSaveButton -a $AppPid |
        Out-Null
    $game = Get-DataGame $Name
    $gameId = [Guid]$game.id
    Invoke-WinApp ui wait-for "GameCard_$($gameId.ToString('D'))" `
        -a $AppPid -t 5000 | Out-Null
    return $gameId
}

function Get-ControlValue {
    param([string]$AutomationId)

    return (
        Invoke-WinApp ui get-value $AutomationId -a $AppPid --json |
            ConvertFrom-Json
    ).text
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

function Wait-BackdropDiagnostic {
    param(
        [string]$ExpectedDiagnostic,
        [int]$TimeoutMilliseconds = 3000)

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    $actual = $null
    do {
        try {
            $actual = Get-RawBackdropDiagnostic
            if ($actual -eq $ExpectedDiagnostic) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Expected '$ExpectedDiagnostic', actual '$actual'."
}

function Wait-ControlEnabled {
    param(
        [string]$AutomationId,
        [bool]$Expected,
        [int]$TimeoutMilliseconds = 3000)

    $expectedValue = if ($Expected) { 'True' } else { 'False' }
    Invoke-WinApp ui wait-for $AutomationId -a $AppPid `
        -p IsEnabled --value $expectedValue `
        -t $TimeoutMilliseconds | Out-Null
}

function Wait-PersistedAcrylicOpacity {
    param(
        [int]$ExpectedPercent,
        [int]$TimeoutMilliseconds = 3000)

    $dataPath = Join-Path $dataDirectory 'data.json'
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    $actual = $null
    do {
        try {
            $data = [IO.File]::ReadAllText($dataPath) | ConvertFrom-Json
            $actual = [int]$data.settings.acrylicTintOpacityPercent
            if ($actual -eq $ExpectedPercent) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Expected persisted Acrylic opacity $ExpectedPercent, actual $actual."
}

function Set-NumberBoxFromKeyboard {
    param(
        [string]$AutomationId,
        [string]$Value)

    Invoke-WinApp ui send-keys ctrl+a --target $AutomationId `
        -a $AppPid --via send-input | Out-Null
    Invoke-WinApp ui send-keys --verbatim $Value `
        --target $AutomationId -a $AppPid --via send-input | Out-Null
    Invoke-WinApp ui send-keys tab --target $AutomationId `
        -a $AppPid --via send-input | Out-Null
}

function Wait-NumberBoxValue {
    param(
        [string]$AutomationId,
        [string]$ExpectedValue,
        [int]$Timeout = 3000)

    $deadline = [DateTime]::UtcNow.AddMilliseconds($Timeout)
    do {
        try {
            $inspection = Invoke-WinApp ui inspect $AutomationId `
                -a $AppPid --json | ConvertFrom-Json
            $numberBox = @($inspection.windows.elements) |
                Where-Object { $_.automationId -eq $AutomationId } |
                Select-Object -First 1
            $inputBox = @($numberBox.children) |
                Where-Object { $_.automationId -eq 'InputBox' } |
                Select-Object -First 1
            if ([string]$inputBox.value -eq $ExpectedValue) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "NumberBox $AutomationId did not reach $ExpectedValue."
}

function Select-ComboItem {
    param(
        [string]$AutomationId,
        [string]$ItemName)

    if ((Get-ControlValue $AutomationId) -eq $ItemName) {
        return
    }

    Invoke-WinApp ui invoke $AutomationId -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 200
    $matches = (
        Invoke-WinApp ui search $ItemName -a $AppPid --json |
            ConvertFrom-Json
    ).matches
    $item = $matches |
        Where-Object { $_.type -eq 'ListItem' -or $_.isInvokable } |
        Select-Object -First 1
    if ($null -eq $item) {
        throw "ComboBox item was not found: $ItemName"
    }

    Invoke-WinApp ui invoke $item.selector -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for $AutomationId -a $AppPid `
        --value $ItemName -t 5000 | Out-Null
}

function Scroll-ToSettingsControl {
    param([string]$AutomationId)

    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        & winapp ui wait-for $AutomationId -a $AppPid `
            -p IsOffscreen --value False -t 100 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Invoke-WinApp ui scroll SettingsScrollViewer -a $AppPid `
            --direction down | Out-Null
        Start-Sleep -Milliseconds 150
    }

    throw "Settings control could not be scrolled into view: $AutomationId"
}

function Save-Screenshot {
    param([string]$Name)

    Start-Sleep -Milliseconds 500
    $path = Join-Path $screenshotDirectory "$Name.png"
    if ($Name -in @(
            '05-theme-dark',
            '05-theme-light',
            '06-theme-off',
            '06-theme-on',
            '07-backdrop-acrylic',
            '07-backdrop-transparent')) {
        Invoke-WinApp ui screenshot -a $AppPid -o $path `
            --capture-screen | Out-Null
        return
    }

    Invoke-WinApp ui screenshot -a $AppPid -o $path | Out-Null
}

function Get-DataGame {
    param([string]$Name)

    $dataPath = Join-Path $dataDirectory 'data.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        try {
            $data = [IO.File]::ReadAllText($dataPath) | ConvertFrom-Json
            $game = @($data.games) |
                Where-Object { $_.name -eq $Name } |
                Select-Object -First 1
            if ($null -ne $game) {
                return $game
            }
        }
        catch {
            # 原子的な保存中は次のポーリングで再読込する。
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Saved game was not found in data.json: $Name"
}

function Get-DataFileFingerprint {
    return (Get-FileHash -LiteralPath (
        Join-Path $dataDirectory 'data.json') -Algorithm SHA256).Hash
}

function Assert-EditorSaveIsBlocked {
    param([string]$ExpectedDataFingerprint)

    Invoke-WinApp ui wait-for GameEditorSaveButton -a $AppPid `
        -p IsEnabled --value False -t 3000 | Out-Null
    & winapp ui click GameEditorSaveButton -a $AppPid 2>$null |
        Out-Null
    Start-Sleep -Milliseconds 300
    Invoke-WinApp ui wait-for GameEditorDialog -a $AppPid -t 3000 |
        Out-Null
    if ((Get-DataFileFingerprint) -ne $ExpectedDataFingerprint) {
        throw 'Disabled editor save changed data.json.'
    }
}

function Collect-AuditSnapshot {
    param([string]$State)

    $rootAutomationId = if ($State -like 'Compact*') {
        'CompactContentHost'
    }
    else {
        'ShellContentFrame'
    }

    function Add-AuditElement {
        param([object]$Element)

        if ($null -eq $Element) {
            return
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$Element.type)) {
            $script:auditElements.Add([pscustomobject]@{
                state = $State
                type = [string]$Element.type
                automationId = [string]$Element.automationId
                name = [string]$Element.name
                className = [string]$Element.className
            })
        }

        foreach ($child in @($Element.children)) {
            if ($null -ne $child) {
                Add-AuditElement $child
            }
        }
        foreach ($child in @($Element.elements)) {
            if ($null -ne $child) {
                Add-AuditElement $child
            }
        }
    }

    if ($State -notlike 'Editor*' -and $State -ne 'DeleteConfirmation') {
        $document = Invoke-WinApp ui inspect $rootAutomationId -a $AppPid `
            --interactive --depth 8 --hide-offscreen --json |
            ConvertFrom-Json
        foreach ($window in @($document.windows)) {
            Add-AuditElement $window
        }
        foreach ($element in @($document.elements)) {
            Add-AuditElement $element
        }
    }

    $stateAutomationIds = switch -Wildcard ($State) {
        'Editor*' {
            @(
                'GameEditorDialog',
                'GameEditorSaveButton',
                'GameEditorCancelButton',
                'GameNameInput',
                'CurrentStaminaInput',
                'MaxStaminaInput',
                'RecoveryMinutesInput',
                'RecoverySecondsInput',
                'RecoverySecondsErrorText',
                'RecoveryIntervalErrorText',
                'GameNotificationToggle',
                'ChooseGameImageButton',
                'GameEditorDeleteButton')
        }
        'DeleteConfirmation' {
            @(
                'GameEditorDialog',
                'GameEditorSaveButton',
                'GameEditorCancelButton',
                'GameNameInput',
                'CurrentStaminaInput',
                'MaxStaminaInput',
                'RecoveryMinutesInput',
                'RecoverySecondsInput',
                'RecoverySecondsErrorText',
                'RecoveryIntervalErrorText',
                'GameNotificationToggle',
                'ChooseGameImageButton',
                'GameEditorDeleteButton',
                'DeleteConfirmButton',
                'DeleteBackButton')
        }
        'Compact*' {
            @(
                'CompactContentHost',
                'CompactGameSelector',
                'ReturnOverviewButton',
                'CompactUpdateButton',
                'CompactEditButton',
                'CompactAddGameButton')
        }
        'Settings*' {
            @(
                'ShellContentFrame',
                'NavOverview',
                'NavSettings',
                'SettingsScrollViewer',
                'ThemeToggle',
                'BackdropSelector',
                'AcrylicOpacitySlider',
                'AcrylicOpacityValue',
                'CloseBehaviorSelector',
                'StartupToggle',
                'NotificationsToggle',
                'NotificationLeadInput',
                'ExportBackupButton',
                'ImportBackupButton') + @($conditionalAutomationIds.Keys)
        }
        'About*' {
            @(
                'ShellContentFrame',
                'AboutScrollViewer',
                'AboutPageRoot',
                'AboutVersionText',
                'OpenGitHubButton',
                'OpenReadmeButton',
                'AboutReadmeHeading',
                'AboutReadmeDescription')
        }
        default {
            @(
                'ShellContentFrame',
                'NavOverview',
                'NavSettings',
                'OverviewScrollViewer',
                'OverviewItems',
                'AddGameCard',
                'CompactModeButton') + @($testGameIds | ForEach-Object {
                    "GameCard_$($_.ToString('D'))"
                })
        }
    }
    foreach ($automationId in @($stateAutomationIds | Sort-Object -Unique)) {
        if (-not (Test-UiElement $AppPid $automationId 100)) {
            continue
        }

        Add-AuditElement (Get-ElementMatch $automationId)
    }
}

function Get-MainWindowInfo {
    $windows = Invoke-WinApp ui list-windows -a $AppPid --json |
        ConvertFrom-Json
    $window = $windows |
        Where-Object { $_.title -ne 'PopupHost' -and $_.hwnd -ne 0 } |
        Select-Object -First 1
    if ($null -eq $window) {
        throw 'The application main window was not found.'
    }

    return $window
}

function Get-MainWindowHandle {
    return (Get-MainWindowInfo).hwnd
}

function Get-ElementMatch {
    param([string]$AutomationId)

    $matches = (
        Invoke-WinApp ui search $AutomationId -a $AppPid --json |
            ConvertFrom-Json
    ).matches
    $match = $matches | Where-Object {
        $_.automationId -eq $AutomationId
    } | Select-Object -First 1
    if ($null -eq $match) {
        throw "Element was not found: $AutomationId"
    }

    return $match
}

function Set-OverviewContentWidth {
    param([int]$EffectiveWidth)

    $window = Get-MainWindowInfo
    $handle = [IntPtr]$window.hwnd
    $dpi = [StaminaManagerUiTestNative]::GetDpiForWindow($handle)
    if ($dpi -eq 0) {
        throw 'GetDpiForWindow returned zero.'
    }

    [StaminaManagerUiTestNative]::ShowWindow($handle, 9) | Out-Null
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $contentFrame = Get-ElementMatch OverviewScrollViewer
        $actualEffectiveWidth = [int][Math]::Round(
            [double]$contentFrame.width * 96d / $dpi)
        $effectiveDelta = $EffectiveWidth - $actualEffectiveWidth
        if ($effectiveDelta -eq 0) {
            return $contentFrame
        }

        $window = Get-MainWindowInfo
        $physicalDelta = [int][Math]::Round(
            [double]$effectiveDelta * $dpi / 96d)
        $moved = [StaminaManagerUiTestNative]::MoveWindow(
            $handle,
            [int]$window.x,
            [int]$window.y,
            [int]$window.width + $physicalDelta,
            [int]$window.height,
            $true)
        if (-not $moved) {
            throw "MoveWindow failed for content width $EffectiveWidth."
        }

        Start-Sleep -Milliseconds 300
    }

    $actual = Get-ElementMatch OverviewScrollViewer
    $actualEffectiveWidth = [int][Math]::Round(
        [double]$actual.width * 96d / $dpi)
    throw "Content width target=$EffectiveWidth, " +
        "actual=$actualEffectiveWidth effective pixels (DPI=$dpi)."
}

function Set-WindowToStandardMinimumWidth {
    $window = Get-MainWindowInfo
    $handle = [IntPtr]$window.hwnd
    $dpi = [StaminaManagerUiTestNative]::GetDpiForWindow($handle)
    if ($dpi -eq 0) {
        throw 'GetDpiForWindow returned zero.'
    }

    [StaminaManagerUiTestNative]::ShowWindow($handle, 9) | Out-Null
    $moved = [StaminaManagerUiTestNative]::MoveWindow(
        $handle,
        [int]$window.x,
        [int]$window.y,
        1,
        [int]$window.height,
        $true)
    if (-not $moved) {
        throw 'MoveWindow failed for the Standard minimum width.'
    }

    Start-Sleep -Milliseconds 500
    $content = Get-ElementMatch OverviewScrollViewer
    $effectiveWidth = [int][Math]::Round(
        [double]$content.width * 96d / $dpi)
    if ($effectiveWidth -ge $threeColumnMinimumWidth) {
        throw "Minimum content width stayed too wide: $effectiveWidth."
    }

    return [pscustomobject]@{
        Element = $content
        EffectiveWidth = $effectiveWidth
    }
}

function Assert-CardColumns {
    param(
        [int]$Columns,
        [Guid[]]$GameIds)

    $cards = $GameIds | ForEach-Object {
        Get-ElementMatch "GameCard_$($_.ToString('D'))"
    } | Sort-Object y, x
    if (@($cards).Count -ne 3) {
        throw "Expected three cards, actual $(@($cards).Count)."
    }

    $firstRow = @($cards | Where-Object {
        [Math]::Abs([int]$_.y - [int]$cards[0].y) -le 2
    })
    if ($firstRow.Count -ne $Columns) {
        throw "Expected $Columns cards in first row, actual $($firstRow.Count)."
    }

    if ($Columns -eq 2 -and [int]$cards[2].y -le [int]$cards[0].y) {
        throw 'The third card did not move to the second row.'
    }
}

function Assert-WindowBoundsEqual {
    param(
        [object]$Expected,
        [object]$Actual,
        [string]$State)

    $nonClientTolerance = 32
    foreach ($property in @('x', 'y', 'width', 'height')) {
        $difference = [Math]::Abs(
            [int]$Expected.$property - [int]$Actual.$property)
        if ($difference -gt $nonClientTolerance) {
            throw "$State bounds mismatch at ${property}: " +
                "$($Expected.$property) != $($Actual.$property)"
        }
    }
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

function Wait-MainWindowVisibility {
    param([bool]$Visible)

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $process = Get-Process -Id $AppPid -ErrorAction Stop
        $process.Refresh()
        if (($process.MainWindowHandle -ne 0) -eq $Visible) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Main window visibility did not become $Visible."
}

function Close-ToTrayAndRestore {
    $process = Get-Process -Id $AppPid -ErrorAction Stop
    $posted = [StaminaManagerUiTestNative]::PostMessage(
        $process.MainWindowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    if (-not $posted) {
        throw "WM_CLOSE could not be posted to PID $AppPid."
    }

    Wait-MainWindowVisibility $false
    $redirect = Invoke-WinApp run $appOutputDirectory `
        --detach --json | ConvertFrom-Json
    $redirectPid = [int]$redirect.ProcessId
    if ($redirectPid -eq $AppPid) {
        throw 'Redirect launch unexpectedly returned the primary PID.'
    }

    Wait-ProcessExit $redirectPid
    Wait-MainWindowVisibility $true
    Invoke-WinApp ui wait-for NavSettings -a $AppPid -t 5000 |
        Out-Null
}

function Wait-LedgerEntry {
    param(
        [Guid]$GameId,
        [string]$State,
        [int]$LeadMinutes)

    $ledgerPath = Join-Path $dataDirectory 'notification-state.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if (Test-Path -LiteralPath $ledgerPath -PathType Leaf) {
            try {
                $ledger = [IO.File]::ReadAllText($ledgerPath) |
                    ConvertFrom-Json
                $entry = @($ledger.entries) | Where-Object {
                    [Guid]$_.gameId -eq $GameId -and
                    $_.state -eq $State -and
                    [int]$_.leadMinutes -eq $LeadMinutes
                } | Select-Object -First 1
                if ($null -ne $entry) {
                    return $entry
                }
            }
            catch {
                # 原子的な置換中は次のポーリングで再読込する。
            }
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Ledger did not reach $State/$LeadMinutes for $GameId."
}

function Assert-XamlAutomationSources {
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $sourceRoot = Join-Path $repositoryRoot 'StaminaManager'
    $declaredIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($file in @(Get-ChildItem -LiteralPath $sourceRoot `
            -Filter '*.xaml' -File -Recurse | Where-Object {
                $_.FullName -notmatch '\\(?:bin|obj)\\'
            })) {
        [xml]$document = [IO.File]::ReadAllText($file.FullName)
        foreach ($node in @($document.SelectNodes('//*'))) {
            foreach ($attribute in @($node.Attributes)) {
                if ($attribute.Name -eq
                    'AutomationProperties.AutomationId') {
                    [void]$declaredIds.Add([string]$attribute.Value)
                }
            }
            if ($node.LocalName -eq 'Setter' -and
                $node.GetAttribute('Property') -eq
                    'AutomationProperties.AutomationId') {
                [void]$declaredIds.Add([string]$node.GetAttribute('Value'))
            }
        }
    }

    $sourceIds = @($requiredAutomationIds) +
        @($conditionalAutomationIds.Keys)
    $missing = @($sourceIds | Where-Object {
        -not $declaredIds.Contains($_)
    })
    if ($missing.Count -gt 0) {
        throw "AutomationId missing from XAML source: $($missing -join ', ')"
    }
}

function Assert-RequiredAutomationIds {
    $required = [Collections.Generic.List[string]]::new()
    foreach ($id in $requiredAutomationIds) {
        $required.Add($id)
    }
    foreach ($gameId in $testGameIds) {
        $required.Add("GameCard_$($gameId.ToString('D'))")
    }

    $seen = @($auditElements | ForEach-Object automationId |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    $missing = @($required | Where-Object { $_ -notin $seen })
    if ($missing.Count -gt 0) {
        throw "Required AutomationIds were not observed: " +
            ($missing -join ', ')
    }
}

function Record-ConditionalAutomationIds {
    $seen = @($auditElements | ForEach-Object automationId |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    foreach ($entry in $conditionalAutomationIds.GetEnumerator()) {
        if ($entry.Key -in $seen) {
            Add-Result Accessibility "条件付きID: $($entry.Key)" PASS
        }
        else {
            Add-Result Accessibility "条件付きID: $($entry.Key)" SKIP `
                "$($entry.Value)。XAML sourceで宣言済み。"
        }
    }
}

function Move-TestWindowToBounds {
    param([object]$Bounds)

    $window = Get-MainWindowInfo
    $moved = [StaminaManagerUiTestNative]::MoveWindow(
        [IntPtr]$window.hwnd,
        [int]$Bounds.x,
        [int]$Bounds.y,
        [int]$Bounds.width,
        [int]$Bounds.height,
        $true)
    if (-not $moved) {
        throw 'The original window bounds could not be restored.'
    }

    Start-Sleep -Milliseconds 300
}

function Restore-OriginalApplicationData {
    Stop-VerifiedAppProcess $AppPid $expectedProcessPath
    Wait-DirectoryFilesWritable $dataDirectory
    Wait-DirectoryFilesWritable $settingsDirectory
    $dataSnapshot = Get-DirectoryContentSnapshot $dataBackupDirectory
    $settingsSnapshot = Get-DirectoryContentSnapshot $settingsBackupDirectory
    try {
        Copy-DirectoryFromBackup $dataBackupDirectory $dataDirectory `
            'Data-before-original-restore'
        Copy-DirectoryFromBackup $settingsBackupDirectory `
            $settingsDirectory 'Settings-before-original-restore'
        if ((Get-DirectoryFingerprint $dataDirectory) -ne
            $originalDataFingerprint) {
            throw 'Restored Data fingerprint does not match the original.'
        }
        if ((Get-DirectoryFingerprint $settingsDirectory) -ne
            $originalSettingsFingerprint) {
            throw 'Restored Settings fingerprint does not match the original.'
        }
    }
    catch {
        $initialRestoreError = $_
        try {
            Wait-DirectoryFilesWritable $dataDirectory
            Wait-DirectoryFilesWritable $settingsDirectory
            Restore-DirectoryFromBackup $dataBackupDirectory $dataDirectory `
                'Data-after-restore-failure'
            Restore-DirectoryFromBackup $settingsBackupDirectory `
                $settingsDirectory 'Settings-after-restore-failure'
            if ((Get-DirectoryFingerprint $dataDirectory) -ne
                    $originalDataFingerprint -or
                (Get-DirectoryFingerprint $settingsDirectory) -ne
                    $originalSettingsFingerprint) {
                throw 'Backup-based emergency restore verification failed.'
            }
        }
        catch {
            $backupRestoreError = $_
            Wait-DirectoryFilesWritable $dataDirectory
            Wait-DirectoryFilesWritable $settingsDirectory
            Restore-DirectoryContentSnapshot $dataDirectory $dataSnapshot `
                'Data-after-emergency-restore-failure'
            Restore-DirectoryContentSnapshot $settingsDirectory `
                $settingsSnapshot 'Settings-after-emergency-restore-failure'
            if ((Get-DirectoryFingerprint $dataDirectory) -ne
                    $originalDataFingerprint -or
                (Get-DirectoryFingerprint $settingsDirectory) -ne
                    $originalSettingsFingerprint) {
                throw "ApplicationData restore failed: $backupRestoreError"
            }
        }

        throw $initialRestoreError
    }

    $reconcileError = $null
    $postReconcileRestoreError = $null
    try {
        Start-PackagedApp | Out-Null
        Move-TestWindowToBounds $originalWindowBounds
        Start-Sleep -Milliseconds 500
    }
    catch {
        $reconcileError = $_
    }
    finally {
        try {
            Stop-VerifiedAppProcess $AppPid $expectedProcessPath
        }
        catch {
            if ($null -eq $reconcileError) {
                $reconcileError = $_
            }
        }

        try {
            Wait-DirectoryFilesWritable $dataDirectory
            Wait-DirectoryFilesWritable $settingsDirectory
            Restore-DirectoryFromBackup $dataBackupDirectory $dataDirectory `
                'Data-after-reconcile'
            Restore-DirectoryFromBackup $settingsBackupDirectory `
                $settingsDirectory 'Settings-after-reconcile'
            if ((Get-DirectoryFingerprint $dataDirectory) -ne
                $originalDataFingerprint) {
                throw 'Final Data fingerprint does not match the original.'
            }
            if ((Get-DirectoryFingerprint $settingsDirectory) -ne
                $originalSettingsFingerprint) {
                throw 'Final Settings fingerprint does not match the original.'
            }
        }
        catch {
            $finalRestoreError = $_
            try {
                Wait-DirectoryFilesWritable $dataDirectory
                Wait-DirectoryFilesWritable $settingsDirectory
                Restore-DirectoryContentSnapshot $dataDirectory `
                    $dataSnapshot 'Data-after-final-restore-failure'
                Restore-DirectoryContentSnapshot $settingsDirectory `
                    $settingsSnapshot 'Settings-after-final-restore-failure'
                if ((Get-DirectoryFingerprint $dataDirectory) -ne
                        $originalDataFingerprint -or
                    (Get-DirectoryFingerprint $settingsDirectory) -ne
                        $originalSettingsFingerprint) {
                    throw 'Emergency ApplicationData verification failed.'
                }
            }
            catch {
                $postReconcileRestoreError =
                    "$finalRestoreError Emergency restore failed: $_"
            }
            if ($null -eq $postReconcileRestoreError) {
                $postReconcileRestoreError = $finalRestoreError
            }
        }
    }

    if ($null -ne $postReconcileRestoreError) {
        throw $postReconcileRestoreError
    }

    if ($null -ne $reconcileError) {
        throw "Original notification reconciliation failed: $reconcileError"
    }
}

function Wait-PickerWindow {
    param([object]$MainWindowHandle)

    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        $windows = Invoke-WinApp ui list-windows -a $AppPid --json |
            ConvertFrom-Json
        $picker = $windows | Where-Object {
            $_.hwnd -ne $MainWindowHandle -and
            $_.title -ne 'PopupHost' -and
            ($_.className -match 'PickerHost|#32770|DesktopChildSiteBridge' -or
                $_.title -match 'Open|Save|開く|保存')
        } | Select-Object -First 1
        if ($null -ne $picker) {
            return $picker
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'The backup picker window did not appear.'
}

function Close-Picker {
    param([object]$WindowHandle)

    foreach ($selector in @('Cancel', 'キャンセル')) {
        & winapp ui invoke $selector -w $WindowHandle 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return
        }
    }

    throw 'The picker Cancel button could not be invoked.'
}

function Assert-AccessibilityAudit {
    $controlTypes = 'Button|TextBox|NumberBox|ComboBox|ToggleSwitch|Edit|Spinner'
    $missing = $auditElements | Where-Object {
        $_.type -match $controlTypes -and
        $_.className -notmatch 'PickerHost|#32770|CabinetWClass' -and
        $_.automationId -notmatch '^(Minimize|Maximize|Close)$' -and
        ([string]::IsNullOrWhiteSpace($_.automationId) -or
            [string]::IsNullOrWhiteSpace($_.name))
    } | Sort-Object state, type, automationId, name -Unique
    if (@($missing).Count -gt 0) {
        $details = $missing | ForEach-Object {
            "$($_.state):$($_.type) id='$($_.automationId)' name='$($_.name)'"
        }
        throw "Interactive controls missing ID or name: $($details -join '; ')"
    }
}

function Get-AccessibilityDisplayState {
    $isHighContrast = $null
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $isHighContrast = [bool][System.Windows.Forms.SystemInformation]::HighContrast
    }
    catch {
    }

    if ($null -eq $isHighContrast) {
        try {
            $flags = [int](Get-ItemPropertyValue `
                -Path 'HKCU:\Control Panel\Accessibility\HighContrast' `
                -Name Flags -ErrorAction Stop)
            $isHighContrast = $flags -ne 0
        }
        catch {
        }
    }

    $textScale = $null
    try {
        $textScalePath = 'HKCU:\Software\Microsoft\Accessibility'
        if (Test-Path -LiteralPath $textScalePath) {
            $textScale = [int](Get-ItemPropertyValue `
                -Path $textScalePath -Name TextScaleFactor `
                -ErrorAction Stop)
        }
        else {
            $textScale = 100
        }
    }
    catch {
    }

    return [pscustomobject]@{
        HighContrast = $isHighContrast
        TextScale = $textScale
    }
}

function Assert-AboutButtonAccessibility {
    foreach ($buttonId in @('OpenGitHubButton', 'OpenReadmeButton')) {
        Get-ElementMatch $buttonId | Out-Null
        Wait-ControlEnabled $buttonId $true
        $properties = (
            Invoke-WinApp ui get-property $buttonId -a $AppPid --json |
                ConvertFrom-Json).properties
        foreach ($propertyName in @('Name', 'HelpText')) {
            if ([string]::IsNullOrWhiteSpace(
                    [string]$properties.$propertyName)) {
                throw "$buttonId has no UIA $propertyName."
            }
        }
    }
}

function Assert-AboutLayoutBounds {
    param([string]$State)

    $window = Get-MainWindowInfo
    if ([int]$window.width -le 0 -or [int]$window.height -le 0) {
        throw "$State window bounds have no area."
    }

    $viewport = Get-ElementMatch AboutScrollViewer
    $root = Get-ElementMatch AboutPageRoot
    $windowRight = [int]$window.x + [int]$window.width
    $windowBottom = [int]$window.y + [int]$window.height
    $viewportRight = [int]$viewport.x + [int]$viewport.width
    $viewportBottom = [int]$viewport.y + [int]$viewport.height
    if ([int]$viewport.width -le 0 -or [int]$viewport.height -le 0 -or
        [int]$viewport.x -lt [int]$window.x -or
        [int]$viewport.y -lt [int]$window.y -or
        $viewportRight -gt $windowRight -or
        $viewportBottom -gt $windowBottom) {
        throw "$State AboutScrollViewer is outside the window."
    }
    $rootRight = [int]$root.x + [int]$root.width
    $rootBottom = [int]$root.y + [int]$root.height
    if ([int]$root.width -le 0 -or [int]$root.height -le 0 -or
        [int]$root.x -lt [int]$window.x -or
        $rootRight -gt $windowRight -or
        [int]$root.y -lt [int]$window.y -or
        $rootBottom -gt $windowBottom -or
        [int]$root.x -lt [int]$viewport.x -or
        $rootRight -gt $viewportRight -or
        [int]$root.y -lt [int]$viewport.y -or
        $rootBottom -gt $viewportBottom) {
        throw "$State bounds are invalid for AboutPageRoot."
    }
    foreach ($automationId in @(
            'AboutVersionText',
            'OpenGitHubButton',
            'OpenReadmeButton',
            'AboutReadmeHeading',
            'AboutReadmeDescription')) {
        Invoke-WinApp ui scroll-into-view $automationId -a $AppPid |
            Out-Null
        $bounds = Get-ElementMatch $automationId
        $x = [int]$bounds.x
        $y = [int]$bounds.y
        $right = $x + [int]$bounds.width
        $bottom = $y + [int]$bounds.height
        if ([int]$bounds.width -le 0 -or [int]$bounds.height -le 0 -or
            $x -lt [int]$window.x -or $right -gt $windowRight) {
            throw "$State bounds are invalid for $automationId."
        }
        if ($x -lt [int]$viewport.x -or $right -gt $viewportRight -or
            $y -lt [int]$viewport.y -or $bottom -gt $viewportBottom) {
            throw "$State viewport bounds are invalid for $automationId."
        }
    }

    $scrollProperties = (
        Invoke-WinApp ui get-property AboutScrollViewer -a $AppPid `
            -p HorizontallyScrollable --json | ConvertFrom-Json).properties
    $horizontalScrollable = [string]$scrollProperties.HorizontallyScrollable
    if ([string]::IsNullOrWhiteSpace($horizontalScrollable)) {
        throw "$State horizontal scroll state was not exposed by UIA."
    }
    if ($horizontalScrollable -notin @('0x0', 'false', '0')) {
        throw "$State unexpectedly allows horizontal scrolling."
    }
}

function Invoke-AboutUiAudit {
    param([string]$State)

    $savedBounds = $null
    $primaryError = $null
    try {
        Invoke-WinApp ui wait-for NavAbout -a $AppPid -t 5000 | Out-Null
        Invoke-WinApp ui invoke NavAbout -a $AppPid | Out-Null
        if ($State -eq 'About') {
            Invoke-WinApp ui wait-for VersionFooterText -a $AppPid -t 5000 |
                Out-Null
        }
        foreach ($automationId in @(
                'AboutPageRoot',
                'AboutVersionText',
                'OpenGitHubButton',
                'OpenReadmeButton',
                'AboutReadmeHeading',
                'AboutReadmeDescription')) {
            Invoke-WinApp ui wait-for $automationId -a $AppPid -t 5000 |
                Out-Null
        }
        Assert-AboutButtonAccessibility

        $savedBounds = Get-MainWindowInfo
        $narrowWidth = [Math]::Max(
            320,
            [Math]::Min(560, [int]$savedBounds.width - 160))
        Move-TestWindowToBounds ([pscustomobject]@{
                x = [int]$savedBounds.x
                y = [int]$savedBounds.y
                width = $narrowWidth
                height = [int]$savedBounds.height
            })
        $narrowBounds = Get-MainWindowInfo
        $actualNarrowWidth = [int]$narrowBounds.width
        if ($actualNarrowWidth -ge [int]$savedBounds.width -or
            ($actualNarrowWidth -ne $narrowWidth -and
                $actualNarrowWidth -gt 560)) {
            throw "The About narrow-width case measured $actualNarrowWidth; " +
                "target was $narrowWidth and original was " +
                "$($savedBounds.width)."
        }
        Assert-AboutLayoutBounds $State
        Collect-AuditSnapshot $State
    }
    catch {
        $primaryError = $_
    }
    finally {
        if ($null -ne $savedBounds) {
            try {
                Move-TestWindowToBounds $savedBounds
                Assert-WindowBoundsEqual $savedBounds (Get-MainWindowInfo) `
                    "$State restoration"
            }
            catch {
                Add-Result About "$State window bounds復元" FAIL `
                    $_.Exception.Message
                if ($null -eq $primaryError) {
                    $primaryError = $_
                }
            }
        }

        try {
            Invoke-WinApp ui invoke NavOverview -a $AppPid | Out-Null
            Invoke-WinApp ui wait-for AddGameCard -a $AppPid -t 5000 |
                Out-Null
        }
        catch {
            Add-Result About "$State Overview復帰" FAIL `
                $_.Exception.Message
            if ($null -eq $primaryError) {
                $primaryError = $_
            }
        }
    }

    if ($null -ne $primaryError) {
        throw $primaryError
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory |
    Out-Null
New-Item -ItemType Directory -Force -Path $screenshotDirectory |
    Out-Null
$accessibilityDisplayState = Get-AccessibilityDisplayState

try {
    $identity = Get-VerifiedAppIdentity $AppPid
    $expectedProcessPath = $identity.ProcessPath
    $packageFamilyName = $identity.PackageFamilyName
    $appOutputDirectory = $identity.AppOutputDirectory
    $isIdentityVerified = $true
    Assert-XamlAutomationSources
    $originalWindowBounds = Get-MainWindowInfo
    $mutexToken = $packageFamilyName -replace '[^A-Za-z0-9_.-]', '_'
    $executionMutexName = "Local\StaminaManager.UiTests.$mutexToken"
    $executionMutex = [Threading.Mutex]::new($false, $executionMutexName)
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

    $packageRoot = Join-Path $env:LOCALAPPDATA (
        "Packages\$packageFamilyName")
    $dataDirectory = Join-Path $env:LOCALAPPDATA (
        "Packages\$packageFamilyName\LocalState\Data")
    $settingsDirectory = Join-Path $packageRoot 'Settings'
    if (-not (Test-Path -LiteralPath $dataDirectory -PathType Container)) {
        throw "ApplicationData Data directory was not found: $dataDirectory"
    }
    if (-not (Test-Path -LiteralPath $settingsDirectory -PathType Container)) {
        throw "ApplicationData Settings directory was not found: $settingsDirectory"
    }

    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
        "StaminaManager-UiTests-$([Guid]::NewGuid().ToString('N'))")
    $dataBackupDirectory = Join-Path $temporaryRoot 'Data-original'
    $settingsBackupDirectory = Join-Path $temporaryRoot 'Settings-original'
    $generatedResidueDirectory = Join-Path $temporaryRoot 'Generated-residue'
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
    Stop-VerifiedAppProcess $AppPid $expectedProcessPath
    Wait-DirectoryFilesWritable $dataDirectory
    Wait-DirectoryFilesWritable $settingsDirectory
    $originalDataFingerprint = Get-DirectoryFingerprint $dataDirectory
    $originalSettingsFingerprint = Get-DirectoryFingerprint $settingsDirectory
    Copy-Item -LiteralPath $dataDirectory `
        -Destination $dataBackupDirectory -Recurse -Force
    Copy-Item -LiteralPath $settingsDirectory `
        -Destination $settingsBackupDirectory `
        -Recurse -Force
    if ((Get-DirectoryFingerprint $dataBackupDirectory) -ne
        $originalDataFingerprint) {
        throw 'ApplicationData Data backup verification failed.'
    }
    if ((Get-DirectoryFingerprint $settingsBackupDirectory) -ne
        $originalSettingsFingerprint) {
        throw 'ApplicationData Settings backup verification failed.'
    }

    $isBackupVerified = $true
    Add-Result Safety 'プロセスID・実行パス検証' PASS $expectedProcessPath
    Add-Result Safety '停止後にApplicationDataを事前退避' PASS `
        "Data=$dataBackupDirectory; Settings=$settingsBackupDirectory"

    Write-EmptyFixture
    $writtenFixtureState = Get-EmptyFixtureState
    $writtenFixtureState = Wait-EmptyFixtureApplied `
        $writtenFixtureState.Fingerprint '起動前'
    Add-Result Safety '起動前の空games fixture検証' PASS `
        (Format-EmptyFixtureState $writtenFixtureState)
    Start-PackagedApp | Out-Null
    if (Test-UiElement $AppPid ReturnOverviewButton 1000) {
        Invoke-WinApp ui invoke ReturnOverviewButton -a $AppPid | Out-Null
    }

    Invoke-WinApp ui wait-for NavOverview -a $AppPid -t 5000 |
        Out-Null
    $appliedFixtureState = Wait-EmptyFixtureApplied `
        $writtenFixtureState.Fingerprint '起動後'
    Add-Result Safety '起動後の空games fixture反映' PASS `
        (Format-EmptyFixtureState $appliedFixtureState)

    Invoke-UiTest Navigation 'OverviewとSettingsの往復' {
        Invoke-WinApp ui wait-for NavOverview -a $AppPid -t 5000 |
            Out-Null
        Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for ThemeToggle -a $AppPid -t 5000 |
            Out-Null
        Invoke-WinApp ui invoke NavOverview -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for AddGameCard -a $AppPid -t 5000 |
            Out-Null
        Save-Screenshot '01-overview-initial'
        Collect-AuditSnapshot Overview
    }

    Invoke-UiTest About 'About navigationと狭幅UIA監査' {
        Invoke-AboutUiAudit About
    }

    if ($accessibilityDisplayState.HighContrast -eq $true -or
        $accessibilityDisplayState.TextScale -ge 200) {
        Invoke-UiTest Accessibility 'About HC/200%狭幅UIA監査' {
            Invoke-AboutUiAudit AboutAccessibility
        }
    }
    elseif ($null -eq $accessibilityDisplayState.HighContrast -or
        $null -eq $accessibilityDisplayState.TextScale) {
        Add-Result Accessibility 'About HC/200%狭幅UIA監査' SKIP `
            'High Contrastまたはテキストスケールのread-only検出に失敗したため実行しない。'
    }
    else {
        Add-Result Accessibility 'About HC/200%狭幅UIA監査' SKIP `
            '現在のOS状態がHigh Contrastでも200%テキストでもないため実行しない。'
    }

    Invoke-UiTest Editor '空入力の検証' {
        Invoke-WinApp ui invoke AddGameCard -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for GameEditorDialog -a $AppPid -t 5000 |
            Out-Null
        Invoke-WinApp ui set-value GameNameInput '' -a $AppPid |
            Out-Null
        Invoke-WinApp ui wait-for GameEditorSaveButton -a $AppPid `
            -p IsEnabled --value False -t 3000 | Out-Null
        Save-Screenshot '02-editor-validation'
        Collect-AuditSnapshot Editor
    }

    Invoke-UiTest Editor 'ゲームを追加して保存' {
        $emptyDataFingerprint = Get-DataFileFingerprint
        Invoke-WinApp ui set-value GameNameInput $testGameName `
            -a $AppPid | Out-Null
        Invoke-WinApp ui set-value CurrentStaminaInput 98 `
            -a $AppPid | Out-Null
        Invoke-WinApp ui set-value MaxStaminaInput 100 `
            -a $AppPid | Out-Null
        Set-NumberBoxFromKeyboard RecoveryMinutesInput '1'
        Set-NumberBoxFromKeyboard RecoverySecondsInput '1.5'
        Invoke-WinApp ui wait-for RecoverySecondsErrorText -a $AppPid `
            -p Name --value '整数で入力してください。' -t 3000 | Out-Null
        Invoke-WinApp ui wait-for RecoverySecondsInput -a $AppPid `
            -p HelpText --value '整数で入力してください。' -t 3000 |
            Out-Null
        Collect-AuditSnapshot EditorSecondsValidation
        Assert-EditorSaveIsBlocked $emptyDataFingerprint

        Set-NumberBoxFromKeyboard RecoveryMinutesInput '0'
        Set-NumberBoxFromKeyboard RecoverySecondsInput '0'
        Wait-UiElementNameEmpty $AppPid RecoverySecondsErrorText
        $intervalError =
            '回復時間は合計1秒～525,600分で入力してください。'
        Invoke-WinApp ui wait-for RecoveryIntervalErrorText -a $AppPid `
            -p Name --value $intervalError `
            -t 3000 | Out-Null
        foreach ($inputId in @(
                'RecoveryMinutesInput',
                'RecoverySecondsInput')) {
            Invoke-WinApp ui wait-for $inputId -a $AppPid `
                -p HelpText --value $intervalError -t 3000 | Out-Null
        }
        Collect-AuditSnapshot EditorIntervalValidation
        Assert-EditorSaveIsBlocked $emptyDataFingerprint

        Set-NumberBoxFromKeyboard RecoveryMinutesInput '8'
        Set-NumberBoxFromKeyboard RecoverySecondsInput '30'
        Invoke-WinApp ui invoke GameNotificationToggle -a $AppPid |
            Out-Null
        Wait-UiElementNameEmpty $AppPid RecoverySecondsErrorText
        Wait-UiElementNameEmpty $AppPid RecoveryIntervalErrorText
        Invoke-WinApp ui wait-for GameNotificationToggle -a $AppPid `
            --value Off -t 3000 | Out-Null
        Save-Screenshot '02-editor-recovery-validation'
        Invoke-WinApp ui wait-for GameEditorSaveButton -a $AppPid `
            -p IsEnabled --value True -t 3000 | Out-Null
        Invoke-WinApp ui invoke GameEditorSaveButton -a $AppPid |
            Out-Null
        Invoke-WinApp ui wait-for GameEditorDialog -a $AppPid `
            --gone -t 5000 | Out-Null
        $game = Get-DataGame $testGameName
        if ([int]$game.recoveryMinutes -ne 8 -or
            [int]$game.recoverySeconds -ne 30 -or
            [bool]$game.isNotificationEnabled) {
            throw 'Saved recovery interval or notification setting differed.'
        }
        $script:testGameId = [Guid]$game.id
        $script:testGameIds.Add($testGameId)
        $cardId = "GameCard_$($testGameId.ToString('D'))"
        Invoke-WinApp ui wait-for $cardId -a $AppPid -t 5000 |
            Out-Null
        Save-Screenshot '03-game-added'
        Collect-AuditSnapshot AddedGame
    }

    Invoke-UiTest Editor '編集・削除確認から戻る・保存' {
        if ($null -eq $testGameId) {
            throw 'The preceding add test did not produce a game ID.'
        }

        $cardId = "GameCard_$($testGameId.ToString('D'))"
        Invoke-WinApp ui invoke $cardId -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for GameEditorDeleteButton `
            -a $AppPid -t 5000 | Out-Null
        Wait-NumberBoxValue RecoveryMinutesInput '8'
        Wait-NumberBoxValue RecoverySecondsInput '30'
        Invoke-WinApp ui wait-for GameNotificationToggle -a $AppPid `
            --value Off -t 3000 | Out-Null
        Invoke-WinApp ui set-value GameNameInput $editedGameName `
            -a $AppPid | Out-Null
        Collect-AuditSnapshot EditorDelete
        Invoke-WinApp ui invoke GameEditorDeleteButton -a $AppPid |
            Out-Null
        Invoke-WinApp ui wait-for DeleteBackButton -a $AppPid -t 3000 |
            Out-Null
        Collect-AuditSnapshot DeleteConfirmation
        Invoke-WinApp ui invoke DeleteBackButton -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for GameNameInput -a $AppPid `
            --value $editedGameName -t 3000 | Out-Null
        Invoke-WinApp ui invoke GameEditorSaveButton -a $AppPid |
            Out-Null
        Get-DataGame $editedGameName | Out-Null
    }

    Invoke-UiTest Overview '3ゲームと3列・2列・最小幅2列' {
        $script:notificationTestGameId = Add-TestGame $secondGameName
        $script:testGameIds.Add($notificationTestGameId)
        $script:testGameIds.Add((Add-TestGame $thirdGameName))
        if ($testGameIds.Count -ne 3) {
            throw "Expected three game IDs, actual $($testGameIds.Count)."
        }

        Set-OverviewContentWidth 720 | Out-Null
        Start-Sleep -Milliseconds 750
        Assert-CardColumns 3 $testGameIds.ToArray()
        Save-Screenshot '03-three-cards-wide'
        Save-Screenshot '04-columns-3-content720'
        Set-OverviewContentWidth 719 | Out-Null
        Start-Sleep -Milliseconds 750
        Assert-CardColumns 2 $testGameIds.ToArray()
        Save-Screenshot '05-columns-2-content719'
        $minimumWidth = Set-WindowToStandardMinimumWidth
        Assert-CardColumns 2 $testGameIds.ToArray()
        Save-Screenshot '06-columns-2-minimum'
        if ($minimumWidth.EffectiveWidth -ge $threeColumnMinimumWidth) {
            throw 'The Standard minimum width did not exercise two columns.'
        }
        Collect-AuditSnapshot ThreeCards
        Set-OverviewContentWidth 719 | Out-Null
        Invoke-WinApp ui wait-for NavSettings -a $AppPid -t 5000 |
            Out-Null
    }

    Invoke-UiTest Compact '選択・boundsの再起動永続化と復帰' {
        $standardBounds = Get-MainWindowInfo
        try {
            Invoke-WinApp ui invoke CompactModeButton -a $AppPid | Out-Null
            Invoke-WinApp ui wait-for ReturnOverviewButton -a $AppPid `
                -t 5000 | Out-Null
            Select-ComboItem CompactGameSelector $editedGameName
            Save-Screenshot '06-compact'
            Collect-AuditSnapshot CompactBeforeRestart
            $compactBounds = Get-MainWindowInfo

            Stop-VerifiedAppProcess $AppPid $expectedProcessPath
            Start-PackagedApp | Out-Null
            Invoke-WinApp ui wait-for ReturnOverviewButton -a $AppPid `
                -t 5000 | Out-Null
            Invoke-WinApp ui wait-for CompactGameSelector -a $AppPid `
                --value $editedGameName -t 5000 | Out-Null
            Assert-WindowBoundsEqual $compactBounds (Get-MainWindowInfo) `
                'Compact'
            Collect-AuditSnapshot CompactAfterRestart
        }
        finally {
            if (Test-UiElement $AppPid ReturnOverviewButton 1000) {
                Invoke-WinApp ui invoke ReturnOverviewButton -a $AppPid |
                    Out-Null
            }
            Invoke-WinApp ui wait-for NavOverview -a $AppPid -t 5000 |
                Out-Null
            Move-TestWindowToBounds $standardBounds
        }
        Assert-WindowBoundsEqual $standardBounds (Get-MainWindowInfo) `
            'Standard'
    }

    Invoke-UiTest Settings 'Light/Darkと5背景' {
        Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for BackdropSelector -a $AppPid -t 5000 |
            Out-Null
        $initialTheme = Get-ControlValue ThemeToggle
        $initialBackdrop = Get-ControlValue BackdropSelector
        if ($initialTheme -eq 'On') {
            Save-Screenshot '05-theme-dark'
        }
        else {
            Save-Screenshot '05-theme-light'
        }

        Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
        $oppositeTheme = if ($initialTheme -eq 'On') { 'Off' } else { 'On' }
        Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
            --value $oppositeTheme -t 3000 | Out-Null
        Save-Screenshot "06-theme-$($oppositeTheme.ToLowerInvariant())"
        Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
            --value $initialTheme -t 3000 | Out-Null

        foreach ($backdrop in @(
                'Mica', 'Acrylic', 'Solid')) {
            Select-ComboItem BackdropSelector $backdrop
            Save-Screenshot "07-backdrop-$($backdrop.ToLowerInvariant())"
        }
        Select-ComboItem BackdropSelector $initialBackdrop
        Collect-AuditSnapshot SettingsAppearance
    }

    Invoke-UiTest Settings 'Acrylic不透明度0/50/100の診断・disabled・永続化とSettings往復' {
        $initialBackdrop = Get-ControlValue BackdropSelector
        Scroll-ToSettingsControl AcrylicOpacitySlider
        $initialOpacity = [int](Get-ControlValue AcrylicOpacitySlider)
        $opacityValues = @(0, 50, 100)
        $expectedDiagnostics = @{
            0 = 'Acrylic|TintOpacity=0.00|SolidSurface=Collapsed'
            50 = 'Acrylic|TintOpacity=0.50|SolidSurface=Collapsed'
            100 = 'Acrylic|TintOpacity=1.00|SolidSurface=Collapsed'
        }

        try {
            foreach ($inactiveBackdrop in @('Mica', 'Solid')) {
                Select-ComboItem BackdropSelector $inactiveBackdrop
                Scroll-ToSettingsControl AcrylicOpacitySlider
                Wait-ControlEnabled AcrylicOpacitySlider $false
            }

            Scroll-ToSettingsControl AcrylicOpacitySlider
            $isAcrylicAvailable = $true
            try {
                Select-ComboItem BackdropSelector 'Acrylic'
                Scroll-ToSettingsControl AcrylicOpacitySlider
                Wait-ControlEnabled AcrylicOpacitySlider $true
            }
            catch {
                $isAcrylicAvailable = $false
            }
            Scroll-ToSettingsControl AcrylicOpacitySlider

            if ($isAcrylicAvailable) {
                foreach ($percent in $opacityValues) {
                    Invoke-WinApp ui set-value AcrylicOpacitySlider $percent `
                        -a $AppPid | Out-Null
                    Invoke-WinApp ui wait-for AcrylicOpacitySlider -a $AppPid `
                        -p Value --value "$percent" -t 3000 | Out-Null
                    Wait-BackdropDiagnostic $expectedDiagnostics[$percent]
                    if ($percent -ne 100) {
                        Wait-PersistedAcrylicOpacity $percent
                    }
                }
            }
            else {
                Wait-ControlEnabled AcrylicOpacitySlider $false
                Wait-BackdropDiagnostic 'Solid|SolidSurface=Visible'
            }

            Invoke-WinApp ui invoke NavOverview -a $AppPid | Out-Null
            Invoke-WinApp ui wait-for AddGameCard -a $AppPid -t 5000 |
                Out-Null
            Start-Sleep -Milliseconds 400

            Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
            Invoke-WinApp ui wait-for AcrylicOpacitySlider -a $AppPid `
                -t 5000 | Out-Null
            if ($isAcrylicAvailable) {
                Wait-PersistedAcrylicOpacity 100
            }
        }
        finally {
            try {
                Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
                Invoke-WinApp ui wait-for BackdropSelector -a $AppPid `
                    -t 5000 | Out-Null
                try {
                    Select-ComboItem BackdropSelector 'Acrylic'
                    Scroll-ToSettingsControl AcrylicOpacitySlider
                    Wait-ControlEnabled AcrylicOpacitySlider $true
                    Invoke-WinApp ui set-value AcrylicOpacitySlider `
                        $initialOpacity -a $AppPid | Out-Null
                    Invoke-WinApp ui wait-for AcrylicOpacitySlider `
                        -a $AppPid -p Value --value "$initialOpacity" `
                        -t 3000 | Out-Null
                    Wait-PersistedAcrylicOpacity $initialOpacity
                }
                catch {
                }
                Select-ComboItem BackdropSelector $initialBackdrop
            }
            catch {
            }
        }
    }

    Invoke-UiTest Settings '閉じる動作とtray redirect復帰' {
        Scroll-ToSettingsControl CloseBehaviorSelector
        $initialClose = Get-ControlValue CloseBehaviorSelector
        $oppositeClose = if ($initialClose -eq 'アプリを終了') {
            'タスクトレイへ格納'
        } else { 'アプリを終了' }
        Select-ComboItem CloseBehaviorSelector $oppositeClose
        Select-ComboItem CloseBehaviorSelector 'タスクトレイへ格納'
        Close-ToTrayAndRestore
        Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
        Invoke-WinApp ui wait-for ThemeToggle -a $AppPid -t 5000 |
            Out-Null
        Scroll-ToSettingsControl CloseBehaviorSelector
        Invoke-WinApp ui wait-for CloseBehaviorSelector -a $AppPid `
            --value 'タスクトレイへ格納' -t 5000 | Out-Null
        Select-ComboItem CloseBehaviorSelector $initialClose
        Collect-AuditSnapshot SettingsGeneral
    }

    $isWindowsNotificationDisabled = Test-UiElement $AppPid `
        OpenWindowsNotificationSettingsButton 1000
    if ($isWindowsNotificationDisabled) {
        Invoke-UiTest Settings 'Windows通知無効の案内と設定遷移' {
            Scroll-ToSettingsControl OpenWindowsNotificationSettingsButton
            Invoke-WinApp ui wait-for `
                OpenWindowsNotificationSettingsButton -a $AppPid `
                -t 5000 | Out-Null
            Collect-AuditSnapshot SettingsNotificationsDisabled
            Save-Screenshot '08-notifications-disabled'
            Invoke-WinApp ui invoke `
                OpenWindowsNotificationSettingsButton -a $AppPid |
                Out-Null
            $settingsPid = Wait-WindowsNotificationSettings
            if ($settingsPid -le 0) {
                throw 'Windows notification settings PID is invalid.'
            }
            Invoke-WinApp ui focus NavSettings -a $AppPid | Out-Null
        }
        Add-Result Settings '通知ledgerの抑止・再予約・lead変更' SKIP `
            'Windows側でStaminaManagerの通知が無効なため予約生成を行わない。'
    }
    else {
        Add-Result Manual 'Windows通知無効状態' SKIP `
            'OS側でStaminaManagerの通知を無効にした場合のみ確認。'
        Invoke-UiTest Settings '通知ledgerの抑止・再予約・lead変更' {
            if ($null -eq $notificationTestGameId) {
                throw '通知ONのテストゲームが作成されていません。'
            }

            Scroll-ToSettingsControl NotificationLeadInput
            Invoke-WinApp ui wait-for NotificationLeadInput -a $AppPid `
                -t 5000 | Out-Null
            $initialNotifications = Get-ControlValue NotificationsToggle
            $initialLead = [int](Get-ControlValue InputBox)
            if ($initialNotifications -eq 'On') {
                Invoke-WinApp ui invoke NotificationsToggle -a $AppPid |
                    Out-Null
                Invoke-WinApp ui wait-for NotificationsToggle -a $AppPid `
                    --value Off -t 5000 | Out-Null
            }

            Wait-LedgerEntry $notificationTestGameId Suppressed `
                $initialLead | Out-Null
            Invoke-WinApp ui invoke NotificationsToggle -a $AppPid | Out-Null
            Invoke-WinApp ui wait-for NotificationsToggle -a $AppPid `
                --value On -t 5000 | Out-Null
            $scheduled = Wait-LedgerEntry `
                $notificationTestGameId Scheduled $initialLead

            $changedLead = if ($initialLead -lt 525600) {
                $initialLead + 1
            } else { $initialLead - 1 }
            Invoke-WinApp ui set-value InputBox $changedLead `
                -a $AppPid | Out-Null
            Invoke-WinApp ui focus NavSettings -a $AppPid | Out-Null
            Invoke-WinApp ui wait-for InputBox -a $AppPid `
                --value $changedLead -t 5000 | Out-Null
            $rescheduled = Wait-LedgerEntry `
                $notificationTestGameId Scheduled $changedLead
            if ($scheduled.key -eq $rescheduled.key -and
                [int]$scheduled.leadMinutes -eq
                    [int]$rescheduled.leadMinutes) {
                throw 'Notification ledger did not change after lead update.'
            }

            Invoke-WinApp ui set-value InputBox $initialLead `
                -a $AppPid | Out-Null
            Invoke-WinApp ui focus NavSettings -a $AppPid | Out-Null
            Invoke-WinApp ui wait-for InputBox -a $AppPid `
                --value $initialLead -t 5000 | Out-Null
            Wait-LedgerEntry $notificationTestGameId Scheduled `
                $initialLead | Out-Null

            Invoke-WinApp ui invoke NotificationsToggle -a $AppPid |
                Out-Null
            Invoke-WinApp ui wait-for NotificationsToggle -a $AppPid `
                --value Off -t 5000 | Out-Null
            Wait-LedgerEntry $notificationTestGameId Suppressed `
                $initialLead | Out-Null
            Collect-AuditSnapshot SettingsNotifications
        }
    }

    Invoke-UiTest Settings 'バックアップpickerをキャンセル' {
        $mainWindowHandle = Get-MainWindowHandle
        Scroll-ToSettingsControl ExportBackupButton
        foreach ($buttonId in @('ExportBackupButton', 'ImportBackupButton')) {
            Invoke-WinApp ui scroll-into-view $buttonId -a $AppPid |
                Out-Null
            Invoke-WinApp ui invoke $buttonId -a $AppPid | Out-Null
            $picker = Wait-PickerWindow $mainWindowHandle
            Close-Picker $picker.hwnd
            Start-Sleep -Milliseconds 300
        }
        Collect-AuditSnapshot SettingsData
    }

    Invoke-UiTest Accessibility 'app-owned interactive UIAのIDとName' {
        Assert-AccessibilityAudit
        Assert-RequiredAutomationIds
        Record-ConditionalAutomationIds
    }
}
catch {
    Add-Result Setup 'UIスイートのセットアップまたは実行' FAIL `
        $_.Exception.Message
}
finally {
    try {
        if ($isIdentityVerified -and $isBackupVerified) {
            try {
                Restore-OriginalApplicationData
                Add-Result Safety '対象プロセスだけを停止' PASS `
                    "最終PIDを同一path検証: $expectedProcessPath"
                Add-Result Safety 'ApplicationData完全復元' PASS `
                    'DataとSettingsのpath・size・SHA-256が一致'
            }
            catch {
                $restoreError = $_
                Add-Result Safety '対象プロセスだけを停止' FAIL `
                    $_.Exception.Message
                Add-Result Safety 'ApplicationData完全復元' FAIL `
                    $_.Exception.Message
            }
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
}

$passed = @($results | Where-Object status -eq PASS).Count
$failed = @($results | Where-Object status -eq FAIL).Count
$skipped = @($results | Where-Object status -eq SKIP).Count
$report = [ordered]@{
    schemaVersion = 2
    runId = $runId
    startedAt = $startedAt.ToString('O')
    finishedAt = [DateTimeOffset]::Now.ToString('O')
    appPid = $AppPid
    processPath = $expectedProcessPath
    appOutputDirectory = $appOutputDirectory
    packageFamilyName = $packageFamilyName
    dataDirectory = $dataDirectory
    settingsDirectory = $settingsDirectory
    executionMutexName = $executionMutexName
    dataBackupDirectory = if (
        -not [string]::IsNullOrWhiteSpace($dataBackupDirectory) -and
        (Test-Path -LiteralPath $dataBackupDirectory) -and
        @(Get-ChildItem -LiteralPath $dataBackupDirectory `
            -Force -File -Recurse).Count -gt 0) {
        $dataBackupDirectory
    } else { $null }
    settingsBackupDirectory = if (
        -not [string]::IsNullOrWhiteSpace($settingsBackupDirectory) -and
        (Test-Path -LiteralPath $settingsBackupDirectory) -and
        @(Get-ChildItem -LiteralPath $settingsBackupDirectory `
            -Force -File -Recurse).Count -gt 0) {
        $settingsBackupDirectory
    } else { $null }
    generatedResidueDirectory = if (
        -not [string]::IsNullOrWhiteSpace($generatedResidueDirectory) -and
        (Test-Path -LiteralPath $generatedResidueDirectory) -and
        @(Get-ChildItem -LiteralPath $generatedResidueDirectory `
            -Force -File -Recurse).Count -gt 0) {
        $generatedResidueDirectory
    } else { $null }
    fingerprints = [ordered]@{
        originalData = $originalDataFingerprint
        restoredData = if (
            $isBackupVerified -and $null -eq $restoreError -and
            -not [string]::IsNullOrWhiteSpace($dataDirectory)) {
            Get-DirectoryFingerprint $dataDirectory
        } else { $null }
        originalSettings = $originalSettingsFingerprint
        restoredSettings = if (
            $isBackupVerified -and $null -eq $restoreError -and
            -not [string]::IsNullOrWhiteSpace($settingsDirectory)) {
            Get-DirectoryFingerprint $settingsDirectory
        } else { $null }
    }
    summary = [ordered]@{
        passed = $passed
        failed = $failed
        skipped = $skipped
        restorePassed = $null -eq $restoreError -and $isBackupVerified
    }
    results = $results
}
[IO.File]::WriteAllText(
    $resultPath,
    ($report | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

Write-Host "Results: $resultPath"
Write-Host "Passed: $passed | Failed: $failed | Skipped: $skipped"
if ($failed -gt 0 -or $null -ne $restoreError) {
    exit 1
}

exit 0
