# Builds the Siege UI bundle without opening Unity.
#
#   .\build.ps1                 uses the version in ProjectSettings/ProjectVersion.txt
#   .\build.ps1 -Unity <path>   uses that Unity.exe instead
#
# Output lands in Build\PeekSeekSiegeUI, which is the folder to upload to the Workshop.

param([string]$Unity)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Unity) {
    $version = (Get-Content (Join-Path $here "ProjectSettings\ProjectVersion.txt") -TotalCount 1) -replace "m_EditorVersion: ", ""
    $Unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (-not (Test-Path $Unity)) {
        throw "Unity $version is not installed. Add it in Unity Hub with the Linux and Mac build support modules, or pass -Unity."
    }
}

$log = Join-Path $here "build.log"
if (Test-Path $log) { Remove-Item $log }

& $Unity -batchmode -nographics -quit -projectPath $here -executeMethod BuildPickBoardBundle.Build -logFile $log | Out-Null
$exit = $LASTEXITCODE

Get-Content $log | Select-String -Pattern "Siege UI bundle written|skipping|no .hash|error CS|Exception" | ForEach-Object { $_.Line }

if ($exit -ne 0) { throw "Unity exited with $exit; see $log" }
Get-ChildItem (Join-Path $here "Build\PeekSeekSiegeUI") -Recurse -File | Select-Object FullName, Length
