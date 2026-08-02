[CmdletBinding(DefaultParameterSetName = 'Build')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Build')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Validate')]
    [string]$Version,

    [Parameter(Mandatory = $true, ParameterSetName = 'Validate')]
    [switch]$ValidateOnly,

    [Parameter(Mandatory = $true, ParameterSetName = 'Validate')]
    [string]$UploadPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedPackageName = 'saica1101.StaminaManager'
$expectedPublisher = 'CN=E42D0651-60BF-47A1-BD3B-ECCF464087D2'
$expectedPublisherDisplayName = 'saica1101'
$expectedMinVersion = '10.0.22000.0'
$expectedExecutable = 'StaminaManager.exe'
$maxNestedPackageBytes = 128MB
$appxBlockSizeBytes = 64KB
$expectedBlockMapHashMethod = `
    'http://www.w3.org/2001/04/xmlenc#sha256'

function Test-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $components = @($Value.Split('.'))
    if ($components.Count -ne 4) {
        return $false
    }

    foreach ($component in $components) {
        if ($component -notmatch '^\d+$') {
            return $false
        }

        [uint32]$numericComponent = 0
        if (-not [uint32]::TryParse(
            $component,
            [ref]$numericComponent) -or
            $numericComponent -gt [uint16]::MaxValue) {
            return $false
        }
    }

    return [uint32]$components[0] -ge 1 -and
        [uint32]$components[3] -eq 0
}

function Assert-AppxBlockMap {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$PackageArchive,

        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$BlockMapEntry
    )

    $blockMapStream = $BlockMapEntry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($blockMapStream)
        try {
            [xml]$blockMap = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $blockMapStream.Dispose()
    }

    $hashMethod = [string]$blockMap.BlockMap.HashMethod
    if ($hashMethod -cne $expectedBlockMapHashMethod) {
        throw "AppxBlockMap.xml HashMethod must be SHA2-256: $hashMethod"
    }

    $mappedPaths = `
        [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($mappedFile in @($blockMap.BlockMap.File)) {
        $mappedPath = ([string]$mappedFile.Name) -replace '\\', '/'
        if ([string]::IsNullOrWhiteSpace($mappedPath) -or
            -not $mappedPaths.Add($mappedPath)) {
            throw "AppxBlockMap.xml contains an invalid or duplicate file path: $mappedPath"
        }

        $matchingEntries = @($PackageArchive.Entries | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.Name) -and
            $_.FullName -ieq $mappedPath
        })
        if ($matchingEntries.Count -ne 1) {
            throw "Mapped file is missing from nested MSIX: $mappedPath"
        }

        [long]$mappedSize = 0
        if (-not [long]::TryParse(
            [string]$mappedFile.Size,
            [ref]$mappedSize) -or
            $mappedSize -lt 0 -or
            $matchingEntries[0].Length -ne $mappedSize) {
            throw "Mapped file size does not match for '$mappedPath'."
        }

        $mappedBlocks = @($mappedFile.Block)
        $expectedBlockCount = [int][Math]::Ceiling(
            [double]$mappedSize / $appxBlockSizeBytes)
        if ($mappedBlocks.Count -ne $expectedBlockCount) {
            throw "Block count does not match for '$mappedPath'."
        }

        $mappedEntryStream = $matchingEntries[0].Open()
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            [long]$hashedBytes = 0
            $blockBuffer = New-Object byte[] $appxBlockSizeBytes
            for ($blockIndex = 0;
                $blockIndex -lt $mappedBlocks.Count;
                $blockIndex++) {
                $blockBytes = 0
                while ($blockBytes -lt $appxBlockSizeBytes) {
                    $bytesRead = $mappedEntryStream.Read(
                        $blockBuffer,
                        $blockBytes,
                        $appxBlockSizeBytes - $blockBytes)
                    if ($bytesRead -eq 0) {
                        break
                    }

                    $blockBytes += $bytesRead
                }

                if ($blockBytes -eq 0) {
                    throw "Mapped file ended before block $blockIndex for '$mappedPath'."
                }

                $actualHash = [Convert]::ToBase64String(
                    $sha256.ComputeHash($blockBuffer, 0, $blockBytes))
                if ($actualHash -cne [string]$mappedBlocks[$blockIndex].Hash) {
                    throw "Block hash does not match for '$mappedPath' block $blockIndex."
                }

                $hashedBytes += $blockBytes
            }

            if ($hashedBytes -ne $mappedSize -or
                $mappedEntryStream.ReadByte() -ne -1) {
                throw "Mapped file length does not match for '$mappedPath'."
            }
        }
        finally {
            $sha256.Dispose()
            $mappedEntryStream.Dispose()
        }
    }

    $unmappedPayloadEntries = @($PackageArchive.Entries | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.Name) -and
        $_.FullName -ine 'AppxBlockMap.xml' -and
        $_.FullName -ine '[Content_Types].xml' -and
        $_.FullName -ine 'AppxSignature.p7x' -and
        -not $mappedPaths.Contains($_.FullName)
    })
    if ($unmappedPayloadEntries.Count -ne 0) {
        throw "Nested MSIX contains a file missing from AppxBlockMap.xml: $($unmappedPayloadEntries[0].FullName)"
    }
}

