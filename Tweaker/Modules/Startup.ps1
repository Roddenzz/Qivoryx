# Startup entries are data. No command found here is ever executed.
function Get-StartupSources {
    @(
        [pscustomobject]@{Key='user-run';Kind='Registry';Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';Scope='Пользователь';Admin=$false;Approved='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'}
        [pscustomobject]@{Key='machine-run';Kind='Registry';Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\Run';Scope='Все пользователи';Admin=$true;Approved='HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'}
        [pscustomobject]@{Key='machine-run32';Kind='Registry';Path='HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run';Scope='Все пользователи (x86)';Admin=$true;Approved='HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32'}
        [pscustomobject]@{Key='user-folder';Kind='Folder';Path=[Environment]::GetFolderPath('Startup');Scope='Пользователь';Admin=$false;Approved='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'}
        [pscustomobject]@{Key='machine-folder';Kind='Folder';Path=[Environment]::GetFolderPath('CommonStartup');Scope='Все пользователи';Admin=$true;Approved='HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'}
    )
}
function Get-StartupId([string]$Source,[string]$Name,[string]$Command) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Source+"`0"+$Name+"`0"+$Command)))).Replace('-','').ToLowerInvariant() }
    finally {$sha.Dispose()}
}
function Test-StartupProtected([string]$Name,[string]$Command) {
    return (($Name+' '+$Command) -match '(?i)(SecurityHealth|Windows.?Defender|MsMpEng|smartscreen|Windows Security|ESET|avast|Bitdefender|Kaspersky|Malwarebytes|Norton|Symantec|Avira|McAfee|DrWeb|Trend.?Micro|Sophos|SentinelOne|CrowdStrike|FortiClient)')
}
function Assert-StartupPlainPath([string]$Path) {
    $current=[IO.Path]::GetFullPath($Path)
    while($current){
        if(Test-Path -LiteralPath $current){
            $item=Get-Item -LiteralPath $current -Force
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Путь автозагрузки содержит ссылку. Операция отменена.'}
        }
        $parent=[IO.Path]::GetDirectoryName($current.TrimEnd('\'))
        if($parent -eq $current){break};$current=$parent
    }
}
function Get-StartupDirectory {
    $dir=Join-Path $store 'Startup'
    Assert-StartupPlainPath $dir
    return $dir
}
function Get-StartupMachinePath {
    $parent=[Environment]::GetFolderPath('CommonApplicationData')
    $legacy=Join-Path $parent 'Monochrome\StartupMachine'
    if(Test-Path -LiteralPath $legacy){return $legacy}
    return Join-Path $parent 'Qivoryx\StartupMachine'
}
function New-StartupMachineAcl([switch]$File) {
    $acl=if($File){New-Object Security.AccessControl.FileSecurity}else{New-Object Security.AccessControl.DirectorySecurity}
    $admins=New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'
    $system=New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'
    $users=New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-545'
    $inherit=if($File){[Security.AccessControl.InheritanceFlags]::None}else{[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'}
    $acl.SetOwner($admins);$acl.SetAccessRuleProtection($true,$false)
    foreach($sid in @($admins,$system)){$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)))}
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users,[Security.AccessControl.FileSystemRights]::ReadAndExecute,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)))
    return $acl
}
function Test-StartupMachineAcl($Acl,[switch]$File) {
    if(!$Acl.AreAccessRulesProtected -or $Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @('S-1-5-32-544','S-1-5-18')){return $false}
    $found=@{}
    $inherit=if($File){[Security.AccessControl.InheritanceFlags]::None}else{[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'}
    foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        if($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $rule.IsInherited -or $rule.InheritanceFlags -ne $inherit -or $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None){return $false}
        $sid=$rule.IdentityReference.Value
        switch($sid){
            {$_ -in @('S-1-5-32-544','S-1-5-18')} {if(($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl){return $false};$found[$sid]=$true}
            'S-1-5-32-545' {
                # Synchronize is added by Windows to allow rules; no write,
                # delete, ownership or ACL rights may appear for Users.
                $allowed=[Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize
                if(($rule.FileSystemRights -band (-bnot [int]$allowed)) -ne 0 -or ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::ReadAndExecute) -ne [Security.AccessControl.FileSystemRights]::ReadAndExecute){return $false};$found[$sid]=$true
            }
            default {return $false}
        }
    }
    return ($found.Count -eq 3)
}
function Get-StartupMachineDirectory([switch]$Create) {
    $dir=Get-StartupMachinePath
    $parent=[IO.Path]::GetDirectoryName($dir)
    foreach($path in @($parent,$dir)){
        Assert-StartupPlainPath $path
        if(!(Test-Path -LiteralPath $path)){
            if(!$Create){return $null}
            $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
            $principal=New-Object Security.Principal.WindowsPrincipal $identity
            if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Для сохранения общей автозагрузки нужны права администратора.'}
            # Create with a protected ACL atomically; never repair/trust a
            # precreated user-owned directory or inherited writable ACL.
            [IO.Directory]::CreateDirectory($path,(New-StartupMachineAcl)) | Out-Null
        }
        $item=Get-Item -LiteralPath $path -Force
        if(!$item.PSIsContainer -or !(Test-StartupMachineAcl ($item.GetAccessControl()))){throw 'Папка системных копий автозагрузки имеет небезопасного владельца или права. Операция отменена.'}
    }
    return $dir
}
function Open-StartupMachineOutput([string]$Path,[switch]$Existing) {
    $dir=Get-StartupMachineDirectory
    if(!$dir -or [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path)) -ne $dir){throw 'Недопустимый путь системной копии автозагрузки.'}
    Assert-StartupPlainPath $Path
    if($Existing){
        if(!(Test-StartupMachineAcl ((Get-Item -LiteralPath $Path).GetAccessControl()) -File)){throw 'Небезопасные права файла системной копии.'}
        return [IO.File]::Open($Path,[IO.FileMode]::Truncate,[IO.FileAccess]::Write,[IO.FileShare]::None)
    }
    return [IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(New-StartupMachineAcl -File))
}
function Write-StartupRecord([string]$Path,$Value,[bool]$Machine) {
    $json=$Value | ConvertTo-Json -Depth 5
    if(!$Machine){$json | Set-Content -LiteralPath $Path -Encoding UTF8;return}
    $stream=Open-StartupMachineOutput $Path -Existing:(Test-Path -LiteralPath $Path)
    try {$bytes=(New-Object Text.UTF8Encoding $true).GetBytes($json);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Copy-StartupMachineFile([string]$Original,[string]$Saved) {
    $inputFile=[IO.File]::Open($Original,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        $outputFile=Open-StartupMachineOutput $Saved
        try {$inputFile.CopyTo($outputFile);$outputFile.Flush($true)}finally{$outputFile.Dispose()}
    }finally{$inputFile.Dispose()}
}
function Read-StartupBackup([string]$Id,[bool]$Machine=$false) {
    if($Id -notmatch '^[0-9a-f]{32}$'){throw 'Неверный идентификатор копии автозагрузки.'}
    $dir=if($Machine){Get-StartupMachineDirectory}else{Get-StartupDirectory}
    if(!$dir){throw 'Копия автозагрузки не найдена.'}
    $path=Join-Path $dir ($Id+'.json')
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw 'Копия автозагрузки не найдена.'}
    Assert-StartupPlainPath $path
    if($Machine -and !(Test-StartupMachineAcl ((Get-Item -LiteralPath $path).GetAccessControl()) -File)){throw 'Небезопасные права системной копии автозагрузки.'}
    $b=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if($b.Version -ne 1 -or $b.Id -ne $Id -or $b.Status -ne 'Disabled'){throw 'Копия автозагрузки недоступна для восстановления.'}
    if($b.Computer -ne $env:COMPUTERNAME -or (!$Machine -and $b.UserSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)){throw 'Копия автозагрузки создана для другого компьютера или профиля.'}
    $source=@(Get-StartupSources | Where-Object Key -eq $b.SourceKey) | Select-Object -First 1
    if(!$source -or $b.Kind -ne $source.Kind -or !$b.Name -or ([string]$b.Name).Contains([char]0)){throw 'Неверный источник копии автозагрузки.'}
    if([bool]$source.Admin -ne $Machine){throw 'Общая автозагрузка восстанавливается только из защищённой системной копии.'}
    if($source.Kind -eq 'Registry' -and $b.ValueKind -notin @('String','ExpandString')){throw 'Неверный тип значения автозагрузки.'}
    if($source.Kind -eq 'Folder' -and ([IO.Path]::GetFileName([string]$b.Name) -ne $b.Name -or $b.Name -in @('.','..','desktop.ini'))){throw 'Неверное имя файла автозагрузки.'}
    if(Test-StartupProtected $b.Name $b.Command){throw 'Восстановление записи безопасности через менеджер автозагрузки запрещено.'}
    return $b
}
function Get-StartupEntries {
    $entries=New-Object 'System.Collections.Generic.List[object]'
    foreach($source in @(Get-StartupSources)){
        if(!$source.Path -or !(Test-Path -LiteralPath $source.Path)){continue}
        if($source.Kind -eq 'Registry'){
            $key=Get-Item -LiteralPath $source.Path -ErrorAction Stop
            foreach($name in $key.GetValueNames()){
                if(!$name){continue}
                $kind=$key.GetValueKind($name).ToString()
                if($kind -notin @('String','ExpandString')){continue}
                $command=[string]$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $protected=Test-StartupProtected $name $command
                $windowsDisabled=$false
                if(Test-Path -LiteralPath $source.Approved){$approved=(Get-Item -LiteralPath $source.Approved).GetValue($name);if($approved -is [byte[]] -and $approved.Length -ge 1){$windowsDisabled=$approved[0] -in @(3,7)}}
                $entries.Add([pscustomobject]@{Id=(Get-StartupId $source.Key $name $command);Name=$name;Command=$command;Scope=$source.Scope;Source='Реестр Run';StateLabel=$(if($windowsDisabled){'Отключено в Windows'}else{'Запись присутствует'});RequiresAdmin=$source.Admin;Protected=$protected;CanDisable=(!$protected -and !$windowsDisabled);SourceKey=$source.Key;Kind=$source.Kind;ValueKind=$kind;WindowsDisabled=$windowsDisabled})
            }
        }else{
            foreach($file in @(Get-ChildItem -LiteralPath $source.Path -Force -File -ErrorAction SilentlyContinue | Where-Object Name -ne 'desktop.ini')){
                $command=$file.FullName
                if($file.Extension -ieq '.lnk'){
                    try {$shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut($file.FullName);$command=($link.TargetPath+' '+$link.Arguments).Trim();[Runtime.InteropServices.Marshal]::ReleaseComObject($link) | Out-Null;[Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null}catch{$command=$file.FullName}
                }
                $protected=Test-StartupProtected $file.Name $command
                $windowsDisabled=$false
                if(Test-Path -LiteralPath $source.Approved){$approved=(Get-Item -LiteralPath $source.Approved).GetValue($file.Name);if($approved -is [byte[]] -and $approved.Length -ge 1){$windowsDisabled=$approved[0] -in @(3,7)}}
                $entries.Add([pscustomobject]@{Id=(Get-StartupId $source.Key $file.Name $command);Name=$file.BaseName;Command=$command;Scope=$source.Scope;Source='Папка Startup';StateLabel=$(if($windowsDisabled){'Отключено в Windows'}else{'Запись присутствует'});RequiresAdmin=$source.Admin;Protected=$protected;CanDisable=(!$protected -and !$windowsDisabled -and !($file.Attributes -band [IO.FileAttributes]::ReparsePoint));SourceKey=$source.Key;Kind=$source.Kind;FileName=$file.Name;WindowsDisabled=$windowsDisabled})
            }
        }
    }
    $disabled=New-Object 'System.Collections.Generic.List[object]'
    foreach($machine in @($false,$true)){
        try {$dir=if($machine){Get-StartupMachineDirectory}else{Get-StartupDirectory}}catch{if($notes){$notes.Add($_.Exception.Message)};continue}
        if(!$dir -or !(Test-Path -LiteralPath $dir)){continue}
        foreach($file in @(Get-ChildItem -LiteralPath $dir -Filter '*.json' -File)){
            try {
                $b=Read-StartupBackup $file.BaseName $machine
                $source=@(Get-StartupSources | Where-Object Key -eq $b.SourceKey)[0]
                $disabled.Add([pscustomobject]@{Id=$b.Id;Name=$b.DisplayName;Command=$b.Command;Scope=$source.Scope;Source=$(if($b.Kind -eq 'Registry'){'Реестр Run'}else{'Папка Startup'});StateLabel='Отключено Qivoryx';RequiresAdmin=$source.Admin;Protected=$false;CanDisable=$false;DisabledAt=$b.Created})
            }catch{if($notes){$notes.Add('Пропущена недоступная копия автозагрузки: '+$file.Name)}}
        }
    }
    return @{Entries=@($entries | Sort-Object Name,Scope);Disabled=@($disabled | Sort-Object Name,Scope)}
}
function Disable-StartupEntry([string]$Id) {
    if($Id -notmatch '^[0-9a-f]{64}$'){throw 'Неверный идентификатор автозагрузки.'}
    $entry=@((Get-StartupEntries).Entries | Where-Object Id -eq $Id) | Select-Object -First 1
    if(!$entry){throw 'Запись изменилась или исчезла. Обновите список.'}
    if(!$entry.CanDisable -or $entry.Protected){throw 'Эта запись защищена или уже отключена в Windows.'}
    $source=@(Get-StartupSources | Where-Object Key -eq $entry.SourceKey)[0]
    $dir=if($source.Admin){Get-StartupMachineDirectory -Create}else{Get-StartupDirectory}
    if(!$source.Admin){New-Item -ItemType Directory -Force -Path $dir | Out-Null}
    $backupId=[guid]::NewGuid().ToString('N');$path=Join-Path $dir ($backupId+'.json')
    # Persist a usable recovery record before changing its source. If the process
    # is interrupted afterwards, restoration still validates the original target.
    $b=[ordered]@{Version=1;Id=$backupId;Status='Disabled';Created=(Get-Date -Format o);Computer=$env:COMPUTERNAME;UserSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;Kind=$source.Kind;SourceKey=$source.Key;Name=$entry.Name;DisplayName=$entry.Name;Command=$entry.Command}
    if($source.Kind -eq 'Registry'){
        $b.ValueKind=$entry.ValueKind
        Write-StartupRecord $path $b $source.Admin
        $key=Get-Item -LiteralPath $source.Path
        # Detect an app updating its command between the inventory and mutation.
        $current=[string]$key.GetValue($entry.Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if((Get-StartupId $source.Key $entry.Name $current) -ne $Id -or $key.GetValueKind($entry.Name).ToString() -ne $entry.ValueKind){throw 'Запись обновлена другим приложением. Операция отменена.'}
        Remove-ItemProperty -LiteralPath $source.Path -Name $entry.Name -ErrorAction Stop
    }else{
        $b.Name=$entry.FileName
        $original=Join-Path $source.Path $entry.FileName
        Assert-StartupPlainPath $original
        $b.SHA256=(Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
        Write-StartupRecord $path $b $source.Admin
        $saved=Join-Path $dir ($backupId+'.startup')
        if($source.Admin){
            Copy-StartupMachineFile $original $saved
            if((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ne $b.SHA256 -or (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $b.SHA256){throw 'Файл автозагрузки изменился во время копирования. Исходный файл сохранён.'}
            Remove-Item -LiteralPath $original -ErrorAction Stop
        }else{Move-Item -LiteralPath $original -Destination $saved -ErrorAction Stop}
    }
    $b.Status='Disabled';Write-StartupRecord $path $b $source.Admin
    return @{Id=$backupId;Message='Автозапуск отключён. Программа продолжит работать в текущем сеансе; следующий вход будет без её автоматического запуска.'}
}
function Restore-StartupEntry([string]$Id) {
    if($Id -notmatch '^[0-9a-f]{32}$'){throw 'Неверный идентификатор копии автозагрузки.'}
    $userDir=Get-StartupDirectory
    if(Test-Path -LiteralPath (Join-Path $userDir ($Id+'.json'))){$b=Read-StartupBackup $Id}else{$b=Read-StartupBackup $Id $true}
    $source=@(Get-StartupSources | Where-Object Key -eq $b.SourceKey)[0]
    $backupDir=if($source.Admin){Get-StartupMachineDirectory}else{Get-StartupDirectory}
    if($source.Kind -eq 'Registry'){
        $key=Get-Item -LiteralPath $source.Path -ErrorAction SilentlyContinue
        if($key -and $b.Name -in @($key.GetValueNames())){throw 'Запись уже существует. Чтобы сохранить новую конфигурацию программы, восстановление отменено.'}
        if(!$key){New-Item -Path $source.Path -Force | Out-Null}
        New-ItemProperty -LiteralPath $source.Path -Name $b.Name -Value ([string]$b.Command) -PropertyType $b.ValueKind -ErrorAction Stop | Out-Null
    }else{
        $original=Join-Path $source.Path $b.Name
        Assert-StartupPlainPath $original
        if(Test-Path -LiteralPath $original){throw 'Файл автозагрузки уже существует. Восстановление отменено без перезаписи.'}
        $saved=Join-Path $backupDir ($Id+'.startup')
        Assert-StartupPlainPath $saved
        if(!(Test-Path -LiteralPath $saved -PathType Leaf) -or (Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ne $b.SHA256){throw 'Резервный файл отсутствует или изменён. Восстановление отменено.'}
        if($source.Admin -and !(Test-StartupMachineAcl ((Get-Item -LiteralPath $saved).GetAccessControl()) -File)){throw 'Небезопасные права сохранённого файла автозагрузки.'}
        if(!(Test-Path -LiteralPath $source.Path)){New-Item -ItemType Directory -Path $source.Path -Force | Out-Null}
        Move-Item -LiteralPath $saved -Destination $original -ErrorAction Stop
    }
    $b.Status='Restored';Write-StartupRecord (Join-Path $backupDir ($Id+'.json')) $b $source.Admin
    return @{Message='Исходная запись автозагрузки восстановлена. Программа не запускалась. Проверяйте её запуск после следующего входа в Windows.'}
}
