$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $workspaceRoot ('artifacts\performance-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$store=$fixture
$notes=New-Object 'System.Collections.Generic.List[string]'
. (Join-Path $workspaceRoot 'Tweaker\Modules\Performance.ps1')
$checks=New-Object 'System.Collections.Generic.List[string]'
function Check($Value,[string]$Label){if(!$Value){throw "FAILED: $Label"};$checks.Add($Label)}
$tokens=$null;$errors=$null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $workspaceRoot 'Tweaker\Modules\Performance.ps1'),[ref]$tokens,[ref]$errors) | Out-Null
Check ($errors.Count -eq 0) 'Performance module parses with the current PowerShell parser'

# Substitute the only OS-setting boundary. Tests never call SystemParametersInfo SET.
$script:fakeVisual=@{client=$true;menus=$true;tooltips=$false}
$script:writes=New-Object 'System.Collections.Generic.List[string]'
$script:rejectNextWrite=$false
function Read-VisualValue($Definition){return $script:fakeVisual[$Definition.Id]}
function Write-VisualValue($Definition,[bool]$Value){$script:writes.Add($Definition.Id+':'+$Value);$script:fakeVisual[$Definition.Id]=$Value;if($script:rejectNextWrite){$script:rejectNextWrite=$false;throw 'simulated setting failure'}}
function Get-VisualOwner {return 'fixture-owner'}
$blocked=$false
try{Set-VisualTweak 'arbitrary-native-code'}catch{$blocked=$_.Exception.Message -like '*Неизвестная*'}
Check $blocked 'Unknown setting ids are rejected before any native write'
Check ($script:writes.Count -eq 0) 'Rejected ids cause no settings writes'
$r=Set-VisualTweak 'tooltips'
Check (!$r.Changed -and !(Test-Path (Join-Path $store 'Backups\visual-tooltips.json'))) 'Already disabled effects do not create a misleading backup'
$r=Set-VisualTweak 'client'
Check ($r.Changed -and !$script:fakeVisual.client) 'Selected visual setting is disabled through the native boundary'
$backup=Read-VisualBackup 'client'
Check ($backup.Original -eq $true -and $backup.Applied -eq $false -and $backup.State -eq 'Applied') 'Exact original value and applied state are saved'
$script:fakeVisual.client=$true
$beforeWrites=$script:writes.Count
$blocked=$false
try{Restore-VisualTweak 'client'}catch{$blocked=$_.Exception.Message -like '*изменена вне*'}
Check ($blocked -and $script:writes.Count -eq $beforeWrites) 'Restore preserves changes made outside Qivoryx'
$blocked=$false
try{Set-VisualTweak 'client'}catch{$blocked=$_.Exception.Message -like '*не перезаписано*'}
Check ($blocked -and (Read-VisualBackup 'client').Original -eq $true) 'Reapply cannot overwrite an active original backup'
$script:fakeVisual.client=$false
$r=Restore-VisualTweak 'client'
Check ($r.Changed -and $script:fakeVisual.client -and (Read-VisualBackup 'client').State -eq 'Restored') 'Restore returns the exact original value'
$script:rejectNextWrite=$true
$failed=$false
try{Set-VisualTweak 'menus'}catch{$failed=$_.Exception.Message -eq 'simulated setting failure'}
Check ($failed -and $script:fakeVisual.menus) 'A partially failed write rolls back the original setting'
Check ((Read-VisualBackup 'menus').State -eq 'RolledBack') 'Successful automatic rollback is recorded'
$backup=Read-VisualBackup 'client';$backup.Original='true';Save-VisualBackup 'client' $backup
$blocked=$false
try{Read-VisualBackup 'client'}catch{$blocked=$_.Exception.Message -like '*повреждена*'}
Check $blocked 'Corrupt backup types are rejected before restore'

# Validate fixed-volume targeting and automatic media operation, without touching drives.
$script:volumeFileSystem='NTFS';$script:volumePresent=$true;$script:maintenanceCalls=0
function Get-CimInstance([string]$ClassName,[string]$Filter){if($script:volumePresent){return [pscustomobject]@{FileSystem=$script:volumeFileSystem;DriveType=3}}}
function Optimize-Volume([string]$DriveLetter,[switch]$Verbose,[string]$ErrorAction){$script:maintenanceCalls++;$script:maintenanceArguments=$PSBoundParameters;return 'fixture maintenance'}
$blocked=$false
try{Invoke-StorageMaintenance 'C: & command'}catch{$blocked=$_.Exception.Message -like '*одну букву*'}
Check ($blocked -and $script:maintenanceCalls -eq 0) 'Drive arguments cannot contain paths or shell fragments'
$script:volumeFileSystem='FAT32';$blocked=$false
try{Invoke-StorageMaintenance 'C'}catch{$blocked=$_.Exception.Message -like '*NTFS*'}
Check ($blocked -and $script:maintenanceCalls -eq 0) 'Unsupported volumes are rejected before maintenance'
$script:volumeFileSystem='NTFS';$r=Invoke-StorageMaintenance 'e'
Check ($script:maintenanceCalls -eq 1 -and $script:maintenanceArguments.DriveLetter -eq 'E') 'Validated drive letters are normalized'
Check (!$script:maintenanceArguments.ContainsKey('Defrag') -and !$script:maintenanceArguments.ContainsKey('ReTrim')) 'Windows chooses the media-appropriate operation'
$script:nativeCalls=New-Object 'System.Collections.Generic.List[object]'
function Native([string]$File,[string[]]$Arguments){$script:nativeCalls.Add(@{File=$File;Arguments=$Arguments});return 'fixture DISM status'}
Invoke-ComponentCleanup | Out-Null
Check ($script:nativeCalls[0].Arguments -contains '/StartComponentCleanup' -and $script:nativeCalls[0].Arguments -contains '/NoRestart' -and $script:nativeCalls[0].Arguments -notcontains '/ResetBase') 'Component cleanup never requests ResetBase or automatic restart'
Invoke-ComponentCheck | Out-Null
Check ($script:nativeCalls[1].Arguments -contains '/CheckHealth' -and $script:nativeCalls[1].Arguments -notcontains '/RestoreHealth') 'Component status check does not invoke repairs'
$checks | ConvertTo-Json | Set-Content (Join-Path $workspaceRoot 'artifacts\performance-tests.json') -Encoding UTF8
$checks
