$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $workspaceRoot ('artifacts\playbook-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
$store=$fixture
$notes=New-Object 'System.Collections.Generic.List[string]'
$checks=New-Object 'System.Collections.Generic.List[string]'
$source=Join-Path $workspaceRoot 'Tweaker\Modules\Tuning.ps1'
$tokens=$null;$parseErrors=$null
[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$parseErrors) | Out-Null
if($parseErrors.Count){throw ($parseErrors | Out-String)}
. $source
function Check($Condition,[string]$Label){if(!$Condition){throw "FAILED: $Label"};$checks.Add($Label)}
function WriteBackup($Kind,$Value){
    $directory=Join-Path $store 'Backups';New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $path=Join-Path $directory ($Kind+'-'+[DateTime]::UtcNow.Ticks+'.json')
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}
# Read-only integration check: catch invalid SPI constants without changing user settings.
$nativeMenu=Get-PBSnapshot 'menu'
Check ($nativeMenu.Kind -eq 'Menu' -and $nativeMenu.Value -ge 0 -and $nativeMenu.Value -le 65535) 'Native SPI_GETMENUSHOWDELAY reads the actual Windows setting successfully'
Check ([IO.File]::ReadAllText($source).Contains('SetNumber(0x006B,')) 'Menu setter uses the independently verified WinUser.h constant 0x006B'
# Substitute ONLY OS get/set boundaries. Production validation, backups and transaction logic run intact.
$script:states=@{
    menu=[pscustomobject]@{Kind='Menu';Value=400}
    mouse=[pscustomobject]@{Kind='Mouse';Value=@(6,10,1)}
    capture=[pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$false;Kind=$null;Value=$null},[pscustomobject]@{Exists=$true;Kind='DWord';Value=1})}
    transparency=[pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$true;Kind='DWord';Value=1})}
}
$script:failOnce=$false;$script:writes=0
function Get-PBSnapshot([string]$Id){Assert-PBId $Id;return Convert-PBSnapshot $Id $script:states[$Id]}
function Set-PBSnapshot([string]$Id,$Snapshot){
    $script:writes++
    $script:states[$Id]=Convert-PBSnapshot $Id $Snapshot
    if($script:failOnce){$script:failOnce=$false;throw 'simulated OS rejection after a partial write'}
}

Check ($parseErrors.Count -eq 0) 'Tuning module parses without executing native APIs'
$blocked=$false
try {Invoke-TuningApply 'unknown'}catch{$blocked=$_.Exception.Message -like '*Неизвестн*'}
Check ($blocked -and $script:writes -eq 0) 'Unknown tweak ID is rejected before touching the OS boundary'
$script:states.menu=[pscustomobject]@{Kind='Menu';Value=20};$writesBefore=$script:writes
$unchanged=Invoke-TuningApply 'menu'
Check ($script:writes -eq $writesBefore -and (Get-PBSnapshot 'menu').Value -eq 20 -and !$unchanged.Backup) 'An existing faster 20ms menu delay is preserved instead of slowing it to 100ms'
$script:states.menu=[pscustomobject]@{Kind='Menu';Value=400}

foreach($id in @('menu','mouse','capture','transparency')){
    $before=Get-PBSnapshot $id
    $result=Invoke-TuningApply $id
    Check ((Test-PBSame $id (Get-PBSnapshot $id) (Get-PBTarget $id)) -and (Test-Path -LiteralPath $result.Backup)) ("$id applies its whitelist target and saves a backup")
    $backup=Get-PBBackup $id
    Check ($backup -and !$backup.Restored -and (Test-PBSame $id $backup.Before $before)) ("$id backup preserves the exact original state")
    Invoke-TuningRestore $id | Out-Null
    Check ((Test-PBSame $id (Get-PBSnapshot $id) $before) -and (Get-PBBackup $id).Restored) ("$id restores exact originals and records restoration")
}
Check (!(Get-PBSnapshot 'capture').Values[0].Exists) 'Rollback preserves a missing capture value instead of inventing a default'

Invoke-TuningApply 'menu' | Out-Null
$script:states.menu=[pscustomobject]@{Kind='Menu';Value=250}
$writesBefore=$script:writes;$blocked=$false
try {Invoke-TuningRestore 'menu'}catch{$blocked=$_.Exception.Message -like '*вне Qivoryx*'}
Check ($blocked -and $script:writes -eq $writesBefore -and (Get-PBSnapshot 'menu').Value -eq 250) 'Restore refuses to overwrite a concurrent external change'

$script:states.capture=[pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$true;Kind='DWord';Value=1},[pscustomobject]@{Exists=$false;Kind=$null;Value=$null})}
$before=Get-PBSnapshot 'capture';$script:failOnce=$true;$failed=$false
try {Invoke-TuningApply 'capture'}catch{$failed=$_.Exception.Message -like '*simulated OS rejection*'}
Check ($failed -and (Test-PBSame 'capture' (Get-PBSnapshot 'capture') $before)) 'Partial failure rolls back both capture settings'
$latest=Get-ChildItem (Join-Path $store 'Backups') -Filter 'playbook-capture-*.json' | Sort-Object Name -Descending | Select-Object -First 1
$record=Get-Content -LiteralPath $latest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
Check (!$record.Completed) 'Failed apply never advertises a completed restore point'