function Find-MSBuild {
    $vswhereCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} `
            'Microsoft Visual Studio\Installer\vswhere.exe'),
        (Join-Path $env:ProgramFiles `
            'Microsoft Visual Studio\Installer\vswhere.exe')
    )

    foreach ($vswherePath in $vswhereCandidates) {
        if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
            continue
        }

        $msbuildPath = & $vswherePath `
            -latest `
            -products '*' `
            -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($msbuildPath)) {
            return $msbuildPath
        }
    }

    return $null
}

function Assert-StoreUpload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UploadPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedVersion
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $absoluteUploadPath = [System.IO.Path]::GetFullPath($UploadPath)
    if (-not (Test-Path -LiteralPath $absoluteUploadPath -PathType Leaf)) {
        throw "Store upload does not exist: $absoluteUploadPath"
    }

    $validationTempRoot = $null
    try {
        $uploadArchive = [System.IO.Compression.ZipFile]::OpenRead(
            $absoluteUploadPath)
        try {
            $uploadEntries = @($uploadArchive.Entries)
            if ($uploadEntries.Count -ne 2) {
                throw "Store upload must contain exactly two root entries, found $($uploadEntries.Count)."
            }

            $nestedEntries = @($uploadEntries | Where-Object {
                $_.FullName -cne $_.Name -or
                [string]::IsNullOrWhiteSpace($_.Name)
            })
            if ($nestedEntries.Count -ne 0) {
                throw 'Store upload must contain exactly two root entries.'
            }

            $packageEntries = @($uploadEntries | Where-Object {
                [System.IO.Path]::GetExtension($_.Name) -ieq '.msix'
            })
            $symbolEntries = @($uploadEntries | Where-Object {
                [System.IO.Path]::GetExtension($_.Name) -ieq '.appxsym'
            })
            if ($packageEntries.Count -ne 1 -or
                $symbolEntries.Count -ne 1) {
                throw 'Store upload must contain exactly one MSIX and one appxsym.'
            }

            if ($packageEntries[0].Length -gt $maxNestedPackageBytes) {
                throw "Store upload nested MSIX exceeds the $maxNestedPackageBytes byte limit."
            }

            $validationTempRoot = Join-Path `
                ([System.IO.Path]::GetTempPath()) `
                ('StaminaManager.StoreUploadValidation-{0}' -f `
                    [Guid]::NewGuid().ToString('N'))
            $null = New-Item `
                -ItemType Directory `
                -Path $validationTempRoot
            $tempPackagePath = Join-Path `
                $validationTempRoot `
                $packageEntries[0].Name

            $entryStream = $packageEntries[0].Open()
            try {
                $tempPackageStream = [System.IO.File]::Open(
                    $tempPackagePath,
                    [System.IO.FileMode]::CreateNew,
                    [System.IO.FileAccess]::Write,
                    [System.IO.FileShare]::None)
                try {
                    $copyBuffer = New-Object byte[] 81920
                    [long]$copiedBytes = 0
                    while (($bytesRead = $entryStream.Read(
                        $copyBuffer,
                        0,
                        $copyBuffer.Length)) -gt 0) {
                        $copiedBytes += $bytesRead
                        if ($copiedBytes -gt $maxNestedPackageBytes) {
                            throw "Store upload nested MSIX exceeds the $maxNestedPackageBytes byte limit."
                        }

                        $tempPackageStream.Write(
                            $copyBuffer,
                            0,
                            $bytesRead)
                    }

                    if ($copiedBytes -ne $packageEntries[0].Length) {
                        throw 'Nested MSIX copied byte count does not match its declared ZIP entry length.'
                    }
                }
                finally {
                    $tempPackageStream.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }
        finally {
            $uploadArchive.Dispose()
        }

        $packageArchive = [System.IO.Compression.ZipFile]::OpenRead(
            $tempPackagePath)
        try {
            $signatureEntries = @($packageArchive.Entries | Where-Object {
                $_.Name -ieq 'AppxSignature.p7x'
            })
            if ($signatureEntries.Count -ne 0) {
                throw 'Nested MSIX must not contain AppxSignature.p7x.'
            }

            $blockMapEntries = @($packageArchive.Entries | Where-Object {
                $_.FullName -ieq 'AppxBlockMap.xml'
            })
            if ($blockMapEntries.Count -eq 0) {
                throw 'AppxBlockMap.xml is missing from nested MSIX.'
            }
            if ($blockMapEntries.Count -ne 1) {
                throw "Expected one AppxBlockMap.xml in MSIX, found $($blockMapEntries.Count)."
            }

            $contentTypeEntries = @($packageArchive.Entries | Where-Object {
                $_.FullName -ieq '[Content_Types].xml'
            })
            if ($contentTypeEntries.Count -eq 0) {
                throw '[Content_Types].xml is missing from nested MSIX.'
            }
            if ($contentTypeEntries.Count -ne 1) {
                throw "Expected one [Content_Types].xml in MSIX, found $($contentTypeEntries.Count)."
            }

            Assert-AppxBlockMap `
                -PackageArchive $packageArchive `
                -BlockMapEntry $blockMapEntries[0]

            $executableEntries = @($packageArchive.Entries | Where-Object {
                $_.FullName -ieq $expectedExecutable
            })
            if ($executableEntries.Count -ne 1) {
                throw "$expectedExecutable is missing from nested MSIX."
            }

            $manifestEntries = @($packageArchive.Entries | Where-Object {
                $_.FullName -ieq 'AppxManifest.xml'
            })
            if ($manifestEntries.Count -ne 1) {
                throw "Expected one AppxManifest.xml in MSIX, found $($manifestEntries.Count)."
            }

            $manifestStream = $manifestEntries[0].Open()
            try {
                $reader = [System.IO.StreamReader]::new($manifestStream)
                try {
                    [xml]$packageManifest = $reader.ReadToEnd()
                }
                finally {
                    $reader.Dispose()
                }
            }
            finally {
                $manifestStream.Dispose()
            }
        }
        finally {
            $packageArchive.Dispose()
        }

        $identity = $packageManifest.Package.Identity
        if ([string]$identity.Name -cne $expectedPackageName) {
            throw "Generated package Name does not match: $($identity.Name)"
        }

        if ([string]$identity.Publisher -cne $expectedPublisher) {
            throw "Generated package Publisher does not match: $($identity.Publisher)"
        }

        if ([string]$identity.Version -cne $ExpectedVersion) {
            throw "Generated package Version does not match: $($identity.Version)"
        }

        if ([string]$identity.ProcessorArchitecture -cne 'x64') {
            throw "Generated package architecture is not x64: $($identity.ProcessorArchitecture)"
        }

        $publisherDisplayName = [string]`
            $packageManifest.Package.Properties.PublisherDisplayName
        if ($publisherDisplayName -cne $expectedPublisherDisplayName) {
            throw "Generated package PublisherDisplayName does not match: $publisherDisplayName"
        }

        $deviceFamilies = @(
            $packageManifest.Package.Dependencies.TargetDeviceFamily)
        $desktopFamilies = @($deviceFamilies | Where-Object {
            [string]$_.Name -ceq 'Windows.Desktop'
        })
        if ($deviceFamilies.Count -ne 1 -or
            $desktopFamilies.Count -ne 1) {
            throw 'Generated package must target exactly one Windows.Desktop device family.'
        }

        if ([string]$desktopFamilies[0].MinVersion -cne `
                $expectedMinVersion) {
            $actualMinVersion = [string]$desktopFamilies[0].MinVersion
            throw "Windows.Desktop MinVersion does not match: $actualMinVersion"
        }

        $namespaceManager = [System.Xml.XmlNamespaceManager]::new(
            $packageManifest.NameTable)
        $namespaceManager.AddNamespace(
            'f',
            $packageManifest.DocumentElement.NamespaceURI)
        $desktopNamespace = `
            $packageManifest.DocumentElement.GetNamespaceOfPrefix('desktop')
        if ([string]::IsNullOrWhiteSpace($desktopNamespace)) {
            throw 'desktop manifest namespace is missing.'
        }

        $namespaceManager.AddNamespace('desktop', $desktopNamespace)
        $startupTasks = @($packageManifest.SelectNodes(
            '/f:Package/f:Applications/f:Application/f:Extensions/' +
                'desktop:Extension[@Category="windows.startupTask"]',
            $namespaceManager))
        if ($startupTasks.Count -ne 1 -or
            [string]$startupTasks[0].Executable -cne $expectedExecutable) {
            $actualExecutable = @($startupTasks | ForEach-Object {
                [string]$_.Executable
            }) -join ', '
            throw "StartupTask Executable does not match: $actualExecutable"
        }

        return $absoluteUploadPath
    }
    finally {
        if ($null -ne $validationTempRoot -and
            (Test-Path -LiteralPath $validationTempRoot)) {
            Remove-Item `
                -LiteralPath $validationTempRoot `
                -Recurse `
                -Force
        }
    }
}

