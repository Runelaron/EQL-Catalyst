param([Parameter(Mandatory)][string]$App,[Parameter(Mandatory)][string]$Output,[string[]]$Only=@())
$ErrorActionPreference='Stop'
$App=(Resolve-Path -LiteralPath $App).Path
$Output=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory $Output -Force | Out-Null
$cases=@(
  @('toolbar','--workspace-toolbar-only','workspace-toolbar-checks.json'),
  @('art','--pet-art-only','pet-art-checks.json'),
  @('repair','--ui-repair-only','ui-repair-checks.json'),
  @('integrated','--integrated-companions-only','render-checks.json'),
  @('sessions','--beta16-review','beta16-checks.json'),
  @('drafts','--pet-workspace-only','pet-workspace-checks.json'),
  @('summons','--pet-context-only','pet-details-popup-checks.json'),
  @('buffs','--standard-pet-buffs-only','standard-buffs-checks.json'),
  @('content','--october-content-only','october-smoke-checks.json'),
  @('selectors','--independent-displays-only','independent-display-checks.json')
)
$results=[Collections.Generic.List[object]]::new()
foreach($case in $cases){
  if($Only.Count -gt 0 -and $case[0] -notin $Only){continue}
  $folder=Join-Path $Output $case[0]
  if(Test-Path $folder){throw "Use a new UI output folder: $folder"}
  $arguments=@('--preview',$folder,$case[1],'--icon-candidates',(Join-Path $PSScriptRoot '../docs/previews/pet-icon-candidates'),'--offline','--checks-only','--beta16-session-only')
  $quoted=($arguments|ForEach-Object{'"'+$_.Replace('"','\"')+'"'}) -join ' '
  $timer=[Diagnostics.Stopwatch]::StartNew()
  $process=Start-Process -FilePath $App -ArgumentList $quoted -WindowStyle Hidden -PassThru
  if(!$process.WaitForExit(600000)){throw "UI case timed out: $($case[0]); process was not force-terminated"}
  $process.Refresh()
  $report=Join-Path $folder $case[2]
  if($process.ExitCode -ne 0 -or !(Test-Path $report) -or (Test-Path (Join-Path $folder 'validation-failure.json'))){throw "UI case failed: $($case[0]); retain its diagnostics"}
  $checks=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
  $results.Add(@{Case=$case[0];Passed=$true;Checks=@($checks.Checks).Count;Milliseconds=$timer.Elapsed.TotalMilliseconds})
}
@{Passed=$true;Cases=$results;Scope='Separate synthetic native processes; physical monitor transitions and live-game acceptance are separate';AppSha256=(Get-FileHash -LiteralPath $App).Hash.ToLowerInvariant()} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $Output 'native-validation.json') -Encoding utf8
