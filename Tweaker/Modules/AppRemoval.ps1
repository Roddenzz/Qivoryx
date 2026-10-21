# Native application removal adapted from sys.ps1. Console prompts, broad
# publisher matching, shell command execution and ignored failures are omitted.
function Get-RemovalHash([string]$Value) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try {return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)))).Replace('-','').ToLowerInvariant()}
    finally {$sha.Dispose()}
}
function Assert-RemovalPlainPath([string]$Path,[switch]$Tree) {
    if(!$Path -or $Path -notmatch '^[A-Za-z]:\\' -or $Path.Substring(2).Contains(':')){throw 'Ожидался локальный абсолютный путь.'}
    $current=[IO.Path]::GetFullPath($Path)
    while($current){
        if(Test-Path -LiteralPath $current){if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Путь содержит ссылку. Удаление отменено.'}}
        $next=[IO.Path]::GetDirectoryName($current.TrimEnd('\'));if($next -eq $current){break};$current=$next
    }
    if($Tree -and (Test-Path -LiteralPath $Path)){
        $pending=New-Object 'System.Collections.Generic.Stack[string]';$pending.Push($Path)
        while($pending.Count){foreach($child in @(Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop)){
            if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Внутри папки найдена ссылка. Удаление отменено.'}
            if($child.PSIsContainer){$pending.Push($child.FullName)}
        }}
    }
}
function Get-RemovalDirectory([switch]$Machine,[switch]$Create) {
    $dir=if($Machine){Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'Qivoryx\RemovalPlans'}else{Join-Path $store 'RemovalPlans'}
    Assert-RemovalPlainPath $dir
    if($Machine){
        $parent=[IO.Path]::GetDirectoryName($dir)
        foreach($path in @($parent,$dir)){
            if(!(Test-Path -LiteralPath $path)){
                if(!$Create){return $null}
                # Create the directory with an admin-owned, read-only-for-users ACL.
                $acl=New-StartupMachineAcl
                [IO.Directory]::CreateDirectory($path,$acl) | Out-Null
            }
            if(!(Test-StartupMachineAcl (Get-Acl -LiteralPath $path))){throw 'Небезопасные разрешения папки плана удаления. Операция отменена.'}
        }
    }elseif($Create){New-Item -ItemType Directory -Path $dir -Force | Out-Null}
    return $dir
}
function Write-RemovalPlan($Plan,[switch]$Trusted) {
    $machine=($Trusted -and $Plan.Scope -eq 'Компьютер')
    $dir=Get-RemovalDirectory -Machine:$machine -Create
    $path=Join-Path $dir ($Plan.PlanId+'.json')
    Assert-RemovalPlainPath $path
    $Plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
    if($machine){Set-Acl -LiteralPath $path -AclObject (New-StartupMachineAcl -File)}
}
function Read-RemovalPlan([string]$PlanId,[switch]$Trusted) {
    if($PlanId -notmatch '^[a-f0-9]{32}$'){throw 'Неверный идентификатор плана удаления.'}
    $dir=Get-RemovalDirectory
    $path=Join-Path $dir ($PlanId+'.json')
    if($Trusted){
        $machineDir=Get-RemovalDirectory -Machine
        if($machineDir -and (Test-Path -LiteralPath (Join-Path $machineDir ($PlanId+'.json')))){
            $path=Join-Path $machineDir ($PlanId+'.json')
            if(!(Test-StartupMachineAcl (Get-Acl -LiteralPath $path) -File)){throw 'Небезопасные разрешения файла плана удаления.'}
        }
    }
    Assert-RemovalPlainPath $path
    if(!(Test-Path -LiteralPath $path)){throw 'План удаления не найден. Повторите предварительный просмотр.'}
    $plan=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if($plan.PlanId -ne $PlanId -or $plan.Version -ne 1 -or ([datetime]::UtcNow-[datetime]$plan.CreatedAt).TotalHours -gt 24){throw 'План удаления устарел. Повторите предварительный просмотр.'}
    if($Trusted -and (!$plan.UninstallVerified -or ($plan.Scope -eq 'Компьютер' -and !$path.StartsWith($machineDir+'\',[StringComparison]::OrdinalIgnoreCase)))){throw 'Нет подтверждённого результата удаления. Очистка остатков заблокирована.'}
    return $plan
}
function Get-RemovalIdentity($App) {
    $raw=if($App.Type -eq 'Win32'){Get-ItemProperty -LiteralPath $App.Id -ErrorAction Stop}else{$null}
    return Get-RemovalHash ($App.Id+"`0"+$App.Name+"`0"+$App.Version+"`0"+$raw.UninstallString+"`0"+$raw.QuietUninstallString+"`0"+$raw.InstallLocation)
}
function Get-RemovalRoots([string]$Scope) {
    $roots=@($env:APPDATA,$env:LOCALAPPDATA)
    if($Scope -eq 'Компьютер'){$roots+=@($env:ProgramFiles,${env:ProgramFiles(x86)},$env:ProgramData)}
    return @($roots | Where-Object {$_} | ForEach-Object {[IO.Path]::GetFullPath($_).TrimEnd('\')} | Select-Object -Unique)
}
function Test-RemovalName([string]$Name) {
    return ($Name.Length -ge 3 -and $Name.Length -le 120 -and $Name -notmatch '[\\/:*?"<>|]' -and $Name -notmatch '^\.+$' -and $Name -notmatch '^(?i:Microsoft|Windows|Windows Defender|WindowsApps|Packages|Programs|Temp|Common Files|Common|Shared|System|System32|Drivers|Qivoryx|Documents|Desktop|Downloads|AppData|Software|WOW6432Node)$')
}
function Test-RemovalCandidate($Plan,$Candidate) {
    if(!(Test-RemovalName $Plan.Name)){return $false}
    if(@(Apps | Where-Object {$_.Id -ne $Plan.Id -and $_.Name -eq $Plan.Name}).Count){return $false}
    if($Candidate.Kind -eq 'Directory'){
        try {$path=[IO.Path]::GetFullPath([string]$Candidate.Path).TrimEnd('\');Assert-RemovalPlainPath $path}catch{return $false}
        $allowed=$false
        foreach($root in @(Get-RemovalRoots $Plan.Scope)){
            if($path.Equals((Join-Path $root $Plan.Name),[StringComparison]::OrdinalIgnoreCase)){$allowed=$true;break}
        }
        if(!$allowed){return $false}
        # A folder still owned by another installed app must never be a residual.
        foreach($other in @(Apps | Where-Object {$_.Type -eq 'Win32' -and $_.Id -ne $Plan.Id})){
            $reg=Get-ItemProperty -LiteralPath $other.Id -ErrorAction SilentlyContinue
            if($reg.InstallLocation){
                try {$install=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($reg.InstallLocation)).TrimEnd('\')}catch{continue}
                if($install.Equals($path,[StringComparison]::OrdinalIgnoreCase) -or $install.StartsWith($path+'\',[StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase)){return $false}
            }
        }
        return $true
    }
    if($Candidate.Kind -eq 'Registry'){
        $roots=@('HKCU:\Software');if($Plan.Scope -eq 'Компьютер'){$roots+=@('HKLM:\SOFTWARE','HKLM:\SOFTWARE\WOW6432Node')}
        return (@($roots | Where-Object {($Candidate.Path -ieq ($_+'\'+$Plan.Name))}).Count -eq 1)
    }
    return $false
}
function Get-RemovalCandidates($Plan) {
    if($Plan.Type -ne 'Win32' -or !(Test-RemovalName $Plan.Name)){return @()}
    $candidates=New-Object 'System.Collections.Generic.List[object]'
    foreach($root in @(Get-RemovalRoots $Plan.Scope)){
        $path=Join-Path $root $Plan.Name
        $item=[pscustomobject]@{Id=(Get-RemovalHash ('Directory|'+$path));Kind='Directory';Path=$path;Label='Папка приложения и его данные'}
        if((Test-Path -LiteralPath $path) -and (Test-RemovalCandidate $Plan $item)){$candidates.Add($item)}
    }
    $registry=@('HKCU:\Software');if($Plan.Scope -eq 'Компьютер'){$registry+=@('HKLM:\SOFTWARE','HKLM:\SOFTWARE\WOW6432Node')}
    foreach($root in $registry){
        $path=$root+'\'+$Plan.Name
        if(Test-Path -LiteralPath $path){$candidates.Add([pscustomobject]@{Id=(Get-RemovalHash ('Registry|'+$path));Kind='Registry';Path=$path;Label='Настройки приложения (реестр)'})}
    }
    return $candidates.ToArray()
}
function Get-RemovalCommand($App) {
    $reg=Get-ItemProperty -LiteralPath $App.Id -ErrorAction Stop
    if($reg.WindowsInstaller -eq 1){
        if($reg.PSChildName -notmatch '^\{[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}$'){throw 'Неверный MSI ProductCode.'}
        return [pscustomobject]@{File=(Join-Path $env:WINDIR 'System32\msiexec.exe');Arguments=('/x '+$reg.PSChildName+' /qn /norestart');Quiet=$true;Mode='MSI / тихое удаление'}
    }
    $quiet=[bool]$reg.QuietUninstallString
    $command=if($quiet){[string]$reg.QuietUninstallString}else{[string]$reg.UninstallString}
    $command=[Environment]::ExpandEnvironmentVariables($command).Trim()
    if($command -match '^"([^"\r\n]+\.exe)"(?:\s+(.*))?$'){$exe=$Matches[1];$arguments=$Matches[2]}
    elseif($command -match '^([^\s"\r\n]+\.exe)(?:\s+(.*))?$'){$exe=$Matches[1];$arguments=$Matches[2]}
    else {throw 'Неоднозначная команда удаления. Запустите деинсталлятор через Параметры Windows.'}
    if(![IO.Path]::IsPathRooted($exe) -or $exe.StartsWith('\\') -or [IO.Path]::GetFileName($exe) -match '^(?i:cmd|powershell|pwsh|wscript|cscript|mshta|rundll32|regsvr32|msiexec)\.exe$' -or $arguments -match '[\r\n]'){throw 'Команда использует оболочку или неподдерживаемый обработчик. Автоматический запуск заблокирован.'}
    Assert-RemovalPlainPath $exe
    if(!(Test-Path -LiteralPath $exe -PathType Leaf)){throw 'Зарегистрированный деинсталлятор не найден.'}
    return [pscustomobject]@{File=$exe;Arguments=[string]$arguments;Quiet=$quiet;Mode=$(if($quiet){'EXE / штатная тихая команда'}else{'EXE / диалог деинсталлятора'})}
}
function Get-AppRemovalPlan([string]$Id) {
    $app=@(Apps | Where-Object Id -eq $Id) | Select-Object -First 1
    if(!$app){throw 'Приложение больше не найдено. Обновите список.'}
    $warnings=New-Object 'System.Collections.Generic.List[string]'
    $can=[bool]$app.Removable;$mode='';$quiet=[bool]$app.Quiet
    if($can -and $app.Type -eq 'Win32'){
        try {$command=Get-RemovalCommand $app;$mode=$command.Mode;$quiet=$command.Quiet}catch{$can=$false;$warnings.Add($_.Exception.Message)}
    }elseif($app.Type -eq 'Appx'){$mode='Удаление пакета текущего пользователя'}elseif($app.Type -eq 'ManagedPortable'){$mode='Удаление управляемого portable-пакета'}
    if(!$app.Removable){$warnings.Add('Windows не предоставляет способ удаления этого компонента.')}
    if(!$quiet -and $can){$warnings.Add('Для приложения нет штатной тихой команды. Может открыться окно деинсталлятора.')}
    $warnings.Add('Очистка личных настроек и файлов выполняется отдельно: только выбранные цели после подтверждённого удаления. Поиск по издателю не используется.')
    $plan=[pscustomobject]@{Version=1;PlanId=[guid]::NewGuid().ToString('N');CreatedAt=[datetime]::UtcNow.ToString('o');Id=$app.Id;Name=$app.Name;Type=$app.Type;Scope=$app.Scope;Quiet=$quiet;CanRemove=$can;CommandMode=$mode;Identity=(Get-RemovalIdentity $app);Steps=@('Проверка актуальной записи приложения','Запуск зарегистрированного деинсталлятора','Проверка исчезновения записи','Выбор и отдельная очистка остатков');Candidates=@();Warnings=@($warnings);UninstallVerified=$false}
    $plan.Candidates=@(Get-RemovalCandidates $plan)
    Write-RemovalPlan $plan
    return $plan
}
function Invoke-RemovalProcess($Command) {
    $si=New-Object Diagnostics.ProcessStartInfo
    $si.FileName=$Command.File;$si.Arguments=$Command.Arguments;$si.UseShellExecute=$false;$si.CreateNoWindow=$Command.Quiet
    $p=[Diagnostics.Process]::Start($si)
    if(!$p){throw 'Деинсталлятор не запустился.'}
    try {$p.WaitForExit();return [int]$p.ExitCode}finally{$p.Dispose()}
}
function Invoke-AppRemoval([string]$PlanId) {
    $plan=Read-RemovalPlan $PlanId
    $app=@(Apps | Where-Object Id -eq $plan.Id) | Select-Object -First 1
    if(!$app -or !$app.Removable -or (Get-RemovalIdentity $app) -ne $plan.Identity){throw 'Запись приложения изменилась. Повторите предварительный просмотр.'}
    if($app.Type -ne $plan.Type -or $app.Name -ne $plan.Name -or $app.Scope -ne $plan.Scope){throw 'План не соответствует приложению.'}
    if($app.Scope -eq 'Компьютер'){Get-RemovalDirectory -Machine -Create | Out-Null}
    $code=0
    if($app.Type -eq 'Win32'){
        $command=Get-RemovalCommand $app
        $code=Invoke-RemovalProcess $command
        if($code -notin @(0,3010,1641)){throw "Деинсталлятор завершился с кодом $code. Очистка остатков не выполнялась."}
    }elseif($app.Type -eq 'Appx'){
        Remove-AppxPackage -Package $app.Id -ErrorAction Stop
    }elseif($app.Type -eq 'ManagedPortable'){
        $root=[IO.Path]::GetFullPath((Join-Path $store 'Programs')).TrimEnd('\')
        $target=[IO.Path]::GetFullPath($app.Id).TrimEnd('\')
        if([IO.Path]::GetDirectoryName($target) -ine $root){throw 'Неверная папка portable-пакета.'}
        Assert-RemovalPlainPath $target -Tree
        $marker=Join-Path $target '.qivoryx-package.json'
        if(!(Test-Path -LiteralPath $marker -PathType Leaf)){throw 'Не найден маркер управляемого пакета.'}
        $package=Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json
        if($package.Id -notmatch '^[A-Za-z0-9_.-]+$'){throw 'Некорректный идентификатор пакета.'}
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
        $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) ('Qivoryx Tools\'+$package.Id+'.lnk')
        Assert-RemovalPlainPath $shortcut
        if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut -Force -ErrorAction Stop}
    }else{throw 'Неподдерживаемый тип приложения.'}
    $remaining=@(Apps | Where-Object Id -eq $app.Id)
    $removed=($remaining.Count -eq 0)
    $plan.UninstallVerified=$removed
    # The elevated removal result owns the authoritative machine-scope plan;
    # cleanup never trusts a user-writable snapshot for machine targets.
    $cleanupReady=$false;$saveError=''
    if($removed){try {Write-RemovalPlan $plan -Trusted;$cleanupReady=$true}catch{$saveError=$_.Exception.Message}}
    $left=@();if($cleanupReady){$left=@($plan.Candidates | Where-Object {Test-Path -LiteralPath $_.Path})}
    $message=if($removed){'Запись приложения удалена. Остатки можно проверить и очистить отдельно.'}else{'Деинсталлятор завершён, но запись приложения осталась. Очистка остатков заблокирована; может требоваться перезагрузка.'}
    if($saveError){$message='Запись приложения удалена, но не удалось сохранить подтверждение для очистки остатков: '+$saveError+'. Очистка заблокирована.'}
    return [pscustomobject]@{PlanId=$PlanId;Name=$app.Name;Removed=$removed;CleanupReady=$cleanupReady;RestartRequired=($code -in @(3010,1641));ExitCode=$code;Message=$message;Candidates=$left;Steps=@('Деинсталлятор завершён','Наличие записи проверено')}
}
function Invoke-AppResidualCleanup([string]$PlanId,[string[]]$Ids) {
    $plan=Read-RemovalPlan $PlanId -Trusted
    if(@(Apps | Where-Object Id -eq $plan.Id).Count){throw 'Приложение всё ещё установлено. Очистка остатков заблокирована.'}
    $selected=@($Ids | Select-Object -Unique)
    if(!$selected.Count){throw 'Не выбраны остатки для очистки.'}
    if(@($selected | Where-Object {$_ -notin @($plan.Candidates.Id)}).Count){throw 'В списке есть цели вне предварительного просмотра.'}
    # Validate every selected target before the first deletion.
    foreach($candidate in @($plan.Candidates | Where-Object Id -in $selected)){
        if(!(Test-RemovalCandidate $plan $candidate)){throw 'Цель больше не принадлежит плану или используется другим приложением. Повторите проверку.'}
        if($candidate.Kind -eq 'Directory' -and (Test-Path -LiteralPath $candidate.Path)){Assert-RemovalPlainPath $candidate.Path -Tree}
    }
    $deleted=New-Object 'System.Collections.Generic.List[string]';$failed=New-Object 'System.Collections.Generic.List[object]'
    foreach($candidate in @($plan.Candidates | Where-Object Id -in $selected)){
        try {
            if(!(Test-Path -LiteralPath $candidate.Path)){continue}
            if($candidate.Kind -eq 'Registry'){
                $backup=Join-Path (Get-RemovalDirectory -Machine:($plan.Scope -eq 'Компьютер') -Create) ($PlanId+'-'+$candidate.Id+'.reg')
                Assert-RemovalPlainPath $backup
                Native (Join-Path $env:WINDIR 'System32\reg.exe') @('export',($candidate.Path.Replace('HKCU:\','HKCU\').Replace('HKLM:\','HKLM\')),$backup,'/y') | Out-Null
                if(!(Test-Path -LiteralPath $backup)){throw 'Не создана резервная копия реестра.'}
            }
            Remove-Item -LiteralPath $candidate.Path -Recurse -Force -ErrorAction Stop
            if(Test-Path -LiteralPath $candidate.Path){throw 'Цель осталась после удаления.'}
            $deleted.Add([string]$candidate.Path)
        }catch{$failed.Add([pscustomobject]@{Path=$candidate.Path;Error=$_.Exception.Message})}
    }
    return [pscustomobject]@{PlanId=$PlanId;Name=$plan.Name;Deleted=@($deleted);Failed=$failed.ToArray();Message=('Удалено целей: '+$deleted.Count+'. Ошибок: '+$failed.Count+'. Личные данные папок автоматически не восстанавливаются.')}
}
