param(
    [ValidateSet('Prepare','Apply','Restore')][string]$Action = 'Prepare',
    [Parameter(Mandatory=$true)][string]$Package,
    [string]$GameFolder = '',
    [Parameter(Mandatory=$true)][string]$Character,
    [Parameter(Mandatory=$true)][string]$Server,
    [string]$TrackerExe
)
$ErrorActionPreference = 'Stop'
if ($Action -eq 'Prepare' -and !$GameFolder) { throw 'Pass -GameFolder when preparing display settings.' }
if (-not $TrackerExe) {
    $trackerParent = Split-Path -Parent $PSScriptRoot
    $TrackerExe = Join-Path $trackerParent 'EQLTracker.exe'
    if (-not (Test-Path -LiteralPath $TrackerExe)) { $TrackerExe = Join-Path $trackerParent 'dist\EQLTracker-win-x64\EQLTracker.exe' }
}
if (-not (Test-Path -LiteralPath $TrackerExe)) { throw 'Pass -TrackerExe with the path to EQLTracker.exe.' }
if ($Action -ne 'Prepare' -and (Get-Process eqgame -ErrorAction SilentlyContinue)) { throw 'Close EverQuest before applying or restoring saved display settings. No game files were changed.' }
$trackerPackage = [IO.Path]::GetFullPath($Package)
$trackerFlag = switch ($Action) { 'Prepare' { '--display-prepare' }; 'Apply' { '--display-apply' }; 'Restore' { '--display-restore' } }
$trackerProcess = Start-Process -FilePath $TrackerExe -ArgumentList @($trackerFlag, ('"' + $trackerPackage + '"'), '--game-folder', ('"' + $GameFolder + '"'), '--character', $Character, '--server', $Server) -WindowStyle Hidden -Wait -PassThru
$trackerReport = Join-Path $trackerPackage 'last-operation.json'
if (Test-Path -LiteralPath $trackerReport) { Get-Content -LiteralPath $trackerReport }
if ($trackerProcess.ExitCode -ne 0) { throw 'Display setup did not complete. See last-operation.json.' }
