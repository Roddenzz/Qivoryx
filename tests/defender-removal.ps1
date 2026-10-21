$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $workspaceRoot ('artifacts\defender-test-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$store=Join-Path $fixture 'Store'
[IO.Directory]::CreateDirectory($store) | Out-Null
$checks=New-Object 'System.Collections.Generic.List[string]'
$notes=New-Object 'System.Collections.Generic.List[string]'
$mutations=New-Object 'System.Collections.Generic.List[string]'
function Check($Condition,[string]$Label){if(!$Condition){throw "FAILED: $Label"};$checks.Add($Label)}
function Rejected([scriptblock]$Action,[string]$Pattern,[string]$Label){$blocked=$false;try{& $Action | Out-Null}catch{$blocked=$_.Exception.Message -like $Pattern};Check $blocked $Label}

# Every destructive boundary is substituted before loading the production module.
# The filesystem exception permits only fixture directory creation, never removal.
function New-Item {
    [CmdletBinding()]param([string]$ItemType,[string]$Path,[switch]$Force)
    if($ItemType -ne 'Directory' -or !([IO.Path]::GetFullPath($Path)).StartsWith($fixture+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'BLOCKED: non-fixture New-Item'}
    Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $Path -Force:$Force
}
function Remove-Item {[CmdletBinding()]param([string]$LiteralPath,[switch]$Recurse,[switch]$Force);$mutations.Add('remove '+$LiteralPath);throw 'fixture deletion denied'}
function Stop-Service {[CmdletBinding()]param([string]$Name,[switch]$Force);$mutations.Add('stop '+$Name);throw 'fixture service stop denied'}
function New-ItemProperty {[CmdletBinding()]param([string]$LiteralPath,[string]$Name,$Value,[string]$PropertyType,[switch]$Force);$mutations.Add('registry '+$LiteralPath);throw 'fixture registry write denied'}
function Get-ItemPropertyValue {[CmdletBinding()]param([string]$LiteralPath,[string]$Name);return 1}
function Get-Service {[CmdletBinding()]param([string]$Name);return [pscustomobject]@{Name=$Name;Status='Stopped'}}
function Native([string]$File,[string[]]$Arguments){$mutations.Add($File+' '+($Arguments -join ' '));throw 'fixture native command denied'}
function Get-RemovalDirectory([switch]$Machine,[switch]$Create){
    $path=Join-Path $fixture $(if($Machine){'MachinePlans'}else{'Store\RemovalPlans'})
    if($Create){[IO.Directory]::CreateDirectory($path)|Out-Null}
    return $path
}
function Assert-RemovalPlainPath([string]$Path){
    $current=[IO.Path]::GetFullPath($Path)
    if(!$current.StartsWith($fixture+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'BLOCKED: report path outside fixture'}
    while($current){
        if(Microsoft.PowerShell.Management\Test-Path -LiteralPath $current){if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'fixture path contains junction'}}
        $current=[IO.Path]::GetDirectoryName($current.TrimEnd('\'))
    }
}
function Test-Path {
    [CmdletBinding()]param([string]$LiteralPath,[string]$Path)
    $target=if($LiteralPath){$LiteralPath}else{$Path}
    if($target -match '^(HKLM|HKCU):'){return $false}
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $target
}
$script:tamper=$true;$script:statusUnavailable=$false;$script:statusCall=0;$script:unknownAfter=$false
function Get-MpComputerStatus {
    [CmdletBinding()]param()
    $script:statusCall++
    if($script:statusUnavailable -or ($script:unknownAfter -and $script:statusCall -gt 1)){throw 'fixture status unavailable'}
    return [pscustomobject]@{AntivirusEnabled=$true;RealTimeProtectionEnabled=$true;IsTamperProtected=$script:tamper;AMProductVersion='fixture'}
}
$source=Join-Path $workspaceRoot 'Tweaker\Modules\DefenderRemoval.ps1'
$tokens=$null;$parseErrors=$null
[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$parseErrors) | Out-Null
Check ($parseErrors.Count -eq 0) 'Production Defender module parses under Windows PowerShell'
. $source

$targets=@(Get-DefenderRemovalTargets)
Check (@($targets | Where-Object Kind -eq 'Служба').Count -eq 5) 'Service target list contains exactly five explicit Defender services and drivers'
Check (@($targets | Where-Object {$_.Name -in @('wscsvc','SecurityHealthService','Sense','smartscreen')}).Count -eq 0) 'Shared security center, SmartScreen and enterprise sensor are excluded'
Check (@($targets | Where-Object {$_.Path -match '[*?]'}).Count -eq 0) 'No wildcard driver or directory targets are present'
Check (@($targets | Where-Object {$_.Path -eq (Join-Path ${env:ProgramFiles(x86)} 'Windows Defender')}).Count -eq 1) '32-bit Program Files environment variable is expanded correctly'

$plan=Get-DefenderRemovalPlan
Check (!$plan.CanApply -and $plan.Status.Tamper -eq $true) 'Tamper protection disables native removal planning'
Rejected {Invoke-DefenderRemoval $plan.PlanId} '*Состояние защиты*' 'Execution rechecks tamper protection before any mutation'
Check ($mutations.Count -eq 0) 'Tamper-protected execution never reaches any destructive boundary'
$script:tamper=$false;$script:statusUnavailable=$true
$unknown=Get-DefenderRemovalPlan
Check (!$unknown.CanApply -and !$unknown.Status.Available) 'Unknown protection status disables execution'
Rejected {Invoke-DefenderRemoval $unknown.PlanId} '*Состояние защиты*' 'Unknown protection status is rechecked during execution'
Check ($mutations.Count -eq 0) 'Unknown-state execution never reaches mutation boundaries'
$script:statusUnavailable=$false
Rejected {Invoke-DefenderRemoval '..\outside'} '*идентификатор*' 'Plan traversal is rejected before opening a file'
function Write-Plan([string]$Kind,[datetime]$Created){
    $id=[guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText((Join-Path $store ('RemovalPlans\'+$id+'.json')),(@{Kind=$Kind;CreatedAt=$Created.ToUniversalTime().ToString('o');PlanId=$id}|ConvertTo-Json))
    return $id
}
$id=Write-Plan 'Application' ([DateTime]::UtcNow)
Rejected {Invoke-DefenderRemoval $id} '*тип плана*' 'An application plan cannot authorize Defender removal'
$id=Write-Plan 'Defender' ([DateTime]::UtcNow.AddHours(-25))
Rejected {Invoke-DefenderRemoval $id} '*устарел*' 'Expired plans cannot authorize removal'
$id=Write-Plan 'Defender' ([DateTime]::UtcNow.AddHours(1))
Rejected {Invoke-DefenderRemoval $id} '*устарел*' 'Future-dated plans cannot authorize removal'
$id=Write-Plan 'Defender' ([DateTime]::UtcNow)
[IO.Directory]::CreateDirectory((Join-Path (Get-RemovalDirectory -Machine -Create) ('Defender-'+$id))) | Out-Null
Rejected {Invoke-DefenderRemoval $id} '*уже использован*' 'Consumed plans cannot be replayed'

$tree=Join-Path $fixture 'Plain';$outside=Join-Path $fixture 'Outside'
[IO.Directory]::CreateDirectory($tree)|Out-Null
[IO.Directory]::CreateDirectory($outside)|Out-Null
[IO.File]::WriteAllText((Join-Path $outside 'preserve.txt'),'fixture')
Assert-DefenderPlainTree $tree
Check $true 'A plain fixture tree passes the deletion guard'
$junction=Join-Path $tree 'Junction'
Microsoft.PowerShell.Management\New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
Rejected {Assert-DefenderPlainTree $tree} '*ссылка*' 'Nested junctions stop recursive deletion before mutation'
Rejected {Assert-DefenderPlainTree (Join-Path $junction 'preserve.txt')} '*ссылка*' 'Junction ancestors stop deletion of an individual target'
Check ([IO.File]::Exists((Join-Path $outside 'preserve.txt'))) 'Outside junction target is preserved'

# Replace the target reader for execute tests with one harmless fixture file and
# one fake service. Every privileged operation remains blocked above.
$blockedFile=Join-Path $fixture 'blocked.sys';[IO.File]::WriteAllText($blockedFile,'fixture')
function Get-DefenderRemovalTargets {
    return @([pscustomobject]@{Kind='Служба';Name='WinDefend';Path='HKLM:\fixture\WinDefend';Exists=$true},[pscustomobject]@{Kind='Файлы';Name='blocked.sys';Path=$blockedFile;Exists=$true})
}
$id=Write-Plan 'Defender' ([DateTime]::UtcNow)
$report=Invoke-DefenderRemoval $id
Check (!$report.Complete -and $report.Items.Count -eq 2) 'Partial failure reports every independently attempted target'
Check (@($report.Items|Where-Object {$_.State -eq 'Ошибка' -and $_.Detail -like '*fixture*denied*'}).Count -eq 2) 'Native and filesystem failures retain their original diagnostic details'
Check ([IO.File]::Exists($blockedFile)) 'Denied deletion preserves the fixture file'
Check ([IO.File]::Exists($report.ReportPath)) 'Partial result is persisted to a report file'
$saved=[IO.File]::ReadAllText($report.ReportPath)|ConvertFrom-Json
Check (!$saved.Complete -and $saved.Items.Count -eq 2) 'Persisted report never turns partial failure into success'
Rejected {Invoke-DefenderRemoval $id} '*уже использован*' 'An executed plan cannot be reused after a partial failure'

function Get-DefenderRemovalTargets {return @()}
$script:statusCall=0;$script:unknownAfter=$true
$id=Write-Plan 'Defender' ([DateTime]::UtcNow)
$report=Invoke-DefenderRemoval $id
Check (!$report.Complete) 'Unavailable final status is not treated as verified disabled protection'
Check ($mutations.Count -eq 2) 'Only two mocked destructive calls occurred; no native mutation executed'
$checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\defender-removal-tests.json') -Encoding UTF8
$checks
