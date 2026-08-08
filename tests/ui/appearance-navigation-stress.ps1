param(
    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$AppPid,
    [ValidateRange(3, 100)]
    [int]$CycleCount = 3,
    [string]$ResultPath = ''
)

$ErrorActionPreference = 'Stop'
$backdrops = @('Mica', 'Acrylic', 'Solid')
$expectedDiagnostics = @{
    Mica = 'Mica|SolidSurface=Collapsed'
    Solid = 'Solid|SolidSurface=Visible'
}
$startedAtUtc = [DateTime]::UtcNow
$runId = '{0}-{1}' -f (
    Get-Date -Format 'yyyyMMdd-HHmmss-fff'),
    ([Guid]::NewGuid().ToString('N').Substring(0, 8))
if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $ResultPath = Join-Path $PSScriptRoot (
        "results\appearance-navigation-stress-$runId.json")
}
$ResultPath = [IO.Path]::GetFullPath($ResultPath)

$events = [Collections.Generic.List[object]]::new()
$initialTheme = $null
$initialBackdrop = $null
$expectedProcessPath = $null
$packageFamilyName = $null
$packageInstallLocation = $null
$dataFilePath = $null
$executionMutex = $null
$executionMutexName = $null
$isExecutionMutexAcquired = $false
$lastCycle = 0
$lastBackdrop = $null
$testError = $null
$restoreError = $null
$isRestorationAttempted = $false
$isRestorationSucceeded = $false
$isCrashObserved = $false

Add-Type -AssemblyName UIAutomationClient

function Add-StressEvent {
    param(
        [Parameter(Mandatory)]
        [string]$Action,
        [string]$Detail = '')

    $script:events.Add([ordered]@{
        atUtc = [DateTime]::UtcNow.ToString('O')
        action = $Action
        detail = $Detail
    })
}

function Invoke-WinApp {
    $output = & winapp @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }

    return $output
}

function Assert-AppAlive {
    param([string]$Context)

    $process = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        $script:isCrashObserved = $true
        throw "StaminaManager exited during $Context."
    }

    $actualPath = [IO.Path]::GetFullPath($process.Path)
    if ($actualPath -ne $expectedProcessPath) {
        $script:isCrashObserved = $true
        throw "PID $AppPid was replaced during $Context. " +
            "Expected '$expectedProcessPath', actual '$actualPath'."
    }
}

