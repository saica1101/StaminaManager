[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$assetsDirectory = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $PSScriptRoot 'AppIconSource.svg'
$edgeCandidates = @(
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe')
)
$edgePath = $edgeCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($edgePath)) {
    throw 'Microsoft Edgeが見つかりません。ブランド資産を再生成できません。'
}

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "SVG正本が見つかりません: $sourcePath"
}

$temporaryDirectory = Join-Path (
    [System.IO.Path]::GetTempPath()
) ("stamina-brand-{0}" -f [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

function New-ThemeSource {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Default', 'Dark', 'Light')]
        [string] $Theme
    )

    [xml] $document = Get-Content -LiteralPath $sourcePath -Raw
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new(
        $document.NameTable
    )
    $namespaceManager.AddNamespace('svg', 'http://www.w3.org/2000/svg')

    $plate = $document.SelectSingleNode(
        "//svg:rect[@id='plate']",
        $namespaceManager
    )
    $arc = $document.SelectSingleNode(
        "//svg:path[@id='stamina-arc']",
        $namespaceManager
    )
    $tick = $document.SelectSingleNode(
        "//svg:path[@id='center-tick']",
        $namespaceManager
    )

    if ($null -eq $plate -or $null -eq $arc -or $null -eq $tick) {
        throw 'SVG正本に必要なplate、stamina-arc、center-tickがありません。'
    }

    if ($Theme -ne 'Default') {
        $null = $plate.ParentNode.RemoveChild($plate)
    }

    if ($Theme -eq 'Dark') {
        $arc.SetAttribute('stroke', '#6CC7FF')
        $tick.SetAttribute('stroke', '#FFFFFF')
    }
    elseif ($Theme -eq 'Light') {
        $arc.SetAttribute('stroke', '#005A9E')
        $tick.SetAttribute('stroke', '#142638')
    }

    $themePath = Join-Path $temporaryDirectory "$Theme.svg"
    $document.Save($themePath)
    return $themePath
}

function Convert-SvgToBitmap {
    param(
        [Parameter(Mandatory)]
        [string] $SvgPath,

        [Parameter(Mandatory)]
        [string] $OutputPath
    )

    $uri = [System.Uri]::new($SvgPath).AbsoluteUri
    $arguments = @(
        '--headless=new'
        '--disable-gpu'
        '--hide-scrollbars'
        '--default-background-color=00000000'
        '--window-size=1024,1024'
        "--screenshot=$OutputPath"
        $uri
    )
    $process = Start-Process `
        -FilePath $edgePath `
        -ArgumentList $arguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden

    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $OutputPath)) {
        throw "SVGのPNG変換に失敗しました: $SvgPath"
    }
}

