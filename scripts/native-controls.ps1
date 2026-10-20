param(
    [ValidateSet('Prepare','PrepareLabels','Compact','Standard','Restore')][string]$Action = 'Prepare',
    [Parameter(Mandatory=$true)][string]$Package,
    [string]$GameFolder = '',
    [Parameter(Mandatory=$true)][string]$Character,
    [Parameter(Mandatory=$true)][string]$Server,
    [string]$TrackerExe,
    [string]$OriginalPackage
)
$ErrorActionPreference = 'Stop'
if ($Action -eq 'Prepare' -and !$GameFolder) { throw 'Pass -GameFolder when preparing native controls.' }
if (-not $TrackerExe) {
    $trackerParent = Split-Path -Parent $PSScriptRoot
    $TrackerExe = Join-Path $trackerParent 'EQLTracker.exe'
    if (-not (Test-Path -LiteralPath $TrackerExe)) { $TrackerExe = Join-Path $trackerParent 'dist\EQLTracker-win-x64\EQLTracker.exe' }
}
if (-not (Test-Path -LiteralPath $TrackerExe)) { throw 'Pass -TrackerExe with the path to EQLTracker.exe.' }
if ($Action -notin @('Prepare','PrepareLabels') -and (Get-Process eqgame -ErrorAction SilentlyContinue)) { throw 'Close EverQuest before installing or restoring native controls. No game files were changed.' }
$trackerPackage = [IO.Path]::GetFullPath($Package)
$trackerArguments = switch ($Action) {
    'Prepare' { @('--native-prepare', ('"' + $trackerPackage + '"'), '--game-folder', ('"' + $GameFolder + '"'), '--character', $Character, '--server', $Server) }
    'PrepareLabels' { if (-not $OriginalPackage) { throw 'Pass -OriginalPackage with the original setup folder.' }; @('--native-label-repair', ('"' + $trackerPackage + '"'), '--from-package', ('"' + [IO.Path]::GetFullPath($OriginalPackage) + '"'), '--character', $Character, '--server', $Server) }
    'Compact' { @('--native-apply', ('"' + $trackerPackage + '"'), '--character', $Character, '--server', $Server) }
    'Standard' { @('--native-apply', ('"' + $trackerPackage + '"'), '--native-standard', '--character', $Character, '--server', $Server) }
    'Restore' { @('--native-restore', ('"' + $trackerPackage + '"'), '--character', $Character, '--server', $Server) }
}
$trackerProcess = Start-Process -FilePath $TrackerExe -ArgumentList $trackerArguments -WindowStyle Hidden -Wait -PassThru
$trackerReport = Join-Path $trackerPackage 'last-operation.json'
if (Test-Path -LiteralPath $trackerReport) { Get-Content -LiteralPath $trackerReport }
if ($trackerProcess.ExitCode -ne 0) { throw 'Native setup did not complete. See last-operation.json; any conflict is reported there.' }
