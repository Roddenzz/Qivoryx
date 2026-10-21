$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $workspaceRoot ('artifacts\startup-test-'+[guid]::NewGuid().ToString('N'))
$store=Join-Path $fixture 'Data'
$startupFixture=Join-Path $fixture 'Startup'
$registryFixture='HKCU:\Software\QivoryxFixture\'+[guid]::NewGuid().ToString('N')
$machineFixture=Join-Path $fixture 'MachineBackups\StartupMachine'
New-Item -ItemType Directory -Force -Path $store,$startupFixture | Out-Null
$notes=New-Object 'System.Collections.Generic.List[string]'
. (Join-Path $workspaceRoot 'Tweaker\Modules\Startup.ps1')
# Substitute source discovery only. Production inventory, validation, disable and
# restoration functions operate on isolated files and a temporary registry key.
function Get-StartupSources {
    @(
        [pscustomobject]@{Key='fixture-folder';Kind='Folder';Path=$startupFixture;Scope='Fixture';Admin=$false;Approved=($registryFixture+'\MissingApproved')}
        [pscustomobject]@{Key='fixture-run';Kind='Registry';Path=$registryFixture;Scope='Fixture';Admin=$false;Approved=($registryFixture+'\MissingApproved')}
        [pscustomobject]@{Key='fixture-machine';Kind='Registry';Path=($registryFixture+'\MissingMachine');Scope='Fixture machine';Admin=$true;Approved=($registryFixture+'\MissingApproved')}
    )
}
function Get-StartupMachinePath {return $machineFixture}
$checks=New-Object 'System.Collections.Generic.List[string]'
function Check($value,$label){if(!$value){throw ('FAILED: '+$label)};$checks.Add($label)}
function Rejected($action){try{& $action;return $false}catch{return $true}}
try {
    Check (Test-StartupMachineAcl (New-StartupMachineAcl)) 'Protected machine directory ACL accepts Admins/System full and Users read'
    Check (Test-StartupMachineAcl (New-StartupMachineAcl -File) -File) 'Protected machine file ACL accepts the expected rights'
    $acl=New-StartupMachineAcl;$acl.SetOwner([Security.Principal.WindowsIdentity]::GetCurrent().User)
    Check (!(Test-StartupMachineAcl $acl)) 'User-owned machine backup directory is rejected'
    $acl=New-StartupMachineAcl;$acl.SetAccessRuleProtection($false,$false)
    Check (!(Test-StartupMachineAcl $acl)) 'Inherited machine backup ACL is rejected'
    $acl=New-StartupMachineAcl
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-545'),[Security.AccessControl.FileSystemRights]::Modify,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)))
    Check (!(Test-StartupMachineAcl $acl)) 'Writable Users permissions on machine backups are rejected'
    $file=Join-Path $startupFixture 'Example.cmd'
    $protected=Join-Path $startupFixture 'SecurityHealth.cmd'
    [IO.File]::WriteAllText($file,'fixture - must never run')
    [IO.File]::WriteAllText($protected,'fixture - must never run')
    $initial=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    New-Item -Path $registryFixture -Force | Out-Null
    $command='%LOCALAPPDATA%\Fixture\Example.exe --quiet "literal"'
    New-ItemProperty -LiteralPath $registryFixture -Name 'FixtureApp' -Value $command -PropertyType ExpandString | Out-Null
    $inventory=Get-StartupEntries
    Check ($inventory.Entries.Count -eq 3) 'Inventory combines startup folder and Run sources'
    $entry=@($inventory.Entries | Where-Object Name -eq 'Example')[0]
    $blocked=@($inventory.Entries | Where-Object Name -eq 'SecurityHealth')[0]
    Check (!$blocked.CanDisable -and $blocked.Protected) 'Security startup entries are protected'
    Check (Rejected {Disable-StartupEntry $blocked.Id}) 'Protected entry is rejected at the backend'
    $saved=Disable-StartupEntry $entry.Id
    Check (!(Test-Path -LiteralPath $file)) 'Selected startup file is removed from active folder'
    Check ((Get-StartupEntries).Disabled.Count -eq 1) 'Disabled file appears in recoverable entries'
    Restore-StartupEntry $saved.Id | Out-Null
    Check ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $initial) 'Startup restoration preserves exact bytes'
    Check ((Get-StartupEntries).Disabled.Count -eq 0) 'Restored startup entry leaves the disabled list'
    Check (Rejected {Restore-StartupEntry $saved.Id}) 'A recovery record cannot be applied twice'
    $run=@((Get-StartupEntries).Entries | Where-Object Name -eq 'FixtureApp')[0]
    $savedRun=Disable-StartupEntry $run.Id
    Check (!('FixtureApp' -in @((Get-Item -LiteralPath $registryFixture).GetValueNames()))) 'Selected Run value is removed'
    Restore-StartupEntry $savedRun.Id | Out-Null
    $key=Get-Item -LiteralPath $registryFixture
    Check ($key.GetValueKind('FixtureApp').ToString() -eq 'ExpandString') 'Registry restoration preserves ExpandString type'
    Check ($key.GetValue('FixtureApp',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -eq $command) 'Registry restoration preserves unexpanded command and arguments'
    $run=@((Get-StartupEntries).Entries | Where-Object Name -eq 'FixtureApp')[0]
    $savedRun=Disable-StartupEntry $run.Id
    New-ItemProperty -LiteralPath $registryFixture -Name 'FixtureApp' -Value 'replacement.exe' -PropertyType String | Out-Null
    Check (Rejected {Restore-StartupEntry $savedRun.Id}) 'A newer Run value blocks restoration'
    Check ((Get-Item -LiteralPath $registryFixture).GetValue('FixtureApp') -eq 'replacement.exe') 'Run conflict keeps the current command'
    $entry=@((Get-StartupEntries).Entries | Where-Object Name -eq 'Example')[0]
    $saved=Disable-StartupEntry $entry.Id
    [IO.File]::WriteAllText($file,'new app configuration')
    Check (Rejected {Restore-StartupEntry $saved.Id}) 'Existing startup target blocks restoration'
    Check ([IO.File]::ReadAllText($file) -eq 'new app configuration') 'Restore conflict never overwrites current configuration'
    Check (Rejected {Restore-StartupEntry '..\outside'}) 'Recovery IDs reject path traversal'
    Check (Rejected {Disable-StartupEntry ('0'*64)}) 'Unknown startup ID is rejected'
    $backupPath=Join-Path (Join-Path $store 'Startup') ($saved.Id+'.json')
    $backup=Get-Content -LiteralPath $backupPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $originalBackup=$backup | ConvertTo-Json
    $backup.Computer='DifferentFixtureComputer'
    $backup | ConvertTo-Json | Set-Content -LiteralPath $backupPath -Encoding UTF8
    Check (Rejected {Read-StartupBackup $saved.Id}) 'Portable startup recovery rejects another computer'
    $backup=$originalBackup | ConvertFrom-Json;$backup.UserSid='S-1-5-21-0-0-0-9999'
    $backup | ConvertTo-Json | Set-Content -LiteralPath $backupPath -Encoding UTF8
    Check (Rejected {Read-StartupBackup $saved.Id}) 'User startup recovery rejects another profile SID'
    $backup=$originalBackup | ConvertFrom-Json;$backup.SourceKey='fixture-machine';$backup.Kind='Registry'
    $backup | ConvertTo-Json | Set-Content -LiteralPath $backupPath -Encoding UTF8
    Check (Rejected {Read-StartupBackup $saved.Id}) 'User-owned backup cannot restore a machine startup source'
    $backup=$originalBackup | ConvertFrom-Json
    $backup.Name='..\outside.cmd'
    $backup | ConvertTo-Json | Set-Content -LiteralPath $backupPath -Encoding UTF8
    Check (Rejected {Restore-StartupEntry $saved.Id}) 'Tampered backup cannot escape startup source'
    Check (Test-Path -LiteralPath $protected) 'Protected startup file remains unchanged'
    $outside=Join-Path $fixture 'Outside'
    New-Item -ItemType Directory -Path $outside | Out-Null
    $linkedFile=Join-Path $outside 'Linked.cmd'
    [IO.File]::WriteAllText($linkedFile,'outside fixture must not move')
    $junction=Join-Path $fixture 'LinkedStartup'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $startupFixture=$junction
    $entry=@((Get-StartupEntries).Entries | Where-Object Name -eq 'Linked')[0]
    Check (Rejected {Disable-StartupEntry $entry.Id}) 'Startup source junction is rejected before file mutation'
    Check (Test-Path -LiteralPath $linkedFile) 'Junction traversal cannot move an outside file'
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($machineFixture)) | Out-Null
    Check (Rejected {Get-StartupMachineDirectory -Create}) 'Precreated user-owned machine store fails closed without ACL repair'
    # Exercise the machine-store routing through the exact production logic.
    # Permission endpoints are substituted because the test is intentionally
    # unelevated. The real ACL validator was exercised on ACL objects above.
    function Get-StartupMachineDirectory([switch]$Create){
        if($Create -and !(Test-Path -LiteralPath $machineFixture)){New-Item -ItemType Directory -Path $machineFixture | Out-Null}
        if(Test-Path -LiteralPath $machineFixture){return $machineFixture};return $null
    }
    function Test-StartupMachineAcl($Acl,[switch]$File){return $true}
    function Open-StartupMachineOutput([string]$Path,[switch]$Existing){
        if([IO.Path]::GetDirectoryName($Path) -ne $machineFixture){throw 'Fixture boundary escaped'}
        $mode=if($Existing){[IO.FileMode]::Truncate}else{[IO.FileMode]::CreateNew}
        return [IO.File]::Open($Path,$mode,[IO.FileAccess]::Write,[IO.FileShare]::None)
    }
    $startupFixture=Join-Path $fixture 'Startup'
    $machineStartupFixture=Join-Path $fixture 'MachineStartup'
    New-Item -ItemType Directory -Path $machineStartupFixture | Out-Null
    New-Item -Path ($registryFixture+'\MissingMachine') | Out-Null
    New-ItemProperty -LiteralPath ($registryFixture+'\MissingMachine') -Name 'MachineFixture' -Value '%ProgramFiles%\Fixture.exe --quiet' -PropertyType ExpandString | Out-Null
    function Get-StartupSources {
        @(
            [pscustomobject]@{Key='fixture-folder';Kind='Folder';Path=$startupFixture;Scope='Fixture';Admin=$false;Approved=($registryFixture+'\MissingApproved')}
            [pscustomobject]@{Key='fixture-run';Kind='Registry';Path=$registryFixture;Scope='Fixture';Admin=$false;Approved=($registryFixture+'\MissingApproved')}
            [pscustomobject]@{Key='fixture-machine';Kind='Registry';Path=($registryFixture+'\MissingMachine');Scope='Fixture machine';Admin=$true;Approved=($registryFixture+'\MissingApproved')}
            [pscustomobject]@{Key='fixture-machine-folder';Kind='Folder';Path=$machineStartupFixture;Scope='Fixture machine';Admin=$true;Approved=($registryFixture+'\MissingApproved')}
        )
    }
    $entry=@((Get-StartupEntries).Entries | Where-Object Name -eq 'MachineFixture')[0]
    $savedMachine=Disable-StartupEntry $entry.Id
    Check (Test-Path -LiteralPath (Join-Path $machineFixture ($savedMachine.Id+'.json'))) 'Machine Run backup is routed to the protected store'
    Check (!(Test-Path -LiteralPath (Join-Path (Get-StartupDirectory) ($savedMachine.Id+'.json')))) 'Machine backup never enters the user-writable startup store'
    Restore-StartupEntry $savedMachine.Id | Out-Null
    Check ((Get-Item -LiteralPath ($registryFixture+'\MissingMachine')).GetValueKind('MachineFixture').ToString() -eq 'ExpandString') 'Machine Run rollback preserves its registry value kind'
    $machineFile=Join-Path $machineStartupFixture 'MachineExample.cmd'
    [IO.File]::WriteAllText($machineFile,'machine fixture never executed')
    $machineHash=(Get-FileHash -LiteralPath $machineFile -Algorithm SHA256).Hash
    $entry=@((Get-StartupEntries).Entries | Where-Object Name -eq 'MachineExample')[0]
    $savedMachine=Disable-StartupEntry $entry.Id
    Check (!(Test-Path -LiteralPath $machineFile)) 'Machine folder startup is removed only after creating its protected copy'
    Check ((Get-FileHash -LiteralPath (Join-Path $machineFixture ($savedMachine.Id+'.startup')) -Algorithm SHA256).Hash -eq $machineHash) 'Protected machine copy keeps the startup file bytes'
    Restore-StartupEntry $savedMachine.Id | Out-Null
    Check ((Get-FileHash -LiteralPath $machineFile -Algorithm SHA256).Hash -eq $machineHash) 'Machine folder startup restores from the protected store'
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\startup-tests.json') -Encoding UTF8
    $checks
} finally {
    # Only remove the literal temporary test key created above.
    if(Test-Path -LiteralPath $registryFixture){Remove-Item -LiteralPath $registryFixture -Recurse -Force}
}
