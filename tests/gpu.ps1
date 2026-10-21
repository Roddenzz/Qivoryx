$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $workspaceRoot ('artifacts\gpu-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
$store=$fixture;$notes=New-Object 'System.Collections.Generic.List[string]';$checks=New-Object 'System.Collections.Generic.List[string]'
$source=Join-Path $workspaceRoot 'Tweaker\Modules\Gpu.ps1'
$tokens=$null;$parseErrors=$null
[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$parseErrors)|Out-Null
if($parseErrors.Count){throw ($parseErrors|Out-String)}
. $source
function Check($Value,[string]$Label){if(!$Value){throw "FAILED: $Label"};$checks.Add($Label)}
function Reject([scriptblock]$Action,[string]$Label){$rejected=$false;try{& $Action|Out-Null}catch{$rejected=$true};Check $rejected $Label}
# Actual native calls below ONLY enumerate, never test/set display modes or open panels.
Initialize-GpuNative
Check ([QivoryxDisplayNative]::ModeSize() -eq 220) 'DEVMODEW matches native Windows Unicode ABI (220 bytes)'
$actual=@(Get-GpuNativeDisplays)
foreach($display in $actual){Check ([bool](Convert-GpuMode $display)) 'Native read validates an actual attached display without modification'}
Check ([IO.File]::ReadAllText($source).Contains('Stopwatch]::StartNew()')) 'Preview watchdog uses monotonic elapsed time'
Check ([IO.File]::ReadAllText((Join-Path $workspaceRoot 'Tweaker\GpuPage.cs')).Contains('catch (IOException)')) 'UI tolerates transient state-file read sharing failures'
Check (Test-GpuAsymmetricX3D 'AMD Ryzen 9 7950X3D 16-Core Processor' 16) 'Known 7950X3D asymmetric model enables readiness guidance'
Check (Test-GpuAsymmetricX3D 'AMD Ryzen 9 9900X3D 12-Core Processor' 12) 'Known 9900X3D asymmetric model enables readiness guidance'
Check (!(Test-GpuAsymmetricX3D 'AMD Ryzen 7 9800X3D 8-Core Processor' 8)) 'Single CCD X3D does not receive asymmetric CCD assumptions'
Check (!(Test-GpuAsymmetricX3D 'AMD Ryzen 9 9950X3D2' 16)) 'Unknown dual-cache X3D2 is not classified by substring'
Check (!(Test-GpuAsymmetricX3D 'AMD Ryzen 9 9950X3D' 8)) 'Core inventory disagreement suppresses topology guidance'
$script:writeRecord=${function:Write-GpuBackupRecord}
function Write-GpuBackupRecord([string]$Path,$Record){if($script:failRecord){throw 'Fixture backup commit failure'};& $script:writeRecord $Path $Record}
function WriteBackup($Kind,$Value){
    $directory=Get-GpuSafeDirectory 'Backups';New-Item -ItemType Directory -Path $directory -Force|Out-Null
    $path=Join-Path $directory ($Kind+'-'+[DateTime]::UtcNow.Ticks+'.json')
    $Value|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}
function Reset-Fixture {
    $script:store=Join-Path $fixture ([guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $store -Force|Out-Null
    $script:mode=[pscustomobject]@{Name='\\.\DISPLAY1';Adapter='Fixture Radeon';AdapterId='PCI\VEN_1002&DEV_FFFF';MonitorId='DISPLAY\FIXTURE\MONITOR1';Width=2560;Height=1440;Bits=32;Frequency=60;Orientation=0;Flags=0;FixedOutput=0;PositionX=0;PositionY=0;SavedFrequency=60;SavedGeometryMatches=$true;Modes=@(60,120,240)}
    $script:connected=$true;$script:writeCalls=0;$script:testCalls=0;$script:answer=$true;$script:onAnswer=$null;$script:failRecord=$false;$script:failSave=$false
}
function Get-GpuNativeDisplays {if($script:connected){return @($script:mode)};return @()}
function Set-GpuNativeFrequency([string]$Device,[int]$Frequency,[bool]$Save,[bool]$Test){
    if($Device -cne $script:mode.Name -or $Frequency -notin $script:mode.Modes){throw 'Fixture unsupported mode'}
    if($Test){$script:testCalls++;return}
    $script:writeCalls++;if($Save -and $script:failSave){$script:failSave=$false;throw 'Fixture driver save failure'}
    $script:mode.Frequency=$Frequency;if($Save){$script:mode.SavedFrequency=$Frequency}
}
function Wait-GpuPreviewAnswer([string]$Token,[DateTime]$Deadline){if($script:onAnswer){& $script:onAnswer};return $script:answer}
function Request([int]$Frequency=120,[bool]$Restore=$false){return [pscustomobject]@{token=[guid]::NewGuid().ToString('N');device='\\.\DISPLAY1';frequency=$Frequency;restore=$Restore}}
Reset-Fixture
Reject {Invoke-GpuDisplayPreview ([pscustomobject]@{token='../bad';restore=$false})} 'Malformed token rejected before mode changes'
Reject {Invoke-GpuDisplayPreview ([pscustomobject]@{token=[guid]::NewGuid().ToString('N');device='\\.\DISPLAY1';frequency=120;restore='false'})} 'String restore flag rejected'
Reject {Invoke-GpuDisplayPreview (Request 999)} 'Unsupported refresh frequency rejected'
$fraction=Request;$fraction.frequency=120.5;Reject {Invoke-GpuDisplayPreview $fraction} 'Fractional mode rejected'
Check ($writeCalls -eq 0 -and $testCalls -eq 0) 'Invalid requests never reach native test/set boundary'
$mode.SavedFrequency=120;Reject {Invoke-GpuDisplayPreview (Request)} 'Transient active/saved mode disagreement rejected'
Reset-Fixture;$script:answer=$false
$cancel=Invoke-GpuDisplayPreview (Request)
Check (!$cancel.Confirmed -and $mode.Frequency -eq 60 -and $mode.SavedFrequency -eq 60) 'Cancel/timeout restores previous active mode without changing persistent mode'
Check ($writeCalls -eq 2 -and $testCalls -eq 1) 'Preview performs driver validation and two fixture-only active changes on cancellation'
Reset-Fixture
$accepted=Invoke-GpuDisplayPreview (Request)
$backup=Get-GpuSavedBackup $mode.Name
Check ($accepted.Confirmed -and $mode.Frequency -eq 120 -and $mode.SavedFrequency -eq 120) 'Confirmation persists the exact supported refresh rate'
Check ($backup.Before.Frequency -eq 60 -and $backup.Applied.Frequency -eq 120 -and $backup.Record.OwnerSid -ceq (Get-GpuOwnerSid)) 'Backup preserves original mode and current user SID'
$restored=Invoke-GpuDisplayPreview (Request 120 $true)
Check ($restored.Confirmed -and $mode.Frequency -eq 60 -and $mode.SavedFrequency -eq 60) 'Confirmed restore returns the exact original active and persistent frequency'
Check (!(Get-GpuSavedBackup $mode.Name)) 'Restored backup is not offered again'
Reset-Fixture;$script:answer=$false;$script:onAnswer={$script:mode.Frequency=240;$script:mode.SavedFrequency=240}
$null=Invoke-GpuDisplayPreview (Request)
Check ($mode.Frequency -eq 240 -and $mode.SavedFrequency -eq 240 -and $writeCalls -eq 1) 'Cancellation never overwrites an external mode change'
Reset-Fixture;$script:answer=$false;$script:onAnswer={$script:mode.MonitorId='DISPLAY\DIFFERENT\MONITOR'}
$null=Invoke-GpuDisplayPreview (Request)
Check ($writeCalls -eq 1) 'Cancellation never alters a swapped monitor identity'
Reset-Fixture;$script:answer=$false;$script:onAnswer={$script:mode.PositionX=-2560}
$null=Invoke-GpuDisplayPreview (Request)
Check ($writeCalls -eq 1 -and $mode.PositionX -eq -2560) 'Cancellation preserves an externally rearranged multi-monitor topology'
Reset-Fixture;$script:answer=$false;$script:onAnswer={$script:connected=$false}
$null=Invoke-GpuDisplayPreview (Request)
Check ($writeCalls -eq 1) 'Disconnected monitor is not targeted for rollback'
Reset-Fixture;$script:onAnswer={$script:mode.Frequency=240}
Reject {Invoke-GpuDisplayPreview (Request)} 'Confirmation rejects an externally changed active mode'
Check ($mode.Frequency -eq 240 -and $mode.SavedFrequency -eq 60) 'External active mode survives failed confirmation'
Reset-Fixture;$script:onAnswer={$script:mode.SavedFrequency=240}
Reject {Invoke-GpuDisplayPreview (Request)} 'Confirmation rejects an externally changed saved mode'
Check ($mode.Frequency -eq 60 -and $mode.SavedFrequency -eq 240) 'Failed confirmation preserves an externally changed persistent mode'
Reset-Fixture;$script:failSave=$true
Reject {Invoke-GpuDisplayPreview (Request)} 'Driver persistence failure is surfaced'
Check ($mode.Frequency -eq 60 -and $mode.SavedFrequency -eq 60) 'Failed persistence rolls active and saved modes back'
Reset-Fixture;$script:failRecord=$true
Reject {Invoke-GpuDisplayPreview (Request)} 'Backup commit failure is surfaced'
Check ($mode.Frequency -eq 60 -and $mode.SavedFrequency -eq 60) 'Post-persistence backup failure restores both active and persistent modes'
Reset-Fixture;$null=Invoke-GpuDisplayPreview (Request)
$backup=Get-GpuSavedBackup $mode.Name;$backup.Record.OwnerSid='S-1-5-21-FOREIGN';Write-GpuBackupRecord $backup.Path $backup.Record
Reject {Invoke-GpuDisplayPreview (Request 120 $true)} 'Backups from another user cannot be restored'
Reset-Fixture;$null=Invoke-GpuDisplayPreview (Request)
$backup=Get-GpuSavedBackup $mode.Name;$backup.Record.Before.Width=1920;Write-GpuBackupRecord $backup.Path $backup.Record
Reject {Invoke-GpuDisplayPreview (Request 120 $true)} 'Forged backup geometry cannot be restored'
Reset-Fixture;$null=Invoke-GpuDisplayPreview (Request)
$mode.MonitorId='DISPLAY\SWAPPED\2'
Reject {Invoke-GpuDisplayPreview (Request 120 $true)} 'Saved restore refuses a different monitor'
Reset-Fixture
$preview=Get-GpuSafeDirectory 'GpuPreview';New-Item -ItemType Directory -Path $preview|Out-Null
$token=[guid]::NewGuid().ToString('N');$statePath=Join-Path $preview ('state-'+$token+'.json')
1..12|ForEach-Object{Write-GpuPreviewState $token @{Phase='preview';Sequence=$_};$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json;Check ($state.Sequence -eq $_) ('Atomic state publication round '+$_)}
Check (@(Get-ChildItem -LiteralPath $preview -Filter '*.tmp').Count -eq 0) 'Atomic state publication leaves no temporary files'
$busyLock=[IO.File]::Open((Join-Path $preview 'active.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{Reject {Invoke-GpuDisplayPreview (Request)} 'Concurrent display preview is rejected by exclusive backend lock'}finally{$busyLock.Dispose()}
Check ($writeCalls -eq 0) 'Concurrent preview never changes display settings'
$safeTarget=Join-Path $fixture 'junction-target';New-Item -ItemType Directory -Path $safeTarget|Out-Null
$linkedStore=Join-Path $fixture 'junction-data';New-Item -ItemType Junction -Path $linkedStore -Target $safeTarget|Out-Null
$script:store=$linkedStore;Reject {Get-GpuSafeDirectory 'GpuPreview'} 'Linked data-directory ancestry is rejected'
Reset-Fixture
Reject {Invoke-GpuPanel 'arbitrary.exe'} 'Panel launch rejects arbitrary executable names'
$checks|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\gpu-tests.json') -Encoding UTF8
Write-Output ('Passed '+$checks.Count+' GPU checks; all mode writes used fixtures. Native integration was read-only.')
