<#
.SYNOPSIS
    Tests and builds Ringside for Windows, Linux and macOS from one Windows machine.

.EXAMPLE
    ./build.ps1                                  # test, then build every platform
    ./build.ps1 -Version 2.0.0 -Pack             # plus installers and update packages
    ./build.ps1 -Version 2.0.0 -Target win -Pack # one platform

.NOTES
    Needs the .NET 8 SDK, and Velopack's CLI for -Pack (dotnet tool install -g vpk).
    Windows: the status line bridge is compiled to native code, which needs Visual Studio's
    "Desktop development with C++" workload.
    Linux: packed here into an AppImage (vpk [linux] pack).
    macOS: built here into an unsigned Ringside.app plus a stage archive that keeps file
    modes; it is signed, notarized and zipped on a Mac with packaging/macos/sign-and-notarize.sh.
#>
param(
    [string]$Version = '2.0.0',
    [ValidateSet('all', 'win', 'linux', 'mac')][string]$Target = 'all',
    [switch]$Pack,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'artifacts'
$releases = Join-Path $out 'releases'
$vpkCmd = Get-Command vpk -ErrorAction SilentlyContinue
$vpk = if ($vpkCmd) { $vpkCmd.Source } else { Join-Path $env:USERPROFILE '.dotnet\tools\vpk.exe' }

function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE) { throw "$exe failed with exit code $LASTEXITCODE" }
}

function Publish([string]$rid, [string]$dir) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force -LiteralPath $dir }
    Run dotnet @('publish', 'src/Ringside', '-c', 'Release', '-r', $rid, '--self-contained', 'true',
        '-o', $dir, '--nologo', "-p:Version=$Version", '-p:DebugType=none')
}

Push-Location $root
try {
    if (-not $SkipTests) { Run dotnet @('test', 'tests/Ringside.Tests', '-c', 'Release', '--nologo') }
    New-Item -ItemType Directory -Force $releases | Out-Null

    if ($Target -in 'all', 'win') {
        # NativeAOT needs the MSVC linker on PATH; vcvars64.bat puts it there.
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        $vcvars = & $vswhere -all -products * -find 'VC\Auxiliary\Build\vcvars64.bat' | Select-Object -First 1
        if (-not $vcvars) { throw 'MSVC not found: install the "Desktop development with C++" workload.' }
        $bridge = Join-Path $out 'bridge'
        & cmd.exe /c "call `"$vcvars`" >nul 2>&1 && dotnet publish src\Ringside.StatusLine -c Release -r win-x64 -o `"$bridge`" --nologo -p:Version=$Version -p:IlcUseEnvironmentalTools=true -p:DebugType=none"
        if ($LASTEXITCODE) { throw 'status line bridge build failed' }

        $app = Join-Path $out 'win'
        Publish 'win-x64' $app
        Copy-Item (Join-Path $bridge 'ringside-statusline.exe') $app -Force
        if ($Pack) {
            Run $vpk @('pack', '--packId', 'Ringside', '--packVersion', $Version, '--packDir', $app,
                '--mainExe', 'Ringside.exe', '--runtime', 'win-x64', '--packTitle', 'Ringside', '--packAuthors', 'Knight AI+AV',
                '--icon', 'assets/ringside.ico', '--splashImage', 'assets/ringside.png',
                '--shortcuts', 'StartMenuRoot', '--releaseNotes', 'CHANGELOG.md', '--outputDir', $releases)
        }
    }

    if ($Target -in 'all', 'linux') {
        # macOS and Linux run the Claude Code bridge as `Ringside --statusline`, so no native helper.
        $app = Join-Path $out 'linux'
        Publish 'linux-x64' $app
        if ($Pack) {
            Run $vpk @('[linux]', 'pack', '--packId', 'Ringside', '--packVersion', $Version, '--packDir', $app,
                '--mainExe', 'Ringside', '--runtime', 'linux-x64', '--packTitle', 'Ringside', '--packAuthors', 'Knight AI+AV',
                '--icon', 'assets/ringside.png', '--releaseNotes', 'CHANGELOG.md', '--outputDir', $releases)
        }
    }

    if ($Target -in 'all', 'mac') {
        $publish = Join-Path $out 'mac-publish'
        Publish 'osx-arm64' $publish
        $bundle = Join-Path $out 'mac\Ringside.app'
        if (Test-Path (Split-Path $bundle)) { Remove-Item -Recurse -Force -LiteralPath (Split-Path $bundle) }
        $macos = Join-Path $bundle 'Contents\MacOS'
        $res = Join-Path $bundle 'Contents\Resources'
        New-Item -ItemType Directory -Force $macos, $res | Out-Null
        Copy-Item (Join-Path $publish '*') $macos -Recurse -Force
        Copy-Item 'assets/ringside.icns' (Join-Path $res 'Ringside.icns')
        $plist = (Get-Content 'packaging/macos/Info.plist' -Raw).Replace('{VERSION}', $Version)
        [IO.File]::WriteAllText((Join-Path $bundle 'Contents\Info.plist'), $plist.Replace("`r`n", "`n"), (New-Object Text.UTF8Encoding($false)))
        $stage = Join-Path $out "mac\Ringside-$Version-osx-arm64-unsigned.tar.gz"
        Run python @('-I', 'tools/make-mac-stage.py', $bundle, $stage, (Join-Path $out "mac\MANIFEST-$Version.txt"))
    }

    if ($Pack) {
        Get-ChildItem $releases -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
            '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
        } | Set-Content (Join-Path $releases 'SHA256SUMS.txt') -Encoding ascii
        Get-Content (Join-Path $releases 'SHA256SUMS.txt')
    }
}
finally {
    Pop-Location
}
