[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$secureUxRoot = Join-Path $projectRoot 'repos\SecureUxTheme-master'
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$wdkRoot = Join-Path $artifactsRoot 'wdk-10.0.28000.1839'
$wdkVersion = '10.0.28000.1839'
$wdkLayoutVersion = '10.0.28000.0'

function Invoke-Checked([scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "A command failed with exit code $LASTEXITCODE."
    }
}

function Get-WdkLibrary([string]$Architecture) {
    $packageId = "microsoft.windows.wdk.$Architecture"
    $archive = Join-Path $artifactsRoot "$packageId.$wdkVersion.zip"
    $expanded = Join-Path $wdkRoot $Architecture
    $library = Join-Path $expanded "c\Lib\$wdkLayoutVersion\um\$Architecture\ntdllp.lib"
    if (Test-Path -LiteralPath $library) {
        return $library
    }

    New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $expanded -Force | Out-Null
    if (-not (Test-Path -LiteralPath $archive)) {
        $uri = "https://api.nuget.org/v3-flatcontainer/$packageId/$wdkVersion/$packageId.$wdkVersion.nupkg"
        Write-Host "Downloading official WDK package $packageId $wdkVersion..."
        Invoke-WebRequest -Uri $uri -OutFile $archive
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
    if (-not (Test-Path -LiteralPath $library)) {
        throw "The WDK package did not contain $library"
    }
    return $library
}

$cmakeHelp = (& cmake --help) -join "`n"
$generator = if ($cmakeHelp -match 'Visual Studio 18 2026') {
    'Visual Studio 18 2026'
} elseif ($cmakeHelp -match 'Visual Studio 17 2022') {
    'Visual Studio 17 2022'
} else {
    throw 'Visual Studio 2022 or 2026 with the C++ workload is required.'
}

$ntdllpX64 = Get-WdkLibrary 'x64'
$ntdllpX86 = Get-WdkLibrary 'x86'
$buildX64 = Join-Path $secureUxRoot 'cmake-build-release-x64'
$buildX86 = Join-Path $secureUxRoot 'cmake-build-release-x86'

Push-Location $secureUxRoot
try {
    Invoke-Checked { cmake -S . -B $buildX64 -G $generator -A x64 "-DSECUREUXTHEME_NTDLLP_LIBRARY=$($ntdllpX64.Replace('\', '/'))" }
    Invoke-Checked { cmake --build $buildX64 --config $Configuration --parallel }
    Invoke-Checked { cmake -S . -B $buildX86 -G $generator -A Win32 "-DSECUREUXTHEME_NTDLLP_LIBRARY=$($ntdllpX86.Replace('\', '/'))" }
    Invoke-Checked { cmake --build $buildX86 --config $Configuration --parallel }
    Invoke-Checked { dotnet build .\SecureUxTheme.wixproj -c $Configuration -a x64 -o .\bin\Release_x64 }
}
finally {
    Pop-Location
}

$installerProject = Join-Path $projectRoot 'installer\Aero11.Installer\Aero11.Installer.csproj'
$uninstallerProject = Join-Path $projectRoot 'installer\SecureUxTheme.Uninstaller\SecureUxTheme.Uninstaller.csproj'
$dist = Join-Path $projectRoot 'dist'
Invoke-Checked { dotnet publish $installerProject -c $Configuration -r win-x64 -o $dist }
Invoke-Checked { dotnet publish $uninstallerProject -c $Configuration -r win-x64 -o $dist }

$installer = Join-Path $dist 'Aero11-Installer.exe'
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer was not produced: $installer"
}
$uninstaller = Join-Path $dist 'SecureUxTheme-ForceUninstaller.exe'
if (-not (Test-Path -LiteralPath $uninstaller)) {
    throw "Uninstaller was not produced: $uninstaller"
}

Write-Host "Built: $installer"
Get-FileHash -LiteralPath $installer -Algorithm SHA256
Write-Host "Built: $uninstaller"
Get-FileHash -LiteralPath $uninstaller -Algorithm SHA256
