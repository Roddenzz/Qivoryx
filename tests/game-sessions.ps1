$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$sdk=Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)){$sdk=(Get-Command dotnet -ErrorAction Stop).Source}
& $sdk run --project (Join-Path $PSScriptRoot 'GameSessions\GameSessions.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Game-session tests failed'}
