$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot `
    'task11-notification-integration.ps1'
$source = [IO.File]::ReadAllText($scriptPath)
$requiredSource = [ordered]@{
    '通常UIと共通のmutex名' =
        'Local\StaminaManager.UiTests.$mutexToken'
    'Abandoned mutexの失敗' =
        'catch [Threading.AbandonedMutexException]'
    'mutexの最外finally解放' = '$executionMutex.ReleaseMutex()'
    'toastのTag' = 'Tag'
    'toastのGroup' = 'Group'
    'toastの配信時刻' = 'DeliveryTimeUtcTicks'
    '1秒以内の時刻許容差' =
        '[TimeSpan]::FromSeconds(1).Ticks'
    'ledgerとtoastの純粋照合' =
        'Test-NotificationStateSnapshot'
    '復元後の意味的整合性検証' =
        'Assert-RestoredNotificationConsistency'
    '純粋helper契約モード' = 'ContractOnly'
}

foreach ($contract in $requiredSource.GetEnumerator()) {
    if (-not $source.Contains(
            [string]$contract.Value,
            [StringComparison]::Ordinal)) {
        throw "task11 source contract missing: $($contract.Key)"
    }
}

$ledgerRestoreCall = [regex]::Escape(
    'Restore-OptionalFile $ledgerPath $ledgerDidExist $ledgerOriginal')
$ledgerRestoreCount = [regex]::Matches(
    $source,
    $ledgerRestoreCall).Count
if ($ledgerRestoreCount -ne 1) {
    throw "Original ledger must be restored exactly once; actual " +
        "$ledgerRestoreCount."
}

$output = & pwsh -NoProfile -File $scriptPath -ContractOnly 2>&1
if ($LASTEXITCODE -ne 0) {
    throw ($output -join [Environment]::NewLine)
}
if (($output -join [Environment]::NewLine) -notmatch
    'Task11 notification contract: PASS') {
    throw 'Pure notification-state contract did not report PASS.'
}

Write-Host 'Task11 notification source contract: PASS'
