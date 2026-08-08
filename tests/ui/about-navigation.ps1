param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'

function Invoke-WinApp {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & winapp ui @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output | Out-String)
    }

    return $output
}

Invoke-WinApp @('wait-for', 'NavAbout', '-a', "$AppPid", '-t', '3000') |
    Out-Null
Invoke-WinApp @('invoke', 'NavAbout', '-a', "$AppPid") | Out-Null
Invoke-WinApp @('wait-for', 'AboutPageRoot', '-a', "$AppPid", '-t', '3000') |
    Out-Null
Invoke-WinApp @('wait-for', 'OpenGitHubButton', '-a', "$AppPid", '-t', '3000') |
    Out-Null
Invoke-WinApp @('wait-for', 'OpenReadmeButton', '-a', "$AppPid", '-t', '3000') |
    Out-Null
Invoke-WinApp @('wait-for', 'AboutReadmeHeading', '-a', "$AppPid", '-t', '3000') |
    Out-Null
Invoke-WinApp @('wait-for', 'AboutReadmeDescription', '-a', "$AppPid", '-t', '3000') |
    Out-Null
Invoke-WinApp @('wait-for', 'VersionFooterText', '-a', "$AppPid", '-t', '3000') |
    Out-Null

Write-Host 'About navigation and link controls are present.'