function Wait-AppSettled {
    param(
        [string]$Context,
        [int]$Milliseconds = 1200)

    $deadline = [DateTime]::UtcNow.AddMilliseconds($Milliseconds)
    do {
        Assert-AppAlive $Context
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
}

function Get-ControlValue {
    param([Parameter(Mandatory)][string]$AutomationId)

    Assert-AppAlive "reading $AutomationId"
    return (
        Invoke-WinApp ui get-value $AutomationId -a $AppPid --json |
            ConvertFrom-Json
    ).text
}

function Find-ComboItem {
    param([Parameter(Mandatory)][string]$ItemName)

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Assert-AppAlive "waiting for ComboBox item '$ItemName'"
        $search = Invoke-WinApp ui search $ItemName -a $AppPid --json |
            ConvertFrom-Json
        $item = $search.matches |
            Where-Object { $_.type -eq 'ListItem' } |
            Select-Object -First 1
        if ($null -ne $item) {
            return $item
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "ComboBox popup item was not found: $ItemName"
}

function Select-ComboItem {
    param(
        [Parameter(Mandatory)]
        [string]$ComboId,
        [Parameter(Mandatory)]
        [string]$ItemName)

    Assert-AppAlive "opening $ComboId"
    Invoke-WinApp ui invoke $ComboId -a $AppPid | Out-Null
    $item = Find-ComboItem $ItemName
    Invoke-WinApp ui invoke $item.selector -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for $ComboId -a $AppPid `
        --value $ItemName -t 5000 | Out-Null
    Wait-AppSettled "selecting $ItemName"
}

function Get-RawBackdropDiagnostic {
    Assert-AppAlive 'reading ActualBackdropDiagnostic'
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition =
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $AppPid)
    $window = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $processCondition)
    if ($null -eq $window) {
        throw "Window not found for PID $AppPid."
    }

    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $elements =
        [Collections.Generic.Queue[
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

function Assert-ActualBackdrop {
    param([Parameter(Mandatory)][string]$Backdrop)

    $expected = $expectedDiagnostics[$Backdrop]
    $actual = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $actual = Get-RawBackdropDiagnostic
        $isAcrylicDiagnostic = $Backdrop -eq 'Acrylic' -and
            $actual -match '^Acrylic\|TintOpacity=(0\.\d{2}|1\.00)' +
                '\|SolidSurface=Collapsed$'
        if ($actual -eq $expected -or $isAcrylicDiagnostic) {
            Add-StressEvent 'BackdropDiagnostic' "$Backdrop=$actual"
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Expected backdrop '$expected', actual '$actual'."
}

function Set-ThemeValue {
    param([Parameter(Mandatory)][string]$ThemeValue)

    if ((Get-ControlValue ThemeToggle) -ne $ThemeValue) {
        Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
    }

    Invoke-WinApp ui wait-for ThemeToggle -a $AppPid `
        --value $ThemeValue -t 5000 | Out-Null
    Wait-AppSettled "setting theme $ThemeValue"
}

function Invoke-StressStep {
    param(
        [Parameter(Mandatory)][int]$Cycle,
        [Parameter(Mandatory)][string]$Backdrop)

    $script:lastCycle = $Cycle
    $script:lastBackdrop = $Backdrop
    $currentTheme = Get-ControlValue ThemeToggle
    $nextTheme = if ($currentTheme -eq 'On') { 'Off' } else { 'On' }
    Set-ThemeValue $nextTheme
    Add-StressEvent 'ThemeChanged' "cycle=$Cycle; value=$nextTheme"

    Select-ComboItem BackdropSelector $Backdrop
    Assert-ActualBackdrop $Backdrop

    Invoke-WinApp ui invoke NavOverview -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for OverviewScrollViewer -a $AppPid `
        -t 5000 | Out-Null
    Wait-AppSettled "cycle $Cycle $Backdrop Overview"

    Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for BackdropSelector -a $AppPid `
        -t 5000 | Out-Null
    Wait-AppSettled "cycle $Cycle $Backdrop Settings"
    Assert-ActualBackdrop $Backdrop
    Add-StressEvent 'StepCompleted' "cycle=$Cycle; backdrop=$Backdrop"
}

function Restore-InitialAppearance {
    Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for BackdropSelector -a $AppPid `
        -t 5000 | Out-Null
    Set-ThemeValue $initialTheme
    Select-ComboItem BackdropSelector $initialBackdrop
    Assert-ActualBackdrop $initialBackdrop
    if ((Get-ControlValue ThemeToggle) -ne $initialTheme -or
        (Get-ControlValue BackdropSelector) -ne $initialBackdrop) {
        throw 'Initial appearance values were not restored.'
    }

    # 設定保存とXAML/DWM更新の完了後に遅れて発生するクラッシュも検出する。
    Wait-AppSettled 'verifying restored appearance' 15000
}

function Test-AppAliveForResult {
    param([Parameter(Mandatory)][string]$Context)

    if ($null -eq $expectedProcessPath) {
        return $false
    }

    try {
        Assert-AppAlive $Context
        return $true
    }
    catch {
        $script:isCrashObserved = $true
        if ($null -eq $script:testError) {
            $script:testError = $_
        }
        return $false
    }
}

function New-StressResult {
    param([Parameter(Mandatory)][bool]$ProcessAlive)

    $status = if ($isCrashObserved) {
        'RED_CRASH_REPRODUCED'
    } elseif ($null -eq $testError -and $null -eq $restoreError) {
        'PASS_NON_REPRODUCED'
    } else {
        'FAIL'
    }
    $hasInitialAppearance =
        $null -ne $initialTheme -and $null -ne $initialBackdrop
    $nextLaunchRestoreRequired =
        $hasInitialAppearance -and -not $isRestorationSucceeded
    $result = [ordered]@{
        runId = $runId
        startedAtUtc = $startedAtUtc.ToString('O')
        completedAtUtc = [DateTime]::UtcNow.ToString('O')
        status = $status
        appPid = $AppPid
        processPath = $expectedProcessPath
        processAlive = $ProcessAlive
        packageFamilyName = $packageFamilyName
        packageInstallLocation = $packageInstallLocation
        dataFilePath = $dataFilePath
        cycleCount = $CycleCount
        backdrops = $backdrops
        lastCycle = $lastCycle
        lastBackdrop = $lastBackdrop
        crashObserved = $isCrashObserved
        testError = if ($null -eq $testError) {
            $null
        } else {
            $testError.ToString()
        }
        restoration = [ordered]@{
            attempted = $isRestorationAttempted
            succeeded = $isRestorationSucceeded
            error = if ($null -eq $restoreError) {
                $null
            } else {
                $restoreError.ToString()
            }
            nextLaunchRestoreRequired = $nextLaunchRestoreRequired
            initialThemeToggleValue = $initialTheme
            initialThemeName = if ($initialTheme -eq 'On') {
                'Dark'
            } elseif ($initialTheme -eq 'Off') {
                'Light'
            } else {
                $null
            }
            initialBackdrop = $initialBackdrop
            guidance = if ($nextLaunchRestoreRequired) {
                '次回起動後、SettingsでinitialThemeNameと' +
                    'initialBackdropへ戻してください。dataFilePathを' +
                    '直接編集しないでください。'
            } elseif ($isRestorationSucceeded) {
                '初期テーマと背景へ復元済みです。'
            } else {
                '初期外観を取得する前に終了したため、' +
                    '復元対象はありません。'
            }
        }
        events = $events
    }

    return $result
}

function Publish-ResultAtomically {
    param([Parameter(Mandatory)][object]$Result)

    $resultDirectory = [IO.Path]::GetDirectoryName($ResultPath)
    New-Item -ItemType Directory -Force -Path $resultDirectory |
        Out-Null
    $temporaryResultPath = Join-Path $resultDirectory (
        '.{0}.{1}.tmp' -f
            [IO.Path]::GetFileName($ResultPath),
            [Guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText(
            $temporaryResultPath,
            ($Result | ConvertTo-Json -Depth 8),
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryResultPath, $ResultPath, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryResultPath)) {
            [IO.File]::Delete($temporaryResultPath)
        }
    }
}

function Write-Result {
    $processAlive = Test-AppAliveForResult 'writing result'
    Publish-ResultAtomically (New-StressResult $processAlive)

    if ($null -eq $expectedProcessPath) {
        return
    }

    $processAlive = Test-AppAliveForResult 'verifying published result'
    if ($processAlive) {
        return
    }

    Add-StressEvent 'PostPublishProcessCheck' (
        'FAIL: PID or executable path changed after result publication.')
    Publish-ResultAtomically (New-StressResult $false)
}

try {
    $inputProcess = Get-Process -Id $AppPid -ErrorAction Stop
    if ($inputProcess.ProcessName -ne 'StaminaManager') {
        throw "PID $AppPid is not StaminaManager."
    }

    $expectedProcessPath = [IO.Path]::GetFullPath($inputProcess.Path)
    $appxPath = Split-Path -Parent $expectedProcessPath
    $package = Get-AppxPackage | Where-Object {
        [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\') -eq
            [IO.Path]::GetFullPath($appxPath).TrimEnd('\')
    } | Select-Object -First 1
    if ($null -eq $package) {
        throw 'Registered debug package was not found.'
    }

    $packageFamilyName = $package.PackageFamilyName
    $packageInstallLocation = [IO.Path]::GetFullPath(
        $package.InstallLocation)
    $dataFilePath = Join-Path $env:LOCALAPPDATA (
        "Packages\$packageFamilyName\LocalState\Data\data.json")
    $mutexToken = $packageFamilyName -replace '[^A-Za-z0-9_.-]', '_'
    $executionMutexName = "Local\StaminaManager.UiTests.$mutexToken"
    $executionMutex = [Threading.Mutex]::new(
        $false,
        $executionMutexName)
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

    Invoke-WinApp ui wait-for NavSettings -a $AppPid -t 5000 |
        Out-Null
    Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for BackdropSelector -a $AppPid `
        -t 5000 | Out-Null
    $initialTheme = Get-ControlValue ThemeToggle
    $initialBackdrop = Get-ControlValue BackdropSelector
    Add-StressEvent 'InitialAppearance' (
        "theme=$initialTheme; backdrop=$initialBackdrop")

    for ($cycle = 1; $cycle -le $CycleCount; $cycle++) {
        foreach ($backdrop in $backdrops) {
            Invoke-StressStep $cycle $backdrop
        }
    }

    Wait-AppSettled 'final delayed-crash poll' 5000
}
catch {
    $testError = $_
}
finally {
    if ($null -ne $initialTheme -and $null -ne $initialBackdrop) {
        $isRestorationAttempted = $true
        try {
            Assert-AppAlive 'starting restoration'
            Restore-InitialAppearance
            $isRestorationSucceeded = $true
            Add-StressEvent 'Restoration' 'PASS'
        }
        catch {
            $restoreError = $_
            Add-StressEvent 'Restoration' "FAIL: $restoreError"
        }
    }

    try {
        Write-Result
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

if ($null -ne $restoreError) {
    throw "Appearance restoration failed: $restoreError"
}

if ($null -ne $testError) {
    throw $testError
}

Write-Host 'Appearance navigation stress: PASS (今回の環境では非再現)'
Write-Host "Result: $ResultPath"
