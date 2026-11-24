<#
.SYNOPSIS
  Builds the submission zip on Windows (twin of make-zip.sh).

.DESCRIPTION
  Creates ..\LidarAgv-Lab5.zip with a top folder "LidarAgv\" that holds LidarPi\ (Pi program) and
  LidarPC\ (PC GUI) as separate folders, plus the solution and docs. bin\, obj\, .vs\, *.user and
  data\ (password hash) are never included. The Python dev viewer (tools\) is left out.
  NuGet packages are restored automatically from the .csproj files when the projects are built.

.EXAMPLE
  .\make-zip.ps1
  .\make-zip.ps1 -Output "$HOME\Desktop\lab5.zip"
#>
param([string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) "LidarAgv-Lab5.zip"))
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "==> Checking that the solution builds"
dotnet build LidarAgv.sln --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "build failed - fix it before zipping" }

$stage = Join-Path ([IO.Path]::GetTempPath()) ("lidaragv-zip-" + [Guid]::NewGuid())
$root = Join-Path $stage "LidarAgv"
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $excludeDirs = '\\(bin|obj|\.vs|data|tools|TestResults|publish)(\\|$)'
    Get-ChildItem -Recurse -File -Force | Where-Object {
        $rel = $_.FullName.Substring($PSScriptRoot.Length)
        ($rel -notmatch $excludeDirs) -and ($rel -notmatch '\\\.git(\\|$)') -and ($_.Extension -notin '.user', '.suo', '.zip')
    } | ForEach-Object {
        $rel = $_.FullName.Substring($PSScriptRoot.Length + 1)
        $target = Join-Path $root $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item $_.FullName $target
    }
    if (Test-Path $Output) { Remove-Item $Output }
    Compress-Archive -Path $root -DestinationPath $Output
    Write-Host "==> Wrote $Output"
}
finally {
    Remove-Item -Recurse -Force $stage
}
