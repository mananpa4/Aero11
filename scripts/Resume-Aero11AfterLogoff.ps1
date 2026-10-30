[CmdletBinding()]
param(
    [string]$InstallerPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\Aero11-Installer.exe'),

    [ValidateSet(
        'Aero11_Seven_Shiny.theme',
        'Aero11_Seven_Clear.theme',
        'Aero11_Vista.theme',
        'Aero11_Metro_8.1.theme',
        'Aero11_Metro_8.theme'
    )]
    [string]$ThemeFile = 'Aero11_Seven_Shiny.theme',

    [switch]$DoNotApplyTheme
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecuta este script como administrador.'
}

$source = (Resolve-Path -LiteralPath $InstallerPath).Path
$resumeDirectory = Join-Path $env:ProgramData 'Aero11\Resume'
$resumeInstaller = Join-Path $resumeDirectory 'Aero11-Installer.exe'
$resumeState = Join-Path $resumeDirectory 'resume.json'

$answer = [Windows.Forms.MessageBox]::Show(
    "Se cerrará la sesión y Aero11 Installer volverá a abrirse al iniciar sesión.`r`n`r`n¿Deseas continuar?",
    'Reanudar Aero11 después de cerrar sesión',
    [Windows.Forms.MessageBoxButtons]::YesNo,
    [Windows.Forms.MessageBoxIcon]::Question
)
if ($answer -ne [Windows.Forms.DialogResult]::Yes) {
    return
}

New-Item -ItemType Directory -Path $resumeDirectory -Force | Out-Null
if (-not [IO.Path]::GetFullPath($source).Equals([IO.Path]::GetFullPath($resumeInstaller), [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $source -Destination $resumeInstaller -Force
}

$state = [ordered]@{
    ThemeFile = $ThemeFile
    ApplyTheme = -not $DoNotApplyTheme.IsPresent
    CreatedAt = [DateTimeOffset]::Now
}
$state | ConvertTo-Json | Set-Content -LiteralPath $resumeState -Encoding UTF8

$taskName = 'Aero11 ResumeAfterLogoff'
$taskPath = '\'
$action = New-ScheduledTaskAction -Execute $resumeInstaller -Argument '--resume-after-logoff'
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null

Start-Process -FilePath "$env:SystemRoot\System32\shutdown.exe" -ArgumentList '/l'
