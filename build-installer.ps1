param(
    [string]$InnoSetupCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    [string]$MakeAppx = "",
    [switch]$SkipExe,
    [switch]$SkipMsix,
    [switch]$DebugOnly
)

$ErrorActionPreference = "Stop"

$root       = $PSScriptRoot
$projectDir = Join-Path $root "WindowBouncer.WinUI"
$projectPath = Join-Path $projectDir "WindowBouncer.WinUI.csproj"
$publishDir = Join-Path $root "publish"

# Read version from the single source of truth: <Version> in WindowBouncer.WinUI.csproj
[xml]$csproj = Get-Content $projectPath
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw "Could not read <Version> from $projectPath" }
Write-Host "Version: $version"

New-Item -ItemType Directory -Force $publishDir | Out-Null


# DEBUG BUILD

if ($DebugOnly) {
    Write-Host "Building debug executable..."
    dotnet build $projectPath -c Debug -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
    Write-Host ""
    Write-Host "Done. Debug output in: $projectDir\bin\x64\Debug"
    exit 0
}


# EXE INSTALLER

if (-not $SkipExe) {
    Write-Host "Publishing self-contained WinUI 3 build..."
    dotnet publish $projectPath -c Release -p:PublishProfile=Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    if (Test-Path $InnoSetupCompiler) {
        Write-Host "Building EXE installer..."
        & $InnoSetupCompiler /DAppVersion=$version "$root\installer\WindowBouncer.iss"
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
    } else {
        Write-Warning "Inno Setup not found at '$InnoSetupCompiler'. Skipping EXE installer."
        Write-Warning "Install Inno Setup 6 or pass -InnoSetupCompiler to override the path."
    }
}


# MSIX

if (-not $SkipMsix) {
    if (-not $MakeAppx) {
        $sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin"
        if (Test-Path $sdkBin) {
            $MakeAppx = Get-ChildItem -Path $sdkBin -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*x64*" } |
                Sort-Object FullName -Descending |
                Select-Object -First 1 -ExpandProperty FullName
        }
    }

    if (-not $MakeAppx -or -not (Test-Path $MakeAppx)) {
        Write-Warning "makeappx.exe not found. Install the Windows SDK or pass -MakeAppx to override the path. Skipping MSIX."
    } else {
        Write-Host "Publishing (MSIX staging)..."
        dotnet publish $projectPath -c Release -p:PublishProfile=ReleaseMsix -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish (MSIX) failed" }

        Write-Host "Generating Store assets from appicon.ico..."
        $stagingDir = Join-Path $projectDir "bin\msix-staging"
        $assetsDir  = Join-Path $stagingDir "Assets"
        if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
        New-Item -ItemType Directory -Force $stagingDir | Out-Null
        New-Item -ItemType Directory -Force $assetsDir  | Out-Null

        $msixPublishDir = Join-Path $projectDir "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish-msix"
        Copy-Item "$msixPublishDir\*" $stagingDir -Recurse -Force

        $msixVersion = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
        $manifest = Get-Content "$root\msix\AppxManifest.xml" -Raw
        $manifest = $manifest -creplace 'Version="[\d.]+"', "Version=""$msixVersion"""
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText("$stagingDir\AppxManifest.xml", $manifest, $utf8NoBom)

        $icoPath = Join-Path $projectDir "Resources\appicon.ico"
        Add-Type -AssemblyName System.Drawing

        function Save-Asset {
            param([int]$W, [int]$H, [string]$Out)
            $bmp = New-Object System.Drawing.Bitmap($W, $H)
            $g   = [System.Drawing.Graphics]::FromImage($bmp)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.Clear([System.Drawing.Color]::Transparent)
            try {
                $icon = New-Object System.Drawing.Icon($icoPath, $W, $H)
                $g.DrawIcon($icon, 0, 0)
                $icon.Dispose()
            } catch {
                $icon   = [System.Drawing.Icon]::ExtractAssociatedIcon($icoPath)
                $srcBmp = $icon.ToBitmap()
                $g.DrawImage($srcBmp, 0, 0, $W, $H)
                $srcBmp.Dispose()
                $icon.Dispose()
            }
            $g.Dispose()
            $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
        }

        function Save-CenteredAsset {
            param([int]$IconSize, [int]$W, [int]$H, [string]$Out)
            $iconBmp = New-Object System.Drawing.Bitmap($IconSize, $IconSize)
            $ig      = [System.Drawing.Graphics]::FromImage($iconBmp)
            $ig.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            try {
                $icon = New-Object System.Drawing.Icon($icoPath, $IconSize, $IconSize)
                $ig.DrawIcon($icon, 0, 0)
                $icon.Dispose()
            } catch {
                $icon   = [System.Drawing.Icon]::ExtractAssociatedIcon($icoPath)
                $srcBmp = $icon.ToBitmap()
                $ig.DrawImage($srcBmp, 0, 0, $IconSize, $IconSize)
                $srcBmp.Dispose()
                $icon.Dispose()
            }
            $ig.Dispose()

            $bmp = New-Object System.Drawing.Bitmap($W, $H)
            $g   = [System.Drawing.Graphics]::FromImage($bmp)
            $g.Clear([System.Drawing.Color]::Transparent)
            $x = [int](($W - $IconSize) / 2)
            $y = [int](($H - $IconSize) / 2)
            $g.DrawImage($iconBmp, $x, $y, $IconSize, $IconSize)
            $iconBmp.Dispose()
            $g.Dispose()
            $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
        }

        Save-Asset        44  44  "$assetsDir\Square44x44Logo.png"
        Save-Asset        150 150 "$assetsDir\Square150x150Logo.png"
        Save-Asset        50  50  "$assetsDir\StoreLogo.png"
        Save-CenteredAsset 100 310 150 "$assetsDir\Wide310x150Logo.png"
        Save-CenteredAsset 200 620 300 "$assetsDir\SplashScreen.png"

        Write-Host "Packaging MSIX..."
        $msixOut = Join-Path $publishDir "WindowBouncer-$version.msix"
        & $MakeAppx pack /d $stagingDir /p $msixOut /overwrite
        if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

        Write-Host "MSIX: $msixOut"
    }
}

Write-Host ""
Write-Host "Done. Output in: $publishDir"
