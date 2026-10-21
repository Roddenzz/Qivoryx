$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $workspaceRoot ('artifacts\backend-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'Tweaker\Modules') -Destination $fixture -Recurse
$originalLocal=$env:LOCALAPPDATA
$originalTemp=$env:TEMP
$env:LOCALAPPDATA=Join-Path $fixture 'Profile'
$env:TEMP=Join-Path $fixture 'Temp'
New-Item -ItemType Directory -Force $env:LOCALAPPDATA,$env:TEMP | Out-Null
$source=Join-Path $workspaceRoot 'Tweaker\Backend.ps1'
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$checks=New-Object 'System.Collections.Generic.List[string]'
function Check($Condition,$Label){if(!$Condition){throw "FAILED: $Label"};$checks.Add($Label)}
function Request($Data,$Script=$source){
    $output=Join-Path $fixture ([guid]::NewGuid().ToString('N')+'.json')
    $payload=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Data|ConvertTo-Json -Depth 8 -Compress)))
    & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $Script -Request $payload -Output $output
    if($LASTEXITCODE -ne 0){throw 'Backend process failed'}
    return Get-Content -LiteralPath $output -Raw -Encoding utf8 | ConvertFrom-Json
}
try {
    $r=Request @{op='service';name='RpcSs';action='disable'}
    Check (!$r.ok -and $r.message -like '*заблокировано*') 'Critical service rejected before mutation'
    Check (!(Test-Path (Join-Path $env:LOCALAPPDATA 'Qivoryx\Backups'))) 'No backup/write path entered for protected service'
    $r=Request @{op='install';id='Not.In.Catalog'}
    Check (!$r.ok -and $r.message -like '*каталоге*') 'Unknown package rejected before installer execution'
    $r=Request @{op='powerApply';source='AC';values=@{min=100;max=50}}
    Check (!$r.ok -and $r.message -like '*превышать*') 'Contradictory CPU bounds rejected before powercfg mutation'
    $r=Request @{op='restoreService';path=(Join-Path $fixture 'outside.json')}
    Check (!$r.ok -and $r.message -like '*Неверный файл*') 'Restore path traversal rejected'
    # Test rollback with the exact production switch and a substituted native boundary.
    $text=[IO.File]::ReadAllText($source)
    $tokens=$null;$parseErrors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$parseErrors)
    Check ($parseErrors.Count -eq 0) 'Backend parses correctly'
    $native=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Native'},$true)
    $mock=@'
