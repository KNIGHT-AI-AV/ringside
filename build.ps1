<#
.SYNOPSIS
    Tests and builds Ringside. With -Pack, also makes the Windows installer.

.EXAMPLE
    ./build.ps1                       # test and build
    ./build.ps1 -Version 1.1.0 -Pack  # plus installer, portable zip and update packages

.NOTES
    Needs the .NET 8 SDK. The status line bridge is compiled to native code, which also
    needs Visual Studio's "Desktop development with C++" workload. -Pack needs Velopack's
    CLI: dotnet tool install -g vpk
#>
param(
    [string]$Version = '1.0.0',
    [switch]$Pack
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'artifacts'

function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE) { throw "$exe failed with exit code $LASTEXITCODE" }
}

Push-Location $root
try {
    Run dotnet @('test', 'tests/Ringside.Tests', '-c', 'Release', '--nologo')

    # NativeAOT needs the MSVC linker on PATH; vcvars64.bat puts it there.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vcvars = & $vswhere -all -products * -find 'VC\Auxiliary\Build\vcvars64.bat' | Select-Object -First 1
    if (-not $vcvars) { throw 'MSVC not found: install the "Desktop development with C++" workload.' }
    $bridge = Join-Path $out 'bridge'
    & cmd.exe /c "call `"$vcvars`" >nul 2>&1 && dotnet publish src\Ringside.StatusLine -c Release -r win-x64 -o `"$bridge`" --nologo -p:Version=$Version -p:IlcUseEnvironmentalTools=true -p:DebugType=none"
    if ($LASTEXITCODE) { throw 'status line bridge build failed' }

    $app = Join-Path $out 'app'
    Run dotnet @('publish', 'src/Ringside', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-o', $app, '--nologo', "-p:Version=$Version", '-p:DebugType=none')
    Copy-Item (Join-Path $bridge 'ringside-statusline.exe') $app -Force

    if ($Pack) {
        $releases = Join-Path $out 'releases'
        New-Item -ItemType Directory -Force $releases | Out-Null
        # Fetch the previous release so Velopack can build a small delta update from it.
        try { & vpk download github --repoUrl https://github.com/KNIGHT-AI-AV/ringside --outputDir $releases 2>&1 | Out-Null }
        catch { Write-Host 'No earlier release to diff against; packing a full release only.' }
        Run vpk @('pack', '--packId', 'Ringside', '--packVersion', $Version, '--packDir', $app,
            '--mainExe', 'Ringside.exe', '--runtime', 'win-x64', '--packTitle', 'Ringside', '--packAuthors', 'Knight AI+AV',
            '--icon', 'assets/ringside.ico', '--splashImage', 'assets/ringside.png',
            '--shortcuts', 'StartMenuRoot', '--releaseNotes', 'CHANGELOG.md', '--outputDir', $releases)
        Get-ChildItem $releases -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
            '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
        } | Set-Content (Join-Path $releases 'SHA256SUMS.txt') -Encoding ascii
        Get-Content (Join-Path $releases 'SHA256SUMS.txt')
    }
}
finally {
    Pop-Location
}
