[CmdletBinding()]
param([string]$Destination = (Join-Path $PSScriptRoot '..\repos'))

$ErrorActionPreference = 'Continue'
$env:GIT_LFS_SKIP_SMUDGE = '1'

$repositories = @(
    @{ Repo = 'nptr/msstyleEditor'; Branch = 'master'; DirName = 'msstyleEditor-master' },
    @{ Repo = 'namazso/SecureUxTheme'; Branch = 'master'; DirName = 'SecureUxTheme-master' },
)

$resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $resolvedDestination | Out-Null

$results = foreach ($item in $repositories) {
    $target = Join-Path $resolvedDestination $item.DirName
    if (Test-Path -LiteralPath $target) { [pscustomobject]@{Repo=$item.Repo;Status="present";Path=$target}; continue }
    if ($item.Branch) {
        & git clone --depth 1 --branch $item.Branch --single-branch "https://github.com/$($item.Repo).git" $target
    } else {
        & git clone --depth 1 "https://github.com/$($item.Repo).git" $target
    }
    [pscustomobject]@{Repo=$item.Repo;Status=(if($LASTEXITCODE -eq 0){"cloned"}else{"failed"});Path=$target}
}
$results | Format-Table -AutoSize
if ($results.Status -contains "failed") { Write-Warning "Algunos repos fallaron - revisa manualmente." }