function Native([string]$File,[string[]]$Arguments) {
    ($File+' '+($Arguments -join ' ')) | Add-Content -LiteralPath (Join-Path $store 'native-calls.txt')
    if($Arguments[0] -eq '/getactivescheme'){return 'Power Scheme GUID: 11111111-2222-3333-4444-555555555555 (fixture)'}
    if($Arguments[0] -eq '/setacvalueindex'){throw 'simulated driver rejection'}
    return ''
}
'@
    $mocked=$text.Substring(0,$native.Extent.StartOffset)+$mock+$text.Substring($native.Extent.EndOffset)
    $mockPath=Join-Path $fixture 'Backend.Mocked.ps1'
    [IO.File]::WriteAllText($mockPath,$mocked,(New-Object Text.UTF8Encoding $true))
    $r=Request @{op='powerApply';source='AC';values=@{min=5;max=100}} $mockPath
    Check (!$r.ok -and $r.message -eq 'simulated driver rejection') 'Driver rejection is reported as failure'
    $calls=Get-Content (Join-Path $env:LOCALAPPDATA 'Qivoryx\native-calls.txt')
    Check (@($calls|Where-Object {$_ -eq 'powercfg.exe /setactive 11111111-2222-3333-4444-555555555555'}).Count -eq 1) 'Rollback reactivates the original plan'
    Check (@($calls|Where-Object {$_ -like 'powercfg.exe /delete *'}).Count -eq 1) 'Rollback deletes the failed clone'
    # Cleanup is exercised only against isolated fixture files via a child TEMP.
    $outside=Join-Path $fixture 'Outside';$nested=Join-Path $env:TEMP 'Nested'
    New-Item -ItemType Directory -Force $outside,$nested | Out-Null
    $old=Join-Path $env:TEMP 'old.tmp';$oldNested=Join-Path $nested 'old.tmp';$recent=Join-Path $env:TEMP 'recent.tmp';$keep=Join-Path $outside 'keep.tmp'
    foreach($path in @($old,$oldNested,$recent,$keep)){Set-Content -LiteralPath $path -Value 'fixture' -Encoding utf8}
    foreach($path in @($old,$oldNested,$keep)){(Get-Item -LiteralPath $path).LastWriteTime=(Get-Date).AddDays(-4)}
    $junction=Join-Path $env:TEMP 'Junction'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $r=Request @{op='clean';ids=@('user')}
    Check $r.ok 'Cleanup succeeds in isolated fixture'
    Check (!(Test-Path $old) -and !(Test-Path $oldNested)) 'Cleanup removes old temporary files'
    Check (Test-Path $recent) 'Cleanup preserves fresh files'
    Check (Test-Path $keep) 'Cleanup does not traverse a junction'
    Check ($r.data.Count -eq 2) 'Cleanup reports the actual deleted file count'
    # Exercise the production archive boundary with an attempted zip traversal.
    $expand=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ExpandSafeZip'},$true)
    Invoke-Expression $expand.Extent.Text
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $evilZip=Join-Path $fixture 'traversal.zip'
    $archiveFile=[IO.File]::Create($evilZip);$archive=[IO.Compression.ZipArchive]::new($archiveFile,[IO.Compression.ZipArchiveMode]::Create)
    $entry=$archive.CreateEntry('../escaped.tmp');$writer=[IO.StreamWriter]::new($entry.Open());$writer.Write('must not escape');$writer.Dispose();$archive.Dispose();$archiveFile.Dispose()
    $blocked=$false
    try {ExpandSafeZip $evilZip (Join-Path $fixture 'Unpack')}catch{$blocked=$_.Exception.Message -like '*вне папки*'}
    Check $blocked 'Fallback ZIP rejects path traversal'
    Check (!(Test-Path (Join-Path $fixture 'escaped.tmp'))) 'ZIP cannot write outside the extraction root'
    # Fake network bytes: an installer with a bad hash must never reach Native.
    $boundary=@'
function Get-Command([string]$Name){return $null}
function Invoke-WebRequest($Uri,[string]$OutFile,[switch]$UseBasicParsing,[int]$TimeoutSec){[IO.File]::WriteAllBytes($OutFile,[byte[]]@(1,2,3,4))}
'@
    $installMock=[regex]::Replace($mocked, '(?m)^try \{\r?\n    \$data=', [Text.RegularExpressions.MatchEvaluator]{param($match) $boundary+"`n"+$match.Value})
    Check ($installMock.Contains('function Invoke-WebRequest')) 'Network boundary is substituted for the installer test'
    [IO.File]::WriteAllText($mockPath,$installMock,(New-Object Text.UTF8Encoding $true))
    @(@{Id='Fixture.Package';Name='Fixture';Url='https://example.test/setup.exe';Extension='exe';SHA256=('0'*64);Args=@('/S');InstallerType='nullsoft'}) | ConvertTo-Json -AsArray | Set-Content (Join-Path $fixture 'catalog.json') -Encoding utf8
    $r=Request @{op='install';id='Fixture.Package'} $mockPath
    Check (!$r.ok -and $r.message -like '*SHA-256*') 'A modified installer is rejected before execution'
    $checks | ConvertTo-Json | Set-Content (Join-Path $workspaceRoot 'artifacts\backend-tests.json') -Encoding utf8
    $checks
}
finally {$env:LOCALAPPDATA=$originalLocal;$env:TEMP=$originalTemp}