if (-not (Test-PackageVersion -Value $Version)) {
    throw 'Store Version must have Major 1..65535, Minor and Build 0..65535, and Revision 0 (example: 1.0.0.0).'
}

if ($PSCmdlet.ParameterSetName -eq 'Validate') {
    $validatedUploadPath = Assert-StoreUpload `
        -UploadPath $UploadPath `
        -ExpectedVersion $Version
    Write-Output "Store upload: $validatedUploadPath"
    return
}

$projectPath = Join-Path $PSScriptRoot `
    'StaminaManager\StaminaManager.csproj'
$sourceManifestPath = Join-Path $PSScriptRoot `
    'StaminaManager\Package.appxmanifest'

[xml]$sourceManifest = Get-Content `
    -Raw `
    -LiteralPath $sourceManifestPath
$sourceVersion = [string]$sourceManifest.Package.Identity.Version
if ($sourceVersion -cne $Version) {
    throw "Version '$Version' does not match manifest Version '$sourceVersion'. Update Package.appxmanifest first."
}

$runId = '{0}-{1}' -f `
    [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), `
    $PID
$packageOutputDirectory = Join-Path $PSScriptRoot `
    "artifacts\store\$Version\$runId"
$isStorePackageReady = $false
try {
    $null = New-Item `
        -ItemType Directory `
        -Path $packageOutputDirectory `
        -Force

    $commonBuildArguments = @(
        '/restore',
        '/t:Rebuild',
        '/p:Configuration=Release',
        '/p:Platform=x64',
        '/p:RuntimeIdentifier=win-x64',
        '/p:GenerateAppxPackageOnBuild=true',
        '/p:AppxPackageSigningEnabled=false',
        '/p:UapAppxPackageBuildMode=StoreOnly',
        '/p:AppxBundle=Never',
        "/p:AppxPackageDir=$packageOutputDirectory\"
    )

    $msbuildPath = Find-MSBuild
    if ($null -ne $msbuildPath) {
        & $msbuildPath $projectPath @commonBuildArguments
    }
    else {
        $dotnet = Get-Command dotnet -ErrorAction Stop
        & $dotnet.Source msbuild $projectPath @commonBuildArguments
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Store package build failed with exit code $LASTEXITCODE."
    }

    $uploads = @(Get-ChildItem `
        -LiteralPath $packageOutputDirectory `
        -Filter '*.msixupload' `
        -File `
        -Recurse)
    if ($uploads.Count -ne 1) {
        throw "Expected one generated msixupload, found $($uploads.Count)."
    }

    $validatedUploadPath = Assert-StoreUpload `
        -UploadPath $uploads[0].FullName `
        -ExpectedVersion $Version
    $isStorePackageReady = $true
    Write-Output "Store upload: $validatedUploadPath"
}
finally {
    if (-not $isStorePackageReady -and
        (Test-Path -LiteralPath $packageOutputDirectory)) {
        Remove-Item `
            -LiteralPath $packageOutputDirectory `
            -Recurse `
            -Force
    }
}
