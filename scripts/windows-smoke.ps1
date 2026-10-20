param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP) { throw 'This installation test is restricted to a disposable GitHub-hosted runner.' }
$root = Join-Path $env:RUNNER_TEMP ('EqlSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
$checks = [Collections.Generic.List[object]]::new()
function Check([bool]$ok,[string]$name) { if (!$ok) { throw $name }; $checks.Add(@{Name=$name;Passed=$true}) }
function Run([string]$exe,[string[]]$arguments) {
  $quoted = @($arguments | ForEach-Object { '"' + $_.Replace('"','\"') + '"' }) -join ' '
  $p = Start-Process -FilePath $exe -ArgumentList $quoted -WorkingDirectory $root -PassThru -WindowStyle Hidden
  if (!$p.WaitForExit(90000)) { throw 'Test process timed out' }; $p.Refresh(); return $p.ExitCode
}
$data = Join-Path $root 'data'
function Snapshot { if (Test-Path $data) { return (@(Get-ChildItem $data -Recurse -File | Sort-Object FullName | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash }) -join ':') }; return '' }
$base=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=desktopicon')
foreach ($scope in @('CURRENTUSER','ALLUSERS')) {
  $app = Join-Path $root ('Custom '+$scope+' '+[char]0x00C9+' Path')
  Check ((Run $Installer ($base+@("/$scope","/DIR=$app"))) -eq 0) "$scope install succeeds with Unicode custom path"
  Check ((Test-Path "$app\coreclr.dll") -and (Test-Path "$app\PresentationFramework.dll")) 'Self-contained runtime is present'
  Check (!(Test-Path $data)) 'Installation creates no profile data'
  $manifest=Get-Content "$app\release-manifest.json" -Raw | ConvertFrom-Json
  foreach ($file in $manifest.Files.PSObject.Properties) {
    Check (((Get-FileHash -LiteralPath (Join-Path $app $file.Name)).Hash).ToLowerInvariant() -eq $file.Value) ('Installed payload hash: ' + $file.Name)
  }
  Check ((Run "$app\EQLTracker.exe" @('--data-root',$data,'--character','ExampleHero','--server','example','--launch-audit')) -eq 0) 'Offline first profile starts'
  $audit=Get-Content "$data\ExampleHero_example\launch-audit.json" -Raw | ConvertFrom-Json
  Check (!$audit.GameFolder -and $audit.Level -eq 0 -and !$audit.Race -and $audit.CurrentClasses.Count -eq 0) 'New facts remain unknown'
  Check (!$audit.NativeSync.Login -and !$audit.NativeSync.Periodic -and !$audit.NativeSync.Verified) 'Captures remain opt-in'
  Check ((Run "$app\EQLTracker.exe" @('--data-root',$data,'--launch-audit')) -eq 0) 'Restart remembers the last profile'
  $before=Snapshot
  Remove-Item -LiteralPath "$app\README.md"
  Check ((Run $Installer ($base+@("/$scope","/DIR=$app"))) -eq 0 -and (Test-Path "$app\README.md")) 'Reinstall repairs files'
  Check ((Snapshot) -eq $before) 'Reinstall preserves profiles byte for byte'
  Check ((Run "$app\unins000.exe" @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')) -eq 0) 'Uninstall succeeds'
  Check (!(Test-Path "$app\EQLTracker.exe") -and (Snapshot) -eq $before) 'Uninstall preserves profiles'
  # The disposable test data is removed only after containment is verified.
  if (([IO.Path]::GetFullPath($data)).StartsWith(([IO.Path]::GetFullPath($root)) + '\')) { Remove-Item -LiteralPath $data -Recurse -Force }
}
$firstRoot=Join-Path $root 'cancelled-first-run'
$cancel=Join-Path $root 'cancel.json'
Check ((Run (Join-Path $PSScriptRoot '../dist/EQL-Catalyst-1.0.0-beta.1-win-x64/EQLTracker.exe') @('--data-root',$firstRoot,'--first-run-cancel-audit',$cancel)) -eq 0) 'First-run cancellation audit succeeds'
Check (!(Test-Path $firstRoot) -and (Get-Content $cancel -Raw | ConvertFrom-Json).Cancelled) 'Cancelling first run writes no profile or launcher'
$result=@{Passed=$true;Count=$checks.Count;Checks=$checks;InstallerSha256=(Get-FileHash -LiteralPath $Installer).Hash;Scope='GitHub-hosted Windows; custom-path installs; no claim of fresh OS without .NET'}
New-Item -ItemType Directory artifacts -Force | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content artifacts/windows-smoke.json -Encoding UTF8
