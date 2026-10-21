$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $workspaceRoot ('artifacts\app-removal-test-'+[guid]::NewGuid().ToString('N'))
$store=Join-Path $fixture 'Data'
$name='QivoryxRemovalFixture'+[guid]::NewGuid().ToString('N')
$registry='HKCU:\Software\'+$name
$appKey=$registry+'\Uninstall'
$checks=New-Object 'System.Collections.Generic.List[string]'
$savedRoaming=$env:APPDATA;$savedLocal=$env:LOCALAPPDATA
$env:APPDATA=Join-Path $fixture 'Roaming';$env:LOCALAPPDATA=Join-Path $fixture 'Local'
New-Item -ItemType Directory -Force -Path $fixture,$store,$env:APPDATA,$env:LOCALAPPDATA | Out-Null
New-Item -Path $appKey -Force | Out-Null
. (Join-Path $workspaceRoot 'Tweaker\Modules\Startup.ps1')
. (Join-Path $workspaceRoot 'Tweaker\Modules\AppRemoval.ps1')
function Check($Value,$Label){if(!$Value){throw ('FAILED: '+$Label)};$checks.Add($Label)}
function Rejected($Action){try{& $Action | Out-Null;return $false}catch{return $true}}
$script:app=[pscustomobject]@{Id=$appKey;Name=$name;Version='1.0';Type='Win32';Scope='Пользователь';Quiet=$true;Removable=$true}
$script:inventory=@($script:app)
function Apps {return @($script:inventory)}
$script:processCode=0;$script:removeOnSuccess=$true;$script:processCalls=0
function Invoke-RemovalProcess($Command){$script:processCalls++;if($script:removeOnSuccess -and $script:processCode -eq 0){$script:inventory=@()};return $script:processCode}
# The only native operation used by cleanup is registry export. Substitute it;
# registry deletion below is restricted to this test's unique HKCU subtree.
function Native([string]$File,[string[]]$Arguments){if($Arguments[0] -ne 'export'){throw 'Unexpected native operation'};Set-Content -LiteralPath $Arguments[2] -Value 'isolated export fixture' -Encoding UTF8}
try {
    $exe=Join-Path $fixture 'uninstall.exe';Set-Content -LiteralPath $exe -Value 'never executed'
    Set-ItemProperty -LiteralPath $appKey -Name UninstallString -Value ('"'+$exe+'" /uninstall')
    Set-ItemProperty -LiteralPath $appKey -Name QuietUninstallString -Value ('"'+$exe+'" /S')
    $dataPath=Join-Path $env:APPDATA $name;New-Item -ItemType Directory -Path $dataPath | Out-Null
    Set-Content -LiteralPath (Join-Path $dataPath 'settings.txt') -Value 'test data'
    $plan=Get-AppRemovalPlan $appKey
    Check ($plan.CanRemove -and $plan.Quiet) 'Plan resolves registered quiet command'
    Check (@($plan.Candidates | Where-Object Path -eq $dataPath).Count -eq 1) 'Exact application folder appears in preview'
    Check (@($plan.Candidates | Where-Object Path -eq $registry).Count -eq 1) 'Only exact application registry key appears in preview'
    Check (Rejected {Invoke-AppResidualCleanup $plan.PlanId @($plan.Candidates[0].Id)}) 'Cleanup rejected before verified uninstall'
    Check (Test-Path -LiteralPath $dataPath) 'Preview and rejected cleanup preserve files'
    Check (Rejected {Read-RemovalPlan '..\outside'}) 'Plan traversal identifier rejected'
    $script:processCode=1603
    Check (Rejected {Invoke-AppRemoval $plan.PlanId}) 'Installer failure is reported and does not permit cleanup'
    Check (Test-Path -LiteralPath $dataPath) 'Installer failure preserves residual data'
    $script:processCode=0;$script:removeOnSuccess=$false
    $result=Invoke-AppRemoval $plan.PlanId
    Check (!$result.Removed -and $result.Message -like '*заблокирована*') 'Exit zero with remaining registration is not reported as removed'
    Check (Rejected {Invoke-AppResidualCleanup $plan.PlanId @($plan.Candidates[0].Id)}) 'Remaining registration blocks residual cleanup'
    $script:removeOnSuccess=$true
    $result=Invoke-AppRemoval $plan.PlanId
    Check $result.Removed 'Removal is verified against refreshed inventory'
    Check (Test-Path -LiteralPath $dataPath) 'Successful uninstall does not automatically delete user data'
    Check (Rejected {Invoke-AppResidualCleanup $plan.PlanId @('unknown-target')}) 'Unknown residual target rejected before deletion'
    $outside=Join-Path $fixture 'Outside';New-Item -ItemType Directory -Path $outside | Out-Null
    $keep=Join-Path $outside 'keep.txt';Set-Content -LiteralPath $keep -Value 'must survive'
    $junction=Join-Path $dataPath 'Junction';New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    Check (Rejected {Invoke-AppResidualCleanup $plan.PlanId @($plan.Candidates.Id)}) 'Junction inside a candidate aborts the entire batch'
    Check (Test-Path -LiteralPath $keep) 'Cleanup does not traverse an external junction'
    [IO.Directory]::Delete($junction)
    $selected=@($plan.Candidates | Where-Object Kind -eq 'Directory' | ForEach-Object Id)
    $result=Invoke-AppResidualCleanup $plan.PlanId $selected
    Check ($result.Deleted.Count -eq 1 -and $result.Failed.Count -eq 0) 'Selected exact folder is removed with an honest count'
    Check (!(Test-Path -LiteralPath $dataPath)) 'Selected data folder removed'
    Check (Test-Path -LiteralPath $registry) 'Unselected registry target preserved'
    Check (Test-Path -LiteralPath $keep) 'Unrelated outside data preserved'
    # Shared folder detection uses the current inventory, including other app IDs.
    New-Item -ItemType Directory -Path $dataPath | Out-Null
    $otherKey=$registry+'\Other';New-Item -Path $otherKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $otherKey -Name InstallLocation -Value $dataPath
    $script:inventory=@([pscustomobject]@{Id=$otherKey;Name='Other application';Type='Win32'})
    Check (!(Test-RemovalCandidate $plan ($plan.Candidates | Where-Object Kind -eq 'Directory' | Select-Object -First 1))) 'Folder shared by another installed app is rejected'
    $script:inventory=@($script:app)
    $bad=[pscustomobject]@{Kind='Directory';Path=$fixture}
    Check (!(Test-RemovalCandidate $plan $bad)) 'Broad parent directory cannot be a residual candidate'
    $bad.Path=$env:APPDATA
    Check (!(Test-RemovalCandidate $plan $bad)) 'Profile root cannot be a residual candidate'
    foreach($command in @('cmd.exe /c erase all','"C:\Windows\System32\cmd.exe" /c anything','C:\Program Files\Bad\uninstall.exe /S','https://example.test/remove.exe')){
        Set-ItemProperty -LiteralPath $appKey -Name QuietUninstallString -Value $command
        Check (Rejected {Get-RemovalCommand $script:app}) ('Ambiguous or shell command rejected: '+$command)
    }
    Set-ItemProperty -LiteralPath $appKey -Name QuietUninstallString -Value ('"'+$exe+'" /S')
    Set-ItemProperty -LiteralPath $appKey -Name InstallLocation -Value $fixture
    Check (Rejected {Invoke-AppRemoval $plan.PlanId}) 'Changed registered identity invalidates the preview'
    Check (!(Test-RemovalName 'Microsoft')) 'Generic shared publisher name is excluded'
    Check (!(Test-RemovalName '..')) 'Traversal name is excluded'
    $script:inventory=@([pscustomobject]@{Id='another-version';Name=$name;Type='Appx'})
    Check (!(Test-RemovalCandidate $plan ($plan.Candidates | Where-Object Kind -eq 'Registry' | Select-Object -First 1))) 'Settings used by another installed version are preserved'
    $script:inventory=@()
    $registryIds=@($plan.Candidates | Where-Object Kind -eq 'Registry' | ForEach-Object Id)
    function Native([string]$File,[string[]]$Arguments){throw 'simulated registry export failure'}
    $result=Invoke-AppResidualCleanup $plan.PlanId $registryIds
    Check ($result.Failed.Count -eq 1 -and (Test-Path -LiteralPath $registry)) 'Registry backup failure preserves the key and returns an error'
    function Native([string]$File,[string[]]$Arguments){if($Arguments[0] -ne 'export'){throw 'Unexpected native operation'};Set-Content -LiteralPath $Arguments[2] -Value 'isolated export fixture' -Encoding UTF8}
    $result=Invoke-AppResidualCleanup $plan.PlanId $registryIds
    Check ($result.Deleted.Count -eq 1 -and !(Test-Path -LiteralPath $registry)) 'Only selected exact registry key is removed after backup'
    Check (@(Get-ChildItem -LiteralPath (Get-RemovalDirectory) -Filter '*.reg').Count -eq 1) 'Registry export retained beside the removal plan'
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\app-removal-tests.json') -Encoding UTF8
    $checks
}finally{
    $env:APPDATA=$savedRoaming;$env:LOCALAPPDATA=$savedLocal
    # This is the single generated, unique test hive; no application keys used.
    if(Test-Path -LiteralPath $registry){Remove-Item -LiteralPath $registry -Recurse -Force}
}