function Save-ScaledPng {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image] $Source,

        [Parameter(Mandatory)]
        [int] $Width,

        [Parameter(Mandatory)]
        [int] $Height,

        [Parameter(Mandatory)]
        [string] $OutputPath,

        [double] $ContentRatio = 1.0
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode =
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality =
                [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode =
                [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode =
                [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

            $side = [Math]::Max(
                1,
                [Math]::Round(
                    [Math]::Min($Width, $Height) * $ContentRatio,
                    [MidpointRounding]::AwayFromZero
                )
            )
            $left = [Math]::Floor(($Width - $side) / 2)
            $top = [Math]::Floor(($Height - $side) / 2)
            $destination = [System.Drawing.Rectangle]::new(
                $left,
                $top,
                $side,
                $side
            )
            $graphics.DrawImage($Source, $destination)
        }
        finally {
            $graphics.Dispose()
        }

        $bitmap.Save(
            $OutputPath,
            [System.Drawing.Imaging.ImageFormat]::Png
        )
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-ScaleSet {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image] $Source,

        [Parameter(Mandatory)]
        [string] $BaseName,

        [Parameter(Mandatory)]
        [int] $BaseWidth,

        [Parameter(Mandatory)]
        [int] $BaseHeight,

        [double] $ContentRatio = 1.0
    )

    $scaleFactors = [ordered]@{
        100 = 1.0
        125 = 1.25
        150 = 1.5
        200 = 2.0
        400 = 4.0
    }

    foreach ($scale in $scaleFactors.GetEnumerator()) {
        $width = [Math]::Round(
            $BaseWidth * $scale.Value,
            [MidpointRounding]::AwayFromZero
        )
        $height = [Math]::Round(
            $BaseHeight * $scale.Value,
            [MidpointRounding]::AwayFromZero
        )
        $outputPath = Join-Path $assetsDirectory (
            '{0}.scale-{1}.png' -f $BaseName, $scale.Key
        )
        Save-ScaledPng `
            -Source $Source `
            -Width $width `
            -Height $height `
            -OutputPath $outputPath `
            -ContentRatio $ContentRatio
    }
}

function Save-Ico {
    param(
        [Parameter(Mandatory)]
        [string[]] $PngPaths,

        [Parameter(Mandatory)]
        [string] $OutputPath
    )

    $images = @($PngPaths | ForEach-Object {
        Write-Output -NoEnumerate ([System.IO.File]::ReadAllBytes($_))
    })
    $stream = [System.IO.File]::Create($OutputPath)
    try {
        $writer = [System.IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16] 0)
            $writer.Write([uint16] 1)
            $writer.Write([uint16] $images.Count)

            $offset = 6 + (16 * $images.Count)
            for ($index = 0; $index -lt $images.Count; $index++) {
                $size = [int] (
                    [System.IO.Path]::GetFileName($PngPaths[$index]) -replace
                        '^.*targetsize-(\d+).*$','$1'
                )
                $dimension = if ($size -eq 256) { 0 } else { $size }
                $writer.Write([byte] $dimension)
                $writer.Write([byte] $dimension)
                $writer.Write([byte] 0)
                $writer.Write([byte] 0)
                $writer.Write([uint16] 1)
                $writer.Write([uint16] 32)
                $writer.Write([uint32] $images[$index].Length)
                $writer.Write([uint32] $offset)
                $offset += $images[$index].Length
            }

            foreach ($image in $images) {
                $writer.Write($image)
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

try {
    $renderedPaths = @{}
    foreach ($theme in @('Default', 'Dark', 'Light')) {
        $themeSourcePath = New-ThemeSource -Theme $theme
        $renderedPath = Join-Path $temporaryDirectory "$theme.png"
        Convert-SvgToBitmap `
            -SvgPath $themeSourcePath `
            -OutputPath $renderedPath
        $renderedPaths[$theme] = $renderedPath
    }

    $defaultImage = [System.Drawing.Image]::FromFile($renderedPaths.Default)
    $darkImage = [System.Drawing.Image]::FromFile($renderedPaths.Dark)
    $lightImage = [System.Drawing.Image]::FromFile($renderedPaths.Light)
    try {
        Save-ScaleSet `
            -Source $darkImage `
            -BaseName 'Square150x150Logo' `
            -BaseWidth 150 `
            -BaseHeight 150 `
            -ContentRatio 0.78
        Save-ScaleSet `
            -Source $darkImage `
            -BaseName 'Wide310x150Logo' `
            -BaseWidth 310 `
            -BaseHeight 150 `
            -ContentRatio 0.78
        Save-ScaleSet `
            -Source $darkImage `
            -BaseName 'SplashScreen' `
            -BaseWidth 620 `
            -BaseHeight 300 `
            -ContentRatio 0.46
        Save-ScaleSet `
            -Source $defaultImage `
            -BaseName 'Square44x44Logo' `
            -BaseWidth 44 `
            -BaseHeight 44
        Save-ScaleSet `
            -Source $defaultImage `
            -BaseName 'StoreLogo' `
            -BaseWidth 50 `
            -BaseHeight 50

        $targetSizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
        foreach ($size in $targetSizes) {
            $defaultPath = Join-Path $assetsDirectory (
                "Square44x44Logo.targetsize-$size.png"
            )
            $darkPath = Join-Path $assetsDirectory (
                "Square44x44Logo.targetsize-${size}_altform-unplated.png"
            )
            $lightPath = Join-Path $assetsDirectory (
                "Square44x44Logo.targetsize-${size}_altform-lightunplated.png"
            )
            Save-ScaledPng `
                -Source $defaultImage `
                -Width $size `
                -Height $size `
                -OutputPath $defaultPath
            Save-ScaledPng `
                -Source $darkImage `
                -Width $size `
                -Height $size `
                -OutputPath $darkPath
            Save-ScaledPng `
                -Source $lightImage `
                -Width $size `
                -Height $size `
                -OutputPath $lightPath
        }

        Save-ScaledPng `
            -Source $defaultImage `
            -Width 50 `
            -Height 50 `
            -OutputPath (Join-Path $assetsDirectory 'StoreLogo.png')
        Save-ScaledPng `
            -Source $darkImage `
            -Width 48 `
            -Height 48 `
            -OutputPath (
                Join-Path $assetsDirectory 'LockScreenLogo.scale-200.png'
            )

        $icoPngPaths = @(16, 24, 32, 48, 256) | ForEach-Object {
            Join-Path $assetsDirectory (
                "Square44x44Logo.targetsize-$_.png"
            )
        }
        Save-Ico `
            -PngPaths $icoPngPaths `
            -OutputPath (Join-Path $assetsDirectory 'AppIcon.ico')
    }
    finally {
        $defaultImage.Dispose()
        $darkImage.Dispose()
        $lightImage.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

Write-Output 'ブランド資産を再生成しました。'