$blocked=$false
try {Convert-PBSnapshot 'menu' ([pscustomobject]@{Kind='Menu';Value='100; arbitrary command'}) | Out-Null}catch{$blocked=$true}
Check $blocked 'Corrupt menu backup cannot inject a command or a string value'
$blocked=$false
try {Convert-PBSnapshot 'mouse' ([pscustomobject]@{Kind='Mouse';Value=@(0,0,99)}) | Out-Null}catch{$blocked=$true}
Check $blocked 'Invalid acceleration mode is rejected before native restoration'
$blocked=$false
try {Convert-PBSnapshot 'capture' ([pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$true;Kind='Unknown';Value=1},[pscustomobject]@{Exists=$false;Kind=$null;Value=$null})}) | Out-Null}catch{$blocked=$true}
Check $blocked 'Unknown registry kinds in a backup are rejected'

$binary=[pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$true;Kind='Binary';Value=@(0,1,255)})}
$normalized=Convert-PBSnapshot 'transparency' ($binary | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
Check ($normalized.Values[0].Value -is [byte[]] -and $normalized.Values[0].Value[2] -eq 255) 'Binary registry originals retain their type through a JSON round trip'
$expanded=[pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$true;Kind='ExpandString';Value='%USERPROFILE%\unchanged'})}
Check ((Convert-PBSnapshot 'transparency' $expanded).Values[0].Value -ceq '%USERPROFILE%\unchanged') 'Expandable strings are preserved without expanding environment variables'
$definition=@(Get-PBRegistryDefinition 'capture')
Check ($definition.Count -eq 2 -and $definition[0].Path -eq 'System\GameConfigStore' -and $definition[1].Name -eq 'AppCaptureEnabled') 'Registry operations use fixed definitions instead of caller-supplied paths'

$latest=Get-ChildItem (Join-Path $store 'Backups') -Filter 'playbook-menu-*.json' | Sort-Object Name -Descending | Select-Object -First 1
$record=Get-Content -LiteralPath $latest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
$owner=$record.OwnerSid;$record.OwnerSid='S-1-5-21-999-999-999-999'
Write-PBRecord $latest.FullName $record
$writesBefore=$script:writes;$blocked=$false
try {Invoke-TuningRestore 'menu'}catch{$blocked=$_.Exception.Message -like '*другому пользователю*'}
Check ($blocked -and $script:writes -eq $writesBefore) 'Portable backup from a different Windows SID cannot change this user settings'
$record.OwnerSid=$owner;$record.Applied.Value=300
Write-PBRecord $latest.FullName $record
$blocked=$false
try {Get-PBBackup 'menu' | Out-Null}catch{$blocked=$_.Exception.Message -like '*недопустимые применённые*'}
Check $blocked 'Backup cannot substitute a different applied target to bypass the fixed tweak definition'

$script:states.capture=[pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$true;Kind='DWord';Value=1},[pscustomobject]@{Exists=$false;Kind=$null;Value=$null})}
$valid=Invoke-TuningApply 'capture'
$script:states.capture=[pscustomobject]@{Kind='Registry';Values=@([pscustomobject]@{Exists=$true;Kind='DWord';Value=1},[pscustomobject]@{Exists=$false;Kind=$null;Value=$null})}
$script:failOnce=$true
try {Invoke-TuningApply 'capture' | Out-Null}catch{}
Check ((Get-PBBackup 'capture').Path -eq $valid.Backup) 'Confirmed rollback of a failed operation does not hide the previous valid backup'
$unfinished=@{Version=1;Id='capture';OwnerSid=(Get-PBOwnerSid);Before=(Get-PBSnapshot 'capture');Applied=(Get-PBTarget 'capture');Completed=$false;Restored=$false;RolledBack=$false}
$unfinishedPath=WriteBackup 'playbook-capture' $unfinished
$blocked=$false
try {Get-PBBackup 'capture' | Out-Null}catch{$blocked=$_.Exception.Message -like '*не завершена*'}
Check $blocked 'Unconfirmed interrupted operation remains visible instead of silently skipping its recovery state'

$originalStore=$store
$linkParent=Join-Path $fixture 'LinkedStore';$linkTarget=Join-Path $fixture 'OutsideBackup'
New-Item -ItemType Directory -Force -Path $linkParent,$linkTarget | Out-Null
New-Item -ItemType Junction -Path (Join-Path $linkParent 'Backups') -Target $linkTarget | Out-Null
$store=$linkParent;$blockedRead=$false;$blockedWrite=$false;$writesBefore=$script:writes
try {Get-PBBackup 'menu' | Out-Null}catch{$blockedRead=$_.Exception.Message -like '*ссылкой*'}
try {Invoke-TuningApply 'menu' | Out-Null}catch{$blockedWrite=$_.Exception.Message -like '*ссылкой*'}
Check ($blockedRead -and $blockedWrite -and $script:writes -eq $writesBefore) 'Backup junctions are rejected on both read and write before an OS mutation'
$store=$originalStore

$checks | ConvertTo-Json | Set-Content (Join-Path $workspaceRoot 'artifacts\playbook-tests.json') -Encoding UTF8
$checks
